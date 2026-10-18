using Retro.Core;
using Retro.Cpu.M6502;

namespace Retro.Systems.Nes;

/// <summary>
/// Nintendo NES (NTSC): Ricoh 2A03 (a 6502 without decimal mode) at 1,789,773 Hz, 2 KB of
/// RAM mirrored up to $1FFF, the 2C02 PPU at $2000-$3FFF (three dots per CPU cycle), OAM DMA
/// at $4014, two controllers at $4016/$4017, 8 KB of PRG RAM at $6000-$7FFF and the
/// cartridge at $8000-$FFFF. The CPU drives the clock: every bus access is one cycle.
/// The APU is not emulated yet: its registers are accepted and the machine is silent.
/// </summary>
public sealed class Nes : IMachine, IM6502Bus, IMemoryView, IWriteWatch, ICodePatch
{
    public const double Clock = 1789773;
    /// <summary>Average CPU cycles per frame: 341 x 262 dots / 3 (one dot less on odd frames).</summary>
    public const double CyclesPerFrame = Ppu.DotsPerLine * Ppu.LinesPerFrame / 3.0;

    /// <summary>Controller buttons in the order the shift register sends them.</summary>
    public static readonly string[] Buttons = { "A", "B", "SELECT", "START", "UP", "DOWN", "LEFT", "RIGHT" };

    private readonly byte[] _ram = new byte[0x800];
    private readonly byte[] _prgRam = new byte[0x2000];
    private readonly M6502 _cpu;
    private readonly M6502Disassembler _disassembler = new();
    private readonly byte[] _pads = new byte[2];       // buttons held, bit 0 = A
    private readonly byte[] _shift = new byte[2];      // what the next reads of $4016/$4017 return
    private bool _strobe;
    private byte _openBus;
    private readonly KeySequencer _sequencer = new();
    private readonly short[] _audio = new short[8192];
    private int _audioCount;
    private double _sampleCarry;
    private const double CyclesPerSample = Clock / 44100;
    private bool _skipBreakpointOnce;
    private ushort _instructionPc;

    public NesCartridge Cartridge { get; }
    public Ppu Ppu { get; }

    public Nes(NesCartridge cartridge)
    {
        Cartridge = cartridge;
        Ppu = new Ppu(cartridge);
        _cpu = new M6502(this) { Name = "Ricoh 2A03", DecimalEnabled = false };
        _disassembler.Symbols = a => SymbolFor(a);
        Reset(true);
    }

    public M6502 Cpu2A03 => _cpu;
    public byte[] Ram => _ram;

    // ------------------------------------------------------------------ IMachine basics
    public string Id => "nes";
    public string DisplayName => "Nintendo NES";
    public ICpuDebugView Cpu => _cpu;
    public IDisassembler Disassembler => _disassembler;
    public IMemoryView Memory => this;
    public VideoFrame Video => Ppu.Frame;
    public AudioBuffer Audio { get; } = new(44100, 4096);
    public double FramesPerSecond => Clock / CyclesPerFrame;
    public long FrameCount { get; private set; }
    public IFlowAnalyzer Flow { get; } = new M6502Flow();
    public bool[]? ExecutedMap { get; set; }
    public string MemoryMapDescription =>
        "0000-07FF RAM (espejada hasta 1FFF) | 2000-2007 PPU (espejada hasta 3FFF) | 4000-4017 APU y mandos | 6000-7FFF PRG RAM | 8000-FFFF cartucho";

    public IReadOnlyList<uint> CodeEntryPoints
    {
        get
        {
            var list = new List<uint>();
            foreach (ushort v in new ushort[] { 0xFFFC, 0xFFFA, 0xFFFE })
                list.Add((uint)(Peek(v) | (Peek((ushort)(v + 1)) << 8)));
            return list;
        }
    }

    public void Reset(bool hard)
    {
        if (hard)
        {
            Array.Clear(_ram);
            Array.Clear(_prgRam);
        }
        Ppu.Reset();
        _strobe = false;
        ReleaseAllKeys();
        _cpu.Reset();
        FrameCount = 0;
    }

    // ------------------------------------------------------------------ the bus
    public byte Read(ushort address)
    {
        byte v = CpuRead(address, sideEffects: true);
        Tick();
        return v;
    }

    public void Write(ushort address, byte value)
    {
        CpuWrite(address, value);
        WriteWatch?.Invoke(address, value, _instructionPc);
        Tick();
    }

