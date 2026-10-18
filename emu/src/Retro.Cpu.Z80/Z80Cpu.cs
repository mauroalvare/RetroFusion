using System.Runtime.CompilerServices;
using Retro.Core;

namespace Retro.Cpu.Z80;

/// <summary>
/// Zilog Z80 interpreter with cycle-exact bus timing.
///
/// Every memory and I/O cycle is issued at the T-state it happens on real hardware,
/// including the "no MREQ" cycles where the address bus still shows an address (these
/// matter on machines with contended memory). Undocumented behaviour is implemented:
/// flags 3 and 5, MEMPTR (WZ), the Q register used by SCF/CCF, IXH/IXL/IYH/IYL,
/// SLL, the DDCB register copies and the duplicated ED opcodes.
/// Timing and flag behaviour follow the FUSE emulator core, which is validated
/// against real hardware; the FUSE test suite runs as part of this project's tests.
/// </summary>
public sealed partial class Z80Cpu : ICpuDebugView
{
    public const byte FC = 0x01, FN = 0x02, FP = 0x04, FV = 0x04, F3 = 0x08, FH = 0x10, F5 = 0x20, FZ = 0x40, FS = 0x80;

    public byte A, F, B, C, D, E, H, L;
    public byte A_, F_, B_, C_, D_, E_, H_, L_;
    public ushort IX, IY, SP, PC;
    /// <summary>Internal MEMPTR register; only visible through flags 3/5 of BIT n,(HL).</summary>
    public ushort WZ;
    public byte I, R;
    public bool IFF1, IFF2;
    public int IM;
    public bool Halted;
    /// <summary>Flags written by the last instruction (0 if it did not write flags).</summary>
    public byte Q;
    private byte _prevQ;

    /// <summary>T-state counter. Machines read it to drive video, audio and interrupts.</summary>
    public long T;

    /// <summary>Level of the /INT pin, driven by the machine.</summary>
    public bool IntLine;
    private bool _nmiPending;
    /// <summary>True right after EI: interrupts are not accepted until one more instruction runs.</summary>
    private bool _eiDelay;

    private readonly IZ80Bus _bus;

    public Z80Cpu(IZ80Bus bus)
    {
        _bus = bus;
        Reset();
    }

    public IZ80Bus Bus => _bus;

    public void Reset()
    {
        // State after /RESET: PC=0, I=R=0, interrupts disabled, IM 0. AF and SP are
        // FFFF on real chips; the other registers are undefined.
        PC = 0;
        I = R = 0;
        IFF1 = IFF2 = false;
        IM = 0;
        Halted = false;
        A = F = 0xFF;
        SP = 0xFFFF;
        WZ = 0;
        Q = _prevQ = 0;
        _eiDelay = false;
        _nmiPending = false;
    }

    // -- register pairs -------------------------------------------------------
    public ushort AF { get => (ushort)((A << 8) | F); set { A = (byte)(value >> 8); F = (byte)value; } }
    public ushort BC { get => (ushort)((B << 8) | C); set { B = (byte)(value >> 8); C = (byte)value; } }
    public ushort DE { get => (ushort)((D << 8) | E); set { D = (byte)(value >> 8); E = (byte)value; } }
    public ushort HL { get => (ushort)((H << 8) | L); set { H = (byte)(value >> 8); L = (byte)value; } }
    public ushort AF_ { get => (ushort)((A_ << 8) | F_); set { A_ = (byte)(value >> 8); F_ = (byte)value; } }
    public ushort BC_ { get => (ushort)((B_ << 8) | C_); set { B_ = (byte)(value >> 8); C_ = (byte)value; } }
    public ushort DE_ { get => (ushort)((D_ << 8) | E_); set { D_ = (byte)(value >> 8); E_ = (byte)value; } }
    public ushort HL_ { get => (ushort)((H_ << 8) | L_); set { H_ = (byte)(value >> 8); L_ = (byte)value; } }
    private ushort IR => (ushort)((I << 8) | R);

    // -- flag tables -------------------------------------------------------------
    private static readonly byte[] SZ53 = new byte[256];
    private static readonly byte[] SZ53P = new byte[256];
    private static readonly byte[] Parity = new byte[256];
    private static readonly byte[] HalfcarryAdd = { 0, FH, FH, FH, 0, 0, 0, FH };
    private static readonly byte[] HalfcarrySub = { 0, 0, FH, 0, FH, 0, FH, FH };
    private static readonly byte[] OverflowAdd = { 0, 0, 0, FV, FV, 0, 0, 0 };
    private static readonly byte[] OverflowSub = { 0, FV, 0, 0, 0, 0, FV, 0 };

