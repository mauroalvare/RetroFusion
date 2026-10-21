using System.Buffers.Binary;

namespace Retro.App;

/// <summary>Local image lookup and size checks before a native decoder allocates pixels.</summary>
internal static class ThumbnailFiles
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public static string? Beside(string media)
    {
        try
        {
            string? directory = Path.GetDirectoryName(media);
            if (directory == null || !Directory.Exists(directory)) return null;
            return Directory.EnumerateFiles(directory).Where(p =>
                Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileNameWithoutExtension(media), StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(p).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                .OrderBy(p => Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static bool Dimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length > MaxBytes) return false;
        if (data.Length >= 33 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
        }
        else if (data.Length >= 4 && data[0] == 255 && data[1] == 216)
        {
            int position = 2;
            while (position < data.Length)
            {
                if (data[position++] != 255) return false;
                while (position < data.Length && data[position] == 255) position++;
                if (position >= data.Length) return false;
                byte marker = data[position++];
                if (marker is 0xDA or 0xD9 || position + 2 > data.Length) return false;
                int length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position, 2));
                if (length < 2 || position + length > data.Length) return false;
                if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
                {
                    if (length < 8) return false;
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 3, 2));
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 5, 2));
                    break;
                }
                position += length;
            }
        }
        return width is > 0 and <= 4096 && height is > 0 and <= 4096 && (long)width * height <= 16 * 1024 * 1024;
    }
}
