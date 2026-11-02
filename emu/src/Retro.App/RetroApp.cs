using System.Globalization;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Retro.Core;
using Retro.Machines;

namespace Retro.App;

/// <summary>Main window: play mode (just the screen) and debugger mode (panels).</summary>
public sealed partial class RetroApp
{
    private readonly EmulatorHost _host = new();
    private readonly InputMapper _input = new();
    private readonly string _romDir;
    private readonly FileBrowser _browser;
    private readonly Retro.Ai.AiSettings _ai;
    private readonly StudioWindow _studio;
    private readonly CreatorWindow _creator;
    private readonly GameLibrary _library;
    private readonly GameMetadataService _gameMetadata;
    private int _libraryRevision;
    private readonly HomeScreen _home;
    private readonly UpdateWindow _updates;
    private bool _pausedBeforeLibrary;
    private string? _autoStudio;
    private readonly CodeWindow _code;
    private bool _wasFusion;
    private Texture2D _screenTex;
    private int _texW, _texH;
    private int[] _rgba = Array.Empty<int>();
    private AudioStream _stream;
    private readonly VideoUploadCache _videoCache = new();
    private bool _debugMode;
    private bool _followPc = true;
    private string _disasmAddr = "0000";
    private string _memAddr = "4000";
    private uint _memStart = 0x4000;
    private string _testPath = "";
    private string _testAddr = "8000";
    private string _testHex = "3E 02 D3 FE 3C 18 FB";
    private bool _testSetPc = true;
    private string? _errorPopup;
    private sealed record OpenRequest(string Path, IReadOnlyList<MediaFile> Files, List<MachineCatalog.Candidate> Versions);
    private OpenRequest? _pendingOpen;
    private bool _pausedBeforeChoice;
    private List<MachineCatalog.Candidate> _versions = new();   // versions of the last opened game
    private readonly AppPreferences _prefs;
    private bool _themeChanged;
    private bool _audioDisabled;
    private float _menuHeight = 28;
    private bool _arrangeDebugger;
    private string? _currentMedia;   // the original file of the game being played, for its library thumbnail
    private readonly string _captureFolder;
    private string? _quitNotice;
    private readonly FocusPause _focusPause = new();
    private readonly int _monitorRate;
    private int _targetFps;
    private readonly Dictionary<string, string> _regEdit = new();
    private string? _editMemAddr;
    private string _editMemValue = "";
    private string? _lastMachineId;
    private bool _screenFocused;
    public bool QuitRequested { get; private set; }

    // --autotest <seconds> <png>: run, save a screenshot of the whole window, quit.
    private double _autotestSeconds = -1;
    // --preparar <file>: prepares that game for the fusions from the studio
    private string? _autoPrepare;
    private string? _autotestShot;
    private string? _autotestReport;
    private double _elapsed;
    private int _windowFrames;
    private string? _autotestResearchAction;
    private bool _autotestGeometry;

