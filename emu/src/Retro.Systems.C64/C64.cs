using Retro.Core;
using Retro.Cpu.M6502;

namespace Retro.Systems.C64;

/// <summary>The three C64 ROMs: originals if the user has them, otherwise the free Open ROMs.</summary>
public sealed record C64Firmware(byte[] Kernal, byte[] Basic, byte[] Chargen, string Description)
{
    private static readonly string[] KernalNames = { "kernal.rom", "kernal.901227-03.bin", "kernal.bin", "kernal_generic.rom" };
    private static readonly string[] BasicNames = { "basic.rom", "basic.901226-01.bin", "basic.bin", "basic_generic.rom" };
    private static readonly string[] CharNames = { "chargen.rom", "characters.901225-01.bin", "chargen.bin", "chargen_openroms.rom" };

    /// <summary>Looks in firmware/c64 (or roms/c64); the original ROMs win over the Open ROMs.</summary>
    public static C64Firmware Find(string firmwareDirectory)
    {
        string dir = Path.Combine(firmwareDirectory, "c64");
        string? Pick(string[] names, int size) => names
            .Select(n => Path.Combine(dir, n))
            .FirstOrDefault(p => File.Exists(p) && new FileInfo(p).Length == size);
        string? k = Pick(KernalNames, 8192), b = Pick(BasicNames, 8192), c = Pick(CharNames, 4096);
        if (k == null || b == null || c == null)
            throw new FileNotFoundException($"Faltan las ROMs del C64 en {dir} (kernal, basic y chargen; vienen las Open ROMs libres en el paquete).");
        bool open = Path.GetFileName(k).Contains("generic");
        return new C64Firmware(File.ReadAllBytes(k), File.ReadAllBytes(b), File.ReadAllBytes(c),
            open ? "firmware libre Open ROMs" : "ROMs originales de Commodore");
    }
}

/// <summary>
/// Commodore 64 (PAL): MOS 6510 at 985,248 Hz, VIC-II, SID, two CIAs, 64 KB of RAM with
/// BASIC, KERNAL and character ROMs banked in through the 6510's I/O port. The CPU drives
/// the clock: every bus access is one cycle of the whole machine.
/// </summary>
public sealed class C64 : IMachine, IM6502Bus, IMemoryView
{
    public const double Clock = 985248;
    public const int CyclesPerFrame = Vic.CyclesPerLine * Vic.Lines;

    private readonly byte[] _ram = new byte[0x10000];
    private readonly byte[] _colorRam = new byte[0x400];
    private readonly byte[] _kernal, _basic, _chargen;
    private readonly M6502 _cpu;
    private readonly M6502Disassembler _disassembler = new();
    private byte _portDdr, _portData;
    private readonly bool[,] _keys = new bool[8, 8];
    private byte _joy1 = 0xFF, _joy2 = 0xFF;
    private bool _restore;
    private readonly KeySequencer _sequencer = new();
    private readonly short[] _audio = new short[8192];
    private int _audioCount;
    private double _sampleCarry;
    private const double CyclesPerSample = Clock / 44100;
    private long _todCarry;
    private bool _skipBreakpointOnce;
    private ushort _lastPc;

    /// <summary>
    /// Addresses inside the KERNAL ($E000-$FF80, not the official jump table) that a program
    /// in RAM jumped to. The free Open ROMs only keep the documented entry points, so these
    /// are the likely incompatibilities when a game misbehaves with them.
    /// </summary>
    public SortedSet<ushort> KernalInternalCalls { get; } = new();

    /// <summary>The internal calls that the compatibility layer does not cover: what to report when a game fails with the Open ROMs.</summary>
    public IReadOnlyList<ushort> UnhandledKernalCalls => KernalInternalCalls.Where(a => !TrappedRoutines.Contains(a)).ToList();

    private static readonly HashSet<ushort> TrappedRoutines = new()
    {
        0xE518, 0xFF5B, 0xFCE2, 0xFDA3, 0xFD50, 0xFD15, 0xEA31, 0xE51B, 0xE536, 0xE544, 0xE566, 0xE5A0, 0xEA7E, 0xEA81,
    };

