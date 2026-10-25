using Retro.Core;
using Retro.Systems.Spectrum;

namespace Retro.Machines;

public enum MachineStatus { Available, Planned }

/// <param name="Needs">What the user has to provide to run software on it (shown in the Machine menu).</param>
public sealed record MachineInfo(string Id, string Name, string Cpu, MachineStatus Status, string[] RomFiles, string[] Extensions, string Needs = "");

/// <summary>
/// Every system the emulator knows about, implemented or planned, with the file
/// formats that belong to it. Used to pick a machine for a file and to tell the user
/// which system a file is for when it is not implemented yet.
/// </summary>
public static class MachineCatalog
{
    public static readonly MachineInfo[] All =
    {
        new("zx48", "ZX Spectrum 48K", "Zilog Z80", MachineStatus.Available, new[] { "48.rom" }, new[] { ".tap", ".tzx", ".z80", ".sna" },
            "La ROM del sistema (48.rom) viene incluida. Los juegos: cintas .tap/.tzx o instantáneas .z80/.sna."),
        new("zx128", "ZX Spectrum 128K", "Zilog Z80", MachineStatus.Planned, new[] { "128-0.rom", "128-1.rom" }, new[] { ".tap", ".tzx", ".z80", ".sna" }),
        new("tk90x", "TK90X", "Zilog Z80", MachineStatus.Planned, new[] { "tk90x.rom" }, new[] { ".tap", ".tzx" }),
        new("msx", "MSX", "Zilog Z80", MachineStatus.Planned, new[] { "msx.rom" }, new[] { ".rom", ".mx1", ".mx2", ".dsk", ".cas" }),
        new("invaders", "Placa arcade Taito 8080", "Intel 8080", MachineStatus.Available, new[] { "invaders.zip" }, new[] { ".h", ".g", ".f", ".e" },
            "Cada juego de arcade es su propia ROM: el invaders.zip de MAME (Space Invaders, 1978)."),
        new("cpc", "Amstrad CPC", "Zilog Z80", MachineStatus.Planned, new[] { "cpc464.rom" }, new[] { ".dsk", ".cdt" }),
        new("pc88", "NEC PC-8801", "Zilog Z80 (NEC uPD780)", MachineStatus.Planned, new[] { "pc88.rom" }, new[] { ".d88" }),
        new("c64", "Commodore 64", "MOS 6510", MachineStatus.Available, new[] { "kernal.rom", "basic.rom", "chargen.rom" }, new[] { ".prg", ".d64", ".t64", ".crt", ".tap" },
            "Trae el firmware libre Open ROMs; con las ROMs originales (kernal.rom, basic.rom, chargen.rom en firmware/c64) es más compatible. Juegos: cintas .t64 y programas .prg."),
        new("atari800", "Atari 800/XL", "MOS 6502", MachineStatus.Planned, new[] { "atarixl.rom" }, new[] { ".atr", ".xex", ".xfd", ".car", ".cas" }),
        new("apple2", "Apple II", "MOS 6502", MachineStatus.Planned, new[] { "apple2.rom" }, new[] { ".dsk", ".do", ".po", ".woz", ".nib" }),
        new("vic20", "Commodore VIC-20", "MOS 6502", MachineStatus.Planned, new[] { "vic20-kernal.rom" }, new[] { ".prg", ".crt" }),
        new("plus4", "Commodore Plus/4", "MOS 7501 (familia 6502)", MachineStatus.Planned, new[] { "plus4-kernal.rom" }, new[] { ".prg", ".tap", ".d64" }),
        new("nes", "Nintendo NES", "Ricoh 2A03 (familia 6502)", MachineStatus.Available, Array.Empty<string>(), new[] { ".nes" },
            "No necesita firmware. Juegos: cartuchos .nes (iNES) con mapper 0 (NROM), 1 (MMC1), 2 (UxROM), 3 (CNROM), 4 (MMC3) o 7 (AxROM)."),
        new("pcxt", "IBM PC / XT", "Intel 8088", MachineStatus.Available, new[] { "pcxt-bios.rom" }, new[] { ".exe", ".com", ".img", ".ima" },
            "Requiere BIOS en firmware/pcxt/pcxt-bios.rom y disquetes de arranque (.img/.ima) de 160 a 720 KB. Los EXE/COM necesitan DOS."),
        new("atarist", "Atari ST", "Motorola 68000", MachineStatus.Planned, new[] { "tos.img" }, new[] { ".st", ".msa", ".stx" }),
        new("amiga500", "Amiga 500", "Motorola 68000", MachineStatus.Planned, new[] { "kick13.rom" }, new[] { ".adf", ".ipf" }),
    };

    public static MachineInfo Get(string id) => All.First(m => m.Id == id);

    public sealed record Candidate(MachineInfo Machine, MediaFile File, int Confidence, string Reason);

    /// <summary>
    /// Arcade boards come as a set of ROM chips inside one zip: identify the whole set.
    /// Returns null when the files are not a known board.
    /// </summary>
    public static Candidate? IdentifySet(IReadOnlyList<MediaFile> files)
    {
        int score = Retro.Systems.Arcade.SpaceInvadersRom.Probe(files);
        if (score >= 80)
            return new Candidate(Get("invaders"), files[0], score, "chips de la placa de Space Invaders (MAME)");
        return null;
    }

