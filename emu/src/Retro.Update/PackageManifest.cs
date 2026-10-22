using System.Security.Cryptography;
using System.Text.Json;

namespace Retro.Update;

public sealed class PackageManifest
{
    public const string FileName = "actualizacion.json";
    public int Format { get; set; } = 1;
    public string Version { get; set; } = "";
    public string Runtime { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public string Executable => Runtime == "win-x64" ? "RetroEmu.exe" : "RetroEmu";
    public static readonly string[] ProtectedFolders = ["ROMs", "Juegos fusionados", "Juegos creados", "firmware"];
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static StringComparer Paths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static bool Protected(string path) => ProtectedFolders.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

    public static string Within(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 240 || relative.Contains('\\') || relative.Contains(':')
            || relative.Any(char.IsControl) || relative.StartsWith('/') || relative.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ')
                || System.Text.RegularExpressions.Regex.IsMatch(p.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Ruta inválida en el paquete.");
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Archivo fuera del paquete.");
        return full;
    }

    public static void NoLinks(string root)
    {
        for (var d = new DirectoryInfo(root); d != null; d = d.Parent)
            if (d.Exists && d.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("La actualización no admite carpetas enlazadas.");
    }

    public static PackageManifest Read(string root)
    {
        NoLinks(root);
        string path = Path.Combine(root, FileName);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 2 * 1024 * 1024 || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Falta el registro de archivos de esta instalación.");
        var manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("Registro vacío.");
        _ = ReleaseClient.ParseVersion(manifest.Version);
        if (manifest.Format != 1 || manifest.Runtime is not ("win-x64" or "linux-x64") || manifest.Files is not { Count: > 0 and <= 4096 })
            throw new InvalidDataException("Registro de actualización inválido.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, sha) in manifest.Files)
        {
            _ = Within(root, name);
            if (!names.Add(name) || name.Equals(FileName, StringComparison.OrdinalIgnoreCase) || sha.Length != 64 || sha.Any(c => !char.IsAsciiHexDigit(c)))
                throw new InvalidDataException("Registro de archivos inválido.");
        }
        foreach (var required in new[] { manifest.Executable, "RetroEmu.dll", "RetroEmu.runtimeconfig.json", "RetroEmu.deps.json" })
            if (!manifest.Files.ContainsKey(required)) throw new InvalidDataException("El paquete no tiene el programa completo.");
        return manifest;
    }

    // Both callers use an updater-owned folder, never the installed library.
    internal static void CopyPackage(string source, string target)
    {
        var manifest = Read(source); manifest.Verify(source, exact: true); NoLinks(target);
        var names = new HashSet<string>(manifest.Files.Keys, Paths) { FileName };
        if (Directory.Exists(target))
            foreach (string path in Directory.EnumerateFileSystemEntries(target, "*", SearchOption.AllDirectories))
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Hay enlaces en la preparación.");
                if (File.Exists(path) && !names.Contains(Path.GetRelativePath(target, path).Replace('\\', '/')))
                    throw new IOException("La preparación contiene un archivo ajeno.");
            }
        Directory.CreateDirectory(target);
        foreach (string relative in names.Where(n => n != FileName).Append(FileName))
        {
            string from = Within(source, relative), to = Within(target, relative);
            NoLinks(Path.GetDirectoryName(to)!); Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(to, File.GetUnixFileMode(from));
            File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
        }
        manifest.Verify(target, exact: true);
    }

    public void Verify(string root, bool exact)
    {
        NoLinks(root);
        foreach (var (name, sha) in Files)
        {
            if (!exact && Protected(name)) continue;
            string path = Within(root, name); NoLinks(Path.GetDirectoryName(path)!);
            var file = new FileInfo(path);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Falta un archivo del programa: " + name);
            using var input = file.OpenRead();
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archivo de programa alterado: " + name);
        }
        if (!exact) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("El paquete contiene enlaces.");
            if (Directory.Exists(path)) continue;
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative != FileName && !Files.ContainsKey(relative)) throw new InvalidDataException("Archivo sin registrar en el paquete.");
        }
    }
}
