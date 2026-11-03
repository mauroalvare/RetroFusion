using System.Buffers.Binary;
using System.Security.Cryptography;
using Retro.Systems.Nes;

namespace Retro.App;

/// <summary>Content-bound PRG NVRAM. Only the player's host opts in; labs and trials stay isolated.</summary>
public sealed class NesBatteryStore(string? folder = null)
{
    private static ReadOnlySpan<byte> Magic => "RF-NES-BATTERY"u8;
    private const int RamSize = 8192, HeaderSize = 14 + 4 + 32 + 32;
    public string Folder { get; } = Path.GetFullPath(folder ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "battery"));

    public string PathFor(string identity)
    {
        if (identity.Length != 64 || !identity.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Identidad de cartucho inválida.");
        return Path.Combine(Folder, identity.ToUpperInvariant() + ".rfbattery");
    }

    public NesBatterySession Open(Nes machine) => new(this, machine);

    internal static bool IsStorageError(Exception e) => e is IOException or UnauthorizedAccessException or InvalidDataException;

    internal static byte[]? ReadRaw(string path)
    {
        try
        {
            RejectLink(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > 65536) throw new InvalidDataException("El archivo de batería supera el tamaño admitido. Se conservó sin cambios.");
            var data = new byte[(int)input.Length];
            input.ReadExactly(data);
            return data;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal static byte[] Decode(byte[] data, string identity)
    {
        if (data.Length != HeaderSize + RamSize || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("El archivo de batería está dañado o tiene un formato desconocido. Se conservó sin cambios.");
        if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(14)) != 1)
            throw new InvalidDataException("La versión del archivo de batería no está soportada. Se conservó sin cambios.");
        if (!data.AsSpan(18, 32).SequenceEqual(Convert.FromHexString(identity)))
            throw new InvalidDataException("La batería pertenece a otra edición del juego. Se conservó sin cambios.");
        var ram = data.AsSpan(HeaderSize);
        if (!CryptographicOperations.FixedTimeEquals(data.AsSpan(50, 32), SHA256.HashData(ram)))
            throw new InvalidDataException("El contenido de la batería está dañado. Se conservó sin cambios.");
        return ram.ToArray();
    }

    internal static byte[] Encode(string identity, byte[] ram)
    {
        if (ram.Length != RamSize) throw new InvalidDataException("La batería no corresponde a la RAM de este cartucho.");
        var data = new byte[HeaderSize + RamSize];
        Magic.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 1);
        Convert.FromHexString(identity).CopyTo(data, 18);
        SHA256.HashData(ram).CopyTo(data, 50);
        ram.CopyTo(data, HeaderSize);
        return data;
    }

    internal byte[] Commit(string identity, byte[] ram, byte[]? expected, bool replace, out string? preserved)
    {
        string path = PathFor(identity);
        Directory.CreateDirectory(Folder);
        // Keep this empty lock file: unlinking it would allow two different locks for the same save.
        RejectLink(path + ".lock");
        using var gate = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        byte[]? current = ReadRaw(path);
        if (!replace && !Same(current, expected))
            throw new IOException("Otra instancia cambió la batería. No se sobrescribió su progreso; exportá una copia de esta partida.");
        byte[] data = Encode(identity, ram);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        preserved = null;
        try
        {
            WriteNew(temporary, data);
            if (current != null)
            {
                // Preserve the exact previous file, including unknown formats, before replacing it.
                preserved = replace ? path + "." + Guid.NewGuid().ToString("N") + ".preserved" : path + ".previous";
                if (replace) WriteNew(preserved, current);
                else WriteAtomic(preserved, current);
            }
            File.Move(temporary, path, overwrite: true);
            return data;
        }
        finally { TryDelete(temporary); }
    }

    internal static string Export(string path, string identity, byte[] ram)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteNew(path, Encode(identity, ram));
        return path;
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        RejectLink(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { WriteNew(temporary, data); File.Move(temporary, path, overwrite: true); }
        finally { TryDelete(temporary); }
    }

    private static void WriteNew(string path, byte[] data)
    {
        // CreateNew is deliberately outside the catch: a pre-existing export must never be deleted.
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try { output.Write(data); output.Flush(flushToDisk: true); }
        catch { output.Dispose(); TryDelete(path); throw; }
    }

    private static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("El archivo de batería es un enlace. Se conservó sin cambios.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    internal static bool Same(byte[]? a, byte[]? b) => a == null ? b == null : b != null && a.AsSpan().SequenceEqual(b);
    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (IsStorageError(e)) { }
    }
}

