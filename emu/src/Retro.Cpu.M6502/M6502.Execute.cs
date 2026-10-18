namespace Retro.Cpu.M6502;

public sealed partial class M6502
{
    private ushort Ea(int mode, Kind kind) => mode switch
    {
        ModeZp => Zp(),
        ModeZpX => ZpIndexed(X),
        ModeZpY => ZpIndexed(Y),
        ModeAbs => Abs(),
        ModeAbsX => AbsIndexed(X, kind),
        ModeAbsY => AbsIndexed(Y, kind),
        ModeIndX => IndX(),
        ModeIndY => IndY(kind),
        _ => throw new InvalidOperationException(),
    };

    private const int ModeImm = 1, ModeZp = 2, ModeZpX = 3, ModeZpY = 4, ModeAbs = 5, ModeAbsX = 6, ModeAbsY = 7, ModeIndX = 8, ModeIndY = 9;

    /// <summary>Addressing mode of the "standard" columns (ALU, read-modify-write, loads and stores).</summary>
    private static int GroupMode(byte op)
    {
        int col = op & 0x1F;
        return col switch
        {
            0x01 or 0x03 => ModeIndX,
            0x05 or 0x06 or 0x07 or 0x04 => ModeZp,
            0x09 or 0x0B => ModeImm,
            0x0D or 0x0E or 0x0F or 0x0C => ModeAbs,
            0x11 or 0x13 => ModeIndY,
            0x15 or 0x16 or 0x17 or 0x14 => ModeZpX,
            0x19 or 0x1B => ModeAbsY,
            0x1D or 0x1E or 0x1F or 0x1C => ModeAbsX,
            0x00 or 0x02 => ModeImm,
            _ => 0,
        };
    }

    private byte ReadOperand(int mode) => mode == ModeImm ? Fetch() : Read(Ea(mode, Kind.Read));

