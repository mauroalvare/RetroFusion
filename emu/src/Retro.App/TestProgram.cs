using System.Globalization;

namespace Retro.App;

/// <summary>Bytes typed in the debugger's test program window.</summary>
public static class TestProgram
{
    /// <summary>Hex bytes separated by spaces or commas, with an optional 0x or $ prefix; null when one is not a byte.</summary>
    public static byte[]? ParseHex(string text)
    {
        var parts = text.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? parts[i][2..]
                : parts[i].StartsWith('$') ? parts[i][1..] : parts[i];
            if (part.Length is 0 or > 2 || !byte.TryParse(part, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
                return null;
        }
        return bytes;
    }
}
