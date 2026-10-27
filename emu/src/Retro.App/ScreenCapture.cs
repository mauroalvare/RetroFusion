using System.Buffers.Binary;
using System.IO.Compression;
using Retro.Core;

namespace Retro.App;

/// <summary>
/// Saves the emulated screen as a PNG at its own resolution: a capture in the pictures folder,
/// or the library thumbnail beside the original (the name ThumbnailFiles looks for). Managed code,
/// so no native window is needed and a capture never replaces an existing file.
/// </summary>
public static class ScreenCapture
{
    public static string DefaultFolder
    {
        get
        {
            // Some Linux systems have no pictures folder: the captures then go to the home folder.
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return pictures.Length > 0 ? Path.Combine(pictures, "RetroEmu")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "RetroEmu-capturas");
        }
    }

    /// <summary>A new file "title-yyyyMMdd-HHmmss.png" (with -2, -3... if taken) in the folder.</summary>
    public static string Save(VideoFrame frame, string folder, string title, DateTime now)
    {
        Directory.CreateDirectory(folder);
        string stem = SafeName(title) + "-" + now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        for (int copy = 1; ; copy++)
        {
            string path = Path.Combine(folder, stem + (copy == 1 ? "" : "-" + copy) + ".png");
            if (TryCreate(path, frame)) return path;
        }
    }

    /// <summary>Thumbnail for the library beside the game file; null when an image is already there.</summary>
    public static string? SaveThumbnail(VideoFrame frame, string media)
    {
        if (ThumbnailFiles.Beside(media) != null) return null;
        string path = Path.ChangeExtension(media, ".png");
        return TryCreate(path, frame) ? path : null;
    }

    public static string SafeName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string name = new string(title.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (name.Length > 60) name = name[..60].TrimEnd();
        return name.Length == 0 ? "captura" : name;
    }

    // CreateNew: an existing file, or one created meanwhile, is never overwritten.
    private static bool TryCreate(string path, VideoFrame frame)
    {
        FileStream stream;
        try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write); }
        catch (IOException) when (File.Exists(path)) { return false; }
        try
        {
            using (stream) WritePng(stream, frame.Width, frame.Height, frame.Pixels);
        }
        catch
        {
            // A full disk must not leave half an image that the library would try to show.
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return true;
    }

    /// <summary>8-bit RGB PNG from 0xAARRGGBB pixels (the emulated screens are opaque).</summary>
    public static void WritePng(Stream output, int width, int height, ReadOnlySpan<int> argb)
    {
        if (width <= 0 || height <= 0 || argb.Length != width * height) throw new ArgumentException("La imagen no coincide con su tamaño.");
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; header[9] = 2;   // 8 bits per channel, truecolour; deflate, adaptive filter, no interlace
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + width * 3];   // filter byte 0: rows are stored as they are
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixel = argb[y * width + x];
                    row[1 + x * 3] = (byte)(pixel >> 16); row[2 + x * 3] = (byte)(pixel >> 8); row[3 + x * 3] = (byte)pixel;
                }
                zlib.Write(row);
            }
        }
        Chunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        Chunk(output, "IEND", []);
    }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        Span<byte> name = stackalloc byte[4];
        for (int i = 0; i < 4; i++) name[i] = (byte)type[i];
        output.Write(name); output.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, name), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
