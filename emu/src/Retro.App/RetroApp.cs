using System.Globalization;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Retro.Core;
using Retro.Machines;

namespace Retro.App;

/// <summary>Main window: play mode (just the screen) and debugger mode (panels).</summary>
public sealed class RetroApp
{
    private readonly EmulatorHost _host = new();
    private readonly InputMapper _input = new();
    private readonly string _romDir;
    private readonly FileBrowser _browser;
    private readonly Retro.Ai.AiSettings _ai = Retro.Ai.AiSettings.Load();
    private readonly StudioWindow _studio;
    private readonly CreatorWindow _creator;
    private readonly GameLibrary _library;
    private int _libraryRevision;
    private readonly HomeScreen _home;
    private bool _pausedBeforeLibrary;
    private bool _advancedTools;
    private string _originalSearch = "";
    private string? _autoStudio;
    private readonly CodeWindow _code;
    private bool _wasFusion;
    private bool _muted;
    private Texture2D _screenTex;
    private int _texW, _texH;
    private int[] _rgba = Array.Empty<int>();
    private AudioStream _stream;
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
    private List<MachineCatalog.Candidate>? _choices;
    private List<MachineCatalog.Candidate> _versions = new();   // versions of the last opened game
    private bool _autoType = true;
    private float _scale = 2;
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
    private double _elapsed;

    public RetroApp(string[] args)
    {
        // the system firmware: a "firmware" folder (release packages) or a "roms" folder that
        // has the Spectrum ROM in it (so a games folder called "ROMs" is never mistaken for it)
        _romDir = FindFolder("firmware") ?? FindFolderWith("roms", "48.rom") ?? Path.Combine(AppContext.BaseDirectory, "firmware");
        _library = new GameLibrary { AutoOrganize = !args.Contains("--no-organize"), InUse = _host.UsesMedia };
        string games = FindGamesFolder() ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _library.Rescan(games);
        Retro.Studio.StudioLibrary.ExportBuiltIns();
        _browser = new FileBrowser(games);
        _studio = new StudioWindow(_host, _romDir, games, _ai);
        _code = new CodeWindow(_host, _ai, ShowInDebugger, OpenFusionStudio);
        _creator = new CreatorWindow(_romDir, _ai, OpenFile, OpenInspiredCreator);
        _home = new HomeScreen(_library, OpenFile, PlayFusion, _browser.Open, OpenFusionStudio, OpenCreator,
            visible =>
            {
                if (visible) { _pausedBeforeLibrary = _host.Paused; _host.Paused = true; }
                else _host.Paused = _pausedBeforeLibrary;
            }, () => _host.LoadedMedia != null || _host.Fusion != null);
        Raylib.SetTargetFPS(Math.Max(60, Raylib.GetMonitorRefreshRate(Raylib.GetCurrentMonitor())));
        // The audio device pulls samples through a callback on its own thread, so the
        // emulator can pace itself on the real consumption rate.
        Raylib.SetAudioStreamBufferSizeDefault(512);
        _stream = Raylib.LoadAudioStream(44100, 16, 1);
        s_queue = _host.Audio;
        unsafe
        {
            Raylib.SetAudioStreamCallback(_stream, &AudioCallback);
        }
        Raylib.PlayAudioStream(_stream);
        try
        {
            _host.Start(MachineCatalog.Get("zx48"), _romDir);
        }
        catch (Exception e)
        {
            _errorPopup = e.Message + "\n\nCopiá la ROM a la carpeta firmware/ junto al programa.";
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
            else if (args[i] == "--studio" && i + 1 < args.Length)
                _autoStudio = args[++i];
            else if (args[i] == "--preparar" && i + 1 < args.Length)
                _autoPrepare = args[++i];
            else if (args[i] == "--crear-window")
                OpenCreator();
            else if (args[i] == "--studio-window")
                OpenFusionStudio();
            else if (args[i] == "--code")
                _code.Open();
            else if (args[i] == "--mods-window")
                _code.OpenModifications();
            else if (args[i] == "--mute")
                _muted = true;
            else if (args[i] != "--hidden" && File.Exists(args[i]))
            {
                OpenFile(args[i]);
                openedFile = true;
            }
        }
        if (!openedFile && !args.Any(a => a is "--studio" or "--preparar" or "--crear-window" or "--studio-window" or "--code" or "--mods-window" or "--debug"))
            _home.Open();
    }

    /// <summary>The user's game folder ("ROMs", exact case: "roms" holds the firmware).</summary>
    private static string? FindGamesFolder() => PlayerFolders.Find("ROMs");

    private void ShowInDebugger(uint address)
    {
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
    }

    public void Shutdown()
    {
        _library.Dispose();
        _home.Dispose();
        _code.Dispose();
        if (_texW != 0)
            Raylib.UnloadTexture(_screenTex);
        Raylib.UnloadAudioStream(_stream);
    }

    // ------------------------------------------------------------------ frame
    public void Frame(float dt)
    {
        HandleDroppedFiles();
        _library.Poll();
        if (_libraryRevision != _library.Revision)
        {
            _libraryRevision = _library.Revision;
            _studio.NotifyLibraryChanged(_library.Folder);
        }
        bool imguiKeyboard = ImGui.GetIO().WantTextInput || _home.IsOpen;
        HandleHotkeys(ImGui.GetIO().WantTextInput);
        _code.Update();
        var m = _host.Machine;
        if (m != null)
        {
            if (m.Id != _lastMachineId)
                OnMachineChanged(m);
            bool keyboardToMachine = !imguiKeyboard && (!_debugMode || _screenFocused);
            if (_host.Fusion is { } fusion)
            {
                if (!_wasFusion)
                    _input.ReleaseAll(m);
                _input.UpdateFusion(fusion, keyboardToMachine && !_host.Paused);
            }
            else
                _input.Update(m, keyboardToMachine && !_host.Paused);
            _wasFusion = _host.Fusion != null;
            _host.Update(dt);
            s_mute = _muted || _host.IsFastForwarding || _host.Paused;
            UploadScreen();
        }
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

        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(18, 18, 22, 255));
        if (!_home.IsOpen && !_debugMode && _texW != 0)
            DrawPlayScreen();
        rlImGui.Begin(dt);
        DrawMenu();
        if (_debugMode && !_home.IsOpen)
            DrawDebugger();
        DrawStatusBar();
        _home.Draw();
        _browser.Draw(OpenFile);
        if (!_home.IsOpen)
        {
            _studio.Draw();
            _creator.Draw();
            _code.Draw();
        }
        DrawPopups();
        AiConnectionWindow.Draw(_ai);
        rlImGui.End();
        Raylib.EndDrawing();

