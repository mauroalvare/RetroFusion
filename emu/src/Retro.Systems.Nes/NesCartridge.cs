namespace Retro.Systems.Nes;

/// <summary>How the PPU's two physical nametables are laid out in its 4 logical ones.</summary>
public enum NesMirroring { Horizontal, Vertical, FourScreen, SingleLower, SingleUpper }

/// <summary>
/// A cartridge read from an iNES (.nes) image: PRG ROM seen by the CPU at $8000-$FFFF and
/// CHR ROM (or 8 KB of CHR RAM) seen by the PPU at $0000-$1FFF. Supported mappers:
/// 0 (NROM), 1 (MMC1), 2 (UxROM: switchable 16 KB PRG), 3 (CNROM: 8 KB CHR banks)
/// 4 (MMC3: bank switching and filtered PPU A12 IRQ) and 7 (AxROM: 32 KB PRG banks with single-screen mirroring).
/// </summary>
public sealed partial class NesCartridge
{
    public const int HeaderSize = 16, TrainerSize = 512, PrgUnit = 0x4000, ChrUnit = 0x2000;
    public static readonly int[] SupportedMappers = { 0, 1, 2, 3, 4, 7 };

    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private int _chrBank, _prgBank;
    private readonly NesMirroring _initialMirroring;
    public string Identity { get; private init; } = "";

