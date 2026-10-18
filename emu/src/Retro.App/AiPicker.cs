using ImGuiNET;
using Retro.Ai;

namespace Retro.App;

/// <summary>The AI chooser and its settings (key, model), shared by the fusion windows and the code view.</summary>
public static class AiPicker
{
    public static readonly string[] Ids = { "sub-claude", "sub-codex", "sub-google", "manual", "agent", "claude", "openai", "gemini", "ollama" };
    public static readonly string[] Names =
    {
        "Claude con tu suscripción Pro/Max (Claude Code, sin API)",
        "ChatGPT con tu suscripción Plus/Pro (Codex CLI, sin API)",
        "Gemini con tu cuenta de Google (Antigravity CLI, sin API)",
        "Copiar y pegar en cualquier chat (ChatGPT, Gemini, Claude.ai...)",
        "Carpeta para agentes (Antigravity, Claude Code, Codex, Gemini CLI)",
        "Claude por API (se paga aparte, con clave)",
        "ChatGPT por API (se paga aparte, con clave)",
        "Gemini por API (con clave de Google AI Studio)",
        "Ollama (IA local en tu PC, gratis)",
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
            SubscriptionSettings(ai, tool);
            return;
        }
        switch (ai.Provider)
        {
            case "claude":
            {
                string key = ai.ClaudeApiKey;
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Clave de API (vacía = ANTHROPIC_API_KEY)", ref key, 200, ImGuiInputTextFlags.Password))
                {
                    ai.ClaudeApiKey = key.Trim();
                    ai.Save();
                }
                int model = Math.Max(0, Array.IndexOf(ClaudeModels, ai.ClaudeModel));
                ImGui.SetNextItemWidth(320);
                if (ImGui.Combo("Modelo", ref model, ClaudeModels, ClaudeModels.Length))
                {
                    ai.ClaudeModel = ClaudeModels[model];
                    ai.Save();
                }
                Hint($"{costNote} Se cobra en tu cuenta de Anthropic. Si Claude rechaza un pedido, se reintenta solo con otro modelo.");
                break;
            }
            case "openai":
            {
                string key = ai.OpenAiApiKey, model = ai.OpenAiModel;
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Clave de API (vacía = OPENAI_API_KEY)", ref key, 200, ImGuiInputTextFlags.Password))
                {
                    ai.OpenAiApiKey = key.Trim();
                    ai.Save();
                }
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Modelo de OpenAI", ref model, 100)) { ai.OpenAiModel = model; ai.Save(); }
                Hint($"Vacío = {OpenAiProvider.DefaultModel}. {costNote} Se cobra en tu cuenta de la API de OpenAI, que se paga aparte de ChatGPT Plus (la clave se crea en platform.openai.com).");
                break;
            }
            case "gemini":
            {
                string key = ai.GeminiApiKey, model = ai.GeminiModel;
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Clave de API (vacía = GEMINI_API_KEY)", ref key, 200, ImGuiInputTextFlags.Password))
                {
                    ai.GeminiApiKey = key.Trim();
                    ai.Save();
                }
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Modelo de Gemini", ref model, 100)) { ai.GeminiModel = model; ai.Save(); }
                Hint($"Vacío = {GeminiProvider.DefaultModel}. La clave se crea en aistudio.google.com. Con la capa gratuita no pagás, pero tiene límites por minuto y por día y Google puede usar los pedidos para mejorar sus productos; con facturación activada, {costNote.ToLowerInvariant()}");
                break;
            }
            case "ollama":
            {
                string url = ai.OllamaUrl, model = ai.OllamaModel;
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Dirección de Ollama", ref url, 200)) { ai.OllamaUrl = url; ai.Save(); }
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("Modelo de Ollama", ref model, 100)) { ai.OllamaModel = model; ai.Save(); }
                Hint("Necesita Ollama abierto (ollama serve) y el modelo descargado (ollama pull llama3.1). Es gratis, pero los modelos chicos programan peor.");
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
            Hint(tool.InstallHint);
        }
        ImGui.SetNextItemWidth(320);
        if (ImGui.InputTextWithHint("Ruta (si no lo encuentra solo)", "vacío = buscar solo", ref path, 400))
        {
            if (string.IsNullOrWhiteSpace(path)) ai.CliPaths.Remove(tool.Id);
            else ai.CliPaths[tool.Id] = path.Trim().Trim('"');
            ai.Save();
        }
        Hint($"Usa tu suscripción ({tool.Subscription}): no hace falta clave de API ni se paga aparte; cada pedido cuenta para los límites de uso de tu plan. RetroEmu nunca ve tu contraseña: entrás una sola vez en {tool.Name}.");
        bool testing = s_test is { IsCompleted: false };
        ImGui.BeginDisabled(testing || found == null);
        if (ImGui.Button(testing ? "Probando..." : "Probar conexión"))
        {
            var provider = new CliAgentProvider(tool, ai.CliPaths.GetValueOrDefault(tool.Id), TimeSpan.FromMinutes(3));
            s_testResult = "";
            s_test = Task.Run(() => provider.TestAsync(CancellationToken.None));
        }
        ImGui.EndDisabled();
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
