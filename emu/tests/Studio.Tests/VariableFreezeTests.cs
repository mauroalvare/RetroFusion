using Retro.Core;
using Retro.Fusion;
using Retro.Studio;
using Retro.Systems.Spectrum;

namespace Studio.Tests;

public class VariableFreezeTests
{
    private string _folder = "";
    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "retro-freeze-studio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder); File.WriteAllBytes(Path.Combine(_folder,"48.rom"), new byte[16384]);
    }
    [TearDown] public void Cleanup() => Directory.Delete(_folder, true);
    private StudioSession Start(string script, bool inspired = false)
    {
        byte[] snapshot = new byte[30 + 49152]; snapshot[6] = 0; snapshot[7] = 0x90;
        snapshot[8] = 0xFE; snapshot[9] = 0xFF;
        byte[] code = [0x21,0,0x80,0x34,0xC3,3,0x90];
        code.CopyTo(snapshot, 30 + 0x9000 - 0x4000);
        var profile = new GameProfile {Id="own-pin-studio",Machine="zx48", Boot=[],
            Variables=new(){["counter"]=new(){Address="8000"},["readonly"]=new(){Address="8001",ReadOnly=true},["other"]=new(){Address="8002"}}};
        var source = new GameSource(profile,[new MediaFile("own.z80",snapshot,"own")],null);
        var game = new StudioGame {Id="own-pin-game",Kind=inspired?"inspired":"fusion",Games=[new(){Slot="a",Profile=profile.Id}],Script=script};
        return StudioSession.Start(game,new Dictionary<string,GameSource>{{profile.Id,source}},_folder);
    }
    private static Spectrum48 Machine(StudioSession session) => (Spectrum48)session.Slots.Single().Machine;

    [TestCase(false)] [TestCase(true)]
    public void FusionAndInspiredGamesKeepPinsAcrossCpuWritesAndWholeRestart(bool inspired)
    {
        var session = Start("function init(){games.a.freeze('counter',17);} function draw(){}",inspired);
        for(int i=0;i<4;i++) {session.Update(1.0/60);Assert.That(session.Error,Is.Null);Assert.That(Machine(session).Memory.Peek(0x8000),Is.EqualTo(17));}
        session.Restart();session.Update(1.0/60);
        Assert.That(Machine(session).Memory.Peek(0x8000),Is.EqualTo(17)); Assert.That(session.Error,Is.Null);
    }

    [TestCase("games.a.release('counter');")] [TestCase("games.a.release();")]
    [TestCase("games.a.set('counter',23);")] [TestCase("games.a.restart();")]
    public void ReleasingSettingOrRestartingTheOriginalDropsItsPriorPin(string action)
    {
        var session = Start("var n=0;function init(){games.a.freeze('counter',17);}function tick(){if(++n===2){"+action+"}}function draw(){}");
        for(int i=0;i<3;i++)session.Update(1.0/60);
        Assert.That(session.Error,Is.Null);Machine(session).WriteMemory(0x8000,91);
        Assert.That(Machine(session).Memory.Peek(0x8000),Is.EqualTo(91));
    }

    [TestCase("games.a.set('readonly',3);")]
    [TestCase("games.a.freeze('readonly',3);")]
    [TestCase("games.a.freeze('counter',256);")]
    [TestCase("games.a.freeze('counter',NaN);")]
    [TestCase("games.a.set('counter',Infinity);")]
    public void InvalidScriptValuesFailClearlyAndReleaseEarlierPins(string invalid)
    {
        var session = Start("function init(){games.a.freeze('counter',17);"+invalid+"}function draw(){}");
        Assert.That(session.Error,Is.Not.Null);Machine(session).WriteMemory(0x8000,91);
        Assert.That(Machine(session).Memory.Peek(0x8000),Is.EqualTo(91));
    }
    [TestCase(999,255)] [TestCase(-1,0)] [TestCase(3.9,3)]
    public void SetPreservesTheExistingRangeClampingAndIntegerConversion(double requested,int expected)
    {
        string value=requested.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var session=Start("function init(){games.a.set('counter',"+value+");}function draw(){}");
        Assert.That(session.Error,Is.Null);Assert.That(Machine(session).Memory.Peek(0x8000),Is.EqualTo(expected));
    }

    [Test]
    public void ReleasingOneVariableKeepsAnotherVariablePinned()
    {
        var session=Start("function init(){games.a.freeze('counter',17);games.a.freeze('other',31);games.a.release('counter');}function draw(){}");
        Assert.That(session.Error,Is.Null);
        Machine(session).WriteMemory(0x8000,71);Machine(session).WriteMemory(0x8002,72);
        Assert.That(Machine(session).Memory.Peek(0x8000),Is.EqualTo(71));
        Assert.That(Machine(session).Memory.Peek(0x8002),Is.EqualTo(31));
    }

}
