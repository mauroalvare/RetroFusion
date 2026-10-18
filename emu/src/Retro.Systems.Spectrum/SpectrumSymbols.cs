namespace Retro.Systems.Spectrum;

/// <summary>Well-known 48K ROM routines and system variables (for the debugger).</summary>
public static class SpectrumSymbols
{
    /// <summary>ROM tape loader entry (LD-BYTES).</summary>
    public const ushort LdBytes = 0x0556;

    public static readonly Dictionary<ushort, string> Rom = new()
    {
        [0x0000] = "START", [0x0008] = "ERROR_1", [0x0010] = "PRINT_A_1", [0x0018] = "GET_CHAR",
        [0x0020] = "NEXT_CHAR", [0x0028] = "FP_CALC", [0x0030] = "BC_SPACES", [0x0038] = "MASK_INT",
        [0x0066] = "RESET_NMI", [0x028E] = "KEY_SCAN", [0x02BF] = "KEYBOARD", [0x03B5] = "BEEPER",
        [0x03F8] = "BEEP", [0x04C2] = "SA_BYTES", [0x0556] = "LD_BYTES", [0x05E3] = "LD_EDGE_2",
        [0x05E7] = "LD_EDGE_1", [0x0D6B] = "CLS", [0x0DAF] = "CL_ALL", [0x0DD9] = "CL_SET",
        [0x0E44] = "CL_LINE", [0x1601] = "CHAN_OPEN", [0x203C] = "PR_STRING", [0x22AA] = "PIXEL_ADD",
        [0x2D2B] = "STACK_BC", [0x2DA2] = "FP_TO_BC", [0x3D00] = "CHARSET",
        [0x4000] = "SCREEN", [0x5800] = "ATTRS", [0x5C00] = "KSTATE", [0x5C08] = "LAST_K",
        [0x5C3A] = "ERR_NR", [0x5C3B] = "FLAGS", [0x5C48] = "BORDCR", [0x5C4B] = "VARS",
        [0x5C53] = "PROG", [0x5C78] = "FRAMES", [0x5C7B] = "UDG", [0x5CB2] = "RAMTOP",
    };
}
