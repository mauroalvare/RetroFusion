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
