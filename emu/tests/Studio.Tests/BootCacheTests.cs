using Retro.Core;
using Retro.Fusion;
using Retro.Studio;
using Retro.Systems.Spectrum;

namespace Studio.Tests;

[TestFixture]
public sealed class BootCacheTests
{
    private string _folder = "";
    private byte[] _rom = null!, _snapshot = null!;
    private GameProfile _profile = null!;
    private StudioGame _game = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "retro-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _rom = new byte[16384]; _rom[1] = 0x3E; _rom[2] = 0x11; _rom[3] = 0x76; // LD A,11; HALT
        File.WriteAllBytes(Path.Combine(_folder, "48.rom"), _rom);
        _snapshot = new byte[30 + 49152]; _snapshot[6] = 1; _snapshot[8] = 0xFE; _snapshot[9] = 0xFF;
        _profile = new GameProfile { Id = "cache-" + Guid.NewGuid().ToString("N"), Machine = "zx48", Boot = ["wait 1"] };
        _game = new StudioGame { Games = [new() { Slot = "a", Profile = _profile.Id }], Script = "function init(){ games.a.pause(); }" };
    }

    private Spectrum48 Start(List<string>? log = null)
    {
        var source = new GameSource(_profile, [new MediaFile("same-name.z80", _snapshot, "own-test")], null);
        var session = StudioSession.Start(_game, new Dictionary<string, GameSource> { [_profile.Id] = source }, _folder, text => log?.Add(text));
        Assert.That(session.Error, Is.Null);
        return (Spectrum48)session.Slots.Single().Machine;
    }

    [TearDown]
    public void CleanUp() => Directory.Delete(_folder, true);

    [Test]
    public void EqualFilenameAndLengthDoNotReuseADifferentGameState()
    {
        _snapshot[30 + 0x4000] = 0x11;
        Assert.That(Start().Memory.Peek(0x8000), Is.EqualTo(0x11));
        _snapshot[30 + 0x4000] = 0x22;
        Assert.That(Start().Memory.Peek(0x8000), Is.EqualTo(0x22));
        var log = new List<string>(); Start(log);
        Assert.That(log, Is.Empty, "la misma copia y configuración sí aprovechan el arranque previo");
    }

    [Test]
    public void ChangingTheBootSequenceInvalidatesThePreviousResult()
    {
        Start(); _profile.Boot = ["wait 2"];
        var log = new List<string>(); Start(log);
        Assert.That(log, Has.Count.EqualTo(1));
        Assert.That(log.Single(), Does.Contain("wait 2"));
    }

    [Test]
    public void ChangingFirmwareDoesNotResumeAStateBootedWithThePreviousRom()
    {
        Assert.That(Start().Z80.A, Is.EqualTo(0x11));
        _rom[2] = 0x22; File.WriteAllBytes(Path.Combine(_folder, "48.rom"), _rom);
        Assert.That(Start().Z80.A, Is.EqualTo(0x22));
    }

    [Test]
    public void RecentlyUsedBootsAreReusedAndOldBootsAreEvicted()
    {
        string originalId = _profile.Id;
        Start();
        for (int n = 0; n < 20; n++)
        {
            _profile.Id = originalId + "-" + n; _game.Games[0].Profile = _profile.Id;
            Start();
        }
        var recent = new List<string>(); Start(recent);
        Assert.That(recent, Is.Empty);
        _profile.Id = originalId; _game.Games[0].Profile = originalId;
        var oldest = new List<string>(); Start(oldest);
        Assert.That(oldest, Has.Count.EqualTo(1), "el arranque antiguo no se conserva sin límite");
    }

    [Test]
    public void CancellingABootDoesNotCacheAPartialState()
    {
        _profile.Boot = ["wait 1", "wait 2"];
        var source = new GameSource(_profile, [new MediaFile("same-name.z80", _snapshot, "own-test")], null);
        using var cts = new CancellationTokenSource();
        int steps = 0;
        Assert.Throws<OperationCanceledException>(() => StudioSession.Start(_game,
            new Dictionary<string, GameSource> { [_profile.Id] = source }, _folder, _ => { steps++; cts.Cancel(); }, cts.Token));
        Assert.That(steps, Is.EqualTo(1), "se detiene antes del siguiente paso");
        var log = new List<string>(); Start(log);
        Assert.That(log, Has.Count.EqualTo(2), "la sesión siguiente arranca completa, sin reutilizar la partida incompleta");
    }
}
