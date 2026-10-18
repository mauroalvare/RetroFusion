namespace Retro.Systems.Spectrum;

/// <summary>
/// Reads the text on the Spectrum screen by matching every 8x8 cell against the
/// character set in ROM (normal or inverse video). Used by tests and the debugger.
/// </summary>
public static class ScreenText
{
    public static string[] Read(Spectrum48 m)
    {
        var glyphs = new Dictionary<ulong, char>();
        for (int c = 0x20; c < 0x80; c++)
        {
            ulong key = 0, inverse = 0;
            for (int row = 0; row < 8; row++)
            {
                byte b = m.Peek((uint)(0x3D00 + (c - 0x20) * 8 + row));
                key = (key << 8) | b;
                inverse = (inverse << 8) | (byte)~b;
            }
            char ch = c switch { 0x60 => '£', 0x7F => '©', _ => (char)c };
            glyphs.TryAdd(key, ch);
            glyphs.TryAdd(inverse, ch);
        }
        var lines = new string[24];
        var buf = new char[32];
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                ulong key = 0;
                for (int row = 0; row < 8; row++)
                {
                    int line = y * 8 + row;
                    int addr = 0x4000 | ((line & 0xC0) << 5) | ((line & 0x07) << 8) | ((line & 0x38) << 2) | x;
                    key = (key << 8) | m.Peek((uint)addr);
                }
                buf[x] = glyphs.TryGetValue(key, out var ch) ? ch : '?';
            }
            lines[y] = new string(buf);
        }
        return lines;
    }

    public static string ReadAll(Spectrum48 m) => string.Join("\n", Read(m));
}
