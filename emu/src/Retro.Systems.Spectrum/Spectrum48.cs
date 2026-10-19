using Retro.Core;
using Retro.Cpu.Z80;
using Retro.Systems.Spectrum.Tape;

namespace Retro.Systems.Spectrum;

/// <summary>
/// Sinclair ZX Spectrum 48K (issue 3 board).
///
/// Z80 at 3.5 MHz, 16 KB ROM + 48 KB RAM. The ULA generates the 50.08 Hz frame
/// (312 lines x 224 T-states), the 32 T-state /INT pulse, the picture (beam drawn
/// T-state by T-state so border and multicolour effects appear), memory and I/O
/// contention, the keyboard matrix, the beeper (EAR/MIC) and the tape EAR input.
/// </summary>
public sealed class Spectrum48 : IMachine, IZ80Bus, IMemoryView, IWriteWatch, IIoWatch
{
    public const int CpuClock = 3_500_000;
    public const int TStatesPerLine = 224;
    public const int LinesPerFrame = 312;
    public const int FrameTStates = TStatesPerLine * LinesPerFrame;   // 69888
    public const int FirstContendedT = 14335;
    public const int InterruptLength = 32;
    public const int BorderTop = 48, BorderBottom = 56, BorderSide = 48;
    public const int ScreenWidth = BorderSide + 256 + BorderSide;      // 352
    public const int ScreenHeight = BorderTop + 192 + BorderBottom;    // 296
    private const int FirstVisibleLine = 64 - BorderTop;               // 16
    public const int SampleRate = 44100;

    private static readonly int[] Palette =
    {
        unchecked((int)0xFF000000), unchecked((int)0xFF0000D7), unchecked((int)0xFFD70000), unchecked((int)0xFFD700D7),
        unchecked((int)0xFF00D700), unchecked((int)0xFF00D7D7), unchecked((int)0xFFD7D700), unchecked((int)0xFFD7D7D7),
        unchecked((int)0xFF000000), unchecked((int)0xFF0000FF), unchecked((int)0xFFFF0000), unchecked((int)0xFFFF00FF),
        unchecked((int)0xFF00FF00), unchecked((int)0xFF00FFFF), unchecked((int)0xFFFFFF00), unchecked((int)0xFFFFFFFF),
    };

    private static readonly byte[] ContentionPattern = { 6, 5, 4, 3, 2, 1, 0, 0 };
    private static readonly byte[] ContentionTable = BuildContention();

    private readonly byte[] _memory = new byte[0x10000];
    private readonly byte[] _rom;
    private readonly Z80Cpu _cpu;
    private readonly Z80Disassembler _disassembler = new();
    private long _frameStart;
    private long _renderedT;           // frame-relative T up to which the picture is drawn
    private byte _border = 7;
    private bool _earOut, _micOut;
    private readonly byte[] _keyRows = { 0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F };
    private byte _kempston;
    private bool _flashPhase;
    private bool _skipBreakpointOnce;

    // audio
    private const double TPerSample = (double)CpuClock / SampleRate;
    private double _audioLastT, _audioNextSample, _audioAcc;
    private double _dcX, _dcY;

    public Spectrum48(byte[] rom)
    {
        if (rom.Length != 0x4000)
            throw new ArgumentException("La ROM del Spectrum 48K debe tener exactamente 16384 bytes.");
        _rom = (byte[])rom.Clone();
        _cpu = new Z80Cpu(this);
        _disassembler.Symbols = a => SpectrumSymbols.Rom.TryGetValue(a, out var n) ? n : null;
        Video = new VideoFrame(ScreenWidth, ScreenHeight);
        Audio = new AudioBuffer(SampleRate, 2048);
        Tape = new TapePlayer();
        Reset(true);
    }

    public string Id => "zx48";
    public string DisplayName => "ZX Spectrum 48K";
    public Z80Cpu Z80 => _cpu;
    public ICpuDebugView Cpu => _cpu;
    public IDisassembler Disassembler => _disassembler;
    public IMemoryView Memory => this;
    public VideoFrame Video { get; }
    public AudioBuffer Audio { get; }
    public double FramesPerSecond => (double)CpuClock / FrameTStates;
    public long FrameCount { get; private set; }
    public TapePlayer Tape { get; }
    public bool KempstonEnabled { get; set; } = true;
    /// <summary>Start the tape automatically when the ROM loader (LD-BYTES) runs.</summary>
    public bool AutoPlayTape { get; set; } = true;
    public byte Border => _border;
    public long FrameTState => _cpu.T - _frameStart;