    public Action<ushort, byte, ushort>? WriteWatch { get; set; }

    /// <summary>No IRQ sources yet: the APU frame counter and IRQ mappers come later.</summary>
    public bool IrqLine => false;
    public bool NmiLine => Ppu.Nmi;

    private void Tick()
    {
        Ppu.Step();
        Ppu.Step();
        Ppu.Step();
        _sampleCarry += 1;
        if (_sampleCarry >= CyclesPerSample)
        {
            _sampleCarry -= CyclesPerSample;
            if (_audioCount < _audio.Length)
                _audio[_audioCount++] = 0;     // silence until the APU exists
        }
    }

    private byte CpuRead(ushort a, bool sideEffects)
    {
        byte v;
        if (a < 0x2000) v = _ram[a & 0x7FF];
        else if (a < 0x4000) v = sideEffects ? Ppu.ReadRegister(a) : PeekPpu(a);
        else if (a == 0x4016 || a == 0x4017) v = sideEffects ? ReadPad(a & 1) : (byte)(_openBus & 0xE0 | _shift[a & 1] & 1);
        else if (a < 0x4020) v = a == 0x4015 ? (byte)0 : _openBus;
        else if (a < 0x6000) v = _openBus;
        else if (a < 0x8000) v = _prgRam[a & 0x1FFF];
        else v = Cartridge.CpuRead(a);
        if (sideEffects)
            _openBus = v;
        return v;
    }

    /// <summary>What the debugger shows for a PPU register: reading it for real would change it.</summary>
    private byte PeekPpu(ushort a) => (a & 7) switch
    {
        3 => Ppu.OamAddress,
        4 => Ppu.Oam[Ppu.OamAddress],
        _ => _openBus,
    };

    private void CpuWrite(ushort a, byte v)
    {
        _openBus = v;
        if (a < 0x2000) _ram[a & 0x7FF] = v;
        else if (a < 0x4000) Ppu.WriteRegister(a, v);
        else if (a == 0x4014) OamDma(v);
        else if (a == 0x4016) WriteStrobe(v);
        else if (a < 0x6000) { }                       // APU (not emulated yet) and expansion
        else if (a < 0x8000) _prgRam[a & 0x1FFF] = v;
        else Cartridge.CpuWrite(a, v);
    }

    /// <summary>
    /// $4014: the CPU stops while 256 bytes of page <paramref name="page"/> are copied to OAM
    /// through $2004: one idle cycle (two on an odd cycle) and a read and a write per byte.
    /// </summary>
    private void OamDma(byte page)
    {
        int idle = (_cpu.CycleCount & 1) == 1 ? 2 : 1;
        for (int i = 0; i < idle; i++)
            Tick();
        for (int i = 0; i < 256; i++)
        {
            byte v = CpuRead((ushort)(page << 8 | i), sideEffects: true);
            Tick();
            Ppu.WriteRegister(0x2004, v);
            Tick();
        }
        _cpu.CycleCount += idle + 512;
    }

    // ------------------------------------------------------------------ controllers
    private void WriteStrobe(byte v)
    {
        _strobe = (v & 1) != 0;
        if (_strobe)
            Latch();
    }

    private void Latch()
    {
        _shift[0] = _pads[0];
        _shift[1] = _pads[1];
    }

    /// <summary>One bit per read, A first; after the eighth, a standard controller returns 1.</summary>
    private byte ReadPad(int port)
    {
        if (_strobe)
            Latch();
        byte bit = (byte)(_shift[port] & 1);
        _shift[port] = (byte)(_shift[port] >> 1 | 0x80);
        return (byte)(_openBus & 0xE0 | bit);
    }

    public IReadOnlyList<MachineKey> Keys { get; } = new[] { "P1", "P2" }
        .SelectMany(p => Buttons.Select(b => new MachineKey($"{p} {b}", $"Mando {p[1]} {b}")))
        .ToList();

    public void SetKey(string name, bool pressed)
    {
        if (name.Length < 4 || name[0] != 'P' || name[2] != ' ' || name[1] is not ('1' or '2'))
            return;
        int bit = Array.IndexOf(Buttons, name[3..]);
        if (bit < 0)
            return;
        ref byte pad = ref _pads[name[1] - '1'];
        pad = pressed ? (byte)(pad | 1 << bit) : (byte)(pad & ~(1 << bit));
    }

    public void ReleaseAllKeys()
    {
        _pads[0] = _pads[1] = 0;
    }

