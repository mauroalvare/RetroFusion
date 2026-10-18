using Retro.Core;

namespace Retro.Cpu.Z80;

/// <summary>Control flow of Z80 instructions (for the routine analyzer).</summary>
public sealed class Z80Flow : IFlowAnalyzer
{
    private readonly Z80Disassembler _dis = new();

    public FlowInfo Analyze(Func<uint, byte> peek, uint address)
    {
        int length = _dis.Disassemble(peek, address).Length;
        byte op = peek(address & 0xFFFF);
        uint W() => (uint)(peek((address + 1) & 0xFFFF) | (peek((address + 2) & 0xFFFF) << 8));
        uint Rel() => (uint)((address + 2 + (sbyte)peek((address + 1) & 0xFFFF)) & 0xFFFF);
        switch (op)
        {
            case 0xCD: return new(length, FlowKind.Call, W());
            case 0xC4: case 0xCC: case 0xD4: case 0xDC: case 0xE4: case 0xEC: case 0xF4: case 0xFC:
                return new(length, FlowKind.CallIf, W());
            case 0xC3: return new(length, FlowKind.Jump, W());
            case 0xC2: case 0xCA: case 0xD2: case 0xDA: case 0xE2: case 0xEA: case 0xF2: case 0xFA:
                return new(length, FlowKind.JumpIf, W());
            case 0x18: return new(length, FlowKind.Jump, Rel());
            case 0x10: case 0x20: case 0x28: case 0x30: case 0x38:
                return new(length, FlowKind.JumpIf, Rel());
            case 0xC9: return new(length, FlowKind.Return, null);
            case 0xC0: case 0xC8: case 0xD0: case 0xD8: case 0xE0: case 0xE8: case 0xF0: case 0xF8:
                return new(length, FlowKind.ReturnIf, null);
            case 0xE9: return new(length, FlowKind.JumpIndirect, null);
            case 0x76: return new(length, FlowKind.Halt, null);
            case 0xC7: case 0xCF: case 0xD7: case 0xDF: case 0xE7: case 0xEF: case 0xF7: case 0xFF:
                return new(length, FlowKind.Call, (uint)(op & 0x38));
            case 0xED:
            {
                byte op2 = peek((address + 1) & 0xFFFF);
                if ((op2 & 0xC7) == 0x45)   // RETN / RETI and duplicates
                    return new(length, FlowKind.Return, null);
                break;
            }
            case 0xDD: case 0xFD:
                if (peek((address + 1) & 0xFFFF) == 0xE9)   // JP (IX) / JP (IY)
                    return new(length, FlowKind.JumpIndirect, null);
                break;
        }
        return new(length, FlowKind.Normal, null);
    }
}
