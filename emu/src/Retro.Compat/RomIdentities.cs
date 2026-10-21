using System.Text.Json;
using Retro.Fusion;

namespace Retro.Compat;

/// <summary>Keeps a basic profile's identity when a name collision requires renaming its file.</summary>
internal sealed class RomIdentities
{
    internal const string MetadataFolder = ".retroemu";
    private const string FileName = "identidades.json";
    private sealed record Identity(string Sha256, string Machine, string Id, string Name);
    private readonly Dictionary<string, Identity> _entries = new(StringComparer.OrdinalIgnoreCase);

    internal static RomIdentities Load(string root)
    {
        var result = new RomIdentities();
        string folder = Path.Combine(root, MetadataFolder), path = Path.Combine(folder, FileName);
        if (Directory.Exists(folder) && File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("La carpeta de organización es un enlace.");
        if (!File.Exists(path)) return result;
        if (new FileInfo(path).Length > 1024 * 1024 || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("No se pueden leer las identidades de los juegos.");
        var entries = JsonSerializer.Deserialize<Dictionary<string, Identity>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Las identidades de los juegos están vacías.");
        foreach (var (key, value) in entries)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.Id) || value.Id.Length > 512 ||
                value.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) ||
                string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 512 || value.Sha256?.Length != 64 ||
                value.Sha256.Any(c => !char.IsAsciiHexDigit(c)))
                throw new InvalidDataException("Las identidades de los juegos están incompletas.");
            if (!result._entries.TryAdd(key, value)) throw new InvalidDataException("Hay identidades repetidas.");
        }
        return result;
    }

    internal bool Apply(string root, string path, GameProfile basic)
    {
        string key = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (_entries.TryGetValue(key, out var saved) && saved.Machine == basic.Machine && basic.Match.Sha256.Contains(saved.Sha256))
        {
            basic.Id = saved.Id;
            basic.Name = saved.Name;
            return true;
        }
        return false;
    }

    internal static void ApplyForFile(string path, GameProfile profile)
    {
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); folder != null; folder = folder.Parent)
        {
            if (!File.Exists(Path.Combine(folder.FullName, MetadataFolder, FileName))) continue;
            if (Load(folder.FullName).Apply(folder.FullName, path, profile)) return;
        }
    }

    internal void Preserve(string root, string destination, GameProfile original)
    {
        string key = Path.GetRelativePath(root, destination).Replace('\\', '/');
        _entries[key] = new(original.Match.Sha256.Single(), original.Machine, original.Id, original.Name);
        string folder = Path.Combine(root, MetadataFolder);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, FileName), temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, _entries, new JsonSerializerOptions { WriteIndented = true });
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
