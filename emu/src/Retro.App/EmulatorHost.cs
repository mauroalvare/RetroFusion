using System.Security.Cryptography;
using System.Diagnostics;
using Retro.Core;
using Retro.Fusion;
using Retro.Machines;
using Retro.Systems.Arcade;
using Retro.Systems.C64;
using Retro.Systems.Pc;
using Retro.Systems.Spectrum;

namespace Retro.App;

/// <summary>
/// Owns the running machine: pacing (real speed, turbo while loading), pause and
/// stepping, breakpoints, save states and the audio queue. Works with any IMachine;
/// machine specific extras (tape, DIP switches) are reached through Zx / Invaders.
/// </summary>
public sealed class EmulatorHost
{
    private IMachine? _machine;

    /// <summary>The machine shown in the debugger: the single machine, or the chosen game of a fusion.</summary>
    public IMachine? Machine
    {
        get => Fusion != null ? Fusion.Slots.FirstOrDefault(s => s.Id == FusionFocus)?.Machine ?? Fusion.Slots[0].Machine : _machine;
        private set => _machine = value;
    }

    /// <summary>Running fusion (two or three games at once), or null.</summary>
    public Retro.Fusion.IFusionRun? Fusion { get; private set; }
    /// <summary>Slot of the fusion shown in the debugger and code views.</summary>
    public string FusionFocus { get; set; } = "a";
    public VideoFrame? CurrentVideo => Fusion?.Video ?? _machine?.Video;
    private double _fusionClock;

    public void StartFusion(Retro.Fusion.IFusionRun session)
    {
        SessionChanging?.Invoke();
        MediaRevision++;
        DiscardQuickState();
        Fusion = session;
        foreach (var slot in session.Slots)
            slot.Machine.ExecutedMap ??= new bool[1 << Math.Clamp(slot.Machine.Memory.AddressBits, 1, 20)];   // the code view sees what each game ran
        FusionFocus = session.Slots[0].Id;
        Paused = false;
        _audio.Clear();
        Status = $"{(session is Retro.Studio.StudioSession studio && studio.Game.IsInspired ? "Juego creado" : "Fusión")}: {session.Title}. {session.Description}";
    }

    public void StopFusion()
    {
        SessionChanging?.Invoke();
        MediaRevision++;
        DiscardQuickState();
        Fusion = null;
        _audio.Clear();
        Status = "Juego detenido.";
    }
    public MachineInfo? Info { get; private set; }
    public Spectrum48? Zx => Machine as Spectrum48;
    public SpaceInvaders? Invaders => Machine as SpaceInvaders;
    public C64? C64 => Machine as C64;
    public PcXt? Pc => Machine as PcXt;
    public Retro.Systems.Nes.Nes? Nes => Machine as Retro.Systems.Nes.Nes;
    public DebugController Debug { get; } = new();
    public bool Paused { get; set; }
    public bool TurboWhileLoading { get; set; } = true;
    public bool Turbo { get; set; }
    public string Status { get; set; } = "Arrastrá un juego a la ventana o usá Juego > Abrir.";
    public string? LastStopReason { get; private set; }
    public string? LoadedMedia { get; private set; }
    public GameProfile? LoadedProfile { get; private set; }
    public string FirmwareIdentity { get; private set; } = "";
    private IReadOnlyList<MediaFile> _loadedFiles = [];
    public IReadOnlyList<string> LoadedMediaHashes => _loadedFiles.Select(ProfileMatch.HashOf).ToArray();
    public bool UsesMedia(string path) => (Fusion is Retro.Studio.StudioSession studio ? studio.MediaSources : _loadedFiles.Select(f => f.Source))
        .Any(source => source.Equals(path, StringComparison.OrdinalIgnoreCase) || source.StartsWith(path + "!", StringComparison.OrdinalIgnoreCase));
    /// <summary>Changes on load, restart or state restoration, even when the file name is unchanged.</summary>
    public long MediaRevision { get; private set; }
    public bool CanUseStates => Fusion == null && _machine != null;
    public event Action<IMachine>? BeforeFrame;
    /// <summary>Releases game-bound edits before a loader can execute bootstrap instructions.</summary>
    public event Action? SessionChanging;
    public double EffectiveSpeed { get; private set; } = 1;

    private readonly AudioQueue _audio = new(44100 / 2);
    private readonly short[] _frameAudio = new short[4096];
    private double _accumulator;
    private readonly Stopwatch _speedWatch = Stopwatch.StartNew();
    private long _speedFrames;
    private byte[]? _quickState;
    private int[]? _quickPixels;
    private uint? _runToAddress;

