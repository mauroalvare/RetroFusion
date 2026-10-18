using Retro.Core;
using Retro.Systems.Arcade;

namespace Arcade.Tests;

/// <summary>
/// Uses the user's MAME files when they are in a ROMs/ folder above the project (or in
/// RETRO_GAMES): the samples set (samples/invaders.zip) and the game ROM set
/// (roms/invaders.zip). Both are called invaders.zip, so every zip is inspected.
/// </summary>
[TestFixture]
[Category("Compat")]
public class InvadersCompatibilityTests
{
    private static IEnumerable<IReadOnlyList<MediaFile>> CandidateZips()
    {
        var dirs = new List<string>();
        var env = Environment.GetEnvironmentVariable("RETRO_GAMES");
        if (env != null && Directory.Exists(env))
            dirs.Add(env);
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "ROMs")))
                dirs.Add(Path.Combine(d.FullName, "ROMs"));
        foreach (var dir in dirs)
            foreach (var zip in Directory.GetFiles(dir, "*.zip"))
            {
                List<MediaFile> files;
                try { files = MediaArchive.Open(zip); }
                catch (InvalidDataException) { continue; }
                yield return files;
            }
    }

    private static byte[] IdleRom()
    {
        var rom = new byte[0x2000];
        new byte[] { 0x31, 0x00, 0x24, 0xC3, 0x03, 0x00 }.CopyTo(rom, 0);   // LXI SP / JMP $0003
        return rom;
    }

    [Test]
    public void MameSamples_ArePlayedExactlyWhenTriggered()
    {
        var samples = CandidateZips().FirstOrDefault(SpaceInvadersSound.IsSampleSet);
        if (samples == null)
            Assert.Ignore("No está el samples/invaders.zip de MAME en ROMs/.");

        var m = new SpaceInvaders(IdleRom());
        Assert.That(m.Sound.LoadSamples(samples!), Is.EqualTo(10), "0.wav a 9.wav");
        Assert.That(m.Sound.UsingSamples, Is.True);

        var buf = new short[4096];
        m.Out(3, 0x20);                                    // amplifier on
        m.RunFrame(null);
        m.DrainAudio(buf);

        m.Out(3, 0x22);                                    // shot (1.wav)
        var played = new List<short>();
        for (int i = 0; i < 25; i++)
        {
            m.RunFrame(null);
            int n = m.DrainAudio(buf);
            played.AddRange(buf.Take(n));
        }
        var wav = samples!.First(f => f.Name == "1.wav");
        short[] expected = WavReader.ReadMono(wav.Data, 44100);

        // The emitted sound must be the recording (scaled by the volume), starting at the
        // sample where the game wrote the port.
        double best = 0;
        for (int offset = 0; offset < 200; offset++)
        {
            double sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < expected.Length && i + offset < played.Count; i++)
            {
                double x = expected[i], y = played[i + offset];
                sxy += x * y; sxx += x * x; syy += y * y;
            }
            if (sxx > 0 && syy > 0)
                best = Math.Max(best, sxy / Math.Sqrt(sxx * syy));
        }
        Assert.That(best, Is.GreaterThan(0.98), "el audio emitido no es la grabación 1.wav");
        // After the recording ends there is silence again (one-shot, not looping).
        var tail = played.Skip(expected.Length + 400).ToList();
        Assert.That(tail.Count, Is.GreaterThan(0));
        Assert.That(tail.Max(x => Math.Abs((int)x)), Is.LessThan(50));
    }

    [Test]
    public void GameRom_RunsAttractModeAndAcceptsCoin()
    {
        var set = CandidateZips().FirstOrDefault(f => SpaceInvadersRom.Probe(f) == 100);
        if (set == null)
            Assert.Ignore("No está el roms/invaders.zip de MAME (invaders.e/f/g/h) en ROMs/.");
        var (rom, description) = SpaceInvadersRom.Build(set!);
        Assert.That(description, Does.Contain("verificada"));
        var m = new SpaceInvaders(rom);
        for (int i = 0; i < 600; i++)
            m.RunFrame(null);
        int lit = m.Video.Pixels.Count(p => p != unchecked((int)0xFF000000));
        Assert.That(lit, Is.GreaterThan(500), "la pantalla de demostración está vacía");
        // Credits are kept in RAM at $20EB (BCD): a coin adds one.
        byte before = m.Peek(0x20EB);
        m.QueueKeys(6, "COIN");
        for (int i = 0; i < 60; i++)
            m.RunFrame(null);
        Assert.That(m.Peek(0x20EB), Is.EqualTo((byte)(before + 1)), "la moneda no sumó un crédito");
    }
}
