using System.Reflection;
using System.Security.Cryptography;
using Retro.Ai;
using Retro.App;
using Retro.Compat;
using Retro.Core;
using Retro.Fusion;
using Retro.Studio;

namespace App.Tests;

[TestFixture, NonParallelizable]
public sealed class FusionCreationBudgetTests
{
    private string _folder = "", _profiles = "", _fusionFolder = "", _createdFolder = "";
    private readonly List<FusionPreparationTarget> _targets = [];
    private static GameConcept Idea => new("Idea propia", "Base y aporte", "Conservar el recorrido", "Flechas",
        "Datos reales del original", new("a"), "Conservar la física");
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Get(object obj, string field) => obj.GetType().GetField(field, Flags)!.GetValue(obj);
    private static void Set(object obj, string field, object? value) => obj.GetType().GetField(field, Flags)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags)!.Invoke(obj, args);

    [SetUp]
    public void Setup()
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-creation-" + Guid.NewGuid())).FullName;
        _profiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES") ?? "";
        _fusionFolder = StudioLibrary.UserFolder; _createdFolder = CreatedGames.UserFolder;
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_folder, "profiles"));
        StudioLibrary.UserFolder = Path.Combine(_folder, "fusions");
        CreatedGames.UserFolder = Path.Combine(_folder, "created");
        _targets.Clear();
        for (int i = 0; i < 2; i++)
        {
            string path = Path.Combine(_folder, $"own{i}.nes");
            byte[] bytes = [(byte)i, 4, 5]; File.WriteAllBytes(path, bytes);
            var profile = new GameProfile { Id = $"own{i}", Name = $"Propio{i}", Machine = "nes",
                Match = new() { Sha256 = [Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()] } };
            ProfileLibrary.Save(profile);
            _targets.Add(new(path, new(profile, [new(Path.GetFileName(path), bytes, path)], _folder)));
        }
    }
    [TearDown]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _profiles.Length == 0 ? null : _profiles);
        StudioLibrary.UserFolder = _fusionFolder; CreatedGames.UserFolder = _createdFolder;
        Directory.Delete(_folder, true);
    }
    private StudioWindow Window(FusionPreparationWorkflow workflow, IAiProvider ai, TimeProvider clock)
    {
        var window = new StudioWindow(new EmulatorHost(), "", _folder, new AiSettings { Provider = "test" }, workflow, () => ai,
            timingPath: Path.Combine(_folder, "timing.json"), workClock: clock);
        var chosen = (List<string>)Get(window, "_chosen")!;
        var games = (List<GameSource>)Get(window, "_games")!;
        var paths = (Dictionary<string, string>)Get(window, "_originalPaths")!;
        foreach (var target in _targets)
        { chosen.Add(target.Source.Profile.Id); games.Add(target.Source); paths[target.Source.Profile.Id] = target.Path; }
        Set(window, "_baseProfile", chosen[0]);
        return window;
    }
    private sealed class WaitingAi : IAiProvider
    {
        public string Name => "Simulada";
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new AssertionException("Canceled calls must not return an answer");
        }
    }

    [Test]
    public async Task WriterMissingDataAutomaticallyReentersResearchWithTheSameOperationAndDeadline()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new FusionPreparationWorkflow { Prepare = async (_, _, _, ct) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new AssertionException("Canceled"); } };
        var ai = new WaitingAi(); var window = Window(workflow, ai, TimeProvider.System);
        Call(window, "BeginCreationWork");
        object deadline = Get(window, "_creationDeadline")!;
        var operation = new AiOperation(ai, FusionResearchPlan.Calls(2, 3));
        Set(window, "_creationOperation", operation);
        Set(window, "_writingConcept", Idea);
        Set(window, "_designTask", Task.FromResult(new DesignOutcome(null, null, 1, StudioDesigner.MissingDataPrefix + "Comprobar la cámara")));
        Call(window, "Poll");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(Get(window, "_fusionPrepareTask"), Is.Not.Null);
        Assert.That(Get(window, "_creationDeadline"), Is.SameAs(deadline));
        Assert.That(Get(window, "_fusionPrepareAi"), Is.SameAs(operation));
        Assert.That(((List<GameConcept>)Get(window, "_concepts")!).Single().MissingData, Does.Contain("cámara"));
        try { await window.CancelWork(); } catch (OperationCanceledException) { }
        Call(window, "Poll");
    }

    [Test]
    public async Task WritingDoesNotGetAnotherHourAfterPreparationHasAlreadyUsedThirtyMinutes()
    {
        var clock = new ManualClock(); var ai = new WaitingAi();
        var window = Window(new FusionPreparationWorkflow(), ai, clock);
        Call(window, "BeginCreationWork");
        var deadline = (CancellationTokenSource)Get(window, "_creationDeadline")!;
        clock.Advance(TimeSpan.FromMinutes(30));
        var operation = new AiOperation(ai, 3);
        var template = new StudioGame { Id = "own", Title = "Propio", Games = [
            new() { Slot = "a", Profile = "own0" }, new() { Slot = "b", Profile = "own1" }], BaseSlot = "a" };
        Call(window, "WriteReady", Idea, template, "test", operation, new OriginalReferences([]), deadline.Token);
        await ai.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.That(((CancellationTokenSource)Get(window, "_cts")!).IsCancellationRequested, Is.True);
        try { await window.CancelWork(); } catch (OperationCanceledException) { }
        Call(window, "Poll");
        Assert.That((string)Get(window, "_progress")!, Does.Contain("una hora"));
        Assert.That(Get(window, "_startTask"), Is.Null);
        Assert.That(StudioWorkStore.Pending(), Is.Not.Empty, "The interrupted writing brief must remain recoverable");
    }

    [Test]
    public void LongFusionOperationsExtendOnlyTheirCliTimeoutWithoutChangingOtherCalls()
    {
        var settings = new AiSettings { Provider = "sub-codex" };
        var normal = (CliAgentProvider)settings.CreateProvider()!;
        var research = (CliAgentProvider)settings.CreateProvider(FusionResearchPlan.TotalTime)!;
        var field = typeof(CliAgentProvider).GetField("_timeout", Flags)!;
        Assert.That((TimeSpan)field.GetValue(normal)!, Is.EqualTo(TimeSpan.FromMinutes(15)));
        Assert.That((TimeSpan)field.GetValue(research)!, Is.EqualTo(TimeSpan.FromHours(1)));
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        private readonly List<Timer> _timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            _timers.Add(timer); timer.Change(dueTime, period); return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            Interlocked.Add(ref _ticks, elapsed.Ticks);
            foreach (var timer in _timers.ToArray()) timer.Fire();
        }
        private sealed class Timer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            private long _due = long.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            { _due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner.GetTimestamp() + dueTime.Ticks; return true; }
            public void Fire()
            {
                if (_due > owner.GetTimestamp()) return;
                _due = long.MaxValue; callback(state);
            }
            public void Dispose() => _due = long.MaxValue;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
