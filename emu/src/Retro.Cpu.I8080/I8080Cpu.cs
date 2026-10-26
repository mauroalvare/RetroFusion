using Retro.Core;

namespace Retro.Cpu.I8080;

/// <summary>Memory and I/O as seen by an 8080 (8-bit port addresses).</summary>
public interface I8080Bus
{
    byte ReadMemory(ushort address);
    void WriteMemory(ushort address, byte value);
    byte In(byte port);
    void Out(byte port, byte value);
}

/// <summary>
/// Intel 8080 interpreter.
///
/// All 256 opcodes, including the undocumented aliases (NOP, JMP, RET, CALL), with the
/// state counts of the Intel 8080 Microcomputer Systems User's Manual (conditional
/// CALL 17/11, conditional RET 11/5). Flags follow the real chip: bit 1 always set,
/// bits 3 and 5 always clear, auxiliary carry of ANA = bit 3 of (A | operand),
/// subtraction done as addition of the complement. Validated with TST8080, 8080PRE,
/// CPUTEST and 8080EXM, including their exact cycle totals.
/// </summary>
public sealed class I8080Cpu : ICpuDebugView
{
    public byte A, B, C, D, E, H, L;
    public ushort SP, PC;
    public bool S, Z, AC, P, CY;
    /// <summary>Interrupt enable flip-flop.</summary>
    public bool INTE;
    public bool Halted;
    public long Cycles;
    public long Instructions;
    private bool _eiDelay;
    private readonly I8080Bus _bus;

    private static readonly bool[] ParityTable = BuildParity();

    private static readonly byte[] CycleTable =
    {
        4, 10, 7, 5, 5, 5, 7, 4, 4, 10, 7, 5, 5, 5, 7, 4,
        4, 10, 7, 5, 5, 5, 7, 4, 4, 10, 7, 5, 5, 5, 7, 4,
        4, 10, 16, 5, 5, 5, 7, 4, 4, 10, 16, 5, 5, 5, 7, 4,
        4, 10, 13, 5, 10, 10, 10, 4, 4, 10, 13, 5, 5, 5, 7, 4,
        5, 5, 5, 5, 5, 5, 7, 5, 5, 5, 5, 5, 5, 5, 7, 5,
        5, 5, 5, 5, 5, 5, 7, 5, 5, 5, 5, 5, 5, 5, 7, 5,
        5, 5, 5, 5, 5, 5, 7, 5, 5, 5, 5, 5, 5, 5, 7, 5,
        7, 7, 7, 7, 7, 7, 7, 7, 5, 5, 5, 5, 5, 5, 7, 5,
        4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
        4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
        4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
        4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
        5, 10, 10, 10, 11, 11, 7, 11, 5, 10, 10, 10, 11, 17, 7, 11,
        5, 10, 10, 10, 11, 11, 7, 11, 5, 10, 10, 10, 11, 17, 7, 11,
        5, 10, 10, 18, 11, 11, 7, 11, 5, 5, 10, 4, 11, 17, 7, 11,
        5, 10, 10, 4, 11, 11, 7, 11, 5, 5, 10, 4, 11, 17, 7, 11,
    };

    public I8080Cpu(I8080Bus bus)
    {
        _bus = bus;
        Reset();
    }

    private static bool[] BuildParity()
    {
        var t = new bool[256];
        for (int i = 0; i < 256; i++)
        {
            int bits = 0;
            for (int b = 0; b < 8; b++)
                bits += (i >> b) & 1;
            t[i] = (bits & 1) == 0;
        }
        return t;
    }

    public void Reset()
    {
        PC = 0;
        INTE = false;
        Halted = false;
        _eiDelay = false;
    }

    // -- register pairs and PSW --------------------------------------------------
    public ushort BC { get => (ushort)((B << 8) | C); set { B = (byte)(value >> 8); C = (byte)value; } }
    public ushort DE { get => (ushort)((D << 8) | E); set { D = (byte)(value >> 8); E = (byte)value; } }
    public ushort HL { get => (ushort)((H << 8) | L); set { H = (byte)(value >> 8); L = (byte)value; } }