    public bool OpenRoms { get; }

    public Vic Vic { get; } = new();
    public Sid Sid { get; } = new(Clock);
    public Cia Cia1 { get; } = new();
    public Cia Cia2 { get; } = new();
    public string FirmwareDescription { get; }

    public C64(C64Firmware firmware)
    {
        _kernal = firmware.Kernal;
        _basic = firmware.Basic;
        _chargen = firmware.Chargen;
        FirmwareDescription = firmware.Description;
        OpenRoms = firmware.Description.Contains("Open ROMs");
        _cpu = new M6502(this) { Name = "MOS 6510" };
        _disassembler.Symbols = a => SymbolFor(a);
        Vic.Fetch = VicFetch;
        Vic.ColorRam = a => _colorRam[a & 0x3FF];
        Cia1.ExternalA = () => _joy2;
        Cia1.ExternalB = KeyboardRows;
        Reset(true);
    }

    public M6502 Cpu6510 => _cpu;
    public byte[] Ram => _ram;

    // ------------------------------------------------------------------ IMachine basics
    public string Id => "c64";
    public string DisplayName => "Commodore 64";
    public ICpuDebugView Cpu => _cpu;
    public IDisassembler Disassembler => _disassembler;
    public IMemoryView Memory => this;
    public VideoFrame Video => Vic.Frame;
    public AudioBuffer Audio { get; } = new(44100, 4096);
    public double FramesPerSecond => Clock / CyclesPerFrame;
    public long FrameCount { get; private set; }
    public IFlowAnalyzer Flow { get; } = new M6502Flow();
    public bool[]? ExecutedMap { get; set; }
    public string MemoryMapDescription =>
        "0000-9FFF RAM | A000-BFFF BASIC | C000-CFFF RAM | D000-D3FF VIC-II | D400 SID | D800 color | DC00 CIA1 | DD00 CIA2 | E000-FFFF KERNAL";

    public IReadOnlyList<uint> CodeEntryPoints
    {
        get
        {
            var list = new List<uint>();
            foreach (ushort v in new ushort[] { 0xFFFC, 0xFFFE, 0xFFFA })
                list.Add((uint)(Peek(v) | (Peek((ushort)(v + 1)) << 8)));
            return list;
        }
    }

    public void Reset(bool hard)
    {
        if (hard)
        {
            // typical power-on pattern of the C64's DRAM
            for (int i = 0; i < _ram.Length; i++)
                _ram[i] = (byte)((i & 0x40) != 0 ? 0xFF : 0x00);
            Array.Clear(_colorRam);
        }
        _portDdr = 0;
        _portData = 0x3F;
        Vic.Reset();
        Sid.Reset();
        Cia1.Reset();
        Cia2.Reset();
        ReleaseAllKeys();
        _cpu.Reset();
        FrameCount = 0;
    }

    // ------------------------------------------------------------------ the bus
    /// <summary>LORAM, HIRAM, CHAREN: output bits of the 6510 port; inputs are pulled up.</summary>
    private int Config => (_portData | ~_portDdr) & 7;

    public byte Read(ushort address)
    {
        // the VIC-II takes the bus on bad lines: the CPU waits on read cycles
        while (Vic.BaLow)
            Tick();
        byte v = CpuRead(address, sideEffects: true);
        Tick();
        return v;
    }

    public void Write(ushort address, byte value)
    {
        CpuWrite(address, value);
        Tick();
    }

    public bool IrqLine => Vic.Irq || Cia1.Interrupt;
    public bool NmiLine => Cia2.Interrupt || _restore;

