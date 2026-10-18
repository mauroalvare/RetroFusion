using Retro.Core;

namespace Retro.Cpu.M6502;

/// <summary>
/// The memory and interrupt lines seen by a 6502. Every Read or Write is one clock cycle,
/// in the same order as the real chip (dummy reads and writes included), so machines that
/// share the bus with video chips (C64, Atari, Apple II) can count cycles exactly. A
/// machine that has to stop the CPU (the C64's VIC-II with RDY) does it inside Read.
/// </summary>
public interface IM6502Bus
{
    byte Read(ushort address);
    void Write(ushort address, byte value);
    /// <summary>IRQ input, level triggered (true = asserted).</summary>
    bool IrqLine { get; }
    /// <summary>NMI input; the CPU reacts to the moment it becomes asserted.</summary>
    bool NmiLine { get; }
}

/// <summary>
/// NMOS 6502 (also the 6510 of the C64, the 7501/8501 of the Plus/4 and, without decimal
/// mode, the 2A03 of the NES). All 256 opcodes, including the undocumented ones the games
/// use, with the exact bus cycle sequence of the real chip and NMOS decimal mode.
/// Verified against Tom Harte's SingleStepTests (every bus cycle) and Klaus Dormann's
/// functional test.
/// </summary>
public sealed partial class M6502 : ICpuDebugView
{
    public const byte FlagC = 0x01, FlagZ = 0x02, FlagI = 0x04, FlagD = 0x08, FlagB = 0x10, FlagU = 0x20, FlagV = 0x40, FlagN = 0x80;

    private readonly IM6502Bus _bus;
    public byte A, X, Y, S = 0xFD, P = FlagU | FlagI;
    public ushort PC;
    /// <summary>Clock cycles executed (one per bus access).</summary>
    public long CycleCount;
    public long Instructions;
    /// <summary>The 2A03 of the NES has no decimal mode.</summary>
    public bool DecimalEnabled { get; set; } = true;
    /// <summary>A JAM (KIL) opcode halts the CPU until reset.</summary>
    public bool Jammed { get; private set; }
    public string Name { get; init; } = "MOS 6502";

    private bool _nmiPending, _nmiPrevious;
    private bool _irqSampled, _irqSampledPrevious;

    public M6502(IM6502Bus bus) => _bus = bus;

    // ------------------------------------------------------------------ bus cycles
    private byte Read(ushort address)
    {
        byte v = _bus.Read(address);
        Cycle();
        return v;
    }

    private void Write(ushort address, byte value)
    {
        _bus.Write(address, value);
        Cycle();
    }

    /// <summary>Interrupts are sampled every cycle; the decision uses the next-to-last cycle of the instruction.</summary>
    private void Cycle()
    {
        CycleCount++;
        bool nmi = _bus.NmiLine;
        if (nmi && !_nmiPrevious)
            _nmiPending = true;
        _nmiPrevious = nmi;
        _irqSampledPrevious = _irqSampled;
        _irqSampled = _bus.IrqLine && (P & FlagI) == 0;
    }

    private byte Fetch() => Read(PC++);

    private void Push(byte v)
    {
        Write((ushort)(0x100 | S), v);
        S--;
    }

    private byte Pull()
    {
        S++;
        return Read((ushort)(0x100 | S));
    }

    // ------------------------------------------------------------------ control
    /// <summary>Power-on or reset: the 6502 does three fake pushes (no writes) and reads the reset vector.</summary>
    public void Reset()
    {
        Jammed = false;
        Read(PC);
        Read(PC);
        Read((ushort)(0x100 | S)); S--;
        Read((ushort)(0x100 | S)); S--;
        Read((ushort)(0x100 | S)); S--;
        P |= FlagI;
        byte lo = Read(0xFFFC);
        byte hi = Read(0xFFFD);
        PC = (ushort)(lo | (hi << 8));
        _nmiPending = false;
    }