    public void Reset(bool hard)
    {
        if (hard)
        {
            var rng = new Random(1982);
            for (int i = 0x4000; i < 0x10000; i++)
                _memory[i] = (byte)rng.Next(256);
        }
        Array.Copy(_rom, _memory, 0x4000);
        _cpu.Reset();
        _cpu.T = 0;
        _frameStart = 0;
        _renderedT = 0;
        _border = 7;
        _earOut = _micOut = false;
        _audioLastT = 0;
        _audioNextSample = TPerSample;
        _audioAcc = 0;
        Audio.Count = 0;
        FrameCount = 0;
        ReleaseAllKeys();
    }

    // ------------------------------------------------------------------ timing tables
    private static byte[] BuildContention()
    {
        var table = new byte[FrameTStates + 1024];
        for (int line = 0; line < 192; line++)
        {
            int start = FirstContendedT + line * TStatesPerLine;
            for (int t = 0; t < 128; t++)
                table[start + t] = ContentionPattern[t & 7];
        }
        return table;
    }

    private int ContentionAt(long tstate)
    {
        long rel = tstate - _frameStart;
        return rel >= 0 && rel < ContentionTable.Length ? ContentionTable[rel] : 0;
    }

    // ------------------------------------------------------------------ IZ80Bus
    public byte ReadMemory(ushort address) => _memory[address];

    public void WriteMemory(ushort address, byte value)
    {
        if (address < 0x4000)
            return; // ROM
        if (address < 0x5B00)
            RenderTo(_cpu.T - _frameStart);
        _memory[address] = value;
        WriteWatch?.Invoke(address, value, _instructionPc);
    }

    public Action<ushort, byte, ushort>? WriteWatch { get; set; }
    public Action<ushort, byte, bool, ushort>? IoWatch { get; set; }
    private ushort _instructionPc;

    public int Contention(ushort address, long tstate) =>
        (address & 0xC000) == 0x4000 ? ContentionAt(tstate) : 0;

    public int PortTimingPre(ushort port, long tstate) =>
        (port & 0xC000) == 0x4000 ? ContentionAt(tstate) + 1 : 1;

    public int PortTimingPost(ushort port, long tstate)
    {
        if ((port & 1) == 0)
            return ContentionAt(tstate) + 3;             // ULA port: C:3
        if ((port & 0xC000) == 0x4000)
        {
            long t = tstate;                             // C:1 C:1 C:1
            for (int i = 0; i < 3; i++)
                t += ContentionAt(t) + 1;
            return (int)(t - tstate);
        }
        return 3;                                        // N:3
    }

    public byte InterruptDataBus => 0xFF;

    public byte ReadPort(ushort port)
    {
        byte value = ReadPortValue(port);
        IoWatch?.Invoke(port, value, false, _instructionPc);
        return value;
    }

    private byte ReadPortValue(ushort port)
    {
        if ((port & 1) == 0)
        {
            int keys = 0x1F;
            for (int row = 0; row < 8; row++)
                if ((port & (0x100 << row)) == 0)
                    keys &= _keyRows[row];
            _earReads++;
            bool ear = Tape.LevelAt(_cpu.T) || _earOut;  // issue 3: EAR output feeds back into bit 6
            return (byte)(keys | 0xA0 | (ear ? 0x40 : 0));
        }
        if (KempstonEnabled && (port & 0xE0) == 0)
            return _kempston;
        return FloatingBus();
    }

    public void WritePort(ushort port, byte value)
    {
        IoWatch?.Invoke(port, value, true, _instructionPc);
        if ((port & 1) != 0)
            return;
        long rel = _cpu.T - _frameStart;
        RenderTo(rel);
        _border = (byte)(value & 7);
        bool ear = (value & 0x10) != 0, mic = (value & 0x08) != 0;
        if (ear != _earOut || mic != _micOut)
        {
            AudioTo(rel);
            _earOut = ear;
            _micOut = mic;
        }
    }

