using Retro.Core;

namespace Retro.Studio;

/// <summary>A decoded 1-bit graphic.</summary>
public sealed record SpriteImage(int Width, int Height, bool[] Pixels);

/// <summary>
/// The picture of a fusion game. Scripts draw on it every tick: pictures of the emulated
/// games (whole or a part, scaled, with a transparent colour), sprites taken from the
/// games' memory, rectangles, lines and text with the ZX Spectrum font.
/// Colours are 0xRRGGBB.
/// </summary>
public sealed class Canvas
{
    private readonly byte[]? _font;   // 96 characters x 8 bytes from the Spectrum ROM

    public VideoFrame Frame { get; private set; }
    public int Width => Frame.Width;
    public int Height => Frame.Height;

    public Canvas(int width, int height, byte[]? font)
    {
        Frame = new VideoFrame(width, height);
        _font = font;
    }

    public void Resize(int width, int height)
    {
        width = Math.Clamp(width, 32, 640);
        height = Math.Clamp(height, 32, 480);
        if (width != Frame.Width || height != Frame.Height)
            Frame = new VideoFrame(width, height);
    }

    private static int Opaque(int rgb) => unchecked((int)0xFF000000) | (rgb & 0xFFFFFF);

    private void Blend(int x, int y, int rgb, double alpha)
    {
        if ((uint)x >= (uint)Frame.Width || (uint)y >= (uint)Frame.Height)
            return;
        int i = y * Frame.Width + x;
        if (alpha >= 0.999)
        {
            Frame.Pixels[i] = Opaque(rgb);
            return;
        }
        if (alpha <= 0.001)
            return;
        int d = Frame.Pixels[i];
        int r = (int)(((d >> 16) & 0xFF) * (1 - alpha) + ((rgb >> 16) & 0xFF) * alpha);
        int g = (int)(((d >> 8) & 0xFF) * (1 - alpha) + ((rgb >> 8) & 0xFF) * alpha);
        int b = (int)((d & 0xFF) * (1 - alpha) + (rgb & 0xFF) * alpha);
        Frame.Pixels[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }

    public void Clear(int rgb) => Array.Fill(Frame.Pixels, Opaque(rgb));

    public void Rect(double x, double y, double w, double h, int rgb, double alpha)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        int x1 = (int)Math.Floor(x + w), y1 = (int)Math.Floor(y + h);
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
        x1 = Math.Min(Frame.Width, x1); y1 = Math.Min(Frame.Height, y1);
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
                Blend(xx, yy, rgb, alpha);
    }

    public void Box(double x, double y, double w, double h, int rgb)
    {
        Rect(x, y, w, 1, rgb, 1);
        Rect(x, y + h - 1, w, 1, rgb, 1);
        Rect(x, y, 1, h, rgb, 1);
        Rect(x + w - 1, y, 1, h, rgb, 1);
    }

    public void Line(double x0, double y0, double x1, double y1, int rgb)
    {
        int steps = (int)Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        if (steps > 2000)
            return;
        for (int i = 0; i <= steps; i++)
        {
            double t = steps == 0 ? 0 : (double)i / steps;
            Blend((int)Math.Round(x0 + (x1 - x0) * t), (int)Math.Round(y0 + (y1 - y0) * t), rgb, 1);
        }
    }

    public void Sprite(SpriteImage s, double x, double y, int rgb, int scale, bool flip, double alpha)
    {
        scale = Math.Clamp(scale, 1, 8);
        int ox = (int)Math.Floor(x), oy = (int)Math.Floor(y);
        for (int sy = 0; sy < s.Height; sy++)
            for (int sx = 0; sx < s.Width; sx++)
            {
                if (!s.Pixels[sy * s.Width + (flip ? s.Width - 1 - sx : sx)])
                    continue;
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        Blend(ox + sx * scale + dx, oy + sy * scale + dy, rgb, alpha);
            }
    }

    public void Text(string text, double x, double y, int rgb, int scale, bool center)
    {
        if (_font == null)
            return;
        scale = Math.Clamp(scale, 1, 8);
        if (center)
            x -= text.Length * 8 * scale / 2.0;
        int ox = (int)Math.Floor(x), oy = (int)Math.Floor(y);
        for (int i = 0; i < text.Length && i < 200; i++)
        {
            int ch = text[i] switch
            {
                'á' or 'à' => 'a', 'é' => 'e', 'í' => 'i', 'ó' => 'o', 'ú' or 'ü' => 'u', 'ñ' => 'n',
                'Á' => 'A', 'É' => 'E', 'Í' => 'I', 'Ó' => 'O', 'Ú' => 'U', 'Ñ' => 'N',
                '¡' => '!', '¿' => '?', _ => text[i],
            };
            if (ch < 32 || ch > 127)
                ch = '?';
            int glyph = (ch - 32) * 8;
            for (int row = 0; row < 8; row++)
            {
                byte bits = _font[glyph + row];
                for (int col = 0; col < 8; col++)
                    if ((bits & (0x80 >> col)) != 0)
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                                Blend(ox + (i * 8 + col) * scale + dx, oy + row * scale + dy, rgb, 1);
            }
        }
    }

    /// <summary>The character set of the ZX Spectrum ROM (at 3D00), used for text.</summary>
    public static byte[]? LoadFont(string romDirectory)
    {
        string path = Path.Combine(romDirectory, "48.rom");
        if (!File.Exists(path))
            return null;
        var rom = File.ReadAllBytes(path);
        return rom.Length >= 0x4000 ? rom[0x3D00..0x4000] : null;
    }
}
