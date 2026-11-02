using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace Retro.App;

public sealed partial class RetroApp
{
    private readonly InterfacePicker _interfacePicker = new();
    private readonly AppTaskCenter _tasks = new();
    private ProgramTerminal? _terminal;
    private TerminalInput? _terminalInput;
    private TerminalView? _terminalView;
    private bool _terminalOpen, _terminalBarKeyboard, _pausedBeforeTerminal;
    private bool _pausedBeforeInterfacePicker;
    private bool Hardcore => _prefs.Interface == InterfaceStyle.Hardcore;
    internal Action<string, Vector2, Vector2>? InterfaceControlBounds { get; set; }
    /// <summary>The console takes the keyboard only while no dialog is in front of it.</summary>
    private bool ConsoleOwnsKeyboard => (_terminalOpen || _terminalBarKeyboard) && Hardcore && !_interfacePicker.IsOpen && !_updates.Blocking && !_commands.IsOpen
        && _pendingOpen == null && _errorPopup == null && _quitNotice == null && !_browser.IsOpen && !ConfigurationOpen;

    private ProgramTerminal Terminal
    {
        get
        {
            if (_terminal != null) return _terminal;
            string? history = _autotestSeconds >= 0 ? null : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "consola-historial.json");
            _terminal = new(() => TerminalCatalog, history: new CommandHistory(history) { Persist = _prefs.ConsoleHistory });
            _terminal.PromptDetail = ConsolePromptDetail;
            RegisterConsoleCapabilities(_terminal);
            _terminalInput = new(_terminal, () => ImGui.GetClipboardText() ?? "", ImGui.SetClipboardText)
            {
                RunningTasks = () => _tasks.Active.Count,
                StopFollowing = () => { bool following = _followed != null; _followed = null; return following; },
            };
            _terminalView = new(_terminal, _terminalInput, () => new(_prefs.ConsoleCursor, _prefs.ConsoleBlink, _prefs.ConsoleScanlines));
            return _terminal;
        }
    }

    /// <summary>[nes pausa] (2): the machine, whether it runs and how many tasks are alive.</summary>
    private string ConsolePromptDetail()
    {
        string machine = _host.Machine == null || !HasGame ? "" : "[" + _host.Machine.Id + (_host.Paused ? " pausa" : "") + "]";
        int tasks = _tasks.Active.Count;
        return machine + (tasks > 0 ? (machine.Length > 0 ? " " : "") + "(" + tasks + ")" : "");
    }

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
        if (_host.Machine != null) _input.ReleaseAll(_host.Machine);
        if (Hardcore)
        {
            _preferencesOpen = _soundWindowOpen = false;
            OpenTerminal();
        }
        else if (wasHardcore)
        {
            _terminalOpen = _terminalBarKeyboard = false; WorkspaceLayout.FooterHeight = 0;
            CloseEditors(); _home.Close(); _debugMode = false;
            if (!HasGame) _home.Open(); else _host.Paused = _pausedBeforeTerminal;
        }
    }

    private void OpenTerminal()
    {
        if (!_terminalOpen) _pausedBeforeTerminal = _host.Paused;
        CloseEditors(); _home.Close(); _debugMode = false;
        _terminalOpen = true; _terminalBarKeyboard = false; _host.Paused = true;
        _ = Terminal;
    }

    /// <summary>Hands the keyboard to the game. The Enter that ran the order must not arrive as a key of the machine.</summary>
    private void LeaveTerminal()
    {
        ReleaseConsoleKeyboard();
        _input.HoldKeyboardUntilReleased(KeyboardKey.Enter, KeyboardKey.KpEnter);
        _host.Continue();
    }

    /// <summary>A panel or the game takes over: the console stops reading keys and the bar does not take them back.</summary>
    private void ReleaseConsoleKeyboard()
    {
        _terminalOpen = _terminalBarKeyboard = false;
        ImGui.SetWindowFocus((string)null!);
    }

    private void PrepareTerminalLayout()
    {
        // The one-row bar leaves the rest of the window to the game.
        WorkspaceLayout.FooterHeight = Hardcore && !_terminalOpen ? MathF.Ceiling(UiTheme.Px(16) * 1.2f) + UiTheme.Px(8) : 0;
        if (!Hardcore) return;
        _menuHeight = 0; WorkspaceLayout.MenuHeight = WorkspaceLayout.NavigationHeight = 0;
    }

    private void SyncTasks()
    {
        _tasks.Track("biblioteca", _library.Scanning ? new("búsqueda de juegos", Path.GetFileName(Path.TrimEndingDirectorySeparator(_library.Folder)), "Revisando la carpeta") : null);
        _tasks.Track("identificacion", _gameMetadata.Pending > 0 ? new("identificación", "catálogo en línea", _gameMetadata.Status, AppTaskState.WaitingProvider,
            _gameMetadata.Asked, _gameMetadata.Asked + _gameMetadata.Pending, "juegos") : null);
        _tasks.Track("candidatos", _gameMetadata.SearchingCandidates ? new("candidatos", "catálogo en línea", _gameMetadata.CandidateStatus, AppTaskState.WaitingProvider, Cancel: _gameMetadata.StopCandidates) : null);
        _tasks.Track("estudio", _studio.Work);
        _tasks.Track("creador", _creator.Work);
        _tasks.Track("analisis", _code.Work);
        _tasks.Poll();
        ReportTasksToConsole();
        if (_awaitingStop && _host.Paused && _terminal != null)
        {
            _awaitingStop = false;
            if (_host.Machine is { } machine)
                _terminal.Write($"Detenida en PC = {machine.Cpu.ProgramCounter:X4}" + (_host.LastStopReason is { Length: > 0 } reason ? " · " + reason : ""), TerminalKind.Detail);
        }
    }

    private void DrawTerminal()
    {
        if (!Hardcore) return;
        _ = Terminal;
        var viewport = ImGui.GetMainViewport();
        float height = _terminalOpen ? viewport.Size.Y - UiTheme.StatusBarHeight : WorkspaceLayout.FooterHeight;
        ImGui.SetNextWindowPos(viewport.Pos + new Vector2(0, viewport.Size.Y - UiTheme.StatusBarHeight - height));
        ImGui.SetNextWindowSize(new(viewport.Size.X, height));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, _terminalOpen ? Vector2.Zero : UiTheme.Px(8, 4));
        ImGui.Begin("##program-terminal", WorkspaceLayout.EditorFlags | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        bool keyboard = ConsoleOwnsKeyboard;
        if (_terminalOpen) _terminalView!.Draw(keyboard);
        else
        {
            _terminalView!.DrawBar(keyboard);
            if (ImGui.IsItemClicked()) _terminalBarKeyboard = true;
            if (keyboard && (ImGui.IsKeyPressed(ImGuiKey.Escape, false) || _terminalInput!.Submitted)) ReleaseConsoleKeyboard();
            // A click elsewhere gives the keyboard back to the game.
            else if (keyboard && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsItemHovered()) ReleaseConsoleKeyboard();
        }
        _terminalInput!.Submitted = false;
        InterfaceControlBounds?.Invoke("Entrada terminal", _terminalView.InputBounds.Min, _terminalView.InputBounds.Max);
        ImGui.End();
        ImGui.PopStyleVar();
    }

    /// <summary>What the status bar says in the console: a notice, the syntax being typed, or the result of an order given from the bar.</summary>
    private string ConsoleStatus()
    {
        if (_terminalInput == null) return "";
        if (_terminalInput.Notice.Length > 0) return _terminalInput.Notice;
        if (_terminalInput.Hint is { } hint) return hint;
        if (!_terminalOpen)
        {
            var output = Terminal.Output;
            if (output.RowCount > 0) return output.RowText(output.Row(output.RowCount - 1));
        }
        return _terminalOpen ? "Tab completa · ayuda explica · como <tema> busca" : "";
    }
}
