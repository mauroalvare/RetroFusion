using Retro.Core;
using Retro.Systems.Spectrum;

namespace Retro.Systems.Spectrum.Tests;

/// <summary>
/// Real games, loaded from tape through the ROM loader and played with simulated key
/// presses. The games are not part of the repository: put them in a ROMs/ folder next
/// to the project (or point RETRO_GAMES at a folder); otherwise these tests are skipped.
/// </summary>
[TestFixture]
[Category("Compat")]
public class GameCompatibilityTests
{
    /// <summary>RETRO_GAMES, or every ROMs/ folder found walking up from the test binaries.</summary>
    private static IEnumerable<string> GameDirs()
    {
        var env = Environment.GetEnvironmentVariable("RETRO_GAMES");
        if (env != null && Directory.Exists(env))
            yield return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, "ROMs");
            if (Directory.Exists(p))
                yield return p;
            dir = dir.Parent;
        }
    }

    private static MediaFile? FindGame(string pattern, string extension, string subfolder = "")
    {
        foreach (var dir in GameDirs().Select(d => Path.Combine(d, subfolder)).Where(Directory.Exists))
            foreach (var path in Directory.GetFiles(dir, pattern))
                foreach (var f in MediaArchive.Open(path).OrderBy(f => MediaArchive.DumpScore(f.Name)))
                    if (f.Extension == extension && SpectrumMedia.Probe(f) > 0)
                        return f;
        return null;
    }

    private static Spectrum48 NewMachine()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        return new Spectrum48(File.ReadAllBytes(Path.Combine(dir!.FullName, "roms", "48.rom")));
    }

    private static void Run(Spectrum48 m, int frames, short[]? audio = null, List<short>? sink = null)
    {
        audio ??= new short[4096];
        for (int i = 0; i < frames; i++)
        {
            m.RunFrame(null);
            int n = m.DrainAudio(audio);
            sink?.AddRange(audio.AsSpan(0, n).ToArray());
        }
    }

    private static void LoadFromTape(Spectrum48 m, MediaFile game)
    {
        SpectrumMedia.Load(m, game);
        Run(m, 150);
        m.TypeLoadCommand();
        for (int i = 0; i < 20000 && !m.Tape.AtEnd; i++)
            Run(m, 1);
        Assert.That(m.Tape.AtEnd, Is.True, "la cinta no terminó de cargar");
    }

    private static double Rms(List<short> s) => Math.Sqrt(s.Average(x => (double)x * x));

    /// <summary>Mean column of white-ink cells with pixels in the given character rows.</summary>
    private static double WhiteCentroid(Spectrum48 m, int firstRow, int lastRow)
    {
        double sum = 0;
        int count = 0;
        for (int row = firstRow; row <= lastRow; row++)
            for (int col = 0; col < 32; col++)
            {
                byte attr = m.Peek((uint)(0x5800 + row * 32 + col));
                if ((attr & 7) != 7)
                    continue;
                for (int line = 0; line < 8; line++)
                {
                    int y = row * 8 + line;
                    int addr = 0x4000 | ((y & 0xC0) << 5) | ((y & 0x07) << 8) | ((y & 0x38) << 2) | col;
                    if (m.Peek((uint)addr) != 0)
                    {
                        sum += col;
                        count++;
                    }
                }
            }
        return count == 0 ? -1 : sum / count;
    }

    [Test]
    public void ManicMiner_LoadsFromTapeAndIsPlayable()
    {
        var game = FindGame("*anic*iner*", ".tzx") ?? FindGame("*anic*iner*", ".tap");
        if (game == null)
            Assert.Ignore("Manic Miner no está en la carpeta ROMs/.");
        var m = NewMachine();
        LoadFromTape(m, game!);

        // The title text is drawn with the game's own graphics (not the ROM font),
        // so check the title tune instead: the beeper must be playing notes.
        var titleAudio = new List<short>();
        Run(m, 150, sink: titleAudio);
        Assert.That(Rms(titleAudio), Is.GreaterThan(1000), "la música del título no suena");

        m.QueueKeys(40, "ENTER");
        Run(m, 100);
        string cavern = ScreenText.ReadAll(m);
        Assert.That(cavern, Does.Contain("Central Cavern"), cavern);
        Assert.That(cavern, Does.Contain("AIR"));
        Assert.That(cavern, Does.Contain("Score"));

        // Miner Willy starts bottom left; holding P walks him right.
        double before = WhiteCentroid(m, 10, 15);
        m.QueueKeys(60, "P");
        Run(m, 70);
        double after = WhiteCentroid(m, 10, 15);
        Assert.That(before, Is.GreaterThanOrEqualTo(0), "no se encontró a Willy");
        Assert.That(after, Is.GreaterThan(before + 2), $"Willy no se movió a la derecha ({before:F1} -> {after:F1})");

        // ...and O walks him back left.
        m.QueueKeys(40, "O");
        Run(m, 50);
        double back = WhiteCentroid(m, 10, 15);
        Assert.That(back, Is.LessThan(after - 1), $"Willy no volvió a la izquierda ({after:F1} -> {back:F1})");
    }

    /// <summary>Its turbo loader waits for the edge that closes each block; without it the game reset after loading.</summary>
    [Test]
    public void GalaxianAtarisoft_TurboTapeStartsTheGame()
    {
        var game = FindGame("galaxian*", ".tzx", Path.Combine("clasicos", "Spectrum"));
        if (game == null)
            Assert.Ignore("Galaxian (.tzx) no está en la carpeta ROMs/.");
        var m = NewMachine();
        LoadFromTape(m, game!);
        Run(m, 100);
        Assert.That(m.Z80.PC, Is.GreaterThanOrEqualTo(0x4000), $"volvió a la ROM (PC={m.Z80.PC:X4}): el cargador falló");
        Assert.That(ScreenText.ReadAll(m), Does.Not.Contain("1982 Sinclair"));
    }

    [Test]
    public void PacMan_LoadsFromTapeAndRunsDemo()
    {
        var game = FindGame("*Pac*Spectrum*", ".tap");
        if (game == null)
            Assert.Ignore("Pac-Man de Spectrum no está en la carpeta ROMs/.");
        var m = NewMachine();
        LoadFromTape(m, game!);
        Run(m, 400);
        // The maze is drawn in blue ink: count blue cells.
        int blue = 0;
        for (int i = 0; i < 768; i++)
            if ((m.Peek((uint)(0x5800 + i)) & 7) == 1)
                blue++;
        Assert.That(blue, Is.GreaterThan(150), "no se ve el laberinto");
        // The attract mode keeps moving: the picture must change over time.
        // (PC is often inside the ROM: the game calls the BEEPER routine for its sounds.)
        int[] first = (int[])m.Video.Pixels.Clone();
        Run(m, 25);
        Assert.That(m.Video.Pixels, Is.Not.EqualTo(first), "la demo no se mueve");
    }

    [Test]
    public void TapeLoadingNoise_HasPilotTone()
    {
        var game = FindGame("*anic*iner*", ".tzx") ?? FindGame("*Pac*Spectrum*", ".tap");
        if (game == null)
            Assert.Ignore("No hay cintas de prueba en ROMs/.");
        var m = NewMachine();
        SpectrumMedia.Load(m, game!);
        Run(m, 150);
        m.TypeLoadCommand();
        var audio = new List<short>();
        Run(m, 220, sink: audio);          // header pilot tone is playing by now
        var last = audio.Skip(audio.Count - 8820).ToList();   // 0.2 s
        int crossings = 0;
        for (int i = 1; i < last.Count; i++)
            if ((last[i - 1] < 0) != (last[i] < 0))
                crossings++;
        double freq = crossings / 2.0 / 0.2;
        // 3.5 MHz / (2 x 2168 T-states) = 807 Hz
        Assert.That(freq, Is.EqualTo(807).Within(20), "frecuencia del tono guía");
    }
}
