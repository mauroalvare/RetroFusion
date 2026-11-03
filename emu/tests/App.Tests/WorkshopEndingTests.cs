using System.Reflection;
using Retro.Ai;
using Retro.App;
using Retro.Compat;
using Retro.Core;
using Retro.Studio;
using Retro.Systems.Spectrum;

namespace App.Tests;

[NonParallelizable]
public sealed class WorkshopEndingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object value, string field, object? data) => value.GetType().GetField(field, Private)!.SetValue(value, data);
    private static object? Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Private)!.Invoke(value, args);
    private string _folder = "";
    private string _previousCreated = "";
    private string? _previousProfiles;
    [SetUp] public void Setup()
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-work-ending-" + Guid.NewGuid().ToString("N"))).FullName;
        _previousCreated = CreatedGames.UserFolder; CreatedGames.UserFolder = Path.Combine(_folder, "created");
        _previousProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_folder, "profiles"));
    }
    [TearDown] public void Cleanup()
    {
        CreatedGames.UserFolder = _previousCreated;
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _previousProfiles);
        Assert.That(Path.GetDirectoryName(_folder), Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
        Assert.That(Path.GetFileName(_folder), Does.StartWith("retro-work-ending-")); Directory.Delete(_folder, true);
    }

    [TestCase("explanation"), TestCase("analysis")]
    public void CompletedAdviceIsNotAppliedAfterCancellationBeforePolling(string kind)
    {
        var machine = new Spectrum48(new byte[16384]); byte[] before = machine.SaveState();
        var notes = CodeAnnotations.Load("own", _folder); notes.Labels["8000"] = "Own original label"; notes.Save("own");
        byte[] saved = File.ReadAllBytes(Path.Combine(_folder, "own.json"));
        using var code = new CodeWindow(new EmulatorHost(), new AiSettings(), _ => { }, annotationsDirectory: Path.Combine(_folder, "notes"));
        Set(code, "_notes", notes); Set(code, "_gameId", "own"); Set(code, "_machine", machine);
        using var cancellation = new CancellationTokenSource();
        var revision = code.GetType().GetField("_mediaRevision", Private)!.GetValue(code);
        if (kind == "explanation")
        {
            var map = new CodeMap(); ((Dictionary<uint, Routine>)typeof(CodeMap).GetField("_routines", Private)!.GetValue(map)!)[0x8000] = new Routine { Start = 0x8000 };
            Set(code, "_map", map); Set(code, "_explainFor", (uint)0x8000); Set(code, "_explainMachine", machine); Set(code, "_explainRevision", revision);
            Set(code, "_cts", cancellation); Set(code, "_explainTask", Task.FromResult(new RoutineExplanation("Own replacement label", "Own result", "", [])));
        }
        else
        {
            Set(code, "_modificationMachine", machine); Set(code, "_modificationRevision", revision); Set(code, "_modificationCts", cancellation);
            Set(code, "_modificationTask", Task.FromResult(new GameAnalysisResult("Own result", [], new ModificationAdvice("Own result", []))));
        }
        var center = new AppTaskCenter(); var task = Follow(center, "code", code.ReadWork());
        code.CancelWork().GetAwaiter().GetResult();
        if (kind == "explanation") Call(code, "PollExplain", machine); else Call(code, "PollModificationAdvice");
        Follow(center, "code", code.ReadWork());
        Assert.That(task.State, Is.EqualTo(AppTaskState.Cancelled)); Assert.That(machine.SaveState(), Is.EqualTo(before));
        Assert.That(notes.Labels["8000"], Is.EqualTo("Own original label"));
        Assert.That(File.ReadAllBytes(Path.Combine(_folder, "own.json")), Is.EqualTo(saved));
    }

    [TestCase("manual"), TestCase("agent"), TestCase("writer"), TestCase("retry")]
    public void ACompletedTrialCannotPlayOrPublishAfterTheUserAlreadyCancelled(string kind)
    {
        Directory.CreateDirectory(CreatedGames.UserFolder);
        string sentinel = Path.Combine(CreatedGames.UserFolder, "own-sentinel.txt"); File.WriteAllText(sentinel, "Own saved work");
        var creator = new CreatorWindow(_folder, new AiSettings(), _ => Assert.Fail("Cancellation must win over a not-yet-consumed result."));
        var draft = new GameDraft("Own draft", "", "", "", "rts");
        var report = new TrialReport { Machine = "c64", Assembly = new Retro.Asm.AsmResult(), File = [1, 8, 0x60] };
        report.Checks.Add(new("Own check", true, "Own synthetic successful result"));
        using var cancellation = new CancellationTokenSource(); Set(creator, "_cts", cancellation);
        if (kind == "writer") Set(creator, "_writeTask", Task.FromResult(new WriterOutcome(draft, report, 1, null)));
        else if (kind == "retry") Set(creator, "_retryTask", Task.FromResult((new CreatedGame { Title = "Own draft", Folder = CreatedGames.UserFolder }, report)));
        else Set(creator, kind == "manual" ? "_manualTask" : "_agentTask", Task.FromResult((draft, report)));
        var center = new AppTaskCenter(); var task = Follow(center, "creator", creator.ReadWork());
        creator.CancelWork().GetAwaiter().GetResult();
        Call(creator, "Poll"); Follow(center, "creator", creator.ReadWork());
        Assert.That(task.State, Is.EqualTo(AppTaskState.Cancelled));
        Assert.That(File.ReadAllText(sentinel), Is.EqualTo("Own saved work"));
        Assert.That(Directory.GetFiles(CreatedGames.UserFolder, "*", SearchOption.AllDirectories), Is.EqualTo(new[] { sentinel }));
    }

    private static Task<T> Ending<T>(bool cancelled) => cancelled ? Task.FromCanceled<T>(new CancellationToken(true))
        : Task.FromException<T>(new IOException("Own simulated worker failure"));
    private static AppTask Follow(AppTaskCenter center, string key, WorkSnapshot snapshot)
    {
        center.Track(key, snapshot.Report, snapshot.Ending, snapshot.Detail); return center.All.Last();
    }

    [TestCase(false), TestCase(true)]
    public void CreatorFailureOrCancellationCannotBeAnnouncedAsCompleted(bool cancelled)
    {
        var creator = new CreatorWindow(_folder, new AiSettings(), _ => Assert.Fail("A failed trial must not play."));
        Set(creator, "_manualTask", Ending<(GameDraft, TrialReport)>(cancelled));
        var center = new AppTaskCenter(); var task = Follow(center, "creator", creator.ReadWork());
        Call(creator, "Poll"); Follow(center, "creator", creator.ReadWork());
        Assert.That(task.State, Is.EqualTo(cancelled ? AppTaskState.Cancelled : AppTaskState.Failed));
        Assert.That(task.Active, Is.False);
        if (!cancelled) Assert.That(task.Errors.Single(), Does.Contain("Own simulated worker failure"));
    }

    [TestCase(false), TestCase(true)]
    public void StudioFailureOrCancellationCannotBeAnnouncedAsCompleted(bool cancelled)
    {
        var studio = new StudioWindow(new EmulatorHost(), _folder, _folder, new AiSettings(), timingPath: Path.Combine(_folder, "times.json"));
        Set(studio, "_conceptTask", Ending<List<GameConcept>>(cancelled));
        var center = new AppTaskCenter(); var task = Follow(center, "studio", studio.ReadWork());
        Call(studio, "Poll"); Follow(center, "studio", studio.ReadWork());
        Assert.That(task.State, Is.EqualTo(cancelled ? AppTaskState.Cancelled : AppTaskState.Failed));
        Assert.That(task.Active, Is.False);
    }

    [TestCase(false), TestCase(true)]
    public void AnalysisFailureOrCancellationCannotBeAnnouncedAsCompleted(bool cancelled)
    {
        using var code = new CodeWindow(new EmulatorHost(), new AiSettings(), _ => { }, annotationsDirectory: Path.Combine(_folder, "notes"));
        Set(code, "_modificationTask", Ending<GameAnalysisResult>(cancelled));
        Set(code, "_modificationRevision", code.GetType().GetField("_mediaRevision", Private)!.GetValue(code));
        var center = new AppTaskCenter(); var task = Follow(center, "analysis", code.ReadWork());
        Call(code, "PollModificationAdvice"); Follow(center, "analysis", code.ReadWork());
        Assert.That(task.State, Is.EqualTo(cancelled ? AppTaskState.Cancelled : AppTaskState.Failed));
        Assert.That(task.Active, Is.False);
    }

    [Test]
    public void ARejectedTrialAndAnUnverifiedEndingAreNotSuccesses()
    {
        var creator = new CreatorWindow(_folder, new AiSettings(), _ => Assert.Fail("A rejected trial must not play."));
        Set(creator, "_retryTask", Task.FromResult((new CreatedGame { Title = "Own draft" }, new TrialReport { Machine = "c64", Assembly = new Retro.Asm.AsmResult() })));
        var center = new AppTaskCenter(); var task = Follow(center, "creator", creator.ReadWork());
        Call(creator, "Poll"); Follow(center, "creator", creator.ReadWork());
        Assert.That(task.State, Is.EqualTo(AppTaskState.Failed));
        center.Track("unknown", new("own", "own", "working")); var unknown = center.Active.Single();
        center.Track("unknown", null); Assert.That(unknown.State, Is.EqualTo(AppTaskState.PartlyCompleted));
    }

    [Test]
    public void SwitchingToAnotherKindPreservesTheActualEndingAndItsError()
    {
        var center = new AppTaskCenter(); center.Track("work", new("trial", "own", "trying")); var previous = center.Active.Single();
        center.Track("work", new("writing", "own", "waiting", AppTaskState.WaitingUser), AppTaskState.Failed, "Own failed trial");
        Assert.That(previous.State, Is.EqualTo(AppTaskState.Failed)); Assert.That(previous.Errors, Is.EqualTo(new[] { "Own failed trial" }));
        Assert.That(center.Active.Single().Kind, Is.EqualTo("writing"));
    }

    [Test]
    public void AnExplanationWhoseNotesCannotBeSavedDoesNotClaimTheyWereSavedAndKeepsTheSession()
    {
        string blocked = Path.Combine(_folder, "blocked"); File.WriteAllText(blocked, "Own sentinel");
        var notes = CodeAnnotations.Load("own", blocked); notes.Summaries["8000"] = "Own explanation";
        using var code = new CodeWindow(new EmulatorHost(), new AiSettings(), _ => { }, annotationsDirectory: Path.Combine(_folder, "notes"));
        Set(code, "_notes", notes); Set(code, "_gameId", "own"); Set(code, "_pasted", "Own answer to preserve");
        Assert.That(Call(code, "SaveExplanation", "Everything was saved"), Is.EqualTo(false));
        Assert.That(code.WorkEnding, Is.EqualTo(AppTaskState.PartlyCompleted));
        Assert.That(code.WorkDetail, Does.Contain("sesión").And.Not.Contain("Everything was saved"));
        Assert.That(notes.Summaries["8000"], Is.EqualTo("Own explanation"));
        Assert.That(File.ReadAllText(blocked), Is.EqualTo("Own sentinel"));
    }
}
