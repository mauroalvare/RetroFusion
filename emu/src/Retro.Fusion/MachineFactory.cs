using Retro.Core;
using Retro.Systems.Arcade;
using Retro.Systems.Spectrum;

namespace Retro.Fusion;

/// <summary>A game ready to be started: its profile and the files it came from.</summary>
public sealed record GameSource(GameProfile Profile, IReadOnlyList<MediaFile> Files, string? Folder);

/// <summary>Creates the right emulated machine for a game and boots it to playing.</summary>
public static class MachineFactory
{
    public static IMachine Create(GameSource source, string romDirectory)
    {
        var m = CreateMachine(source, romDirectory);
        m.ExecutedMap = new bool[0x10000];   // the code view sees everything the game ran, boot included
        return m;
    }

    private static IMachine CreateMachine(GameSource source, string romDirectory)
    {
        switch (source.Profile.Machine)
        {
            case "invaders":
            {
                var (rom, _) = SpaceInvadersRom.Build(source.Files);
                var m = new SpaceInvaders(rom);
                string? samples = SpaceInvadersSound.FindSamples(source.Folder, romDirectory);
                if (samples != null)
                    m.Sound.LoadSamples(samples);
                return m;
            }
            case "zx48":
            {
                var m = new Spectrum48(File.ReadAllBytes(Path.Combine(romDirectory, "48.rom")));
                var media = source.Files
                    .Where(f => SpectrumMedia.Probe(f) > 0)
                    .OrderBy(f => MediaArchive.DumpScore(f.Name))
                    .ThenBy(f => f.Extension switch { ".tzx" => 0, ".tap" => 1, ".z80" => 2, _ => 3 })
                    .FirstOrDefault() ?? throw new InvalidDataException($"No hay un archivo de Spectrum para {source.Profile.Name}.");
                SpectrumMedia.Load(m, media);
                return m;
            }
            default:
                throw new NotSupportedException($"La máquina \"{source.Profile.Machine}\" todavía no está implementada.");
        }
    }

    /// <summary>
    /// Runs the profile's boot script as fast as possible: tapes load through the ROM
    /// (real signal), keys are pressed for the given number of frames.
    /// </summary>
    public static void Boot(IMachine m, GameProfile profile, Action<string>? log = null)
    {
        foreach (var step in profile.Boot)
        {
            var parts = step.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "wait":
                    RunFrames(m, int.Parse(parts[1]));
                    break;
                case "press":
                {
                    int frames = int.Parse(parts[^1]);
                    string key = string.Join(' ', parts[1..^1]);
                    m.SetKey(key, true);
                    RunFrames(m, frames);
                    m.SetKey(key, false);
                    break;
                }
                case "load-tape":
                    if (m is Spectrum48 zx && zx.Tape.IsLoaded)
                    {
                        RunFrames(m, 150);
                        zx.TypeLoadCommand();
                        for (int i = 0; i < 30000 && !zx.Tape.AtEnd; i++)
                            zx.RunFrame(null);
                        zx.DrainAudio(new short[4096]);
                    }
                    break;
            }
            log?.Invoke($"{profile.Name}: {step}");
        }
        m.DrainAudio(new short[8192]);
    }

    private static void RunFrames(IMachine m, int n)
    {
        var sink = new short[4096];
        for (int i = 0; i < n; i++)
        {
            m.RunFrame(null);
            m.DrainAudio(sink);
        }
    }
}
