using System.Security.Cryptography;

namespace Retro.Systems.Pc;

/// <summary>PC/XT physical memory with conventional RAM, 16 KB CGA RAM and a top-aligned system BIOS.</summary>
public sealed partial class PcMemory
{
    private readonly byte[] _ram, _video = new byte[0x4000], _bios;
    private readonly uint _biosStart;

    public PcMemory(byte[] bios, int ramKilobytes = 640)
    {
        ArgumentNullException.ThrowIfNull(bios);
        if (bios.Length is not (0x2000 or 0x4000 or 0x8000 or 0x10000))
            throw new ArgumentException("El BIOS debe ocupar 8, 16, 32 o 64 KB.", nameof(bios));
        if (ramKilobytes is < 16 or > 640 || ramKilobytes % 16 != 0)
            throw new ArgumentOutOfRangeException(nameof(ramKilobytes), "La RAM debe ser un múltiplo de 16 KB, entre 16 y 640 KB.");
        _bios = bios.ToArray(); _biosStart = 0x100000 - (uint)_bios.Length;
        _ram = new byte[ramKilobytes * 1024];
        BiosIdentity = Convert.ToHexString(SHA256.HashData(_bios));
    }

    public int RamKilobytes => _ram.Length / 1024;
    public uint BiosStart => _biosStart;
    public string BiosIdentity { get; }
    public ReadOnlySpan<byte> Ram => _ram;
    public ReadOnlySpan<byte> VideoRam => _video;

    public byte Read(uint address)
    {
        Validate(address);
        if (address < _ram.Length) return _ram[address];
        if (address is >= 0xB8000 and <= 0xBFFFF) return _video[address & 0x3FFF];
        if (address >= _biosStart) return _bios[address - _biosStart];
        return 0xFF;
    }

    public void Write(uint address, byte value)
    {
        Validate(address);
        if (address < _ram.Length) _ram[address] = value;
        else if (address is >= 0xB8000 and <= 0xBFFFF) _video[address & 0x3FFF] = value;
        // No RAM backing for system ROM or unpopulated address ranges.
    }

    public void ClearRam() { Array.Clear(_ram); Array.Clear(_video); }

    private static void Validate(uint address)
    {
        if (address > 0xFFFFF) throw new ArgumentOutOfRangeException(nameof(address));
    }
}