using Retro.Core;
using Retro.Fusion;
using Retro.Machines;

namespace Retro.Compat;

public sealed record OrganizedRom(string From, string To, string Machine);
public sealed record OrganizationResult(IReadOnlyList<OrganizedRom> Moved, int Unclassified, int Deferred, string? Error = null);

/// <summary>Moves only loose, confidently identified media into system folders, without overwriting anything.</summary>
public static class RomOrganizer
{
    private const long MaxFileBytes = 256L * 1024 * 1024;
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".json", ".png", ".jpg", ".jpeg", ".gif", ".pdf", ".nfo", ".url", ".ini", ".tmp", ".part", ".crdownload" };

    public static OrganizationResult Organize(string folder, TimeSpan? settleTime = null, Func<string, bool>? inUse = null)
    {
        var moved = new List<OrganizedRom>();
        int unknown = 0, deferred = 0;
        try
        {
            string root = Path.GetFullPath(folder);
            if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
                return new(moved, 0, 0);
            var paths = Directory.GetFiles(root).Where(p => !Skip.Contains(Path.GetExtension(p)) && !CompatDiscovery.IsExcluded(p)).ToList();
            if (paths.Count == 0) return new(moved, 0, 0);
            // Other instances use the same lock; no two processes update identities or rename games at once.
            string metadata = Path.Combine(root, RomIdentities.MetadataFolder);
            EnsureChild(root, metadata);
            Directory.CreateDirectory(metadata);
            string lockPath = Path.Combine(metadata, "organizar.lock");
            EnsureChild(root, lockPath);
            using var operation = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var identities = RomIdentities.Load(root);
            foreach (string source in paths)
            {
                try
                {
                    var info = new FileInfo(source);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) { unknown++; continue; }
                    if (inUse?.Invoke(source) == true) { deferred++; continue; }
                    if (info.Length == 0 || info.Length > MaxFileBytes || DateTime.UtcNow - info.LastWriteTimeUtc < (settleTime ?? TimeSpan.FromSeconds(3)))
                    { deferred++; continue; }
                    // Allows our rename, but conflicts with an existing writer or reader that does not allow deletion.
                    using var held = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Delete);
                    var bytes = new byte[checked((int)held.Length)];
                    held.ReadExactly(bytes);
                    var files = MediaArchive.Open(bytes, info.Name, source);
                    string? machine = Identify(files);
                    if (machine == null) { unknown++; continue; }
                    string targetFolder = Path.Combine(root, FolderName(machine));
                    EnsureChild(root, targetFolder);
                    Directory.CreateDirectory(targetFolder);
                    string destination = Destination(targetFolder, info.Name, source);
                    EnsureChild(root, destination);
                    if (Path.GetFileName(destination) != info.Name)
                    {
                        var (basicMachine, media, _) = GamePreparer.Choose(files);
                        if (basicMachine != null && media != null && ProfileLibrary.ForFiles(files) == null)
                        {
                            var original = GameCatalog.BasicProfile(source, files, basicMachine, media);
                            identities.Apply(root, source, original);
                            // Persist before moving: an interrupted move must never orphan a stored fusion's profile id.
                            identities.Preserve(root, destination, original);
                        }
                    }
                    if (inUse?.Invoke(source) == true) { deferred++; continue; }
                    File.Move(source, destination, overwrite: false);
                    moved.Add(new(source, destination, machine));
                    MovePictures(source, destination);
                }
                catch (Exception e) when (e is InvalidDataException or ArgumentException or OverflowException) { unknown++; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { deferred++; }
            }
            return new(moved, unknown, deferred);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { return new(moved, unknown, deferred, "No se pudo ordenar la carpeta: " + e.Message); }
    }

    public static string FolderName(string machine) => machine switch
    {
        "zx48" or "zx128" or "tk90x" => "ZX Spectrum",
        "invaders" => "Arcade - Space Invaders",
        "c64" => "Commodore 64", "nes" => "Nintendo NES", "pcxt" => "PC - DOS",
        "msx" => "MSX", "cpc" => "Amstrad CPC", "pc88" => "NEC PC-8801",
        "atari800" => "Atari 8 bits", "apple2" => "Apple II", "vic20" => "Commodore VIC-20",
        "plus4" => "Commodore Plus-4", "atarist" => "Atari ST", "amiga500" => "Amiga 500",
        _ => throw new ArgumentException("Sistema sin carpeta de ROMs: " + machine),
    };

    private static string? Identify(IReadOnlyList<MediaFile> files)
    {
        if (files.Count == 0) return null;
        if (MachineCatalog.IdentifySet(files) is { Confidence: 100 } && files.Count == 4 &&
            files.All(f => new[] { "invaders.h", "invaders.g", "invaders.f", "invaders.e" }.Contains(f.Name.ToLowerInvariant()) && f.Data.Length == 2048))
            return "invaders";
        string? system = null;
        foreach (var file in files)
        {
            if (!CompleteHeader(file)) return null;
            var candidates = MachineCatalog.Identify(file).Where(c => c.Confidence >= 80)
                .Select(c => c.Machine.Id).Distinct().ToArray();
            if (candidates.Length != 1 || system != null && system != candidates[0]) return null;
            system = candidates[0];
        }
        return system;
    }

    // Some existing probes intentionally accept partial media for manual diagnosis; automatic moves are stricter.
    private static bool CompleteHeader(MediaFile file)
    {
        var data = file.Data;
        if (data.AsSpan().StartsWith("NES\x1A"u8))
        {
            if (data.Length < 16 || data[4] == 0 || (data[7] & 12) == 8 && data[9] != 0) return false;
            int required = 16 + ((data[6] & 4) != 0 ? 512 : 0) + data[4] * 16384 + data[5] * 8192;
            return data.Length >= required;
        }
        if (file.Extension == ".z80")
        {
            if (data.Length < 30 || (data[29] & 3) == 3) return false;
            if ((data[6] | data[7]) != 0)
                return (data[12] & 32) == 0 ? data.Length >= 30 + 49152 : ExpandedSize(data.AsSpan(30), 49152);
            if (data.Length < 35) return false;
            int extra = data[30] | data[31] << 8;
            if (extra is not (23 or 54 or 55) || data.Length <= 32 + extra) return false;
            // Other Z80 hardware variants need a richer probe; do not label them as 48K.
            if (extra == 23 ? data[34] is not (0 or 1) : data[34] is not (0 or 1 or 3)) return false;
            int position = 32 + extra, pages = 0;
            while (position + 3 <= data.Length)
            {
                int size = data[position] | data[position + 1] << 8;
                int bytes = size == 65535 ? 16384 : size;
                position += 3;
                if (position + bytes > data.Length || bytes == 0 || size != 65535 && !ExpandedSize(data.AsSpan(position, bytes), 16384)) return false;
                position += bytes; pages++;
            }
            return position == data.Length && pages >= 3;
        }
        return true;
    }

    private static bool ExpandedSize(ReadOnlySpan<byte> data, int size)
    {
        int expanded = 0;
        for (int i = 0; i < data.Length && expanded < size; i++)
        {
            if (i + 3 < data.Length && data[i] == 0xED && data[i + 1] == 0xED)
            { expanded += data[i + 2]; i += 3; }
            else expanded++;
        }
        return expanded == size;
    }

    private static void EnsureChild(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new IOException("La carpeta de destino queda fuera de ROMs.");
        string directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("La carpeta de destino es un enlace.");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("El destino es un enlace.");
    }

    private static string Destination(string folder, string name, string source)
    {
        var used = Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string candidate = name;
        for (int i = 2; used.Contains(candidate) || Pictures(source).Any(p => used.Contains(Path.ChangeExtension(candidate, Path.GetExtension(p)))); i++)
            candidate = Path.GetFileNameWithoutExtension(name) + $" ({i})" + Path.GetExtension(name);
        return Path.Combine(folder, candidate);
    }

    private static IEnumerable<string> Pictures(string source) => Directory.EnumerateFiles(Path.GetDirectoryName(source)!)
        .Where(p => Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileNameWithoutExtension(source), StringComparison.OrdinalIgnoreCase) &&
            new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));

    private static void MovePictures(string source, string destination)
    {
        foreach (string picture in Pictures(source))
        {
            try
            {
                if (File.GetAttributes(picture).HasFlag(FileAttributes.ReparsePoint)) continue;
                using var held = new FileStream(picture, FileMode.Open, FileAccess.Read, FileShare.Delete);
                File.Move(picture, Path.ChangeExtension(destination, Path.GetExtension(picture)), overwrite: false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Keep an in-use picture where it was. */ }
        }
    }
}