    private void Tick()
    {
        Vic.Tick();
        Cia1.Tick();
        Cia2.Tick();
        _sampleCarry += 1;
        if (_sampleCarry >= CyclesPerSample)
        {
            _sampleCarry -= CyclesPerSample;
            if (_audioCount < _audio.Length)
                _audio[_audioCount++] = Sid.NextSample();
        }
        if (++_todCarry >= (long)(Clock / 10))
        {
            _todCarry = 0;
            Cia1.TodTick();
            Cia2.TodTick();
        }
    }

    private byte CpuRead(ushort a, bool sideEffects)
    {
        if (a < 2)
            return a == 0 ? _portDdr : (byte)((_portData & _portDdr) | (~_portDdr & 0x17));
        int cfg = Config;
        bool loram = (cfg & 1) != 0, hiram = (cfg & 2) != 0, charen = (cfg & 4) != 0;
        switch (a >> 12)
        {
            case 0xA:
            case 0xB:
                return loram && hiram ? _basic[a & 0x1FFF] : _ram[a];
            case 0xD:
                if (!loram && !hiram) return _ram[a];
                if (!charen) return _chargen[a & 0x0FFF];
                return IoRead(a, sideEffects);
            case 0xE:
            case 0xF:
                return hiram ? _kernal[a & 0x1FFF] : _ram[a];
            default:
                return _ram[a];
        }
    }

    private void CpuWrite(ushort a, byte v)
    {
        if (a == 0) _portDdr = v;
        else if (a == 1) _portData = v;
        if ((a >> 12) == 0xD)
        {
            int cfg = Config;
            if ((cfg & 3) != 0 && (cfg & 4) != 0)
            {
                IoWrite(a, v);
                return;
            }
        }
        _ram[a] = v;
    }

    private byte IoRead(ushort a, bool sideEffects)
    {
        switch ((a >> 8) & 0x0F)
        {
            case 0x0: case 0x1: case 0x2: case 0x3:
                return sideEffects ? Vic.Read(a) : Vic.Peek(a);
            case 0x4: case 0x5: case 0x6: case 0x7:
                return sideEffects ? Sid.Read(a) : Sid.Peek(a);
            case 0x8: case 0x9: case 0xA: case 0xB:
                return (byte)(_colorRam[a & 0x3FF] | 0xF0);
            case 0xC:
                return sideEffects ? Cia1.Read(a) : Cia1.Peek(a);
            case 0xD:
                return sideEffects ? Cia2.Read(a) : Cia2.Peek(a);
            default:
                return 0xFF;   // I/O 1 and 2: no cartridge
        }
    }

    private void IoWrite(ushort a, byte v)
    {
        switch ((a >> 8) & 0x0F)
        {
            case 0x0: case 0x1: case 0x2: case 0x3: Vic.Write(a, v); break;
            case 0x4: case 0x5: case 0x6: case 0x7: Sid.Write(a, v); break;
            case 0x8: case 0x9: case 0xA: case 0xB: _colorRam[a & 0x3FF] = (byte)(v & 0x0F); break;
            case 0xC: Cia1.Write(a, v); break;
            case 0xD: Cia2.Write(a, v); break;
        }
    }

    /// <summary>The VIC sees one 16 KB bank (chosen by CIA 2) and the character ROM at $1000 in banks 0 and 2.</summary>
    private byte VicFetch(int address)
    {
        int bank = 3 - (Cia2.PortAPins & 3);
        int full = (bank << 14) | (address & 0x3FFF);
        if ((bank & 1) == 0 && (address & 0x3000) == 0x1000)
            return _chargen[address & 0x0FFF];
        return _ram[full];
    }

    // ------------------------------------------------------------------ IMemoryView (debugger)
    public int AddressBits => 16;
    public byte Peek(uint address) => CpuRead((ushort)address, sideEffects: false);
    public void Poke(uint address, byte value)
    {
        ushort a = (ushort)address;
        if ((a >> 12) == 0xD && (a & 0x0C00) == 0x0800 && (Config & 4) != 0 && (Config & 3) != 0)
            _colorRam[a & 0x3FF] = (byte)(value & 0x0F);
        else
            _ram[a] = value;
    }