    public AudioQueue Audio => _audio;
    public bool IsFastForwarding => Machine != null && (Turbo || (TurboWhileLoading && Zx != null && Zx.Tape.IsPlaying));

    /// <summary>Starts a machine that has its firmware in the firmware folder (ZX Spectrum, Commodore 64).</summary>
    public void Start(MachineInfo info, string romDirectory)
    {
        switch (info.Id)
        {
            case "zx48":
            {
                string rom = Path.Combine(romDirectory, info.RomFiles[0]);
                if (!File.Exists(rom))
                    throw new FileNotFoundException($"Falta la ROM {info.RomFiles[0]} en {romDirectory}.");
                var firmware = File.ReadAllBytes(rom);
                SetMachine(new Spectrum48(firmware), info, Convert.ToHexString(SHA256.HashData(firmware)));
                Status = $"{info.Name} encendido.";
                break;
            }
            case "c64":
            {
                var firmware = C64Firmware.Find(romDirectory);
                var c64 = new C64(firmware);
                SetMachine(c64, info, Convert.ToHexString(SHA256.HashData(
                    [.. firmware.Kernal, .. firmware.Basic, .. firmware.Chargen])));
                Status = $"{info.Name} encendido ({c64.FirmwareDescription}).";
                break;
            }
            case "pcxt":
            {
                var pc = new PcXt(PcFirmware.Read(romDirectory)); SetMachine(pc, info, pc.Board.Memory.BiosIdentity);
                Status = "PC/XT encendido. Abrí un disquete de arranque para iniciar el sistema."; break;
            }
            case "invaders":
                throw new InvalidOperationException("Space Invaders necesita la ROM del juego: abrí invaders.zip (MAME).");
            default:
                throw new NotSupportedException($"{info.Name} todavía no está implementado.");
        }
    }

    /// <summary>Starts the NES with a cartridge: the machine has no firmware, the program is in the .nes image.</summary>
    public void StartNes(MachineInfo info, MediaFile file)
    {
        var cart = Retro.Systems.Nes.NesCartridge.Load(file.Data);
        SetMachine(new Retro.Systems.Nes.Nes(cart), info);
        LoadedMedia = file.Name;
        _loadedFiles = [file];
        LoadedProfile = ProfileLibrary.ForFiles([file]);
        Status = $"{file.Name}: {cart.Describe()}. Mando: flechas, Z = B, X = A, Enter = Start, Shift derecho = Select.";
    }

    public void StartPc(MachineInfo info, MediaFile file, string firmwareDirectory)
    {
        var launched = MachineLauncher.Launch("pcxt", [file], file, firmwareDirectory);
        var pc = (PcXt)launched.Machine; SetMachine(pc, info, pc.Board.Memory.BiosIdentity);
        LoadedMedia = file.Name; _loadedFiles = [file]; LoadedProfile = ProfileLibrary.ForFiles([file]);
        Status = "PC/XT con CGA. El disquete se inicia desde su propio sistema; F1–F10 llegan al PC. Atajos del emulador: Ctrl + tecla de función.";
    }

    /// <summary>Starts the Space Invaders board with the game ROM from the user's files.</summary>
    public void StartInvaders(MachineInfo info, IReadOnlyList<MediaFile> files, string romDirectory, string? mediaFolder)
    {
        var (rom, description) = SpaceInvadersRom.Build(files);
        var m = new SpaceInvaders(rom);
        // MAME's recorded samples, if the user has them, replace the synthesized sounds.
        string? samples = SpaceInvadersSound.FindSamples(mediaFolder, romDirectory);
        if (samples != null && m.Sound.LoadSamples(samples) > 0)
            description += " Sonidos: grabaciones de la placa (samples de MAME).";
        else
            description += " Sonidos: sintetizados (aproximan los circuitos analógicos).";
        SetMachine(m, info);
        LoadedMedia = "invaders";
        _loadedFiles = files.ToArray();
        LoadedProfile = ProfileLibrary.ForFiles(files);
        Status = description + " Moneda: 5 o C, empezar: 1.";
    }

