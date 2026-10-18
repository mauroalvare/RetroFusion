using Retro.Core;

namespace Retro.Cpu.I8080;

/// <summary>Control flow of 8080 instructions (for the routine analyzer).</summary>
public sealed class I8080Flow : IFlowAnalyzer
{
    private readonly I8080Disassembler _dis = new();

    public FlowInfo Analyze(Func<uint, byte> peek, uint address)
    {
        int length = _dis.Disassemble(peek, address).Length;
        byte op = peek(address & 0xFFFF);
        uint W() => (uint)(peek((address + 1) & 0xFFFF) | (peek((address + 2) & 0xFFFF) << 8));
        int z = op & 7;
        if (op >= 0xC0)
        {
            switch (z)
            {
                case 0: return new(length, FlowKind.ReturnIf, null);
                case 1:
                    if (op is 0xC9 or 0xD9) return new(length, FlowKind.Return, null);
                    if (op == 0xE9) return new(length, FlowKind.JumpIndirect, null);
                    break;
                case 2: return new(length, FlowKind.JumpIf, W());
                case 3:
                    if (op is 0xC3 or 0xCB) return new(length, FlowKind.Jump, W());
                    break;
                case 4: return new(length, FlowKind.CallIf, W());
                case 5:
                    if (op is 0xCD or 0xDD or 0xED or 0xFD) return new(length, FlowKind.Call, W());
                    break;
                case 7: return new(length, FlowKind.Call, (uint)(op & 0x38));
            }
        }
        if (op == 0x76)
            return new(length, FlowKind.Halt, null);
        return new(length, FlowKind.Normal, null);
    }
}