        _elapsed += dt;
        if (_autotestSeconds >= 0 && (int)_elapsed != (int)(_elapsed - dt))
            Console.WriteLine($"t={_elapsed:F1}s fps={Raylib.GetFPS()} velocidad={_host.EffectiveSpeed * 100:F0}% audio_en_cola={_host.Audio.Count} cuadros={_host.Machine?.FrameCount}");
        if (_autotestSeconds >= 0 && _elapsed >= _autotestSeconds && _autotestShot != null)
        {
            Raylib.TakeScreenshot(_autotestShot);
            foreach (var group in _library.Entries.GroupBy(e => e.Machine.Name))
                Console.WriteLine($"biblioteca: {group.Key}: {string.Join(", ", group.Select(e => e.Name))}");
            foreach (var e in _library.Entries.Where(e => FusionsOf(e.ProfileId).Count > 0))
                Console.WriteLine($"con fusión: {e.Name}: {string.Join(", ", FusionsOf(e.ProfileId))}");
            QuitRequested = true;
        }
    }

    /// <summary>Per-machine defaults for the debugger panels.</summary>
    private void OnMachineChanged(IMachine m)
    {
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
    private static volatile bool s_mute;

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static unsafe void AudioCallback(void* buffer, uint frames)
    {
        var span = new Span<short>(buffer, (int)frames);
        var q = s_queue;
        if (q == null)
        {
            span.Clear();
            return;
        }
        q.Pop(span);   // consumed even when muted: the queue level paces the emulation
        if (s_mute)
            span.Clear();
    }

    private void UploadScreen()
    {
        var video = _host.CurrentVideo;
        if (video == null)
            return;
        EnsureScreenTexture(video.Width, video.Height);
        var src = video.Pixels;
        for (int i = 0; i < src.Length; i++)
        {
            int c = src[i];
            // ARGB -> RGBA bytes in memory (little endian: R G B A)
            _rgba[i] = unchecked((int)0xFF000000) | ((c & 0xFF) << 16) | (c & 0xFF00) | ((c >> 16) & 0xFF);
        }
        Raylib.UpdateTexture(_screenTex, _rgba);
    }

    private void DrawPlayScreen()
    {
        float top = 22, bottom = 24;
        // the AI studio panel sits on the right while you play: the game uses the rest
        float reserved = Math.Max(_code.RightReserve(Raylib.GetScreenWidth()),
            Math.Max(_studio.RightReserve(Raylib.GetScreenWidth()), _creator.RightReserve(Raylib.GetScreenWidth())));
        float w = Math.Max(1, Raylib.GetScreenWidth() - reserved), h = Raylib.GetScreenHeight() - top - bottom;
        float s = MathF.Min(w / _texW, h / _texH);
        if (_scale > 0)
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
        _home.Close();
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
                _errorPopup = $"{fileName} está vacío (o es un zip sin archivos).";
                return;
            }
            if (files.Count == 1 && MachineCatalog.Unsupported(files[0]) is { } why)
            {
                _errorPopup = $"{fileName} {why}";
                return;
            }
            if (Retro.Systems.Arcade.SpaceInvadersSound.IsSampleSet(files))
            {
                _errorPopup = _host.InstallInvadersSamples(files, _romDir);
                return;
            }
            if (MachineCatalog.IdentifySet(files) is { } board)
            {
                LaunchArcade(board.Machine, files, Path.GetDirectoryName(path));
                return;
            }
            var candidates = files.SelectMany(MachineCatalog.Identify)
                .OrderByDescending(c => c.Machine.Status == MachineStatus.Available)
                .ThenByDescending(c => c.Confidence)
                .ThenBy(c => MediaArchive.DumpScore(c.File.Name))
                .ToList();
            if (candidates.Count == 0)
            {
                string inside = files.Count > 1 ? $" Adentro hay: {string.Join(", ", files.Take(8).Select(f => Path.GetFileName(f.Name)))}." : "";
                _errorPopup = $"No reconozco {fileName}: no coincide con el formato de ninguna máquina que conozco.{inside}\n" +
                              "Formatos que reconozco: Spectrum (.tap .tzx .z80 .sna), arcade (zip de MAME), C64 (.t64 .prg .d64 .crt), " +
                              "Atari 8 bits (.atr .xex), Apple II (.dsk .woz), Amstrad CPC, PC-88 (.d88), DOS (.exe .com), Atari ST (.st .msa), Amiga (.adf).";
                return;
            }
            var playable = candidates.Where(c => c.Machine.Status == MachineStatus.Available && c.Confidence >= 50 && MachineCatalog.NotLoadableYet(c) == null).ToList();
            if (playable.Count == 0)
            {
                var best = candidates[0];
                _errorPopup = MachineCatalog.NotLoadableYet(best) is { } notYet
                    ? $"{fileName} es para {best.Machine.Name} (CPU {best.Machine.Cpu}): {best.Reason}.\nEsa máquina ya anda, pero {notYet}."
                    : $"{fileName} es para {best.Machine.Name} (CPU {best.Machine.Cpu}): {best.Reason}.\n" +
                      "Esa máquina todavía no está implementada; está en el plan de trabajo (siguen Atari 800 y Apple II, con la misma CPU 6502).\n" +
                      "En Máquina vas a ver qué juegos de tu carpeta corren hoy.";
                return;
            }
            // Several dumps or formats of the game: the cleanest dump starts right away and
            // the others stay in Archivo > Otras versiones.
            _versions = playable
                .GroupBy(c => c.File.Name).Select(g => g.First())
                .OrderBy(c => MediaArchive.DumpScore(c.File.Name))
                .ThenBy(c => FormatPreference(c.File.Extension))
                .ToList();
            Launch(_versions[0]);
            if (_versions.Count > 1)
                _host.Status += $"  |  Había {_versions.Count} versiones: cargué la recomendada (las otras, en Archivo > Otras versiones).";
        }
        catch (Exception e)
        {
            _errorPopup = $"No se pudo abrir {fileName}: {e.Message}";
        }
    }

    /// <summary>Tapes load the way the original did; snapshots skip the loading.</summary>
    private static int FormatPreference(string ext) => ext switch
    {
        ".tzx" => 0, ".tap" => 1, ".z80" => 2, ".sna" => 3, _ => 9,
    };

    private void OpenFusionStudio() { _home.Close(); _studio.Open(); }
    private void OpenCreator() { _home.Close(); _creator.Open(); }
    private void OpenInspiredCreator() { _home.Close(); _studio.Open(inspired: true); }
    private void PlayFusion(string id) { _home.Close(); _studio.PlayById(id); }
    private void OpenModifications() { _home.Close(); _code.OpenModifications(); }

    private void Launch(MachineCatalog.Candidate c)
    {
        _home.Close();
        try
        {
            if (_host.Machine != null)
                _input.ReleaseAll(_host.Machine);
            if (c.Machine.Id == "nes")
                _host.StartNes(c.Machine, c.File);     // a cartridge: a fresh machine for each game
            else
            {
                if (_host.Info?.Id != c.Machine.Id || _host.Fusion != null)
                    _host.Start(c.Machine, _romDir);
                _host.Load(c.File, _autoType);
            }
            _host.Status = $"{c.Machine.Name} (CPU {c.Machine.Cpu}), {c.Reason}  |  {_host.Status}";
        }
        catch (Exception e)
        {
            _errorPopup = e is FileNotFoundException
                ? $"No se pudo arrancar {c.Machine.Name}: {e.Message}\nEl firmware va en la carpeta firmware/ junto al programa."
                : $"No se pudo cargar {c.File.Name} en {c.Machine.Name}: {e.Message}";
        }
    }

    private void LaunchArcade(MachineInfo info, IReadOnlyList<MediaFile> files, string? folder)
    {
        _home.Close();
        try
        {
            if (_host.Machine != null)
                _input.ReleaseAll(_host.Machine);
            _host.StartInvaders(info, files, _romDir, folder);
        }
        catch (Exception e)
        {
            _errorPopup = $"No se pudo encender {info.Name}: {e.Message}";
        }
    }

    // ------------------------------------------------------------------ hotkeys
    private void HandleHotkeys(bool textInput)
    {
        if (textInput)
            return;
        if (Raylib.IsKeyPressed(KeyboardKey.F12))
        {
            _home.Close();
            _debugMode = !_debugMode;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.F11))
            Raylib.ToggleFullscreen();
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.O)) _browser.Open();
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.K) && _host.Machine != null)
        {
            _home.Close();
            _code.Open();
        }
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.L)) _home.Open();
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.M) && _host.Machine != null) OpenModifications();
        if (_home.IsOpen) return;
        if (_host.Machine == null)
            return;
        // the C64 has its own F1 F3 F5 F7 keys (games say "press F1"): there quick load is
        // F4 and reset is in the Machine menu
        bool machineFKeys = _host.C64 != null && _host.Fusion == null;
        if (Raylib.IsKeyPressed(KeyboardKey.F2)) _host.QuickSave();
        if (Raylib.IsKeyPressed(KeyboardKey.F4) || (!machineFKeys && Raylib.IsKeyPressed(KeyboardKey.F3))) _host.QuickLoad();
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

    // ------------------------------------------------------------------ menu
    private void DrawMenu()
    {
        if (!ImGui.BeginMainMenuBar())
            return;
        if (ImGui.MenuItem("Inicio")) _home.Open();
        if (ImGui.BeginMenu("Modificar", _host.Machine != null))
        {
            if (ImGui.MenuItem("Variables y cambios divertidos...", "Ctrl+M")) OpenModifications();
            if (ImGui.MenuItem("Buscar una variable...")) { _home.Close(); _code.OpenFinder(); }
            if (ImGui.MenuItem("Analizar el código y las rutinas...")) { _home.Close(); _code.Open(); }
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Archivo"))
        {
            if (ImGui.MenuItem("Abrir juego o programa...", "Ctrl+O")) _browser.Open();
            if (ImGui.BeginMenu("Otras versiones del último juego", _versions.Count > 1))
            {
                ImGui.TextDisabled("La primera es el volcado más limpio. [a] alternativa, [h] hack, [t] trucos, [cr] crackeado.");
                foreach (var v in _versions)
                    if (ImGui.MenuItem($"{Path.GetFileName(v.File.Name)}##{v.File.Name}"))
                        Launch(v);
                ImGui.EndMenu();
            }
            ImGui.MenuItem("Escribir LOAD \"\" al abrir cintas", "", ref _autoType);
            ImGui.Separator();
            if (ImGui.MenuItem("Guardar estado rápido", "F2", false, _host.CanUseStates)) _host.QuickSave();
            if (ImGui.MenuItem("Cargar estado rápido", _host.C64 != null ? "F4" : "F3", false, _host.CanUseStates)) _host.QuickLoad();
            if (ImGui.BeginMenu("Ranuras de estado", _host.CanUseStates))
            {
                string id = _host.Machine?.Id ?? "none";
                for (int i = 1; i <= 5; i++)
                {
                    string file = StatePath(id, i);
                    if (ImGui.MenuItem($"Guardar en ranura {i}")) TryStateAction(() => _host.SaveStateToFile(file));
                    if (ImGui.MenuItem($"Cargar ranura {i}", "", false, File.Exists(file))) TryStateAction(() => _host.LoadStateFromFile(file));
                }
                ImGui.EndMenu();
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Salir")) QuitRequested = true;
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Máquina"))
        {
            DrawMachineList();
            ImGui.Separator();
            if (ImGui.MenuItem("Reiniciar", "F5")) _host.Reset(false);
            if (ImGui.MenuItem("Reinicio total (borra RAM)", "Shift+F5")) _host.Reset(true);
            if (ImGui.MenuItem(_host.Paused ? "Continuar" : "Pausa", "F9", false, !_home.IsOpen))
            {
                if (_host.Paused) _host.Continue(); else _host.Pause("Pausa");
            }
            bool turbo = _host.Turbo;
            if (ImGui.MenuItem("Turbo (máxima velocidad)", "F8", ref turbo)) _host.Turbo = turbo;
            if (_host.Zx != null)
            {
                bool tl = _host.TurboWhileLoading;
                if (ImGui.MenuItem("Acelerar mientras carga la cinta", "", ref tl)) _host.TurboWhileLoading = tl;
                if (ImGui.BeginMenu("Joystick en flechas / gamepad"))
                {
                    foreach (JoystickMode jm in Enum.GetValues<JoystickMode>())
                    {
                        string name = jm switch
                        {
                            JoystickMode.Kempston => "Kempston (Alt = disparo)",
                            JoystickMode.Cursor => "Cursor (5 6 7 8 0)",
                            _ => "Sinclair 1 (6 7 8 9 0)",
                        };
                        if (ImGui.MenuItem(name, "", _input.Joystick == jm)) _input.Joystick = jm;
                    }
                    ImGui.EndMenu();
                }
            }
            if (_host.C64 is { } c64)
            {
                if (ImGui.BeginMenu("Joystick en flechas / gamepad"))
                {
                    if (ImGui.MenuItem("Puerto 2 (la mayoría de los juegos)", "", _input.C64Port == 2)) _input.C64Port = 2;
                    if (ImGui.MenuItem("Puerto 1", "", _input.C64Port == 1)) _input.C64Port = 1;
                    ImGui.TextDisabled("Si el personaje no se mueve, probá el otro puerto.");
                    ImGui.EndMenu();
                }
                if (ImGui.MenuItem("Pulsar RUN/STOP + RESTORE (vuelve al BASIC)"))
                    c64.QueueKeys(8, "RUN/STOP", "RESTORE");
                if (c64.OpenRoms && c64.UnhandledKernalCalls.Count > 0)
                    ImGui.TextDisabled($"El juego llamó a rutinas internas del KERNAL original: {string.Join(", ", c64.UnhandledKernalCalls.Select(a => $"${a:X4}"))}.");
            }
            if (_host.Invaders is { } inv && ImGui.BeginMenu("DIP switches de la placa"))
            {
                for (int ships = 3; ships <= 6; ships++)
                    if (ImGui.MenuItem($"{ships} naves por partida", "", inv.Ships == ships)) inv.Ships = ships;
                ImGui.Separator();
                bool bonus = inv.BonusAt1000;
                if (ImGui.MenuItem("Nave extra a los 1000 puntos (si no, 1500)", "", ref bonus)) inv.BonusAt1000 = bonus;
                bool coin = inv.ShowCoinInfo;
                if (ImGui.MenuItem("Mostrar información de monedas", "", ref coin)) inv.ShowCoinInfo = coin;
                ImGui.TextDisabled("Los cambios se leen al empezar la próxima partida.");
                ImGui.EndMenu();
            }
            ImGui.EndMenu();
        }
        if (_host.Zx is { } zx && ImGui.BeginMenu("Cinta"))
        {
            var tape = zx.Tape;
            if (ImGui.MenuItem(tape.IsPlaying ? "Detener" : "Reproducir", "F6", false, tape.IsLoaded)) ToggleTape();
            if (ImGui.MenuItem("Rebobinar", "F7", false, tape.IsLoaded)) tape.Rewind();
            if (ImGui.MenuItem("Expulsar", "", false, tape.IsLoaded)) tape.Eject();
            bool auto = zx.AutoPlayTape;
            if (ImGui.MenuItem("Arrancar sola al ejecutar LOAD", "", ref auto)) zx.AutoPlayTape = auto;
            if (ImGui.MenuItem("Escribir LOAD \"\" ahora")) zx.TypeLoadCommand();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Fusión"))
        {
            if (ImGui.MenuItem("Fusionar conservando un juego base...")) OpenFusionStudio();
            if (ImGui.MenuItem("Detener el juego actual", "", false, _host.Fusion != null)) _host.StopFusion();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Crear"))
        {
            if (ImGui.MenuItem("Desde cero (C64 o Spectrum)...")) OpenCreator();
            if (ImGui.MenuItem("A partir de tus ROMs...")) OpenInspiredCreator();
            ImGui.EndMenu();
        }
        if ((_advancedTools || _debugMode) && ImGui.BeginMenu("Código"))
        {
            if (ImGui.MenuItem("Ver el código del juego (rutinas, IA, cambios)...", "Ctrl+K", false, _host.Machine != null)) _code.Open();
            ImGui.EndMenu();
        }
        if ((_advancedTools || _debugMode) && ImGui.BeginMenu("Depurar"))
        {
            if (ImGui.MenuItem("Modo depurador", "F12", _debugMode)) _debugMode = !_debugMode;
            if (ImGui.MenuItem("Paso (una instrucción)", "F10")) { _host.StepInto(); _debugMode = true; }
            if (ImGui.MenuItem("Paso por encima (CALL, bucles)", "Shift+F10")) { _host.StepOver(); _debugMode = true; }
            if (ImGui.MenuItem("Avanzar un cuadro")) { _host.StepFrame(); _debugMode = true; }
            if (ImGui.MenuItem("Borrar todos los breakpoints")) _host.Debug.Clear();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Ver"))
        {
            ImGui.MenuItem("Herramientas avanzadas (código y depurador)", "", ref _advancedTools);
            if (ImGui.MenuItem("Pantalla completa", "F11")) Raylib.ToggleFullscreen();
            bool integer = _scale > 0;
            if (ImGui.MenuItem("Escala entera (píxeles nítidos)", "", ref integer)) _scale = integer ? 2 : 0;
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Ayuda"))
        {
            if (ImGui.MenuItem("Conectar IA...")) AiConnectionWindow.Open(_ai);
            if (ImGui.BeginMenu("Abrir y jugar"))
            {
                ImGui.TextUnformatted("En Inicio elegí una máquina o Todos los juegos y pulsá Jugar.");
                ImGui.TextUnformatted("También podés arrastrar un archivo a la ventana.");
                ImGui.TextUnformatted("Hacé clic en la pantalla para usar el teclado.");
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Controles de la partida"))
            {
                DrawControlsHelp();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Atajos"))
            {
                ImGui.TextUnformatted("Ctrl+L: inicio    Ctrl+M: modificar");
                ImGui.TextUnformatted(_host.Fusion != null ? "Los estados guardados están disponibles en originales."
                    : _host.C64 != null ? "F2: guardar estado    F4: cargar estado" : "F2: guardar estado    F3: cargar estado");
                ImGui.TextUnformatted("F8: turbo    F9: pausa    F11: pantalla completa");
                ImGui.TextUnformatted("Ctrl+K: código    F10: paso    F12: depurador");
                ImGui.EndMenu();
            }
            ImGui.EndMenu();
        }
        ImGui.EndMainMenuBar();
    }

    private void DrawControlsHelp()
    {
        if (_host.Fusion is Retro.Studio.StudioSession fusion)
        {
            ImGui.TextWrapped(fusion.Game.HowToPlay);
            return;
        }
        switch (_host.Machine?.Id)
        {
            case "zx48":
                ImGui.TextUnformatted("Teclado: letras y números del juego.");
                ImGui.TextUnformatted("Shift: CAPS SHIFT    Ctrl: SYMBOL SHIFT    Backspace: DELETE");
                ImGui.TextUnformatted("Joystick: flechas y Alt. Elegí el tipo en Máquina.");
                break;
            case "c64":
                ImGui.TextUnformatted("Joystick: flechas y Alt o Ctrl derecho.");
                ImGui.TextUnformatted("Si no responde, cambiá el puerto en Máquina > Joystick.");
                ImGui.TextUnformatted("Esc: RUN/STOP    RePág: RESTORE    Ctrl izquierdo: C=    Tab: CTRL");
                ImGui.TextUnformatted("F1, F3, F5 y F7 son las teclas del C64.");
                break;
            case "nes":
                ImGui.TextUnformatted("Flechas: mover    X: A    Z: B");
                ImGui.TextUnformatted("Enter: Start    Shift derecho: Select");
                break;
            case "invaders":
                ImGui.TextUnformatted("Flechas o A/D: mover    Espacio: disparar");
                ImGui.TextUnformatted("5 o C: moneda    1/2: empezar    T: tilt");
                break;
            default:
                ImGui.TextUnformatted("Abrí un juego para ver sus controles.");
                break;
        }
    }

    /// <summary>
    /// Machines grouped by CPU. Each one shows what it needs and the games of the user's
    /// folder that it runs (click to play); planned machines say which of the user's games
    /// are waiting for them.
    /// </summary>
    private void DrawMachineList()
    {
        DrawOriginalGames();
        DrawFusedGames();
        ImGui.Spacing();
        string? lastCpu = null;
        foreach (var mi in MachineCatalog.All)
        {
            if (mi.Cpu != lastCpu)
            {
                if (lastCpu != null)
                    ImGui.Spacing();
                ImGui.TextDisabled("CPU " + mi.Cpu);
                lastCpu = mi.Cpu;
            }
            var games = _library.For(mi.Id).ToList();
            bool current = _host.Info?.Id == mi.Id && _host.Fusion == null;
            string label = mi.Status == MachineStatus.Available
                ? $"   {mi.Name}{(games.Count > 0 ? $"  ({games.Count} juego{(games.Count > 1 ? "s" : "")})" : "")}"
                : $"   {mi.Name} - próximamente{(games.Count > 0 ? $"  (tenés {games.Count} juego{(games.Count > 1 ? "s" : "")} para ella)" : "")}";
            if (current)
                label += "  <- encendida";
            if (!ImGui.BeginMenu(label + "##" + mi.Id))
                continue;
            if (mi.Needs.Length > 0)
                ImGui.TextDisabled(mi.Needs);
            if (mi.Status == MachineStatus.Available)
            {
                if (mi.Id is "zx48" or "c64" && ImGui.MenuItem("Encender sin juego"))
                {
                    try { _home.Close(); _host.Start(mi, _romDir); }
                    catch (Exception e) { _errorPopup = e.Message; }
                }
                if (games.Count == 0)
                    ImGui.TextDisabled(_library.Scanning ? "Buscando juegos en tu carpeta..." : "No encontré juegos para esta máquina en tu carpeta.");
                else
                {
                    ImGui.Separator();
                    ImGui.TextDisabled("Juegos de tu carpeta (clic para jugar):");
                    foreach (var g in games)
                    {
                        if (g.NotYet != null)
                            ImGui.TextDisabled($"{g.Name}: {g.NotYet}");
                        else
                        {
                            var fusions = FusionsOf(g.ProfileId);
                            string tag = fusions.Count > 0 ? "  · también tiene fusión" : "";
                            if (ImGui.MenuItem(g.Name + tag + "##" + g.Path, "", false, !_library.Scanning))
                                OpenFile(g.Path);
                        }
                    }
                }
            }
            else
            {
                ImGui.TextDisabled($"Todavía no está implementada (CPU {mi.Cpu}). Está en el plan de trabajo.");
                if (games.Count > 0)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled("Estos juegos de tu carpeta la van a usar:");
                    foreach (var g in games)
                        ImGui.TextDisabled($"   {g.Name}  ({g.Reason})");
                }
            }
            ImGui.EndMenu();
        }
        var others = _library.Entries.Where(e => MachineCatalog.All.All(m => m.Id != e.Machine.Id)).ToList();
        if (others.Count > 0 && ImGui.BeginMenu("   Otras máquinas que piden tus juegos"))
        {
            foreach (var g in others)
                ImGui.TextDisabled($"{g.Name}: {g.Machine.Name} ({g.Machine.Cpu}), {g.Reason}");
            ImGui.EndMenu();
        }
        ImGui.Spacing();
        if (ImGui.MenuItem(_library.Scanning ? "Buscando juegos..." : "Volver a buscar en la carpeta de juegos", "", false, !_library.Scanning))
            _library.Rescan(_library.Folder);
        ImGui.TextDisabled("Abrir cualquier archivo (Archivo > Abrir) detecta la máquina sola.");
    }

    /// <summary>Original files have their own searchable entry, independent of saved fusions.</summary>
    private void DrawOriginalGames()
    {
        if (!ImGui.BeginMenu("   Juegos originales##originales")) return;
        ImGui.SetNextItemWidth(300);
        ImGui.InputTextWithHint("##buscar-original", "Buscar por nombre", ref _originalSearch, 150);
        ImGui.Separator();
        var games = _library.Originals(_originalSearch).ToList();
        if (games.Count == 0)
            ImGui.TextDisabled(_library.Scanning ? "Buscando juegos..." : "No encontré originales con ese nombre.");
        foreach (var g in games)
        {
            bool playable = g.NotYet == null && g.Confidence >= 50 && !_library.Scanning;
            if (ImGui.MenuItem($"{g.Name} ({g.Machine.Name})##original-{g.Path}", "", false, playable))
                OpenFile(g.Path);
            if (!playable && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(g.NotYet ?? g.Reason);
        }
        ImGui.EndMenu();
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

    /// <summary>
    /// "Juegos fusionados": every studio game, with the games it
    /// mixes; the ones whose games are all in the user's folder play with a click.
    /// </summary>
    private void DrawFusedGames()
    {
        var have = _library.Entries.Where(e => e.ProfileId != null).Select(e => e.ProfileId!).ToHashSet();
        var studio = StudioGames();
        int ready = studio.Count(s => s.Games.All(g => have.Contains(g.Profile)));
        if (!ImGui.BeginMenu($"   Juegos fusionados  ({ready} para jugar)##fusionados"))
            return;
        string Names(IEnumerable<Retro.Fusion.RecipeGame> games) =>
            string.Join(" + ", games.Select(g => Retro.Fusion.ProfileLibrary.Find(g.Profile)?.Name ?? g.Profile));
        string Missing(IEnumerable<Retro.Fusion.RecipeGame> games) =>
            string.Join(", ", games.Where(g => !have.Contains(g.Profile)).Select(g => Retro.Fusion.ProfileLibrary.Find(g.Profile)?.Name ?? g.Profile));
        if (studio.Count == 0)
            ImGui.TextDisabled("   Ninguno todavía: Fusión > Crear juego fusionado con IA.");
        foreach (var s in studio)
        {
            if (s.Games.All(g => have.Contains(g.Profile)))
            {
                if (ImGui.MenuItem($"{s.Title}   ({Names(s.Games)})##estudio-{s.Id}"))
                    PlayFusion(s.Id);
            }
            else
                ImGui.TextDisabled($"   {s.Title}: falta {Missing(s.Games)} en tu carpeta");
        }
        ImGui.Separator();
        ImGui.EndMenu();
    }

    private static string StatePath(string machineId, int slot)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "states");
        return Path.Combine(dir, $"{machineId}_slot{slot}.rfstate");
    }

    private void TryStateAction(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { _errorPopup = "No se pudo guardar o cargar el estado: " + error.Message; }
    }

    private void DrawStatusBar()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X, vp.Pos.Y + vp.Size.Y - 24));
        ImGui.SetNextWindowSize(new Vector2(vp.Size.X, 24));
        ImGui.Begin("##status", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoFocusOnAppearing);
        if (_home.IsOpen)
        {
            ImGui.TextUnformatted(_library.Scanning ? "Buscando juegos..." : $"{_home.GameCount} juegos en la biblioteca");
            ImGui.End();
            return;
        }
        var m = _host.Machine;
        string speed = m == null ? "" : _host.Paused ? "PAUSA" : _host.IsFastForwarding ? $"rápido x{_host.EffectiveSpeed:F0}" : $"{_host.EffectiveSpeed * 100:F0}%";
        string extra = "";
        if (_host.Zx is { } zx)
            extra = !zx.Tape.IsLoaded ? "  |  sin cinta" : zx.Tape.IsPlaying ? $"  |  cinta en marcha, bloque {zx.Tape.CurrentBlock} ({zx.Tape.Progress * 100:F0}%)" : zx.Tape.AtEnd ? "  |  cinta al final" : "  |  cinta detenida";
        if (_host.C64 is { OpenRoms: true } c64 && c64.UnhandledKernalCalls.Count > 0)
            extra = $"  |  usa rutinas internas del KERNAL original ({c64.UnhandledKernalCalls.Count}): si falla, poné las ROMs originales en firmware/c64";
        string title = _host.Fusion is { } f ? $"{(f is Retro.Studio.StudioSession { Game.IsInspired: true } ? "Juego creado" : "Fusión")}: {f.Title} (depurador en {_host.Machine?.DisplayName})" : _host.Info?.Name ?? "Sin máquina";
        if (_host.Fusion != null)
            extra = "";
        ImGui.TextUnformatted($"{title}  |  {speed}{extra}  |  {_host.Status}");
        ImGui.End();
    }

    private void DrawPopups()
    {
        if (_errorPopup != null)
        {
            ImGui.OpenPopup("Aviso");
            bool open = true;
            if (ImGui.BeginPopupModal("Aviso", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted(_errorPopup);
                if (ImGui.Button("Aceptar"))
                {
                    _errorPopup = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            if (!open)
                _errorPopup = null;
        }
        if (_choices != null)
        {
            ImGui.OpenPopup("Elegí qué cargar");
            bool open = true;
            if (ImGui.BeginPopupModal("Elegí qué cargar", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("El archivo trae varias versiones o formatos. La primera es el volcado más limpio:");
                ImGui.TextDisabled("TZX/TAP cargan como la cinta original; Z80/SNA son instantáneas ya cargadas.");
                ImGui.TextDisabled("[a] alternativa, [h] hack, [t] con trucos, [cr] crackeado.");
                bool first = true;
                foreach (var c in _choices.Take(30))
                {
                    string label = $"{c.File.Name}  ->  {c.Machine.Name}{(first ? "   (recomendado)" : "")}";
                    first = false;
                    if (ImGui.Selectable(label, false))
                    {
                        _choices = null;
                        ImGui.CloseCurrentPopup();
                        Launch(c);
                        break;
                    }
                }
                if (ImGui.Button("Cancelar"))
                {
                    _choices = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            if (!open)
                _choices = null;
        }
    }

    // ------------------------------------------------------------------ debugger
    private void DrawDebugger()
    {
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
    }

    private void DrawScreenWindow()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 30), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(720, 640), ImGuiCond.FirstUseEver);
        ImGui.Begin("Pantalla");
        if (_texW != 0)
        {
            var avail = ImGui.GetContentRegionAvail();
            float s = MathF.Max(0.5f, MathF.Min(avail.X / _texW, (avail.Y - 20) / _texH));
            rlImGui.ImageSize(_screenTex, (int)(_texW * s), (int)(_texH * s));
        }
        _screenFocused = ImGui.IsWindowFocused();
        if (_screenFocused)
            ImGui.TextUnformatted("El teclado va a la máquina emulada.");
        else
            ImGui.TextDisabled("Hacé clic en la pantalla para usar el teclado en la máquina.");
        ImGui.End();
    }

    private void DrawCpuWindow(IMachine m)
    {
        var cpu = m.Cpu;
        ImGui.SetNextWindowPos(new Vector2(740, 30), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 440), ImGuiCond.FirstUseEver);
        ImGui.Begin("CPU###cpu");
        ImGui.TextUnformatted(cpu.CpuName);
        if (ImGui.Button(_host.Paused ? "Continuar (F9)" : "Pausa (F9)"))
        {
            if (_host.Paused) _host.Continue(); else _host.Pause("Pausa");
        }
        ImGui.SameLine();
        if (ImGui.Button("Paso")) _host.StepInto();
        ImGui.SameLine();
        if (ImGui.Button("Por encima")) _host.StepOver();
        if (ImGui.Button("Cuadro")) _host.StepFrame();
        ImGui.SameLine();
        if (ImGui.Button("Reiniciar")) _host.Reset(false);
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
                    ImGui.SetNextItemWidth(70);
                    if (ImGui.InputText("##" + r.Name, ref text, 4, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue))
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
                    ImGui.TextUnformatted(fmt);
                }
            }
            ImGui.EndTable();
        }
        ImGui.Separator();
        ImGui.TextUnformatted("Flags (clic para cambiar, en pausa):");
        int i = 0;
        foreach (var f in cpu.GetFlags())
        {
            if (i++ > 0) ImGui.SameLine();
            bool set = f.Set;
            if (ImGui.Checkbox(f.Name + "##flag", ref set) && _host.Paused)
                cpu.TrySetFlag(f.Name, set);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(f.Description);
        }
        ImGui.Separator();
        foreach (var line in cpu.GetInterruptState())
            ImGui.TextUnformatted(line);
        ImGui.Separator();
        foreach (var line in m.GetMachineInfo())
            ImGui.TextUnformatted(line);
        ImGui.End();
    }

    private uint DisassemblyStart(IMachine m, uint pc)
    {
        // Find an address before PC whose decoding lands exactly on PC.
        for (int back = 24; back > 0; back--)
        {
            uint a = (uint)((pc - back) & 0xFFFF);
            int guard = 0;
            while (a != pc && guard++ < 30)
            {
                var ins = m.Disassembler.Disassemble(x => m.Memory.Peek(x), a);
                uint next = (uint)((a + ins.Length) & 0xFFFF);
                if (((pc - a) & 0xFFFF) < ((next - a) & 0xFFFF))
                    break;
                a = next;
            }
            if (a == pc)
                return (uint)((pc - back) & 0xFFFF);
        }
        return pc;
    }

    private void DrawDisassemblyWindow(IMachine m)
    {
        ImGui.SetNextWindowPos(new Vector2(1030, 30), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(420, 640), ImGuiCond.FirstUseEver);
        ImGui.Begin("Desensamblado");
        ImGui.Checkbox("Seguir PC", ref _followPc);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70);
        if (ImGui.InputText("Ir a##dis", ref _disasmAddr, 4, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue))
            _followPc = false;
        uint pc = m.Cpu.ProgramCounter;
        uint addr = _followPc ? DisassemblyStart(m, pc) : uint.TryParse(_disasmAddr, NumberStyles.HexNumber, null, out var a0) ? a0 : 0;
        ImGui.TextDisabled("Clic en ( ) para breakpoint. Clic derecho: ejecutar hasta ahí.");
        ImGui.BeginChild("dislist");
        for (int i = 0; i < 48; i++)
        {
            var ins = m.Disassembler.Disassemble(x => m.Memory.Peek(x), addr);
            bool isPc = addr == pc;
            bool bp = _host.Debug.IsBreakpoint(addr);
            ImGui.PushID((int)addr);
            ImGui.PushStyleColor(ImGuiCol.Text, bp ? new Vector4(1, 0.3f, 0.3f, 1) : new Vector4(0.4f, 0.4f, 0.4f, 1));
            if (ImGui.SmallButton(bp ? "(*)" : "( )"))
                _host.Debug.Toggle(addr);
            ImGui.PopStyleColor();
            ImGui.SameLine();
            string bytes = string.Join(" ", ins.Bytes.Select(b => b.ToString("X2")));
            string? sym = m.SymbolFor(addr);
            string line = $"{(isPc ? ">" : " ")} {addr:X4}  {bytes,-12} {ins.Text}";
            if (sym != null)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1), sym + ":");
                ImGui.SameLine();
            }
            if (isPc)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 0.3f, 1));
            ImGui.Selectable(line, isPc);
            if (isPc)
                ImGui.PopStyleColor();
            if (ImGui.BeginPopupContextItem("ctx"))
            {
                if (ImGui.MenuItem("Ejecutar hasta aquí")) _host.RunTo((ushort)addr);
                if (ImGui.MenuItem("Mover PC aquí")) m.Cpu.ProgramCounter = addr;
                if (ImGui.MenuItem("Ver en memoria")) { _memStart = addr & 0xFFF0; _memAddr = _memStart.ToString("X4"); }
                ImGui.EndPopup();
            }
            ImGui.PopID();
            addr = (uint)((addr + ins.Length) & 0xFFFF);
        }
        ImGui.EndChild();
        ImGui.End();
    }

    private void DrawMemoryWindow(IMachine m)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 680), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(720, 330), ImGuiCond.FirstUseEver);
        ImGui.Begin("Memoria");
        ImGui.SetNextItemWidth(70);
        if (ImGui.InputText("Dirección", ref _memAddr, 4, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue)
            && uint.TryParse(_memAddr, NumberStyles.HexNumber, null, out var a))
            _memStart = a & 0xFFF0;
        ImGui.SameLine();
        if (ImGui.Button("< página")) _memStart = (_memStart - 0x100) & 0xFFFF;
        ImGui.SameLine();
        if (ImGui.Button("página >")) _memStart = (_memStart + 0x100) & 0xFFFF;
        ImGui.SameLine();
        if (ImGui.Button("PC")) _memStart = m.Cpu.ProgramCounter & 0xFFF0;
        ImGui.SameLine();
        if (ImGui.Button("SP")) _memStart = m.Cpu.StackPointer & 0xFFF0;
        ImGui.SameLine();
        if (ImGui.Button("Video")) _memStart = m.Id == "invaders" ? 0x2400u : 0x4000u;
        ImGui.TextDisabled(m.MemoryMapDescription + ". Clic en un byte para editarlo.");
        ImGui.BeginChild("memgrid");
        uint pc = m.Cpu.ProgramCounter, sp = m.Cpu.StackPointer;
        for (int row = 0; row < 16; row++)
        {
            uint rowAddr = (uint)((_memStart + row * 16) & 0xFFFF);
            ImGui.TextUnformatted($"{rowAddr:X4} ");
            for (int col = 0; col < 16; col++)
            {
                uint ad = (uint)((rowAddr + col) & 0xFFFF);
                byte v = m.Memory.Peek(ad);
                ImGui.SameLine();
                bool highlight = ad == pc || ad == sp;
                if (highlight) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 0.3f, 1));
                ImGui.PushID((int)ad);
                if (ImGui.Selectable(v.ToString("X2"), false, ImGuiSelectableFlags.None, new Vector2(18, 0)))
                {
                    _editMemAddr = ad.ToString("X4");
                    _editMemValue = v.ToString("X2");
                    ImGui.OpenPopup("editmem");
                }
                if (ImGui.BeginPopup("editmem"))
                {
                    ImGui.TextUnformatted("Nuevo valor en $" + _editMemAddr);
                    ImGui.SetNextItemWidth(50);
                    if (ImGui.InputText("##val", ref _editMemValue, 2, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        if (byte.TryParse(_editMemValue, NumberStyles.HexNumber, null, out var nv) && _editMemAddr != null)
                            m.Memory.Poke(uint.Parse(_editMemAddr, NumberStyles.HexNumber), nv);
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.TextDisabled("La ROM no se puede modificar.");
                    ImGui.EndPopup();
                }
                ImGui.PopID();
                if (highlight) ImGui.PopStyleColor();
            }
            ImGui.SameLine();
            var chars = new char[16];
            for (int col = 0; col < 16; col++)
            {
                byte v = m.Memory.Peek((uint)((rowAddr + col) & 0xFFFF));
                chars[col] = v is >= 32 and < 127 ? (char)v : '.';
            }
            ImGui.TextUnformatted(" " + new string(chars));
        }
        ImGui.EndChild();
        ImGui.End();
    }

    private void DrawStackWindow(IMachine m)
    {
        ImGui.SetNextWindowPos(new Vector2(740, 480), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 240), ImGuiCond.FirstUseEver);
        ImGui.Begin("Pila");
        uint sp = m.Cpu.StackPointer;
        for (int i = 0; i < 14; i++)
        {
            uint a = (uint)((sp + i * 2) & 0xFFFF);
            uint v = (uint)(m.Memory.Peek(a) | (m.Memory.Peek((a + 1) & 0xFFFF) << 8));
            string? sym = m.SymbolFor(v);
            ImGui.TextUnformatted($"{(i == 0 ? "SP>" : "   ")} {a:X4}: {v:X4}{(sym != null ? "  " + sym : "")}");
        }
        ImGui.End();
    }

    private void DrawTapeWindow()
    {
        var zx = _host.Zx!;
        ImGui.SetNextWindowPos(new Vector2(740, 730), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(420, 280), ImGuiCond.FirstUseEver);
        ImGui.Begin("Cinta");
        var tape = zx.Tape;
        if (!tape.IsLoaded)
        {
            ImGui.TextUnformatted("No hay cinta. Abrí un .tap o .tzx.");
            ImGui.End();
            return;
        }
        if (ImGui.Button(tape.IsPlaying ? "Detener (F6)" : "Reproducir (F6)")) ToggleTape();
        ImGui.SameLine();
        if (ImGui.Button("Rebobinar (F7)")) tape.Rewind();
        ImGui.SameLine();
        if (ImGui.Button("LOAD \"\"")) zx.TypeLoadCommand();
        ImGui.ProgressBar((float)tape.Progress, new Vector2(-1, 0), $"{tape.Progress * 100:F0}%");
        int current = tape.CurrentBlock;
        foreach (var b in tape.Tape!.Blocks)
            ImGui.Selectable($"{b.Index,3}  {b.Description}", b.Index == current);
        ImGui.End();
    }

    private void DrawTestProgramWindow(IMachine m)
    {
        ImGui.SetNextWindowPos(new Vector2(1170, 680), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(330, 250), ImGuiCond.FirstUseEver);
        ImGui.Begin("Programa de prueba");
        ImGui.TextWrapped($"Cargá bytes de {m.Cpu.CpuName} o un .bin en memoria para estudiar su ejecución paso a paso.");
        ImGui.SetNextItemWidth(70);
        ImGui.InputText("Dirección##test", ref _testAddr, 4, ImGuiInputTextFlags.CharsHexadecimal);
        ImGui.Checkbox("Poner PC en esa dirección", ref _testSetPc);
        ImGui.InputText("Bytes (hex)", ref _testHex, 512);
        ushort addr = ushort.TryParse(_testAddr, NumberStyles.HexNumber, null, out var ta) ? ta : (ushort)0x8000;
        if (ImGui.Button("Cargar bytes"))
        {
            try
            {
                var bytes = _testHex.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(h => byte.Parse(h, NumberStyles.HexNumber)).ToArray();
                _host.LoadBinary(bytes, addr, _testSetPc);
            }
            catch (FormatException)
            {
                _errorPopup = "Bytes inválidos: usá pares hexadecimales separados por espacios.";
            }
        }
        ImGui.InputText("Archivo .bin", ref _testPath, 512);
        if (ImGui.Button("Cargar archivo"))
        {
            if (File.Exists(_testPath))
                _host.LoadBinary(File.ReadAllBytes(_testPath), addr, _testSetPc);
            else
                _errorPopup = "No existe el archivo " + _testPath;
        }
        ImGui.TextDisabled("Escribí bytes en hexadecimal o cargá un archivo binario.");
        ImGui.End();
    }
}
