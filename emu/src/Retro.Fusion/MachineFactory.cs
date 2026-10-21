using Retro.Core;
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
        try
        {
            var member = source.Profile.Match.Member is { } name
                ? source.Files.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase))
                : null;
            return Retro.Machines.MachineLauncher.Launch(source.Profile.Machine, source.Files, member, romDirectory, source.Folder).Machine;
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{source.Profile.Name}: {e.Message}", e);
        }
    }

    /// <summary>
    /// Runs the profile's boot script as fast as possible: tapes load through the ROM
    /// (real signal), keys are pressed for the given number of frames.
    /// </summary>
    public static void Boot(IMachine m, GameProfile profile, Action<string>? log = null, CancellationToken ct = default)
    {
        foreach (var step in profile.Boot)
        {
            ct.ThrowIfCancellationRequested();
            var parts = step.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "wait":
                    RunFrames(m, int.Parse(parts[1]), ct);
                    break;
                case "press":
                {
                    // "press JOY2 FIRE 6", "press CAPS SHIFT+5 4": several keys at once joined with +
                    int frames = int.Parse(parts[^1]);
                    var keys = string.Join(' ', parts[1..^1]).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var key in keys) m.SetKey(key, true);
                    try { RunFrames(m, frames, ct); }
                    finally { foreach (var key in keys) m.SetKey(key, false); }
                    break;
                }
                case "load-tape":
                    if (m is Spectrum48 zx && zx.Tape.IsLoaded)
                        Retro.Machines.MachineLauncher.LoadTape(zx, ct: ct);
                    break;
            }
            log?.Invoke($"{profile.Name}: {step}");
        }
        m.DrainAudio(new short[8192]);
    }

    private static void RunFrames(IMachine m, int n, CancellationToken ct)
    {
        var sink = new short[4096];
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            m.RunFrame(null);
            m.DrainAudio(sink);
        }
    }
}
