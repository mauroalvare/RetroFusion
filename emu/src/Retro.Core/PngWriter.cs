using System.IO.Compression;

namespace Retro.Core;

/// <summary>Minimal PNG encoder (RGB, 8 bits) for screenshots, without dependencies.</summary>
public static class PngWriter
{
    public static void Write(string path, VideoFrame frame, int scale = 1)
    {
        int w = frame.Width * scale, h = frame.Height * scale;
        var raw = new byte[h * (1 + w * 3)];
        int p = 0;
        for (int y = 0; y < h; y++)
        {
            raw[p++] = 0;
            int srcRow = (y / scale) * frame.Width;
            for (int x = 0; x < w; x++)
            {
                int c = frame.Pixels[srcRow + x / scale];
                raw[p++] = (byte)(c >> 16);
                raw[p++] = (byte)(c >> 8);
                raw[p++] = (byte)c;
            }
        }
        using var fs = File.Create(path);
        fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)w);
        WriteBE(ihdr, 4, (uint)h);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // RGB
        Chunk(fs, "IHDR", ihdr);
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true))
                z.Write(raw);
            Chunk(fs, "IDAT", ms.ToArray());
        }
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Retro.Core.Crc32.Update(0xFFFFFFFF, typeBytes);
        crc = Retro.Core.Crc32.Update(crc, data) ^ 0xFFFFFFFF;
        var crcBytes = new byte[4];
        WriteBE(crcBytes, 0, crc);
        s.Write(crcBytes);
    }

    private static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }
}
