using ImGuiNET;
using Retro.Ai;

namespace Retro.App;

/// <summary>The AI chooser and its settings (key, model), shared by the fusion windows and the code view.</summary>
public static class AiPicker
{
    public static readonly string[] Ids = { "sub-claude", "sub-codex", "sub-google", "manual", "agent", "claude", "openai", "gemini", "ollama" };
    public static readonly string[] Names =
    {
        "Claude Code (cuenta de Claude)",
        "Codex CLI (cuenta de ChatGPT)",
        "Antigravity CLI (cuenta de Google)",
        "Copiar y pegar",
        "Carpeta para un agente",
        "Claude API",
        "OpenAI API",
        "Gemini API",
        "Ollama (local)",
    };

    /// <summary>True when the emulator talks to the AI by itself (the others are copy/paste and the agent folder).</summary>
    public static bool IsApi(string provider) => provider is "claude" or "openai" or "gemini" or "ollama" or "sub-claude" or "sub-codex" or "sub-google";

    public static string ShortName(string provider) => provider switch
    {
        "claude" or "sub-claude" => "Claude",
        "sub-codex" => "ChatGPT",
        "sub-google" => "Gemini",
        "openai" => "ChatGPT",
        "gemini" => "Gemini",
        "ollama" => "Ollama",
        "manual" => "copiar y pegar",
        _ => "agente",
    };

