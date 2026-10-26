using System.Buffers.Binary;
using System.Text;
using Retro.Core;

namespace Retro.Machines;

/// <summary>Builds a private, deterministic FAT12 boot disk. User files are only read.</summary>
public static class PcDos
{
    public static string RuntimeDirectory => Path.Combine(AppContext.BaseDirectory, "system", "pcxt");
    public static int ProgramPriority(string name) => Path.GetFileNameWithoutExtension(name).ToUpperInvariant()
        is "SETUP" or "INSTALL" or "CONFIG" or "CONFIGUR" or "SOUND" or "HELP" ? 10 : 0;

    public static byte[] Prepare(IReadOnlyList<MediaFile> files, MediaFile program)
    {
        if (program.Extension is not (".exe" or ".com") || program.Data.Length == 0)
            throw new InvalidDataException("Elegí un programa DOS EXE o COM.");
        var input = Gather(files, program);
        var root = new Node("", null, null);
        byte[] kernel = ReadRuntime("kernel.sys");
        root.Add("KERNEL.SYS", kernel); root.Add("COMMAND.COM", ReadRuntime("command.com"));
        root.Add("FDCONFIG.SYS", Encoding.ASCII.GetBytes("SHELL=\\COMMAND.COM /E:512 /P\r\nFILES=30\r\nBUFFERS=10\r\nNUMLOCK=OFF\r\n"));
        // A native DOS program marks boot ready, then waits two BIOS ticks before the game starts.
        root.Add("RETRO.COM", Convert.FromHexString("B850008EC026C7060000524626C7060200444FB840008EC0268B1E6C00B90200263B1E6C0074F9268B1E6C00E2F2B8004CCD21"));
        var game = root.Add("GAME", null);
        string? executable = null;
        int aliases = 0;
        var entries = input.Select(file => (File: file, Path: DisplayPath(file))).OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var reserved = entries.SelectMany(entry => entry.Path.Replace('\\', '/').Split('/'))
            .Where(part => IsShortName(part)).Select(part => part.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string selected = DisplayPath(program);
        string Alias(string prefix, string extension = "")
        {
            string name;
            do { name = prefix + (++aliases).ToString("D4") + extension; } while (!reserved.Add(name));
            return name;
        }
        var originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var file = entry.File;
            string path = entry.Path.Replace('\\', '/');
            if (!originals.Add(path)) throw new InvalidDataException("Dos archivos del juego tienen el mismo nombre DOS: " + path);
            bool isProgram = path.Equals(selected, StringComparison.OrdinalIgnoreCase);
            string[] parts = path.Replace('\\', '/').Split('/');
            if (parts.Any(p => p is "" or "." or ".." || p.Contains(':')))
                throw new InvalidDataException("El ZIP contiene una ruta DOS inválida.");
            // Empty launcher sidecars with non-DOS names cannot be native game data.
            if (!isProgram && file.Data.Length == 0 && !IsShortName(parts[^1])) continue;
            var folder = game;
            foreach (string part in parts[..^1])
            {
                var existing = folder.Children.FirstOrDefault(n => n.Original.Equals(part, StringComparison.OrdinalIgnoreCase) && n.Data == null);
                if (existing != null) { folder = existing; continue; }
                string alias = IsShortName(part) ? part.ToUpperInvariant() : Alias("DIR");
                folder = folder.Add(alias, null, part);
            }
            string name = parts[^1].ToUpperInvariant();
            if (!IsShortName(name))
            {
                if (isProgram) name = Alias("RUN", program.Extension.ToUpperInvariant());
                else if (Path.GetExtension(name).ToLowerInvariant() is ".txt" or ".md" or ".nfo" or ".diz" or ".jpg" or ".png" or ".pdf") continue;
                else throw new InvalidDataException("Un archivo del juego no usa nombres DOS de 8.3 caracteres: " + parts[^1]);
            }
            string destination = folder.FullPath + "\\" + name;
            if (!paths.Add(destination)) throw new InvalidDataException("Dos archivos del juego tienen el mismo nombre DOS: " + name);
            folder.Add(name, file.Data);
            if (isProgram) executable = destination;
        }
        if (executable == null) throw new InvalidDataException("No se encontró el programa elegido dentro de sus archivos.");
        int last = executable.LastIndexOf('\\');
        string commands = "@ECHO OFF\r\nRETRO.COM\r\nCD " + executable[..last] + "\r\n" + executable[(last + 1)..] + "\r\n";
        root.Add("AUTOEXEC.BAT", Encoding.ASCII.GetBytes(commands));
        return Build(root, kernel.Length);
    }