    /// <summary>
    /// Keeps MAME's samples/invaders.zip in the emulator's roms folder, so the sounds are
    /// found later even if the user replaces it with the ROM zip of the same name.
    /// </summary>
    public string InstallInvadersSamples(IReadOnlyList<MediaFile> files, string romDirectory)
    {
        string target = Path.Combine(romDirectory, "samples", "invaders");
        Directory.CreateDirectory(target);
        foreach (var f in files.Where(f => f.Extension == ".wav"))
            File.WriteAllBytes(Path.Combine(target, Path.GetFileName(f.Name)), f.Data);
        string msg = $"Guardé los sonidos grabados de Space Invaders en {target}.";
        if (Invaders is { } inv)
        {
            inv.Sound.LoadSamples(files);
            msg += " Ya se están usando.";
        }
        else
        {
            msg += " Ese zip son los sonidos (samples/ de MAME), no el juego: para jugar abrí el invaders.zip de roms/ (invaders.e, .f, .g, .h).";
        }
        Status = msg;
        return msg;
    }

    private void SetMachine(IMachine m, MachineInfo info, string firmware = "")
    {
        SessionChanging?.Invoke();
        MediaRevision++;
        DiscardQuickState();
        Fusion = null;
        m.ExecutedMap = new bool[1 << Math.Clamp(m.Memory.AddressBits, 1, 20)];   // the code view sees everything the game ran
        Machine = m;
        FirmwareIdentity = firmware;
        Info = info;
        Paused = false;
        LoadedMedia = null;
        LoadedProfile = null;
        _loadedFiles = [];
        Debug.Clear();
        _audio.Clear();
    }

    public void Load(MediaFile file, bool autoType)
    {
        var profile = ProfileLibrary.ForFiles([file]);
        SessionChanging?.Invoke();
        if (C64 is { } c64)
        {
            var r = C64Media.Load(c64, file);
            LoadedMedia = file.Name;
            _loadedFiles = [file];
            LoadedProfile = profile;
            MediaRevision++;
            DiscardQuickState();
            Status = $"{file.Name}: {r.Description}" + (r.Warnings.Count > 0 ? " (" + string.Join("; ", r.Warnings) + ")" : "");
            Paused = false;
            return;
        }
        var zx = Zx ?? throw new InvalidOperationException("No hay un ZX Spectrum encendido.");
        var result = SpectrumMedia.Load(zx, file);
        LoadedMedia = file.Name;
        _loadedFiles = [file];
        LoadedProfile = profile;
        MediaRevision++;
        DiscardQuickState();
        Status = $"{file.Name}: {result.Description}" + (result.Warnings.Count > 0 ? " (" + string.Join("; ", result.Warnings) + ")" : "");
        if (result.Kind == SpectrumMediaKind.Tape && autoType)
        {
            // Fresh boot so the ROM is at the K cursor, then type LOAD "" like a user.
            zx.Reset(false);
            for (int i = 0; i < 150; i++)
                zx.RunFrame(null);
            zx.TypeLoadCommand();
        }
        Paused = false;
    }

    /// <summary>Called once per host frame with the real time elapsed.</summary>
    public void Update(double dt)
    {
        if (Fusion is { } fusion)
        {
            if (Paused)
                return;
            // fixed 60 ticks per second; each game advances at its own frame rate inside
            _fusionClock += Math.Min(dt, 0.1);
            int ticks = 0;
            while (_fusionClock >= 1.0 / 60 && ticks++ < 4)
            {
                _fusionClock -= 1.0 / 60;
                foreach (var slot in fusion.Slots) BeforeFrame?.Invoke(slot.Machine);
                fusion.Update(1.0 / 60);
            }
            int n = fusion.DrainAudio(_frameAudio);
            _audio.Push(_frameAudio.AsSpan(0, n));
            if (_audio.Count > 44100 / 5)
                _audio.Discard(44100 / 15);
            EffectiveSpeed = 1;
            return;
        }
        var m = Machine;
        if (m == null || Paused)
        {
            _accumulator = 0;
            return;
        }
        double frameTime = 1.0 / m.FramesPerSecond;
        if (IsFastForwarding)
        {
            // Run as many frames as fit in ~12 ms; sound is skipped while fast-forwarding.
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < 12 && !Paused)
            {
                if (!RunOneFrame(m))
                    break;
                m.DrainAudio(_frameAudio);
            }
            _accumulator = 0;
            _audio.Clear();
            return;
        }