    /// <summary>Flag byte: S Z 0 AC 0 P 1 CY.</summary>
    public byte F
    {
        get => (byte)((S ? 0x80 : 0) | (Z ? 0x40 : 0) | (AC ? 0x10 : 0) | (P ? 0x04 : 0) | 0x02 | (CY ? 0x01 : 0));
        set
        {
            S = (value & 0x80) != 0;
            Z = (value & 0x40) != 0;
            AC = (value & 0x10) != 0;
            P = (value & 0x04) != 0;
            CY = (value & 0x01) != 0;
        }
    }

    public ushort PSW { get => (ushort)((A << 8) | F); set { A = (byte)(value >> 8); F = (byte)value; } }

    // -- memory helpers ------------------------------------------------------------
    private byte Next() => _bus.ReadMemory(PC++);

    private ushort NextWord()
    {
        byte lo = Next();
        byte hi = Next();
        return (ushort)(lo | (hi << 8));
    }

    private void Push(ushort v)
    {
        SP--;
        _bus.WriteMemory(SP, (byte)(v >> 8));
        SP--;
        _bus.WriteMemory(SP, (byte)v);
    }

    private ushort Pop()
    {
        byte lo = _bus.ReadMemory(SP++);
        byte hi = _bus.ReadMemory(SP++);
        return (ushort)(lo | (hi << 8));
    }

