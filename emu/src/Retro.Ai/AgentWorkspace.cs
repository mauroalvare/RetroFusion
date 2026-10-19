using System.Text.Json;
using Retro.Fusion;

namespace Retro.Ai;

/// <summary>
/// Bridge for coding agents that work on files instead of an API (Antigravity, Claude
/// Code, Codex, Gemini CLI): the emulator writes a folder with the task, the game
/// profiles and the schema; the agent writes receta.json; the emulator validates it
/// and, if it has mistakes, writes errores.md for the agent to fix.
/// </summary>
public static class AgentWorkspace
{
    public const string RecipeFile = "receta.json";
    public const string ErrorsFile = "errores.md";

    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RetroEmu", "agente");

    public static string Create(IReadOnlyList<GameProfile> games, string instructions)
    {
        string name = string.Join("-", games.Select(g => g.Id)) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string dir = Path.Combine(Root, name);
        Directory.CreateDirectory(Path.Combine(dir, "perfiles"));
        foreach (var g in games)
            File.WriteAllText(Path.Combine(dir, "perfiles", g.Id + ".json"), g.ToJson());
        File.WriteAllText(Path.Combine(dir, "esquema-receta.json"),
            JsonSerializer.Serialize(FusionDesigner.Schema(games.Select(g => g.Id)), new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(dir, "TAREA.md"), $"""
            # Tarea: diseñar una fusión de juegos para RetroEmu

            {FusionDesigner.Guide}

            {FusionDesigner.UserPrompt(games, instructions)}

            ## Cómo entregar

            1. Escribí la receta en `{RecipeFile}` (en esta carpeta), siguiendo `esquema-receta.json`.
            2. RetroEmu la valida sola. Si aparece `{ErrorsFile}`, corregí lo que dice y volvé a escribir
               `{RecipeFile}`. Cuando la receta es válida, `{ErrorsFile}` se borra y el juego arranca.
            3. Los perfiles completos de cada juego están en `perfiles/`.
            """);
        string pointer = "Leé TAREA.md en esta carpeta y seguí sus instrucciones. Entregá el resultado en receta.json.\n";
        File.WriteAllText(Path.Combine(dir, "AGENTS.md"), pointer);
        File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), pointer);
        File.WriteAllText(Path.Combine(dir, "GEMINI.md"), pointer);
        return dir;
    }

    /// <summary>
    /// Checks the folder: null if there is no recipe yet; otherwise the validated recipe,
    /// or the errors (also written to errores.md for the agent).
    /// </summary>
    public static (FusionRecipe? recipe, List<string> errors)? Poll(string dir, IReadOnlyList<GameProfile> games, Func<string, GameProfile?> findProfile)
    {
        string path = Path.Combine(dir, RecipeFile);
        if (!File.Exists(path))
            return null;
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;   // the agent is still writing it
        }
        var (recipe, errors) = FusionDesigner.Check(text, games, findProfile);
        string errorsPath = Path.Combine(dir, ErrorsFile);
        if (recipe == null)
            File.WriteAllText(errorsPath, "# Errores de la receta\n\n- " + string.Join("\n- ", errors) + "\n");
        else if (File.Exists(errorsPath))
            File.Delete(errorsPath);
        return (recipe, errors);
    }
}

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
            model: string.IsNullOrWhiteSpace(SessionModel) ? CliModels.GetValueOrDefault(Provider) : SessionModel),
        _ => null,   // manual and agent modes are driven by the UI
    };

    private static string KeyOrEnv(string key, string variable) =>
        string.IsNullOrWhiteSpace(key) ? Environment.GetEnvironmentVariable(variable) ?? "" : key;
}