    /// <summary>Which machines could run this file, best first. Signatures beat extensions.</summary>
    public static List<Candidate> Identify(MediaFile f)
    {
        var list = new List<Candidate>();
        int zx = SpectrumMedia.Probe(f);
        // .cdt is the Amstrad CPC's tape: same TZX container, different machine
        if (zx > 0 && f.Extension != ".cdt")
            list.Add(new Candidate(Get("zx48"), f, zx, FormatName(f.Extension, "formato de ZX Spectrum")));

        string ext = f.Extension;
        var d = f.Data;
        bool Starts(string sig) => d.Length >= sig.Length && d.AsSpan(0, sig.Length).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(sig));
        bool Bytes(params byte[] sig) => d.Length >= sig.Length && d.AsSpan(0, sig.Length).SequenceEqual(sig);
        void Add(string id, int confidence, string reason) => list.Add(new Candidate(Get(id), f, confidence, reason));
        string name = f.Name.ToLowerInvariant();

        // Commodore
        if (Starts("C64 tape image file") || Starts("C64S tape"))
            Add("c64", 95, "cinta .T64 (programas de C64)");
        if (Starts("C64 CARTRIDGE"))
            Add("c64", 95, "cartucho de C64");
        if (ext == ".tap" && (Starts("C64-TAPE-RAW") || Starts("C16-TAPE-RAW")))
            Add(name.Contains("plus4") || name.Contains("c16") || Starts("C16-TAPE-RAW") ? "plus4" : "c64", 90, "cinta de Commodore (pulsos)");
        if (ext == ".d64" && (d.Length == 174848 || d.Length == 175531))
            Add("c64", 90, "disco de 1541 (.d64)");
        if (ext == ".crt" && !Starts("C64 CARTRIDGE") && d.Length >= 2 && d[0] == 0 && d[1] is 0xA0 or 0x20 or 0x40 or 0x60)
            Add("vic20", 85, $"cartucho de VIC-20 (se carga en ${d[1]:X2}00)");
        if (ext == ".prg" && d.Length > 2)
        {
            int load = d[0] | (d[1] << 8);
            if (load == 0x0801) Add("c64", 80, "programa de C64 (empieza en $0801)");
            else if (load is 0x1001 or 0x1201) Add("vic20", 75, $"programa de VIC-20 (empieza en ${load:X4})");
            else Add("c64", 50, $"programa de Commodore (empieza en ${load:X4})");
        }

        // Atari 8 bits
        if (Bytes(0x96, 0x02))
            Add("atari800", 95, "disco de Atari 8 bits (.atr)");
        if (Bytes(0xFF, 0xFF) && d.Length > 6 && ext is ".xex" or ".com" or ".exe" or ".obj")
            Add("atari800", 95, "programa de Atari 8 bits (.xex)");
        if (Starts("CART") && ext == ".car")
            Add("atari800", 95, "cartucho de Atari 8 bits");

        // NES
        if (Bytes(0x4E, 0x45, 0x53, 0x1A))
            Add("nes", 95, NesMapperProblem(d) is { } problem ? $"cartucho de NES (iNES; {problem})" : "cartucho de NES (iNES)");

        // Amstrad CPC
        if (Starts("MV - CPC") || Starts("EXTENDED CPC DSK"))
            Add("cpc", 95, "disco de Amstrad CPC");
        else if (AmsdosHeader(d))
            Add("cpc", 90, "archivo de Amstrad CPC (cabecera AMSDOS)");
        if (Starts("ZXTape!") && ext == ".cdt")
            Add("cpc", 95, "cinta de Amstrad CPC (.cdt)");

        // NEC PC-88
        if (ext == ".d88" && d.Length > 0x20 && BitConverter.ToInt32(d, 0x1C) == d.Length)
            Add("pc88", 90, "disco D88 (NEC PC-88)");

        // 16 bits
        if (Bytes(0x0E, 0x0F) && ext == ".msa")
            Add("atarist", 95, "disco comprimido de Atari ST (.msa)");
        if (ext == ".st" && d.Length % 512 == 0 && d.Length >= 360 * 1024)
            Add("atarist", 80, "disco de Atari ST (.st)");
        if (ext == ".adf" && (d.Length == 901120 || d.Length == 1802240))
            Add("amiga500", Starts("DOS") ? 95 : 85, "disco de Amiga (.adf)");
        if (ext == ".exe" && IsDosExecutable(d))
            Add("pcxt", 90, "programa de MS-DOS (.exe)");
        if (ext == ".com" && !Bytes(0xFF, 0xFF) && d.Length < 65280)
            Add("pcxt", 60, "programa de MS-DOS (.com)");

        if (ext is ".img" or ".ima" && Retro.Systems.Pc.PcFirmware.IsSupportedDiskSize(d.Length))
            Add("pcxt", d.Length >= 512 && d[510] == 0x55 && d[511] == 0xAA ? 90 : 70, "imagen de disquete de PC");