public sealed class NesBatterySession
{
    private readonly NesBatteryStore _store;
    private readonly Nes _machine;
    private byte[]? _expected, _exported;
    private byte[] _saved;
    private bool _blocked;
    private double _elapsed;
    public string Path { get; }
    public string? Problem { get; private set; }
    public string? ExportedPath { get; private set; }
    public string? PreservedPath { get; private set; }
    public bool Pending => !NesBatteryStore.Same(_saved, _machine.SaveBattery());
    public bool HasRecovery
    {
        get
        {
            if (_exported == null || ExportedPath == null || !NesBatteryStore.Same(_exported, _machine.SaveBattery())) return false;
            try
            {
                byte[]? file = NesBatteryStore.ReadRaw(ExportedPath);
                return file != null && NesBatteryStore.Same(_exported, NesBatteryStore.Decode(file, _machine.Cartridge.Identity));
            }
            catch (Exception e) when (NesBatteryStore.IsStorageError(e)) { return false; }
        }
    }

    internal NesBatterySession(NesBatteryStore store, Nes machine)
    {
        if (!machine.Cartridge.SupportsBatterySave) throw new ArgumentException("Este cartucho no admite archivos de batería de 8 KB.");
        _store = store; _machine = machine; Path = store.PathFor(machine.Cartridge.Identity);
        _saved = machine.SaveBattery();
        try
        {
            _expected = NesBatteryStore.ReadRaw(Path);
            if (_expected != null) machine.LoadBattery(NesBatteryStore.Decode(_expected, machine.Cartridge.Identity));
            _saved = machine.SaveBattery();
        }
        catch (Exception e) when (NesBatteryStore.IsStorageError(e)) { _blocked = true; Problem = e.Message; }
    }

    public void Poll(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        _elapsed += Math.Min(seconds, 5);
        if (_elapsed < 2) return;
        _elapsed = 0;
        Flush();
    }

    /// <summary>Failure keeps both the current RAM and the previous file. The caller decides whether to leave.</summary>
    public bool Flush()
    {
        byte[] ram = _machine.SaveBattery();
        if (NesBatteryStore.Same(_saved, ram)) return true;
        if (_blocked) return false;
        try
        {
            _expected = _store.Commit(_machine.Cartridge.Identity, ram, _expected, replace: false, out _);
            _saved = ram; Problem = null;
            return true;
        }
        catch (Exception e) when (NesBatteryStore.IsStorageError(e)) { Problem = e.Message; return false; }
    }

    public string Export(string path)
    {
        byte[] ram = _machine.SaveBattery();
        string result = NesBatteryStore.Export(path, _machine.Cartridge.Identity, ram);
        _exported = ram; ExportedPath = result;
        return result;
    }

    /// <summary>Explicit recovery: preserve current memory and the previous disk file before committing the chosen copy.</summary>
    public void Restore(string path)
    {
        byte[] ram = NesBatteryStore.Decode(NesBatteryStore.ReadRaw(path) ?? throw new FileNotFoundException("No se encontró la copia de batería."), _machine.Cartridge.Identity);
        byte[] current = _machine.SaveBattery();
        if (!NesBatteryStore.Same(current, ram))
        {
            string backup = System.IO.Path.Combine(_store.Folder, "recovery", _machine.Cartridge.Identity + "." + Guid.NewGuid().ToString("N") + ".rfbattery");
            // If preservation fails, neither memory nor the primary file is modified.
            Export(backup);
        }
        byte[] committed = _store.Commit(_machine.Cartridge.Identity, ram, _expected, replace: true, out string? preserved);
        _machine.LoadBattery(ram);
        _expected = committed; _saved = ram; _blocked = false; Problem = null; PreservedPath = preserved;
    }
}
