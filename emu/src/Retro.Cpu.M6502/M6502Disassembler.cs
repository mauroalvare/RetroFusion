using Retro.Core;

namespace Retro.Cpu.M6502;

/// <summary>Addressing modes of the 6502, as written in assembly.</summary>
public enum M6502Mode { Imp, Acc, Imm, Zp, ZpX, ZpY, Abs, AbsX, AbsY, IndX, IndY, Ind, Rel }

/// <summary>6502 disassembler with the usual names for the undocumented opcodes (LAX, SAX, DCP...).</summary>
public sealed class M6502Disassembler : IDisassembler
{
    public static readonly (string Name, M6502Mode Mode)[] Table = BuildTable();

    public Func<ushort, string?>? Symbols { get; set; }

    public static int Length(M6502Mode mode) => mode switch
    {
        M6502Mode.Imp or M6502Mode.Acc => 1,
        M6502Mode.Abs or M6502Mode.AbsX or M6502Mode.AbsY or M6502Mode.Ind => 3,
        _ => 2,
    };

    public DisassembledInstruction Disassemble(Func<uint, byte> peek, uint address)
    {
        byte op = peek(address & 0xFFFF);
        var (name, mode) = Table[op];
        int len = Length(mode);
        var bytes = new byte[len];
        for (int i = 0; i < len; i++)
            bytes[i] = peek((uint)((address + i) & 0xFFFF));
        byte b1 = len > 1 ? bytes[1] : (byte)0;
        ushort w = len > 2 ? (ushort)(b1 | (bytes[2] << 8)) : b1;
        string Addr(ushort a) => Symbols?.Invoke(a) ?? $"${a:X4}";
        string Zp(byte a) => Symbols?.Invoke(a) ?? $"${a:X2}";
        string operand = mode switch
        {
            M6502Mode.Imp => "",
            M6502Mode.Acc => " A",
            M6502Mode.Imm => $" #${b1:X2}",
            M6502Mode.Zp => " " + Zp(b1),
            M6502Mode.ZpX => $" {Zp(b1)},X",
            M6502Mode.ZpY => $" {Zp(b1)},Y",
            M6502Mode.Abs => " " + Addr(w),
            M6502Mode.AbsX => $" {Addr(w)},X",
            M6502Mode.AbsY => $" {Addr(w)},Y",
            M6502Mode.IndX => $" ({Zp(b1)},X)",
            M6502Mode.IndY => $" ({Zp(b1)}),Y",
            M6502Mode.Ind => $" ({Addr(w)})",
            M6502Mode.Rel => " " + Addr((ushort)(address + 2 + (sbyte)b1)),
            _ => "",
        };
        return new DisassembledInstruction(address, len, name + operand, bytes);
    }