    /// <summary>Byte the ULA is fetching from video memory at this moment (unattached ports).</summary>
    private byte FloatingBus()
    {
        long rel = _cpu.T - _frameStart;
        long fromScreen = rel - (64 * TStatesPerLine - 24);
        if (fromScreen < 0)
            return 0xFF;
        int line = (int)(fromScreen / TStatesPerLine);
        if (line >= 192)
            return 0xFF;
        int through = (int)(fromScreen % TStatesPerLine);
        if (through < 24 || through >= 24 + 128)
            return 0xFF;
        int column = ((through - 24) / 8) * 2;
        switch (through % 8)
        {
            case 3: return _memory[BitmapAddress(line, column)];
            case 4: return _memory[AttributeAddress(line, column)];
            case 5: return _memory[BitmapAddress(line, column + 1)];
            case 6: return _memory[AttributeAddress(line, column + 1)];
            default: return 0xFF;
        }
    }

    private static int BitmapAddress(int line, int column) =>
        0x4000 | ((line & 0xC0) << 5) | ((line & 0x07) << 8) | ((line & 0x38) << 2) | column;

    private static int AttributeAddress(int line, int column) => 0x5800 + (line >> 3) * 32 + column;

    // ------------------------------------------------------------------ video
    /// <summary>Draws the picture up to frame-relative T-state <paramref name="target"/>.</summary>
    private void RenderTo(long target)
    {
        if (target > FrameTStates)
            target = FrameTStates;
        int[] px = Video.Pixels;
        int borderColor = Palette[_border];
        while (_renderedT < target)
        {
            long d = _renderedT + 24;
            int line = (int)(d / TStatesPerLine);
            int col = (int)(d % TStatesPerLine);
            long lineStart = line * (long)TStatesPerLine - 24;
            if (line < FirstVisibleLine)
            {
                _renderedT = Math.Min(FirstVisibleLine * (long)TStatesPerLine - 24, target);
                continue;
            }
            int y = line - FirstVisibleLine;
            if (y >= ScreenHeight)
            {
                _renderedT = target;
                break;
            }
            if (col >= 176)
            {
                _renderedT = Math.Min(lineStart + TStatesPerLine, target);
                continue;
            }
            int row = y * ScreenWidth;
            int paperLine = y - BorderTop;
            if (col >= 24 && col < 152 && paperLine >= 0 && paperLine < 192)
            {
                int cell = (col - 24) >> 2;
                int cellStartCol = 24 + (cell << 2);
                if (col == cellStartCol)
                    DrawCell(px, row + BorderSide + cell * 8, paperLine, cell);
                _renderedT = lineStart + cellStartCol + 4;
            }
            else
            {
                int x = row + col * 2;
                px[x] = borderColor;
                px[x + 1] = borderColor;
                _renderedT++;
            }
        }
    }

    private void DrawCell(int[] px, int offset, int line, int cell)
    {
        byte bits = _memory[BitmapAddress(line, cell)];
        byte attr = _memory[AttributeAddress(line, cell)];
        int bright = (attr & 0x40) != 0 ? 8 : 0;
        int ink = Palette[(attr & 7) | bright];
        int paper = Palette[((attr >> 3) & 7) | bright];
        if ((attr & 0x80) != 0 && _flashPhase)
            (ink, paper) = (paper, ink);
        for (int i = 0; i < 8; i++)
            px[offset + i] = (bits & (0x80 >> i)) != 0 ? ink : paper;
    }

    // ------------------------------------------------------------------ audio
    private bool _tapeAudioLevel;

    private int Level() => (_earOut ? 12000 : 0) + (_micOut ? 1500 : 0) + (_tapeAudioLevel && Tape.IsLoaded ? 3000 : 0);