    public RetroApp(string[] args)
    {
        _audioDisabled = !StartupRequirements.WantsAudio(args);
        _ai = args.Contains("--autotest") || args.Contains("--no-ai")
            ? new Retro.Ai.AiSettings { Provider = "manual", ProvidersDisabled = true } : Retro.Ai.AiSettings.Load();
        _input.KeySink = _host.SetInput;
        _host.InputReset += _input.ForgetKeys;
        _prefs = new AppPreferences(persist: !args.Contains("--autotest"));
        int languageArg = Array.IndexOf(args, "--language");
        if (languageArg >= 0 && languageArg + 1 < args.Length && args[languageArg + 1] is "es" or "en") _prefs.Language = args[languageArg + 1];
        UiText.Language = _prefs.Language;
        _gameMetadata = new(new GameIdentificationCache(persist: !args.Contains("--autotest")));
        _gameMetadata.CandidatesReady += ReportCandidates;
        int interfaceArg = Array.IndexOf(args, "--interface");
        if (interfaceArg >= 0 && interfaceArg + 1 < args.Length && InterfaceStyles.TryParse(args[interfaceArg + 1], out var appearance)) _prefs.Interface = appearance;
        // --text-size 150 and --high-contrast change this session, as --mute does with the sound.
        int textArg = Array.IndexOf(args, "--text-size");
        if (textArg >= 0 && textArg + 1 < args.Length && int.TryParse(args[textArg + 1], out int text) && AppPreferences.TextSizes.Contains(text))
            _prefs.TextPercent = text;
        if (args.Contains("--high-contrast")) _prefs.HighContrast = true;
        ConfigureStyle();
        _input.Joystick = _prefs.SpectrumJoystick; _input.C64Port = _prefs.C64Port;
        _host.TurboWhileLoading = _prefs.TurboWhileLoading;
        if (_prefs.Fullscreen && !args.Contains("--hidden") && !args.Contains("--autotest") && !Raylib.IsWindowFullscreen())
            Raylib.ToggleFullscreen();
        _sound = new SoundSettings(persist: !args.Contains("--autotest"));
        _stateSlots = new StateSlotStore(args.Contains("--autotest") ? Path.Combine(Environment.CurrentDirectory, "out", "autotest-state-slots") : null);
        // the system firmware: a "firmware" folder (release packages) or a "roms" folder that
        // has the Spectrum ROM in it (so a games folder called "ROMs" is never mistaken for it)
        _romDir = FindFolder("firmware") ?? FindFolderWith("roms", "48.rom") ?? Path.Combine(AppContext.BaseDirectory, "firmware");
        _library = new GameLibrary { AutoOrganize = !args.Contains("--no-organize"), InUse = _host.UsesMedia };
        int gamesArgument = Array.IndexOf(args, "--games-folder");
        string games = gamesArgument >= 0 && gamesArgument + 1 < args.Length && Directory.Exists(args[gamesArgument + 1])
            ? Path.GetFullPath(args[gamesArgument + 1])
            : _prefs.LibraryFolder is { } chosen && Directory.Exists(chosen) ? chosen : FindGamesFolder() ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _library.Rescan(games);
        Retro.Studio.StudioLibrary.ExportBuiltIns();
        _browser = new FileBrowser(_prefs.LastFolder is { } last && Directory.Exists(last) ? last : games);
        _captureFolder = args.Contains("--autotest") ? Path.Combine(Environment.CurrentDirectory, "out", "autotest-capturas") : ScreenCapture.DefaultFolder;
        _studio = new StudioWindow(_host, _romDir, games, _ai, playOriginal:OpenFile, analyzeOriginal:OpenOriginalAnalysis);
        _code = new CodeWindow(_host, _ai, ShowInDebugger, EditCurrentStudioGame,
            new ModificationHistory(persist: !args.Contains("--autotest")),firmwareDirectory:_romDir);
        _creator = new CreatorWindow(_romDir, _ai, OpenFile);
        _home = new HomeScreen(_library, OpenFile, PlayFusion, _browser.Open,
            visible =>
            {
                if (visible) { _pausedBeforeLibrary = _host.Paused; _host.Paused = true; }
                else _host.Paused = _pausedBeforeLibrary;
            }, () => _host.LoadedMedia != null || _host.Fusion != null,
            args.Contains("--autotest") ? new LibraryActivity(persist: false) : null,
            new HomeActions(OpenCreator, OpenFusionStudio, _commands.Open, () => _preferencesOpen = true, () => _prefs.OnlineGameInfo, () => _browser.OpenFolder(UseLibraryFolder)),
            args.Contains("--autotest") ? new LibraryMetadata(persist: false) : null, identification: _gameMetadata);
        _studio.GameStarted += game => _home.RecordFusion(game);
        _monitorRate = Raylib.GetMonitorRefreshRate(Raylib.GetCurrentMonitor());
        _targetFps = FramePacing.TargetFps(_monitorRate, saving: false, emulating: true, focused: true, minimized: false);
        Raylib.SetTargetFPS(_targetFps);
        // The audio device pulls samples through a callback on its own thread, so the
        // emulator can pace itself on the real consumption rate.
        s_queue = _host.Audio;
        s_forceMute = args.Contains("--mute"); s_mute = _sound.Muted;
        s_volume = _sound.VolumePercent / 100f; s_output.Reset();
        StartAudioStream();
        try
        {
            _host.Start(MachineCatalog.Get("zx48"), _romDir);
        }
        catch (Exception e)
        {
            _errorPopup = e.Message + UiText.Get("\n\nCopiá la ROM a la carpeta firmware/ junto al programa.");
        }
        bool openedFile = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--debug")
                _debugMode = true;
            else if (args[i] == "--autotest" && i + 2 < args.Length)
            {
                _autotestSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                _autotestShot = args[++i];
            }
            else if (args[i] == "--autotest-report" && args.Contains("--autotest") && i + 1 < args.Length) _autotestReport = args[++i];
            else if (args[i] == "--studio" && i + 1 < args.Length)
                _autoStudio = args[++i];
            else if (args[i] == "--games-folder" && i + 1 < args.Length) i++;
            else if (args[i] == "--input-check" && args.Contains("--autotest") && args.Contains("--hidden"))
            { OpenHub(HubPage.Help); _inputCheckOpen = true; }
            else if (args[i] == "--preparar" && i + 1 < args.Length)
                _autoPrepare = args[++i];
            else if (args[i] == "--crear-window")
                OpenCreator();
            else if (args[i] == "--states-window")
                OpenStateWindow();
            else if (args[i] == "--studio-window")
                OpenFusionStudio();
            else if (args[i] == "--code")
                _code.Open();
            else if (args[i] == "--mods-window")
                _code.OpenModifications();
            else if (args[i] == "--mute")
                _sound.Muted = true;
            else if (args[i] == "--sound-window")
                _soundWindowOpen = true;
            else if (args[i] == "--preferences-window")
                _preferencesOpen = true;
            else if (args[i] == "--actions-window" && args.Contains("--autotest"))
                _commands.Open();
            else if (args[i] == "--create-hub" && args.Contains("--autotest")) OpenHub(HubPage.Create);
            else if (args[i] == "--tools-hub" && args.Contains("--autotest")) OpenHub(HubPage.Tools);
            else if (args[i] == "--help-hub" && args.Contains("--autotest")) OpenHub(HubPage.Help);
            else if (args[i] == "--research-window" && args.Contains("--autotest") && i+1<args.Length) { OpenFusionStudio(); _studio.ShowResearch(args[++i]); }
            else if (args[i] == "--research-action" && args.Contains("--autotest") && args.Contains("--hidden") && i+1<args.Length) _autotestResearchAction=args[++i];
            else if(args[i]=="--auto-analysis-window" && args.Contains("--autotest")) {CloseEditors();_home.Close();_code.OpenAutomaticAnalysis();}
            else if (args[i] is "--auto-analysis-action" or "--auto-analysis-section" && args.Contains("--autotest") && i + 1 < args.Length)
                i++;
            else if (args[i] is "--text-size" or "--interface" or "--language")
                i++;
            else if (args[i] != "--hidden" && File.Exists(args[i]))
            {
                OpenFile(args[i]);
                openedFile = true;
            }
        }
        if (args.Contains("--autotest") && args.Contains("--hidden") && args.Contains("--auto-analysis-window"))
        {
            string AnalysisArgument(string flag)
            { int index = Array.IndexOf(args, flag); return index >= 0 && index + 1 < args.Length ? args[index + 1] : ""; }
            _code.ConfigureAnalysisAutotest(AnalysisArgument("--auto-analysis-action"), AnalysisArgument("--auto-analysis-section"), enabled: true);
        }
        if (!openedFile && !args.Any(a => a is "--studio" or "--preparar" or "--crear-window" or "--studio-window" or "--code" or "--mods-window" or "--states-window" or "--sound-window" or "--preferences-window" or "--debug" or "--create-hub" or "--tools-hub" or "--help-hub" or "--research-window" or "--auto-analysis-window"))
            _home.Open();
        _updates = new UpdateWindow(args);
        _updates.ControlBounds = (name, min, max) => InterfaceControlBounds?.Invoke(name, min, max);
        if (Hardcore) OpenTerminal();
        if (args.Contains("--interface-picker")) OpenInterfacePicker();
    }

    /// <summary>Without a sound device everything else keeps working: games run silent, at their real speed.</summary>
    private unsafe void StartAudioStream()
    {
        if (_audioDisabled || !Raylib.IsAudioDeviceReady())
        {
            _stream = default; _host.SoundDeviceMissing = true; _audioRetried = false;
            return;
        }
        Raylib.SetAudioStreamBufferSizeDefault(512);
        _stream = Raylib.LoadAudioStream(44100, 16, 1);
        if (Raylib.IsAudioStreamValid(_stream))
        {
            Raylib.SetAudioStreamCallback(_stream, &AudioCallback);
            Raylib.PlayAudioStream(_stream);
        }
        _host.SoundDeviceMissing = !AudioReady;
        _audioRetried = false;
    }

    private bool AudioReady => !_audioDisabled && Raylib.IsAudioDeviceReady() && Raylib.IsAudioStreamValid(_stream);
    private bool _audioRetried;

    /// <summary>A device plugged in later is picked up without restarting the program.</summary>
    private void RetryAudio()
    {
        if (AudioReady) return;
        _audioDisabled = false;
        if (!Raylib.IsAudioDeviceReady()) Raylib.InitAudioDevice();
        if (Raylib.IsAudioDeviceReady()) StartAudioStream(); else _audioRetried = true;
    }

    /// <summary>The user's game folder ("ROMs", exact case: "roms" holds the firmware).</summary>
    private static string? FindGamesFolder() => PlayerFolders.Find("ROMs");

    private void ConfigureStyle() => UiTheme.Apply(_prefs.TextScale, _prefs.HighContrast, _prefs.Interface, _prefs.ConsolePalette, _prefs.ConsoleColors);

    private void ShowInDebugger(uint address)
    {
        CloseEditors(); _home.Close();
        _followPc = false;
        _disasmAddr = address.ToString("X4");
        _debugMode = true;
    }

    private static string? FindFolderWith(string name, string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, name);
            if (File.Exists(Path.Combine(p, file)))
                return p;
        }
        return null;
    }

    private static string? FindFolder(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, name);
            if (Directory.Exists(p))
                return p;
            dir = dir.Parent;
        }
        return null;
    }

    private void EnsureScreenTexture(int w, int h)
    {
        if (w == _texW && h == _texH)
            return;
        if (_texW != 0)
            Raylib.UnloadTexture(_screenTex);
        var img = Raylib.GenImageColor(w, h, Color.Black);
        _screenTex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_screenTex, TextureFilter.Point);
        _rgba = new int[w * h];
        _texW = w;
        _texH = h;
        _videoCache.Invalidate();
    }

    public void Shutdown()
    {
        _updates.Dispose();
        foreach (var task in _tasks.Active) task.Cancel();
        _terminal?.History.Save();
        var pending = Task.WhenAll(_studio.CancelWork(), _creator.CancelWork(), _code.CancelWork(), AiPicker.CancelWork());
        try { pending.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { /* Cancellation and provider errors are already retained by the jobs. */ }
        _code.Dispose();
        _library.Dispose();
        _gameMetadata.Dispose();
        _home.Dispose();
        if (_texW != 0)
            Raylib.UnloadTexture(_screenTex);
        s_queue = null;
        if (Raylib.IsAudioStreamValid(_stream)) Raylib.UnloadAudioStream(_stream);
    }

    private bool CaptureGuiKeyboard()
    {
        var io = ImGui.GetIO();
        // The focused debugger screen is itself an ImGui window, but owns game input.
        return io.WantTextInput || ConsoleOwnsKeyboard || InterfaceWorkspace || ConfigurationOpen || _interfacePicker.IsOpen || _commands.IsOpen || _pendingOpen != null || _browser.IsOpen
            || (io.WantCaptureKeyboard && !(_debugMode && _screenFocused));
    }

    // ------------------------------------------------------------------ frame
    public void Frame(float dt)
    {
        _updates.Poll();
        if (_updates.RestartRequested) QuitRequested = true;
        if (!_updates.Blocking && _pendingOpen == null && !_browser.IsOpen) HandleDroppedFiles();
        _library.Poll();
        WatchAudioDevice();
        _gameMetadata.Poll(_library.Entries, _library.Folder, _library.Revision, _prefs.OnlineGameInfo && _autotestSeconds < 0);
        SyncTasks();
        if (_libraryRevision != _library.Revision)
        {
            _libraryRevision = _library.Revision;
            _studio.NotifyLibraryChanged(_library.Folder);
        }
        bool imguiKeyboard = CaptureGuiKeyboard();
        if (!_updates.Blocking && !_interfacePicker.IsOpen && _pendingOpen == null && !_browser.IsOpen) HandleHotkeys(ImGui.GetIO().WantTextInput);
        if (!_updates.Blocking && !_interfacePicker.IsOpen && _pendingOpen == null && !_browser.IsOpen) HandleGamepadShortcut();
        imguiKeyboard = CaptureGuiKeyboard();
        _code.Update();
        _studio.Update(); _creator.Update();
        if (_home.IsOpen) _host.Paused = true;
        var m = _host.Machine;
        PauseInBackground(m);
        if (m != null && !_updates.Blocking)
        {
            _input.InterfaceActive=imguiKeyboard || _commands.IsOpen || _host.Paused;
            if (m.Id != _lastMachineId)
                OnMachineChanged(m);
            bool keyboardToMachine = !imguiKeyboard && !_commands.IsOpen && (!_debugMode || _screenFocused);
            if (_host.Fusion is { } fusion)
            {
                if (!_wasFusion)
                    _input.ReleaseAll(m);
                _input.UpdateFusion(fusion, keyboardToMachine && !_host.Paused);
            }
            else
                _input.Update(m, keyboardToMachine && !_host.Paused);
            _wasFusion = _host.Fusion != null;
            _host.AudioBufferFrames = _sound.BufferFrames;
            _host.Update(dt);
            UploadScreen();
        }
        s_mute = _sound.Muted || InterfaceWorkspace || _interfacePicker.IsOpen || _host.IsFastForwarding || _host.Paused || _updates.Blocking
            || (_sound.MuteInBackground && !Raylib.IsWindowFocused());
        s_volume = _sound.VolumePercent / 100f;
        PaceFrames(m);
        if (_autoStudio != null)
        {
            PlayFusion(_autoStudio);
            _autoStudio = null;
        }
        if (_autoPrepare != null)
        {
            _studio.Prepare(_autoPrepare);
            _autoPrepare = null;
        }
        if (_autotestResearchAction is { } action && _elapsed>1)
        {
            Console.WriteLine(UiText.Get("acción investigación: ")+_studio.OpenResearchOriginal("a",analyze:action=="analyze"));
            _autotestResearchAction=null;
        }

        // A new text size rebuilds the font atlas, which ImGui only allows between frames.
        if (_themeChanged) { ConfigureStyle(); _themeChanged = false; }
        PrepareTerminalLayout();
        SetKeyboardNavigation(m);
        Raylib.BeginDrawing();
        Raylib.ClearBackground(UiTheme.Terminal ? new Color(2, 5, 3, 255) : new Color(15, 18, 16, 255));
        if (!InterfaceWorkspace && !_debugMode && _texW != 0)
            DrawPlayScreen();
        rlImGui.Begin(dt);
        ImGui.BeginDisabled(_updates.Blocking || _interfacePicker.IsOpen);
        if (!Hardcore) { DrawMenu(); DrawNavigation(); }
        if (_debugMode && !InterfaceWorkspace)
            DrawDebugger();
        DrawStatusBar();
        _home.Draw();
        DrawHub();
        _browser.Draw(PickFromBrowser);
        if (!_home.IsOpen)
        {
            _studio.Draw();
            _creator.Draw();
            _code.Draw();
            DrawStateWindow();
        }
        DrawPopups();
        DrawSoundWindow();
        DrawPreferencesWindow();
        DrawPaletteEditor();
        AiConnectionWindow.Draw(_ai);
        _commands.Draw(_commands.IsOpen ? CommandPalette.WithOrders(Commands, Terminal.Commands, PrepareConsoleOrder) : Commands);
        DrawTerminal();
        ImGui.EndDisabled();
        _updates.Draw();
        _interfacePicker.ControlBounds = InterfaceControlBounds;
        if (!_updates.Blocking) _interfacePicker.Draw(FinishInterfaceChoice);
        if (!_updates.Blocking) DrawNews();
        rlImGui.End();
        Raylib.EndDrawing();

        _elapsed += dt; _windowFrames++;
        if(_autotestSeconds>=0 && !_autotestGeometry && _elapsed>=1)
        {
            _autotestGeometry=true;
            var viewport=ImGui.GetMainViewport();var io=ImGui.GetIO();
            Console.WriteLine(UiText.Format($"geometria: ventana={Raylib.GetScreenWidth()}x{Raylib.GetScreenHeight()} render={Raylib.GetRenderWidth()}x{Raylib.GetRenderHeight()} dpi={Raylib.GetWindowScaleDPI()} viewport={viewport.Pos}/{viewport.Size} imgui={io.DisplaySize}/{io.DisplayFramebufferScale}"));
        }
        // Frames drawn during the last second: Raylib.GetFPS averages samples taken per call, so read once per second it overstated the rate.
        if (_autotestSeconds >= 0 && (int)_elapsed != (int)(_elapsed - dt))
        {
            Console.WriteLine(UiText.Format($"t={_elapsed:F1}s fps={_windowFrames} objetivo={_targetFps} velocidad={_host.EffectiveSpeed * 100:F0}% audio_en_cola={_host.Audio.Count} cuadros={_host.Machine?.FrameCount}"));
            _windowFrames = 0;
        }
        if (_autotestSeconds >= 0 && _elapsed >= _autotestSeconds && _autotestShot != null)
        {
            FramebufferCapture.Save(_autotestShot);
            if (_autotestReport != null)
                File.WriteAllText(_autotestReport, System.Text.Json.JsonSerializer.Serialize(new
                {
                    version = typeof(RetroApp).Assembly.GetName().Version?.ToString(3), machine = _host.Machine?.Id,
                    frames = _host.Machine?.FrameCount ?? 0, elapsed = _elapsed, paused = _host.Paused,
                    audioDisabled = _audioDisabled, audioDeviceReady = (bool)Raylib.IsAudioDeviceReady(),
                    audioStreamReady = AudioReady, aiDisabled = _ai.ProvidersDisabled,
                    onlineMetadata = _prefs.OnlineGameInfo, style = InterfaceStyles.Describe(_prefs.Interface).Id,
                    palette = _prefs.ConsolePalette, width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight()
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            // The emulated screen too, through the same path as Juego > Guardar captura (out/autotest-capturas).
            if (!_home.IsOpen) { SaveCapture(); Console.WriteLine(UiText.Get("captura: ") + _host.Status); }
            foreach (var group in _library.Entries.GroupBy(e => e.Machine.Name))
                Console.WriteLine(UiText.Format($"biblioteca: {group.Key}: {string.Join(", ", group.Select(e => e.Name))}"));
            foreach (var e in _library.Entries.Where(e => FusionsOf(e.ProfileId).Count > 0))
                Console.WriteLine(UiText.Format($"con fusión: {UiText.Raw(e.Name)}: {string.Join(", ", FusionsOf(e.ProfileId))}"));
            QuitRequested = true;
        }
    }

    /// <summary>
    /// Tab, arrows, Enter and Esc move through the interface while no running game owns the keyboard:
    /// the library, dialogs, menus and panels with the game paused.
    /// </summary>
    private void SetKeyboardNavigation(IMachine? m)
    {
        var io = ImGui.GetIO();
        bool interfaceOnly = m == null || _host.Paused || InterfaceWorkspace || ConfigurationOpen || _browser.IsOpen || _pendingOpen != null
            || _errorPopup != null || _quitNotice != null || _updates.Blocking || _commands.IsOpen;
        // The gamepad moves through the same places (D-pad, A to choose, B to go back).
        var navigation = ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.NavEnableGamepad;
        if (interfaceOnly) io.ConfigFlags |= navigation;
        else io.ConfigFlags &= ~navigation;
        // The console reads Tab and the arrows itself.
        if (ConsoleOwnsKeyboard) io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;
    }

    /// <summary>Select + Start on the gamepad opens the library, or returns to the game from it.</summary>
    private void HandleGamepadShortcut()
    {
        if (!Raylib.IsGamepadAvailable(0) || !Raylib.IsGamepadButtonDown(0, GamepadButton.MiddleLeft)
            || !Raylib.IsGamepadButtonPressed(0, GamepadButton.MiddleRight)) return;
        if (Hardcore) { if (_terminalOpen && HasGame) { OpenPlay(); LeaveTerminal(); } else OpenTerminal(); }
        else if (!_home.IsOpen) { CloseEditors(); _home.Open(); }
        else if (_host.LoadedMedia != null || _host.Fusion != null) _home.Close();
        _input.HoldGamepadUntilReleased();
    }

    /// <summary>Optional pause while another window has the focus (Preferencias > Rendimiento).</summary>
    private void PauseInBackground(IMachine? m)
    {
        bool running = m != null && !_home.IsOpen && !_updates.Blocking;
        switch (_focusPause.Update(_prefs.PauseInBackground, Raylib.IsWindowFocused(), running, _host.Paused))
        {
            case FocusPause.Step.Pause: _host.Pause("Pausa: la ventana está en segundo plano."); break;
            case FocusPause.Step.Resume: _host.Continue(); _host.Status = "Partida reanudada."; break;
        }
    }

    /// <summary>Window close button and Juego > Salir: asks first when AI work would be cancelled.</summary>
    public void RequestQuit()
    {
        _quitNotice = QuitGuard.Notice(_studio.IsWorking, _creator.IsWorking, _code.IsWorking, _tasks.Active.Count(task => !task.Tracked));
        if (_quitNotice == null) QuitRequested = true;
    }

    private void PaceFrames(IMachine? m)
    {
        bool emulating = m != null && !_updates.Blocking && !_host.Paused;
        int fps = FramePacing.TargetFps(_monitorRate, _prefs.PowerSaving, emulating, Raylib.IsWindowFocused(), Raylib.IsWindowMinimized());
        if (fps == _targetFps) return;
        _targetFps = fps;
        Raylib.SetTargetFPS(fps);
    }

    /// <summary>Per-machine defaults for the debugger panels.</summary>
    private void OnMachineChanged(IMachine m)
    {
        _commandCatalog = null;
        _lastMachineId = m.Id;
        _regEdit.Clear();
        if (m.Id == "invaders")
        {
            _memStart = 0x2400;
            _testAddr = "2000";
            _testHex = "3C C3 00 20";                 // INR A / JMP $2000
        }
        else if (m.Id == "c64")
        {
            _memStart = 0x0400;                       // the text screen
            _testAddr = "C000";
            _testHex = "EE 20 D0 4C 00 C0";           // INC $D020 / JMP $C000: border colour loop
        }
        else if (m.Id == "pcxt")
        {
            _memStart = 0xB8000;
            _testAddr = "10000";
            _testHex = "B8 01 00 40 EB FD";           // MOV AX,1 / INC AX / JMP back
        }
        else if (m.Id == "nes")
        {
            _memStart = 0x0000;                       // the 2 KB of RAM
            _testAddr = "0300";
            _testHex = "E6 00 4C 00 03";              // INC $00 / JMP $0300
        }
        else
        {
            _memStart = 0x4000;
            _testAddr = "8000";
            _testHex = "3E 02 D3 FE 3C 18 FB";        // border colour loop
        }
        _memAddr = _memStart.ToString("X4");
    }

    private static AudioQueue? s_queue;
    private static volatile bool s_mute, s_forceMute;
    private static volatile float s_volume = 1;
    private static readonly AudioOutput s_output = new();
    private static long s_lastPull;

    /// <summary>Headphones that disconnect mid-game stop the callback: the queue must not keep pacing the emulation.</summary>
    private void WatchAudioDevice()
    {
        if (!AudioReady) return;
        long last = Interlocked.Read(ref s_lastPull);
        // Until the first pull the stream is still starting.
        if (last != 0) _host.SoundDeviceMissing = RetroAudioWatch.Silent(System.Diagnostics.Stopwatch.GetElapsedTime(last));
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static unsafe void AudioCallback(void* buffer, uint frames)
    {
        var span = new Span<short>(buffer, (int)frames);
        Interlocked.Exchange(ref s_lastPull, System.Diagnostics.Stopwatch.GetTimestamp());
        var q = s_queue;
        if (q == null)
        {
            span.Clear();
            return;
        }
        q.Pop(span);   // consumed even when muted: the queue level paces the emulation
        if (s_forceMute) { span.Clear(); s_output.Reset(); }
        else s_output.Process(span, s_volume, s_mute);
    }

    private void UploadScreen()
    {
        var video = _host.CurrentVideo;
        if (video == null)
            return;
        EnsureScreenTexture(video.Width, video.Height);
        if (_videoCache.Convert(video, _rgba)) Raylib.UpdateTexture(_screenTex, _rgba);
    }

    private void DrawPlayScreen()
    {
        // Bars measured on the previous frame, so a larger text size never covers the picture.
        float top = _menuHeight + WorkspaceLayout.NavigationHeight, bottom = UiTheme.StatusBarHeight + WorkspaceLayout.FooterHeight;
        float w = Math.Max(1, Raylib.GetScreenWidth()), h = Math.Max(1, Raylib.GetScreenHeight() - top - bottom);
        float s = MathF.Min(w / _texW, h / _texH);
        if (_prefs.IntegerScale)
            s = MathF.Min(s, MathF.Max(1, MathF.Floor(s)));
        float dw = _texW * s, dh = _texH * s;
        var dest = new Rectangle((w - dw) / 2, top + (h - dh) / 2, dw, dh);
        Raylib.DrawTexturePro(_screenTex, new Rectangle(0, 0, _texW, _texH), dest, Vector2.Zero, 0, Color.White);
        _screenFocused = true;
    }

    // ------------------------------------------------------------------ files
    private void HandleDroppedFiles()
    {
        if (!Raylib.IsFileDropped())
            return;
        var files = Raylib.GetDroppedFiles();
        if (files.Length > 0)
            OpenFile(files[0]);
    }

    private void OpenFile(string path)
    {
        CloseEditors(); _home.Close(); _debugMode=false;
        string fileName = Path.GetFileName(path);
        try
        {
            if (path.EndsWith(".rfstate", StringComparison.OrdinalIgnoreCase))
            {
                _host.LoadStateFromFile(path);
                return;
            }
            var files = MediaArchive.Open(path);
            if (files.Count == 0)
            {
                _errorPopup = UiText.Format($"{fileName} está vacío (o es un zip sin archivos).");
                return;
            }
            if (files.Count == 1 && MachineCatalog.Unsupported(files[0]) is { } why)
            {
                _errorPopup = UiText.Format($"{fileName} {why}");
                return;
            }
            if (Retro.Systems.Arcade.SpaceInvadersSound.IsSampleSet(files))
            {
                _errorPopup = _host.InstallInvadersSamples(files, _romDir);
                return;
            }
            if (MachineCatalog.IdentifySet(files) is { } board)
            {
                if (LaunchArcade(board.Machine, files, Path.GetDirectoryName(path)))
                {
                    _versions = []; _openedMedia = files; _currentMedia = path;
                    _home.RecordOriginal(path, board.Machine.Id, files);
                }
                return;
            }
            var candidates = files.SelectMany(MachineCatalog.Identify)
                .OrderByDescending(c => c.Machine.Status == MachineStatus.Available)
                .ThenByDescending(c => c.Confidence)
                .ThenBy(c => MediaArchive.DumpScore(c.File.Name))
                .ToList();
            if (candidates.Count == 0)
            {
                _errorPopup = UiText.Format($"No se reconoce el formato de {fileName}.\nAbrí el archivo del juego o su ZIP. En Biblioteca se indican los formatos de cada máquina.");
                return;
            }
            var playable = candidates.Where(c => c.Machine.Status == MachineStatus.Available && c.Confidence >= 50 && MachineCatalog.NotLoadableYet(c) == null).ToList();
            if (playable.Count == 0)
            {
                var best = candidates[0];
                _errorPopup = MachineCatalog.NotLoadableYet(best) is { } notYet
                    ? UiText.Format($"{fileName}: {UiText.Raw(best.Machine.Name)}.\nNo se puede abrir todavía: {notYet}.")
                    : UiText.Format($"{fileName} es para {UiText.Raw(best.Machine.Name)} (CPU {best.Machine.Cpu}): {best.Reason}.\n") +
                      UiText.Get("Este sistema todavía está en desarrollo. En Biblioteca podés ver las máquinas disponibles.");
                return;
            }
            var versions = playable
                .GroupBy(c => (c.Machine.Id, c.File.Source)).Select(g => g.First())
                .OrderBy(c => c.Machine.Id == "pcxt" ? PcDos.ProgramPriority(c.File.Name) : 0)
                .ThenBy(c => MediaArchive.DumpScore(c.File.Name))
                .ThenBy(c => FormatPreference(c.File.Extension))
                .ToList();
            var request = new OpenRequest(path, files, versions);
            if (versions.Count > 1 && versions.All(c => c.Machine.Id == "pcxt"))
            {
                _pausedBeforeChoice = _host.Paused;
                _host.Paused = true;
                _pendingOpen = request;
                return;
            }
            CompleteOpen(request, versions[0]);

        }
        catch (Exception e)
        {
            _errorPopup = UiText.Format($"No se pudo abrir {fileName}: {e.Message}");
        }
    }

    private bool CompleteOpen(OpenRequest request, MachineCatalog.Candidate choice)
    {
        if (!Launch(choice, request.Files)) return false;
        _versions = request.Versions;
        _openedMedia = request.Files; _currentMedia = request.Path;
        _home.RecordOriginal(request.Path, choice.Machine.Id, request.Files);
        return true;
    }

    private void CancelOpen()
    {
        _pendingOpen = null;
        _host.Paused = _pausedBeforeChoice;
    }

    /// <summary>Tapes load the way the original did; snapshots skip the loading.</summary>
    private static int FormatPreference(string ext) => ext switch
    {
        ".tzx" => 0, ".tap" => 1, ".z80" => 2, ".sna" => 3, _ => 9,
    };

    private void EditCurrentStudioGame()
    {
        if (_host.Fusion is Retro.Studio.StudioSession { Game.IsInspired: true }) OpenInspiredCreator();
        else OpenFusionStudio();
    }
    private void CloseEditors() { _hub=HubPage.None; _stateWindowOpen = false; _studio.Close(); _creator.Close(); _code.Close(); }
    private void OpenFusionStudio() { CloseEditors(); _home.Close(); _studio.Open(); }
    private void OpenCreator() { CloseEditors(); _home.Close(); _creator.Open(); }
    private void OpenInspiredCreator() { CloseEditors(); _home.Close(); _studio.Open(inspired: true); }
    private void PlayFusion(string id) { CloseEditors(); _home.Close(); _studio.PlayById(id); _studio.Close(); }
    private void OpenModifications() { CloseEditors(); _home.Close(); _code.OpenModifications(); }
    private void OpenOriginalAnalysis(string path) { OpenFile(path); _code.OpenAutomaticAnalysis(); }
    private void OpenAutomaticAnalysis() {CloseEditors();_home.Close();_debugMode=false;_code.OpenAutomaticAnalysis();}

    private IReadOnlyList<MediaFile> _openedMedia = [];

    private bool Launch(MachineCatalog.Candidate c, IReadOnlyList<MediaFile>? files = null)
    {
        _home.Close();
        try
        {
            if (_host.Machine != null)
                _input.ReleaseAll(_host.Machine);
            if (c.Machine.Id == "nes")
                _host.StartNes(c.Machine, c.File);     // a cartridge: a fresh machine for each game
            else if (c.Machine.Id == "pcxt") _host.StartPc(c.Machine, c.File, _romDir, files ?? _openedMedia);
            else
            {
                if (_host.Info?.Id != c.Machine.Id || _host.Fusion != null)
                    _host.Start(c.Machine, _romDir);
                _host.Load(c.File, _prefs.TypeLoad);
            }
            return true;
        }
        catch (Exception e)
        {
            _errorPopup = e is FileNotFoundException
                ? UiText.Format($"No se pudo arrancar {UiText.Raw(c.Machine.Name)}: {e.Message}\nEl firmware va en la carpeta firmware/ junto al programa.")
                : UiText.Format($"No se pudo cargar {UiText.Raw(c.File.Name)} en {UiText.Raw(c.Machine.Name)}: {e.Message}");
            return false;
        }
    }

    private bool LaunchArcade(MachineInfo info, IReadOnlyList<MediaFile> files, string? folder)
    {
        _home.Close();
        try
        {
            if (_host.Machine != null)
                _input.ReleaseAll(_host.Machine);
            _host.StartInvaders(info, files, _romDir, folder);
            return true;
        }
        catch (Exception e)
        {
            _errorPopup = UiText.Format($"No se pudo encender {UiText.Raw(info.Name)}: {e.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------ hotkeys
    private void HandleHotkeys(bool textInput)
    {
        if (Hardcore)
        {
            bool consoleShortcut = (Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl)) &&
                (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift)) && Raylib.IsKeyPressed(KeyboardKey.T);
            if (consoleShortcut) { if (_terminalOpen && HasGame) { OpenPlay(); LeaveTerminal(); } else OpenTerminal(); return; }
            // With the console closed, the emulator keys of the other styles work in the game too.
            if (ConsoleOwnsKeyboard) { if (Raylib.IsKeyPressed(KeyboardKey.F11)) ToggleFullscreen(); return; }
        }
        if (textInput)
            return;
        if (Raylib.IsKeyPressed(KeyboardKey.F11))
            ToggleFullscreen();
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        bool toolModifier = ctrl && (!ToolShortcutsNeedShift || Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift));
        if (!Hardcore)
        {
            if (toolModifier && Raylib.IsKeyPressed(KeyboardKey.P)) { _commands.Open(); return; }
            if (_commands.IsOpen || ConfigurationOpen) return;
            if (Raylib.IsKeyPressed(KeyboardKey.F12))
            { if (_debugMode && !InterfaceWorkspace) OpenPlay(); else OpenDebugger(); }
            if (toolModifier && Raylib.IsKeyPressed(KeyboardKey.O)) _browser.Open();
            if (toolModifier && Raylib.IsKeyPressed(KeyboardKey.K) && _host.Machine != null)
            {
                CloseEditors(); _home.Close();
                _code.Open();
            }
            if (toolModifier && Raylib.IsKeyPressed(KeyboardKey.L)) { CloseEditors(); _home.Open(); }
            if (toolModifier && Raylib.IsKeyPressed(KeyboardKey.M) && _host.Machine != null) OpenModifications();
        }
        if (InterfaceWorkspace) return;
        if (_host.Machine == null)
            return;
        if (toolModifier && Raylib.IsKeyPressed(KeyboardKey.S)) SaveCapture();
        // the C64 has its own F1 F3 F5 F7 keys (games say "press F1"): there quick load is
        // F4 and reset is in the Machine menu
        if (_host.Pc != null && _host.Fusion == null && !ctrl) return;
        bool machineFKeys = _host.C64 != null && _host.Fusion == null;
        if (Raylib.IsKeyPressed(KeyboardKey.F2)) _host.QuickSave();
        if (Raylib.IsKeyPressed(KeyboardKey.F4) || (!machineFKeys && _host.Pc == null && Raylib.IsKeyPressed(KeyboardKey.F3))) _host.QuickLoad();
        if (!machineFKeys && Raylib.IsKeyPressed(KeyboardKey.F5)) _host.Reset(Raylib.IsKeyDown(KeyboardKey.LeftShift));
        if (Raylib.IsKeyPressed(KeyboardKey.F6)) ToggleTape();
        if (Raylib.IsKeyPressed(KeyboardKey.F7)) _host.Zx?.Tape.Rewind();
        if (Raylib.IsKeyPressed(KeyboardKey.F8)) _host.Turbo = !_host.Turbo;
        if (Raylib.IsKeyPressed(KeyboardKey.F9))
        {
            if (_host.Paused) _host.Continue();
            else _host.Pause("Pausa");
        }
        if (Raylib.IsKeyPressed(KeyboardKey.F10))
        {
            if (Raylib.IsKeyDown(KeyboardKey.LeftShift)) _host.StepOver();
            else _host.StepInto();
            _debugMode = true;
        }
    }

    private void ToggleTape()
    {
        var zx = _host.Zx;
        if (zx == null) return;
        if (zx.Tape.IsPlaying) zx.Tape.Stop(zx.Z80.T);
        else zx.Tape.Play(zx.Z80.T);
    }

    /// <summary>Scans another folder in the background. The files stay where they are and the choice is remembered.</summary>
    private void UseLibraryFolder(string folder)
    {
        string full = Path.GetFullPath(folder);
        _prefs.LibraryFolder = full; _prefs.Save();
        _library.Rescan(full); _home.TerminalRefresh();
        _studio.NotifyLibraryChanged(full);
    }

    private void PickFromBrowser(string path)
    {
        if (_browser.Folder != _prefs.LastFolder) { _prefs.LastFolder = _browser.Folder; _prefs.Save(); }
        OpenFile(path);
    }

    private void SaveCapture()
    {
        if (_host.CurrentVideo is not { } video) return;
        string title = _host.Fusion?.Title ?? (_host.LoadedMedia != null ? Path.GetFileNameWithoutExtension(_host.LoadedMedia) : _host.Info?.Name ?? "captura");
        try { _host.Status = UiText.Get("Captura guardada: ") + ScreenCapture.Save(video, _captureFolder, title, DateTime.Now); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { _errorPopup = UiText.Get("No se pudo guardar la captura: ") + e.Message; }
    }

    /// <summary>The library original being played: thumbnails are not offered for fusions, creations or loose files.</summary>
    private string? ThumbnailTarget() =>
        _host.Fusion == null && _host.LoadedMedia != null && _host.CurrentVideo != null && _currentMedia is { } media
        && _library.Entries.Any(entry => entry.Path.Equals(media, StringComparison.OrdinalIgnoreCase)) ? media : null;

    private void SaveThumbnail()
    {
        if (ThumbnailTarget() is not { } media || _host.CurrentVideo is not { } video) return;
        try
        {
            _host.Status = ScreenCapture.SaveThumbnail(video, media) is { } path
                ? UiText.Format($"Miniatura guardada: {UiText.Raw(Path.GetFileName(path))}. Se ve al volver a la biblioteca.")
                : "Ese juego ya tiene una imagen junto a su archivo; no se reemplaza.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { _errorPopup = UiText.Get("No se pudo guardar la miniatura: ") + e.Message; }
    }

    private void ToggleFullscreen()
    {
        Raylib.ToggleFullscreen();
        _prefs.Fullscreen = Raylib.IsWindowFullscreen(); _prefs.Save();
    }

    // ------------------------------------------------------------------ menu
    private bool ToolShortcutsNeedShift => InputMapper.ToolShortcutsNeedShift(_host.Machine, _host.Fusion != null);
    private string ToolShortcut(string key) => (ToolShortcutsNeedShift ? "Ctrl+Shift+" : "Ctrl+") + key;
    private string Shortcut(string key) => _host.Pc != null && _host.Fusion == null ? UiText.Get("Ctrl+") + key :
        _host.Fusion == null && _host.C64 != null && key is "F5" or "Shift+F5" ? "" : key;

    private void DrawMenu()
    {
        if (!ImGui.BeginMainMenuBar()) return;
        _menuHeight = ImGui.GetWindowHeight();
        // ImGui moves to a menu bar when Alt is released, but only in the focused window: focusing
        // the main bar while Alt is down lets the keyboard reach Biblioteca, Juego, Opciones...
        var io = ImGui.GetIO();
        if ((io.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0 && !io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.LeftAlt, false))
            ImGui.SetWindowFocus();
        float menuWidth = ImGui.CalcTextSize(UiText.Get(UiText.Get("Biblioteca Juego Crear Herramientas Opciones Ayuda") + (UiTheme.CompactHeight ? " Volver a jugar" : ""))).X + ImGui.GetStyle().ItemSpacing.X * 14;
        bool compactMenu = ImGui.GetMainViewport().WorkSize.X < menuWidth;
        if (compactMenu && !ImGui.BeginMenu(UiText.Label("Menú")))
        {
            if (ImGui.MenuItem(UiText.Label("Buscar acción…"), ToolShortcut("P"))) _commands.Open();
            ImGui.EndMainMenuBar(); return;
        }
        if (ImGui.MenuItem(UiText.Label("Biblioteca"), ToolShortcut("L"))) { CloseEditors(); _home.Open(); }
        if (UiTheme.CompactHeight && ImGui.MenuItem(UiText.Label("Volver a jugar"), "", false, _host.Machine != null)) OpenPlay();
        if (ImGui.BeginMenu(UiText.Label("Juego")))
        {
            if (ImGui.MenuItem(UiText.Label("Abrir juego o programa..."), ToolShortcut("O"))) _browser.Open();
            if (ImGui.BeginMenu(UiText.Label("Recientes")))
            {
                var recents = _home.Recents(10);
                if (recents.Count == 0) ImGui.TextDisabled(UiText.Get("Todavía no abriste juegos."));
                for (int r = 0; r < recents.Count; r++)
                    if (ImGui.MenuItem($"{UiTheme.Fit(recents[r].Title, UiTheme.Px(360))}##reciente{r}", recents[r].Detail)) recents[r].Play();
                ImGui.Separator();
                if (ImGui.MenuItem(UiText.Label("Ver todos en la biblioteca"))) { CloseEditors(); _home.ShowRecents(); }
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(UiText.Label(_versions.Count > 0 && _versions.All(v => v.Machine.Id == "pcxt") ? "Programas del archivo" : "Otras versiones del último juego"), _versions.Count > 1))
            {
                ImGui.TextDisabled(UiText.Get("Elegí otro archivo del mismo paquete."));
                foreach (var v in _versions)
                    if (ImGui.MenuItem(UiText.Label(UiText.Format($"{PcDos.DisplayPath(v.File)}##{v.File.Source}"))))
                        Launch(v);
                ImGui.EndMenu();
            }
            ImGui.Separator();
            if (ImGui.MenuItem(UiText.Label("Guardar estado rápido"), Shortcut("F2"), false, _host.CanUseStates)) _host.QuickSave();
            if (ImGui.MenuItem(UiText.Label("Cargar estado rápido"), _host.Pc != null ? Shortcut("F4") : _host.C64 != null ? "F4" : "F3", false, _host.CanUseStates)) _host.QuickLoad();
            if (ImGui.MenuItem(UiText.Label("Partidas guardadas..."), "", false, _host.CanUseStates)) OpenStateWindow();
            if (ImGui.BeginMenu(UiText.Label("Rebobinado y entradas"), _host.CanUseStates))
            {
                bool rewind = _host.RewindEnabled;
                if (ImGui.MenuItem(UiText.Label("Conservar historial (hasta 64 MB)"), "", ref rewind)) _host.SetRewindEnabled(rewind);
                if (ImGui.MenuItem(UiText.Label("Retroceder cinco segundos"), "", false, _host.RewindPoints > 0 && !_host.IsRecordingInputs && !_host.IsReplayingInputs))
                    TryStateAction(() => _host.Rewind());
                if (ImGui.MenuItem(UiText.Label("Vaciar historial"), "", false, _host.RewindPoints > 0)) _host.ClearRewind();
                ImGui.TextDisabled(UiText.Get(UiText.Format($"Historial: {_host.RewindPoints} puntos · {_host.RewindBytes / (1024 * 1024.0):F1} MB")));
                ImGui.Separator();
                if (ImGui.MenuItem(UiText.Label("Grabar entradas desde aquí"), "", false, _host.CanRecordInputs)) TryStateAction(() => _host.StartInputRecording());
                if (ImGui.MenuItem(UiText.Label("Terminar grabación"), "", false, _host.IsRecordingInputs)) _host.StopInputRecording();
                if (ImGui.MenuItem(UiText.Label("Reproducir última grabación"), "", false, _host.CanReplayInputs)) TryStateAction(() => _host.StartInputReplay());
                if (ImGui.MenuItem(UiText.Label("Detener reproducción"), "", false, _host.IsReplayingInputs)) _host.StopInputReplay();
                ImGui.TextDisabled(UiText.Get(UiText.Format($"Grabación: {_host.RecordedFrames} cuadros (solo esta sesión)")));
                ImGui.EndMenu();
            }
            ImGui.Separator();
            if (ImGui.MenuItem(UiText.Label("Guardar captura de pantalla"), ToolShortcut("S"), false, _host.CurrentVideo != null)) SaveCapture();
            if (ImGui.IsItemHovered()) UiTheme.Tooltip(UiText.Get("PNG de la pantalla emulada, a su resolución, en ") + _captureFolder);
            if (ImGui.MenuItem(UiText.Label("Usar la pantalla como miniatura"), "", false, ThumbnailTarget() != null)) SaveThumbnail();
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Guarda la imagen junto al juego para la biblioteca. No reemplaza una imagen que ya exista.");
            ImGui.Separator();
            ImGui.BeginDisabled(_host.Machine == null);
            if (ImGui.MenuItem(UiText.Label(_host.Paused ? "Continuar" : "Pausa"), Shortcut("F9"), false, !_home.IsOpen))
            { if (_host.Paused) _host.Continue(); else _host.Pause("Pausa"); }
            if (ImGui.MenuItem(UiText.Label(_host.Fusion != null ? "Reiniciar fusión o creación" : "Reiniciar"), Shortcut("F5"))) _host.Reset(false);
            if (ImGui.MenuItem(UiText.Label("Reinicio total (borra RAM)"), Shortcut("Shift+F5"), false, _host.Fusion == null)) _host.Reset(true);
            bool turbo = _host.Turbo;
            if (ImGui.MenuItem(UiText.Label("Turbo"), Shortcut("F8"), ref turbo)) _host.Turbo = turbo;
            if (ImGui.MenuItem(UiText.Label("Detener fusión o creación"), "", false, _host.Fusion != null)) _host.StopFusion();
            if ((_host.Zx != null || _host.C64 != null || _host.Invaders != null) && ImGui.BeginMenu(UiText.Label("Controles y hardware")))
            {
            if (_host.Zx != null)
            {
                if (ImGui.BeginMenu(UiText.Label("Joystick en flechas / gamepad")))
                {
                    foreach (JoystickMode jm in Enum.GetValues<JoystickMode>())
                        if (ImGui.MenuItem(UiText.Label(JoystickNames[(int)jm]), "", _input.Joystick == jm)) SetSpectrumJoystick(jm);
                    ImGui.EndMenu();
                }
            }
            if (_host.C64 is { } c64)
            {
                if (ImGui.BeginMenu(UiText.Label("Joystick en flechas / gamepad")))
                {
                    if (ImGui.MenuItem(UiText.Label("Puerto 2 (la mayoría de los juegos)"), "", _input.C64Port == 2)) SetC64Port(2);
                    if (ImGui.MenuItem(UiText.Label("Puerto 1"), "", _input.C64Port == 1)) SetC64Port(1);
                    ImGui.TextDisabled(UiText.Get("Si el personaje no se mueve, probá el otro puerto."));
                    ImGui.EndMenu();
                }
                if (ImGui.MenuItem(UiText.Label("Pulsar RUN/STOP + RESTORE (vuelve al BASIC)")))
                    c64.QueueKeys(8, "RUN/STOP", "RESTORE");
                if (c64.OpenRoms && c64.UnhandledKernalCalls.Count > 0)
                    ImGui.TextDisabled(UiText.Get(UiText.Format($"El juego llamó a rutinas internas del KERNAL original: {string.Join(", ", c64.UnhandledKernalCalls.Select(a => UiText.Format($"${a:X4}")))}.")));
            }
            if (_host.Invaders is { } inv && ImGui.BeginMenu(UiText.Label("DIP switches de la placa")))
            {
                for (int ships = 3; ships <= 6; ships++)
                    if (ImGui.MenuItem(UiText.Label(UiText.Format($"{ships} naves por partida")), "", inv.Ships == ships)) inv.Ships = ships;
                ImGui.Separator();
                bool bonus = inv.BonusAt1000;
                if (ImGui.MenuItem(UiText.Label("Nave extra a los 1000 puntos (si no, 1500)"), "", ref bonus)) inv.BonusAt1000 = bonus;
                bool coin = inv.ShowCoinInfo;
                if (ImGui.MenuItem(UiText.Label("Mostrar información de monedas"), "", ref coin)) inv.ShowCoinInfo = coin;
                ImGui.TextDisabled(UiText.Get("Los cambios se leen al empezar la próxima partida."));
                ImGui.EndMenu();
            }
                ImGui.EndMenu();
            }
        if (_host.Zx is { } zx && ImGui.BeginMenu(UiText.Label("Cinta")))
        {
            var tape = zx.Tape;
            bool typeLoad = _prefs.TypeLoad;
            if (ImGui.MenuItem(UiText.Label("Escribir LOAD \"\" al abrir cintas"), "", ref typeLoad)) { _prefs.TypeLoad = typeLoad; _prefs.Save(); }
            bool turboLoading = _host.TurboWhileLoading;
            if (ImGui.MenuItem(UiText.Label("Acelerar durante la carga"), "", ref turboLoading)) SetTurboWhileLoading(turboLoading);
            if (ImGui.MenuItem(UiText.Label(tape.IsPlaying ? "Detener" : "Reproducir"), "F6", false, tape.IsLoaded)) ToggleTape();
            if (ImGui.MenuItem(UiText.Label("Rebobinar"), "F7", false, tape.IsLoaded)) tape.Rewind();
            if (ImGui.MenuItem(UiText.Label("Expulsar"), "", false, tape.IsLoaded)) tape.Eject();
            bool auto = zx.AutoPlayTape;
            if (ImGui.MenuItem(UiText.Label("Arrancar sola al ejecutar LOAD"), "", ref auto)) zx.AutoPlayTape = auto;
            if (ImGui.MenuItem(UiText.Label("Escribir LOAD \"\" ahora"))) zx.TypeLoadCommand();
            ImGui.EndMenu();
        }
            ImGui.EndDisabled();
            if (ImGui.BeginMenu(UiText.Label("Encender sin juego")))
            {
                foreach (string id in new[] { "zx48", "c64" })
                {
                    var info = MachineCatalog.Get(id);
                    if (ImGui.MenuItem(info.Name))
                    {
                        try { CloseEditors(); _home.Close(); _host.Start(info, _romDir); }
                        catch (Exception error) { _errorPopup = error.Message; }
                    }
                }
                ImGui.EndMenu();
            }
            ImGui.Separator();
            if (ImGui.MenuItem(UiText.Label("Salir"))) RequestQuit();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu(UiText.Label("Crear")))
        {
            if (ImGui.MenuItem(UiText.Label("Fusionar juegos..."))) OpenFusionStudio();
            if (ImGui.MenuItem(UiText.Label("Juego nuevo desde cero..."))) OpenCreator();
            if (ImGui.MenuItem(UiText.Label("Juego nuevo con tus ROMs..."))) OpenInspiredCreator();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu(UiText.Label("Herramientas")))
        {
            if (ImGui.MenuItem(UiText.Label("Buscar acción..."), ToolShortcut("P"))) _commands.Open();
            ImGui.Separator();
            ImGui.BeginDisabled(_host.Machine == null);
            if (ImGui.MenuItem(UiText.Label("Modificar y analizar..."), ToolShortcut("M"))) OpenModifications();
            if(ImGui.MenuItem(UiText.Label("Investigar variables automáticamente..."),"",false,_host.LoadedMediaSource!=null && _host.Fusion==null)) OpenAutomaticAnalysis();
            if (ImGui.MenuItem(UiText.Label("Código y rutinas..."), ToolShortcut("K"))) { CloseEditors(); _home.Close(); _code.Open(); }
            ImGui.Separator();
            if (ImGui.BeginMenu(UiText.Label("Depuración")))
            {
                if (ImGui.MenuItem(UiText.Label("Mostrar depurador"), "F12", _debugMode)) { if (_debugMode && !InterfaceWorkspace) OpenPlay(); else OpenDebugger(); }
                if (ImGui.MenuItem(UiText.Label("Paso (una instrucción)"), Shortcut("F10"))) { OpenDebugger(); _host.StepInto(); }
                if (ImGui.MenuItem(UiText.Label("Paso por encima (CALL, bucles)"), Shortcut("Shift+F10"))) { OpenDebugger(); _host.StepOver(); }
                if (ImGui.MenuItem(UiText.Label("Avanzar un cuadro"))) { OpenDebugger(); _host.StepFrame(); }
                if (ImGui.MenuItem(UiText.Label("Reorganizar paneles"))) { OpenDebugger(); _arrangeDebugger = true; }
                if (ImGui.IsItemHovered()) UiTheme.Tooltip("Vuelve a ubicar los paneles para que entren en la ventana actual.");
                if (ImGui.MenuItem(UiText.Label("Borrar todos los breakpoints"), "", false, _host.Debug.Breakpoints.Count > 0)) _host.Debug.Clear();
                ImGui.EndMenu();
            }
            ImGui.EndDisabled();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu(UiText.Label("Opciones")))
        {
            if (ImGui.MenuItem(UiText.Label("Estilos de interfaz…"))) OpenInterfacePicker();
            if (ImGui.MenuItem(UiText.Label("Preferencias..."))) _preferencesOpen = true;
            if (ImGui.MenuItem(UiText.Label("Conectar o configurar IA..."))) AiConnectionWindow.Open(_ai);
            ImGui.Separator();
            if (ImGui.BeginMenu(UiText.Label("Sonido")))
            {
                bool muted = _sound.Muted;
                if (ImGui.MenuItem(UiText.Label("Silenciar"), "", ref muted)) { _sound.Muted = muted; _sound.Save(); }
                if (ImGui.MenuItem(UiText.Label("Volumen y opciones..."))) _soundWindowOpen = true;
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(UiText.Label("Pantalla")))
            {
                if (ImGui.MenuItem(UiText.Label("Pantalla completa"), "F11", Raylib.IsWindowFullscreen())) ToggleFullscreen();
                bool integer = _prefs.IntegerScale;
                if (ImGui.MenuItem(UiText.Label("Escala entera (píxeles nítidos)"), "", ref integer)) { _prefs.IntegerScale = integer; _prefs.Save(); }
                bool fps = _prefs.ShowFps;
                if (ImGui.MenuItem(UiText.Label("Mostrar cuadros por segundo"), "", ref fps)) { _prefs.ShowFps = fps; _prefs.Save(); }
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(UiText.Label("Accesibilidad")))
            {
                if (ImGui.BeginMenu(UiText.Label("Tamaño del texto")))
                {
                    foreach (int size in AppPreferences.TextSizes)
                        if (ImGui.MenuItem(UiText.Label(UiText.Format($"{size} %")), "", _prefs.TextPercent == size)) SetTextSize(size);
                    ImGui.EndMenu();
                }
                bool contrast = _prefs.HighContrast;
                if (ImGui.MenuItem(UiText.Label("Alto contraste"), "", ref contrast)) SetHighContrast(contrast);
                ImGui.EndMenu();
            }
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu(UiText.Label("Ayuda")))
        {
            if (ImGui.MenuItem(UiText.Label("Novedades…"))) OpenNews();
            if (ImGui.MenuItem(UiText.Label("Actualizaciones..."))) _updates.Open();
            if (ImGui.BeginMenu(UiText.Label("Sistemas disponibles y previstos")))
            {
                foreach (var info in MachineCatalog.All)
                {
                    ImGui.TextUnformatted(UiText.Get(info.Name + (info.Status == MachineStatus.Available ? " · disponible" : " · en desarrollo")));
                    if (ImGui.IsItemHovered()) UiTheme.Tooltip(info.Cpu + "\n" + info.Needs);
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu(UiText.Label("Abrir y jugar")))
            {
                ImGui.TextUnformatted(UiText.Get("En Biblioteca elegí una máquina o Todos los juegos y pulsá Jugar."));
                ImGui.TextUnformatted(UiText.Get("También podés arrastrar un archivo a la ventana."));
                ImGui.TextUnformatted(UiText.Get("Hacé clic en la pantalla para usar el teclado."));
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(UiText.Label("Controles de la partida")))
            {
                DrawControlsHelp();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(UiText.Label("Atajos")))
            {
                DrawShortcutsHelp();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(UiText.Label("Accesibilidad y teclado")))
            {
                ImGui.PushTextWrapPos(UiTheme.Px(440));
                ImGui.TextWrapped(UiText.Get("Opciones > Accesibilidad cambia el tamaño del texto y activa el alto contraste. Los cambios se guardan."));
                ImGui.TextWrapped(UiText.Get("En la biblioteca, los diálogos y con la partida en pausa, Alt izquierdo lleva a los menús; Tab y las flechas recorren los controles, Enter o Espacio los activan y Esc vuelve."));
                ImGui.TextWrapped(UiText.Get(UiText.Get("Mientras el juego corre, el teclado es de la máquina emulada. Pausá con ") + Shortcut("F9") + UiText.Get(" para usar la interfaz con el teclado.")));
                ImGui.TextWrapped(UiText.Get("Con un mando, Select + Start abre la biblioteca; la cruceta recorre, A elige y B vuelve."));
                ImGui.TextWrapped(UiText.Get("Opciones > Preferencias puede pausar el juego al pasar a otra ventana."));
                ImGui.PopTextWrapPos();
                ImGui.EndMenu();
            }
            ImGui.EndMenu();
        }
        if (compactMenu) ImGui.EndMenu();
        ImGui.EndMainMenuBar();
    }

    private void DrawControlsHelp()
    {
        if (_host.Fusion is Retro.Studio.StudioSession) ImGui.TextWrapped(UiText.Get(ControlsHelp()[0]));
        else foreach (string line in ControlsHelp()) ImGui.TextUnformatted(UiText.Get(line));
    }

    /// <summary>Controls of the current machine, shared by the help pages and the console.</summary>
    private string[] ControlsHelp()
    {
        if (_host.Fusion is Retro.Studio.StudioSession fusion) return [fusion.Game.HowToPlay];
        return _host.Machine?.Id switch
        {
            "zx48" => ["Teclado: letras y números del juego.", "Shift: CAPS SHIFT    Ctrl: SYMBOL SHIFT    Backspace: DELETE",
                "Joystick: flechas y Alt. Elegí el tipo en Juego > Controles y hardware.", "Ctrl+Shift+O/L/M/K: abrir, biblioteca, modificar y código."],
            "c64" => ["Joystick: flechas y Alt o Ctrl derecho.", "Si no responde, cambiá el puerto en Juego > Controles y hardware.",
                "Esc: RUN/STOP    RePág: RESTORE    Ctrl izquierdo: C=    Tab: CTRL", "F1, F3, F5 y F7 son las teclas del C64.", "Ctrl+Shift+O/L/M/K: abrir, biblioteca, modificar y código."],
            "pcxt" => ["Teclado de PC de 83 teclas, con distribución estadounidense.", "F1–F10 llegan al PC. Ctrl + tecla de función controla el emulador.", "Ctrl+Shift+O/L/M/K: abrir, biblioteca, modificar y código."],
            "nes" => ["Flechas: mover    X: A    Z: B", "Enter: Start    Shift derecho: Select"],
            "invaders" => ["Flechas o A/D: mover    Espacio: disparar", "5 o C: moneda    1/2: empezar    T: tilt"],
            _ => ["Abrí un juego para ver sus controles."]
        };
    }

    private void DrawShortcutsHelp()
    {
        string loadKey = _host.Pc != null ? Shortcut("F4") : _host.C64 != null ? "F4" : "F3";
        var rows = new List<(string Keys, string Action)>
        {
            (ToolShortcut("O"), "Abrir juego o programa"), (ToolShortcut("L"), "Biblioteca"),
            (ToolShortcut("M"), "Modificar y analizar"), (ToolShortcut("K"), "Código del juego"),
            (ToolShortcut("P"), "Buscar una acción o herramienta"),
            (ToolShortcut("S"), "Guardar captura de pantalla"),
        };
        if (_host.Fusion == null)
        {
            rows.Add((Shortcut("F2"), "Guardar estado rápido"));
            rows.Add((loadKey, "Cargar estado rápido"));
            if (Shortcut("F5").Length > 0) rows.Add((Shortcut("F5") + " / " + Shortcut("Shift+F5"), "Reiniciar / reinicio total"));
        }
        if (_host.Zx != null) rows.Add(("F6 / F7", "Cinta: reproducir o detener / rebobinar"));
        rows.Add((Shortcut("F8"), "Turbo"));
        rows.Add((Shortcut("F9"), "Pausa y continuar"));
        rows.Add((Shortcut("F10") + " / " + Shortcut("Shift+F10"), "Paso / paso por encima"));
        rows.Add(("F11", "Pantalla completa"));
        rows.Add(("F12", "Depurador"));
        rows.Add(("Alt izquierdo", "Menús, con la partida en pausa"));
        rows.Add(("Ctrl+F", "Buscar en la biblioteca"));
        rows.Add(("Select + Start", "Biblioteca desde el mando"));
        if (_host.Fusion != null) ImGui.TextDisabled(UiText.Get("Los estados guardados están disponibles en originales."));
        if (!ImGui.BeginTable("atajos", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg)) return;
        foreach (var (keys, action) in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextColored(UiTheme.Accent, UiText.Get(keys));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(UiText.Get(action));
        }
        ImGui.EndTable();
    }

    /// <summary>The newest version of each game made in the AI studio.</summary>
    private static List<Retro.Studio.StudioGame> StudioGames() =>
        Retro.Studio.StudioLibrary.AllVersions().GroupBy(g => g.Id).Select(g => g.OrderByDescending(v => v.Version).First()).ToList();

    /// <summary>Names of the studio games that use a profile.</summary>
    private static List<string> FusionsOf(string? profileId)
    {
        if (profileId == null)
            return new List<string>();
        return StudioGames().Where(s => s.Games.Any(g => g.Profile == profileId)).Select(s => s.Title)
            .Distinct().ToList();
    }

    private void TryStateAction(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { _errorPopup = UiText.Get("No se pudo guardar o cargar el estado: ") + error.Message; }
    }

    private void RevealCurrentWork()
    {
        CloseEditors(); _home.Close();
        if (_studio.IsWorking) _studio.ShowWork();
        else if (_creator.IsWorking) _creator.ShowWork();
        else if (_code.IsWorking) _code.ShowWork();
    }

    private void DrawStatusBar()
    {
        var vp = ImGui.GetMainViewport();
        float height = UiTheme.StatusBarHeight;
        ImGui.SetNextWindowPos(new(vp.Pos.X, vp.Pos.Y + vp.Size.Y - height));
        ImGui.SetNextWindowSize(new(vp.Size.X, height));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, UiTheme.Px(8, 3));
        ImGui.Begin("##status", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoScrollbar);
        string status;
        if (Hardcore && (_terminalOpen || _terminalBarKeyboard)) status = ConsoleStatus();
        else if (_home.IsOpen) status = _library.Scanning ? "Buscando juegos..." : UiText.Format($"{_home.GameCount} {(_home.GameCount == 1 ? UiText.Get("juego") : UiText.Get("juegos"))} en la biblioteca");
        else if (_hub!=HubPage.None) status = _hub==HubPage.Create ? "Taller de creación · Ideas, proyectos y trabajos guardados." : _hub==HubPage.Tools ? "Herramientas · Partida, análisis y configuración." : "Ayuda · Controles, atajos y próximos pasos.";
        else if (ShowEmptyPreview) status = _creator.IsOpen ? "Creación desde cero · Prepará una idea o empezá desde una plantilla."
            : "Creación con originales · Elegí los juegos y definí cómo se combinan.";
        else
        {
            var m = _host.Machine;
            string speed = m == null ? "" : _host.Paused ? "Pausa" : _host.IsFastForwarding ? UiText.Format($"Rápido x{_host.EffectiveSpeed:F0}") : UiText.Format($"{_host.EffectiveSpeed * 100:F0}%");
            string extra = _host.Zx is { } zx ? !zx.Tape.IsLoaded ? UiText.Get(" · Sin cinta") : zx.Tape.IsPlaying
                ? UiText.Format($" · Cargando cinta ({zx.Tape.Progress * 100:F0}%)") : zx.Tape.AtEnd ? UiText.Get(" · Cinta al final") : UiText.Get(" · Cinta detenida") : "";
            if (_host.C64 is { OpenRoms: true } c64 && c64.UnhandledKernalCalls.Count > 0) extra = UiText.Get(" · Requiere revisar el firmware");
            if (_host.SoundDeviceMissing) extra += UiText.Get(" · Sin salida de audio");
            string title = _host.Fusion?.Title ?? _host.Info?.Name ?? "Sin máquina";
            status = UiText.Format($"{title} · {UiText.Get(speed)}{extra} · {_host.Status}");
            if (_host.Fusion is Retro.Studio.StudioSession session)
            {
                string live = FusionPresentation.Status(session.Hud, session.Events, session.Tick);
                if (live.Length > 0) status = UiText.Format($"{live} · {title} · {speed}");
            }
        }
        if (_prefs.ShowFps) status = UiText.Format($"{Raylib.GetFPS()} cuadros/s · {status}");
        // A scan of a few milliseconds is not worth a button that flickers.
        var running = _tasks.Active.Where(task => task.Elapsed > TimeSpan.FromSeconds(1)).ToArray();
        int jobs = running.Length;
        bool work = jobs > 0;
        string workLabel = jobs > 1 ? UiText.Format($"{jobs} tareas en curso") : jobs == 1 ? _tasks.IsQuiet(running[0]) ? UiText.Get("Tarea sin actividad") : UiText.Get("Tarea: ") + UiText.Get(running[0].Kind) : "";
        float buttonWidth = ImGui.CalcTextSize(UiText.Get(workLabel)).X + ImGui.GetStyle().FramePadding.X * 2;
        // The console's status line uses its cells: at large text sizes the proportional font leaves room for very little.
        if (Hardcore) ImGui.PushFont(UiTheme.ConsoleFont);
        ImGui.TextUnformatted(UiText.Get(UiTheme.Fit(status, ImGui.GetContentRegionAvail().X - (work ? buttonWidth + UiTheme.Px(12) : 0))));
        if (Hardcore) ImGui.PopFont();
        if (ImGui.IsItemHovered()) UiTheme.Tooltip(status);
        if (work)
        {
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - buttonWidth - UiTheme.Px(8)));
            if (ImGui.SmallButton(UiText.Label(workLabel))) ImGui.OpenPopup(UiText.Label("tareas-activas"));
            DrawTaskList();
        }
        ImGui.End(); ImGui.PopStyleVar();
    }

    /// <summary>Every long operation in one list: what it is waiting for, how far it got and how to stop it.</summary>
    private void DrawTaskList()
    {
        ImGui.SetNextWindowSizeConstraints(new(Math.Min(UiTheme.Px(420), ImGui.GetMainViewport().Size.X - 24), 0), ImGui.GetMainViewport().Size - new Vector2(24));
        if (!ImGui.BeginPopup(UiText.Label("tareas-activas"))) return;
        UiTheme.Eyebrow("TAREAS EN CURSO");
        foreach (var task in _tasks.Active)
        {
            ImGui.PushID(task.Id);
            ImGui.TextUnformatted(UiText.Get(task.Kind + " · " + task.Target));
            UiTheme.Hint(_tasks.Describe(task) + " · " + AppTaskCenter.Duration(task.Elapsed));
            if (task.Stage.Length > 0) UiTheme.Hint(task.Stage);
            // A bar only where something was counted; a guessed percentage would be a lie.
            if (task.Fraction is { } fraction) ImGui.ProgressBar((float)fraction, new(-1, 0), AppTaskCenter.Progress(task));
            if (_tasks.Advice(task) is { Length: > 0 } advice) UiTheme.Hint(advice);
            ImGui.BeginDisabled(!task.CanCancel);
            if (ImGui.SmallButton(UiText.Label(task.CancelRequested ? "Cancelando…" : "Cancelar"))) task.Cancel();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiTheme.Tooltip("Lo que ya produjo se conserva.");
            ImGui.PopID();
            ImGui.Separator();
        }
        if ((_studio.IsWorking || _creator.IsWorking || _code.IsWorking) && ImGui.Button(UiText.Label("Ver el taller"))) { ImGui.CloseCurrentPopup(); RevealCurrentWork(); }
        ImGui.EndPopup();
    }

    private void DrawPopups()
    {
        if (_quitNotice is { } notice)
        {
            const string title = "Salir de RetroEmu";
            ImGui.OpenPopup(UiText.Label(title));
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowSize(new(Math.Min(UiTheme.Px(520), viewport.WorkSize.X - 24), 0), ImGuiCond.Appearing);
            ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new(0.5f));
            bool open = true;
            if (ImGui.BeginPopupModal(UiText.Label(title), ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking))
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Min(UiTheme.Px(490), viewport.WorkSize.X - 60));
                ImGui.TextWrapped(UiText.Get(notice));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                if (ImGui.Button(UiText.Label("Volver"))) { _quitNotice = null; ImGui.CloseCurrentPopup(); }
                ImGui.SameLine();
                if (ImGui.Button(UiText.Label("Ver el trabajo"))) { _quitNotice = null; ImGui.CloseCurrentPopup(); RevealCurrentWork(); }
                ImGui.SameLine();
                if (ImGui.Button(UiText.Label("Salir y cancelar"))) { _quitNotice = null; ImGui.CloseCurrentPopup(); QuitRequested = true; }
                if (ImGui.IsKeyPressed(ImGuiKey.Escape)) { _quitNotice = null; ImGui.CloseCurrentPopup(); }
                ImGui.EndPopup();
            }
            if (!open) _quitNotice = null;
        }
        if (_errorPopup != null)
        {
            ImGui.OpenPopup(UiText.Label("Aviso"));
            bool open = true;
            var area = ImGui.GetMainViewport().WorkSize;
            float width = Math.Min(UiTheme.Px(620), area.X - 24);
            ImGui.SetNextWindowSizeConstraints(new(width, UiTheme.Px(100)), new(width, area.Y - 32));
            ImGui.SetNextWindowSize(new(width, 0), ImGuiCond.Appearing);
            if (ImGui.BeginPopupModal(UiText.Label("Aviso"), ref open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                float minimum = UiTheme.Px(55);
                float height = Math.Clamp(ImGui.CalcTextSize(UiText.Get(_errorPopup), false, ImGui.GetContentRegionAvail().X).Y + UiTheme.Px(12),
                    minimum, Math.Max(minimum, area.Y - UiTheme.Px(160)));
                ImGui.BeginChild("mensaje-aviso", new(0, height)); ImGui.TextWrapped(UiText.Get(_errorPopup)); ImGui.EndChild();
                UiTheme.Hint("Tus juegos y partidas no cambiaron.");
                if (UiTheme.PrimaryButton("Aceptar"))
                {
                    _errorPopup = null;
                    ImGui.CloseCurrentPopup();
                }
                UiTheme.SameLineIfFits("Ir a la biblioteca");
                if (ImGui.Button(UiText.Label("Ir a la biblioteca")))
                {
                    _errorPopup = null;
                    ImGui.CloseCurrentPopup();
                    if (Hardcore) OpenTerminal(); else { CloseEditors(); _home.Open(); }
                }
                UiTheme.SameLineIfFits("Copiar mensaje");
                if (_errorPopup != null && ImGui.Button(UiText.Label("Copiar mensaje"))) ImGui.SetClipboardText(Anonymize(_errorPopup));
                ImGui.EndPopup();
            }
            if (!open)
                _errorPopup = null;
        }
        if (_pendingOpen is { } request)
        {
            const string title = "Elegir programa";
            ImGui.OpenPopup(UiText.Label(title));
            bool open = true;
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowSize(Vector2.Min(UiTheme.Px(660, 430), viewport.WorkSize - new Vector2(24)), ImGuiCond.Always);
            ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize / 2, ImGuiCond.Appearing, new(0.5f));
            if (ImGui.BeginPopupModal(UiText.Label(title), ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoDocking))
            {
                ImGui.TextUnformatted(UiText.Get("El archivo contiene varios programas. Elegí cuál iniciar."));
                ImGui.Separator();
                if (ImGui.BeginChild("programas", new(0, -ImGui.GetFrameHeightWithSpacing() - UiTheme.Px(10)), ImGuiChildFlags.Borders))
                {
                    foreach (var c in request.Versions)
                    {
                        ImGui.PushID(c.File.Source);
                        string path = PcDos.DisplayPath(c.File);
                        bool picked = ImGui.Selectable(UiTheme.Fit(path, ImGui.GetContentRegionAvail().X) + "##programa");
                        if (ImGui.IsItemHovered()) UiTheme.Tooltip(path);
                        ImGui.PopID();
                        if (!picked) continue;
                        _pendingOpen = null;
                        ImGui.CloseCurrentPopup();
                        if (!CompleteOpen(request, c)) _host.Paused = _pausedBeforeChoice;
                        break;
                    }
                }
                ImGui.EndChild();
                if (ImGui.Button(UiText.Label("Cancelar"), new(UiTheme.Px(110), 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape))
                {
                    CancelOpen();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            if (!open && _pendingOpen != null) CancelOpen();
        }
    }

    // ------------------------------------------------------------------ debugger
    private void DrawDebugger()
    {
        using var font = UiTheme.FixedWidth();
        ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);
        var m = _host.Machine;
        if (m == null)
            return;
        DrawScreenWindow();
        DrawCpuWindow(m);
        DrawDisassemblyWindow(m);
        DrawMemoryWindow(m);
        DrawStackWindow(m);
        if (_host.Zx != null)
            DrawTapeWindow();
        DrawTestProgramWindow(m);
        _arrangeDebugger = false;
    }

    /// <summary>
    /// First placement of a debugger panel, fitted to the current window; Herramientas > Depuración >
    /// Reorganizar paneles applies it again (undocking) after the panels were moved or the window resized.
    /// </summary>
    private void PlacePanel(DebuggerLayout.Panel panel)
    {
        var viewport = ImGui.GetMainViewport();
        var area = Vector2.Max(new Vector2(240, 180), WorkspaceLayout.Size - new Vector2(8, 8));
        var (position, size) = DebuggerLayout.Place(panel, WorkspaceLayout.Origin + new Vector2(4), area, _host.Zx != null);
        var condition = _arrangeDebugger ? ImGuiCond.Always : ImGuiCond.FirstUseEver;
        if (_arrangeDebugger) ImGui.SetNextWindowDockID(0, ImGuiCond.Always);
        ImGui.SetNextWindowPos(position, condition);
        ImGui.SetNextWindowSize(size, condition);
    }

    private void DrawScreenWindow()
    {
        PlacePanel(DebuggerLayout.Panel.Screen);
        ImGui.Begin(UiText.Label("Pantalla"));
        WorkspaceLayout.KeepOnScreen();
        if (_texW != 0)
        {
            var avail = ImGui.GetContentRegionAvail();
            // Room for the hint below the picture, so it is not hidden under the next panel.
            float hint = ImGui.GetTextLineHeightWithSpacing() * 2 + ImGui.GetStyle().ItemSpacing.Y;
            float s = MathF.Max(0.5f, MathF.Min(avail.X / _texW, (avail.Y - hint) / _texH));
            rlImGui.ImageSize(_screenTex, (int)(_texW * s), (int)(_texH * s));
        }
        _screenFocused = ImGui.IsWindowFocused();
        if (_screenFocused)
            ImGui.TextWrapped(UiText.Get("El teclado va a la máquina emulada."));
        else
            UiTheme.Hint("Hacé clic en la pantalla para usar el teclado en la máquina.");
        ImGui.End();
    }

    private void DrawCpuWindow(IMachine m)
    {
        var cpu = m.Cpu;
        PlacePanel(DebuggerLayout.Panel.Cpu);
        ImGui.Begin(UiText.Label("CPU###cpu"));
        WorkspaceLayout.KeepOnScreen();
        ImGui.TextUnformatted(UiText.Get(cpu.CpuName));
        if (ImGui.Button(UiText.Label((_host.Paused ? "Continuar (" : "Pausa (") + Shortcut("F9") + ")")))
        {
            if (_host.Paused) _host.Continue(); else _host.Pause("Pausa");
        }
        UiTheme.SameLineIfFits("Paso");
        if (ImGui.Button(UiText.Label("Paso"))) _host.StepInto();
        UiTheme.SameLineIfFits("Por encima");
        if (ImGui.Button(UiText.Label("Por encima"))) _host.StepOver();
        UiTheme.SameLineIfFits("Cuadro");
        if (ImGui.Button(UiText.Label("Cuadro"))) _host.StepFrame();
        UiTheme.SameLineIfFits("Reiniciar");
        if (ImGui.Button(UiText.Label("Reiniciar"))) _host.Reset(false);
        ImGui.Separator();
        if (ImGui.BeginTable("regs", 2, ImGuiTableFlags.SizingFixedFit))
        {
            foreach (var r in cpu.GetRegisters())
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(r.Name);
                ImGui.TableNextColumn();
                string fmt = r.Bits == 8 ? r.Value.ToString("X2") : r.Value.ToString("X4");
                if (_host.Paused)
                {
                    if (!_regEdit.TryGetValue(r.Name, out var text))
                        text = fmt;
                    ImGui.SetNextItemWidth(UiTheme.Px(70));
                    if (ImGui.InputText(UiText.Label("##" + r.Name), ref text, (uint)((r.Bits + 3) / 4), ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        if (uint.TryParse(text, NumberStyles.HexNumber, null, out var v))
                            cpu.TrySetRegister(r.Name, v);
                        _regEdit.Remove(r.Name);
                    }
                    else if (ImGui.IsItemActive())
                        _regEdit[r.Name] = text;
                    else
                        _regEdit.Remove(r.Name);
                }
                else
                {
                    ImGui.TextUnformatted(UiText.Get(fmt));
                }
            }
            ImGui.EndTable();
        }
        ImGui.Separator();
        ImGui.TextUnformatted(UiText.Get("Flags (clic para cambiar, en pausa):"));
        int i = 0;
        foreach (var f in cpu.GetFlags())
        {
            // Flags wrap to the next row instead of leaving the panel at large text sizes.
            if (i++ > 0) UiTheme.SameLineIfFits(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(f.Name).X);
            bool set = f.Set;
            if (ImGui.Checkbox(UiText.Label(f.Name + UiText.Get("##flag")), ref set) && _host.Paused)
                cpu.TrySetFlag(f.Name, set);
            if (ImGui.IsItemHovered())
                UiTheme.Tooltip(f.Description);
        }
        ImGui.Separator();
        foreach (var line in cpu.GetInterruptState())
            ImGui.TextUnformatted(UiText.Message(line));
        ImGui.Separator();
        foreach (var line in m.GetMachineInfo())
            ImGui.TextUnformatted(UiText.Get(line));
        ImGui.End();
    }

    private uint DisassemblyStart(IMachine m, uint pc, Func<uint, byte> peek)
    {
        return DisassemblyViewport.Start(pc, m.Memory.AddressBits,
            address => m.Disassembler.Disassemble(peek, address).Length, m.ExecutedMap);
    }

    private void DrawDisassemblyWindow(IMachine m)
    {
        PlacePanel(DebuggerLayout.Panel.Disassembly);
        ImGui.Begin(UiText.Label("Desensamblado"));
        WorkspaceLayout.KeepOnScreen();
        ImGui.Checkbox(UiText.Label("Seguir PC"), ref _followPc);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(UiTheme.Px(70));
        if (ImGui.InputText(UiText.Label("Ir a##dis"), ref _disasmAddr, (uint)((m.Memory.AddressBits + 3) / 4), ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue))
            _followPc = false;
        uint pc = m.Cpu.ProgramCounter;
        Func<uint, byte> peek = m.Memory.Peek;   // one delegate per frame instead of one per decoded instruction
        uint addr = _followPc ? DisassemblyStart(m, pc, peek) : uint.TryParse(_disasmAddr, NumberStyles.HexNumber, null, out var a0) ? a0 : 0;
        UiTheme.Hint("Clic en ( ) para breakpoint. Clic derecho: ejecutar hasta ahí.");
        ImGui.BeginChild("dislist", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        for (int i = 0; i < 48; i++)
        {
            var ins = m.Disassembler.Disassemble(peek, addr);
            bool isPc = addr == pc;
            bool bp = _host.Debug.IsBreakpoint(addr);
            ImGui.PushID((int)addr);
            ImGui.PushStyleColor(ImGuiCol.Text, bp ? UiTheme.Error : UiTheme.Muted);
            if (ImGui.SmallButton(UiText.Label(bp ? "(*)" : "( )")))
                _host.Debug.Toggle(addr);
            ImGui.PopStyleColor();
            ImGui.SameLine();
            string bytes = string.Join(" ", ins.Bytes.Select(b => HexBytes[b]));
            string? sym = m.SymbolFor(addr);
            string line = UiText.Format($"{(isPc ? UiText.Get(">") : UiText.Get(" "))} {addr:X4}  {bytes,-12} {ins.Text}");
            if (sym != null)
            {
                ImGui.TextColored(UiTheme.Accent, UiText.Get(sym + ":"));
                ImGui.SameLine();
            }
            if (isPc)
                ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Highlight);
            ImGui.Selectable(UiText.Label(line), isPc);
            if (isPc)
                ImGui.PopStyleColor();
            if (ImGui.BeginPopupContextItem("ctx"))
            {
                if (ImGui.MenuItem(UiText.Label("Ejecutar hasta aquí"))) _host.RunTo(addr);
                if (ImGui.MenuItem(UiText.Label("Mover PC aquí"))) m.Cpu.ProgramCounter = addr;
                if (ImGui.MenuItem(UiText.Label("Ver en memoria"))) { _memStart = addr & (((1u << m.Memory.AddressBits) - 1) & ~15u); _memAddr = _memStart.ToString("X4"); }
                ImGui.EndPopup();
            }
            ImGui.PopID();
            addr = (uint)((addr + ins.Length) & ((1u << m.Memory.AddressBits) - 1));
        }
        ImGui.EndChild();
        ImGui.End();
    }

    private static readonly string[] HexBytes = Enumerable.Range(0, 256).Select(b => b.ToString("X2")).ToArray();

    private void DrawMemoryWindow(IMachine m)
    {
        PlacePanel(DebuggerLayout.Panel.Memory);
        ImGui.Begin(UiText.Label("Memoria"));
        WorkspaceLayout.KeepOnScreen();
        ImGui.SetNextItemWidth(UiTheme.Px(70));
        if (ImGui.InputText(UiText.Label("Dirección"), ref _memAddr, (uint)((m.Memory.AddressBits + 3) / 4), ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue)
            && uint.TryParse(_memAddr, NumberStyles.HexNumber, null, out var a))
            _memStart = a & (((1u << m.Memory.AddressBits) - 1) & ~15u);
        UiTheme.SameLineIfFits("< página");
        if (ImGui.Button(UiText.Label("< página"))) _memStart = (_memStart - 0x100) & ((1u << m.Memory.AddressBits) - 1);
        UiTheme.SameLineIfFits("página >");
        if (ImGui.Button(UiText.Label("página >"))) _memStart = (_memStart + 0x100) & ((1u << m.Memory.AddressBits) - 1);
        UiTheme.SameLineIfFits("PC");
        if (ImGui.Button(UiText.Label("PC"))) _memStart = m.Cpu.ProgramCounter & (((1u << m.Memory.AddressBits) - 1) & ~15u);
        UiTheme.SameLineIfFits("SP");
        if (ImGui.Button(UiText.Label("SP"))) _memStart = m.Cpu.StackPointer & (((1u << m.Memory.AddressBits) - 1) & ~15u);
        UiTheme.SameLineIfFits("Video");
        if (ImGui.Button(UiText.Label("Video"))) _memStart = m.Id == "pcxt" ? 0xB8000u : m.Id == "invaders" ? 0x2400u : 0x4000u;
        ImGui.TextDisabled(UiText.Get(m.MemoryMapDescription + UiText.Get(". Clic en un byte para editarlo.")));
        ImGui.BeginChild("memgrid", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        uint pc = m.Cpu.ProgramCounter, sp = m.Cpu.StackPointer;
        Span<char> chars = stackalloc char[17];
        for (int row = 0; row < 16; row++)
        {
            uint rowAddr = (uint)((_memStart + row * 16) & ((1u << m.Memory.AddressBits) - 1));
            ImGui.TextUnformatted(UiText.Get(UiText.Format($"{rowAddr:X4} ")));
            for (int col = 0; col < 16; col++)
            {
                uint ad = (uint)((rowAddr + col) & ((1u << m.Memory.AddressBits) - 1));
                byte v = m.Memory.Peek(ad);
                ImGui.SameLine();
                bool highlight = ad == pc || ad == sp;
                if (highlight) ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Highlight);
                ImGui.PushID((int)ad);
                if (ImGui.Selectable(UiText.Label(HexBytes[v]), false, ImGuiSelectableFlags.None, new Vector2(MathF.Ceiling(ImGui.CalcTextSize(UiText.Get("00")).X), 0)))
                {
                    _editMemAddr = ad.ToString("X4");
                    _editMemValue = HexBytes[v];
                    ImGui.OpenPopup(UiText.Label("editmem"));
                }
                if (ImGui.BeginPopup(UiText.Label("editmem")))
                {
                    ImGui.TextUnformatted(UiText.Get(UiText.Get("Nuevo valor en $") + _editMemAddr));
                    ImGui.SetNextItemWidth(UiTheme.Px(50));
                    if (ImGui.InputText("##val", ref _editMemValue, 2, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        if (byte.TryParse(_editMemValue, NumberStyles.HexNumber, null, out var nv) && _editMemAddr != null)
                            m.Memory.Poke(uint.Parse(_editMemAddr, NumberStyles.HexNumber), nv);
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.TextDisabled(UiText.Get("La ROM no se puede modificar."));
                    ImGui.EndPopup();
                }
                ImGui.PopID();
                if (highlight) ImGui.PopStyleColor();
            }
            ImGui.SameLine();
            chars[0] = ' ';
            for (int col = 0; col < 16; col++)
            {
                byte v = m.Memory.Peek((uint)((rowAddr + col) & ((1u << m.Memory.AddressBits) - 1)));
                chars[col + 1] = v is >= 32 and < 127 ? (char)v : '.';
            }
            ImGui.TextUnformatted(UiText.Get(new string(chars)));
        }
        ImGui.EndChild();
        ImGui.End();
    }

    private void DrawStackWindow(IMachine m)
    {
        PlacePanel(DebuggerLayout.Panel.Stack);
        ImGui.Begin(UiText.Label("Pila"));
        WorkspaceLayout.KeepOnScreen();
        uint sp = m.Cpu.StackPointer;
        for (int i = 0; i < 14; i++)
        {
            uint a = (uint)((sp + i * 2) & ((1u << m.Memory.AddressBits) - 1));
            uint v = (uint)(m.Memory.Peek(a) | (m.Memory.Peek((a + 1) & ((1u << m.Memory.AddressBits) - 1)) << 8));
            string? sym = m.SymbolFor(v);
            ImGui.TextUnformatted(UiText.Get(UiText.Format($"{(i == 0 ? UiText.Get("SP>") : UiText.Get("   "))} {a:X4}: {v:X4}{(sym != null ? UiText.Get("  ") + sym : UiText.Get(""))}")));
        }
        ImGui.End();
    }

    private void DrawTapeWindow()
    {
        var zx = _host.Zx!;
        PlacePanel(DebuggerLayout.Panel.Tape);
        ImGui.Begin(UiText.Label("Cinta"));
        WorkspaceLayout.KeepOnScreen();
        var tape = zx.Tape;
        if (!tape.IsLoaded)
        {
            ImGui.TextUnformatted(UiText.Get("No hay cinta. Abrí un .tap o .tzx."));
            ImGui.End();
            return;
        }
        if (ImGui.Button(UiText.Label(tape.IsPlaying ? "Detener (F6)" : "Reproducir (F6)"))) ToggleTape();
        UiTheme.SameLineIfFits("Rebobinar (F7)");
        if (ImGui.Button(UiText.Label("Rebobinar (F7)"))) tape.Rewind();
        UiTheme.SameLineIfFits("LOAD \"\"");
        if (ImGui.Button(UiText.Label("LOAD \"\""))) zx.TypeLoadCommand();
        ImGui.ProgressBar((float)tape.Progress, new Vector2(-1, 0), UiText.Format($"{tape.Progress * 100:F0}%"));
        int current = tape.CurrentBlock;
        foreach (var b in tape.Tape!.Blocks)
            ImGui.Selectable(UiText.Label(UiText.Format($"{b.Index,3}  {b.Description}")), b.Index == current);
        ImGui.End();
    }

    private void DrawTestProgramWindow(IMachine m)
    {
        PlacePanel(DebuggerLayout.Panel.TestProgram);
        ImGui.Begin(UiText.Label("Programa de prueba"));
        WorkspaceLayout.KeepOnScreen();
        ImGui.TextWrapped(UiText.Get(UiText.Format($"Cargá bytes de {m.Cpu.CpuName} o un .bin en memoria para estudiar su ejecución paso a paso.")));
        ImGui.SetNextItemWidth(UiTheme.Px(70));
        ImGui.InputText(UiText.Label("Dirección##test"), ref _testAddr, (uint)((m.Memory.AddressBits + 3) / 4), ImGuiInputTextFlags.CharsHexadecimal);
        ImGui.Checkbox(UiText.Label("Poner PC en esa dirección"), ref _testSetPc);
        ImGui.InputText(UiText.Label("Bytes (hex)"), ref _testHex, 512);
        uint addr = uint.TryParse(_testAddr, NumberStyles.HexNumber, null, out var ta) ? ta & ((1u << m.Memory.AddressBits) - 1) : 0x8000;
        // Bad input or a program that does not fit only shows a notice: it used to close the program.
        if (ImGui.Button(UiText.Label("Cargar bytes")))
        {
            if (TestProgram.ParseHex(_testHex) is { } bytes) TryLoadBinary(() => bytes, addr);
            else _errorPopup = "Bytes inválidos: usá pares hexadecimales separados por espacios.";
        }
        ImGui.InputText(UiText.Label("Archivo .bin"), ref _testPath, 512);
        if (ImGui.Button(UiText.Label("Cargar archivo")))
        {
            if (File.Exists(_testPath))
                TryLoadBinary(() => File.ReadAllBytes(_testPath), addr);
            else
                _errorPopup = UiText.Get("No existe el archivo ") + _testPath;
        }
        ImGui.TextDisabled(UiText.Get("Escribí bytes en hexadecimal o cargá un archivo binario."));
        ImGui.End();
    }

    private void TryLoadBinary(Func<byte[]> read, uint address)
    {
        try { _host.LoadBinary(read(), address, _testSetPc); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        { _errorPopup = UiText.Get("No se pudo cargar el programa: ") + error.Message; }
    }
}
