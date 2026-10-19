using Retro.Core;
using Retro.Fusion;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture]
[Category("Compat")]
public class StudioGameTests
{
    private static (Dictionary<string, GameSource> sources, string romDir) Games() =>
        TestGames.Find() ?? throw new IgnoreException("Hacen falta roms/48.rom, invaders.zip y Manic Miner en ROMs/.");

    private static StudioGame ManicInvaders() => StudioLibrary.BuiltIn.Single(g => g.Id == "manic-invaders");

    [TestCase("screen.game(games.b);")]
    [TestCase("screen.game(games.b, { scale: 0.1, alpha: 0.1 });")]
    [TestCase("try { screen.game(games.b); } catch (e) {}")]
    [TestCase("__h.RejectGamePicture();")]
    public void Playtest_RejectsOriginalScreensEvenWhenTheScriptCatchesTheError(string drawing)
    {
        var (sources, romDir) = Games();
        var game = ManicInvaders().Copy();
        game.Script = "function init() { game.state('title'); game.score(0); }\n" +
                      "function draw() { " + drawing + " }";
        var report = StudioValidator.Run(game, sources, romDir, 1);
        Assert.That(report.Ok, Is.False);
        Assert.That(report.Error + string.Join(" ", report.Problems), Does.Contain("escena").And.Contain("sprites y tiles"));
    }

    [Test]
    public void ManicInvaders_PassesThePlaytest()
    {
        var (sources, romDir) = Games();
        var report = StudioValidator.Run(ManicInvaders(), sources, romDir);
        TestContext.Out.WriteLine(report.Summary);
        Assert.That(report.Error, Is.Null);
        Assert.That(report.Ok, Is.True, string.Join("; ", report.Problems));
        Assert.That(report.Robots!.Single(r => r.Name == "tirador").Score, Is.GreaterThan(0), "disparando se suman puntos");
        Assert.That(report.Shots!.Select(s => s.Label), Does.Contain("titulo"));
    }

    [Test]
    public void PacManMina_IsRejectedUntilItHasItsOwnScene()
    {
        var (sources, romDir) = Games();
        if (!sources.ContainsKey("pacman-c64"))
            Assert.Ignore("Hace falta Pac_Man_C64_ROM_EN.zip en ROMs/.");
        var report = StudioValidator.Run(StudioLibrary.BuiltIn.Single(g => g.Id == "pacman-mina"), sources, romDir);
        TestContext.Out.WriteLine(report.Summary);
        Assert.That(report.Ok, Is.False);
        Assert.That(report.Error, Does.Contain("escena").And.Contain("screen.game"));
    }

    [Test]
    public void ManicInvaders_StartsOnTheTitleAndUsesTheOriginalCaverns()
    {
        var (sources, romDir) = Games();
        var s = StudioSession.Start(ManicInvaders(), sources, romDir);
        for (int t = 0; t < 30; t++) s.Update(1.0 / 60);
        Assert.That(s.State, Is.EqualTo("title"));
        s.SetInput("start", true);
        s.Update(1.0 / 60);
        s.SetInput("start", false);
        for (int t = 0; t < 120; t++) s.Update(1.0 / 60);
        Assert.That(s.State, Is.EqualTo("playing"));
        Assert.That(s.Level, Is.EqualTo(1));
        Assert.That(s.Hud, Does.Contain("Central Cavern"));
        Assert.That(s.Video.Width, Is.EqualTo(256));
    }

    [Test]
    public void ManicInvaders_ShootingFromBelowHitsTheFleet()
    {
        var (sources, romDir) = Games();
        var s = StudioSession.Start(ManicInvaders(), sources, romDir);
        s.SetInput("start", true);
        s.Update(1.0 / 60);
        s.SetInput("start", false);
        var inv = s.Slots.Single(x => x.Id == "a");
        for (int t = 0; t < 60 * 20; t++)
        {
            s.SetInput("fire", t % 14 < 2);
            s.SetInput("right", t / 150 % 2 == 0);
            s.SetInput("left", t / 150 % 2 == 1);
            s.Update(1.0 / 60);
            Assert.That(s.Error, Is.Null);
        }
        TestContext.Out.WriteLine($"puntos {s.Score}; {s.Hud}");
        Assert.That(s.Score, Is.GreaterThan(0), "el láser atraviesa los pisos y derriba invasores");
        Assert.That(inv.Profile.Variables["ships"].Read(inv.Machine.Memory), Is.GreaterThan(0), "el cañón oculto de Invaders nunca muere");
    }

    [Test]
    public void ScriptErrors_AreReportedWithTheLine()
    {
        var (sources, romDir) = Games();
        var game = ManicInvaders().Copy();
        game.Script = "function init() { screen.size(256, 192); }\nfunction tick(input) {\n  games.b.control('volar', true);\n}\n";
        var report = StudioValidator.Run(game, sources, romDir, 2);
        TestContext.Out.WriteLine(report.Error);
        Assert.That(report.Error, Does.Contain("volar"));

        game.Script = "function tick(input) {\n  var x = ;\n}";
        report = StudioValidator.Run(game, sources, romDir, 2);
        TestContext.Out.WriteLine(report.Error);
        Assert.That(report.Error, Does.Contain("línea 2"));

        game.Script = "function tick(input) { while (true) {} }";
        report = StudioValidator.Run(game, sources, romDir, 2);
        TestContext.Out.WriteLine(report.Error);
        Assert.That(report.Error, Does.Contain("bucle"));
    }

    [Test]
    public void Playtest_CatchesAGameWithoutStructure()
    {
        var (sources, romDir) = Games();
        var game = ManicInvaders().Copy();
        // two games pasted one over the other: no title, no score, no states
        game.Script = "function draw() { screen.clear(0); }";
        var report = StudioValidator.Run(game, sources, romDir, 5);
        TestContext.Out.WriteLine(report.Summary);
        Assert.That(report.Ok, Is.False);
        Assert.That(string.Join(" ", report.Problems), Does.Contain("game.state"));
        Assert.That(string.Join(" ", report.Problems), Does.Contain("game.score"));
    }
}
