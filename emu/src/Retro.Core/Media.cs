using System.IO.Compression;

namespace Retro.Core;

/// <summary>A file the user wants to run: name and contents (possibly taken from a zip).</summary>
public sealed record MediaFile(string Name, byte[] Data, string Source)
{
    public string Extension => Path.GetExtension(Name).ToLowerInvariant();
}

/// <summary>Opens game files, including .zip archives with nested zips (TOSEC style).</summary>
public static class MediaArchive
{
    private static readonly HashSet<string> Junk = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".nfo", ".diz", ".url", ".htm", ".html", ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".ini",
    };

    public static List<MediaFile> Open(string path, bool keepAuxiliary = false)
    {
        var data = File.ReadAllBytes(path);
        return Open(data, Path.GetFileName(path), path, keepAuxiliary);
    }

    /// <summary>Reads bytes already held under a file lock, without reopening the source.</summary>
    public static List<MediaFile> Open(byte[] data, string name, string source, bool keepAuxiliary = false)
    {
        var result = new List<MediaFile>();
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            long expanded = 0;
            ExtractZip(data, source, result, 0, ref expanded, keepAuxiliary);
        }
        else
            result.Add(new MediaFile(name, data, source));
        return result;
    }

    private static void ExtractZip(byte[] zipData, string source, List<MediaFile> output, int depth, ref long expanded, bool keepAuxiliary)
    {
        using var zip = new ZipArchive(new MemoryStream(zipData), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || (entry.ExternalAttributes & 0x10) != 0 || (!keepAuxiliary && entry.Length == 0))
                continue;
            string ext = Path.GetExtension(entry.Name);
            if (!keepAuxiliary && Junk.Contains(ext))
                continue;
            expanded += entry.Length;
            if (expanded > 256L * 1024 * 1024)
                throw new InvalidDataException("El zip es demasiado grande.");
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();
            if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) && depth < 4)
                ExtractZip(bytes, source + "!" + entry.FullName, output, depth + 1, ref expanded, keepAuxiliary);
            else
                output.Add(new MediaFile(entry.Name, bytes, source + "!" + entry.FullName));
        }
    }

    /// <summary>
    /// TOSEC tags: [a] alternate, [h] hack, [t] trainer, [cr] crack, [b] bad dump.
    /// Lower score = cleaner dump.
    /// </summary>
    public static int DumpScore(string name)
    {
        int score = 0;
        string n = name.ToLowerInvariant();
        if (n.Contains("[b")) score += 100;
        if (n.Contains("[h")) score += 40;
        if (n.Contains("[t")) score += 30;
        if (n.Contains("[cr")) score += 20;
        if (n.Contains("[a")) score += 10;
        if (n.Contains("[k-file]")) score += 5;
        return score;
    }
}