    /// <summary>
    /// Integrates the speaker output (EAR/MIC bits plus the tape signal heard through
    /// the EAR socket) up to frame-relative T-state <paramref name="rel"/>, producing
    /// 44.1 kHz samples. Tape edges are applied at their exact T-state.
    /// </summary>
    private void AudioTo(long rel)
    {
        if (Tape.IsPlaying)
            Tape.LevelAt(_frameStart + rel);
        var edges = Tape.Edges;
        int edge = 0;
        double t = rel;
        while (_audioLastT < t)
        {
            double segEnd = Math.Min(t, _audioNextSample);
            while (edge < edges.Count && edges[edge].T - _frameStart <= segEnd)
            {
                double et = Math.Max(edges[edge].T - _frameStart, _audioLastT);
                _audioAcc += Level() * (et - _audioLastT);
                _audioLastT = et;
                _tapeAudioLevel = edges[edge].Level;
                edge++;
            }
            _audioAcc += Level() * (segEnd - _audioLastT);
            _audioLastT = segEnd;
            if (segEnd >= _audioNextSample)
            {
                double sample = _audioAcc / TPerSample;
                // remove the DC offset of the speaker circuit
                double y = sample - _dcX + 0.995 * _dcY;
                _dcX = sample;
                _dcY = y;
                if (Audio.Count < Audio.Samples.Length)
                    Audio.Samples[Audio.Count++] = (short)Math.Clamp(y, -32767, 32767);
                _audioAcc = 0;
                _audioNextSample += TPerSample;
            }
        }
        if (edge > 0)
            edges.RemoveRange(0, edge);
    }

    // ------------------------------------------------------------------ execution
    public RunStopReason RunFrame(DebugController? debug)
    {
        long frameEnd = _frameStart + FrameTStates;
        while (_cpu.T < frameEnd)
        {
            if (debug != null && debug.Enabled && !_skipBreakpointOnce && debug.IsBreakpoint(_cpu.PC))
                return RunStopReason.Breakpoint;
            _skipBreakpointOnce = false;
            ExecuteOne();
        }
        EndFrame();
        return RunStopReason.FrameComplete;
    }

    /// <summary>Call after a breakpoint stop so the next run does not stop at the same PC.</summary>
    public void ResumeFromBreakpoint() => _skipBreakpointOnce = true;

    public void StepInstruction()
    {
        ExecuteOne();
        if (_cpu.T >= _frameStart + FrameTStates)
            EndFrame();
    }

    public IFlowAnalyzer Flow { get; } = new Z80Flow();
    public IReadOnlyList<uint> CodeEntryPoints { get; } = new uint[] { 0x0000, 0x0038, 0x0066 };
    public bool[]? ExecutedMap { get; set; }

    private void ExecuteOne()
    {
        _instructionPc = _cpu.PC;
        if (ExecutedMap != null)
            ExecutedMap[_cpu.PC] = true;
        _cpu.IntLine = _cpu.T - _frameStart < InterruptLength;
        if (AutoPlayTape && _cpu.PC == SpectrumSymbols.LdBytes && Tape.IsLoaded && !Tape.IsPlaying && !Tape.AtEnd)
            Tape.Play(_cpu.T);
        _cpu.Step();
        if (_macro.Count > 0 || _macroKeys != null)
            RunMacro();
    }

    private void EndFrame()
    {
        RenderTo(FrameTStates);
        AudioTo(FrameTStates);
        _audioLastT -= FrameTStates;
        _audioNextSample -= FrameTStates;
        _frameStart += FrameTStates;
        _renderedT = 0;
        FrameCount++;
        if (FrameCount % 16 == 0)
            _flashPhase = !_flashPhase;
        WatchTapeUse();
    }

    private int _earReads, _quietFrames;
    /// <summary>A loader reads port $FE over a thousand times per frame; a game reading its keys, a few dozen.</summary>
    private const int LoaderReadsPerFrame = 200, QuietFramesBeforeStop = 75;

    /// <summary>
    /// Auto play also stops: after a second in which nothing reads the tape like a loader,
    /// the deck stops in the pilot of the block the ROM can load (going back to its start) or
    /// before the next one (multiload games on .tap, which has no "stop the tape" marks, ask
    /// for their next part later through LD-BYTES).
    /// </summary>
    private void WatchTapeUse()
    {
        // the ROM loader waits about a second without reading (LD-WAIT, $0571): that is loading too
        bool inRomLoader = _cpu.PC >= SpectrumSymbols.LdBytes && _cpu.PC < 0x0605;
        _quietFrames = Tape.IsPlaying && _earReads < LoaderReadsPerFrame && !inRomLoader ? _quietFrames + 1 : 0;
        _earReads = 0;
        if (!AutoPlayTape || !Tape.IsPlaying)
            return;
        bool quiet = _quietFrames >= QuietFramesBeforeStop;
        if (!(quiet && Tape.StopInRomPilot(_cpu.T)))
            Tape.StopAtNextRomBlock = quiet;
    }