    // ------------------------------------------------------------------ running
    public RunStopReason RunFrame(DebugController? debug)
    {
        while (true)
        {
            if (debug != null && debug.Enabled && !_skipBreakpointOnce && debug.IsBreakpoint(_cpu.PC))
                return RunStopReason.Breakpoint;
            _skipBreakpointOnce = false;
            if (ExecutedMap != null)
                ExecutedMap[_cpu.PC] = true;
            WatchKernal();
            _cpu.Step();
            if (Vic.FrameDone)
            {
                Vic.FrameDone = false;
                FrameCount++;
                _sequencer.Tick(FrameCount, SetKey);
                return RunStopReason.FrameComplete;
            }
        }
    }

    /// <summary>The KERNAL's default IRQ handler ($0314 after boot), where "JMP $EA31" really wants to go.</summary>
    public ushort DefaultIrqHandler { get; set; } = 0xEA31;

    private void WatchKernal()
    {
        ushort pc = _cpu.PC;
        bool fromRam = _lastPc < 0xA000 || (_lastPc >= 0xC000 && _lastPc < 0xD000);
        if (fromRam && pc >= 0xE000 && pc < 0xFF81 && (Config & 2) != 0 && JumpedTo(_lastPc, pc))
        {
            if (KernalInternalCalls.Count < 64)
                KernalInternalCalls.Add(pc);
            if (OpenRoms)
                KernalTrap(pc);
        }
        _lastPc = _cpu.PC;
    }

    /// <summary>
    /// True when the instruction at <paramref name="from"/> is a JMP or JSR whose target is
    /// <paramref name="to"/>: a real call, not an RTI or RTS back into code that was interrupted.
    /// </summary>
    private bool JumpedTo(ushort from, ushort to)
    {
        byte op = _ram[from];
        ushort operand = (ushort)(_ram[(ushort)(from + 1)] | (_ram[(ushort)(from + 2)] << 8));
        if (op == 0x4C || op == 0x20)
            return operand == to;
        if (op == 0x6C)
        {
            // JMP (ind) with the 6502 page wrap of the pointer
            ushort hiAt = (ushort)((operand & 0xFF00) | ((operand + 1) & 0xFF));
            return (Peek(operand) | (Peek(hiAt) << 8)) == to;
        }
        return false;
    }

    // ------------------------------------------------------------------ Open ROMs compatibility
    // Internal routines of the original Commodore KERNAL that old programs call directly. The
    // free Open ROMs only keep the documented entry points, so these are done here when a
    // program in RAM jumps to them (the Open ROMs' own code never takes this path).
    private void KernalTrap(ushort pc)
    {
        switch (pc)
        {
            case 0xE518: _cpu.PC = 0xFF81; break;            // CINT body -> CINT entry
            case 0xFF5B: _cpu.PC = 0xFF81; break;            // CINT (the entry's target)
            case 0xFCE2: SoftReset(); break;                 // RESET: cartridge check first
            case 0xFDA3: _cpu.PC = 0xFF84; break;            // IOINIT
            case 0xFD50: _cpu.PC = 0xFF87; break;            // RAMTAS
            case 0xFD15: _cpu.PC = 0xFF8A; break;            // RESTOR
            case 0xEA31: _cpu.PC = DefaultIrqHandler; break; // standard IRQ handler
            case 0xE51B: EditorDefaults(); ClearScreen(); Return(); break;
            case 0xE536: _ram[0x0286] = _cpu.A; EditorDefaults(keepColor: true); ClearScreen(); Return(); break;
            case 0xE544: ClearScreen(); Return(); break;
            case 0xE566: HomeCursor(); Return(); break;
            case 0xE5A0: VicDefaults(); Return(); break;
            case 0xEA7E: Cia1.Read(0x0D); RestoreAndRti(); break;
            case 0xEA81: RestoreAndRti(); break;
        }
    }