        // Real time drives the emulation. The audio queue only nudges the pace so the
        // machine and the sound card clock do not drift apart; if the sound device
        // stalls, old samples are dropped instead of freezing the picture.
        // The nudge is proportional to the elapsed time, so it is the same ±25% at any
        // monitor refresh rate (a fixed step per host frame stalled 240 Hz screens).
        double step = Math.Min(dt, 0.1);
        _accumulator += step;
        int queued = _audio.Count;
        int perFrame = (int)(44100 / m.FramesPerSecond);
        if (queued < perFrame)
            _accumulator += step * 0.25;
        else if (queued > perFrame * 3)
            _accumulator -= step * 0.25;
        if (queued > perFrame * 8)
            _audio.Discard(perFrame * 3);
        int guard = 0;
        while (_accumulator >= frameTime && guard++ < 5 && !Paused)
        {
            _accumulator -= frameTime;
            if (!RunOneFrame(m))
                break;
            int n = m.DrainAudio(_frameAudio);
            _audio.Push(_frameAudio.AsSpan(0, n));
        }
    }

    private bool RunOneFrame(IMachine m)
    {
        BeforeFrame?.Invoke(m);
        Debug.Temporary = _runToAddress;
        var reason = m.RunFrame(Debug.Enabled ? Debug : null);
        if (_runToAddress is uint target && reason == RunStopReason.Breakpoint && m.Cpu.ProgramCounter == target)
        {
            _runToAddress = null;
            Debug.Temporary = null;
            Pause($"Llegó a ${target:X4}");
            return false;
        }
        _speedFrames++;
        if (_speedWatch.Elapsed.TotalSeconds >= 1)
        {
            EffectiveSpeed = _speedFrames / m.FramesPerSecond / _speedWatch.Elapsed.TotalSeconds;
            _speedFrames = 0;
            _speedWatch.Restart();
        }
        if (reason == RunStopReason.Breakpoint)
        {
            Pause($"Breakpoint en ${m.Cpu.ProgramCounter:X4}");
            return false;
        }
        return true;
    }

    public void Pause(string reason)
    {
        Paused = true;
        LastStopReason = reason;
        Status = reason;
    }

    public void Continue()
    {
        if (Machine == null)
            return;
        Machine.ResumeFromBreakpoint();
        Paused = false;
        LastStopReason = null;
    }

    public void StepInto()
    {
        if (Machine == null)
            return;
        Paused = true;
        BeforeFrame?.Invoke(Machine);
        Machine.StepInstruction();
    }

    /// <summary>Runs a subroutine call, RST, loop or block instruction completely.</summary>
    public void StepOver()
    {
        var m = Machine;
        if (m == null)
            return;
        var ins = m.Disassembler.Disassemble(a => m.Memory.Peek(a), m.Cpu.ProgramCounter);
        string mnemonic = ins.Text.Split(' ')[0].TrimEnd('*');
        string cpuName = m.Cpu.CpuName;
        bool over = cpuName.Contains("Z80")
            ? mnemonic is "CALL" or "RST" or "DJNZ" or "LDIR" or "LDDR" or "CPIR" or "CPDR" or "INIR" or "INDR" or "OTIR" or "OTDR" or "HALT"
            : cpuName.Contains("65")
                ? mnemonic is "JSR"
                : mnemonic is "CALL" or "CNZ" or "CZ" or "CNC" or "CC" or "CPO" or "CPE" or "CP" or "CM" or "RST" or "HLT";
        if (!over)
        {
            StepInto();
            return;
        }
        RunTo(m.Flow.Analyze(m.Memory.Peek, m.Cpu.ProgramCounter).Next ?? ((m.Cpu.ProgramCounter + (uint)ins.Length) & ((1u << m.Memory.AddressBits) - 1)));
    }

    public void RunTo(uint address)
    {
        if (Machine == null)
            return;
        _runToAddress = address;
        Machine.ResumeFromBreakpoint();
        Paused = false;
    }

    public void StepFrame()
    {
        if (Machine == null)
            return;
        Paused = true;
        Machine.ResumeFromBreakpoint();
        BeforeFrame?.Invoke(Machine);
        Machine.RunFrame(Debug);
        Machine.DrainAudio(_frameAudio);
    }

    public void Reset(bool hard)
    {
        SessionChanging?.Invoke();
        Machine?.Reset(hard);
        StateRestored();
        Status = hard ? "Reinicio total (memoria borrada)." : "Reinicio.";
    }

    // -- save states -------------------------------------------------------------
    public void QuickSave()
    {
        if (!OriginalStateAvailable())
            return;
        _quickState = Machine!.SaveState();
        _quickPixels = Machine.Video.Pixels.ToArray();
        Status = $"Estado guardado en memoria (F2). {(C64 != null ? "F4" : "F3")} para volver.";
    }

    public void QuickLoad()
    {
        if (!OriginalStateAvailable() || _quickState == null)
            return;
        RestoreState(_quickState, _quickPixels);
        Status = "Estado restaurado.";
    }

    public void SaveStateToFile(string path)
    {
        if (!OriginalStateAvailable())
            return;
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, Machine!.SaveState());
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Status = "Estado guardado: " + Path.GetFileName(path);
    }

    public void LoadStateFromFile(string path)
    {
        if (!OriginalStateAvailable())
            return;
        RestoreState(File.ReadAllBytes(path));
        Status = "Estado cargado: " + Path.GetFileName(path);
    }

    private void DiscardQuickState() { _quickState = null; _quickPixels = null; }

    private bool OriginalStateAvailable()
    {
        if (Fusion != null) Status = "Las fusiones no admiten estados guardados. Reiniciá desde el estudio.";
        return CanUseStates;
    }

    private void RestoreState(byte[] state, int[]? pixels = null)
    {
        var machine = Machine!;
        byte[] previous = machine.SaveState();
        int[] previousPixels = machine.Video.Pixels.ToArray();
        try { machine.LoadState(state); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or InvalidOperationException or OverflowException)
        {
            machine.LoadState(previous);
            previousPixels.CopyTo(machine.Video.Pixels, 0);
            throw new InvalidDataException("El estado está dañado o no corresponde a esta máquina. Se conservó la partida actual.", error);
        }
        if (pixels != null && pixels.Length == machine.Video.Pixels.Length)
            pixels.CopyTo(machine.Video.Pixels, 0);
        SessionChanging?.Invoke();
        StateRestored();
    }

    /// <summary>Finds a newly prepared profile without restarting the running game.</summary>
    public void RefreshLoadedProfile()
    {
        if (Fusion != null || _loadedFiles.Count == 0) return;
        var profile = ProfileLibrary.ForFiles(_loadedFiles);
        if (profile?.ToJson() == LoadedProfile?.ToJson()) return;
        SessionChanging?.Invoke();
        LoadedProfile = profile;
        MediaRevision++;
    }

    /// <summary>Invalidates game-bound work and clears sound/pacing after a state change.</summary>
    public void StateRestored()
    {
        MediaRevision++;
        _accumulator = 0;
        _audio.Clear();
        _runToAddress = null;
        Debug.Temporary = null;
    }

    /// <summary>Loads raw bytes at an address and points PC there (test programs).</summary>
    public void LoadBinary(byte[] data, uint address, bool setPc)
    {
        var m = Machine;
        if (m == null)
            return;
        uint end = 1u << m.Memory.AddressBits;
        if (address >= end || data.Length > end - address)
            throw new InvalidDataException("El programa no entra en el espacio de memoria de esta máquina.");
        for (int i = 0; i < data.Length; i++)
            m.Memory.Poke((uint)(address + i), data[i]);
        if (setPc)
            m.Cpu.ProgramCounter = address;
        Paused = true;
        Status = $"{data.Length} bytes cargados en ${address:X4}" + (setPc ? ", PC apunta ahí. Usá Paso o Continuar." : ".");
    }
}

