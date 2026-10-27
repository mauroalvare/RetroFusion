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
    [TestCase("rom")]
    [TestCase("profile")]
    [TestCase("cached-bytes")]
    public async Task DirectCreationRejectsChangedInputsBeforeStartingReferencesOrAskingAi(string changed)
    {
        var ai = new WaitingAi();
        var window = Window(new FusionPreparationWorkflow(), ai, TimeProvider.System);
        switch (changed)
        {
            case "rom": File.AppendAllText(_targets[0].Path, "changed"); break;
            case "profile":
                var edited = GameProfile.Parse(_targets[0].Source.Profile.ToJson());
                edited.Description = "Edición externa";
                ProfileLibrary.Save(edited); break;
            case "cached-bytes": _targets[0].Source.Files[0].Data[0] ^= 1; break;
        }
        try
        {
            Call(window, "Write", Idea, null!);
            Assert.That(Get(window, "_referenceTask"), Is.Null, "Changed inputs must be rejected before work begins");
            Assert.That(Get(window, "_problem"), Is.Not.Null);
            Assert.That(ai.Entered.Task.IsCompleted, Is.False);
        }
        finally { try { await window.CancelWork(); } catch (Exception) { } }
    }

    private FusionInputSnapshot Snapshot(string firmware = "") => FusionInputSnapshot.Capture(
        _targets.ToDictionary(t => t.Source.Profile.Id, t => t.Source),
        _targets.ToDictionary(t => t.Source.Profile.Id, t => t.Path), firmware);

    [TestCase("rom")]
    [TestCase("profile")]
    [TestCase("delete-profile")]
    [TestCase("memory-profile")]
    [TestCase("memory-bytes")]
    public void ChangedInputsCannotBeUsedToSaveAndFrozenInputsRemainUntouched(string changed)
    {
        var snapshot = Snapshot();
        string frozenProfile = snapshot.Sources["own0"].Profile.ToJson();
        byte[] frozenBytes = (byte[])snapshot.Sources["own0"].Files[0].Data.Clone();
        switch (changed)
        {
            case "rom": File.AppendAllText(_targets[0].Path, "changed"); break;
            case "profile":
                var edited = GameProfile.Parse(_targets[0].Source.Profile.ToJson());
                edited.Description = "Edición externa"; ProfileLibrary.Save(edited); break;
            case "delete-profile": File.Delete(Path.Combine(ProfileLibrary.UserFolder, "own0.json")); break;
            case "memory-profile": _targets[0].Source.Profile.Description = "Cambio en memoria"; break;
            case "memory-bytes": _targets[0].Source.Files[0].Data[0] ^= 1; break;
        }
        Assert.Throws<InvalidDataException>(() => snapshot.Ensure());
        Assert.That(snapshot.Sources["own0"].Profile.ToJson(), Is.EqualTo(frozenProfile));
        Assert.That(snapshot.Sources["own0"].Files[0].Data, Is.EqualTo(frozenBytes));
    }

    [Test]
    public void ReplacingTheLibraryObjectDoesNotBypassTheSaveGuard()
    {
        var snapshot = Snapshot();
        var current = _targets.ToDictionary(t => t.Source.Profile.Id, t => t.Source);
        var changed = GameProfile.Parse(current["own0"].Profile.ToJson());
        changed.Description = "Reemplazo";
        current["own0"] = current["own0"] with { Profile = changed };
        Assert.Throws<InvalidDataException>(() => snapshot.Ensure(current));
    }

    [Test]
    public void OwnPreparationMayUpdateProfilesButMustCaptureThemAgainBeforeWriting()
    {
        var snapshot = Snapshot();
        _targets[0].Source.Profile.Description = "Dato verificado durante la preparación";
        ProfileLibrary.Save(_targets[0].Source.Profile);
        Assert.DoesNotThrow(snapshot.EnsureOriginals);
        Assert.Throws<InvalidDataException>(() => snapshot.Ensure());
        Assert.DoesNotThrow(() => Snapshot().Ensure());
    }

    [Test]
    public void FirmwareChangesAreDetectedEvenWhenTheOriginalFileIsUnchanged()
    {
        string firmware = Path.Combine(_folder, "firmware"); Directory.CreateDirectory(firmware);
        File.WriteAllBytes(Path.Combine(firmware, "48.rom"), new byte[16384]);
        _targets[0].Source.Profile.Machine = "zx48";
        ProfileLibrary.Save(_targets[0].Source.Profile);
        var snapshot = Snapshot(firmware);
        byte[] changed = new byte[16384]; changed[42] = 1;
        File.WriteAllBytes(Path.Combine(firmware, "48.rom"), changed);
        Assert.Throws<InvalidDataException>(snapshot.EnsureOriginals);
    }

    [Test]
    public void CorruptedProfileIsReportedBeforeDirectCreationInsteadOfEscapingTheWindow()
    {
        var ai = new WaitingAi(); var window = Window(new FusionPreparationWorkflow(), ai, TimeProvider.System);
        File.WriteAllText(Path.Combine(ProfileLibrary.UserFolder, "own0.json"), "{broken");
        Assert.DoesNotThrow(() => Call(window, "Write", Idea, null!));
        Assert.That(Get(window, "_referenceTask"), Is.Null);
        Assert.That(Get(window, "_problem"), Is.Not.Null);
        Assert.That(ai.Entered.Task.IsCompleted, Is.False);
    }

    [Test]
    public void NestedArchiveInputsAreFrozenAndAnAddedMemberInvalidatesTheGuard()
    {
        string path = _targets[0].Path + ".zip";
        void Zip(bool extra)
        {
            using var file = File.Create(path);
            using var zip = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Create);
            using (var stream = zip.CreateEntry("folder/own.nes").Open()) stream.Write(new byte[] { 1, 2, 3 });
            if (extra) using (var stream = zip.CreateEntry("other.nes").Open()) stream.Write(new byte[] { 4, 5, 6 });
        }
        Zip(false);
        var target = _targets[0];
        _targets[0] = target with { Path = path, Source = target.Source with { Files = MediaArchive.Open(path) } };
        var snapshot = Snapshot();
        Assert.DoesNotThrow(() => snapshot.Ensure());
        Zip(true);
        Assert.Throws<InvalidDataException>(() => snapshot.Ensure());
    }

    [Test]
    public async Task WholeCreationClockKeepsResearchTimeAcrossWritingAndCancelledWorkDoesNotTrain()
    {
        var clock = new ManualClock(); var ai = new WaitingAi();
        var window = Window(new FusionPreparationWorkflow(), ai, clock);
        Call(window, "BeginCreationWork"); Call(window, "StartCreationTiming", true);
        var timing = (AiTiming)Get(window, "_creationTiming")!;
        clock.Advance(TimeSpan.FromMinutes(30));
        var deadline = (CancellationTokenSource)Get(window, "_creationDeadline")!;
        var template = new StudioGame { Id = "own", Title = "Propio", Games = [
            new() { Slot = "a", Profile = "own0" }, new() { Slot = "b", Profile = "own1" }], BaseSlot = "a" };
        Call(window, "WriteReady", Idea, template, "test", new AiOperation(ai, 3), new OriginalReferences([]), deadline.Token);
        await ai.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(Get(window, "_creationTiming"), Is.SameAs(timing));
        Assert.That(timing.Elapsed, Is.EqualTo(TimeSpan.FromMinutes(30)));
        try { await window.CancelWork(); } catch (OperationCanceledException) { }
        Call(window, "Poll");
        var history = new AiDurationHistory(Path.Combine(_folder, "timing.json"));
        string identity = (string)Call(window, "CreationTimingIdentity", true)!;
        Assert.That(history.Estimate(AiDurationHistory.Key(identity)), Is.Null);
        Assert.That(File.Exists(Path.Combine(_folder, "timing.json")), Is.False);
    }

    [Test]
    public void UpfrontEstimateUsesCompletedJobsOfTheSameConfigurationOnly()
    {
        var clock = new ManualClock(); var window = Window(new FusionPreparationWorkflow(), new WaitingAi(), clock);
        string identity = (string)Call(window, "CreationTimingIdentity", true)!;
        var history = new AiDurationHistory(Path.Combine(_folder, "timing.json"));
        for (int n = 0; n < 3; n++) history.Record(AiDurationHistory.Key(identity), TimeSpan.FromMinutes(8));
        var range = (AiTimeRange)Call(window, "CreationEstimate", true)!;
        Assert.That(range.Samples, Is.EqualTo(3));
        Assert.That(FusionResearchPlan.EstimateText(2, true, range), Does.Contain("6–12").And.Contain("trabajos completos"));
        Assert.That(Call(window, "CreationEstimate", false), Is.Null);
    }

    [TestCase("rom")]
    [TestCase("profile")]
    [TestCase("configuration")]
    public void ChangedInputsAreRejectedAtSaveAndTheRecoverableDraftIsPreserved(string changed)
    {
        var clock = new ManualClock(); var window = Window(new FusionPreparationWorkflow(), new WaitingAi(), clock);
        Call(window, "BeginCreationWork");
        Assert.That(Call(window, "CaptureCreationInputs"), Is.EqualTo(true));
        Set(window, "_creationContext", Call(window, "PreparationContext"));
        Call(window, "StartCreationTiming", false);
        clock.Advance(TimeSpan.FromMinutes(10));
        var work = new StudioWork { State = new() { Template = new() { Id = "own", Title = "Idea propia" } } };
        StudioWorkStore.Save(work); Set(window, "_activeWork", work.Id);
        switch (changed)
        {
            case "rom": File.AppendAllText(_targets[0].Path, "changed"); break;
            case "profile":
                var edited = GameProfile.Parse(_targets[0].Source.Profile.ToJson());
                edited.Description = "Edición externa"; ProfileLibrary.Save(edited); break;
            case "configuration": Set(window, "_wish", "Otro pedido"); break;
        }
        Call(window, "SaveAndStart", work.State.Template.Copy(), false, null!);
        Assert.That(Get(window, "_startTask"), Is.Null);
        Assert.That(StudioLibrary.AllVersions().Any(g => g.Id.StartsWith("own")), Is.False);
        Assert.That(StudioWorkStore.Pending().Select(w => w.Id), Does.Contain(work.Id));
        string identity = (string)Call(window, "CreationTimingIdentity", false)!;
        Assert.That(new AiDurationHistory(Path.Combine(_folder, "timing.json")).Estimate(AiDurationHistory.Key(identity)), Is.Null);
    }

    [Test]
    public async Task FileChangeDuringWritingCancelsTheWaitingProviderAndKeepsItsWork()
    {
        var clock = new ManualClock(); var ai = new WaitingAi();
        var window = Window(new FusionPreparationWorkflow(), ai, clock);
        Call(window, "BeginCreationWork"); Assert.That(Call(window, "CaptureCreationInputs"), Is.EqualTo(true));
        Set(window, "_creationContext", Call(window, "PreparationContext"));
        var deadline = (CancellationTokenSource)Get(window, "_creationDeadline")!;
        var template = new StudioGame { Id = "own", Title = "Propio", Games = [
            new() { Slot = "a", Profile = "own0" }, new() { Slot = "b", Profile = "own1" }], BaseSlot = "a" };
        Call(window, "WriteReady", Idea, template, "test", new AiOperation(ai, 3), new OriginalReferences([]), deadline.Token);
        await ai.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        File.AppendAllText(_targets[0].Path, "changed");
        clock.Advance(TimeSpan.FromSeconds(2));
        Call(window, "Poll");
        Assert.That(((CancellationTokenSource)Get(window, "_cts")!).IsCancellationRequested, Is.True);
        Assert.That((string)Get(window, "_problem")!, Does.Contain("cambiaron"));
        try { await window.CancelWork(); } catch (OperationCanceledException) { }
        Call(window, "Poll");
        Assert.That(Get(window, "_startTask"), Is.Null);
        Assert.That(StudioWorkStore.Pending(), Is.Not.Empty);
    }

}
