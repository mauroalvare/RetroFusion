namespace Retro.Systems.Nes;

/// <summary>How the PPU's two physical nametables are laid out in its 4 logical ones.</summary>
public enum NesMirroring { Horizontal, Vertical, FourScreen }

/// <summary>
/// A cartridge read from an iNES (.nes) image: PRG ROM seen by the CPU at $8000-$FFFF and
/// CHR ROM (or 8 KB of CHR RAM) seen by the PPU at $0000-$1FFF. Supported mappers:
/// 0 (NROM: 16 or 32 KB of PRG, fixed) and 3 (CNROM: any write to $8000-$FFFF picks the
/// 8 KB CHR bank).
/// </summary>
public sealed class NesCartridge
{
    public const int HeaderSize = 16, TrainerSize = 512, PrgUnit = 0x4000, ChrUnit = 0x2000;
    public static readonly int[] SupportedMappers = { 0, 3 };

    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private int _chrBank;

    public int Mapper { get; }
    public NesMirroring Mirroring { get; }
    public bool HasBattery { get; }
    public bool ChrIsRam { get; }
    public int PrgSize => _prg.Length;
    public int ChrSize => _chr.Length;
    /// <summary>The CHR bank the PPU sees now (always 0 on NROM).</summary>
    public int ChrBank => _chrBank;

    private NesCartridge(byte[] prg, byte[] chr, bool chrIsRam, int mapper, NesMirroring mirroring, bool battery)
    {
        _prg = prg;
        _chr = chr;
        ChrIsRam = chrIsRam;
        Mapper = mapper;
        Mirroring = mirroring;
        HasBattery = battery;
    }

    /// <summary>True when the data starts with the iNES signature "NES" + $1A.</summary>
    public static bool IsINes(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize && data[0] == (byte)'N' && data[1] == (byte)'E' && data[2] == (byte)'S' && data[3] == 0x1A;

    /// <summary>Reads an iNES or NES 2.0 image. Throws <see cref="InvalidDataException"/> with the reason in Spanish.</summary>
    public static NesCartridge Load(byte[] data)
    {
        if (!IsINes(data))
            throw new InvalidDataException("No es una imagen iNES (.nes): falta la firma NES<1A>.");
        int prgUnits = data[4], chrUnits = data[5];
        byte f6 = data[6], f7 = data[7];
        bool nes2 = (f7 & 0x0C) == 0x08;
        // old dumpers wrote text ("DiskDude!") in bytes 7-15: then the high mapper nibble is junk
        bool junkTail = !nes2 && (data[12] | data[13] | data[14] | data[15]) != 0;
        int mapper = (f6 >> 4) | (junkTail ? 0 : f7 & 0xF0);
        if (nes2)
            mapper |= (data[8] & 0x0F) << 8;
        if (!SupportedMappers.Contains(mapper))
            throw new InvalidDataException($"Mapper {mapper} no soportado todavía (por ahora: 0 NROM y 3 CNROM).");
        if (prgUnits == 0)
            throw new InvalidDataException("La imagen dice que no tiene PRG ROM.");
        int offset = HeaderSize + ((f6 & 0x04) != 0 ? TrainerSize : 0);
        int prgSize = prgUnits * PrgUnit, chrSize = chrUnits * ChrUnit;
        if (data.Length < offset + prgSize + chrSize)
            throw new InvalidDataException($"La imagen está cortada: el encabezado pide {prgSize + chrSize} bytes de ROM y hay {data.Length - offset}.");
        var prg = data.AsSpan(offset, prgSize).ToArray();
        bool chrRam = chrSize == 0;
        var chr = chrRam ? new byte[ChrUnit] : data.AsSpan(offset + prgSize, chrSize).ToArray();
        var mirroring = (f6 & 0x08) != 0 ? NesMirroring.FourScreen : (f6 & 0x01) != 0 ? NesMirroring.Vertical : NesMirroring.Horizontal;
        return new NesCartridge(prg, chr, chrRam, mapper, mirroring, (f6 & 0x02) != 0);
    }

    /// <summary>CPU read in $8000-$FFFF (16 KB of PRG appears twice).</summary>
    public byte CpuRead(ushort address) => _prg[(address - 0x8000) % _prg.Length];

    /// <summary>CPU write in $8000-$FFFF: on CNROM it selects the CHR bank; on NROM it does nothing.</summary>
    public void CpuWrite(ushort address, byte value)
    {
        if (Mapper == 3)
            _chrBank = value % (_chr.Length / ChrUnit);
    }

    /// <summary>PPU read in $0000-$1FFF (pattern tables).</summary>
    public byte PpuRead(ushort address) => _chr[_chrBank * ChrUnit + (address & 0x1FFF)];

    /// <summary>PPU write in $0000-$1FFF: only CHR RAM is writable.</summary>
    public void PpuWrite(ushort address, byte value)
    {
        if (ChrIsRam)
            _chr[address & 0x1FFF] = value;
    }

    /// <summary>Which physical nametable (0-3; 2 and 3 only with four-screen RAM) a $2000-$2FFF address uses.</summary>
    public int NametableFor(ushort address)
    {
        int table = (address >> 10) & 3;
        return Mirroring switch
        {
            NesMirroring.Horizontal => table >> 1,
            NesMirroring.Vertical => table & 1,
            _ => table,
        };
    }

    public string Describe() =>
        $"Cartucho NES: mapper {Mapper} ({(Mapper == 0 ? "NROM" : "CNROM")}), PRG {PrgSize / 1024} KB, " +
        (ChrIsRam ? "CHR RAM 8 KB" : $"CHR ROM {ChrSize / 1024} KB") +
        $", espejado {(Mirroring switch { NesMirroring.Horizontal => "horizontal", NesMirroring.Vertical => "vertical", _ => "de 4 pantallas" })}" +
        (HasBattery ? ", con batería" : "");
}