    /// <summary>
    /// The KERNAL reset routine without a hardware reset: with the "CBM80" cartridge signature
    /// at $8004 it jumps through $8000 (a trick of crackers and freezers to restart their own
    /// code); otherwise it runs the firmware's normal reset.
    /// </summary>
    private void SoftReset()
    {
        _cpu.S = 0xFF;
        _cpu.P = (byte)((_cpu.P | 0x04) & ~0x08);
        bool cbm80 = Peek(0x8004) == 0xC3 && Peek(0x8005) == 0xC2 && Peek(0x8006) == 0xCD && Peek(0x8007) == 0x38 && Peek(0x8008) == 0x30;
        _cpu.PC = cbm80
            ? (ushort)(Peek(0x8000) | (Peek(0x8001) << 8))
            : (ushort)(_kernal[0x1FFC] | (_kernal[0x1FFD] << 8));
    }

    private byte PullRaw() => _ram[0x100 | ++_cpu.S];

    private void Return()
    {
        int lo = PullRaw(), hi = PullRaw();
        _cpu.PC = (ushort)(((hi << 8) | lo) + 1);
    }

    /// <summary>PLA TAY PLA TAX PLA RTI: the end of the KERNAL's interrupt handler.</summary>
    private void RestoreAndRti()
    {
        _cpu.Y = PullRaw();
        _cpu.X = PullRaw();
        _cpu.A = PullRaw();
        _cpu.P = (byte)((PullRaw() & 0xCF) | 0x20);
        int lo = PullRaw(), hi = PullRaw();
        _cpu.PC = (ushort)((hi << 8) | lo);
    }

    private void EditorDefaults(bool keepColor = false)
    {
        _ram[0x0291] = 0; _ram[0xCF] = 0;
        _ram[0x028F] = 0x48; _ram[0x0290] = 0xEB;
        _ram[0x0289] = 0x0A; _ram[0x028C] = 0x0A;
        if (!keepColor) _ram[0x0286] = 0x0E;
        _ram[0x028B] = 0x04; _ram[0xCD] = 0x0C; _ram[0xCC] = 0x0C;
    }

    private void ClearScreen()
    {
        int page = _ram[0x0288] == 0 ? 4 : _ram[0x0288];
        int screen = page << 8;
        for (int i = 0; i < 1000; i++)
        {
            _ram[(screen + i) & 0xFFFF] = 0x20;
            _colorRam[i] = (byte)(_ram[0x0286] & 0x0F);
        }
        for (int row = 0; row < 25; row++)   // screen line link table
            _ram[0xD9 + row] = (byte)(0x80 | ((screen + row * 40) >> 8));
        HomeCursor();
    }

    private void HomeCursor()
    {
        int screen = (_ram[0x0288] == 0 ? 4 : _ram[0x0288]) << 8;
        _ram[0xD3] = 0; _ram[0xD6] = 0;
        _ram[0xD1] = (byte)screen; _ram[0xD2] = (byte)(screen >> 8);
        _ram[0xF3] = 0x00; _ram[0xF4] = 0xD8;
    }

    private void VicDefaults()
    {
        byte[] defaults = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x9B, 0x37, 0, 0, 0, 0x08, 0, 0x14, 0x0F, 0, 0, 0, 0, 0, 0, 0x0E, 0x06, 0x01, 0x02, 0x03, 0x04, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        for (int i = 0; i < defaults.Length; i++)
            Vic.Write(i, defaults[i]);
    }

    public void StepInstruction()
    {
        if (ExecutedMap != null)
            ExecutedMap[_cpu.PC] = true;
        _cpu.Step();
        if (Vic.FrameDone)
        {
            Vic.FrameDone = false;
            FrameCount++;
        }
    }

    public void ResumeFromBreakpoint() => _skipBreakpointOnce = true;

    public int DrainAudio(short[] destination)
    {
        int n = Math.Min(destination.Length, _audioCount);
        Array.Copy(_audio, destination, n);
        Array.Copy(_audio, n, _audio, 0, _audioCount - n);
        _audioCount -= n;
        return n;
    }

