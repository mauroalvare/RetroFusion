using Retro.Core;

namespace Retro.Fusion;

/// <summary>Combines the pictures of the games of a fusion into one frame.</summary>
public static class Compositor
{
    private const int Black = unchecked((int)0xFF000000);

    public static (int width, int height) OutputSize(string mode, IReadOnlyList<FusionSlot> slots, string baseSlot)
    {
        if (mode == "side")
        {
            int h = slots.Max(s => s.Machine.Video.Height);
            int w = slots.Sum(s => s.Machine.Video.Width * h / s.Machine.Video.Height);
            return (w, h);
        }
        var b = slots.First(s => s.Id == baseSlot).Machine.Video;
        return (b.Width, b.Height);
    }

    public static void Compose(string mode, IReadOnlyList<FusionSlot> slots, string baseSlot, double opacity, VideoFrame output)
    {
        switch (mode)
        {
            case "side":
            {
                int x = 0;
                foreach (var s in slots)
                {
                    var v = s.Machine.Video;
                    int w = v.Width * output.Height / v.Height;
                    Blit(v, output, x, 0, w, output.Height, 1.0, opaque: true);
                    x += w;
                }
                break;
            }
            case "pip":
            {
                var b = slots.First(s => s.Id == baseSlot);
                Blit(b.Machine.Video, output, 0, 0, output.Width, output.Height, 1.0, opaque: true);
                int y = 8;
                foreach (var s in slots.Where(s => s.Id != baseSlot))
                {
                    var v = s.Machine.Video;
                    int h = output.Height / 3;
                    int w = v.Width * h / v.Height;
                    Blit(v, output, output.Width - w - 8, y, w, h, 1.0, opaque: true);
                    y += h + 8;
                }
                break;
            }
            default: // overlay
            {
                var b = slots.First(s => s.Id == baseSlot);
                Blit(b.Machine.Video, output, 0, 0, output.Width, output.Height, 1.0, opaque: true);
                foreach (var s in slots.Where(s => s.Id != baseSlot))
                {
                    // keep the aspect ratio, centred
                    var v = s.Machine.Video;
                    double scale = Math.Min((double)output.Width / v.Width, (double)output.Height / v.Height);
                    int w = (int)(v.Width * scale), h = (int)(v.Height * scale);
                    Blit(v, output, (output.Width - w) / 2, (output.Height - h) / 2, w, h, opacity, opaque: false);
                }
                break;
            }
        }
    }

    /// <summary>Nearest-neighbour scale of <paramref name="src"/> into a rectangle of <paramref name="dst"/>.
    /// When not opaque, black pixels are transparent and the rest are blended.</summary>
    private static void Blit(VideoFrame src, VideoFrame dst, int dx, int dy, int dw, int dh, double alpha, bool opaque)
    {
        int a = (int)(alpha * 256);
        for (int y = 0; y < dh; y++)
        {
            int ty = dy + y;
            if (ty < 0 || ty >= dst.Height)
                continue;
            int sy = y * src.Height / dh;
            int srow = sy * src.Width, drow = ty * dst.Width;
            for (int x = 0; x < dw; x++)
            {
                int tx = dx + x;
                if (tx < 0 || tx >= dst.Width)
                    continue;
                int c = src.Pixels[srow + x * src.Width / dw];
                if (opaque)
                {
                    dst.Pixels[drow + tx] = c;
                    continue;
                }
                if ((c & 0xFFFFFF) == 0)
                    continue;
                int d = dst.Pixels[drow + tx];
                int r = (((c >> 16) & 0xFF) * a + ((d >> 16) & 0xFF) * (256 - a)) >> 8;
                int g = (((c >> 8) & 0xFF) * a + ((d >> 8) & 0xFF) * (256 - a)) >> 8;
                int bl = ((c & 0xFF) * a + (d & 0xFF) * (256 - a)) >> 8;
                dst.Pixels[drow + tx] = Black | (r << 16) | (g << 8) | bl;
            }
        }
    }
}
