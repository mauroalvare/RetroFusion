namespace Retro.App;

/// <summary>Previous instructions are shown only when observed boundaries lead to the current PC.</summary>
internal static class DisassemblyViewport
{
    public static uint Start(uint pc, int addressBits, Func<uint, int> instructionLength, bool[]? executed)
    {
        if (addressBits is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(addressBits));
        uint mask = (uint)((1UL << addressBits) - 1);
        pc &= mask;
        if (executed == null) return pc;
        for (uint back = 24; back > 0; back--)
        {
            uint candidate = (pc - back) & mask, address = candidate;
            for (int guard = 0; address != pc && guard < 24; guard++)
            {
                if (address >= executed.Length || !executed[address]) break;
                int length = instructionLength(address);
                if (length <= 0 || (uint)length > ((pc - address) & mask)) break;
                address = (address + (uint)length) & mask;
            }
            if (address == pc) return candidate;
        }
        return pc;
    }
}
