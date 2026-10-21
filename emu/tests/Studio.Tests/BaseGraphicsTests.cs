using Retro.Studio;

namespace Studio.Tests;

public partial class BaseFusionTests
{
    [Test]
    public void CapturedGraphicKeepsItsColoursAndTransparency_WithoutReplacingTheBase()
    {
        var game = Original(); game.BaseSlot = "b";
        game.Script = """
            var spr;
            function init() { games.a.pause(); games.b.pause(); }
            function tick() { if (spr === undefined) spr = games.a.capture(10, 20, 3, 1, 0); }
            function draw() { screen.base(games.b); screen.sprite(spr, 40, 30); screen.sprite(spr, 50, 30, 0xFFFF00); }
            """;
        var session = Start(game);
        var source = session.Slots.Single(s => s.Id == "a").Machine.Video;
        source.Pixels[20 * source.Width + 10] = unchecked((int)0xFFFF0000);
        source.Pixels[20 * source.Width + 11] = unchecked((int)0xFF0000FF);
        source.Pixels[20 * source.Width + 12] = unchecked((int)0xFF000000);
        session.Update(1.0 / 60);
        Assert.That(session.Error, Is.Null);
        Assert.That(session.Video.Pixels[30 * 256 + 40] & 0xFFFFFF, Is.EqualTo(0xFF0000));
        Assert.That(session.Video.Pixels[30 * 256 + 41] & 0xFFFFFF, Is.EqualTo(0x0000FF));
        var native = session.Slots.Single(s => s.Id == "b").Machine.Video;
        int basePixel = native.Pixels[(Retro.Systems.Spectrum.Spectrum48.BorderTop + 30) * native.Width
            + Retro.Systems.Spectrum.Spectrum48.BorderSide + 42];
        Assert.That(session.Video.Pixels[30 * 256 + 42], Is.EqualTo(basePixel));
        Assert.That(session.Video.Pixels[30 * 256 + 50] & 0xFFFFFF, Is.EqualTo(0xFFFF00));
        source.Pixels[20 * source.Width + 10] = 0;
        session.Update(1.0 / 60);
        Assert.That(session.Video.Pixels[30 * 256 + 40] & 0xFFFFFF, Is.EqualTo(0xFF0000), "el recurso guarda una copia independiente");
    }

    [TestCase("games.a.capture(0,0,224,256);")]
    [TestCase("for(var n=0;n<5;n++) games.a.capture(0,0,32,32);")]
    [TestCase("games.a.capture(-1,0,8,8);")]
    public void LargeOrTiledGameScreensCannotBeSmuggledAsCapturedSprites(string script)
    {
        var game = Original(); game.BaseSlot = "b";
        game.Script = "function init(){ games.a.pause(); games.b.pause(); game.state('playing'); try { " + script
            + " } catch(e) {} } function draw(){ screen.base(games.b); }";
        var report = StudioValidator.Run(Start(game), 1);
        Assert.That(report.Ok, Is.False);
        Assert.That(report.Error + string.Join(" ", report.Problems), Does.Contain("g.capture"), report.Summary);
    }

    [Test]
    public void RestartReleasesCapturedResourcesAndTheirPixelBudget()
    {
        var game = Original(); game.BaseSlot = "b";
        game.Script = "var s; function init(){ games.a.pause(); games.b.pause(); for(var n=0;n<4;n++) s=games.a.capture(0,0,32,32); } "
            + "function draw(){ screen.base(games.b); screen.sprite(s,0,0); }";
        var session = Start(game);
        for (int n = 0; n < 5; n++)
        {
            session.Restart(); session.Update(1.0 / 60);
            Assert.That(session.Error, Is.Null);
        }
    }
}