    /// <summary>Executes one instruction, or takes a pending interrupt.</summary>
    public void Step()
    {
        if (Jammed)
        {
            Read(0xFFFF);
            return;
        }
        if (_nmiPending || _irqSampledPrevious && _irqPendingCheck)
        {
            Interrupt();
            _irqPendingCheck = false;
            return;
        }
        Execute(Fetch());
        Instructions++;
        // the IRQ decision of the next boundary uses the next-to-last cycle of this instruction
        _irqPendingCheck = true;
    }

    private bool _irqPendingCheck;

    private void Interrupt()
    {
        Read(PC);
        Read(PC);
        Push((byte)(PC >> 8));
        Push((byte)PC);
        // an NMI that arrives while an IRQ is being taken takes over its vector
        bool nmi = _nmiPending;
        Push((byte)((P | FlagU) & ~FlagB));
        P |= FlagI;
        ushort vector = nmi ? (ushort)0xFFFA : (ushort)0xFFFE;
        if (nmi)
            _nmiPending = false;
        byte lo = Read(vector);
        byte hi = Read((ushort)(vector + 1));
        PC = (ushort)(lo | (hi << 8));
    }

    // ------------------------------------------------------------------ flags
    private void NZ(byte v) => P = (byte)((P & ~(FlagN | FlagZ)) | (v & FlagN) | (v == 0 ? FlagZ : 0));
    private void SetFlag(byte flag, bool on) => P = on ? (byte)(P | flag) : (byte)(P & ~flag);
    private bool Flag(byte flag) => (P & flag) != 0;

    // ------------------------------------------------------------------ addressing
    private enum Kind { Read, Write, Modify }

    private ushort Indexed(byte lo, byte hi, byte index, Kind kind)
    {
        int sum = lo + index;
        // the first access uses the high byte before the carry: an extra (dummy) read when
        // the page is crossed, and always for writes and read-modify-writes
        if (sum > 0xFF || kind != Kind.Read)
            Read((ushort)((hi << 8) | (sum & 0xFF)));
        return (ushort)(((hi << 8) + sum) & 0xFFFF);
    }

    private ushort Zp() => Fetch();

    private ushort ZpIndexed(byte index)
    {
        byte z = Fetch();
        Read(z);
        return (byte)(z + index);
    }

    private ushort Abs()
    {
        byte lo = Fetch();
        byte hi = Fetch();
        return (ushort)(lo | (hi << 8));
    }

    private ushort AbsIndexed(byte index, Kind kind)
    {
        byte lo = Fetch();
        byte hi = Fetch();
        return Indexed(lo, hi, index, kind);
    }

    private ushort IndX()
    {
        byte z = Fetch();
        Read(z);
        z += X;
        byte lo = Read(z);
        byte hi = Read((byte)(z + 1));
        return (ushort)(lo | (hi << 8));
    }

    private ushort IndY(Kind kind)
    {
        byte z = Fetch();
        byte lo = Read(z);
        byte hi = Read((byte)(z + 1));
        return Indexed(lo, hi, Y, kind);
    }

    // ------------------------------------------------------------------ operations
    private void Adc(byte v)
    {
        int c = P & FlagC;
        if (Flag(FlagD) && DecimalEnabled)
        {
            int tmp = (A & 0xF) + (v & 0xF) + c;
            if (tmp > 9) tmp += 6;
            tmp = tmp <= 0xF ? (tmp & 0xF) + (A & 0xF0) + (v & 0xF0) : (tmp & 0xF) + (A & 0xF0) + (v & 0xF0) + 0x10;
            SetFlag(FlagZ, ((A + v + c) & 0xFF) == 0);
            SetFlag(FlagN, (tmp & 0x80) != 0);
            SetFlag(FlagV, ((A ^ tmp) & 0x80) != 0 && ((A ^ v) & 0x80) == 0);
            if ((tmp & 0x1F0) > 0x90) tmp += 0x60;
            SetFlag(FlagC, (tmp & 0xFF0) > 0xF0);
            A = (byte)tmp;
        }
        else
        {
            int t = A + v + c;
            SetFlag(FlagV, (~(A ^ v) & (A ^ t) & 0x80) != 0);
            SetFlag(FlagC, t > 0xFF);
            A = (byte)t;
            NZ(A);
        }
    }