/// <summary>
/// Ring buffer of 16-bit mono samples shared by the emulator (producer) and the
/// audio device thread (consumer).
/// </summary>
public sealed class AudioQueue
{
    private readonly short[] _buf;
    private readonly object _sync = new();
    private int _read, _write, _count;
    private short _last;

    public AudioQueue(int capacity) => _buf = new short[capacity];

    public int Count
    {
        get { lock (_sync) return _count; }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _read = _write = _count = 0;
            _last = 0;
        }
    }

    /// <summary>Drops the oldest samples so that only <paramref name="keep"/> remain.</summary>
    public void Discard(int keep)
    {
        lock (_sync)
        {
            if (_count <= keep)
                return;
            int drop = _count - keep;
            _read = (_read + drop) % _buf.Length;
            _count = keep;
        }
    }

    public void Push(ReadOnlySpan<short> samples)
    {
        lock (_sync)
        {
            foreach (var s in samples)
            {
                if (_count == _buf.Length)
                {
                    _read = (_read + 1) % _buf.Length;
                    _count--;
                }
                _buf[_write] = s;
                _write = (_write + 1) % _buf.Length;
                _count++;
            }
        }
    }

    /// <summary>Fills <paramref name="dest"/>; on underrun fades the last sample (no clicks).</summary>
    public void Pop(Span<short> dest)
    {
        lock (_sync)
        {
            for (int i = 0; i < dest.Length; i++)
            {
                if (_count > 0)
                {
                    _last = _buf[_read];
                    _read = (_read + 1) % _buf.Length;
                    _count--;
                }
                else
                {
                    _last = (short)(_last * 0.98);
                }
                dest[i] = _last;
            }
        }
    }
}