    public static bool IsBootLoading(Retro.Systems.Pc.PcXt pc) => Marker(pc, "BOOT");
    public static bool IsBootReady(Retro.Systems.Pc.PcXt pc) => Marker(pc, "RFDO");
    private static bool Marker(Retro.Systems.Pc.PcXt pc, string value)
    {
        for (int i = 0; i < 4; i++) if (pc.Peek((uint)(0x500 + i)) != value[i]) return false;
        return true;
    }

    private static int ArchiveSeparator(MediaFile file)
    {
        int position = file.Source.LastIndexOf(".zip!", StringComparison.OrdinalIgnoreCase);
        return position < 0 || File.Exists(file.Source) ? -1 : position + 4;
    }

    /// <summary>Member path, including its directory; does not mistake a literal ! for an archive boundary.</summary>
    public static string DisplayPath(MediaFile file)
    {
        int separator = ArchiveSeparator(file);
        return (separator >= 0 ? file.Source[(separator + 1)..] : file.Name).Replace('\\', '/');
    }

    private static IReadOnlyList<MediaFile> Gather(IReadOnlyList<MediaFile> files, MediaFile program)
    {
        int separator = ArchiveSeparator(program);
        if (separator >= 0)
        {
            int outer = program.Source.IndexOf(".zip!", StringComparison.OrdinalIgnoreCase) + 4;
            string archive = program.Source[..outer];
            if (File.Exists(archive)) files = MediaArchive.Open(archive, keepAuxiliary: true);
            string parentArchive = program.Source[..(separator + 1)];
            return files.Where(f => f.Source.StartsWith(parentArchive, StringComparison.OrdinalIgnoreCase)
                && ArchiveSeparator(f) == separator).ToArray();
        }
        // A loose program may depend on data alongside it. Do not collect other game archives.
        if (files.Count > 1 || !File.Exists(program.Source)) return files;
        string folder = Path.GetDirectoryName(Path.GetFullPath(program.Source))!;
        var result = new List<MediaFile>();
        long total = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 8 };
        foreach (string path in Directory.EnumerateFiles(folder, "*", options))
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".log" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".pdf" or ".json" or ".md" or ".cs" or ".py" or ".zip" or ".7z" or ".rar" or ".rfstate" or ".dll" or ".nes" or ".tap" or ".tzx" or ".prg" or ".t64" or ".rom" or ".img" or ".ima") continue;
            var info = new FileInfo(path); total += info.Length;
            if (total > 720 * 1024 || result.Count >= 256) throw Capacity();
            result.Add(new MediaFile(Path.GetRelativePath(folder, path).Replace('\\', '/'), File.ReadAllBytes(path), path));
        }
        return result;
    }

    private static byte[] ReadRuntime(string name)
    {
        string path = Path.Combine(RuntimeDirectory, name);
        if (!File.Exists(path)) throw new FileNotFoundException("Falta la base FreeDOS incluida. Volvé a extraer el paquete completo del emulador.", path);
        return File.ReadAllBytes(path);
    }

    private static bool IsShortName(string name)
    {
        string[] parts = name.Split('.');
        return parts.Length <= 2 && parts[0].Length is >= 1 and <= 8
            && (parts.Length == 1 || parts[1].Length is >= 1 and <= 3)
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '~' or '$' or '.' or '!');
    }
    private static InvalidDataException Capacity() => new("El juego y sus archivos no entran en el disquete de 720 KB disponible. Probá una edición para PC/XT más pequeña o una imagen de arranque propia.");

    private sealed class Node(string name, byte[]? data, Node? parent, string? original = null)
    {
        public readonly string Name = name, Original = original ?? name;
        public readonly byte[]? Data = data;
        public readonly Node? Parent = parent;
        public readonly List<Node> Children = [];
        public int Cluster, Count;
        public string FullPath => Parent == null ? "" : Parent.FullPath + "\\" + Name;
        public Node Add(string name, byte[]? bytes, string? original = null)
        {
            if (Children.Any(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Hay archivos o carpetas con nombres DOS duplicados: " + name);
            var child = new Node(name, bytes, this, original); Children.Add(child); return child;
        }
        public IEnumerable<Node> Descendants()
        {
            foreach (var child in Children) { yield return child; foreach (var n in child.Descendants()) yield return n; }
        }
    }

    private static byte[] Build(Node root, int kernelLength)
    {
        const int sector = 512, clusterSize = 1024, fatSize = 1536, rootOffset = 7 * sector, dataOffset = 14 * sector;
        byte[] disk = new byte[720 * 1024], fat = new byte[fatSize];
        var nodes = root.Descendants().ToList();
        int next = 2;
        foreach (var node in nodes)
        {
            int size = node.Data?.Length ?? (node.Children.Count + 2) * 32;
            node.Count = (size + clusterSize - 1) / clusterSize;
            node.Cluster = node.Count == 0 ? 0 : next;
            next += node.Count;
        }
        if (dataOffset + (long)(next - 2) * clusterSize > disk.Length || root.Children.Count > 112) throw Capacity();
        void Fat(int cluster, int value)
        {
            int offset = cluster + cluster / 2;
            int old = BinaryPrimitives.ReadUInt16LittleEndian(fat.AsSpan(offset));
            BinaryPrimitives.WriteUInt16LittleEndian(fat.AsSpan(offset), (ushort)((cluster & 1) == 0 ? (old & 0xF000) | value : (old & 15) | value << 4));
        }
        fat[0] = 0xF9; fat[1] = fat[2] = 0xFF;
        void Entry(int offset, string name, bool directory, int cluster, int size)
        {
            string[] parts = name.Split('.');
            string encoded = name is "." or ".." ? name.PadRight(11) : parts[0].PadRight(8) + (parts.Length > 1 ? parts[1] : "").PadRight(3);
            Encoding.ASCII.GetBytes(encoded).CopyTo(disk, offset);
            disk[offset + 11] = (byte)(directory ? 0x10 : 0x20);
            BinaryPrimitives.WriteUInt16LittleEndian(disk.AsSpan(offset + 24), 0x0021); // 1980-01-01: deterministic disk identity.
            BinaryPrimitives.WriteUInt16LittleEndian(disk.AsSpan(offset + 26), (ushort)cluster);
            BinaryPrimitives.WriteInt32LittleEndian(disk.AsSpan(offset + 28), size);
        }
        void Directory(Node node, int offset, bool isRoot)
        {
            int entry = 0;
            if (!isRoot)
            {
                Entry(offset, ".", true, node.Cluster, 0); Entry(offset + 32, "..", true, node.Parent!.Cluster, 0); entry = 2;
            }
            foreach (var child in node.Children)
                Entry(offset + 32 * entry++, child.Name, child.Data == null, child.Cluster, child.Data?.Length ?? 0);
        }
        Directory(root, rootOffset, true);
        foreach (var node in nodes)
        {
            for (int n = 0; n < node.Count; n++) Fat(node.Cluster + n, n + 1 == node.Count ? 0xFFF : node.Cluster + n + 1);
            if (node.Cluster == 0) continue;
            int offset = dataOffset + (node.Cluster - 2) * clusterSize;
            if (node.Data != null) node.Data.CopyTo(disk, offset); else Directory(node, offset, false);
        }
        fat.CopyTo(disk, sector); fat.CopyTo(disk, 4 * sector);
        WriteBoot(disk, kernelLength);
        return disk;
    }

    // Own boot loader: KERNEL.SYS is allocated contiguously first. BIOS and DOS run normally.
    // No intercepted DOS calls or host execution of an EXE/COM.
    private static void WriteBoot(byte[] disk, int kernelLength)
    {
        disk[0] = 0xEB; disk[1] = 0x3C; disk[2] = 0x90;
        Encoding.ASCII.GetBytes("RETROEMU").CopyTo(disk, 3);
        void W(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(disk.AsSpan(offset), value);
        W(11, 512); disk[13] = 2; W(14, 1); disk[16] = 2; W(17, 112); W(19, 1440);
        disk[21] = 0xF9; W(22, 3); W(24, 9); W(26, 2); disk[38] = 0x29;
        Encoding.ASCII.GetBytes("RETROEMU   FAT12   ").CopyTo(disk, 43);
        var code = new List<byte>();
        void E(params byte[] bytes) => code.AddRange(bytes);
        // Relocate the loader and its stack above the kernel's load area.
        E(0xFA, 0x31, 0xC0, 0x8E, 0xD8, 0xBE, 0, 0x7C, 0xB8, 0, 0x90, 0x8E, 0xC0);
        E(0x31, 0xFF, 0xB9, 0, 1, 0xFC, 0xF3, 0xA5, 0xEA, 0, 0, 0, 0x90);
        int entry = 62 + code.Count; code[^4] = (byte)entry; code[^3] = (byte)(entry >> 8);
        E(0xB8, 0, 0x90, 0x8E, 0xD8, 0xB8, 0, 0x80, 0x8E, 0xD0, 0xBC, 0xF0, 0xFF, 0xFB, 0xFC);
        E(0x88, 0x16, 0x24, 0);
        E(0xB8, 0x50, 0, 0x8E, 0xC0, 0x26, 0xC7, 0x06, 0, 0, 0x42, 0x4F, 0x26, 0xC7, 0x06, 2, 0, 0x4F, 0x54);
        E(0xB8, 0x60, 0, 0x8E, 0xC0, 0x31, 0xDB);
        E(0xBF, 14, 0);
        int sectors = (kernelLength + 511) / 512;
        if (sectors is < 1 or > 127) throw new InvalidDataException("El núcleo DOS no corresponde al cargador de PC/XT.");
        E(0xBE, (byte)sectors, 0);
        int loop = code.Count;
        E(0x89, 0xF8, 0x31, 0xD2, 0xF7, 0x36, 0x18, 0, 0x88, 0xD1, 0xFE, 0xC1);
        E(0x31, 0xD2, 0xF7, 0x36, 0x1A, 0, 0x88, 0xC5, 0x88, 0xD6, 0x8A, 0x16, 0x24, 0);
        E(0xB8, 1, 2, 0xCD, 0x13, 0x72, 0); int failure = code.Count - 1;
        E(0x81, 0xC3, 0, 2, 0x47, 0x4E, 0x75, 0); code[^1] = unchecked((byte)(loop - code.Count));
        E(0x8A, 0x1E, 0x24, 0, 0xEA, 0, 0, 0x60, 0);
        code[failure] = (byte)(code.Count - failure - 1);
        E(0xB8, (byte)'E', 0x0E, 0xCD, 0x10, 0xFA, 0xF4, 0xEB, 0xFD);
        code.ToArray().CopyTo(disk, 62); disk[510] = 0x55; disk[511] = 0xAA;
    }
}