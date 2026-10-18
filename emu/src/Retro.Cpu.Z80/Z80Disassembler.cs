using Retro.Core;

namespace Retro.Cpu.Z80;

/// <summary>Z80 disassembler (Zilog syntax), including undocumented opcodes.</summary>
public sealed class Z80Disassembler : IDisassembler
{
    private static readonly string[] R = { "B", "C", "D", "E", "H", "L", "(HL)", "A" };
    private static readonly string[] RP = { "BC", "DE", "HL", "SP" };
    private static readonly string[] RP2 = { "BC", "DE", "HL", "AF" };
    private static readonly string[] CC = { "NZ", "Z", "NC", "C", "PO", "PE", "P", "M" };
    private static readonly string[] ALU = { "ADD A,", "ADC A,", "SUB ", "SBC A,", "AND ", "XOR ", "OR ", "CP " };
    private static readonly string[] ROT = { "RLC", "RRC", "RL", "RR", "SLA", "SRA", "SLL", "SRL" };
    private static readonly string[] BLOCK =
    {
        "LDI", "CPI", "INI", "OUTI", "LDD", "CPD", "IND", "OUTD",
        "LDIR", "CPIR", "INIR", "OTIR", "LDDR", "CPDR", "INDR", "OTDR",
    };

    /// <summary>Optional name lookup for addresses (labels, ROM routines).</summary>
    public Func<ushort, string?>? Symbols { get; set; }