    /// <summary>Takes the audio produced so far (call once per frame from the frontend).</summary>
    public int DrainAudio(short[] destination)
    {
        int n = Math.Min(destination.Length, Audio.Count);
        Array.Copy(Audio.Samples, destination, n);
        Audio.Count = 0;
        return n;
    }

    // ------------------------------------------------------------------ keyboard
    private static readonly string[][] Matrix =
    {
        new[] { "CAPS SHIFT", "Z", "X", "C", "V" },
        new[] { "A", "S", "D", "F", "G" },
        new[] { "Q", "W", "E", "R", "T" },
        new[] { "1", "2", "3", "4", "5" },
        new[] { "0", "9", "8", "7", "6" },
        new[] { "P", "O", "I", "U", "Y" },
        new[] { "ENTER", "L", "K", "J", "H" },
        new[] { "SPACE", "SYMBOL SHIFT", "M", "N", "B" },
    };

    private static readonly Dictionary<string, (int row, int bit)> KeyIndex = BuildKeyIndex();

    private static Dictionary<string, (int, int)> BuildKeyIndex()
    {
        var d = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r < 8; r++)
            for (int b = 0; b < 5; b++)
                d[Matrix[r][b]] = (r, b);
        return d;
    }

    public IReadOnlyList<MachineKey> Keys { get; } =
        Matrix.SelectMany(r => r).Select(k => new MachineKey(k, k)).ToArray();

    public void SetKey(string name, bool pressed)
    {
        if (name.StartsWith("KEMPSTON ", StringComparison.OrdinalIgnoreCase))
        {
            int bit = name[9..].ToUpperInvariant() switch
            {
                "RIGHT" => 0, "LEFT" => 1, "DOWN" => 2, "UP" => 3, "FIRE" => 4, _ => -1,
            };
            if (bit >= 0)
                _kempston = (byte)(pressed ? _kempston | (1 << bit) : _kempston & ~(1 << bit));
            return;
        }
        if (!KeyIndex.TryGetValue(name, out var k))
            return;
        if (pressed)
            _keyRows[k.row] &= (byte)~(1 << k.bit);
        else
            _keyRows[k.row] |= (byte)(1 << k.bit);
    }

    public void ReleaseAllKeys()
    {
        for (int i = 0; i < 8; i++)
            _keyRows[i] = 0x1F;
        _kempston = 0;
    }

    // -- typed key sequences (e.g. LOAD "" ENTER), held for a number of frames each
    private readonly Queue<(string[] keys, int frames)> _macro = new();
    private long _macroUntilFrame = -1;
    private string[]? _macroKeys;

    public void QueueKeys(int framesHeld, params string[] keys) => _macro.Enqueue((keys, framesHeld));

    /// <summary>Types LOAD "" ENTER the way a user would (K mode: J = LOAD).</summary>
    public void TypeLoadCommand()
    {
        QueueKeys(4, "J");
        QueueKeys(4);
        QueueKeys(4, "SYMBOL SHIFT", "P");
        QueueKeys(4);
        QueueKeys(4, "SYMBOL SHIFT", "P");
        QueueKeys(4);
        QueueKeys(4, "ENTER");
        QueueKeys(2);
    }

    private void RunMacro()
    {
        if (FrameCount < _macroUntilFrame)
            return;
        if (_macroKeys != null)
            foreach (var k in _macroKeys)
                SetKey(k, false);
        _macroKeys = null;
        if (_macro.Count == 0)
            return;
        var (keys, frames) = _macro.Dequeue();
        foreach (var k in keys)
            SetKey(k, true);
        _macroKeys = keys;
        _macroUntilFrame = FrameCount + frames;
    }

    public bool TypingInProgress => _macro.Count > 0 || _macroKeys != null;

    // ------------------------------------------------------------------ debugger info
    public IReadOnlyList<string> GetMachineInfo()
    {
        long ft = FrameTState;
        return new[]
        {
            $"T-states total: {_cpu.T:N0}",
            $"Cuadro {FrameCount}, T en cuadro: {ft} de {FrameTStates}",
            $"Haz: línea {ft / TStatesPerLine}, ciclo {ft % TStatesPerLine}",
            ft < InterruptLength ? "Pulso /INT activo (primeros 32 T)" : $"Próxima /INT en {FrameTStates - ft} T",
            $"Borde: {_border}  EAR={(_earOut ? 1 : 0)} MIC={(_micOut ? 1 : 0)}",
        };
    }

    public string? SymbolFor(uint address) =>
        SpectrumSymbols.Rom.TryGetValue((ushort)address, out var n) ? n : null;

    public string MemoryMapDescription => "0000-3FFF ROM | 4000-57FF píxeles | 5800-5AFF atributos | 5B00+ RAM";

    // ------------------------------------------------------------------ media
    public void InsertTape(TapeImage tape) => Tape.Insert(tape);

    /// <summary>Loads a 48K memory image and CPU state (from a snapshot file).</summary>
    public void LoadMemoryImage(ReadOnlySpan<byte> ram48k, byte border)
    {
        ram48k.CopyTo(_memory.AsSpan(0x4000));
        _border = (byte)(border & 7);
        _renderedT = 0;
    }

    // ------------------------------------------------------------------ IMemoryView
    public int AddressBits => 16;
    public byte Peek(uint address) => _memory[address & 0xFFFF];

    public void Poke(uint address, byte value)
    {
        if ((address & 0xFFFF) >= 0x4000)
            _memory[address & 0xFFFF] = value;
    }

    // ------------------------------------------------------------------ save states
    private const string StateMagic = "RETRO-ZX48";
    private const int StateVersion = 1;

    public byte[] SaveState()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(StateMagic);
        w.Write(StateVersion);
        var c = _cpu;
        w.Write(c.AF); w.Write(c.BC); w.Write(c.DE); w.Write(c.HL);
        w.Write(c.AF_); w.Write(c.BC_); w.Write(c.DE_); w.Write(c.HL_);
        w.Write(c.IX); w.Write(c.IY); w.Write(c.SP); w.Write(c.PC); w.Write(c.WZ);
        w.Write(c.I); w.Write(c.R); w.Write(c.IFF1); w.Write(c.IFF2); w.Write(c.IM); w.Write(c.Halted); w.Write(c.Q);
        w.Write(c.T - _frameStart);
        w.Write(_memory, 0x4000, 0xC000);
        w.Write(_border); w.Write(_earOut); w.Write(_micOut); w.Write(_flashPhase); w.Write(FrameCount);
        var tape = Tape.Capture(c.T);
        w.Write(tape.segment); w.Write(tape.remaining); w.Write(tape.playing); w.Write(tape.level);
        return ms.ToArray();
    }

    public void LoadState(byte[] state)
    {
        using var r = new BinaryReader(new MemoryStream(state));
        if (r.ReadString() != StateMagic)
            throw new InvalidDataException("No es un estado guardado de ZX Spectrum 48K.");
        if (r.ReadInt32() != StateVersion)
            throw new InvalidDataException("Versión de estado guardado no soportada.");
        var c = _cpu;
        c.AF = r.ReadUInt16(); c.BC = r.ReadUInt16(); c.DE = r.ReadUInt16(); c.HL = r.ReadUInt16();
        c.AF_ = r.ReadUInt16(); c.BC_ = r.ReadUInt16(); c.DE_ = r.ReadUInt16(); c.HL_ = r.ReadUInt16();
        c.IX = r.ReadUInt16(); c.IY = r.ReadUInt16(); c.SP = r.ReadUInt16(); c.PC = r.ReadUInt16(); c.WZ = r.ReadUInt16();
        c.I = r.ReadByte(); c.R = r.ReadByte(); c.IFF1 = r.ReadBoolean(); c.IFF2 = r.ReadBoolean();
        c.IM = r.ReadInt32(); c.Halted = r.ReadBoolean(); c.Q = r.ReadByte();
        long rel = r.ReadInt64();
        _frameStart = 0;
        c.T = rel;
        r.Read(_memory, 0x4000, 0xC000);
        _border = r.ReadByte(); _earOut = r.ReadBoolean(); _micOut = r.ReadBoolean(); _flashPhase = r.ReadBoolean();
        FrameCount = r.ReadInt64();
        int seg = r.ReadInt32(); long remaining = r.ReadInt64(); bool playing = r.ReadBoolean(); bool level = r.ReadBoolean();
        if (Tape.IsLoaded)
            Tape.Restore(seg, remaining, playing, level, c.T);
        _renderedT = 0;
        _audioLastT = rel;
        _audioNextSample = rel + TPerSample;
        Audio.Count = 0;
    }
}
