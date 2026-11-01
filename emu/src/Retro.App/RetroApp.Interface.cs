using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace Retro.App;

public sealed partial class RetroApp
{
    private readonly InterfacePicker _interfacePicker = new();
    private ProgramTerminal? _terminal;
    private bool _terminalOpen, _terminalFocus, _pausedBeforeTerminal;
    private bool _pausedBeforeInterfacePicker;
    private string _terminalInput = "";
    private int _terminalDrawnRevision = -1;
    private ImGuiInputTextCallback? _terminalCallback;
    private bool Hardcore => _prefs.Interface == InterfaceStyle.Hardcore;
    internal Action<string, Vector2, Vector2>? InterfaceControlBounds { get; set; }
    private ProgramTerminal Terminal => _terminal ??= new(BuildTerminalCommands,
        () => Commands.Select(command => command.Id).Concat(Options.Select(option => option.Id)).Concat(InterfaceStyles.All.Select(style => style.Id)));

    private void OpenInterfacePicker() => ShowInterfacePicker(null);
    private void ShowInterfacePicker(string? version)
    {
        if (!_interfacePicker.IsOpen) _pausedBeforeInterfacePicker = _host.Paused;
        _host.Paused = true; _interfacePicker.Open(_prefs.Interface, version);
    }

    /// <summary>Only called after UpdateRunner has validated and acknowledged the actual startup receipt.</summary>
    public bool OfferInterfaceAfterUpdate(string version)
    {
        if (!System.Version.TryParse(version, out _) || _prefs.InterfaceOfferedVersion == version) return false;
        ShowInterfacePicker(version); return true;
    }

    private void FinishInterfaceChoice(InterfaceStyle style, string? version)
    {
        if (version != null) _prefs.InterfaceOfferedVersion = version;
        SetInterface(style);
        if (!Hardcore) _host.Paused = _pausedBeforeInterfacePicker;
    }

    private void SetInterface(InterfaceStyle style)
    {
        bool wasHardcore = Hardcore;
        _prefs.Interface = style; _prefs.Save(); _themeChanged = true;
        if (Hardcore)
        {
            _preferencesOpen = _soundWindowOpen = false;
            OpenTerminal();
        }
        else if (wasHardcore)
        {
            _terminalOpen = false; WorkspaceLayout.FooterHeight = 0;
            CloseEditors(); _home.Close(); _debugMode = false;
            if (!HasGame) _home.Open(); else _host.Paused = _pausedBeforeTerminal;
        }
    }

    private void OpenTerminal()
    {
        if (!_terminalOpen) _pausedBeforeTerminal = _host.Paused;
        CloseEditors(); _home.Close(); _debugMode = false;
        _terminalOpen = true; _terminalFocus = true; _host.Paused = true;
        _ = Terminal;
    }

    private void LeaveTerminal()
    {
        _terminalOpen = false; _terminalFocus = false;
        _host.Continue();
    }

    private void PrepareTerminalLayout()
    {
        WorkspaceLayout.FooterHeight = Hardcore && !_terminalOpen ? ImGui.GetFrameHeightWithSpacing() * 2 + UiTheme.Px(12) : 0;
        if (!Hardcore) return;
        _menuHeight = 0; WorkspaceLayout.MenuHeight = WorkspaceLayout.NavigationHeight = 0;
    }

    private unsafe int TerminalInputCallback(ImGuiInputTextCallbackData* pointer)
    {
        var data = new ImGuiInputTextCallbackDataPtr(pointer);
        string current = data.BufTextLen == 0 ? "" : System.Text.Encoding.UTF8.GetString((byte*)data.Buf, data.BufTextLen);
        string next = data.EventFlag == ImGuiInputTextFlags.CallbackCompletion ? Terminal.Complete(current)
            : Terminal.Recall(data.EventKey == ImGuiKey.UpArrow ? -1 : 1, current);
        data.DeleteChars(0, data.BufTextLen); data.InsertChars(0, next);
        // At large sizes keyboard navigation can move focus after a history arrow. Keep editing the recalled line.
        _terminalFocus = true;
        return 0;
    }

    private unsafe void DrawTerminal()
    {
        if (!Hardcore) return;
        var viewport = ImGui.GetMainViewport();
        float height = _terminalOpen ? viewport.Size.Y - UiTheme.StatusBarHeight : WorkspaceLayout.FooterHeight;
        ImGui.SetNextWindowPos(viewport.Pos + new Vector2(0, viewport.Size.Y - UiTheme.StatusBarHeight - height));
        ImGui.SetNextWindowSize(new(viewport.Size.X, height));
        ImGui.Begin("##program-terminal", WorkspaceLayout.EditorFlags);
        if (_terminalOpen)
        {
            using (UiTheme.Label()) ImGui.TextColored(UiTheme.Accent, "RETROEMU / CONSOLE 01");
            ImGui.Separator();
            float inputHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetTextLineHeightWithSpacing() + UiTheme.Px(6);
            ImGui.BeginChild("terminal-scroll", new(0, -inputHeight));
            bool bottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - ImGui.GetTextLineHeight();
            ImGui.PushTextWrapPos(0);
            foreach (string line in Terminal.Lines) ImGui.TextUnformatted(line);
            ImGui.PopTextWrapPos();
            if (Terminal.Revision != _terminalDrawnRevision && bottom) ImGui.SetScrollHereY(1);
            _terminalDrawnRevision = Terminal.Revision;
            ImGui.EndChild();
        }
        ImGui.TextColored(UiTheme.Accent, Terminal.Prompt + (_terminalOpen ? "" : " · Ctrl+Shift+T abre la consola"));
        if (_terminalFocus && !_interfacePicker.IsOpen) { ImGui.SetKeyboardFocusHere(); _terminalFocus = false; }
        ImGui.SetNextItemWidth(-1);
        _terminalCallback ??= TerminalInputCallback;
        bool submit = ImGui.InputTextWithHint("##terminal-input", "ayuda · juegos · opciones · acciones", ref _terminalInput, 2048,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackHistory | ImGuiInputTextFlags.CallbackCompletion, _terminalCallback);
        InterfaceControlBounds?.Invoke("Entrada terminal", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (submit)
        {
            string command = _terminalInput; _terminalInput = "";
            Terminal.Submit(command); _terminalFocus = _terminalOpen && Hardcore;
            // Enter may switch to a game or instrument. The small command bar must not reclaim its keyboard.
            if (!_terminalFocus) ImGui.SetWindowFocus((string)null!);
        }
        ImGui.End();
    }
}
