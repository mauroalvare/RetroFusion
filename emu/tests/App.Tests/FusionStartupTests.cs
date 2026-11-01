using System.Reflection;
using Retro.Ai;
using Retro.App;
using Retro.Fusion;
using Retro.Studio;

namespace App.Tests;

public sealed class FusionStartupTests
{
    private string _folder = "";
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object? Get(object value, string field) => value.GetType().GetField(field, Flags)!.GetValue(value);
    private static void Set(object value, string field, object? data) => value.GetType().GetField(field, Flags)!.SetValue(value, data);
    private static void Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Flags)!.Invoke(value, args);
    private static StudioGame Game() => new() { Id = "startup-own", Title = "Arranque propio", Script = "function init(){game.state('playing');} function tick(input){} function draw(){}" };
    [SetUp] public void Setup() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-startup-" + Guid.NewGuid())).FullName;
    [TearDown] public void Cleanup()
    {
        string parent = Path.GetDirectoryName(Path.GetFullPath(_folder))!;
        Assert.That(parent, Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)), "Only the dedicated temporary directory may be removed.");
        Assert.That(Path.GetFileName(_folder), Does.StartWith("retro-startup-"));
        Directory.Delete(_folder, true);
    }
    private StudioWindow Window(EmulatorHost host) => new(host, "", _folder, new AiSettings { Provider = "manual" }, timingPath: Path.Combine(_folder, "timing.json"));

    [Test]
    public async Task CancelWorkReachesTheFusionBootRatherThanLeavingItRunning()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new EmulatorHost();
        var window = new StudioWindow(host, "", _folder, new AiSettings { Provider = "manual" }, timingPath: Path.Combine(_folder, "timing.json"))
        {
            StartSession = (game, sources, firmware, ct) =>
            {
                entered.TrySetResult(ct);
                ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)); ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The test boot was not canceled.");
            }
        };
        try
        {
            Call(window, "StartGame", Game());
            var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var pending = window.CancelWork();
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.CatchAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Call(window, "Poll");
            Assert.That(Get(window, "_progress"), Does.Contain("cancelado"));
            Assert.That(Get(window, "_problem"), Is.Null);
            Assert.That(host.Fusion, Is.Null);
        }
        finally { try { await window.CancelWork(); } catch (Exception) { } }
    }

    [Test]
    public void ACanceledBootIsReportedAsCancellationWithoutAnUnknownError()
    {
        var window = Window(new());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Set(window, "_cts", cancellation);
        Set(window, "_startTask", Task.FromCanceled<StudioSession>(cancellation.Token));
        Call(window, "Poll");
        Assert.That(Get(window, "_problem"), Is.Null);
        Assert.That(Get(window, "_progress"), Does.Contain("cancelado"));
    }

    [Test]
    public void ACompletedBootCannotTakeOverThePlayerAfterCancellation()
    {
        var host = new EmulatorHost(); var window = Window(host);
        var game = Game();
        var session = StudioSession.Start(game, new Dictionary<string, GameSource>(), "");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Set(window, "_cts", cancellation); Set(window, "_starting", game);
        Set(window, "_startTask", Task.FromResult(session));
        int started = 0; window.GameStarted += _ => started++;
        Call(window, "Poll");
        Assert.That(host.Fusion, Is.Null);
        Assert.That(started, Is.Zero);
        Assert.That(Get(window, "_starting"), Is.Null);
        Assert.That(Get(window, "_problem"), Is.Null);
    }

    [Test]
    public void AnInitErrorKeepsThePreviousPlayerAndShowsItsActualCause()
    {
        var host = new EmulatorHost(); var window = Window(host);
        var game = Game(); game.Script = "function init(){undefinedFunction();} function tick(input){} function draw(){}";
        byte[] rom = new byte[16 + 16384 + 8192];
        rom[0] = 0x4e; rom[1] = 0x45; rom[2] = 0x53; rom[3] = 0x1a; rom[4] = 1; rom[5] = 1;
        rom[16] = 0x4c; rom[17] = 0; rom[18] = 0x80; rom[16 + 0x3ffd] = 0x80;
        var profile = new GameProfile { Id = "startup-own", Machine = "nes", Name = "Programa propio" };
        game.Games = [new() { Slot = "a", Profile = profile.Id }];
        var session = StudioSession.Start(game, new Dictionary<string, GameSource> { [profile.Id] = new(profile, [new("own.nes", rom, "generated-own.nes")], null) }, "");
        Assert.That(session.Error, Does.Contain("undefinedFunction"));
        Set(window, "_starting", game); Set(window, "_startTask", Task.FromResult(session));
        Call(window, "Poll");
        Assert.That(host.Fusion, Is.Null);
        Assert.That(Get(window, "_problem"), Does.Contain("undefinedFunction"));
        Assert.That(Get(window, "_starting"), Is.Null);
    }

    [Test]
    public async Task AnUnfinishedBootHasItsOwnDeadlineAndDoesNotLookLikeUserCancellation()
    {
        var window = new StudioWindow(new(), "", _folder, new AiSettings { Provider = "manual" }, timingPath: Path.Combine(_folder, "timing.json"))
        {
            StartupTimeout = TimeSpan.FromMilliseconds(100),
            StartSession = (_, _, _, ct) => { ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)); ct.ThrowIfCancellationRequested(); throw new InvalidOperationException("Deadline was not applied"); }
        };
        try
        {
            Call(window, "StartGame", Game());
            var pending = (Task<StudioSession>)Get(window, "_startTask")!;
            Assert.CatchAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Call(window, "Poll");
            Assert.That(Get(window, "_problem"), Does.Contain("tiempo de arranque"));
            Assert.That(Get(window, "_starting"), Is.Null);
        }
        finally { try { await window.CancelWork(); } catch (Exception) { } }
    }
}