    public DisassembledInstruction Disassemble(Func<uint, byte> peek, uint address)
    {
        var d = new Decoder(peek, (ushort)address, Symbols);
        string text = d.Decode();
        var bytes = new byte[d.Length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = peek((ushort)(address + i));
        return new DisassembledInstruction(address, d.Length, text, bytes);
    }

    private sealed class Decoder
    {
        private readonly Func<uint, byte> _peek;
        private readonly ushort _start;
        private readonly Func<ushort, string?>? _symbols;
        private int _pos;
        private string? _index;     // "IX" / "IY" when prefixed
        private sbyte _disp;
        private bool _dispRead;

        public Decoder(Func<uint, byte> peek, ushort start, Func<ushort, string?>? symbols)
        {
            _peek = peek;
            _start = start;
            _symbols = symbols;
        }

        public int Length => _pos;

        private byte Next() => _peek((ushort)(_start + _pos++));

        private string N() => $"${Next():X2}";

        private string NN()
        {
            int lo = Next(), hi = Next();
            return Addr((ushort)(lo | (hi << 8)));
        }

        private string Addr(ushort a)
        {
            string? name = _symbols?.Invoke(a);
            return name ?? $"${a:X4}";
        }

        private string Rel()
        {
            sbyte e = (sbyte)Next();
            return Addr((ushort)(_start + _pos + e));
        }

        private string Reg(int r)
        {
            if (_index == null)
                return R[r];
            if (r == 6)
                return IndexOperand();
            if (r == 4) return _index + "H";
            if (r == 5) return _index + "L";
            return R[r];
        }

        private string IndexOperand()
        {
            if (!_dispRead)
            {
                _disp = (sbyte)Next();
                _dispRead = true;
            }
            return _disp >= 0 ? $"({_index}+${_disp:X2})" : $"({_index}-${-_disp:X2})";
        }

        private string HL => _index ?? "HL";

        public string Decode()
        {
            byte op = Next();
            while (op == 0xDD || op == 0xFD)
            {
                _index = op == 0xDD ? "IX" : "IY";
                op = Next();
                if (op == 0xDD || op == 0xFD || op == 0xED)
                {
                    if (op == 0xED)
                    {
                        _index = null;
                        return DecodeED(Next());
                    }
                }
            }
            if (op == 0xCB)
                return _index == null ? DecodeCB(Next()) : DecodeIndexedCB();
            if (op == 0xED)
                return DecodeED(Next());
            return DecodeBase(op);
        }

        private string DecodeBase(byte op)
        {
            int x = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
            switch (x)
            {
                case 0:
                    switch (z)
                    {
                        case 0:
                            return y switch
                            {
                                0 => "NOP",
                                1 => "EX AF,AF'",
                                2 => "DJNZ " + Rel(),
                                3 => "JR " + Rel(),
                                _ => $"JR {CC[y - 4]},{Rel()}",
                            };
                        case 1:
                            return q == 0 ? $"LD {(p == 2 ? HL : RP[p])},{NN()}" : $"ADD {HL},{(p == 2 ? HL : RP[p])}";
                        case 2:
                            return (q, p) switch
                            {
                                (0, 0) => "LD (BC),A",
                                (0, 1) => "LD (DE),A",
                                (0, 2) => $"LD ({NN()}),{HL}",
                                (0, _) => $"LD ({NN()}),A",
                                (1, 0) => "LD A,(BC)",
                                (1, 1) => "LD A,(DE)",
                                (1, 2) => $"LD {HL},({NN()})",
                                _ => $"LD A,({NN()})",
                            };
                        case 3:
                            return (q == 0 ? "INC " : "DEC ") + (p == 2 ? HL : RP[p]);
                        case 4: return "INC " + Reg(y);
                        case 5: return "DEC " + Reg(y);
                        case 6:
                        {
                            string dst = Reg(y);
                            return $"LD {dst},{N()}";
                        }
                        default:
                            return new[] { "RLCA", "RRCA", "RLA", "RRA", "DAA", "CPL", "SCF", "CCF" }[y];
                    }
                case 1:
                    if (op == 0x76)
                        return "HALT";
                    if (_index != null && (y == 6 || z == 6))
                    {
                        // LD r,(IX+d) / LD (IX+d),r keep H and L
                        return y == 6 ? $"LD {IndexOperand()},{R[z]}" : $"LD {R[y]},{IndexOperand()}";
                    }
                    return $"LD {Reg(y)},{Reg(z)}";
                case 2:
                    return ALU[y] + Reg(z);
                default:
                    switch (z)
                    {
                        case 0: return "RET " + CC[y];
                        case 1:
                            if (q == 0)
                                return "POP " + (p == 2 ? HL : RP2[p]);
                            return p switch { 0 => "RET", 1 => "EXX", 2 => $"JP ({HL})", _ => $"LD SP,{HL}" };
                        case 2: return $"JP {CC[y]},{NN()}";
                        case 3:
                            return y switch
                            {
                                0 => "JP " + NN(),
                                2 => $"OUT ({N()}),A",
                                3 => $"IN A,({N()})",
                                4 => $"EX (SP),{HL}",
                                5 => "EX DE,HL",
                                6 => "DI",
                                7 => "EI",
                                _ => "DB $CB",
                            };
                        case 4: return $"CALL {CC[y]},{NN()}";
                        case 5:
                            if (q == 0)
                                return "PUSH " + (p == 2 ? HL : RP2[p]);
                            return p == 0 ? "CALL " + NN() : "DB $" + op.ToString("X2");
                        case 6: return ALU[y] + N();
                        default: return "RST " + Addr((ushort)(y * 8));
                    }
            }
        }

        private string DecodeCB(byte op)
        {
            int x = op >> 6, y = (op >> 3) & 7, z = op & 7;
            return x switch
            {
                0 => $"{ROT[y]} {R[z]}",
                1 => $"BIT {y},{R[z]}",
                2 => $"RES {y},{R[z]}",
                _ => $"SET {y},{R[z]}",
            };
        }

        private string DecodeIndexedCB()
        {
            string operand = IndexOperand();
            byte op = Next();
            int x = op >> 6, y = (op >> 3) & 7, z = op & 7;
            string copy = z == 6 || x == 1 ? "" : "," + R[z];
            return x switch
            {
                0 => $"{ROT[y]} {operand}{copy}",
                1 => $"BIT {y},{operand}",
                2 => $"RES {y},{operand}{copy}",
                _ => $"SET {y},{operand}{copy}",
            };
        }

        private string DecodeED(byte op)
        {
            int x = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
            if (x == 1)
            {
                switch (z)
                {
                    case 0: return y == 6 ? "IN (C)" : $"IN {R[y]},(C)";
                    case 1: return y == 6 ? "OUT (C),0" : $"OUT (C),{R[y]}";
                    case 2: return (q == 0 ? "SBC HL," : "ADC HL,") + RP[p];
                    case 3: return q == 0 ? $"LD ({NN()}),{RP[p]}" : $"LD {RP[p]},({NN()})";
                    case 4: return "NEG";
                    case 5: return y == 1 ? "RETI" : "RETN";
                    case 6: return "IM " + ((y & 3) switch { 0 => "0", 1 => "0/1", 2 => "1", _ => "2" });
                    default:
                        return new[] { "LD I,A", "LD R,A", "LD A,I", "LD A,R", "RRD", "RLD", "NOP*", "NOP*" }[y];
                }
            }
            if (x == 2 && z <= 3 && y >= 4)
                return BLOCK[(y - 4) * 4 + z];
            return $"NOP* (ED {op:X2})";
        }
    }
}
