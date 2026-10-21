using Retro.Fusion;
using Retro.Studio;
using Retro.Systems.Spectrum;

namespace Studio.Tests;

[TestFixture]
public class BaseFusionTests
{
    private static StudioGame Original() => StudioLibrary.BuiltIn.Single(g => g.Id == "manic-invaders").Copy();
    private static StudioSession Start(StudioGame game)
    {
        var found = TestGames.Find() ?? throw new IgnoreException("Hacen falta los originales para la prueba nativa.");
        return StudioSession.Start(game, found.sources, found.romDir);
    }

    [Test]
    public void OldGamesKeepTheirRenderingMode_AndTheChosenBaseSurvivesCopying()
    {
        var game = Original();
        Assert.That(game.BaseSlot, Is.Null);
        game.BaseSlot = "b";
        Assert.That(game.Copy().BaseSlot, Is.EqualTo("b"));
        game.BaseSlot = "missing";
        Assert.Throws<InvalidDataException>(() => game.Copy());
        Assert.That(new FusionDirection().Resolve(Original().Games), Is.EqualTo("a"));
        Assert.That(new FusionDirection("b").Resolve(Original().Games), Is.EqualTo("b"));
        Assert.That(new FusionDirection(Reimagine: true).Resolve(Original().Games), Is.Null);
    }

    [TestCase("a")]
    [TestCase("b")]
    public void NativeBase_KeepsEveryActivePixelAndItsResolution_AfterRestart(string selected)
    {
        var game = Original();
        game.BaseSlot = selected;
        game.Script = $"function init() {{ games.a.pause(); games.b.pause(); game.state('playing'); }} function draw() {{ screen.base(games.{selected}); }}";
        var session = Start(game);
        for (int pass = 0; pass < 2; pass++)
        {
            session.Update(1.0 / 60);
            Assert.That(session.Error, Is.Null);
            var native = session.Slots.Single(s => s.Id == selected).Machine.Video;
            int x = selected == "b" ? Spectrum48.BorderSide : 0;
            int y = selected == "b" ? Spectrum48.BorderTop : 0;
            int width = selected == "b" ? 256 : native.Width;
            int height = selected == "b" ? 192 : native.Height;
            Assert.That(session.Video.Width, Is.EqualTo(width));
            Assert.That(session.Video.Height, Is.EqualTo(height));
            Assert.That(session.Video.Pixels.Distinct().Take(2).Count(), Is.EqualTo(2), "el arranque pausado debe conservar la imagen nativa, también desde caché");
            for (int row = 0; row < height; row++)
                Assert.That(session.Video.Pixels.AsSpan(row * width, width).ToArray(),
                    Is.EqualTo(native.Pixels.AsSpan((y + row) * native.Width + x, width).ToArray()));
            session.Restart();
        }
    }

    [Test]
    public void ASecondSessionRestoresTheBootPictureWithoutAdvancingTheOriginal()
    {
        var game = Original(); game.BaseSlot = "b";
        game.Script = "function init() { games.a.pause(); games.b.pause(); } function draw() { screen.base(games.b); }";
        var first = Start(game); first.Update(1.0 / 60);
        var picture = first.Video.Pixels.ToArray();
        var second = Start(game); second.Update(1.0 / 60);
        Assert.That(second.Video.Pixels, Is.EqualTo(picture));
        Assert.That(second.Video.Pixels.Distinct().Take(2).Count(), Is.EqualTo(2));
        Assert.That(second.Slots.Single(s => s.Id == "b").Machine.FrameCount,
            Is.EqualTo(first.Slots.Single(s => s.Id == "b").Machine.FrameCount));
    }

    [TestCase("screen.base(games.a);")]
    [TestCase("try { screen.base(games.a); } catch(e) {}")]
    [TestCase("__h.DrawBase('a');")]
    public void OnlyTheDeclaredBaseCanSupplyACompleteScreen_EvenIfTheErrorIsCaught(string drawing)
    {
        var game = Original(); game.BaseSlot = "b";
        game.Script = "function init() { game.state('playing'); } function draw() { " + drawing + " }";
        var report = StudioValidator.Run(Start(game), 1);
        Assert.That(report.Ok, Is.False);
        Assert.That(report.Error + string.Join(" ", report.Problems), Does.Contain("base").And.Contain("pantallas completas"));
    }

    [Test]
    public void ABaseGameMayStartPlayingWithoutInventingATitleOrScore()
    {
        var game = Original(); game.BaseSlot = "b";
        game.Script = """
            var alien, x = 30;
            function init() { alien = games.a.sprite('alienA'); games.a.pause(); game.state('playing'); }
            function tick(i) { games.b.control('left', i.left); games.b.control('right', i.right); x = 30 + i.tick % 80; }
            function draw() { screen.base(games.b); screen.sprite(alien, x, 30, 0xFFFFFF); }
            """;
        var report = StudioValidator.Run(Start(game), 10);
        Assert.That(report.Ok, Is.True, report.Summary);
        Assert.That(report.Notes, Has.Some.Contains("puntaje"));
        Assert.That(report.Problems, Has.None.Contains("título"));
    }

    [Test]
    public void ShowingOnlyTheBaseIsNotAcceptedAsAFusion()
    {
        var game = Original(); game.BaseSlot = "b";
        game.Script = "function init() { game.state('playing'); } function draw() { screen.base(games.b); }";
        var report = StudioValidator.Run(Start(game), 1);
        Assert.That(report.Ok, Is.False);
        Assert.That(report.Problems, Has.Some.Contains("games.a"));
    }

    [Test]
    public void RestartDoesNotAccumulateSpritesFromPreviousScriptInstances()
    {
        var game = Original(); game.BaseSlot = "b";
        game.Script = "function init() { for(var n=0;n<200;n++) games.a.sprite('alienA'); games.a.pause(); games.b.pause(); } function draw() { screen.base(games.b); }";
        var session = Start(game);
        for (int i = 0; i < 4; i++)
        {
            session.Restart(); session.Update(1.0 / 60);
            Assert.That(session.Error, Is.Null, "los sprites de la instancia anterior se liberan al reiniciar");
        }
    }
}
