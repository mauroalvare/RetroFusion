using System.Text;
using Retro.Core;
using Retro.Fusion;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture]
public sealed class NativeCompositionTests
{
    private string _folder = "";
    private StudioGame _game = null!;
    private Dictionary<string, GameSource> _sources = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "retro-composition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder); File.WriteAllBytes(Path.Combine(_folder, "48.rom"), new byte[16384]);
        _sources = new();
        foreach (string id in new[] { "a", "b" })
        {
            var data = new byte[30 + 49152]; data[7] = 0x80; data[8] = 0xFE; data[9] = 0xFF;
            Array.Fill(data, id == "a" ? (byte)0xAA : (byte)0xF0, 30, 6144);
            Array.Fill(data, id == "a" ? (byte)0x42 : (byte)0x44, 30 + 6144, 768);
            var profile = new GameProfile { Id = "own-" + id + Guid.NewGuid().ToString("N"), Name = "Original " + id, Machine = "zx48", Boot = ["wait 1"] };
            _sources[profile.Id] = new GameSource(profile, [new MediaFile(id + ".z80", data, "own-test")], null);
        }
        _game = new StudioGame { BaseSlot = "b", Games = _sources.Select((s, i) => new RecipeGame { Slot = i == 0 ? "a" : "b", Profile = s.Key }).ToList() };
    }

    [TearDown]
    public void CleanUp() => Directory.Delete(_folder, true);

    private StudioSession Start(string drawing)
    {
        _game.Script = "function init(){ games.a.pause(); games.b.pause(); game.state('playing'); games.a.pixel(0,0); } " + drawing;
        var session = StudioSession.Start(_game, _sources, _folder);
        Assert.That(session.Slots.All(s => s.Machine.Video.Pixels.Distinct().Count() > 1), Is.True, "los originales propios deben haber producido una imagen real");
        return session;
    }

    [Test]
    public void NativeSceneIsRestoredEveryFrame_AndCannotBeChangedThroughTheCallersMetadata()
    {
        var session = Start("var n=0; function draw(){ if(n++===0) screen.rect(4,5,1,1,0x123456); }");
        _game.BaseSlot = "a";
        session.Update(1.0 / 60);
        Assert.That(session.Video.Pixels[5 * 256 + 4] & 0xFFFFFF, Is.EqualTo(0x123456));
        session.Update(1.0 / 60);
        Assert.That(session.Game.BaseSlot, Is.EqualTo("b"));
        Assert.That(session.Error, Is.Null);
        Assert.That(session.Video.Pixels[5 * 256 + 4] & 0xFFFFFF, Is.Not.EqualTo(0x123456), "la escena nativa se repone aunque el guion no llame a screen.base");
    }

    [TestCase("screen.clear(0);")]
    [TestCase("try { screen.clear(0); } catch(e) {}")]
    public void ABaseGameCannotReplaceTheSceneWithClear(string drawing)
    {
        var report = StudioValidator.Run(Start("function draw(){ " + drawing + " }"), 1);
        Assert.That(report.Ok, Is.False);
        Assert.That(report.Error + string.Join(" ", report.Problems), Does.Contain("screen.clear").And.Contain("base"));
    }

    [Test]
    public void PaintingOverTheWholeBaseIsAlsoRejectedWithoutCallingClear()
    {
        var report = StudioValidator.Run(Start("function draw(){ screen.rect(0,0,screen.width,screen.height,0); }"), 4);
        Assert.That(report.Ok, Is.False);
        Assert.That(report.Problems, Has.Some.Contains("tapa casi toda"));
    }

    [Test]
    public void CopyingAnotherCompleteScreenWithRectanglesDoesNotBypassTheBaseChoice()
    {
        var original = Start("");
        var other = original.Slots.Single(s => s.Id == "a").Machine;
        var script = CopyPicture(other.Video, 0);
        var report = StudioValidator.Run(Start(script), 4);
        Assert.That(report.Ok, Is.False);
        Assert.That(report.Problems, Has.Some.Contains("Original a").And.Some.Contains("pantalla completa"));
    }

    [Test]
    public void AColourChangeThatKeepsTheOriginalGeometryIsNotMistakenForAnErasedScene()
    {
        var original = Start("");
        var selected = original.Slots.Single(s => s.Id == "b").Machine;
        var report = StudioValidator.Run(Start(CopyPicture(selected.Video, 0x0000FF)), 1);
        Assert.That(report.Error, Is.Null);
        Assert.That(report.Problems, Has.None.Contains("tapa casi toda").And.None.Contains("otra pantalla"));
    }

    [Test]
    public void AShortTestWithoutDeathsDoesNotClaimThatTheGameIsTooDifficult()
    {
        var report = StudioValidator.Run(Start("function draw(){}"), 1);
        Assert.That(report.Robots!.All(r => r.FirstDeathSeconds == null), Is.True);
        Assert.That(report.Problems, Has.None.Contains("Demasiado difícil"));
    }

    private static string CopyPicture(VideoFrame frame, int recolor)
    {
        var text = new StringBuilder("function draw(){");
        int borderX = Retro.Systems.Spectrum.Spectrum48.BorderSide, borderY = Retro.Systems.Spectrum.Spectrum48.BorderTop;
        for (int y = 0; y < 192;)
        {
            int bottom = y + 1;
            var row = frame.Pixels.AsSpan((borderY + y) * frame.Width + borderX, 256);
            while (bottom < 192 && row.SequenceEqual(frame.Pixels.AsSpan((borderY + bottom) * frame.Width + borderX, 256))) bottom++;
            for (int x = 0; x < 256;)
            {
                int color = frame.Pixels[(borderY + y) * frame.Width + borderX + x] & 0xFFFFFF;
                int end = x + 1;
                while (end < 256 && (frame.Pixels[(borderY + y) * frame.Width + borderX + end] & 0xFFFFFF) == color) end++;
                text.Append($"screen.rect({x},{y},{end - x},{bottom - y},{(recolor != 0 && color != 0 ? recolor : color)});");
                x = end;
            }
            y = bottom;
        }
        return text.Append('}').ToString();
    }
}