    private byte GetReg(int r) => r switch
    {
        0 => B, 1 => C, 2 => D, 3 => E, 4 => H, 5 => L, 6 => _bus.ReadMemory(HL), _ => A,
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
            case 6: _bus.WriteMemory(HL, v); break;
            default: A = v; break;
        }
    }

    private ushort GetRp(int p) => p switch { 0 => BC, 1 => DE, 2 => HL, _ => SP };

    private void SetRp(int p, ushort v)
    {
        switch (p)
        {
            case 0: BC = v; break;
            case 1: DE = v; break;
            case 2: HL = v; break;
            default: SP = v; break;
        }
    }

    private bool Condition(int cc) => cc switch
    {
        0 => !Z, 1 => Z, 2 => !CY, 3 => CY, 4 => !P, 5 => P, 6 => !S, _ => S,
    };

    // -- ALU --------------------------------------------------------------------------
    private void SetSZP(byte v)
    {
        S = v >= 0x80;
        Z = v == 0;
        P = ParityTable[v];
    }

    private void Add(byte v, int carry)
    {
        int r = A + v + carry;
        AC = ((A & 0x0F) + (v & 0x0F) + carry) > 0x0F;
        CY = r > 0xFF;
        A = (byte)r;
        SetSZP(A);
    }

    /// <summary>A - v - borrow, computed by the 8080 as A + ~v + !borrow.</summary>
    private void Sub(byte v, int borrow)
    {
        int nv = ~v & 0xFF;
        int carryIn = 1 - borrow;
        int r = A + nv + carryIn;
        AC = ((A & 0x0F) + (nv & 0x0F) + carryIn) > 0x0F;
        CY = r <= 0xFF;
        A = (byte)r;
        SetSZP(A);
    }

    private void Alu(int op, byte v)
    {
        switch (op)
        {
            case 0: Add(v, 0); break;                                   // ADD
            case 1: Add(v, CY ? 1 : 0); break;                          // ADC
            case 2: Sub(v, 0); break;                                   // SUB
            case 3: Sub(v, CY ? 1 : 0); break;                          // SBB
            case 4:                                                     // ANA
                AC = ((A | v) & 0x08) != 0;
                A &= v;
                CY = false;
                SetSZP(A);
                break;
            case 5:                                                     // XRA
                A ^= v;
                AC = CY = false;
                SetSZP(A);
                break;
            case 6:                                                     // ORA
                A |= v;
                AC = CY = false;
                SetSZP(A);
                break;
            default:                                                    // CMP
            {
                byte save = A;
                Sub(v, 0);
                A = save;
                break;
            }
        }
    }

    private void Daa()
    {
        bool carry = CY;
        int correction = 0;
        int lsb = A & 0x0F, msb = A >> 4;
        if (AC || lsb > 9)
            correction += 0x06;
        if (CY || msb > 9 || (msb >= 9 && lsb > 9))
        {
            correction += 0x60;
            carry = true;
        }
        Add((byte)correction, 0);
        CY = carry;
    }

    // -- execution ----------------------------------------------------------------------
    /// <summary>True when an interrupt request would be accepted now.</summary>
    public bool InterruptsAccepted => INTE && !_eiDelay;

    /// <summary>Executes one instruction. Returns the states (clock cycles) used.</summary>
    public int Step()
    {
        if (Halted)
        {
            Cycles += 4;
            return 4;
        }
        _eiDelay = false;
        return Execute(Next());
    }

    /// <summary>
    /// Interrupt request with the instruction the hardware places on the data bus
    /// (normally an RST). Returns the states used, or 0 if interrupts are disabled.
    /// </summary>
    public int Interrupt(byte instruction)
    {
        if (!InterruptsAccepted)
            return 0;
        INTE = false;
        Halted = false;
        return Execute(instruction);
    }

    private int Execute(byte op)
    {
        Instructions++;
        int cycles = CycleTable[op];
        int x = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
        switch (x)
        {
            case 0:
                switch (z)
                {
                    case 0:
                        break;                                              // NOP (and aliases)
                    case 1:
                        if (q == 0) SetRp(p, NextWord());                   // LXI
                        else                                                // DAD
                        {
                            int r = HL + GetRp(p);
                            CY = r > 0xFFFF;
                            HL = (ushort)r;
                        }
                        break;
                    case 2:
                        switch (y)
                        {
                            case 0: _bus.WriteMemory(BC, A); break;         // STAX B
                            case 1: A = _bus.ReadMemory(BC); break;         // LDAX B
                            case 2: _bus.WriteMemory(DE, A); break;         // STAX D
                            case 3: A = _bus.ReadMemory(DE); break;         // LDAX D
                            case 4:                                         // SHLD
                            {
                                ushort a = NextWord();
                                _bus.WriteMemory(a, L);
                                _bus.WriteMemory((ushort)(a + 1), H);
                                break;
                            }
                            case 5:                                         // LHLD
                            {
                                ushort a = NextWord();
                                L = _bus.ReadMemory(a);
                                H = _bus.ReadMemory((ushort)(a + 1));
                                break;
                            }
                            case 6: _bus.WriteMemory(NextWord(), A); break; // STA
                            default: A = _bus.ReadMemory(NextWord()); break;// LDA
                        }
                        break;
                    case 3:
                        SetRp(p, (ushort)(GetRp(p) + (q == 0 ? 1 : -1)));   // INX / DCX
                        break;
                    case 4:                                                 // INR
                    {
                        byte v = (byte)(GetReg(y) + 1);
                        AC = (v & 0x0F) == 0;
                        SetSZP(v);
                        SetReg(y, v);
                        break;
                    }
                    case 5:                                                 // DCR
                    {
                        byte v = (byte)(GetReg(y) - 1);
                        AC = (v & 0x0F) != 0x0F;
                        SetSZP(v);
                        SetReg(y, v);
                        break;
                    }
                    case 6:
                        SetReg(y, Next());                                  // MVI
                        break;
                    default:
                        switch (y)
                        {
                            case 0: CY = (A & 0x80) != 0; A = (byte)((A << 1) | (CY ? 1 : 0)); break;      // RLC
                            case 1: CY = (A & 0x01) != 0; A = (byte)((A >> 1) | (CY ? 0x80 : 0)); break;   // RRC
                            case 2: { bool c = CY; CY = (A & 0x80) != 0; A = (byte)((A << 1) | (c ? 1 : 0)); break; }      // RAL
                            case 3: { bool c = CY; CY = (A & 0x01) != 0; A = (byte)((A >> 1) | (c ? 0x80 : 0)); break; }   // RAR
                            case 4: Daa(); break;
                            case 5: A = (byte)~A; break;                                                     // CMA
                            case 6: CY = true; break;                                                        // STC
                            default: CY = !CY; break;                                                        // CMC
                        }
                        break;
                }
                break;

            case 1:
                if (op == 0x76)
                    Halted = true;                                          // HLT
                else
                    SetReg(y, GetReg(z));                                   // MOV
                break;

            case 2:
                Alu(y, GetReg(z));
                break;

            default:
                switch (z)
                {
                    case 0:                                                 // Rcc
                        if (Condition(y))
                        {
                            PC = Pop();
                            cycles += 6;
                        }
                        break;
                    case 1:
                        if (q == 0)                                         // POP
                        {
                            ushort v = Pop();
                            if (p == 3) PSW = v;
                            else SetRp(p, v);
                        }
                        else
                        {
                            switch (p)
                            {
                                case 0: case 1: PC = Pop(); break;          // RET (D9 alias)
                                case 2: PC = HL; break;                     // PCHL
                                default: SP = HL; break;                    // SPHL
                            }
                        }
                        break;
                    case 2:                                                 // Jcc
                    {
                        ushort a = NextWord();
                        if (Condition(y))
                            PC = a;
                        break;
                    }
                    case 3:
                        switch (y)
                        {
                            case 0: case 1: PC = NextWord(); break;         // JMP (CB alias)
                            case 2: _bus.Out(Next(), A); break;             // OUT
                            case 3: A = _bus.In(Next()); break;             // IN
                            case 4:                                         // XTHL
                            {
                                byte lo = _bus.ReadMemory(SP);
                                byte hi = _bus.ReadMemory((ushort)(SP + 1));
                                _bus.WriteMemory(SP, L);
                                _bus.WriteMemory((ushort)(SP + 1), H);
                                L = lo;
                                H = hi;
                                break;
                            }
                            case 5: (D, H) = (H, D); (E, L) = (L, E); break; // XCHG
                            case 6: INTE = false; break;                     // DI
                            default: INTE = true; _eiDelay = true; break;    // EI
                        }
                        break;
                    case 4:                                                 // Ccc
                    {
                        ushort a = NextWord();
                        if (Condition(y))
                        {
                            Push(PC);
                            PC = a;
                            cycles += 6;
                        }
                        break;
                    }
                    case 5:
                        if (q == 0)                                         // PUSH
                            Push(p == 3 ? PSW : GetRp(p));
                        else                                                // CALL (DD/ED/FD aliases)
                        {
                            ushort a = NextWord();
                            Push(PC);
                            PC = a;
                        }
                        break;
                    case 6:
                        Alu(y, Next());                                     // ADI..CPI
                        break;
                    default:                                                // RST
                        Push(PC);
                        PC = (ushort)(y * 8);
                        break;
                }
                break;
        }
        Cycles += cycles;
        return cycles;
    }

    // -- ICpuDebugView --------------------------------------------------------------------
    public string CpuName => "Intel 8080";
    public uint ProgramCounter { get => PC; set => PC = value <= ushort.MaxValue ? (ushort)value : throw new ArgumentOutOfRangeException(nameof(value)); }
    public uint StackPointer => SP;
    long ICpuDebugView.Cycles => Cycles;

    public IReadOnlyList<RegisterValue> GetRegisters() => new[]
    {
        new RegisterValue("PSW", 16, PSW), new RegisterValue("BC", 16, BC), new RegisterValue("DE", 16, DE),
        new RegisterValue("HL", 16, HL), new RegisterValue("SP", 16, SP), new RegisterValue("PC", 16, PC),
    };

    public bool TrySetRegister(string name, uint value)
    {
        if (value > ushort.MaxValue) return false;
        ushort w = (ushort)value;
        switch (name.ToUpperInvariant())
        {
            case "PSW": PSW = w; return true;
            case "BC": BC = w; return true;
            case "DE": DE = w; return true;
            case "HL": HL = w; return true;
            case "SP": SP = w; return true;
            case "PC": PC = w; return true;
            default: return false;
        }
    }

    public IReadOnlyList<FlagValue> GetFlags() => new[]
    {
        new FlagValue("S", S, "Signo"),
        new FlagValue("Z", Z, "Cero"),
        new FlagValue("AC", AC, "Acarreo auxiliar (bit 3 a 4)"),
        new FlagValue("P", P, "Paridad par"),
        new FlagValue("CY", CY, "Acarreo"),
    };

    public bool TrySetFlag(string name, bool value)
    {
        switch (name)
        {
            case "S": S = value; return true;
            case "Z": Z = value; return true;
            case "AC": AC = value; return true;
            case "P": P = value; return true;
            case "CY": CY = value; return true;
            default: return false;
        }
    }

    public IReadOnlyList<string> GetInterruptState() => new[]
    {
        $"INTE={(INTE ? 1 : 0)}{(_eiDelay ? " (se habilita tras la próxima instrucción)" : "")}",
        Halted ? "CPU detenida en HLT" : "CPU ejecutando",
        $"Instrucciones ejecutadas: {Instructions:N0}",
    };
}
