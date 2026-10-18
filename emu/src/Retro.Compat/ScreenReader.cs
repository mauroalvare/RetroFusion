using System.Security.Cryptography;
using System.Text;
using Retro.Core;
using Retro.Systems.C64;
using Retro.Systems.Spectrum;

namespace Retro.Compat;

/// <summary>Colour statistics of a picture.</summary>
public sealed record PictureStats(int DistinctColors, double DominantShare);

/// <summary>
/// What is on the screen, in forms a script (or an agent) can check: the text shown, a
/// hash of the picture and its colours.
/// </summary>
public static class ScreenReader
{
    /// <summary>
    /// The text on screen, 1 line per text row, or null when the machine has no text mode
    /// to read. C64: the VIC-II text matrix (screen codes). Spectrum: each 8x8 cell compared
    /// with the ROM font (normal or inverse), so text drawn with the ROM's characters is read.
    /// </summary>
    public static string? Text(IMachine m) => m switch
    {
        C64 c => c.ScreenText(),
        Spectrum48 zx => ScreenText.ReadAll(zx) + "\n",
        _ => null,
    };

    /// <summary>Short hash of the picture (same picture = same hash): evidence of determinism.</summary>
    public static string Hash(VideoFrame frame)
    {
        var bytes = new byte[frame.Pixels.Length * 4];
        Buffer.BlockCopy(frame.Pixels, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
    }

    public static PictureStats Stats(VideoFrame frame)
    {
        var counts = new Dictionary<int, int>();
        foreach (int p in frame.Pixels)
        {
            counts.TryGetValue(p, out int n);
            counts[p] = n + 1;
        }
        int max = counts.Count == 0 ? 0 : counts.Values.Max();
        return new PictureStats(counts.Count, frame.Pixels.Length == 0 ? 1 : (double)max / frame.Pixels.Length);
    }

    /// <summary>Where the game's own picture is inside its frame (without the Spectrum or C64 border).</summary>
    public static (int x, int y, int w, int h) PictureArea(IMachine m) => m switch
    {
        Spectrum48 => (Spectrum48.BorderSide, Spectrum48.BorderTop, 256, 192),
        Retro.Systems.Arcade.SpaceInvaders => (0, 0, Retro.Systems.Arcade.SpaceInvaders.ScreenWidth, Retro.Systems.Arcade.SpaceInvaders.ScreenHeight),
        // 25-row display: VIC X 24 (32 border pixels in) and raster line 51 (36 lines down)
        C64 => (32, 51 - Vic.FirstVisibleLine, 320, 200),
        _ => (0, 0, m.Video.Width, m.Video.Height),
    };

    /// <summary>
    /// A rough drawing of the picture in characters, for an AI that only reads text: the
    /// most common colour is the background and every character tells how much of its
    /// cell is covered by something else (" .:+#" from empty to full).
    /// </summary>
    public static string Sketch(IMachine m, int cols = 64, int rows = 24)
    {
        var (x0, y0, w, h) = PictureArea(m);
        var px = m.Video.Pixels;
        int width = m.Video.Width;
        var counts = new Dictionary<int, int>();
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                counts.TryGetValue(px[y * width + x], out int c);
                counts[px[y * width + x]] = c + 1;
            }
        int background = counts.Count == 0 ? 0 : counts.MaxBy(kv => kv.Value).Key;
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            var line = new StringBuilder();
            int ya = y0 + r * h / rows, yb = y0 + (r + 1) * h / rows;
            for (int c = 0; c < cols; c++)
            {
                int xa = x0 + c * w / cols, xb = x0 + (c + 1) * w / cols, set = 0, all = 0;
                for (int y = ya; y < yb; y++)
                    for (int x = xa; x < xb; x++, all++)
                        if (px[y * width + x] != background)
                            set++;
                double share = all == 0 ? 0 : (double)set / all;
                line.Append(share == 0 ? ' ' : share < 0.15 ? '.' : share < 0.35 ? ':' : share < 0.65 ? '+' : '#');
            }
            sb.AppendLine(line.ToString().TrimEnd());
        }
        return sb.ToString();
    }

    /// <summary>Why a picture shows that the program is not running, or null (machine prompts and error reports).</summary>
    public static string? FailureSign(IMachine m, string? text)
    {
        if (text == null)
            return null;
        string flat = text.Replace('@', ' ');
        if (m is C64 && flat.Contains("READY.") && !flat.Contains("PRESS"))
            return "la pantalla muestra READY. del BASIC: el programa terminó o no arrancó";
        if (m is Spectrum48)
        {
            if (flat.Contains("1982 Sinclair Research Ltd"))
                return "quedó en la pantalla de inicio del Spectrum";
            var lines = flat.Split('\n');
            string last = lines.Length >= 2 ? lines[^2].Trim() : "";
            if (System.Text.RegularExpressions.Regex.IsMatch(last, @"^[0-9A-R] [A-Za-z].*, \d+:\d+$"))
                return $"el BASIC muestra un mensaje de error: \"{last}\"";
        }
        return null;
    }
}
