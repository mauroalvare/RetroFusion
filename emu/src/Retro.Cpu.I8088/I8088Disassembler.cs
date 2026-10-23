using Retro.Core;

namespace Retro.Cpu.I8088;

/// <summary>8088 integer instruction decoder. An optional CS preserves segment wrapping in the live view.</summary>
public sealed class I8088Disassembler(Func<ushort>? codeSegment = null) : IDisassembler
{
    private static readonly string[] Words = ["AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI"];
    private static readonly string[] Bytes = ["AL", "CL", "DL", "BL", "AH", "CH", "DH", "BH"];
    private static readonly string[] Segments = ["ES", "CS", "SS", "DS"];
    private static readonly string[] Arithmetic = ["ADD", "OR", "ADC", "SBB", "AND", "SUB", "XOR", "CMP"];
    private static readonly string[] Conditions = ["JO", "JNO", "JB", "JAE", "JE", "JNE", "JBE", "JA", "JS", "JNS", "JP", "JNP", "JL", "JGE", "JLE", "JG"];
    private static readonly string[] Addresses = ["BX+SI", "BX+DI", "BP+SI", "BP+DI", "SI", "DI", "BP", "BX"];
    private static readonly string[] Shifts = ["ROL", "ROR", "RCL", "RCR", "SHL", "SHR", "SETMO", "SAR"];
    public DisassembledInstruction Disassemble(Func<uint, byte> peek, uint address) => Decode(peek, address).Instruction;
    internal (DisassembledInstruction Instruction, FlowInfo Flow) Decode(Func<uint, byte> peek, uint address)
    {
        const uint mask = 0xFFFFF;
        address &= mask;
        var bytes = new List<byte>();
        ushort? segment = codeSegment?.Invoke();
        uint offset = segment is ushort cs ? (address - ((uint)cs << 4)) & mask : 0;
        if (offset > ushort.MaxValue) segment = null;
        uint Position(int relative) => segment is ushort code ? I8088Cpu.Physical(code, (ushort)(offset + relative)) : (address + (uint)relative) & mask;
        byte Read() { byte value = peek(Position(bytes.Count)); bytes.Add(value); return value; }
        ushort Word() { int low = Read(); return (ushort)(low | Read() << 8); }
        string Register(int number, bool wide) => (wide ? Words : Bytes)[number];
        string Immediate(bool wide) => wide ? $"${Word():X4}" : $"${Read():X2}";
        string? memorySegment = null;
        string Operand(bool wide, out int reg)
        {
            int modrm = Read(), mode = modrm >> 6, rm = modrm & 7;
            reg = (modrm >> 3) & 7;
            if (mode == 3) return Register(rm, wide);
            string expression = mode == 0 && rm == 6 ? $"${Word():X4}" : Addresses[rm];
            int displacement = mode == 1 ? (sbyte)Read() : mode == 2 ? (short)Word() : 0;
            if (displacement != 0) expression += (displacement < 0 ? "-" : "+") + $"${Math.Abs(displacement):X}";
            return (wide ? "word " : "byte ") + "[" + (memorySegment == null ? "" : memorySegment + ":") + expression + "]";
        }
        FlowKind kind = FlowKind.Normal; uint? target = null;
        string Relative(int displacement, FlowKind flow)
        {
            kind = flow; target = Position(bytes.Count + displacement); return $"${target:X5}";
        }
        string Far(FlowKind flow)
        {
            ushort ip = Word(), cs = Word(); kind = flow; target = I8088Cpu.Physical(cs, ip);
            return $"${cs:X4}:${ip:X4}";
        }
        uint Vector(byte vector)
        {
            uint a = (uint)vector * 4;
            return I8088Cpu.Physical((ushort)(peek(a + 2) | peek(a + 3) << 8), (ushort)(peek(a) | peek(a + 1) << 8));
        }
        string repeat = "", locking = "";
        byte op;
        while (true)
        {
            if (bytes.Count >= 64)
                return (new(address, 1, $"DB ${bytes[0]:X2}", [bytes[0]]), new(1, FlowKind.Halt, null));
            op = Read();
            if (op is 0x26 or 0x2E or 0x36 or 0x3E) memorySegment = Segments[(op >> 3) & 3];
            else if (op is 0xF0 or 0xF1) locking = "LOCK ";
            else if (op is 0xF2 or 0xF3) repeat = op == 0xF2 ? "REPNE " : "REP ";
            else break;
        }
        bool word = (op & 1) != 0;
        string text;
        if (op <= 0x3D && (op & 7) <= 5)
        {
            string mnemonic = Arithmetic[op >> 3];
            if ((op & 7) < 4)
            {
                string operand = Operand(word, out int reg), register = Register(reg, word);
                text = mnemonic + " " + ((op & 2) == 0 ? operand + ", " + register : register + ", " + operand);
            }
            else text = mnemonic + " " + Register(0, word) + ", " + Immediate(word);
        }
        else if (op is >= 0x40 and <= 0x4F) text = (op < 0x48 ? "INC " : "DEC ") + Words[op & 7];
        else if (op is >= 0x50 and <= 0x5F) text = (op < 0x58 ? "PUSH " : "POP ") + Words[op & 7];
        else if (op is >= 0x70 and <= 0x7F) text = Conditions[op & 15] + " " + Relative((sbyte)Read(), FlowKind.JumpIf);
        else if (op is >= 0x91 and <= 0x97) text = "XCHG AX, " + Words[op & 7];
        else if (op is >= 0xB0 and <= 0xBF) text = "MOV " + Register(op & 7, op >= 0xB8) + ", " + Immediate(op >= 0xB8);
        else switch (op)
        {
            case 0x06: case 0x0E: case 0x16: case 0x1E: text = "PUSH " + Segments[(op >> 3) & 3]; break;
            case 0x07: case 0x0F: case 0x17: case 0x1F: text = "POP " + Segments[(op >> 3) & 3]; break;
            case 0x27: text = "DAA"; break; case 0x2F: text = "DAS"; break;
            case 0x37: text = "AAA"; break; case 0x3F: text = "AAS"; break;
            case >= 0x80 and <= 0x83:
            {
                string operand = Operand(word, out int reg);
                string immediate = op == 0x83 ? ((sbyte)Read()).ToString() : Immediate(word);
                text = Arithmetic[reg] + " " + operand + ", " + immediate; break;
            }
            case >= 0x84 and <= 0x8B:
            {
                string operand = Operand(word, out int reg), register = Register(reg, word);
                string mnemonic = op < 0x86 ? "TEST" : op < 0x88 ? "XCHG" : "MOV";
                text = mnemonic + " " + (op >= 0x8A ? register + ", " + operand : operand + ", " + register); break;
            }
            case 0x8C: case 0x8E:
            {
                string operand = Operand(true, out int reg), register = Segments[reg & 3];
                text = "MOV " + (op == 0x8C ? operand + ", " + register : register + ", " + operand); break;
            }
            case 0x8D: case 0xC4: case 0xC5:
            {
                string operand = Operand(true, out int reg);
                text = (op == 0x8D ? "LEA " : op == 0xC4 ? "LES " : "LDS ") + Words[reg] + ", " + operand; break;
            }
            case 0x8F: text = "POP " + Operand(true, out _); break;
            case 0x90: text = "NOP"; break;
            case 0x98: text = "CBW"; break; case 0x99: text = "CWD"; break;
            case 0x9A: text = "CALL " + Far(FlowKind.Call); break;
            case 0x9B: text = "WAIT"; break;
            case 0x9C: text = "PUSHF"; break; case 0x9D: text = "POPF"; break;
            case 0x9E: text = "SAHF"; break; case 0x9F: text = "LAHF"; break;
            case >= 0xA0 and <= 0xA3:
            {
                string memory = "[" + (memorySegment == null ? "" : memorySegment + ":") + $"${Word():X4}]";
                text = "MOV " + (op < 0xA2 ? Register(0, word) + ", " + memory : memory + ", " + Register(0, word)); break;
            }
            case 0xA8: case 0xA9: text = "TEST " + Register(0, word) + ", " + Immediate(word); break;
            case 0xA4: case 0xA5: case 0xA6: case 0xA7: case >= 0xAA and <= 0xAF:
            {
                text = (op & 0xFE) switch { 0xA4 => "MOVS", 0xA6 => "CMPS", 0xAA => "STOS", 0xAC => "LODS", _ => "SCAS" };
                text += word ? "W" : "B";
                if (memorySegment != null) text = memorySegment + ": " + text;
                break;
            }
            case 0xC2: case 0xCA: text = (op == 0xC2 ? "RET " : "RETF ") + Immediate(true); kind = FlowKind.Return; break;
            case 0xC3: text = "RET"; kind = FlowKind.Return; break;
            case 0xCB: text = "RETF"; kind = FlowKind.Return; break;
            case 0xC6: case 0xC7: text = "MOV " + Operand(word, out _) + ", " + Immediate(word); break;
            case 0xCC: case 0xCD:
            {
                byte vector = op == 0xCC ? (byte)3 : Read(); text = $"INT ${vector:X2}"; target = Vector(vector); kind = FlowKind.Call; break;
            }
            case 0xCE: text = "INTO"; target = Vector(4); kind = FlowKind.CallIf; break;
            case 0xCF: text = "IRET"; kind = FlowKind.Return; break;
            case >= 0xD0 and <= 0xD3:
            {
                string operand = Operand(word, out int reg);
                text = Shifts[reg] + " " + operand + ", " + (op < 0xD2 ? "1" : "CL"); break;
            }
            case 0xD4: case 0xD5: text = (op == 0xD4 ? "AAM " : "AAD ") + Immediate(false); break;
            case 0xD6: text = "SALC"; break; case 0xD7: text = "XLAT"; break;
            case >= 0xD8 and <= 0xDF:
            {
                string operand = Operand(true, out int reg); text = $"ESC ${(op & 7) * 8 + reg:X2}, " + operand; break;
            }
            case >= 0xE0 and <= 0xE3:
                text = new[] { "LOOPNE ", "LOOPE ", "LOOP ", "JCXZ " }[op & 3] + Relative((sbyte)Read(), FlowKind.JumpIf); break;
            case >= 0xE4 and <= 0xE7:
            {
                string port = Immediate(false), register = Register(0, word);
                text = op < 0xE6 ? "IN " + register + ", " + port : "OUT " + port + ", " + register; break;
            }
            case 0xE8: case 0xE9: text = (op == 0xE8 ? "CALL " : "JMP ") + Relative((short)Word(), op == 0xE8 ? FlowKind.Call : FlowKind.Jump); break;
            case 0xEA: text = "JMP " + Far(FlowKind.Jump); break;
            case 0xEB: text = "JMP " + Relative((sbyte)Read(), FlowKind.Jump); break;
            case >= 0xEC and <= 0xEF: text = op < 0xEE ? "IN " + Register(0, word) + ", DX" : "OUT DX, " + Register(0, word); break;
            case 0xF4: text = "HLT"; kind = FlowKind.Halt; break;
            case 0xF5: text = "CMC"; break;
            case 0xF6: case 0xF7:
            {
                string operand = Operand(word, out int reg);
                text = new[] { "TEST", "TEST", "NOT", "NEG", "MUL", "IMUL", "DIV", "IDIV" }[reg] + " " + operand;
                if (reg < 2) text += ", " + Immediate(word); break;
            }
            case >= 0xF8 and <= 0xFD: text = new[] { "CLC", "STC", "CLI", "STI", "CLD", "STD" }[op - 0xF8]; break;
            case 0xFE: case 0xFF:
            {
                string operand = Operand(op == 0xFF, out int reg);
                text = new[] { "INC", "DEC", "CALL", "CALL FAR", "JMP", "JMP FAR", "PUSH", "DB" }[reg] + " " + operand;
                if (reg is 2 or 3) kind = FlowKind.Call;
                if (reg is 4 or 5) kind = FlowKind.JumpIndirect;
                break;
            }
            default: text = $"DB ${op:X2}"; kind = FlowKind.Halt; break;
        }
        text = locking + repeat + text;
        return (new(address, bytes.Count, text, bytes.ToArray()), new(bytes.Count, kind, target) { Next = Position(bytes.Count) });
    }
}

public sealed class I8088Flow(I8088Disassembler? disassembler = null) : IFlowAnalyzer
{
    private readonly I8088Disassembler _disassembler = disassembler ?? new();
    public int AddressBits => 20;
    public FlowInfo Analyze(Func<uint, byte> peek, uint address) => _disassembler.Decode(peek, address).Flow;
}