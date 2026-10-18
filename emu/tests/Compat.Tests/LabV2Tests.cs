using Retro.Compat;

namespace Compatibility.Tests;

/// <summary>
/// The lab's experiments and automatic verification against a tiny C64 game whose
/// variables are known: a BCD score that fire adds to and the screen shows, a player
/// position (shown as a sprite) with a copy the game rewrites every frame, lives, a timer
/// and four enemies in a table of 4-byte records.
/// </summary>
[TestFixture]
public class LabV2Tests
{
    private string _games = "";
    private Retro.Core.IMachine _m = null!;

    [SetUp]
    public void SetUp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms.");
        _games = Path.Combine(Path.GetTempPath(), "retro-lab2-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_games);
        File.WriteAllBytes(Path.Combine(_games, "juego.prg"), C64TestProgram.Game());
        var runner = new CompatRunner(_games, Path.Combine(dir!.FullName, "roms"), null);
        var (m, _, problem) = runner.Start(new CompatScript { Id = "j", Machine = "c64", File = "juego.prg", Frames = 20 });
        Assert.That(m, Is.Not.Null, problem);
        _m = m!;
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_games, true);

    private static readonly LabExperiment Shooting = new()
    {
        Kind = "score", Description = "disparar cada 20 cuadros", Frames = 400, RepeatEvery = 20,
        Steps = { new CompatStep { Frame = 0, Keys = "JOY2 FIRE", Hold = 4 } },
    };

    [Test]
    public void ScoreExperiment_FindsTheBcdScore_AndVerifiesItOnScreen()
    {
        var found = LabExperiments.Analyze(_m, "c64", Shooting);
        var score = found.FirstOrDefault(f => f.Address == "C020")
            ?? throw new AssertionException("no encontró el puntaje: " + string.Join("; ", found.Select(f => $"{f.Address} {f.Encoding}")));
        Assert.That((score.Encoding, score.Bytes), Is.EqualTo(("bcd", 2)));
        Assert.That(score.Evidence[0], Does.Contain("de 0 a 200"));

        var v = LabVerifier.Verify(_m, score, Shooting);
        Assert.That(v.Result, Is.EqualTo("verified"), string.Join("; ", v.Observations));
        Assert.That(v.Observations, Has.Some.Contains("el texto de la pantalla muestra"));
        Assert.That(v.Observations, Has.Some.Contains("siguió cambiándolo"));

        // the digits on the screen also go up, but the game redraws them from the real score
        var shown = found.FirstOrDefault(f => f.Address == "0400" && f.Encoding == "ascii");
        Assert.That(shown, Is.Not.Null, "los dígitos de la pantalla también son candidatos");
        Assert.That(LabVerifier.Verify(_m, shown!, Shooting).Result, Is.EqualTo("refuted"));
    }

    [Test]
    public void Verification_TellsTheStoredPositionFromTheCopyTheGameRewrites()
    {
        var report = MemoryLab.Analyze(_m, "c64", new MemoryLab.Options(IdleFrames: 1300));
        var x = report.Findings.First(f => f.Kind == "position-x" && f.Address == "C001");
        Assert.That(LabVerifier.Verify(_m, x).Result, Is.EqualTo("verified"), string.Join("; ", x.Verification!.Observations));
        Assert.That(x.Verification!.PixelsChanged, Is.GreaterThan(0), "el sprite se mueve");

        var copy = new LabFinding { Kind = "position-x", Address = "C005" };
        Assert.That(LabVerifier.Verify(_m, copy).Result, Is.EqualTo("refuted"));
        Assert.That(copy.Nature, Is.EqualTo("derived"));

        var lives = report.Findings.First(f => f.Kind == "lives" && f.Address == "C002");
        Assert.That(LabVerifier.Verify(_m, lives).Result, Is.EqualTo("verified"), string.Join("; ", lives.Verification!.Observations));
        Assert.That(lives.Verification!.Observations, Has.Some.Contains("siguió cambiándolo"), "el juego descuenta desde el valor escrito");
    }

    [Test]
    public void EntityTable_IsToldApartFromTheLooseVariable()
    {
        var report = MemoryLab.Analyze(_m, "c64", new MemoryLab.Options(IdleFrames: 1300));
        var table = report.Findings.FirstOrDefault(f => f.Kind == "entity-table" && f.Address == "C010")
            ?? throw new AssertionException("no encontró la tabla: " + string.Join("; ", report.Findings.Where(f => f.Kind == "entity-table").Select(f => $"{f.Address} x{f.Count}/{f.Stride}")));
        Assert.That((table.Stride, table.Count), Is.EqualTo((4, 4)));
        Assert.That(report.Findings.Count(f => f.Kind == "entity-table"), Is.EqualTo(1), "una sola tabla");
        var x = report.Findings.First(f => f.Kind == "position-x" && f.Address == "C001");
        Assert.That(x.Evidence, Has.Some.Contains("variable suelta"));

        LabVerifier.VerifyBest(_m, report);
        Assert.That(table.Status, Is.EqualTo("verified"), string.Join("; ", table.Verification!.Observations));
    }
}
