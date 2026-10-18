using Retro.Compat;

namespace Compatibility.Tests;

/// <summary>The reverse engineering lab against a tiny C64 "game" whose variables are known.</summary>
[TestFixture]
public class LabTests
{
    /// <summary>
    /// Each frame (synchronised on raster line $F8): joystick left/right move $C001; every 8
    /// frames the timer $C000 goes down; when it reaches 0 a life ($C002) is lost and the
    /// timer is refilled. Interrupts off, so the KERNAL does not touch the joystick port.
    /// </summary>
    private static byte[] Game()
    {
        var c = new List<byte>();
        int Here() => 0x080D + c.Count;
        void E(params byte[] b) => c.AddRange(b);
        byte Rel(int target) => (byte)(target - (Here() + 2));
        E(0x78);                                  // SEI
        E(0xA9, 0xFF, 0x8D, 0x00, 0xDC);          // LDA #$FF / STA $DC00
        E(0xA9, 0x40, 0x8D, 0x00, 0xC0);          // timer = 64
        E(0xA9, 0x80, 0x8D, 0x01, 0xC0);          // x = 128
        E(0xA9, 0x03, 0x8D, 0x02, 0xC0);          // lives = 3
        int loop = Here();
        E(0xAD, 0x12, 0xD0, 0xC9, 0xF8); E(0xD0, Rel(loop));          // wait for line $F8
        int w2 = Here();
        E(0xAD, 0x12, 0xD0, 0xC9, 0xF8); E(0xF0, Rel(w2));            // and for it to pass
        E(0xEE, 0x03, 0xC0);                                           // frames++
        E(0xAD, 0x00, 0xDC, 0x29, 0x04); E(0xD0, 0x03); E(0xCE, 0x01, 0xC0);   // left: x--
        E(0xAD, 0x00, 0xDC, 0x29, 0x08); E(0xD0, 0x03); E(0xEE, 0x01, 0xC0);   // right: x++
        E(0xAD, 0x03, 0xC0, 0x29, 0x07); E(0xD0, Rel(loop));          // every 8 frames
        E(0xCE, 0x00, 0xC0); E(0xD0, Rel(loop));                       // timer--
        E(0xCE, 0x02, 0xC0, 0xA9, 0x40, 0x8D, 0x00, 0xC0);             // lives--, timer = 64
        E(0x4C, (byte)loop, (byte)(loop >> 8));
        var prg = new List<byte> { 0x01, 0x08, 0x0B, 0x08, 0x0A, 0x00, 0x9E, 0x32, 0x30, 0x36, 0x31, 0x00, 0x00, 0x00 };
        prg.AddRange(c);
        return prg.ToArray();
    }

    [Test]
    public void FindsTheTimerTheLivesAndThePosition_WithWriters()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms.");
        string games = Path.Combine(Path.GetTempPath(), "retro-lab-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(games);
        try
        {
            File.WriteAllBytes(Path.Combine(games, "juego.prg"), Game());
            var runner = new CompatRunner(games, Path.Combine(dir!.FullName, "roms"), null);
            var (m, sha, problem) = runner.Start(new CompatScript { Id = "j", Machine = "c64", File = "juego.prg", Frames = 20 });
            Assert.That(m, Is.Not.Null, problem);
            var report = MemoryLab.Analyze(m!, "c64", new MemoryLab.Options(IdleFrames: 1300));
            LabFinding Find(string kind, string addr) => report.Findings.FirstOrDefault(f => f.Kind == kind && f.Address == addr)
                ?? throw new AssertionException($"no encontró {kind} en ${addr}: " + string.Join("; ", report.Findings.Select(f => f.Kind + " " + f.Address)));
            var timer = Find("timer", "C000");
            var lives = Find("lives", "C002");
            var x = Find("position-x", "C001");
            Assert.That(lives.Evidence, Has.Some.Contains("se recargan"), "la vida perdida coincide con la recarga del tiempo");
            Assert.That(timer.Confidence, Is.GreaterThanOrEqualTo(0.7));
            Assert.That(x.Confidence, Is.GreaterThanOrEqualTo(0.75));
            Assert.That(x.Writers, Is.Not.Empty, "registra qué instrucción escribe la posición");
            Assert.That(sha, Has.Length.EqualTo(64));
        }
        finally
        {
            Directory.Delete(games, true);
        }
    }
}