    static Z80Cpu()
    {
        for (int i = 0; i < 256; i++)
        {
            SZ53[i] = (byte)(i & (F3 | F5 | FS));
            int p = 0;
            for (int k = 0, j = i; k < 8; k++, j >>= 1)
                p ^= j & 1;
            Parity[i] = (byte)(p != 0 ? 0 : FP);
            SZ53P[i] = (byte)(SZ53[i] | Parity[i]);
        }
        SZ53[0] |= FZ;
        SZ53P[0] |= FZ;
    }

    // -- bus cycles ---------------------------------------------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void IncR() => R = (byte)((R & 0x80) | ((R + 1) & 0x7F));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DecR() => R = (byte)((R & 0x80) | ((R - 1) & 0x7F));

    /// <summary>M1 cycle: opcode fetch (4 T-states) plus memory refresh.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte FetchOpcode()
    {
        T += _bus.Contention(PC, T);
        T += 4;
        byte op = _bus.ReadMemory(PC);
        PC++;
        IncR();
        return op;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Read(ushort address)
    {
        T += _bus.Contention(address, T);
        T += 3;
        return _bus.ReadMemory(address);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Write(ushort address, byte value)
    {
        T += _bus.Contention(address, T);
        T += 3;
        _bus.WriteMemory(address, value);
    }

    /// <summary>Internal cycles that keep <paramref name="address"/> on the bus without MREQ.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Contend(ushort address, int cycles)
    {
        for (int i = 0; i < cycles; i++)
        {
            T += _bus.Contention(address, T);
            T += 1;
        }
    }

    /// <summary>A memory cycle whose data is not used (untaken jumps still put the address out).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ContendRead(ushort address, int time)
    {
        T += _bus.Contention(address, T);
        T += time;
    }

    private byte In(ushort port)
    {
        T += _bus.PortTimingPre(port, T);
        byte v = _bus.ReadPort(port);
        T += _bus.PortTimingPost(port, T);
        return v;
    }

    private void Out(ushort port, byte value)
    {
        T += _bus.PortTimingPre(port, T);
        _bus.WritePort(port, value);
        T += _bus.PortTimingPost(port, T);
    }

    private ushort ReadWordAtPc()
    {
        byte lo = Read(PC++);
        byte hi = Read(PC++);
        return (ushort)(lo | (hi << 8));
    }

    private void Push(ushort value)
    {
        SP--;
        Write(SP, (byte)(value >> 8));
        SP--;
        Write(SP, (byte)value);
    }

    private ushort Pop()
    {
        byte lo = Read(SP++);
        byte hi = Read(SP++);
        return (ushort)(lo | (hi << 8));
    }

    // -- execution ------------------------------------------------------------------
    public void RaiseNmi() => _nmiPending = true;

    /// <summary>Executes one instruction, or accepts a pending interrupt. Returns T-states used.</summary>
    public int Step()
    {
        long start = T;
        if (_nmiPending)
        {
            _nmiPending = false;
            AcceptNmi();
            return (int)(T - start);
        }
        if (IntLine && IFF1 && !_eiDelay)
        {
            AcceptInterrupt();
            return (int)(T - start);
        }
        _eiDelay = false;
        _prevQ = Q;
        Q = 0;
        ExecuteBase(FetchOpcode());
        return (int)(T - start);
    }

    /// <summary>True when the next Step would accept a maskable interrupt.</summary>
    public bool InterruptsAccepted => IFF1 && !_eiDelay;

    private void AcceptInterrupt()
    {
        if (Halted)
        {
            Halted = false;
            PC++;
        }
        IFF1 = IFF2 = false;
        IncR();
        T += 7; // acknowledge cycle is 2 T-states longer than a normal M1
        Push(PC);
        if (IM == 2)
        {
            ushort vector = (ushort)((I << 8) | _bus.InterruptDataBus);
            byte lo = Read(vector);
            byte hi = Read((ushort)(vector + 1));
            PC = (ushort)(lo | (hi << 8));
        }
        else
        {
            // IM 1, and IM 0 with $FF (RST 38h) on the data bus as on the Spectrum.
            PC = 0x0038;
        }
        WZ = PC;
        Q = 0;
    }

    private void AcceptNmi()
    {
        if (Halted)
        {
            Halted = false;
            PC++;
        }
        IFF1 = false;
        IncR();
        T += 5;
        Push(PC);
        PC = 0x0066;
        WZ = PC;
        Q = 0;
    }

    // -- ALU ----------------------------------------------------------------------------
    private void Add8(byte v)
    {
        int t = A + v;
        int lookup = ((A & 0x88) >> 3) | ((v & 0x88) >> 2) | ((t & 0x88) >> 1);
        A = (byte)t;
        F = (byte)(((t & 0x100) != 0 ? FC : 0) | HalfcarryAdd[lookup & 7] | OverflowAdd[lookup >> 4] | SZ53[A]);
        Q = F;
    }

    private void Adc8(byte v)
    {
        int t = A + v + (F & FC);
        int lookup = ((A & 0x88) >> 3) | ((v & 0x88) >> 2) | ((t & 0x88) >> 1);
        A = (byte)t;
        F = (byte)(((t & 0x100) != 0 ? FC : 0) | HalfcarryAdd[lookup & 7] | OverflowAdd[lookup >> 4] | SZ53[A]);
        Q = F;
    }

    private void Sub8(byte v)
    {
        int t = A - v;
        int lookup = ((A & 0x88) >> 3) | ((v & 0x88) >> 2) | ((t & 0x88) >> 1);
        A = (byte)t;
        F = (byte)(((t & 0x100) != 0 ? FC : 0) | FN | HalfcarrySub[lookup & 7] | OverflowSub[lookup >> 4] | SZ53[A]);
        Q = F;
    }

    private void Sbc8(byte v)
    {
        int t = A - v - (F & FC);
        int lookup = ((A & 0x88) >> 3) | ((v & 0x88) >> 2) | ((t & 0x88) >> 1);
        A = (byte)t;
        F = (byte)(((t & 0x100) != 0 ? FC : 0) | FN | HalfcarrySub[lookup & 7] | OverflowSub[lookup >> 4] | SZ53[A]);
        Q = F;
    }

    private void And8(byte v)
    {
        A &= v;
        F = (byte)(FH | SZ53P[A]);
        Q = F;
    }

    private void Xor8(byte v)
    {
        A ^= v;
        F = SZ53P[A];
        Q = F;
    }

    private void Or8(byte v)
    {
        A |= v;
        F = SZ53P[A];
        Q = F;
    }

    private void Cp8(byte v)
    {
        int t = A - v;
        int lookup = ((A & 0x88) >> 3) | ((v & 0x88) >> 2) | ((t & 0x88) >> 1);
        F = (byte)(((t & 0x100) != 0 ? FC : ((t & 0xFF) != 0 ? 0 : FZ)) | FN | HalfcarrySub[lookup & 7]
                   | OverflowSub[lookup >> 4] | (v & (F3 | F5)) | (t & FS));
        Q = F;
    }

    private void Alu(int op, byte v)
    {
        switch (op)
        {
            case 0: Add8(v); break;
            case 1: Adc8(v); break;
            case 2: Sub8(v); break;
            case 3: Sbc8(v); break;
            case 4: And8(v); break;
            case 5: Xor8(v); break;
            case 6: Or8(v); break;
            default: Cp8(v); break;
        }
    }

    private byte Inc8(byte v)
    {
        v++;
        F = (byte)((F & FC) | (v == 0x80 ? FV : 0) | ((v & 0x0F) != 0 ? 0 : FH) | SZ53[v]);
        Q = F;
        return v;
    }

    private byte Dec8(byte v)
    {
        byte f = (byte)((F & FC) | ((v & 0x0F) != 0 ? 0 : FH) | FN);
        v--;
        F = (byte)(f | (v == 0x7F ? FV : 0) | SZ53[v]);
        Q = F;
        return v;
    }

    private ushort Add16(ushort a, ushort b)
    {
        int t = a + b;
        int lookup = ((a & 0x0800) >> 11) | ((b & 0x0800) >> 10) | ((t & 0x0800) >> 9);
        WZ = (ushort)(a + 1);
        F = (byte)((F & (FV | FZ | FS)) | ((t & 0x10000) != 0 ? FC : 0) | ((t >> 8) & (F3 | F5)) | HalfcarryAdd[lookup]);
        Q = F;
        return (ushort)t;
    }

    private void Adc16(ushort v)
    {
        int hl = HL;
        int t = hl + v + (F & FC);
        int lookup = ((hl & 0x8800) >> 11) | ((v & 0x8800) >> 10) | ((t & 0x8800) >> 9);
        WZ = (ushort)(hl + 1);
        HL = (ushort)t;
        F = (byte)(((t & 0x10000) != 0 ? FC : 0) | OverflowAdd[lookup >> 4] | (H & (F3 | F5 | FS))
                   | HalfcarryAdd[lookup & 7] | (HL != 0 ? 0 : FZ));
        Q = F;
    }

    private void Sbc16(ushort v)
    {
        int hl = HL;
        int t = hl - v - (F & FC);
        int lookup = ((hl & 0x8800) >> 11) | ((v & 0x8800) >> 10) | ((t & 0x8800) >> 9);
        WZ = (ushort)(hl + 1);
        HL = (ushort)t;
        F = (byte)(((t & 0x10000) != 0 ? FC : 0) | FN | OverflowSub[lookup >> 4] | (H & (F3 | F5 | FS))
                   | HalfcarrySub[lookup & 7] | (HL != 0 ? 0 : FZ));
        Q = F;
    }

    private byte Rotate(int op, byte v)
    {
        int f;
        switch (op)
        {
            case 0: v = (byte)((v << 1) | (v >> 7)); f = v & FC; break;                    // RLC
            case 1: f = v & FC; v = (byte)((v >> 1) | (v << 7)); break;                    // RRC
            case 2: { int old = v; v = (byte)((v << 1) | (F & FC)); f = old >> 7; } break; // RL
            case 3: { int old = v; v = (byte)((v >> 1) | (F << 7)); f = old & FC; } break; // RR
            case 4: f = v >> 7; v = (byte)(v << 1); break;                                  // SLA
            case 5: f = v & FC; v = (byte)((v & 0x80) | (v >> 1)); break;                   // SRA
            case 6: f = v >> 7; v = (byte)((v << 1) | 1); break;                            // SLL (undocumented)
            default: f = v & FC; v = (byte)(v >> 1); break;                                 // SRL
        }
        F = (byte)(f | SZ53P[v]);
        Q = F;
        return v;
    }

    private void Bit(int bit, byte v, int flags35)
    {
        int f = (F & FC) | FH | (flags35 & (F3 | F5));
        if ((v & (1 << bit)) == 0)
            f |= FP | FZ;
        if (bit == 7 && (v & 0x80) != 0)
            f |= FS;
        F = (byte)f;
        Q = F;
    }

    private void Daa()
    {
        int add = 0;
        int carry = F & FC;
        if ((F & FH) != 0 || (A & 0x0F) > 9)
            add = 6;
        if (carry != 0 || A > 0x99)
            add |= 0x60;
        if (A > 0x99)
            carry = FC;
        if ((F & FN) != 0)
            Sub8((byte)add);
        else
            Add8((byte)add);
        F = (byte)((F & ~(FC | FP)) | carry | Parity[A]);
        Q = F;
    }

    private bool Condition(int cc) => cc switch
    {
        0 => (F & FZ) == 0,
        1 => (F & FZ) != 0,
        2 => (F & FC) == 0,
        3 => (F & FC) != 0,
        4 => (F & FP) == 0,
        5 => (F & FP) != 0,
        6 => (F & FS) == 0,
        _ => (F & FS) != 0,
    };

    // -- control flow helpers ---------------------------------------------------------
    private void Jr()
    {
        sbyte offset = (sbyte)Read(PC);
        Contend(PC, 5);
        PC = (ushort)(PC + offset + 1);
        WZ = PC;
    }

    private void Jp()
    {
        byte lo = Read(PC);
        byte hi = Read((ushort)(PC + 1));
        PC = (ushort)(lo | (hi << 8));
        WZ = PC;
    }

    private void Call()
    {
        byte lo = Read(PC++);
        byte hi = Read(PC);
        Contend(PC, 1);
        PC++;
        Push(PC);
        PC = (ushort)(lo | (hi << 8));
        WZ = PC;
    }

    private void Ret()
    {
        PC = Pop();
        WZ = PC;
    }

    private void Rst(ushort address)
    {
        Push(PC);
        PC = address;
        WZ = PC;
    }

    // -- 8-bit register access by opcode index (B C D E H L - A) ----------------
    private byte GetReg(int r) => r switch
    {
        0 => B, 1 => C, 2 => D, 3 => E, 4 => H, 5 => L, 7 => A,
        _ => throw new InvalidOperationException("(HL) no es un registro"),
    };

    private void SetReg(int r, byte v)
    {
        switch (r)
        {
            case 0: B = v; break;
            case 1: C = v; break;
            case 2: D = v; break;
            case 3: E = v; break;
            case 4: H = v; break;
            case 5: L = v; break;
            case 7: A = v; break;
            default: throw new InvalidOperationException("(HL) no es un registro");
        }
    }

    // -- ICpuDebugView ------------------------------------------------------------------
    public string CpuName => "Zilog Z80";
    public uint ProgramCounter { get => PC; set => PC = (ushort)value; }
    public uint StackPointer => SP;
    public long Cycles => T;

    public IReadOnlyList<RegisterValue> GetRegisters() => new[]
    {
        new RegisterValue("AF", 16, AF), new RegisterValue("BC", 16, BC), new RegisterValue("DE", 16, DE),
        new RegisterValue("HL", 16, HL), new RegisterValue("IX", 16, IX), new RegisterValue("IY", 16, IY),
        new RegisterValue("SP", 16, SP), new RegisterValue("PC", 16, PC),
        new RegisterValue("AF'", 16, AF_), new RegisterValue("BC'", 16, BC_), new RegisterValue("DE'", 16, DE_),
        new RegisterValue("HL'", 16, HL_), new RegisterValue("I", 8, I), new RegisterValue("R", 8, R),
        new RegisterValue("WZ", 16, WZ),
    };

    public bool TrySetRegister(string name, uint value)
    {
        ushort w = (ushort)value;
        switch (name.ToUpperInvariant())
        {
            case "AF": AF = w; return true;
            case "BC": BC = w; return true;
            case "DE": DE = w; return true;
            case "HL": HL = w; return true;
            case "IX": IX = w; return true;
            case "IY": IY = w; return true;
            case "SP": SP = w; return true;
            case "PC": PC = w; return true;
            case "AF'": AF_ = w; return true;
            case "BC'": BC_ = w; return true;
            case "DE'": DE_ = w; return true;
            case "HL'": HL_ = w; return true;
            case "I": I = (byte)value; return true;
            case "R": R = (byte)value; return true;
            case "WZ": WZ = w; return true;
            default: return false;
        }
    }

    public IReadOnlyList<FlagValue> GetFlags() => new[]
    {
        new FlagValue("S", (F & FS) != 0, "Signo (bit 7 del resultado)"),
        new FlagValue("Z", (F & FZ) != 0, "Cero"),
        new FlagValue("5", (F & F5) != 0, "Bit 5 no documentado"),
        new FlagValue("H", (F & FH) != 0, "Medio acarreo (bit 3 a 4)"),
        new FlagValue("3", (F & F3) != 0, "Bit 3 no documentado"),
        new FlagValue("P/V", (F & FP) != 0, "Paridad / desborde"),
        new FlagValue("N", (F & FN) != 0, "Última operación fue resta"),
        new FlagValue("C", (F & FC) != 0, "Acarreo"),
    };

    public bool TrySetFlag(string name, bool value)
    {
        int bit = name switch
        {
            "S" => FS, "Z" => FZ, "5" => F5, "H" => FH, "3" => F3, "P/V" => FP, "N" => FN, "C" => FC, _ => 0,
        };
        if (bit == 0)
            return false;
        F = (byte)(value ? F | bit : F & ~bit);
        return true;
    }

    public IReadOnlyList<string> GetInterruptState() => new[]
    {
        $"IM {IM}   IFF1={(IFF1 ? 1 : 0)} IFF2={(IFF2 ? 1 : 0)}",
        $"/INT {(IntLine ? "activa" : "inactiva")}{(_eiDelay ? " (bloqueada tras EI)" : "")}",
        Halted ? "CPU detenida en HALT" : "CPU ejecutando",
    };
}