    private void Execute(byte op)
    {
        switch (op)
        {
            // ---------------------------------------------------------- single byte / control
            case 0x00: // BRK
                Fetch();
                Push((byte)(PC >> 8));
                Push((byte)PC);
                {
                    bool nmi = _nmiPending;
                    Push((byte)(P | FlagB | FlagU));
                    P |= FlagI;
                    ushort vector = nmi ? (ushort)0xFFFA : (ushort)0xFFFE;
                    if (nmi) _nmiPending = false;
                    byte lo = Read(vector);
                    byte hi = Read((ushort)(vector + 1));
                    PC = (ushort)(lo | (hi << 8));
                }
                return;
            case 0x20: // JSR abs
            {
                byte lo = Fetch();
                Read((ushort)(0x100 | S));
                Push((byte)(PC >> 8));
                Push((byte)PC);
                byte hi = Fetch();
                PC = (ushort)(lo | (hi << 8));
                return;
            }
            case 0x40: // RTI
                Read(PC);
                Read((ushort)(0x100 | S));
                P = (byte)((Pull() & ~FlagB) | FlagU);
                {
                    byte lo = Pull();
                    byte hi = Pull();
                    PC = (ushort)(lo | (hi << 8));
                }
                return;
            case 0x60: // RTS
                Read(PC);
                Read((ushort)(0x100 | S));
                {
                    byte lo = Pull();
                    byte hi = Pull();
                    PC = (ushort)(lo | (hi << 8));
                }
                Read(PC);
                PC++;
                return;
            case 0x4C: // JMP abs
                PC = Abs();
                return;
            case 0x6C: // JMP (ind), with the page wrap of the NMOS 6502
            {
                ushort ptr = Abs();
                byte lo = Read(ptr);
                byte hi = Read((ushort)((ptr & 0xFF00) | ((ptr + 1) & 0xFF)));
                PC = (ushort)(lo | (hi << 8));
                return;
            }
            case 0x08: Read(PC); Push((byte)(P | FlagB | FlagU)); return;                  // PHP
            case 0x48: Read(PC); Push(A); return;                                          // PHA
            case 0x28: Read(PC); Read((ushort)(0x100 | S)); P = (byte)((Pull() & ~FlagB) | FlagU); return; // PLP
            case 0x68: Read(PC); Read((ushort)(0x100 | S)); A = Pull(); NZ(A); return;     // PLA

            case 0x10: Branch(!Flag(FlagN)); return;
            case 0x30: Branch(Flag(FlagN)); return;
            case 0x50: Branch(!Flag(FlagV)); return;
            case 0x70: Branch(Flag(FlagV)); return;
            case 0x90: Branch(!Flag(FlagC)); return;
            case 0xB0: Branch(Flag(FlagC)); return;
            case 0xD0: Branch(!Flag(FlagZ)); return;
            case 0xF0: Branch(Flag(FlagZ)); return;

            case 0x18: Read(PC); P &= unchecked((byte)~FlagC); return;
            case 0x38: Read(PC); P |= FlagC; return;
            case 0x58: Read(PC); P &= unchecked((byte)~FlagI); return;
            case 0x78: Read(PC); P |= FlagI; return;
            case 0xB8: Read(PC); P &= unchecked((byte)~FlagV); return;
            case 0xD8: Read(PC); P &= unchecked((byte)~FlagD); return;
            case 0xF8: Read(PC); P |= FlagD; return;

            case 0xAA: Read(PC); X = A; NZ(X); return;   // TAX
            case 0x8A: Read(PC); A = X; NZ(A); return;   // TXA
            case 0xA8: Read(PC); Y = A; NZ(Y); return;   // TAY
            case 0x98: Read(PC); A = Y; NZ(A); return;   // TYA
            case 0xBA: Read(PC); X = S; NZ(X); return;   // TSX
            case 0x9A: Read(PC); S = X; return;          // TXS
            case 0xE8: Read(PC); X++; NZ(X); return;     // INX
            case 0xC8: Read(PC); Y++; NZ(Y); return;     // INY
            case 0xCA: Read(PC); X--; NZ(X); return;     // DEX
            case 0x88: Read(PC); Y--; NZ(Y); return;     // DEY
            case 0xEA: case 0x1A: case 0x3A: case 0x5A: case 0x7A: case 0xDA: case 0xFA:
                Read(PC); return;                        // NOP

            case 0x0A: Read(PC); A = Asl(A); return;
            case 0x4A: Read(PC); A = Lsr(A); return;
            case 0x2A: Read(PC); A = Rol(A); return;
            case 0x6A: Read(PC); A = Ror(A); return;

            case 0x02: case 0x12: case 0x22: case 0x32: case 0x42: case 0x52: case 0x62: case 0x72:
            case 0x92: case 0xB2: case 0xD2: case 0xF2:
                Jam();
                return;

            // ---------------------------------------------------------- loads, stores, compares with X/Y
            case 0xA0: Y = Fetch(); NZ(Y); return;
            case 0xA4: Y = Read(Zp()); NZ(Y); return;
            case 0xB4: Y = Read(ZpIndexed(X)); NZ(Y); return;
            case 0xAC: Y = Read(Abs()); NZ(Y); return;
            case 0xBC: Y = Read(AbsIndexed(X, Kind.Read)); NZ(Y); return;
            case 0xA2: X = Fetch(); NZ(X); return;
            case 0xA6: X = Read(Zp()); NZ(X); return;
            case 0xB6: X = Read(ZpIndexed(Y)); NZ(X); return;
            case 0xAE: X = Read(Abs()); NZ(X); return;
            case 0xBE: X = Read(AbsIndexed(Y, Kind.Read)); NZ(X); return;
            case 0x84: Write(Zp(), Y); return;
            case 0x94: Write(ZpIndexed(X), Y); return;
            case 0x8C: Write(Abs(), Y); return;
            case 0x86: Write(Zp(), X); return;
            case 0x96: Write(ZpIndexed(Y), X); return;
            case 0x8E: Write(Abs(), X); return;
            case 0xC0: Compare(Y, Fetch()); return;
            case 0xC4: Compare(Y, Read(Zp())); return;
            case 0xCC: Compare(Y, Read(Abs())); return;
            case 0xE0: Compare(X, Fetch()); return;
            case 0xE4: Compare(X, Read(Zp())); return;
            case 0xEC: Compare(X, Read(Abs())); return;
            case 0x24: Bit(Read(Zp())); return;
            case 0x2C: Bit(Read(Abs())); return;

            // ---------------------------------------------------------- undocumented, special
            case 0x87: Write(Zp(), (byte)(A & X)); return;              // SAX
            case 0x97: Write(ZpIndexed(Y), (byte)(A & X)); return;
            case 0x8F: Write(Abs(), (byte)(A & X)); return;
            case 0x83: Write(IndX(), (byte)(A & X)); return;
            case 0xA7: A = X = Read(Zp()); NZ(A); return;               // LAX
            case 0xB7: A = X = Read(ZpIndexed(Y)); NZ(A); return;
            case 0xAF: A = X = Read(Abs()); NZ(A); return;
            case 0xBF: A = X = Read(AbsIndexed(Y, Kind.Read)); NZ(A); return;
            case 0xA3: A = X = Read(IndX()); NZ(A); return;
            case 0xB3: A = X = Read(IndY(Kind.Read)); NZ(A); return;
            case 0xAB: A = X = (byte)((A | 0xEE) & Fetch()); NZ(A); return;            // LXA
            case 0x8B: A = (byte)((A | 0xEE) & X & Fetch()); NZ(A); return;            // ANE
            case 0x0B: case 0x2B: A &= Fetch(); NZ(A); SetFlag(FlagC, (A & 0x80) != 0); return; // ANC
            case 0x4B: A &= Fetch(); A = Lsr(A); return;                               // ALR
            case 0x6B: Arr(Fetch()); return;                                           // ARR
            case 0xCB:                                                                 // SBX
            {
                byte v = Fetch();
                int t = (A & X) - v;
                SetFlag(FlagC, t >= 0);
                X = (byte)t;
                NZ(X);
                return;
            }
            case 0xEB: Sbc(Fetch()); return;                                           // SBC (copy)
            case 0xBB:                                                                 // LAS
            {
                byte v = (byte)(Read(AbsIndexed(Y, Kind.Read)) & S);
                A = X = S = v;
                NZ(v);
                return;
            }
            case 0x9C: { byte lo = Fetch(), hi = Fetch(); StoreHigh(lo, hi, X, Y); return; }   // SHY abs,X
            case 0x9E: { byte lo = Fetch(), hi = Fetch(); StoreHigh(lo, hi, Y, X); return; }   // SHX abs,Y
            case 0x9F: { byte lo = Fetch(), hi = Fetch(); StoreHigh(lo, hi, Y, (byte)(A & X)); return; } // SHA abs,Y
            case 0x9B: { byte lo = Fetch(), hi = Fetch(); S = (byte)(A & X); StoreHigh(lo, hi, Y, S); return; } // TAS abs,Y
            case 0x93:                                                                 // SHA (zp),Y
            {
                byte z = Fetch();
                byte lo = Read(z);
                byte hi = Read((byte)(z + 1));
                StoreHigh(lo, hi, Y, (byte)(A & X));
                return;
            }
            case 0x80: case 0x82: case 0x89: case 0xC2: case 0xE2: Fetch(); return;   // NOP #imm
            case 0x04: case 0x44: case 0x64: Read(Zp()); return;                      // NOP zp
            case 0x14: case 0x34: case 0x54: case 0x74: case 0xD4: case 0xF4: Read(ZpIndexed(X)); return;
            case 0x0C: Read(Abs()); return;
            case 0x1C: case 0x3C: case 0x5C: case 0x7C: case 0xDC: case 0xFC: Read(AbsIndexed(X, Kind.Read)); return;
        }

        // ---------------------------------------------------------- the regular groups
        int mode = GroupMode(op);
        int group = op & 0xE3;
        switch (op & 0x03)
        {
            case 0x01: // ORA AND EOR ADC STA LDA CMP SBC
            {
                int which = op >> 5;
                if (which == 4)
                {
                    Write(Ea(mode, Kind.Write), A);
                    return;
                }
                byte v = ReadOperand(mode);
                switch (which)
                {
                    case 0: A |= v; NZ(A); break;
                    case 1: A &= v; NZ(A); break;
                    case 2: A ^= v; NZ(A); break;
                    case 3: Adc(v); break;
                    case 5: A = v; NZ(A); break;
                    case 6: Compare(A, v); break;
                    case 7: Sbc(v); break;
                }
                return;
            }
            case 0x02: // ASL ROL LSR ROR (DEC INC) on memory
            {
                int which = op >> 5;
                ushort a = Ea(mode, Kind.Modify);
                switch (which)
                {
                    case 0: Rmw(a, Asl); break;
                    case 1: Rmw(a, Rol); break;
                    case 2: Rmw(a, Lsr); break;
                    case 3: Rmw(a, Ror); break;
                    case 6: Rmw(a, Dec); break;
                    case 7: Rmw(a, Inc); break;
                }
                return;
            }
            case 0x03: // SLO RLA SRE RRA (SAX LAX) DCP ISC: read-modify-write + ALU
            {
                int which = op >> 5;
                ushort a = Ea(mode, Kind.Modify);
                switch (which)
                {
                    case 0: Rmw(a, v => { v = Asl(v); A |= v; NZ(A); return v; }); break;
                    case 1: Rmw(a, v => { v = Rol(v); A &= v; NZ(A); return v; }); break;
                    case 2: Rmw(a, v => { v = Lsr(v); A ^= v; NZ(A); return v; }); break;
                    case 3: Rmw(a, v => { v = Ror(v); Adc(v); return v; }); break;
                    case 6: Rmw(a, v => { v--; Compare(A, v); return v; }); break;
                    case 7: Rmw(a, v => { v++; Sbc(v); return v; }); break;
                }
                return;
            }
        }
        _ = group;
        throw new InvalidOperationException($"Opcode {op:X2} sin implementar");
    }
}
