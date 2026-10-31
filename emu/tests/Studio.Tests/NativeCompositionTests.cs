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
    public void AScriptCanRequestAWholeRestartAfterItsTickWithoutOldInputsObjectsOrAudio()
    {
        _game.Script = "var shots=0; function init(){games.a.pause();games.b.pause();game.state('playing');ui.hud('init');} " +
            "function tick(input){if(input.start){game.score(10);game.death('prueba');sound.beep(440,500,1);shots++;game.restart();return;}ui.hud(String(shots));} function draw(){}";
        var session = StudioSession.Start(_game, _sources, _folder);
        byte[][] states = session.Slots.Select(s => s.Machine.SaveState()).ToArray();
        session.SetInput("start", true); session.Update(1.0 / 60);
        Assert.That(session.Error, Is.Null);
        Assert.That(session.Tick, Is.Zero);
        Assert.That(session.Score, Is.Zero);
        Assert.That(session.Deaths, Is.Empty);
        Assert.That(session.Hud, Is.EqualTo("init"));
        Assert.That(session.DrainAudio(new short[2048]), Is.Zero);
        for (int i = 0; i < states.Length; i++) Assert.That(session.Slots[i].Machine.SaveState(), Is.EqualTo(states[i]));
        session.Update(1.0 / 60);
        Assert.That(session.Tick, Is.EqualTo(1), "The previous held Start must not cause another restart");
        Assert.That(session.Hud, Is.EqualTo("0"), "The JavaScript heap must be fresh");
        Assert.That(session.DrainAudio(new short[2048]), Is.EqualTo(735));
    }

    [Test]
    public void TheRevengeDoesNotEraseTheRobotsActualDeathsAndScore()
    {
        _game.Script = "var n=0; function init(){games.a.pause();games.b.pause();games.a.peek(0);game.state('playing');} " +
            "function tick(input){n++;if(n===200){game.score(7);game.death('prueba');game.state('gameover');}if(n>200&&input.start){game.restart();return;}}function draw(){}";
        var report = StudioValidator.Run(_game, _sources, _folder, 8);
        Assert.That(report.Robots, Has.Count.EqualTo(3));
        foreach (var robot in report.Robots!)
        {
            Assert.That(robot.Deaths, Is.EqualTo(1)); Assert.That(robot.Score, Is.EqualTo(7));
            Assert.That(robot.Causes["prueba"], Is.EqualTo(1)); Assert.That(robot.RestartWorked, Is.True);
        }
    }

    [TestCase(false)] [TestCase(true)]
    public void AShortNativeBlankTransitionIsDistinguishedFromAPermanentlyBlankScene(bool permanent)
    {
        string id = _game.Games.Single(s => s.Slot == "b").Profile;
        var data = _sources[id].Files[0].Data.ToArray(); data[6] = 0; data[7] = 0x90;
        data[30 + 0x5000] = 0xF3; data[30 + 0x5001] = 0x76; // DI; HALT: video RAM stays controlled.
        _sources[id] = new(_sources[id].Profile, [new MediaFile("own.z80", data, "own-test")], null);
        _game.Script = "var n=0;function init(){games.a.pause();games.b.resume();games.a.peek(0);game.state('playing');} " +
            "function tick(){n++;if(n===180)for(var i=0;i<6144;i++)games.b.poke(0x4000+i,0);" +
            (permanent ? "" : "if(n===300)for(var i=0;i<6144;i++)games.b.poke(0x4000+i,0xF0);") + "}function draw(){}";
        var report = StudioValidator.Run(_game, _sources, _folder, 8);
        Assert.That(report.Problems.Any(p => p.Contains("solo color")), Is.EqualTo(permanent), report.Summary);
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

    [Test]
    public void RestartRestoresTheNativePictureAndResolutionBeforeInitRuns()
    {
        var rom = new byte[0x2000];
        // MVI A,FF; STA 2400; HLT: an original test program draws eight pixels.
        new byte[] { 0x3E, 0xFF, 0x32, 0x00, 0x24, 0x76 }.CopyTo(rom, 0);
        var profile = new GameProfile { Id = "own-arcade-" + Guid.NewGuid().ToString("N"), Machine = "invaders", Boot = ["wait 1"] };
        var game = new StudioGame { BaseSlot = "a", Games = [new() { Slot = "a", Profile = profile.Id }],
            Script = "function init(){ games.a.pause(); log(screen.width + 'x' + screen.height); game.state('playing'); }" };
        var source = new GameSource(profile, [new MediaFile("own-invaders.bin", rom, "own-test")], null);
        var session = StudioSession.Start(game, new Dictionary<string, GameSource> { [profile.Id] = source }, _folder);
        Assert.That(session.Log.Last(), Is.EqualTo("224x256"));
        int[] original = session.Video.Pixels.ToArray();
        Assert.That(original.Distinct().Count(), Is.GreaterThan(1));

        session.Restart();

        Assert.Multiple(() =>
        {
            Assert.That(session.Error, Is.Null);
            Assert.That(session.Log.Last(), Is.EqualTo("224x256"), "init must see the same native canvas at every start");
            Assert.That(session.Video.Width, Is.EqualTo(224));
            Assert.That(session.Video.Height, Is.EqualTo(256));
            Assert.That(session.Video.Pixels, Is.EqualTo(original), "the native picture must be present before the first update");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AGameWithoutPointsIsNotRejectedOrGivenInventedScoringRequirements(bool reportsZero)
    {
        _game.Kind = "inspired";
        _game.BaseSlot = null;
        _game.Script = """
            var x = 80, actor;
            function init(){
                games.a.pause(); games.b.pause(); actor=games.a.capture(0,0,8,8);
                game.state('title');
            }
            function tick(i){
                if(i.start || i.fire || i.jump) game.state('playing');
                if(i.left) x=clamp(x-1,0,240);
                if(i.right) x=clamp(x+1,0,240);
                game.level(Math.floor(x/16)+1);
            }
            function draw(){ screen.clear(0); screen.sprite(actor,x,60); }
            """ + (reportsZero ? "\nfunction scoreNothing(){ game.score(0); } scoreNothing();" : "");

        var report = StudioValidator.Run(_game, _sources, _folder, 4);

        Assert.Multiple(() =>
        {
            Assert.That(report.Ok, Is.True, report.Summary);
            Assert.That(report.Problems, Has.None.Contains("puntos").And.None.Contains("game.score"));
            Assert.That(report.Notes, Has.Some.Contains("puntaje"));
            Assert.That(report.Summary, Does.Contain("prueba técnica").And.Contain("no certifica"));
            Assert.That(report.Summary, Does.Not.Contain("Pasó la prueba automática de jugabilidad"));
        });
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
    [Test]
    public void CancelledPlaytestDoesNotTurnCancellationIntoAStartupFailureReport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            StudioValidator.Run(_game, _sources, _folder, ct: cancellation.Token));
    }

    [Test]
    public async Task CancellingAnActiveRobotStopsThePlaytestAndReleasesItsInputs()
    {
        var session = Start("""
            function init() { games.a.pause(); games.b.pause(); game.state('title'); }
            function tick(input) {
                if (input.fire || input.jump || input.start) game.level(42);
                game.score(input.left || input.right || input.up || input.down || input.fire || input.jump || input.start ? 1 : 0);
            }
            """);
        using var cancellation = new CancellationTokenSource();
        var work = Task.Run(() => StudioValidator.Run(session, 3600, cancellation.Token));
        try
        {
            Assert.That(SpinWait.SpinUntil(() => session.Level == 42 || work.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(work.IsCompleted, Is.False, "Cancel during actual robot activity, before validation finishes");
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await work.WaitAsync(TimeSpan.FromSeconds(3)));
            long score = session.Score;
            session.Update(1.0 / 60);
            Assert.That(session.Score, Is.EqualTo(score), "The canceled robot must not leave keys held in a reusable session");
        }
        finally
        {
            cancellation.Cancel();
            try { await work; } catch (OperationCanceledException) { }
        }
    }

}
