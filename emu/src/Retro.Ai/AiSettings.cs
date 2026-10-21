using System.Text.Json;
using Retro.Fusion;

namespace Retro.Ai;

/// <summary>AI connection settings, stored in the user's application data folder.</summary>
public sealed class AiSettings
{
    /// <summary>
    /// claude, openai, gemini (APIs, billed apart), sub-claude, sub-codex, sub-google (the
    /// subscription tools: Claude Code, Codex CLI, Antigravity CLI), ollama, manual
    /// (copy/paste into any chat) or agent (folder for coding agents).
    /// </summary>
    public string Provider { get; set; } = "sub-claude";
    public string ClaudeModel { get; set; } = ClaudeProvider.DefaultModel;
    /// <summary>Empty = use ANTHROPIC_API_KEY or the `ant auth login` profile.</summary>
    public string ClaudeApiKey { get; set; } = "";
    public string OpenAiModel { get; set; } = OpenAiProvider.DefaultModel;
    /// <summary>Empty = use OPENAI_API_KEY.</summary>
    public string OpenAiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = GeminiProvider.DefaultModel;
    /// <summary>Empty = use GEMINI_API_KEY.</summary>
    public string GeminiApiKey { get; set; } = "";
    /// <summary>Where each subscription tool is, when it is not found on PATH (tool id -> path).</summary>
    public Dictionary<string, string> CliPaths { get; set; } = new();
    /// <summary>The model each subscription tool uses (tool id -> model); missing or empty = the tool's own default.</summary>
    public Dictionary<string, string> CliModels { get; set; } = new();
    /// <summary>Per-tool reasoning effort, selected from that model's published capabilities.</summary>
    public Dictionary<string, string> CliEfforts { get; set; } = new();
    /// <summary>A model for this run only (retro-cli --modelo), over <see cref="CliModels"/>; not saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string? SessionModel { get; set; }
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.1";

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "ia.json");

    /// <summary>The saved settings; RETROEMU_MODELO (retro-cli --modelo) picks the subscription tool's model for this run.</summary>
    public static AiSettings Load()
    {
        var s = new AiSettings();
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(FilePath), GameProfile.Json) ?? s;
        }
        catch (JsonException)
        {
        }
        s.SessionModel = Environment.GetEnvironmentVariable("RETROEMU_MODELO");
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, GameProfile.Json));
    }

    public IAiProvider? CreateProvider() => Provider switch
    {
        "claude" => new ClaudeProvider(ClaudeApiKey, ClaudeModel),
        "openai" => new OpenAiProvider(KeyOrEnv(OpenAiApiKey, "OPENAI_API_KEY"), OpenAiModel),
        "gemini" => new GeminiProvider(KeyOrEnv(GeminiApiKey, "GEMINI_API_KEY"), GeminiModel),
        "ollama" => new OllamaProvider(OllamaUrl, OllamaModel),
        "sub-claude" or "sub-codex" or "sub-google" => new CliAgentProvider(CliTools.ById(Provider)!, CliPaths.GetValueOrDefault(Provider),
            model: string.IsNullOrWhiteSpace(SessionModel) ? CliModels.GetValueOrDefault(Provider) : SessionModel,
            effort: string.IsNullOrWhiteSpace(SessionModel) ? CliEfforts.GetValueOrDefault(Provider) : null),
        _ => null,   // manual and agent modes are driven by the UI
    };

    private static string KeyOrEnv(string key, string variable) =>
        string.IsNullOrWhiteSpace(key) ? Environment.GetEnvironmentVariable(variable) ?? "" : key;
}
