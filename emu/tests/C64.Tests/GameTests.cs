using Retro.Core;
using Retro.Systems.C64;

namespace Commodore.Tests;

/// <summary>
/// Real games from the user's ROMs folder (not distributed: skipped when missing), played
/// with scripted key presses through their cracker intros and menus.
/// </summary>
[TestFixture]
[Category("Juegos")]
public class GameTests
{
    private static C64 Load(string zip)
    {
        string? path = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null && path == null; dir = dir.Parent)
        {
            var roms = dir.EnumerateDirectories().FirstOrDefault(d => d.Name == "ROMs");
            if (roms != null && File.Exists(Path.Combine(roms.FullName, zip)))
                path = Path.Combine(roms.FullName, zip);
        }
        if (path == null)
            Assert.Ignore($"No está {zip} en la carpeta ROMs.");
        var file = MediaArchive.Open(path!).First(f => C64Media.Probe(f) > 0);
        var m = new C64(MachineTests.Firmware());
        C64Media.Load(m, file);
        return m;
    }

    /// <summary>Runs the given number of frames, pressing keys on the given frames.</summary>
    private static void Play(C64 m, int frames, params (int frame, string key, int hold)[] presses)
    {
        var sink = new short[8192];
        for (int f = 0; f < frames; f++)
        {
            foreach (var p in presses.Where(p => p.frame == f))
                m.QueueKeys(p.hold, p.key);
            m.RunFrame(null);
            m.DrainAudio(sink);
        }
    }

    private static string Text(C64 m) => m.ScreenText().Replace('@', ' ');

    [Test]
    public void PacMan_ReachesTheMaze()
    {
        var m = Load("Pac_Man_C64_ROM_EN.zip");
        Play(m, 3000, (400, "SPACE", 6), (900, "RUN/STOP", 6), (1100, "RUN/STOP", 6), (1300, "RUN/STOP", 6), (1500, "RUN/STOP", 6),
            (1700, "N", 6), (1850, "N", 6), (2000, "N", 6), (2150, "N", 6), (2600, "F1", 6));
        // the maze is made of characters: a row of dots (screen code 1) between walls
        Assert.That(m.ScreenText(), Does.Contain("AAAAAAAAAAAAAAAA"), m.ScreenText());
        Assert.That(m.UnhandledKernalCalls, Is.Empty);
    }

    [Test]
    public void Ghostbusters_StartsAFranchise()
    {
        var m = Load("Ghostbusters_C64_ROM_EN.zip");
        var presses = new List<(int, string, int)> { (1000, "SPACE", 6), (1500, "RUN/STOP", 6), (1700, "RUN/STOP", 6), (2300, "JOY2 FIRE", 6) };
        for (int f = 2700; f <= 3600; f += 150)
            presses.Add((f, "N", 10));                         // the trainer's questions
        presses.AddRange(new[] { (4200, "F1", 8), (5000, "R", 6), (5100, "E", 6), (5200, "T", 6), (5300, "RETURN", 6), (5500, "N", 6), (5700, "RETURN", 6) });
        Play(m, 6200, presses.ToArray());
        Assert.That(Text(m), Does.Contain("WELCOME TO YOUR NEW"), Text(m));
        Assert.That(m.UnhandledKernalCalls, Is.Empty);
    }

    [Test]
    public void SpyVsSpy_ShowsItsDocuments()
    {
        var m = Load("Spy_vs_Spy_C64_EN.zip");
        Play(m, 4200, (1500, "SPACE", 6));
        Assert.That(Text(m), Does.Contain("SPY VS SPY DOCUMENTS"), Text(m));
        Assert.That(m.UnhandledKernalCalls, Is.Empty);
    }
}