    private void Sbc(byte v)
    {
        int borrow = Flag(FlagC) ? 0 : 1;
        if (Flag(FlagD) && DecimalEnabled)
        {
            int tmp = A - v - borrow;
            int ta = (A & 0xF) - (v & 0xF) - borrow;
            ta = (ta & 0x10) != 0 ? ((ta - 6) & 0xF) | ((A & 0xF0) - (v & 0xF0) - 0x10) : (ta & 0xF) | ((A & 0xF0) - (v & 0xF0));
            if ((ta & 0x100) != 0) ta -= 0x60;
            SetFlag(FlagC, tmp >= 0);
            NZ((byte)tmp);
            SetFlag(FlagV, ((A ^ tmp) & 0x80) != 0 && ((A ^ v) & 0x80) != 0);
            A = (byte)ta;
        }
        else
        {
            int t = A - v - borrow;
            SetFlag(FlagV, ((A ^ v) & (A ^ t) & 0x80) != 0);
            SetFlag(FlagC, t >= 0);
            A = (byte)t;
            NZ(A);
        }
    }

    private void Compare(byte r, byte v)
    {
        int t = r - v;
        SetFlag(FlagC, t >= 0);
        NZ((byte)t);
    }

    private byte Asl(byte v) { SetFlag(FlagC, (v & 0x80) != 0); v <<= 1; NZ(v); return v; }
    private byte Lsr(byte v) { SetFlag(FlagC, (v & 1) != 0); v >>= 1; NZ(v); return v; }
    private byte Rol(byte v) { int c = P & FlagC; SetFlag(FlagC, (v & 0x80) != 0); v = (byte)((v << 1) | c); NZ(v); return v; }
    private byte Ror(byte v) { int c = P & FlagC; SetFlag(FlagC, (v & 1) != 0); v = (byte)((v >> 1) | (c << 7)); NZ(v); return v; }
    private byte Inc(byte v) { v++; NZ(v); return v; }
    private byte Dec(byte v) { v--; NZ(v); return v; }

    private void Bit(byte v)
    {
        SetFlag(FlagZ, (A & v) == 0);
        P = (byte)((P & 0x3F) | (v & 0xC0));
    }

    private void Arr(byte v)
    {
        int t = A & v;
        int c = P & FlagC;
        if (Flag(FlagD) && DecimalEnabled)
        {
            int r = (t >> 1) | (c << 7);
            NZ((byte)r);
            SetFlag(FlagV, ((r ^ t) & 0x40) != 0);
            if ((t & 0xF) + (t & 1) > 5)
                r = (r & 0xF0) | ((r + 6) & 0xF);
            bool carry = (t & 0xF0) + (t & 0x10) > 0x50;
            if (carry)
                r = (r & 0x0F) | ((r + 0x60) & 0xF0);
            SetFlag(FlagC, carry);
            A = (byte)r;
        }
        else
        {
            byte r = (byte)((t >> 1) | (c << 7));
            A = r;
            NZ(r);
            SetFlag(FlagC, (r & 0x40) != 0);
            SetFlag(FlagV, (((r & 0x40) >> 6) ^ ((r & 0x20) >> 5)) != 0);
        }
    }

    private void Branch(bool taken)
    {
        sbyte offset = (sbyte)Fetch();
        if (!taken)
            return;
        Read(PC);
        ushort target = (ushort)(PC + offset);
        if ((target & 0xFF00) != (PC & 0xFF00))
            Read((ushort)((PC & 0xFF00) | (target & 0xFF)));
        PC = target;
    }

    private void Rmw(ushort a, Func<byte, byte> op)
    {
        byte v = Read(a);
        Write(a, v);          // the 6502 writes the old value back first
        Write(a, op(v));
    }

