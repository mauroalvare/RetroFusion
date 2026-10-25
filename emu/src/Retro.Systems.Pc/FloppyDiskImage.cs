using System.Security.Cryptography;

namespace Retro.Systems.Pc;

/// <summary>Sector image with a private copy-on-write overlay. The supplied file bytes are never modified.</summary>
public sealed class FloppyDiskImage
{
    private readonly byte[] _original;
    private readonly Dictionary<int, byte[]> _overlay = [];
    public FloppyDiskImage(byte[] image, bool writeProtected = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        (Cylinders, Heads, SectorsPerTrack) = image.Length switch
        {
            163840 => (40, 1, 8), 184320 => (40, 1, 9), 327680 => (40, 2, 8), 368640 => (40, 2, 9),
            737280 => (80, 2, 9), 1228800 => (80, 2, 15), 1474560 => (80, 2, 18),
            _ => throw new InvalidDataException("La imagen debe ser un disquete de 160, 180, 320, 360, 720, 1200 o 1440 KB.")
        };
        _original = image.ToArray(); Identity = Convert.ToHexString(SHA256.HashData(image)); WriteProtected = writeProtected;
    }
    public string Identity { get; }
    public int Cylinders { get; }
    public int Heads { get; }
    public int SectorsPerTrack { get; }
    public bool WriteProtected { get; }
    public int ModifiedSectors => _overlay.Count;
    private int Sector(int cylinder, int head, int sector)
    {
        if (cylinder < 0 || cylinder >= Cylinders || head < 0 || head >= Heads || sector < 1 || sector > SectorsPerTrack) return -1;
        return (cylinder * Heads + head) * SectorsPerTrack + sector - 1;
    }
    public bool ReadSector(int cylinder, int head, int sector, Span<byte> destination)
    {
        if (destination.Length != 512) throw new ArgumentException("Un sector debe ocupar 512 bytes.", nameof(destination));
        int index = Sector(cylinder, head, sector); if (index < 0) return false;
        if (_overlay.TryGetValue(index, out var changed)) changed.CopyTo(destination);
        else _original.AsSpan(index * 512, 512).CopyTo(destination);
        return true;
    }
    public bool WriteSector(int cylinder, int head, int sector, ReadOnlySpan<byte> source)
    {
        if (source.Length != 512) throw new ArgumentException("Un sector debe ocupar 512 bytes.", nameof(source));
        int index = Sector(cylinder, head, sector); if (index < 0 || WriteProtected) return false;
        _overlay[index] = source.ToArray(); return true;
    }
    public void SaveOverlay(BinaryWriter writer)
    {
        writer.Write(Identity); writer.Write(_overlay.Count);
        foreach (var sector in _overlay.OrderBy(p => p.Key)) { writer.Write(sector.Key); writer.Write(sector.Value); }
    }
    public void LoadOverlay(BinaryReader reader)
    {
        if (reader.ReadString() != Identity) throw new InvalidDataException("Las escrituras pertenecen a otro disquete.");
        int count = reader.ReadInt32(); if (count < 0 || count > _original.Length / 512) throw new InvalidDataException("Cantidad de sectores modificados inválida.");
        var next = new Dictionary<int, byte[]>();
        for (int n = 0; n < count; n++)
        {
            int index = reader.ReadInt32(); byte[] bytes = reader.ReadBytes(512);
            if (index < 0 || index >= _original.Length / 512 || !next.TryAdd(index, bytes)) throw new InvalidDataException("Sector modificado inválido o repetido.");
            if (bytes.Length != 512) throw new EndOfStreamException();
        }
        _overlay.Clear(); foreach (var entry in next) _overlay.Add(entry.Key, entry.Value);
    }
}