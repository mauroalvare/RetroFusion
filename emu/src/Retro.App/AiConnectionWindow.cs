using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Retro.Ai;

namespace Retro.App;

/// <summary>One optional setup page, shared by creation, fusion and the Help menu.</summary>
public static class AiConnectionWindow
{
    private static bool s_open;
    private static int s_selected;
    private static string s_error = "";
    private static Vector2 s_workSize;
    private static readonly string[] Ids = { "sub-claude", "sub-codex", "sub-google", "ollama" };
    private static readonly string[] Names = { "Claude Code", "Codex CLI (ChatGPT)", "Antigravity CLI", "Ollama" };

    public static void Open(AiSettings ai)
    {
        s_selected = Math.Max(0, Array.IndexOf(Ids, ai.Provider));
        s_open = true;
    }

    public static void Draw(AiSettings ai)
    {
        if (!s_open) return;
        var vp = ImGui.GetMainViewport();
        var condition = s_workSize != vp.WorkSize ? ImGuiCond.Always : ImGuiCond.Appearing;
        s_workSize = vp.WorkSize;
        var maximum = vp.WorkSize - new Vector2(16);
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(UiTheme.Px(320, 240), maximum), maximum);
        ImGui.SetNextWindowSize(Vector2.Min(UiTheme.Px(670, 650), maximum), condition);
        ImGui.SetNextWindowPos(vp.GetCenter(), condition, new Vector2(.5f));
        if (!ImGui.Begin("Conectar IA", ref s_open, ImGuiWindowFlags.NoCollapse)) { ImGui.End(); return; }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##herramienta", ref s_selected, Names, Names.Length)) s_error = "";
        string id = Ids[s_selected];
        bool windows = OperatingSystem.IsWindows();
        var (url, install, login, note) = id switch
        {
            "sub-claude" => ("https://code.claude.com/docs/en/quickstart",
                windows ? "irm https://claude.ai/install.ps1 | iex" : "curl -fsSL https://claude.ai/install.sh | bash",
                "claude", "Entrá con tu cuenta de Claude y un plan que incluya Claude Code."),
            "sub-codex" => ("https://developers.openai.com/codex/cli/",
                windows ? "Invoke-WebRequest https://chatgpt.com/codex/install.ps1 -OutFile \"$env:TEMP\\codex-install.ps1\"\npowershell -ExecutionPolicy Bypass -File \"$env:TEMP\\codex-install.ps1\""
                    : "curl -fsSL https://chatgpt.com/codex/install.sh | sh",
                "codex login", "Elegí iniciar sesión con ChatGPT y completá el acceso en el navegador."),
            "sub-google" => ("https://codelabs.developers.google.com/antigravity-cli-hands-on",
                windows ? "irm https://antigravity.google/cli/install.ps1 | iex" : "curl -fsSL https://antigravity.google/cli/install.sh | bash",
                "agy", "Elegí el acceso con Google y seguí los pasos del navegador."),
            _ => ("https://ollama.com/download", "", "ollama", "Para modelos locales no hace falta iniciar sesión. Abrí Ollama y descargá un modelo desde su selector."),
        };
        ImGui.Spacing();
        bool installed = CliTools.ById(id) is { } tool && CliTools.Find(tool, ai.CliPaths.GetValueOrDefault(id)) != null;
        ImGui.TextUnformatted(installed ? "1. Herramienta instalada" : "1. Instalá la herramienta");
        bool expanded = installed && ImGui.TreeNode("Ver pasos de instalación");
        if (!installed || expanded)
        {
        AiPicker.Hint(id == "ollama" ? "Descargá Ollama para tu sistema y seguí su instalador."
            : windows ? "Abrí PowerShell desde Inicio y pegá este comando." : "Abrí una terminal y pegá este comando.");
        if (ImGui.Button(id == "ollama" ? "Abrir descarga oficial" : "Abrir guía oficial"))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { s_error = "No pude abrir el navegador. Copiá el enlace de la guía."; }
        }
        ImGui.SameLine();
        if (ImGui.Button("Copiar enlace")) ImGui.SetClipboardText(url);
        if (install.Length > 0) Command("instalar", install);
        if (expanded) ImGui.TreePop();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted(id == "ollama" ? "2. Abrí Ollama y agregá un modelo" : "2. Iniciá sesión");
        AiPicker.Hint(note);
        if (id != "ollama")
        {
            AiPicker.Hint(windows ? "Abrí una ventana nueva de PowerShell y ejecutá:" : "Abrí una terminal nueva y ejecutá:");
            Command("sesion", login);
            if (id == "sub-google" && Path.GetFileNameWithoutExtension(CliTools.Find(CliTools.Google, ai.CliPaths.GetValueOrDefault(id)) ?? "") == "gemini")
                AiPicker.Hint("Se detectó el anterior Gemini CLI: abrí gemini para iniciar sesión. Para consultar modelos desde RetroEmu, instalá Antigravity CLI.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("3. Elegí el modelo en RetroEmu");
        if (ai.Provider != id)
        {
            if (ImGui.Button("Usar " + Names[s_selected])) { ai.Provider = id; ai.Save(); }
        }
        else AiPicker.Settings(ai, "", setupLink: false);
        AiPicker.Hint("Actualizar modelos vuelve a detectar la herramienta y consulta sus opciones. Si acabás de instalarla y no aparece, reiniciá RetroEmu.");
        ImGui.Separator();
        AiPicker.Hint("RetroEmu no instala estas herramientas ni guarda tu acceso. Crear o fusionar usa los límites de la cuenta elegida.");
        if (s_error.Length > 0) ImGui.TextWrapped(s_error);
        ImGui.End();
    }

    private static void Command(string id, string command)
    {
        ImGui.PushID(id);
        if (ImGui.Button("Copiar comando")) ImGui.SetClipboardText(command);
        ImGui.TextWrapped(command);
        ImGui.PopID();
    }
}