    /// <summary>SHA/SHX/SHY/TAS: the stored value is ANDed with the high byte of the base address + 1, and replaces that high byte when the index crosses a page.</summary>
    private void StoreHigh(byte lo, byte hi, byte index, byte value)
    {
        int sum = lo + index;
        Read((ushort)((hi << 8) | (sum & 0xFF)));
        byte v = (byte)(value & (hi + 1));
        ushort address = sum > 0xFF ? (ushort)((v << 8) | (sum & 0xFF)) : (ushort)((hi << 8) + sum);
        Write(address, v);
    }

    private void Jam()
    {
        // what the address bus shows on a jammed NMOS 6502
        Read(PC);
        Read(0xFFFF);
        Read(0xFFFE);
        Read(0xFFFE);
        for (int i = 0; i < 6; i++)
            Read(0xFFFF);
        Jammed = true;
    }

    // ------------------------------------------------------------------ save states
    public void Save(BinaryWriter w)
    {
        w.Write(A); w.Write(X); w.Write(Y); w.Write(S); w.Write(P); w.Write(PC);
        w.Write(CycleCount); w.Write(Instructions); w.Write(Jammed);
        w.Write(_nmiPending); w.Write(_nmiPrevious); w.Write(_irqSampled); w.Write(_irqSampledPrevious); w.Write(_irqPendingCheck);
    }

    public void Load(BinaryReader r)
    {
        A = r.ReadByte(); X = r.ReadByte(); Y = r.ReadByte(); S = r.ReadByte(); P = r.ReadByte(); PC = r.ReadUInt16();
        CycleCount = r.ReadInt64(); Instructions = r.ReadInt64(); Jammed = r.ReadBoolean();
        _nmiPending = r.ReadBoolean(); _nmiPrevious = r.ReadBoolean(); _irqSampled = r.ReadBoolean(); _irqSampledPrevious = r.ReadBoolean(); _irqPendingCheck = r.ReadBoolean();
    }

    // ------------------------------------------------------------------ ICpuDebugView
    public string CpuName => Name;
    public uint ProgramCounter { get => PC; set => PC = (ushort)value; }
    public uint StackPointer => (uint)(0x100 | S);
    public long Cycles => CycleCount;

    public IReadOnlyList<RegisterValue> GetRegisters() => new[]
    {
        new RegisterValue("A", 8, A), new RegisterValue("X", 8, X), new RegisterValue("Y", 8, Y),
        new RegisterValue("S", 8, S), new RegisterValue("P", 8, P), new RegisterValue("PC", 16, PC),
    };

    public bool TrySetRegister(string name, uint value)
    {
        switch (name)
        {
            case "A": A = (byte)value; return true;
            case "X": X = (byte)value; return true;
            case "Y": Y = (byte)value; return true;
            case "S": S = (byte)value; return true;
            case "P": P = (byte)(value | FlagU); return true;
            case "PC": PC = (ushort)value; return true;
            default: return false;
        }
    }

    private static readonly (string name, byte bit, string description)[] FlagInfo =
    {
        ("N", FlagN, "Negativo (bit 7 del resultado)"), ("V", FlagV, "Desborde con signo"),
        ("B", FlagB, "Break (solo existe en la copia guardada en la pila)"), ("D", FlagD, "Modo decimal (BCD)"),
        ("I", FlagI, "Interrupciones IRQ deshabilitadas"), ("Z", FlagZ, "Cero"), ("C", FlagC, "Acarreo"),
    };

    public IReadOnlyList<FlagValue> GetFlags() => FlagInfo.Select(f => new FlagValue(f.name, (P & f.bit) != 0, f.description)).ToList();

    public bool TrySetFlag(string name, bool value)
    {
        var f = FlagInfo.FirstOrDefault(x => x.name == name);
        if (f.name == null)
            return false;
        SetFlag(f.bit, value);
        return true;
    }

    public IReadOnlyList<string> GetInterruptState() => new[]
    {
        $"IRQ: {(_bus.IrqLine ? "pedida" : "-")}{(Flag(FlagI) ? " (enmascarada por I)" : "")}",
        $"NMI: {(_nmiPending ? "pendiente" : "-")}",
        Jammed ? "CPU TRABADA (opcode JAM): hace falta un reset" : $"Ciclos: {CycleCount}",
    };
}
