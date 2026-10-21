using System.Text;
using System.Text.Json;

namespace Retro.Ai;

/// <summary>Counts reported by a provider for one call, never an account quota or a price.</summary>
public sealed record AiUsage(long InputTokens, long OutputTokens, long? CachedInputTokens = null)
{
    private static long? Number(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n) && n >= 0 ? n : null;
    private static JsonElement Child(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) ? value : default;
    private static AiUsage? Pair(JsonElement root, string input, string output, long? cached = null) =>
        Number(root, input) is long i && Number(root, output) is long o ? new(i, o, cached) : null;

    public static AiUsage? Responses(JsonElement root)
    {
        var usage = Child(root, "usage");
        return Pair(usage, "input_tokens", "output_tokens", Number(Child(usage, "input_tokens_details"), "cached_tokens"));
    }
    public static AiUsage? Claude(JsonElement root)
    {
        var usage = Child(root, "usage");
        if (Pair(usage, "input_tokens", "output_tokens") is not { } pair) return null;
        long read = Number(usage, "cache_read_input_tokens") ?? 0, written = Number(usage, "cache_creation_input_tokens") ?? 0;
        return pair with { InputTokens = pair.InputTokens + read + written, CachedInputTokens = Number(usage, "cache_read_input_tokens") };
    }
    public static AiUsage? Ollama(JsonElement root) => Pair(root, "prompt_eval_count", "eval_count");
    public static AiUsage? Gemini(JsonElement root)
    {
        var usage = Child(root, "usageMetadata");
        return Pair(usage, "promptTokenCount", "candidatesTokenCount", Number(usage, "cachedContentTokenCount")) is { } pair
            ? pair with { InputTokens = pair.InputTokens + (Number(usage, "toolUsePromptTokenCount") ?? 0),
                OutputTokens = pair.OutputTokens + (Number(usage, "thoughtsTokenCount") ?? 0) } : null;
    }
    public static AiUsage? Cli(string tool, string stdout)
    {
        try
        {
            if (tool == "sub-codex")
            {
                AiUsage? total = null;
                foreach (string line in stdout.Split('\n'))
                {
                    if (!line.TrimStart().StartsWith('{')) continue;
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (Child(root, "type").ValueKind != JsonValueKind.String || Child(root, "type").GetString() != "turn.completed") continue;
                    var usage = Child(root, "usage");
                    if (Pair(usage, "input_tokens", "output_tokens", Number(usage, "cached_input_tokens")) is not { } current) continue;
                    total = total == null ? current : new(total.InputTokens + current.InputTokens, total.OutputTokens + current.OutputTokens,
                        total.CachedInputTokens is long a && current.CachedInputTokens is long b ? a + b : null);
                }
                return total;
            }
            int start = stdout.IndexOf('{');
            if (start < 0) return null;
            using var json = JsonDocument.Parse(stdout[start..]);
            if (tool == "sub-claude") return Claude(json.RootElement);
            var models = Child(Child(json.RootElement, "stats"), "models");
            if (tool == "sub-google" && models.ValueKind == JsonValueKind.Object)
            {
                AiUsage? total = null;
                foreach (var model in models.EnumerateObject())
                {
                    var tokens = Child(model.Value, "tokens");
                    if (Pair(tokens, "prompt", "candidates", Number(tokens, "cached")) is not { } current) return null;
                    current = current with { InputTokens = current.InputTokens + (Number(tokens, "tool") ?? 0),
                        OutputTokens = current.OutputTokens + (Number(tokens, "thoughts") ?? 0) };
                    total = total == null ? current : new(total.InputTokens + current.InputTokens, total.OutputTokens + current.OutputTokens,
                        total.CachedInputTokens is long a && current.CachedInputTokens is long b ? a + b : null);
                }
                return total;
            }
            // Versions without documented token metadata remain unknown.
            return null;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Local text heuristic; images, output and a CLI's own context are deliberately separate.</summary>
public sealed record AiEstimate(long Characters, int Images, long LowTokens, long HighTokens)
{
    public static AiEstimate For(AiRequest request)
    {
        var texts = request.Turns.Select(t => t.Text).Append(request.System).Append(request.Schema?.GetRawText() ?? "");
        long chars = 0, bytes = 0;
        foreach (string text in texts) { chars += text.Length; bytes += Encoding.UTF8.GetByteCount(text); }
        return new(chars, request.Images?.Count ?? 0, (chars + 4) / 5, (bytes + 1) / 2);
    }
}

public sealed record AiOperationStats(int Calls, int CallsWithUsage, long TextCharacters, long InputTokens, long OutputTokens, long? CachedInputTokens);

/// <summary>One user operation: limits whole requests, never truncates instructions or source.</summary>
public sealed class AiOperation(IAiProvider provider, int maxCalls, long maxTextCharacters = 0, AiOperationStats? previous = null) : IAiProvider
{
    private readonly object _gate = new();
    private AiOperationStats _stats = previous ?? new(0, 0, 0, 0, 0, null);
    public IAiProvider Provider => provider;
    public string Name => provider.Name;
    public bool SupportsImages => provider.SupportsImages;
    public AiUsage? LastUsage => provider.LastUsage;
    public AiOperationStats Stats { get { lock (_gate) return _stats; } }
    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var estimate = AiEstimate.For(request);
        lock (_gate)
        {
            if (_stats.Calls - (previous?.Calls ?? 0) >= maxCalls) throw new AiException("Se alcanzó el límite de pedidos de este trabajo. Podés continuar con el borrador.");
            if (maxTextCharacters > 0 && _stats.TextCharacters + estimate.Characters > maxTextCharacters)
                throw new AiException("Se alcanzó el límite de texto de este trabajo. No se envió este pedido. Ampliá el límite para continuar.");
            _stats = _stats with { Calls = _stats.Calls + 1, TextCharacters = _stats.TextCharacters + estimate.Characters };
        }
        try { return await provider.CompleteAsync(request, ct); }
        finally
        {
            if (provider.LastUsage is { } usage)
                lock (_gate)
                {
                    _stats = _stats with { CallsWithUsage = _stats.CallsWithUsage + 1,
                        InputTokens = _stats.InputTokens + usage.InputTokens, OutputTokens = _stats.OutputTokens + usage.OutputTokens,
                        CachedInputTokens = usage.CachedInputTokens is long cached && (_stats.CallsWithUsage == 0 || _stats.CachedInputTokens != null)
                            ? (_stats.CachedInputTokens ?? 0) + cached : null };
                }
        }
    }
}
