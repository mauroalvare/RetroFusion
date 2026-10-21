using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Retro.Ai;

public sealed record ModelEffort(string Id, string? ModelId = null);
public sealed record AvailableModel(string Id, string Name, IReadOnlyList<ModelEffort> Efforts, bool IsDefault = false)
{
    public bool Matches(string id) => Id == id || Efforts.Any(e => e.ModelId == id);
}

/// <summary>Metadata only: never sends a user message, starts a turn or reads credential files.</summary>
public static class AiModelCatalog
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<IReadOnlyList<AvailableModel>> ReadCliAsync(CliTool tool, string executable,
        CancellationToken ct, TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        string work = Path.Combine(Path.GetTempPath(), "RetroEmu-catalogo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            if (tool.Id == "sub-google")
            {
                if (!Path.GetFileNameWithoutExtension(executable).Equals("agy", StringComparison.OrdinalIgnoreCase))
                    throw new AiException("Este Gemini CLI no publica un catálogo compatible. Podés usar su modelo predeterminado o instalar Antigravity CLI.");
                var (code, output, _) = await CliAgentProvider.RunAsync(executable, new[] { "models" }, work, "",
                    timeout ?? TimeSpan.FromSeconds(30), deadline.Token);
                if (code != 0) throw new AiException("No pude consultar los modelos. Abrí agy e iniciá sesión; después pulsá Actualizar modelos.");
                return RequireModels(ParseAntigravity(output));
            }
            string[] args = tool.Id == "sub-codex" ? new[] { "app-server" }
                : new[] { "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
                    "--no-session-persistence", "--setting-sources", "", "--strict-mcp-config", "--tools", "" };
            using var process = new Process { StartInfo = CliAgentProvider.StartInfo(executable, args, work) };
            process.Start();
            var errors = DrainAsync(process.StandardError);
            try
            {
                if (tool.Id == "sub-claude")
                {
                    await Send(new { type = "control_request", request_id = "retroemu-models", request = new { subtype = "initialize" } });
                    var response = await Receive(e => Text(e, "type") == "control_response");
                    var envelope = response.GetProperty("response");
                    if (Text(envelope, "subtype") != "success") throw new AiException("Claude no pudo publicar sus modelos. Revisá la sesión en Claude Code.");
                    return RequireModels(ParseClaude(envelope.GetProperty("response")));
                }
                await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "retroemu", version = typeof(AiModelCatalog).Assembly.GetName().Version?.ToString() ?? "0" } } });
                await Receive(e => e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 1);
                await Send(new { method = "initialized" });
                var models = new List<AvailableModel>();
                string? cursor = null;
                var seen = new HashSet<string>();
                do
                {
                    await Send(new { id = 2, method = "model/list", @params = new { limit = 100, cursor, includeHidden = false } });
                    var response = await Receive(e => e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 2);
                    if (!response.TryGetProperty("result", out var result)) throw new AiException("Codex no pudo publicar sus modelos. Revisá la sesión en Codex CLI.");
                    models.AddRange(ParseCodex(result));
                    cursor = Text(result, "nextCursor");
                    if (cursor != null && !seen.Add(cursor)) throw new AiException("El catálogo de Codex repitió una página. Actualizá la herramienta y reintentá.");
                } while (cursor != null && models.Count < 1000);
                return RequireModels(models.DistinctBy(m => m.Id).ToArray());

                async Task Send(object value)
                {
                    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), deadline.Token);
                    await process.StandardInput.FlushAsync(deadline.Token);
                }
                async Task<JsonElement> Receive(Func<JsonElement, bool> matches)
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        string? line = await process.StandardOutput.ReadLineAsync(deadline.Token);
                        if (line == null) break;
                        if (line.Length > 2_000_000) throw new AiException("El catálogo devolvió una respuesta demasiado grande.");
                        JsonDocument doc;
                        try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }
                        using (doc)
                            if (doc.RootElement.ValueKind == JsonValueKind.Object && matches(doc.RootElement)) return doc.RootElement.Clone();
                    }
                    throw new AiException("La herramienta no devolvió su catálogo. Iniciá sesión o actualizala y volvé a intentar.");
                }
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await errors;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiException("La consulta de modelos tardó demasiado. Revisá la conexión y volvé a intentar.");
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new AiException("No pude leer el catálogo de esta versión. Actualizá la herramienta y volvé a intentar.", e);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static async Task<IReadOnlyList<AvailableModel>> ReadOllamaAsync(string url, CancellationToken ct, HttpClient? http = null)
    {
        try
        {
            using var response = await (http ?? Http).GetAsync(url.TrimEnd('/') + "/api/tags", ct);
            response.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return Items(doc.RootElement, "models").Select(m => Text(m, "name"))
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(1000)
                .Select(id => new AvailableModel(id!, id!, Array.Empty<ModelEffort>())).ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiException("Ollama tardó demasiado en publicar sus modelos. Abrilo y volvé a intentar.");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or UriFormatException)
        {
            throw new AiException("No pude consultar Ollama. Abrilo y revisá su dirección; después pulsá Actualizar modelos.", e);
        }
    }

    private static IReadOnlyList<AvailableModel> RequireModels(IReadOnlyList<AvailableModel> models) => models.Count > 0 ? models
        : throw new AiException("La herramienta no publicó modelos. Iniciá sesión o actualizala y volvé a intentar.");

    private static IReadOnlyList<AvailableModel> ParseClaude(JsonElement root) => Items(root, "models")
        .Where(m => Text(m, "value") is { Length: > 0 })
        .Select(m => new AvailableModel(Text(m, "value")!, Text(m, "displayName") ?? Text(m, "value")!,
            Items(m, "supportedEffortLevels").Where(e => e.ValueKind == JsonValueKind.String).Select(e => new ModelEffort(e.GetString()!)).ToArray(),
            Text(m, "value") == "default")).DistinctBy(m => m.Id).ToArray();

    private static IReadOnlyList<AvailableModel> ParseCodex(JsonElement root) => Items(root, "data")
        .Where(m => Text(m, "model") is { Length: > 0 } && !Flag(m, "hidden"))
        .Select(m => new AvailableModel(Text(m, "model")!, Text(m, "displayName") ?? Text(m, "model")!,
            Items(m, "supportedReasoningEfforts").Select(e => Text(e, "reasoningEffort")).Where(e => e != null)
                .Select(e => new ModelEffort(e!)).ToArray(), Flag(m, "isDefault"))).ToArray();

    private static IReadOnlyList<AvailableModel> ParseAntigravity(string text)
    {
        var models = new List<AvailableModel>();
        foreach (string line in text.Split('\n'))
        {
            string[] parts = line.Trim().Split('\t', 2);
            if (parts.Length != 2 || !Regex.IsMatch(parts[0], "^[A-Za-z0-9][A-Za-z0-9._:/+-]*$")) continue;
            var variant = Regex.Match(parts[1], @"^(.*?) \(([A-Za-z]+)\)$");
            string effort = variant.Success ? variant.Groups[2].Value.ToLowerInvariant() : "";
            bool hasEffort = effort.Length > 0 && parts[0].EndsWith("-" + effort, StringComparison.Ordinal);
            string name = hasEffort ? variant.Groups[1].Value : parts[1];
            string family = hasEffort ? parts[0][..^(effort.Length + 1)] : parts[0];
            int previous = models.FindIndex(m => m.Name == name && m.Id.StartsWith(family + "-", StringComparison.Ordinal));
            var tier = new ModelEffort(effort, parts[0]);
            if (hasEffort && previous >= 0)
                models[previous] = models[previous] with { Efforts = models[previous].Efforts.Append(tier).DistinctBy(e => e.Id).ToArray() };
            else models.Add(new AvailableModel(parts[0], name, hasEffort ? new[] { tier } : Array.Empty<ModelEffort>()));
        }
        return models;
    }

    private static IEnumerable<JsonElement> Items(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : Array.Empty<JsonElement>();
    private static string? Text(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Flag(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer) > 0) { }
    }
}