    // ------------------------------------------------------------------ keyboard and joysticks
    /// <summary>Key matrix: column = CIA 1 port A bit, row = port B bit.</summary>
    private static readonly string[][] Matrix =
    {
        new[] { "DEL", "RETURN", "CRSR RIGHT", "F7", "F1", "F3", "F5", "CRSR DOWN" },
        new[] { "3", "W", "A", "4", "Z", "S", "E", "LSHIFT" },
        new[] { "5", "R", "D", "6", "C", "F", "T", "X" },
        new[] { "7", "Y", "G", "8", "B", "H", "U", "V" },
        new[] { "9", "I", "J", "0", "M", "K", "O", "N" },
        new[] { "+", "P", "L", "-", ".", ":", "@", "," },
        new[] { "POUND", "*", ";", "HOME", "RSHIFT", "=", "ARROW UP", "/" },
        new[] { "1", "ARROW LEFT", "CTRL", "2", "SPACE", "C=", "Q", "RUN/STOP" },
    };

    private static readonly Dictionary<string, (int col, int row)> KeyPositions = BuildPositions();

    private static Dictionary<string, (int, int)> BuildPositions()
    {
        var d = new Dictionary<string, (int, int)>();
        for (int c = 0; c < 8; c++)
            for (int r = 0; r < 8; r++)
                d[Matrix[c][r]] = (c, r);
        return d;
    }

    private static readonly string[] JoyNames = { "UP", "DOWN", "LEFT", "RIGHT", "FIRE" };

    public IReadOnlyList<MachineKey> Keys { get; } = Matrix.SelectMany(c => c).Select(k => new MachineKey(k, k))
        .Concat(new[] { new MachineKey("RESTORE", "RESTORE") })
        .Concat(JoyNames.Select(j => new MachineKey("JOY2 " + j, "Joystick 2 " + j)))
        .Concat(JoyNames.Select(j => new MachineKey("JOY1 " + j, "Joystick 1 " + j)))
        .ToList();

    public void SetKey(string name, bool pressed)
    {
        if (KeyPositions.TryGetValue(name, out var p))
        {
            _keys[p.col, p.row] = pressed;
            return;
        }
        if (name == "RESTORE")
        {
            _restore = pressed;
            return;
        }
        if (name.StartsWith("JOY"))
        {
            int bit = Array.IndexOf(JoyNames, name[5..]);
            if (bit < 0) return;
            ref byte joy = ref name[3] == '2' ? ref _joy2 : ref _joy1;
            joy = pressed ? (byte)(joy & ~(1 << bit)) : (byte)(joy | (1 << bit));
        }
    }

    public void ReleaseAllKeys()
    {
        Array.Clear(_keys);
        _joy1 = _joy2 = 0xFF;
        _restore = false;
    }

    public void QueueKeys(int framesHeld, params string[] keys) => _sequencer.Enqueue(framesHeld, keys);

    /// <summary>Rows pulled low by pressed keys in the columns CIA 1 drives low (and joystick 1, which shares port B).</summary>
    private byte KeyboardRows()
    {
        byte cols = (byte)(Cia1.PortAPins & _joy2);
        int rows = 0xFF;
        for (int c = 0; c < 8; c++)
        {
            if ((cols & (1 << c)) != 0)
                continue;
            for (int r = 0; r < 8; r++)
                if (_keys[c, r])
                    rows &= ~(1 << r);
        }
        return (byte)(rows & _joy1);
    }

