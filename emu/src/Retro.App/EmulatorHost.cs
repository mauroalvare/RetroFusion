using System.Diagnostics;
using Retro.Core;
using Retro.Machines;
using Retro.Systems.Arcade;
using Retro.Systems.C64;
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
        Fusion = session;
        foreach (var slot in session.Slots)
            slot.Machine.ExecutedMap ??= new bool[0x10000];   // the code view sees what each game ran
        FusionFocus = session.Slots[0].Id;
        Paused = false;
        _audio.Clear();
        Status = $"Fusión: {session.Title}. {session.Description}";
    }

    public void StopFusion()
    {
        Fusion = null;
        _audio.Clear();
        Status = "Fusión detenida.";
    }
    public MachineInfo? Info { get; private set; }
    public Spectrum48? Zx => Machine as Spectrum48;
    public SpaceInvaders? Invaders => Machine as SpaceInvaders;
    public C64? C64 => Machine as C64;
    public DebugController Debug { get; } = new();
    public bool Paused { get; set; }
    public bool TurboWhileLoading { get; set; } = true;
    public bool Turbo { get; set; }
    public string Status { get; set; } = "Arrastrá un juego a la ventana o usá Archivo > Abrir.";
    public string? LastStopReason { get; private set; }
    public string? LoadedMedia { get; private set; }
    public double EffectiveSpeed { get; private set; } = 1;

    private readonly AudioQueue _audio = new(44100 / 2);
    private readonly short[] _frameAudio = new short[4096];
    private double _accumulator;
    private readonly Stopwatch _speedWatch = Stopwatch.StartNew();
    private long _speedFrames;
    private byte[]? _quickState;
    private ushort? _runToAddress;

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
                SetMachine(new Spectrum48(File.ReadAllBytes(rom)), info);
                Status = $"{info.Name} encendido.";
                break;
            }
            case "c64":
            {
                var c64 = new C64(C64Firmware.Find(romDirectory));
                SetMachine(c64, info);
                Status = $"{info.Name} encendido ({c64.FirmwareDescription}).";
                break;
            }
            case "invaders":
                throw new InvalidOperationException("Space Invaders necesita la ROM del juego: abrí invaders.zip (MAME).");
            default:
                throw new NotSupportedException($"{info.Name} todavía no está implementado.");
        }
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

    private void SetMachine(IMachine m, MachineInfo info)
    {
        Fusion = null;
        m.ExecutedMap = new bool[0x10000];   // the code view sees everything the game ran
        Machine = m;
        Info = info;
        Paused = false;
        LoadedMedia = null;
        Debug.Clear();
        _audio.Clear();
    }

    public void Load(MediaFile file, bool autoType)
    {
        if (C64 is { } c64)
        {
            var r = C64Media.Load(c64, file);
            LoadedMedia = file.Name;
            Status = $"{file.Name}: {r.Description}" + (r.Warnings.Count > 0 ? " (" + string.Join("; ", r.Warnings) + ")" : "");
            Paused = false;
            return;
        }
        var zx = Zx ?? throw new InvalidOperationException("No hay un ZX Spectrum encendido.");
        var result = SpectrumMedia.Load(zx, file);
        LoadedMedia = file.Name;
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
        Debug.Temporary = _runToAddress;
        var reason = m.RunFrame(Debug.Enabled ? Debug : null);
        if (_runToAddress is ushort target && reason == RunStopReason.Breakpoint && m.Cpu.ProgramCounter == target)
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
        RunTo((ushort)(m.Cpu.ProgramCounter + ins.Length));
    }

    public void RunTo(ushort address)
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
        Machine.RunFrame(Debug);
        Machine.DrainAudio(_frameAudio);
    }

    public void Reset(bool hard)
    {
        Machine?.Reset(hard);
        Status = hard ? "Reinicio total (memoria borrada)." : "Reinicio.";
    }

    // -- save states -------------------------------------------------------------
    public void QuickSave()
    {
        if (Machine == null)
            return;
        _quickState = Machine.SaveState();
        Status = "Estado guardado en memoria (F2). F3 para volver.";
    }

    public void QuickLoad()
    {
        if (Machine == null || _quickState == null)
            return;
        Machine.LoadState(_quickState);
        Status = "Estado restaurado.";
    }

    public void SaveStateToFile(string path)
    {
        if (Machine == null)
            return;
        File.WriteAllBytes(path, Machine.SaveState());
        Status = "Estado guardado: " + Path.GetFileName(path);
    }

    public void LoadStateFromFile(string path)
    {
        if (Machine == null)
            return;
        Machine.LoadState(File.ReadAllBytes(path));
        Status = "Estado cargado: " + Path.GetFileName(path);
    }

    /// <summary>Loads raw bytes at an address and points PC there (test programs).</summary>
    public void LoadBinary(byte[] data, ushort address, bool setPc)
    {
        var m = Machine;
        if (m == null)
            return;
        for (int i = 0; i < data.Length && address + i < 0x10000; i++)
            m.Memory.Poke((uint)(address + i), data[i]);
        if (setPc)
            m.Cpu.ProgramCounter = address;
        Paused = true;
        Status = $"{data.Length} bytes cargados en ${address:X4}" + (setPc ? ", PC apunta ahí. Usá Paso (F10) o Continuar." : ".");
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
            _read = _write = _count = 0;
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
