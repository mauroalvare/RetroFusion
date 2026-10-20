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
    private static readonly string[] ClaudeModels = { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5" };

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

    public static void Settings(AiSettings ai, string costNote)
    {
        if (CliTools.ById(ai.Provider) is { } tool)
        {
            string? found = CliTools.Find(tool, ai.CliPaths.GetValueOrDefault(tool.Id));
            if (found == null) SubscriptionSettings(ai, tool);
            else
            {
                string model = ai.CliModels.GetValueOrDefault(tool.Id, "");
                ImGui.TextColored(new System.Numerics.Vector4(0.5f, 1, 0.5f, 1), $"{tool.Name} encontrado{(model.Length == 0 ? "" : " · " + model)}");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(found);
                ImGui.PushID(tool.Id);
                if (ImGui.TreeNode("Cambiar modelo o conexión"))
                {
                    SubscriptionSettings(ai, tool);
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
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
                int model = Math.Max(0, Array.IndexOf(ClaudeModels, ai.ClaudeModel));
                ImGui.SetNextItemWidth(320);
                if (ImGui.Combo("Modelo", ref model, ClaudeModels, ClaudeModels.Length))
                {
                    ai.ClaudeModel = ClaudeModels[model];
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
                string url = ai.OllamaUrl, model = ai.OllamaModel;
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Dirección de Ollama", ref url, 200)) { ai.OllamaUrl = url; ai.Save(); }
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Modelo de Ollama", ref model, 100)) { ai.OllamaModel = model; ai.Save(); }
                Hint("Ollama debe estar abierto y tener el modelo instalado.");
                break;
            }
        }
    }

    /// <summary>The subscription tools: where the tool is, how to install and sign in, and a connection test.</summary>
    private static void SubscriptionSettings(AiSettings ai, CliTool tool)
    {
        string path = ai.CliPaths.GetValueOrDefault(tool.Id, "");
        string? found = CliTools.Find(tool, path);
        if (found != null)
            ImGui.TextColored(new System.Numerics.Vector4(0.5f, 1, 0.5f, 1), $"{tool.Name} encontrado: {found}");
        else
        {
            ImGui.TextColored(new System.Numerics.Vector4(1, 0.6f, 0.4f, 1), $"No encontré {tool.Name} en esta PC.");
            Hint($"Instalá {tool.Name} e iniciá sesión antes de continuar.");
        }
        ImGui.SetNextItemWidth(320);
        if (ImGui.InputTextWithHint("Ruta", "Detección automática", ref path, 400))
        {
            if (string.IsNullOrWhiteSpace(path)) ai.CliPaths.Remove(tool.Id);
            else ai.CliPaths[tool.Id] = path.Trim().Trim('"');
            ai.Save();
        }
        string model = ai.CliModels.GetValueOrDefault(tool.Id, "");
        ImGui.SetNextItemWidth(320);
        if (ImGui.InputTextWithHint("Modelo", "El de la herramienta", ref model, 100))
        {
            if (string.IsNullOrWhiteSpace(model)) ai.CliModels.Remove(tool.Id);
            else ai.CliModels[tool.Id] = model.Trim();
            ai.Save();
        }
        Hint("Usa tu cuenta y los límites de tu plan.");
        bool testing = s_test is { IsCompleted: false };
        ImGui.BeginDisabled(testing || found == null);
        if (ImGui.Button(testing ? "Probando..." : "Probar conexión"))
        {
            var provider = new CliAgentProvider(tool, ai.CliPaths.GetValueOrDefault(tool.Id), TimeSpan.FromMinutes(3), ai.CliModels.GetValueOrDefault(tool.Id));
            s_testResult = "";
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
        if (s_testResult.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextWrapped(s_testResult);
        }
    }

    /// <summary>Grey text that wraps to the window width.</summary>
    public static void Hint(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}