    public void QueueKeys(int framesHeld, params string[] keys) => _sequencer.Enqueue(framesHeld, keys);

    // ------------------------------------------------------------------ IMemoryView (debugger)
    public int AddressBits => 16;
    public byte Peek(uint address) => CpuRead((ushort)address, sideEffects: false);

    public void Poke(uint address, byte value)
    {
        ushort a = (ushort)address;
        if (a < 0x2000) _ram[a & 0x7FF] = value;
        else if (a >= 0x6000 && a < 0x8000) _prgRam[a & 0x1FFF] = value;
    }

    public void PatchCode(uint address, byte value)
    {
        ushort a = (ushort)address;
        if (a >= 0x8000)
            Cartridge.PatchPrg(a, value);
        else
            Poke(a, value);
    }

    // ------------------------------------------------------------------ running
    public RunStopReason RunFrame(DebugController? debug)
    {
        while (true)
        {
            if (debug != null && debug.Enabled && !_skipBreakpointOnce && debug.IsBreakpoint(_cpu.PC))
                return RunStopReason.Breakpoint;
            _skipBreakpointOnce = false;
            StepInstruction();
            if (_frameEnded)
            {
                _frameEnded = false;
                _sequencer.Tick(FrameCount, SetKey);
                return RunStopReason.FrameComplete;
            }
        }
    }

    private bool _frameEnded;

    public void StepInstruction()
    {
        if (ExecutedMap != null)
            ExecutedMap[_cpu.PC] = true;
        _instructionPc = _cpu.PC;
        _cpu.Step();
        if (Ppu.FrameReady)
        {
            Ppu.FrameReady = false;
            FrameCount++;
            _frameEnded = true;
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

    // ------------------------------------------------------------------ info
    private static readonly Dictionary<ushort, string> Symbols = new()
    {
        [0x2000] = "PPUCTRL", [0x2001] = "PPUMASK", [0x2002] = "PPUSTATUS", [0x2003] = "OAMADDR",
        [0x2004] = "OAMDATA", [0x2005] = "PPUSCROLL", [0x2006] = "PPUADDR", [0x2007] = "PPUDATA",
        [0x4014] = "OAMDMA", [0x4015] = "APU_STATUS", [0x4016] = "JOY1", [0x4017] = "JOY2_FRAMECNT",
    };

    public string? SymbolFor(uint address) => Symbols.TryGetValue((ushort)address, out var s) ? s : null;

    public IReadOnlyList<string> GetMachineInfo() => new[]
    {
        $"Cuadro {FrameCount}, línea {Ppu.Line}, punto {Ppu.Dot}{(Ppu.Line >= Ppu.VblankLine && Ppu.Line < Ppu.PreRenderLine ? " (vblank)" : "")}",
        $"PPU: v={Ppu.ScrollV:X4} t={Ppu.ScrollT:X4} x={Ppu.FineX} | imagen {(Ppu.RenderingEnabled ? "activa" : "apagada")} | NMI {(Ppu.Nmi ? "pedida" : "-")}",
        $"Mandos: 1={_pads[0]:X2} 2={_pads[1]:X2}",
        Cartridge.Describe(),
        "Sonido: el APU todavía no está emulado",
    };

    // ------------------------------------------------------------------ save states
    private const string StateMagic = "RETRO-NES";

    public byte[] SaveState()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(StateMagic);
        w.Write(1);
        _cpu.Save(w);
        w.Write(_ram);
        w.Write(_prgRam);
        Ppu.Save(w);
        Cartridge.Save(w);
        w.Write(_strobe);
        w.Write(_shift);
        w.Write(_openBus);
        w.Write(FrameCount);
        w.Write(_sampleCarry);
        return ms.ToArray();
    }

    public void LoadState(byte[] state)
    {
        using var r = new BinaryReader(new MemoryStream(state));
        if (r.ReadString() != StateMagic || r.ReadInt32() != 1)
            throw new InvalidDataException("No es un estado guardado de NES.");
        _cpu.Load(r);
        r.Read(_ram, 0, _ram.Length);
        r.Read(_prgRam, 0, _prgRam.Length);
        Ppu.Load(r);
        Cartridge.Load(r);
        _strobe = r.ReadBoolean();
        r.Read(_shift, 0, _shift.Length);
        _openBus = r.ReadByte();
        FrameCount = r.ReadInt64();
        _sampleCarry = r.ReadDouble();
        _audioCount = 0;
        ReleaseAllKeys();
    }
}