    private static (string, M6502Mode)[] BuildTable()
    {
        var t = new (string, M6502Mode)[256];
        // the regular columns: aaabbbcc
        string[] alu = { "ORA", "AND", "EOR", "ADC", "STA", "LDA", "CMP", "SBC" };
        string[] rmw = { "ASL", "ROL", "LSR", "ROR", "STX", "LDX", "DEC", "INC" };
        string[] ill = { "SLO", "RLA", "SRE", "RRA", "SAX", "LAX", "DCP", "ISC" };
        M6502Mode[] modes01 = { M6502Mode.IndX, M6502Mode.Zp, M6502Mode.Imm, M6502Mode.Abs, M6502Mode.IndY, M6502Mode.ZpX, M6502Mode.AbsY, M6502Mode.AbsX };
        M6502Mode[] modes02 = { M6502Mode.Imm, M6502Mode.Zp, M6502Mode.Acc, M6502Mode.Abs, M6502Mode.Imp, M6502Mode.ZpX, M6502Mode.Imp, M6502Mode.AbsX };
        for (int op = 0; op < 256; op++)
        {
            int a = op >> 5, b = (op >> 2) & 7;
            switch (op & 3)
            {
                case 1: t[op] = (alu[a], modes01[b]); break;
                case 2: t[op] = (rmw[a], modes02[b]); break;
                case 3: t[op] = (ill[a], modes01[b]); break;
                default: t[op] = ("NOP", M6502Mode.Imp); break;
            }
        }
        // column 2 exceptions
        foreach (int op in new[] { 0x02, 0x12, 0x22, 0x32, 0x42, 0x52, 0x62, 0x72, 0x92, 0xB2, 0xD2, 0xF2 })
            t[op] = ("JAM", M6502Mode.Imp);
        foreach (int op in new[] { 0x82, 0xC2, 0xE2 })
            t[op] = ("NOP", M6502Mode.Imm);
        foreach (int op in new[] { 0x1A, 0x3A, 0x5A, 0x7A, 0xDA, 0xFA })
            t[op] = ("NOP", M6502Mode.Imp);
        t[0x8A] = ("TXA", M6502Mode.Imp); t[0x9A] = ("TXS", M6502Mode.Imp); t[0xAA] = ("TAX", M6502Mode.Imp);
        t[0xBA] = ("TSX", M6502Mode.Imp); t[0xCA] = ("DEX", M6502Mode.Imp); t[0xEA] = ("NOP", M6502Mode.Imp);
        t[0x96] = ("STX", M6502Mode.ZpY); t[0xB6] = ("LDX", M6502Mode.ZpY); t[0xBE] = ("LDX", M6502Mode.AbsY);
        t[0x9E] = ("SHX", M6502Mode.AbsY);
        // column 3 exceptions
        t[0x97] = ("SAX", M6502Mode.ZpY); t[0xB7] = ("LAX", M6502Mode.ZpY); t[0xBF] = ("LAX", M6502Mode.AbsY);
        t[0x0B] = ("ANC", M6502Mode.Imm); t[0x2B] = ("ANC", M6502Mode.Imm); t[0x4B] = ("ALR", M6502Mode.Imm);
        t[0x6B] = ("ARR", M6502Mode.Imm); t[0x8B] = ("ANE", M6502Mode.Imm); t[0xAB] = ("LXA", M6502Mode.Imm);
        t[0xCB] = ("SBX", M6502Mode.Imm); t[0xEB] = ("SBC", M6502Mode.Imm);
        t[0x93] = ("SHA", M6502Mode.IndY); t[0x9F] = ("SHA", M6502Mode.AbsY); t[0x9B] = ("TAS", M6502Mode.AbsY);
        t[0xBB] = ("LAS", M6502Mode.AbsY);
        t[0x89] = ("NOP", M6502Mode.Imm);
        // column 0
        string[] col0Names = { "BRK", "JSR", "RTI", "RTS", "NOP", "LDY", "CPY", "CPX" };
        t[0x00] = ("BRK", M6502Mode.Imp); t[0x20] = ("JSR", M6502Mode.Abs); t[0x40] = ("RTI", M6502Mode.Imp); t[0x60] = ("RTS", M6502Mode.Imp);
        t[0x80] = ("NOP", M6502Mode.Imm); t[0xA0] = ("LDY", M6502Mode.Imm); t[0xC0] = ("CPY", M6502Mode.Imm); t[0xE0] = ("CPX", M6502Mode.Imm);
        _ = col0Names;
        t[0x04] = ("NOP", M6502Mode.Zp); t[0x24] = ("BIT", M6502Mode.Zp); t[0x44] = ("NOP", M6502Mode.Zp); t[0x64] = ("NOP", M6502Mode.Zp);
        t[0x84] = ("STY", M6502Mode.Zp); t[0xA4] = ("LDY", M6502Mode.Zp); t[0xC4] = ("CPY", M6502Mode.Zp); t[0xE4] = ("CPX", M6502Mode.Zp);
        t[0x08] = ("PHP", M6502Mode.Imp); t[0x28] = ("PLP", M6502Mode.Imp); t[0x48] = ("PHA", M6502Mode.Imp); t[0x68] = ("PLA", M6502Mode.Imp);
        t[0x88] = ("DEY", M6502Mode.Imp); t[0xA8] = ("TAY", M6502Mode.Imp); t[0xC8] = ("INY", M6502Mode.Imp); t[0xE8] = ("INX", M6502Mode.Imp);
        t[0x0C] = ("NOP", M6502Mode.Abs); t[0x2C] = ("BIT", M6502Mode.Abs); t[0x4C] = ("JMP", M6502Mode.Abs); t[0x6C] = ("JMP", M6502Mode.Ind);
        t[0x8C] = ("STY", M6502Mode.Abs); t[0xAC] = ("LDY", M6502Mode.Abs); t[0xCC] = ("CPY", M6502Mode.Abs); t[0xEC] = ("CPX", M6502Mode.Abs);
        string[] branches = { "BPL", "BMI", "BVC", "BVS", "BCC", "BCS", "BNE", "BEQ" };
        for (int i = 0; i < 8; i++)
            t[0x10 + i * 0x20] = (branches[i], M6502Mode.Rel);
        foreach (int op in new[] { 0x14, 0x34, 0x54, 0x74, 0xD4, 0xF4 })
            t[op] = ("NOP", M6502Mode.ZpX);
        t[0x94] = ("STY", M6502Mode.ZpX); t[0xB4] = ("LDY", M6502Mode.ZpX);
        string[] flags = { "CLC", "SEC", "CLI", "SEI", "TYA", "CLV", "CLD", "SED" };
        for (int i = 0; i < 8; i++)
            t[0x18 + i * 0x20] = (flags[i], M6502Mode.Imp);
        foreach (int op in new[] { 0x1C, 0x3C, 0x5C, 0x7C, 0xDC, 0xFC })
            t[op] = ("NOP", M6502Mode.AbsX);
        t[0x9C] = ("SHY", M6502Mode.AbsX); t[0xBC] = ("LDY", M6502Mode.AbsX);
        return t;
    }
}

/// <summary>Control flow of 6502 instructions, for splitting programs into routines.</summary>
public sealed class M6502Flow : IFlowAnalyzer
{
    public FlowInfo Analyze(Func<uint, byte> peek, uint address)
    {
        address &= 0xFFFF;
        byte op = peek(address);
        var (name, mode) = M6502Disassembler.Table[op];
        int len = M6502Disassembler.Length(mode);
        ushort Word() => (ushort)(peek((address + 1) & 0xFFFF) | (peek((address + 2) & 0xFFFF) << 8));
        return name switch
        {
            "JSR" => new FlowInfo(len, FlowKind.Call, Word()),
            "JMP" when mode == M6502Mode.Abs => new FlowInfo(len, FlowKind.Jump, Word()),
            "JMP" => new FlowInfo(len, FlowKind.JumpIndirect, null),
            "RTS" or "RTI" => new FlowInfo(len, FlowKind.Return, null),
            "BRK" or "JAM" => new FlowInfo(len, FlowKind.Halt, null),
            _ when mode == M6502Mode.Rel => new FlowInfo(len, FlowKind.JumpIf, (uint)((address + 2 + (sbyte)peek((address + 1) & 0xFFFF)) & 0xFFFF)),
            _ => new FlowInfo(len, FlowKind.Normal, null),
        };
    }
}
