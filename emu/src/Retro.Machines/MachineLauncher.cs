using Retro.Core;
using Retro.Systems.Arcade;
using Retro.Systems.C64;
using Retro.Systems.Nes;
using Retro.Systems.Pc;
using Retro.Systems.Spectrum;

namespace Retro.Machines;

/// <summary>What was started: the machine, the file it runs and how it was loaded.</summary>
public sealed record LaunchResult(IMachine Machine, MediaFile Media, string Description, IReadOnlyList<string> Warnings, bool IsTape);

/// <summary>
/// Starts an implemented machine with a game file, the same way for the app, the CLI, the
/// compatibility runs and the fusions: firmware from the firmware folder, the best file of
/// the set when none is given, and the media loaded (a tape stays ready to be played).
/// </summary>
public static class MachineLauncher
{
    /// <summary>The machines this launcher can start.</summary>
    public static readonly string[] Implemented = { "zx48", "invaders", "c64", "nes", "pcxt" };

    public static bool IsImplemented(string machineId) => Implemented.Contains(machineId);

    /// <summary>
    /// Creates the machine and loads <paramref name="media"/> (or the best file of
    /// <paramref name="files"/> for that machine). Throws with a message for the user when
    /// the firmware is missing or the file is not something the machine can load yet.
    /// </summary>
    public static LaunchResult Launch(string machineId, IReadOnlyList<MediaFile> files, MediaFile? media, string firmwareDirectory, string? mediaFolder = null)
    {
        switch (machineId)
        {
            case "invaders":
            {
                var (rom, description) = SpaceInvadersRom.Build(files);
                var m = new SpaceInvaders(rom);
                string? samples = SpaceInvadersSound.FindSamples(mediaFolder, firmwareDirectory);
                if (samples != null)
                    m.Sound.LoadSamples(samples);
                return new LaunchResult(m, files[0], description, Array.Empty<string>(), false);
            }
            case "zx48":
            {
                string rom = Path.Combine(firmwareDirectory, "48.rom");
                if (!File.Exists(rom))
                    throw new FileNotFoundException($"Falta la ROM del Spectrum (48.rom) en {firmwareDirectory}.");
                var m = new Spectrum48(File.ReadAllBytes(rom));
                media ??= files
                    .Where(f => SpectrumMedia.Probe(f) > 0)
                    .OrderBy(f => MediaArchive.DumpScore(f.Name))
                    .ThenBy(f => f.Extension switch { ".tzx" => 0, ".tap" => 1, ".z80" => 2, _ => 3 })
                    .FirstOrDefault() ?? throw new InvalidDataException("No hay un archivo de Spectrum (.tap, .tzx, .z80, .sna).");
                var r = SpectrumMedia.Load(m, media);
                return new LaunchResult(m, media, r.Description, r.Warnings, r.Kind == SpectrumMediaKind.Tape);
            }
            case "c64":
            {
                var m = new C64(C64Firmware.Find(firmwareDirectory));
                media ??= files
                    .Where(f => C64Media.Probe(f) > 0)
                    .OrderBy(f => MediaArchive.DumpScore(f.Name))
                    .FirstOrDefault() ?? throw new InvalidDataException("No hay un archivo de C64 que se pueda cargar (.t64 o .prg).");
                var r = C64Media.Load(m, media);
                return new LaunchResult(m, media, r.Description, r.Warnings, false);
            }
            case "pcxt":
            {
                media ??= files.SelectMany(MachineCatalog.Identify).Where(c => c.Machine.Id == "pcxt" && c.Confidence >= 50)
                    .OrderBy(c => PcDos.ProgramPriority(c.File.Name)).ThenBy(c => MediaArchive.DumpScore(c.File.Name))
                    .Select(c => c.File).FirstOrDefault() ?? throw new InvalidDataException("No hay un programa DOS o disquete de PC que se pueda abrir.");
                bool program = media.Extension is ".exe" or ".com";
                byte[] disk = program ? PcDos.Prepare(files, media) : media.Data;
                var m = new PcXt(PcFirmware.Read(firmwareDirectory), disk);
                var identity = program ? media with { Data = disk } : media;
                return new LaunchResult(m, identity, program ? "PC/XT con FreeDOS integrado; los archivos originales quedan intactos."
                    : "PC/XT con CGA y disquete; el arranque depende del sistema incluido en la imagen.", Array.Empty<string>(), false);
            }
            case "nes":
            {
                media ??= files
                    .Where(f => NesCartridge.IsINes(f.Data))
                    .OrderBy(f => MediaArchive.DumpScore(f.Name))
                    .FirstOrDefault() ?? throw new InvalidDataException("No hay un cartucho de NES (.nes).");
                var cart = NesCartridge.Load(media.Data);
                var m = new Nes(cart);
                return new LaunchResult(m, media, cart.Describe(), Array.Empty<string>(), false);
            }
            default:
                throw new NotSupportedException($"La máquina \"{machineId}\" todavía no está implementada.");
        }
    }

    /// <summary>
    /// Loads a Spectrum tape the way a user does: boot to the copyright screen, type
    /// LOAD "" and play the tape through the ROM until it ends, or until a "stop the tape"
    /// block stops it (multiload games keep their next levels after it; the ROM starts the
    /// tape again when the game asks for them). Returns the frames it took.
    /// </summary>
    public static int LoadTape(Spectrum48 zx, int maxFrames = 30000, CancellationToken ct = default)
    {
        var sink = new short[8192];
        int frames = 0;
        for (; frames < 150; frames++)
        {
            ct.ThrowIfCancellationRequested();
            zx.RunFrame(null);
            zx.DrainAudio(sink);
        }
        zx.TypeLoadCommand();
        bool played = false;
        int stoppedFor = 0;
        while (frames < maxFrames && !zx.Tape.AtEnd)
        {
            ct.ThrowIfCancellationRequested();
            zx.RunFrame(null);
            zx.DrainAudio(sink);
            frames++;
            played |= zx.Tape.IsPlaying;
            stoppedFor = played && !zx.Tape.IsPlaying ? stoppedFor + 1 : 0;
            if (stoppedFor >= 100)
                break;      // stopped by the tape itself: the game took over
        }
        return frames;
    }

    /// <summary>Firmware in use, for reports ("ROM 48K de Sinclair", "Open ROMs"...).</summary>
    public static string FirmwareOf(IMachine m) => m switch
    {
        C64 c => c.FirmwareDescription,
        Spectrum48 => "ROM 48K de Sinclair (48.rom)",
        SpaceInvaders => "ROM del juego (4 chips de la placa)",
        PcXt p => $"BIOS PC ({p.Board.Memory.BiosIdentity[..12]})",
        Nes => "sin firmware (el programa está en el cartucho)",
        _ => "?",
    };

    /// <summary>Model and memory, for reports.</summary>
    public static string ModelOf(IMachine m) => m switch
    {
        C64 => "Commodore 64 PAL, 64 KB",
        Spectrum48 => "ZX Spectrum 48K (PAL)",
        SpaceInvaders => "Placa Taito/Midway 8080, 8 KB ROM + 8 KB RAM",
        PcXt p => $"PC/XT 8088, {p.Board.Memory.RamKilobytes} KB, CGA",
        Nes n => $"Nintendo NES NTSC, 2 KB RAM; mapper {n.Cartridge.Mapper}",
        _ => m.DisplayName,
    };
}