    // ------------------------------------------------------------------ info
    private static readonly Dictionary<ushort, string> Symbols = new()
    {
        [0xFFD2] = "CHROUT", [0xFFE4] = "GETIN", [0xFFCF] = "CHRIN", [0xFFD5] = "LOAD", [0xFFD8] = "SAVE",
        [0xFFBA] = "SETLFS", [0xFFBD] = "SETNAM", [0xFFC0] = "OPEN", [0xFFC3] = "CLOSE", [0xFF81] = "CINT",
        [0xFF84] = "IOINIT", [0xFF87] = "RAMTAS", [0xFFE1] = "STOP", [0xFFDE] = "RDTIM", [0xFF9F] = "SCNKEY",
        [0xD011] = "VIC_CTRL1", [0xD012] = "VIC_RASTER", [0xD015] = "VIC_SPR_ENA", [0xD016] = "VIC_CTRL2",
        [0xD018] = "VIC_MEMPTR", [0xD019] = "VIC_IRQ", [0xD01A] = "VIC_IRQ_ENA", [0xD020] = "VIC_BORDER",
        [0xD021] = "VIC_BG0", [0xD418] = "SID_VOLUME", [0xDC00] = "CIA1_PRA", [0xDC01] = "CIA1_PRB",
        [0xDC0D] = "CIA1_ICR", [0xDD00] = "CIA2_PRA", [0xDD0D] = "CIA2_ICR", [0x0314] = "IRQ_VECTOR",
    };

    public string? SymbolFor(uint address) => Symbols.TryGetValue((ushort)address, out var s) ? s : null;

    /// <summary>
    /// The 40x25 text matrix the VIC-II is showing (wherever the game put it), as ASCII:
    /// letters, digits and punctuation in screen codes; anything else is a space.
    /// </summary>
    public string ScreenText()
    {
        int screen = (Vic.Peek(0x18) >> 4) * 0x400;
        var sb = new System.Text.StringBuilder(25 * 41);
        for (int row = 0; row < 25; row++)
        {
            for (int col = 0; col < 40; col++)
            {
                int c = VicFetch(screen + row * 40 + col) & 0x7F;   // bit 7: reversed
                sb.Append(c switch
                {
                    0 => '@',
                    < 27 => (char)('A' + c - 1),
                    >= 32 and < 64 => (char)c,
                    >= 65 and < 91 => (char)c,                       // capitals of the lower case set
                    _ => ' ',
                });
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public IReadOnlyList<string> GetMachineInfo() => new[]
    {
        $"Cuadro {FrameCount}, {Vic.Describe()}",
        $"CIA 1: {Cia1.Describe()}",
        $"CIA 2: {Cia2.Describe()} | banco VIC {3 - (Cia2.PortAPins & 3)}",
        $"Puerto 6510: {_portData:X2} (dirección {_portDdr:X2}) -> {(Config & 3) switch { 3 => "BASIC+KERNAL", 2 => "KERNAL", 1 => "solo RAM arriba", _ => "todo RAM" }}{((Config & 4) != 0 ? ", E/S" : ", ROM de caracteres")}",
        $"Firmware: {FirmwareDescription}",
    };

    // ------------------------------------------------------------------ save states
    private const string StateMagic = "RETRO-C64";

    public byte[] SaveState()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(StateMagic);
        w.Write(1);
        _cpu.Save(w);
        w.Write(_ram);
        w.Write(_colorRam);
        w.Write(_portDdr);
        w.Write(_portData);
        Vic.Save(w);
        Sid.Save(w);
        Cia1.Save(w);
        Cia2.Save(w);
        w.Write(FrameCount);
        w.Write(_sampleCarry);
        w.Write(_todCarry);
        return ms.ToArray();
    }

    public void LoadState(byte[] state)
    {
        using var r = new BinaryReader(new MemoryStream(state));
        if (r.ReadString() != StateMagic || r.ReadInt32() != 1)
            throw new InvalidDataException("No es un estado guardado de Commodore 64.");
        _cpu.Load(r);
        r.Read(_ram, 0, _ram.Length);
        r.Read(_colorRam, 0, _colorRam.Length);
        _portDdr = r.ReadByte();
        _portData = r.ReadByte();
        Vic.Load(r);
        Sid.Load(r);
        Cia1.Load(r);
        Cia2.Load(r);
        FrameCount = r.ReadInt64();
        _sampleCarry = r.ReadDouble();
        _todCarry = r.ReadInt64();
        _audioCount = 0;
        ReleaseAllKeys();
    }
}
