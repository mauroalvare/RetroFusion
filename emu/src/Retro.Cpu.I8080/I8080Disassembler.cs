using Retro.Core;

namespace Retro.Cpu.I8080;

/// <summary>Intel 8080 disassembler (Intel mnemonics, $ for hexadecimal).</summary>
public sealed class I8080Disassembler : IDisassembler
{
    private static readonly string[] R = { "B", "C", "D", "E", "H", "L", "M", "A" };
    private static readonly string[] RP = { "B", "D", "H", "SP" };
    private static readonly string[] RP2 = { "B", "D", "H", "PSW" };
    private static readonly string[] CC = { "NZ", "Z", "NC", "C", "PO", "PE", "P", "M" };
    private static readonly string[] ALU = { "ADD", "ADC", "SUB", "SBB", "ANA", "XRA", "ORA", "CMP" };
    private static readonly string[] ALUI = { "ADI", "ACI", "SUI", "SBI", "ANI", "XRI", "ORI", "CPI" };
    private static readonly string[] MISC = { "RLC", "RRC", "RAL", "RAR", "DAA", "CMA", "STC", "CMC" };

    public Func<ushort, string?>? Symbols { get; set; }

    public DisassembledInstruction Disassemble(Func<uint, byte> peek, uint address)
    {
        ushort a = (ushort)address;
        byte op = peek(a);
        byte b1 = peek((ushort)(a + 1)), b2 = peek((ushort)(a + 2));
        ushort word = (ushort)(b1 | (b2 << 8));
        string W() => Symbols?.Invoke(word) ?? $"${word:X4}";
        string N() => $"${b1:X2}";
        int x = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
        int len = 1;
        string text;
        switch (x)
        {
            case 0:
                switch (z)
                {
                    case 0: text = op == 0 ? "NOP" : "NOP*"; break;
                    case 1: text = q == 0 ? $"LXI {RP[p]},{W()}" : $"DAD {RP[p]}"; len = q == 0 ? 3 : 1; break;
                    case 2:
                        (text, len) = y switch
                        {
                            0 => ("STAX B", 1), 1 => ("LDAX B", 1), 2 => ("STAX D", 1), 3 => ("LDAX D", 1),
                            4 => ($"SHLD {W()}", 3), 5 => ($"LHLD {W()}", 3), 6 => ($"STA {W()}", 3), _ => ($"LDA {W()}", 3),
                        };
                        break;
                    case 3: text = (q == 0 ? "INX " : "DCX ") + RP[p]; break;
                    case 4: text = "INR " + R[y]; break;
                    case 5: text = "DCR " + R[y]; break;
                    case 6: text = $"MVI {R[y]},{N()}"; len = 2; break;
                    default: text = MISC[y]; break;
                }
                break;
            case 1:
                text = op == 0x76 ? "HLT" : $"MOV {R[y]},{R[z]}";
                break;
            case 2:
                text = $"{ALU[y]} {R[z]}";
                break;
            default:
                switch (z)
                {
                    case 0: text = "R" + CC[y]; break;
                    case 1:
                        text = q == 0 ? "POP " + RP2[p] : p switch { 0 => "RET", 1 => "RET*", 2 => "PCHL", _ => "SPHL" };
                        break;
                    case 2: text = $"J{CC[y]} {W()}"; len = 3; break;
                    case 3:
                        (text, len) = y switch
                        {
                            0 => ($"JMP {W()}", 3), 1 => ($"JMP* {W()}", 3), 2 => ($"OUT {N()}", 2), 3 => ($"IN {N()}", 2),
                            4 => ("XTHL", 1), 5 => ("XCHG", 1), 6 => ("DI", 1), _ => ("EI", 1),
                        };
                        break;
                    case 4: text = $"C{CC[y]} {W()}"; len = 3; break;
                    case 5:
                        if (q == 0) text = "PUSH " + RP2[p];
                        else { text = (p == 0 ? "CALL " : "CALL* ") + W(); len = 3; }
                        break;
                    case 6: text = $"{ALUI[y]} {N()}"; len = 2; break;
                    default: text = "RST " + y; break;
                }
                break;
        }
        var bytes = new byte[len];
        for (int i = 0; i < len; i++)
            bytes[i] = peek((ushort)(a + i));
        return new DisassembledInstruction(address, len, text, bytes);
    }
}
