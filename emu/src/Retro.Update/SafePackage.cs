using System.Formats.Tar;
using System.IO.Compression;

namespace Retro.Update;

public static class SafePackage
{
    public const long MaxExpanded = 512L * 1024 * 1024;

    /// <summary>Extracts into a private, new staging folder, never over an installed app.</summary>
    public static string Extract(string archive, string staging, ReleaseAsset asset, CancellationToken ct)
    {
        PackageManifest.NoLinks(staging);
        if (Directory.Exists(staging)) throw new IOException("La carpeta temporal ya existe.");
        Directory.CreateDirectory(staging);
        string folder = $"RetroEmu-{asset.Version.ToString(3)}-{asset.Runtime}";
        string root = Path.Combine(staging, folder);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0; int entries = 0;
        string? Destination(string name, bool directory, long size)
        {
            ct.ThrowIfCancellationRequested();
            if (++entries > 4096 || size < 0 || size > MaxExpanded || (expanded += size) > MaxExpanded)
                throw new InvalidDataException("El paquete supera los límites de extracción.");
            if (name == folder || name == folder + "/") return directory ? null : throw new InvalidDataException("Raíz de paquete inválida.");
            if (!name.StartsWith(folder + "/", StringComparison.Ordinal)) throw new InvalidDataException("El paquete contiene otra carpeta raíz.");
            string relative = name[(folder.Length + 1)..].TrimEnd('/');
            string path = PackageManifest.Within(root, relative);
            if (!names.Add(relative)) throw new InvalidDataException("Ruta repetida en el paquete.");
            if (directory) { Directory.CreateDirectory(path); return null; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }
        if (asset.Runtime == "win-x64")
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                int kind = (entry.ExternalAttributes >> 16) & 0xF000;
                bool directory = entry.FullName.EndsWith('/');
                if (kind != 0 && kind != 0x8000 && kind != 0x4000) throw new InvalidDataException("No se admiten enlaces ni archivos especiales.");
                string? path = Destination(entry.FullName, directory, entry.Length);
                if (path == null) continue;
                using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew);
                Copy(input, output, entry.Length, ct);
                File.SetLastWriteTimeUtc(path, entry.LastWriteTime.UtcDateTime);
            }
        }
        else if (asset.Runtime == "linux-x64")
        {
            using var file = File.OpenRead(archive); using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var limited = new LimitedStream(gzip, MaxExpanded);
            using var tar = new TarReader(limited);
            TarEntry? entry;
            while ((entry = tar.GetNextEntry()) != null)
            {
                if (entry.EntryType == TarEntryType.GlobalExtendedAttributes) continue;
                bool directory = entry.EntryType == TarEntryType.Directory;
                if (!directory && entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new InvalidDataException("No se admiten enlaces ni archivos especiales.");
                string? path = Destination(entry.Name, directory, entry.Length);
                if (path == null) continue;
                using var output = new FileStream(path, FileMode.CreateNew);
                if (entry.DataStream != null) Copy(entry.DataStream, output, entry.Length, ct);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, entry.Mode & (UnixFileMode)0x1FF);
                File.SetLastWriteTimeUtc(path, entry.ModificationTime.UtcDateTime);
            }
        }
        else throw new InvalidDataException("Plataforma no admitida.");
        var manifest = PackageManifest.Read(root);
        if (manifest.Version != asset.Version.ToString(3) || manifest.Runtime != asset.Runtime) throw new InvalidDataException("El paquete pertenece a otra versión o sistema.");
        manifest.Verify(root, exact: true);
        return root;
    }

    private static void Copy(Stream input, Stream output, long expected, CancellationToken ct)
    {
        byte[] buffer = new byte[65536]; long total = 0; int count;
        while ((count = input.Read(buffer)) > 0)
        {
            ct.ThrowIfCancellationRequested(); total += count;
            if (total > expected) throw new InvalidDataException("Contenido mayor al tamaño declarado.");
            output.Write(buffer, 0, count);
        }
        if (total != expected) throw new InvalidDataException("Archivo incompleto en el paquete.");
    }

    private sealed class LimitedStream(Stream source, long limit) : Stream
    {
        private long _read;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) { int n = source.Read(buffer); _read += n; if (_read > limit) throw new InvalidDataException("Paquete expandido demasiado grande."); return n; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