    public int Mapper { get; }
    public NesMirroring Mirroring { get; private set; }
    public bool HasBattery { get; }
    public bool ChrIsRam { get; }
    public bool PrgRamEnabled => Mapper switch { 1 => (_mmc1Prg & 0x10) == 0, 4 => (_mmc3Ram & 0x80) != 0, _ => true };
    public bool PrgRamWritable => PrgRamEnabled && (Mapper != 4 || (_mmc3Ram & 0x40) == 0);
    public bool Irq => Mapper == 4 && _mmc3Irq;
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
        _initialMirroring = mirroring;
        ResetBanks();
        HasBattery = battery;
    }

    /// <summary>True when the data starts with the iNES signature "NES" + $1A.</summary>
    public static bool IsINes(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize && data[0] == (byte)'N' && data[1] == (byte)'E' && data[2] == (byte)'S' && data[3] == 0x1A;

    private readonly record struct Header(int Mapper, int Offset, int PrgSize, int ChrSize, NesMirroring Mirroring, bool Battery);

    /// <summary>Checks a complete image without copying or hashing its ROM data.</summary>
    public static void ValidateImage(ReadOnlySpan<byte> data) { _ = ReadHeader(data); }

    private static Header ReadHeader(ReadOnlySpan<byte> data)
    {
        if (!IsINes(data))
            throw new InvalidDataException("No es una imagen iNES (.nes): falta la firma NES<1A>.");
        int prgUnits = data[4], chrUnits = data[5];
        byte f6 = data[6], f7 = data[7];
        bool nes2 = (f7 & 0x0C) == 0x08;
        // Some old dumpers appended text to the header, including the high mapper nibble.
        bool junkTail = !nes2 && (data[12] | data[13] | data[14] | data[15]) != 0;
        int mapper = (f6 >> 4) | (junkTail ? 0 : f7 & 0xF0);
        if (nes2) mapper |= (data[8] & 0x0F) << 8;
        if (!SupportedMappers.Contains(mapper))
            throw new InvalidDataException($"Mapper {mapper} no soportado todavÃ­a (por ahora: 0 NROM, 1 MMC1, 2 UxROM, 3 CNROM, 4 MMC3 y 7 AxROM).");
        if (prgUnits == 0) throw new InvalidDataException("La imagen dice que no tiene PRG ROM.");
        if (nes2 && data[9] != 0)
            throw new InvalidDataException("Los tamaÃ±os ampliados de NES 2.0 todavÃ­a no estÃ¡n implementados.");
        if (mapper is 0 or 3 && prgUnits > 2 || mapper == 7 && (prgUnits < 2 || prgUnits % 2 != 0))
            throw new InvalidDataException("El tamaÃ±o de PRG ROM no corresponde a este mapper.");
        if (mapper == 1)
        {
            int submapper = nes2 ? data[8] >> 4 : 0;
            int ram = nes2 ? RamSize(data[10] & 15) + RamSize(data[10] >> 4) : (junkTail || data[8] == 0 ? 1 : data[8]) * 8192;
            if (submapper is not (0 or 5) || ram > 8192 || chrUnits > 16 || prgUnits > 32 ||
                prgUnits > 16 && chrUnits != 0 || submapper == 5 && prgUnits != 2 || (f6 & 8) != 0)
                throw new InvalidDataException("Esta variante de placa MMC1 todavÃ­a no estÃ¡ soportada (RAM ampliada, submapper o tamaÃ±o de ROM).");
        }
        if (mapper == 4)
        {
            int submapper = nes2 ? data[8] >> 4 : 0;
            int ram = nes2 ? RamSize(data[10] & 15) + RamSize(data[10] >> 4) : (junkTail || data[8] == 0 ? 1 : data[8]) * 8192;
            if (submapper != 0 || ram > 8192 || prgUnits > 32 || prgUnits < 2 || chrUnits > 32)
                throw new InvalidDataException("Esta variante MMC3 todavÃ­a no estÃ¡ soportada (MMC6, RAM ampliada o tamaÃ±os de ROM).");
        }
        int offset = HeaderSize + ((f6 & 0x04) != 0 ? TrainerSize : 0);
        int prgSize = prgUnits * PrgUnit, chrSize = chrUnits * ChrUnit;
        if (data.Length < offset + prgSize + chrSize)
            throw new InvalidDataException($"La imagen estÃ¡ cortada: el encabezado pide {prgSize + chrSize} bytes de ROM y hay {data.Length - offset}.");
        var mirroring = (f6 & 8) != 0 ? NesMirroring.FourScreen : (f6 & 1) != 0 ? NesMirroring.Vertical : NesMirroring.Horizontal;
        return new(mapper, offset, prgSize, chrSize, mirroring, (f6 & 2) != 0);
    }

    private static int RamSize(int shift) => shift == 0 ? 0 : 64 << shift;

    public static NesCartridge Load(byte[] data)
    {
        var h = ReadHeader(data);
        var prg = data.AsSpan(h.Offset, h.PrgSize).ToArray();
        var chr = h.ChrSize == 0 ? new byte[ChrUnit] : data.AsSpan(h.Offset + h.PrgSize, h.ChrSize).ToArray();
        return new NesCartridge(prg, chr, h.ChrSize == 0, h.Mapper, h.Mirroring, h.Battery)
        { Identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)) };
    }

    /// <summary>CPU read in $8000-$FFFF (16 KB of PRG appears twice).</summary>
    private int PrgOffset(ushort address) => Mapper switch
    {
        1 => Mmc1PrgOffset(address),
        4 => Mmc3PrgOffset(address),
        2 => ((address < 0xC000 ? _prgBank : _prg.Length / PrgUnit - 1) * PrgUnit + (address & 0x3FFF)) % _prg.Length,
        7 => (_prgBank * 0x8000 + (address & 0x7FFF)) % _prg.Length,
        _ => (address - 0x8000) % _prg.Length
    };
    public byte CpuRead(ushort address) => _prg[PrgOffset(address)];

    /// <summary>Mapper register write. The optional CPU cycle filters consecutive MMC1 data writes.</summary>
    public void CpuWrite(ushort address, byte value, long cycle = -1)
    {
        if (Mapper == 1) Mmc1Write(address, value, cycle);
        else if (Mapper == 4) Mmc3Write(address, value);
        else if (Mapper == 3) _chrBank = value % (_chr.Length / ChrUnit);
        else if (Mapper == 2) _prgBank = (value & 15) % (_prg.Length / PrgUnit);
        else if (Mapper == 7)
        {
            _prgBank = (value & 7) % (_prg.Length / 0x8000);
            Mirroring = (value & 0x10) == 0 ? NesMirroring.SingleLower : NesMirroring.SingleUpper;
        }
    }

    /// <summary>Code editor change to the copy of PRG in memory (the file is not touched).</summary>
    public void PatchPrg(ushort address, byte value) => _prg[PrgOffset(address)] = value;

    /// <summary>PPU read in $0000-$1FFF (pattern tables).</summary>
    private int ChrOffset(ushort address) => Mapper switch { 1 => Mmc1ChrOffset(address), 4 => Mmc3ChrOffset(address), _ => _chrBank * ChrUnit + (address & 0x1FFF) };
    public byte PpuRead(ushort address) => _chr[ChrOffset(address)];

    /// <summary>PPU write in $0000-$1FFF: only CHR RAM is writable.</summary>
    public void PpuWrite(ushort address, byte value)
    {
        if (ChrIsRam)
            _chr[ChrOffset(address)] = value;
    }

    /// <summary>Which physical nametable (0-3; 2 and 3 only with four-screen RAM) a $2000-$2FFF address uses.</summary>
    public int NametableFor(ushort address)
    {
        int table = (address >> 10) & 3;
        return Mirroring switch
        {
            NesMirroring.Horizontal => table >> 1,
            NesMirroring.Vertical => table & 1,
            NesMirroring.SingleLower => 0,
            NesMirroring.SingleUpper => 1,
            _ => table,
        };
    }

    /// <summary>Mutable mapper registers, serial transfer state and CHR RAM.</summary>
    public void Save(BinaryWriter w)
    {
        w.Write(_chrBank);
        if (ChrIsRam) w.Write(_chr);
        if (Mapper == 1) SaveMmc1(w);
        if (Mapper == 4) SaveMmc3(w);
        if (Mapper is 2 or 7) { w.Write(_prgBank); w.Write((int)Mirroring); }
    }

    public void Load(BinaryReader r)
    {
        int bank = r.ReadInt32();
        if (bank < 0 || bank >= _chr.Length / ChrUnit) throw new InvalidDataException("El banco CHR del estado no es vÃ¡lido.");
        byte[]? chr = ChrIsRam ? r.ReadBytes(_chr.Length) : null;
        if (chr != null && chr.Length != _chr.Length) throw new EndOfStreamException();
        int prg = 0; var mirroring = _initialMirroring;
        if (Mapper is 2 or 7)
        {
            prg = r.ReadInt32(); mirroring = (NesMirroring)r.ReadInt32();
            if (prg < 0 || prg >= _prg.Length / (Mapper == 7 ? 0x8000 : PrgUnit) ||
                (Mapper == 7 ? mirroring is not (NesMirroring.SingleLower or NesMirroring.SingleUpper) : mirroring != _initialMirroring))
                throw new InvalidDataException("El banco PRG del estado no es vÃ¡lido.");
        }
        if (Mapper == 1) LoadMmc1(r);
        if (Mapper == 4) LoadMmc3(r);
        _chrBank = bank; _prgBank = prg;
        if (Mapper != 4) Mirroring = mirroring;
        if (Mapper == 1) UpdateMmc1Mirroring();
        if (chr != null) chr.CopyTo(_chr, 0);
    }

    public void ResetBanks()
    {
        _chrBank = _prgBank = 0;
        Mirroring = Mapper == 7 ? NesMirroring.SingleLower : _initialMirroring;
        if (Mapper == 1) ResetMmc1();
        if (Mapper == 4) ResetMmc3();
    }

    public string Describe() =>
        $"Cartucho NES: mapper {Mapper} ({(Mapper switch { 0 => "NROM", 1 => "MMC1", 2 => "UxROM", 3 => "CNROM", 4 => "MMC3", 7 => "AxROM", _ => "desconocido" })}), PRG {PrgSize / 1024} KB, " +
        (ChrIsRam ? "CHR RAM 8 KB" : $"CHR ROM {ChrSize / 1024} KB") +
        $", espejado {(Mirroring switch { NesMirroring.Horizontal => "horizontal", NesMirroring.Vertical => "vertical", NesMirroring.SingleLower => "una pantalla inferior", NesMirroring.SingleUpper => "una pantalla superior", _ => "de 4 pantallas" })}" +
        (HasBattery ? ", con baterÃ­a" : "");
}
