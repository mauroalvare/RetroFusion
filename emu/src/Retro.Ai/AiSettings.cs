using System.Text.Json;
using Retro.Fusion;

namespace Retro.Ai;

/// <summary>AI connection settings, stored in the user's application data folder.</summary>
public sealed class AiSettings
{
    private string? _path;
    private bool _unreadable;
    [System.Text.Json.Serialization.JsonIgnore] public string? Problem { get; private set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool CanRecover => _unreadable;
    [System.Text.Json.Serialization.JsonIgnore] public string? RecoveryPath { get; private set; }
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
    public bool Economic { get; set; }
    /// <summary>Thousands of input text characters per operation; zero leaves text unlimited.</summary>
    public int TextLimitThousands { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public int DesignAttempts => Economic ? 2 : 3;
    public AiOperation Operation(IAiProvider provider, int? calls = null, AiOperationStats? previous = null) =>
        new(provider, calls ?? DesignAttempts, Math.Clamp(TextLimitThousands, 0, 10000) * 1000L, previous);

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "ia.json");

    /// <summary>The saved settings; RETROEMU_MODELO (retro-cli --modelo) picks the subscription tool's model for this run.</summary>
    public static AiSettings Load() => Load(FilePath);

    public static AiSettings Load(string path)
    {
        var s = new AiSettings();
        try
        {
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException();
                s = JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(path), GameProfile.Json) ?? throw new InvalidDataException();
                if (!s.Valid()) throw new InvalidDataException();
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            s = new AiSettings { Provider = "manual", _unreadable = true,
                Problem = "No pude leer la configuración de IA. Se conservó el archivo; los cambios quedan solo en esta sesión." };
        }
        s._path = path;
        s.SessionModel = Environment.GetEnvironmentVariable("RETROEMU_MODELO");
        return s;
    }

    private bool Valid() => Provider is "claude" or "openai" or "gemini" or "sub-claude" or "sub-codex" or "sub-google" or "ollama" or "manual" or "agent"
        && TextLimitThousands is >= 0 and <= 10000 && CliPaths != null && CliModels != null && CliEfforts != null
        && !CliPaths.Values.Any(value => value == null) && !CliModels.Values.Any(value => value == null) && !CliEfforts.Values.Any(value => value == null)
        && ClaudeModel != null && OpenAiModel != null && GeminiModel != null && OllamaUrl != null && OllamaModel != null
        && ClaudeApiKey != null && OpenAiApiKey != null && GeminiApiKey != null;

    public void Save() => TrySave();

    private bool TrySave()
    {
        if (!Valid()) { Problem = "La configuración de IA contiene valores inválidos."; return false; }
        if (_unreadable) return false;
        string path = _path ?? FilePath, temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, this, GameProfile.Json); file.Flush(flushToDisk: true);
            }
            if (new FileInfo(temporary).Length > 1024 * 1024) throw new InvalidDataException();
            File.Move(temporary, path, overwrite: true); Problem = null; return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { Problem = "No pude guardar la configuración de IA. Los cambios quedan en esta sesión."; return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Explicit recovery keeps the unreadable configuration before replacing it.</summary>
    public bool Recover()
    {
        if (!_unreadable) return TrySave();
        if (!Valid()) { Problem = "La configuración de IA contiene valores inválidos."; return false; }
        string path = _path ?? FilePath, backup = path + ".respaldo-" + Guid.NewGuid().ToString("N");
        try { File.Copy(path, backup, overwrite: false); RecoveryPath = backup; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Problem = "No pude conservar el archivo anterior. Los cambios siguen solo en esta sesión."; return false; }
        _unreadable = false;
        if (TrySave()) { RecoveryPath = backup; return true; }
        _unreadable = true; return false;
    }

    public IAiProvider? CreateProvider() => CreateProvider(null);

    public IAiProvider? CreateProvider(TimeSpan? timeout) => Provider switch
    {
        "claude" => new ClaudeProvider(ClaudeApiKey, ClaudeModel),
        "openai" => new OpenAiProvider(KeyOrEnv(OpenAiApiKey, "OPENAI_API_KEY"), OpenAiModel),
        "gemini" => new GeminiProvider(KeyOrEnv(GeminiApiKey, "GEMINI_API_KEY"), GeminiModel),
        "ollama" => new OllamaProvider(OllamaUrl, OllamaModel),
        "sub-claude" or "sub-codex" or "sub-google" => new CliAgentProvider(CliTools.ById(Provider)!, CliPaths.GetValueOrDefault(Provider), timeout: timeout,
            model: string.IsNullOrWhiteSpace(SessionModel) ? CliModels.GetValueOrDefault(Provider) : SessionModel,
            effort: string.IsNullOrWhiteSpace(SessionModel) ? CliEfforts.GetValueOrDefault(Provider) : null),
        _ => null,   // manual and agent modes are driven by the UI
    };

    private static string KeyOrEnv(string key, string variable) =>
        string.IsNullOrWhiteSpace(key) ? Environment.GetEnvironmentVariable(variable) ?? "" : key;
}