        // Apple II
        if (ext is ".dsk" or ".do" or ".po" && d.Length == 143360 && list.All(c => c.Machine.Id != "cpc"))
            Add("apple2", 85, "disco de 140 KB (Apple II)");
        if (Starts("WOZ1") || Starts("WOZ2"))
            Add("apple2", 95, "disco de Apple II (.woz)");

        foreach (var m in All)
            if (m.Extensions.Contains(ext) && list.All(c => c.Machine.Id != m.Id) && m.Id is not ("zx48" or "invaders"))
                list.Add(new Candidate(m, f, 20, "por la extensión " + ext));
        return list.OrderByDescending(c => c.Machine.Status == MachineStatus.Available && c.Confidence >= 50)
            .ThenByDescending(c => c.Confidence).ToList();
    }

    /// <summary>
    /// Why an implemented machine cannot load this file yet (a format of a later stage), or
    /// null when it can.
    /// </summary>
    public static string? NotLoadableYet(Candidate c) => c.Machine.Id switch
    {
        "pcxt" when c.File.Extension is ".exe" or ".com" => "el programa necesita DOS: abrí un disquete que incluya el sistema y el programa",
        "pcxt" when c.File.Extension is ".img" or ".ima" && !Retro.Systems.Pc.PcFirmware.IsSupportedDiskSize(c.File.Data.Length) => "por ahora PC/XT admite disquetes de 160 a 720 KB",
        "nes" when NesMapperProblem(c.File.Data) is { } problem => problem,
        "c64" when c.File.Extension is ".d64" => "los discos .d64 del C64 llegan en la próxima etapa: buscá la versión .t64 o .prg del juego",
        "c64" when c.File.Extension is ".crt" => "los cartuchos .crt del C64 llegan en la próxima etapa: buscá la versión .t64 o .prg del juego",
        "c64" when c.File.Extension is ".tap" => "las cintas de pulsos .tap del C64 llegan en la próxima etapa: buscá la versión .t64 o .prg del juego",
        _ => null,
    };

    /// <summary>"mapper N" when an iNES image uses a mapper the NES does not have yet, otherwise null.</summary>
    private static string? NesMapperProblem(byte[] d)
    {
        if (!Retro.Systems.Nes.NesCartridge.IsINes(d))
            return null;
        try
        {
            Retro.Systems.Nes.NesCartridge.ValidateImage(d);
            return null;
        }
        catch (InvalidDataException e)
        {
            return e.Message.TrimEnd('.');
        }
    }

    private static string FormatName(string ext, string fallback) => ext switch
    {
        ".tzx" => "cinta TZX de ZX Spectrum",
        ".tap" => "cinta TAP de ZX Spectrum",
        ".z80" => "instantánea Z80 de ZX Spectrum",
        ".sna" => "instantánea SNA de ZX Spectrum",
        _ => fallback,
    };

    /// <summary>Amstrad files saved by AMSDOS start with a 128-byte header whose bytes 67-68 are the sum of bytes 0-66.</summary>
    private static bool AmsdosHeader(byte[] d)
    {
        if (d.Length < 128 || d[0] > 15)
            return false;
        int sum = 0;
        for (int i = 0; i < 67; i++)
            sum += d[i];
        return sum != 0 && (d[67] | (d[68] << 8)) == (sum & 0xFFFF);
    }

    // Windows/OS2 executables have a DOS stub too; that does not make them DOS games.
    private static bool IsDosExecutable(byte[] data)
    {
        if (data.Length < 28 || data[0] != 'M' || data[1] != 'Z') return false;
        int pages = data[4] | data[5] << 8, last = data[2] | data[3] << 8, header = (data[8] | data[9] << 8) * 16;
        long size = (pages - 1L) * 512 + (last == 0 ? 512 : last);
        if (pages == 0 || last > 511 || header < 28 || header > size || size > data.Length) return false;
        if (data.Length >= 64)
        {
            uint offset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(60, 4));
            if (offset >= 64 && offset <= data.Length - 2)
            {
                var signature = data.AsSpan((int)offset, 2);
                if (signature.SequenceEqual("PE"u8) || signature.SequenceEqual("NE"u8) || signature.SequenceEqual("LE"u8) ||
                    signature.SequenceEqual("LX"u8) || signature.SequenceEqual("W3"u8) || signature.SequenceEqual("W4"u8)) return false;
            }
        }
        return true;
    }

    /// <summary>Why a file cannot be opened at all (compressed formats the emulator does not read), or null.</summary>
    public static string? Unsupported(MediaFile f)
    {
        var d = f.Data;
        bool Starts(byte[] sig) => d.Length >= sig.Length && d.AsSpan(0, sig.Length).SequenceEqual(sig);
        if (Starts(new byte[] { 0x52, 0x61, 0x72, 0x21 }))
            return "es un archivo comprimido RAR, que el emulador todavía no sabe abrir. Descomprimilo con 7-Zip o WinRAR y abrí el juego que tiene adentro.";
        if (Starts(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }))
            return "es un archivo comprimido 7z, que el emulador todavía no sabe abrir. Descomprimilo y abrí el juego que tiene adentro.";
        return null;
    }
}