    public static void Combo(AiSettings ai, string id = "##ia")
    {
        int provider = Math.Max(0, Array.IndexOf(Ids, ai.Provider));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo(id, ref provider, Names, Names.Length))
        {
            ai.Provider = Ids[provider];
            ai.Save();
        }
    }

    /// <summary>Key and model of the chosen API provider. <paramref name="costNote"/> says what one request is.</summary>
    private static Task<string>? s_test;
    private static string s_testResult = "";
    private static string s_testKey = "";

    public static void Settings(AiSettings ai, string costNote, bool setupLink = true)
    {
        if (CliTools.ById(ai.Provider) is { } tool)
        {
            SubscriptionSettings(ai, tool, setupLink);
            return;
        }
        switch (ai.Provider)
        {
            case "claude":
            {
                string key = ai.ClaudeApiKey;
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Clave de API", ref key, 200, ImGuiInputTextFlags.Password))
                {
                    ai.ClaudeApiKey = key.Trim();
                    ai.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Si está vacía, usa ANTHROPIC_API_KEY del entorno.");
                string model = ai.ClaudeModel;
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Modelo de Claude", ref model, 100))
                {
                    ai.ClaudeModel = model;
                    ai.Save();
                }
                Hint("La API se cobra por separado de tu suscripción. " + costNote);
                break;
            }
            case "openai":
            {
                string key = ai.OpenAiApiKey, model = ai.OpenAiModel;
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Clave de API", ref key, 200, ImGuiInputTextFlags.Password))
                {
                    ai.OpenAiApiKey = key.Trim();
                    ai.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Si está vacía, usa OPENAI_API_KEY del entorno.");
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Modelo de OpenAI", ref model, 100)) { ai.OpenAiModel = model; ai.Save(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Si está vacío, usa {OpenAiProvider.DefaultModel}.");
                Hint("La API se cobra por separado de tu suscripción. " + costNote);
                break;
            }
            case "gemini":
            {
                string key = ai.GeminiApiKey, model = ai.GeminiModel;
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Clave de API", ref key, 200, ImGuiInputTextFlags.Password))
                {
                    ai.GeminiApiKey = key.Trim();
                    ai.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Si está vacía, usa GEMINI_API_KEY del entorno.");
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Modelo de Gemini", ref model, 100)) { ai.GeminiModel = model; ai.Save(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Si está vacío, usa {GeminiProvider.DefaultModel}.");
                Hint("La API tiene sus propios límites y condiciones. " + costNote);
                break;
            }
            case "ollama":
            {
                if (setupLink && ImGui.Button("Cómo conectar Ollama")) AiConnectionWindow.Open(ai);
                DrawModels(ai, null, null);
                if (ImGui.TreeNode("Dirección de Ollama"))
                {
                    string url = ai.OllamaUrl;
                    ImGui.SetNextItemWidth(-1);
                    if (Edit("ollama-url", "http://localhost:11434", ref url, 200)) { ai.OllamaUrl = url.Trim(); ai.Save(); }
                    Hint("Enter para guardar. Por defecto: http://localhost:11434");
                    ImGui.TreePop();
                }
                break;
            }
        }
    }

    /// <summary>The subscription tools: where the tool is, how to install and sign in, and a connection test.</summary>
    private static void SubscriptionSettings(AiSettings ai, CliTool tool, bool setupLink)
    {
        string path = ai.CliPaths.GetValueOrDefault(tool.Id, "");
        string? found = CliTools.Find(tool, path);
        if (found != null)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.5f, 1, 0.5f, 1), $"{tool.Name} instalado");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(found);
        }
        else
        {
            ImGui.TextColored(new System.Numerics.Vector4(1, 0.6f, 0.4f, 1), $"No encontré {tool.Name} en esta PC.");
            if (ImGui.Button("Volver a detectar")) found = CliTools.Find(tool, path);
        }
        if (setupLink && ImGui.Button("Cómo instalar o iniciar sesión")) AiConnectionWindow.Open(ai);
        if (found != null) DrawModels(ai, tool, found);
        if (!ImGui.TreeNode("Conexión avanzada")) return;
        ImGui.SetNextItemWidth(-1);
        if (Edit(tool.Id + "-ruta", "Ruta opcional; Enter para guardar", ref path, 400))
        {
            if (string.IsNullOrWhiteSpace(path)) ai.CliPaths.Remove(tool.Id);
            else ai.CliPaths[tool.Id] = path.Trim().Trim('"');
            ai.Save();
        }
        string model = ai.CliModels.GetValueOrDefault(tool.Id, "");
        ImGui.SetNextItemWidth(-1);
        if (Edit(tool.Id + "-modelo", "Modelo manual opcional; Enter para guardar", ref model, 100))
        {
            if (string.IsNullOrWhiteSpace(model)) ai.CliModels.Remove(tool.Id);
            else ai.CliModels[tool.Id] = model.Trim();
            ai.CliEfforts.Remove(tool.Id);
            ai.Save();
        }
        Hint("Usa tu cuenta y los límites de tu plan.");
        bool testing = s_test is { IsCompleted: false };
        string key = tool.Id + "|" + found + "|" + model + "|" + ai.CliEfforts.GetValueOrDefault(tool.Id);
        ImGui.BeginDisabled(testing || found == null);
        if (ImGui.Button(testing ? "Probando..." : "Probar conexión"))
        {
            var provider = new CliAgentProvider(tool, ai.CliPaths.GetValueOrDefault(tool.Id), TimeSpan.FromMinutes(3), ai.CliModels.GetValueOrDefault(tool.Id), ai.CliEfforts.GetValueOrDefault(tool.Id));
            s_testResult = "";
            s_testKey = key;
            s_test = Task.Run(() => provider.TestAsync(CancellationToken.None));
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Envía un pedido breve para comprobar la sesión. Cuenta para los límites de tu plan.");
        if (s_test is { IsCompleted: true } t)
        {
            s_testResult = t.IsCompletedSuccessfully ? $"Funciona. Respondió: \"{t.Result}\"" : "No funcionó: " + t.Exception!.GetBaseException().Message;
            s_test = null;
        }
        if (s_testResult.Length > 0 && s_testKey == key)
        {
            ImGui.SameLine();
            ImGui.TextWrapped(s_testResult);
        }
        ImGui.TreePop();
    }

    private sealed class CatalogState
    {
        public Task<IReadOnlyList<AvailableModel>>? Task;
        public IReadOnlyList<AvailableModel> Models = Array.Empty<AvailableModel>();
        public string Error = "";
        public DateTime LastSeen;
    }
    private static readonly Dictionary<string, CatalogState> Catalogs = new();

    private static void DrawModels(AiSettings ai, CliTool? tool, string? executable)
    {
        string provider = tool?.Id ?? "ollama";
        string key = provider + "|" + (executable ?? ai.OllamaUrl);
        if (!Catalogs.TryGetValue(key, out var state)) Catalogs[key] = state = new();
        var now = DateTime.UtcNow;
        if (state.Task is not { IsCompleted: false } && (now - state.LastSeen).TotalSeconds > 2) Refresh();
        state.LastSeen = now;
        if (state.Task is { IsCompleted: true } task)
        {
            state.Models = task.IsCompletedSuccessfully ? task.Result : Array.Empty<AvailableModel>();
            state.Error = task.IsCompletedSuccessfully ? "" : task.Exception?.GetBaseException().Message ?? "Consulta cancelada.";
            state.Task = null;
        }
        bool loading = state.Task != null;
        ImGui.BeginDisabled(loading);
        if (ImGui.Button(loading ? "Consultando modelos..." : "Actualizar modelos")) Refresh();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Consulta las opciones de la herramienta sin generar respuestas.");
        if (state.Error.Length > 0) { Hint(state.Error); return; }
        if (state.Task != null) return;
        if (state.Models.Count == 0) { Hint("No hay modelos descargados. Agregá uno en Ollama y actualizá la lista."); return; }

        string selected = tool != null ? ai.CliModels.GetValueOrDefault(provider, "") : ai.OllamaModel;
        var current = state.Models.FirstOrDefault(m => m.Matches(selected));
        string preview = selected.Length == 0 ? "Predeterminado de la herramienta" : current?.Name ?? $"Guardado: {selected}";
        ImGui.TextUnformatted("Modelo:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##modelo-detectado", preview))
        {
            if (tool != null && ImGui.Selectable("Predeterminado de la herramienta", selected.Length == 0)) Select("");
            foreach (var option in state.Models)
                if (ImGui.Selectable(option.Name + "##" + option.Id, option == current)) Select(option.Id);
            ImGui.EndCombo();
        }
        if (selected.Length > 0 && current == null) Hint("El modelo guardado no aparece en esta consulta. Elegí uno de la lista.");
        var active = current ?? (selected.Length == 0 ? state.Models.FirstOrDefault(m => m.IsDefault) : null);
        if (active is { Efforts.Count: > 0 })
        {
            bool variants = active.Efforts.Any(e => e.ModelId != null);
            string effort = variants ? active.Efforts.FirstOrDefault(e => e.ModelId == selected)?.Id ?? ""
                : ai.CliEfforts.GetValueOrDefault(provider, "");
            if (effort.Length > 0 && active.Efforts.All(e => e.Id != effort)) Hint("El esfuerzo guardado ya no aparece disponible. Elegí otro de la lista.");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##esfuerzo", "Esfuerzo: " + (effort.Length == 0 ? "predeterminado" : EffortName(effort))))
            {
                if (!variants && ImGui.Selectable("Predeterminado", effort.Length == 0)) { ai.CliEfforts.Remove(provider); ai.Save(); }
                foreach (var level in active.Efforts)
                    if (ImGui.Selectable(EffortName(level.Id) + "##" + level.Id, level.Id == effort))
                    {
                        if (variants) { ai.CliModels[provider] = level.ModelId!; ai.CliEfforts.Remove(provider); }
                        else ai.CliEfforts[provider] = level.Id;
                        ai.Save();
                    }
                ImGui.EndCombo();
            }
        }
        else Hint("Esta opción usa el esfuerzo propio de la herramienta.");

        void Refresh()
        {
            _ = state.Task?.Exception;
            state.Error = "";
            string address = ai.OllamaUrl;
            state.Task = Task.Run(() => tool != null ? AiModelCatalog.ReadCliAsync(tool, executable!, CancellationToken.None)
                : AiModelCatalog.ReadOllamaAsync(address, CancellationToken.None));
        }
        void Select(string id)
        {
            if (tool != null) { ai.CliModels[provider] = id; ai.CliEfforts.Remove(provider); }
            else ai.OllamaModel = id;
            ai.Save();
        }
    }

    private static string EffortName(string id) => id switch
    {
        "none" => "Sin razonamiento", "minimal" => "Mínimo", "low" => "Bajo", "medium" => "Medio",
        "high" => "Alto", "xhigh" => "Muy alto", "max" => "Máximo", "ultra" => "Ultra", _ => id,
    };

    private static readonly Dictionary<string, string> Edits = new();
    private static bool Edit(string id, string hint, ref string value, uint length)
    {
        string buffer = Edits.GetValueOrDefault(id, value);
        bool entered = ImGui.InputTextWithHint("##" + id, hint, ref buffer, length, ImGuiInputTextFlags.EnterReturnsTrue);
        Edits[id] = buffer;
        if (entered) value = buffer;
        return entered;
    }

    /// <summary>Grey text that wraps to the window width.</summary>
    public static void Hint(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}
