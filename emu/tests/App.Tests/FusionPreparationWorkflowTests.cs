using System.Security.Cryptography;
using Retro.Ai;
using Retro.App;
using Retro.Compat;
using Retro.Core;
using Retro.Fusion;
using Retro.Studio;

namespace App.Tests;

[TestFixture]
public sealed class FusionPreparationWorkflowTests
{
    private string _folder = "";
    private List<FusionPreparationTarget> _targets = [];
    private List<RecipeGame> _slots = [];
    private static GameConcept Idea => new("Idea elegida", "Base y aporte", "Premisa", "Controles",
        "Recorrido conservado", new("b"), "Mantener la base", "Posición comprobada");

    [SetUp]
    public void Setup()
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-preparation-" + Guid.NewGuid())).FullName;
        _targets = []; _slots = [];
        for (int i = 0; i < 2; i++)
        {
            string path = Path.Combine(_folder, $"Original{i}.nes");
            byte[] data = [(byte)i, 4, 9]; File.WriteAllBytes(path, data);
            var profile = new GameProfile { Id = $"old{i}", Name = $"Propio{i}", Machine = "nes",
                Match = new() { Sha256 = [Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()] } };
            _targets.Add(new(path, new(profile, [new($"Original{i}.nes", data, path)], _folder)));
            _slots.Add(new() { Slot = i == 0 ? "a" : "b", Profile = profile.Id });
        }
    }

    [TearDown] public void Cleanup() => Directory.Delete(_folder, true);

    private PrepareResult Prepared(string path, bool variable = true, bool remap = false)
    {
        var target = _targets.Single(t => t.Path == path);
        string sha = GamePreparer.Sha256Of(target.Source.Files[0]);
        var p = new GameProfile { Id = target.Source.Profile.Id + (remap ? "-prepared" : ""),
            Name = target.Source.Profile.Name, Machine = "nes", Match = new() { Sha256 = [sha] },
            Verified = "Arranque reproducido; variables verificadas por una prueba propia." };
        if (variable) p.Variables["x"] = new() { Address = "0020", Description = "Posición comprobada." };
        return new() { Ok = true, Game = p.Name, Machine = p.Machine, Sha256 = sha, Profile = p,
            CheckedData = ["Arranque reproducido."] };
    }

    private sealed class FakeAi(string answer) : IAiProvider
    {
        public string Name => "IA simulada";
        public int Calls { get; private set; }
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(answer); }
    }

    [Test]
    public async Task PlaysBothOriginalsAndReviewsFreshProfilesWhilePreservingSlotsAndIdea()
    {
        var goals = new List<string>();
        var workflow = new FusionPreparationWorkflow { Prepare = (path, options, progress, ct) =>
        {
            goals.Add(options.Goal); return Task.FromResult(Prepared(path, remap: true));
        } };
        var ai = new FakeAi("""{"missingData":"","evidence":[{"slot":"b","kind":"variable","name":"x","reason":"Posición comprobada en esta edición."}]}""");
        var operation = new AiOperation(ai, 1);
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", null,
            (idea, slots, find, ct) => new StudioDesigner().ReassessConceptAsync(operation, slots, find, idea, ct),
            false, null, CancellationToken.None);
        Assert.That(goals, Has.Count.EqualTo(2));
        Assert.That(goals.All(goal => goal.Contains(Idea.MissingData) && goal.Contains(Idea.Title)), Is.True);
        Assert.That(result.Slots.Select(slot => slot.Profile), Is.EqualTo(new[] { "old0-prepared", "old1-prepared" }));
        Assert.That(result.Concept.MissingData, Is.Empty);
        Assert.That(result.Concept.Title, Is.EqualTo(Idea.Title));
        Assert.That(result.Concept.Direction, Is.EqualTo(Idea.Direction));
        Assert.That(result.Concept.PlayerRequest, Is.EqualTo(Idea.PlayerRequest));
        Assert.That(ai.Calls, Is.EqualTo(1));
        Assert.That(operation.Stats.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task EmptyPartialProfilesCannotClearMissingDataWithAnUnbackedReply()
    {
        var workflow = new FusionPreparationWorkflow { Prepare = (path, _, _, _) => Task.FromResult(Prepared(path, variable: false)) };
        var ai = new FakeAi("""{"missingData":"","evidence":[]}""");
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", null,
            (idea, slots, find, ct) => new StudioDesigner().ReassessConceptAsync(ai, slots, find, idea, ct),
            true, null, CancellationToken.None);
        Assert.That(result.Concept.MissingData, Does.Contain(Idea.MissingData));
        Assert.That(result.Preparations, Has.Count.EqualTo(2));
        Assert.That(ai.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task FailedPreparationStopsBeforeReviewAndKeepsAlreadyPreparedIdentity()
    {
        int prepared = 0, reviewed = 0;
        var workflow = new FusionPreparationWorkflow { Prepare = (path, _, _, _) =>
            Task.FromResult(++prepared == 1 ? Prepared(path, remap: true) : new PrepareResult { Problem = "No llegó a la partida" }) };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", null,
            (idea, _, _, _) => { reviewed++; return Task.FromResult(idea with { MissingData = "" }); },
            false, null, CancellationToken.None);
        Assert.That(reviewed, Is.Zero);
        Assert.That(result.Concept, Is.EqualTo(Idea));
        Assert.That(result.Summary, Does.Contain("No llegó"));
        Assert.That(result.Slots[0].Profile, Is.EqualTo("old0-prepared"));
        Assert.That(result.Slots[1].Profile, Is.EqualTo("old1"));
    }

    [Test]
    public void CancellationAfterOneOriginalDoesNotPrepareAnotherOrReview()
    {
        using var cts = new CancellationTokenSource(); int calls = 0;
        var workflow = new FusionPreparationWorkflow { Prepare = (path, _, _, _) =>
        { calls++; cts.Cancel(); return Task.FromResult(Prepared(path)); } };
        Assert.ThrowsAsync<OperationCanceledException>(() => workflow.RunAsync(Idea, _slots, _targets, "", null,
            (_, _, _, _) => throw new AssertionException("A canceled job must not review"), false, null, cts.Token));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void AChangedOriginalCannotUseOldObservations()
    {
        var workflow = new FusionPreparationWorkflow { Prepare = (path, _, _, _) =>
        { var result = Prepared(path); File.WriteAllBytes(path, [8, 7, 6]); return Task.FromResult(result); } };
        Assert.ThrowsAsync<InvalidDataException>(() => workflow.RunAsync(Idea, _slots, _targets, "", null,
            (_, _, _, _) => throw new AssertionException("Changed media must not review"), false, null, CancellationToken.None));
    }

    [Test]
    public void AProfileFromAnotherEditionCannotUnlockTheIdea()
    {
        var workflow = new FusionPreparationWorkflow { Prepare = (path, _, _, _) =>
        { var result = Prepared(path); result.Sha256 = "wrong"; return Task.FromResult(result); } };
        Assert.ThrowsAsync<InvalidDataException>(() => workflow.RunAsync(Idea, _slots, _targets, "", null,
            (_, _, _, _) => throw new AssertionException("Other editions must not review"), false, null, CancellationToken.None));
    }

    [Test]
    public async Task ManualPreparationDoesNotPretendToReassessOrAutoClearRequirements()
    {
        var workflow = new FusionPreparationWorkflow { Prepare = (path, _, _, _) => Task.FromResult(Prepared(path)) };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", null, null, true, null, CancellationToken.None);
        Assert.That(result.Concept, Is.EqualTo(Idea));
        Assert.That(result.Sources["old1"].Profile.Variables.ContainsKey("x"), Is.True);
    }

    [Test]
    public async Task RemappedIdentitiesCannotOverwriteAnotherSource()
    {
        var workflow = new FusionPreparationWorkflow { Prepare = (path, _, _, _) =>
        {
            var result = Prepared(path);
            result.Profile!.Id = path == _targets[0].Path ? "old1" : "new-second";
            return Task.FromResult(result);
        } };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", null, null, false, null, CancellationToken.None);
        Assert.That(result.Sources.Keys, Is.EquivalentTo(new[] { "old1", "new-second" }));
        Assert.That(result.Sources["old1"].Files[0].Data, Is.EqualTo(_targets[0].Source.Files[0].Data));
        Assert.That(result.Sources["new-second"].Files[0].Data, Is.EqualTo(_targets[1].Source.Files[0].Data));
    }

    [Test]
    public async Task TheCompletedResultStillDetectsChangesWhileWaitingForManualReview()
    {
        var workflow = new FusionPreparationWorkflow { Prepare = (path, _, _, _) => Task.FromResult(Prepared(path)) };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", null, null, false, null, CancellationToken.None);
        result.EnsureOriginalsUnchanged();
        File.WriteAllBytes(_targets[0].Path, [88, 4, 9]);
        Assert.That(() => result.EnsureOriginalsUnchanged(), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task AnOkFlagWithoutAProfileDoesNotReachReview()
    {
        var workflow = new FusionPreparationWorkflow { Prepare = (_, _, _, _) => Task.FromResult(new PrepareResult { Ok = true }) };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", null,
            (_, _, _, _) => throw new AssertionException("No profile must not review"), false, null, CancellationToken.None);
        Assert.That(result.Concept.MissingData, Is.EqualTo(Idea.MissingData));
        Assert.That(result.Preparations.Single().Profile, Is.Null);
    }

    [TestCase("wish")]
    [TestCase("folder")]
    [TestCase("base")]
    [TestCase("selection")]
    [TestCase("model")]
    [TestCase("connection")]
    [TestCase("executable")]
    public async Task WindowCancelsAnObsoletePreparationBeforeItCanCreate(string changed)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Set(object obj, string field, object? value) => obj.GetType().GetField(field, flags)!.SetValue(obj, value);
        object? Get(object obj, string field) => obj.GetType().GetField(field, flags)!.GetValue(obj);
        object? Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, flags)!.Invoke(obj, args);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<PrepareResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new FusionPreparationWorkflow { Prepare = async (_, _, _, ct) =>
        {
            entered.TrySetResult();
            return await gate.Task.WaitAsync(ct);
        } };
        var settings = new AiSettings { Provider = "test" };
        var window = new StudioWindow(new EmulatorHost(), "", _folder, settings, workflow, () => new FakeAi("{}"));
        var chosen = (List<string>)Get(window, "_chosen")!;
        var games = (List<GameSource>)Get(window, "_games")!;
        var paths = (Dictionary<string, string>)Get(window, "_originalPaths")!;
        foreach (var target in _targets)
        {
            chosen.Add(target.Source.Profile.Id); games.Add(target.Source); paths[target.Source.Profile.Id] = target.Path;
        }
        Set(window, "_concepts", new List<GameConcept> { Idea });
        Call(window, "PrepareConcept", Idea);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        switch (changed)
        {
            case "wish": Set(window, "_wish", "Otro pedido"); break;
            case "folder": Set(window, "_folder", _folder + "-different"); break;
            case "base": Set(window, "_baseProfile", chosen[1]); break;
            case "selection": chosen.Reverse(); break;
            case "model": settings.SessionModel = "another-model"; break;
            case "connection": settings.OllamaUrl = "http://127.0.0.1:9999"; break;
            case "executable": settings.CliPaths["test"] = "another-cli"; break;
        }
        Call(window, "Poll");
        for (int i = 0; i < 100 && Get(window, "_fusionPrepareTask") != null; i++)
        {
            await Task.Delay(5); Call(window, "Poll");
        }
        Assert.That(Get(window, "_fusionPrepareTask"), Is.Null);
        Assert.That(Get(window, "_designTask"), Is.Null);
        Assert.That(((List<GameConcept>)Get(window, "_concepts")!).Single().MissingData, Is.EqualTo(Idea.MissingData));
        await window.CancelWork();
    }

    [Test]
    public async Task AUsefulDonorKeepsItsHelpersWhileOnlyTheMissingOriginalIsPrepared()
    {
        var donor = _targets[0].Source.Profile;
        donor.Helpers.Add("g.native = function(){ return 1; };");
        _targets[0] = _targets[0] with { NeedsPreparation = false };
        var preparedPaths = new List<string>();
        var workflow = new FusionPreparationWorkflow { Prepare = (path, _, _, _) =>
        { preparedPaths.Add(path); return Task.FromResult(Prepared(path)); } };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", null, null, false, null, CancellationToken.None);
        Assert.That(preparedPaths, Is.EqualTo(new[] { _targets[1].Path }));
        Assert.That(result.Sources["old0"].Profile.Helpers, Is.EqualTo(donor.Helpers));
        Assert.That(result.Preparations, Has.Count.EqualTo(1));
    }

    [Test]
    public void AWriterThatDiscoversMissingDataOffersPreparationForTheSameIdea()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var window = new StudioWindow(new EmulatorHost(), "", _folder, new AiSettings());
        var first = Idea with { Design = "Otra propuesta con el mismo título", MissingData = "" };
        var selected = Idea with { MissingData = "" };
        var concepts = new List<GameConcept> { first, selected };
        typeof(StudioWindow).GetField("_concepts", flags)!.SetValue(window, concepts);
        typeof(StudioWindow).GetField("_writingConcept", flags)!.SetValue(window, selected);
        typeof(StudioWindow).GetMethod("OfferPreparation", flags)!.Invoke(window,
            new object[] { StudioDesigner.MissingDataPrefix + "velocidad aún sin comprobar" });
        Assert.That(concepts[0], Is.EqualTo(first));
        Assert.That(concepts[1].MissingData, Does.Contain("velocidad"));
        Assert.That(concepts[1].PlayerRequest, Is.EqualTo(selected.PlayerRequest));
        Assert.That(concepts[1].Direction, Is.EqualTo(selected.Direction));
    }
    private sealed class ResearchAdvisor : IPlayAdvisor, IPreparationExperimentAdvisor
    {
        public string Name => "Asesor simulado";
        public Task<PlayAdvice> AdviseAsync(PlayObservation o, CancellationToken ct) => Task.FromResult(new PlayAdvice());
        public Task<List<LabExperiment>> PlanExperimentsAsync(PreparationExperimentQuestion q, CancellationToken ct) => Task.FromResult(new List<LabExperiment>());
    }

    private sealed class ResearchClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan time) => _ticks += time.Ticks;
    }

    [Test]
    public async Task MissingRequirementsTriggerAnotherResearchRoundAfterMoreThanTwentySimulatedMinutes()
    {
        var clock = new ResearchClock();
        int reviews = 0;
        var optionsSeen = new List<PrepareOptions>();
        var workflow = new FusionPreparationWorkflow { Clock = clock, Prepare = (path, options, _, _) =>
        {
            optionsSeen.Add(options); clock.Advance(TimeSpan.FromMinutes(6));
            var result = Prepared(path);
            result.Experiments.Add(new("plan-" + options.InvestigationPass, "Otra prueba", "Efecto observado."));
            return Task.FromResult(result);
        } };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", new ResearchAdvisor(),
            (idea, _, _, _) =>
            {
                clock.Advance(TimeSpan.FromMinutes(6));
                return Task.FromResult(idea with { MissingData = ++reviews == 1 ? "Falta comprobar la cámara." : "" });
            }, true, null, CancellationToken.None);
        Assert.That(result.ResearchPasses, Is.EqualTo(2));
        Assert.That(result.Concept.MissingData, Is.Empty);
        Assert.That(clock.GetTimestamp(), Is.EqualTo(TimeSpan.FromMinutes(36).Ticks));
        Assert.That(optionsSeen, Has.Count.EqualTo(4));
        Assert.That(optionsSeen[2].Goal, Does.Contain("cámara"));
        Assert.That(optionsSeen[2].PreviousExperiments, Has.Count.EqualTo(1));
        Assert.That(optionsSeen[2].PreviousProfile!.Variables, Does.ContainKey("x"));
        Assert.That(optionsSeen.All(o => o.ContinueResearch && o.MaxRounds == 24), Is.True);
        Assert.That(result.Concept.PlayerRequest, Is.EqualTo(Idea.PlayerRequest));
        Assert.That(result.Concept.Direction, Is.EqualTo(Idea.Direction));
    }

    [Test]
    public async Task LongDesignCannotPushTheMissingRequirementsOutOfThePreparationGoal()
    {
        var idea = Idea with { Design = new string('X', 12000), PlayerRequest = "Conservar la física original." };
        var goals = new List<string>();
        var workflow = new FusionPreparationWorkflow { Prepare = (path, options, _, _) =>
        { goals.Add(options.Goal); return Task.FromResult(Prepared(path)); } };
        await workflow.RunAsync(idea, _slots, _targets, "", null, null, false, null, CancellationToken.None);
        Assert.That(goals.All(g => g.Length <= 4000 && g.Contains(Idea.MissingData) && g.Contains(idea.PlayerRequest)), Is.True);
    }

    [Test]
    public async Task EmptyPlansAndIdenticalEvidenceStopRatherThanRetryingTheSameWorkForever()
    {
        int reviews = 0;
        var workflow = new FusionPreparationWorkflow { Prepare = (path, _, _, _) => Task.FromResult(Prepared(path)) };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", new ResearchAdvisor(),
            (idea, _, _, _) => { reviews++; return Task.FromResult(idea); }, false, null, CancellationToken.None);
        Assert.That(reviews, Is.EqualTo(4));
        Assert.That(result.StopReason, Does.Contain("tres rondas"));
        Assert.That(result.Concept.MissingData, Is.EqualTo(Idea.MissingData));
        Assert.That(result.Preparations, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ResearchTimeLimitKeepsVerifiedResultsWithoutClearingUnresolvedRequirements()
    {
        var clock = new ResearchClock();
        var workflow = new FusionPreparationWorkflow { Clock = clock, Prepare = (path, _, _, _) =>
        { clock.Advance(TimeSpan.FromMinutes(26)); return Task.FromResult(Prepared(path)); } };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", new ResearchAdvisor(),
            (idea, _, _, _) => Task.FromResult(idea), false, null, CancellationToken.None);
        Assert.That(result.StopReason, Does.Contain("tiempo"));
        Assert.That(result.Concept.MissingData, Is.EqualTo(Idea.MissingData));
        Assert.That(result.Preparations, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ResearchTimeoutInterruptsAnAwaitAndReportsAPartialResultRatherThanUserCancellation()
    {
        var workflow = new FusionPreparationWorkflow { ResearchTime = TimeSpan.FromMilliseconds(60),
            Prepare = async (_, _, _, ct) =>
            { await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new AssertionException("Should be canceled"); } };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", new ResearchAdvisor(),
            (idea, _, _, _) => Task.FromResult(idea), false, null, CancellationToken.None);
        Assert.That(result.StopReason, Does.Contain("tiempo"));
        Assert.That(result.Concept.MissingData, Is.Not.Empty);
    }

    [Test]
    public async Task EachRoundMayUpdateItsOwnSavedProfileWithoutBeingMistakenForAnExternalEdit()
    {
        int reviews = 0;
        var workflow = new FusionPreparationWorkflow { Prepare = (path, options, _, _) =>
        {
            var prepared = Prepared(path);
            prepared.Experiments.Add(new("round-" + options.InvestigationPass, "Prueba propia", "Observado"));
            prepared.ProfilePath = Path.Combine(_folder, prepared.Profile!.Id + ".json");
            File.WriteAllText(prepared.ProfilePath, prepared.Profile.ToJson() + "\n" + options.InvestigationPass);
            return Task.FromResult(prepared);
        } };
        var result = await workflow.RunAsync(Idea, _slots, _targets, "", new ResearchAdvisor(),
            (idea, _, _, _) => Task.FromResult(idea with { MissingData = ++reviews < 3 ? idea.MissingData : "" }),
            false, null, CancellationToken.None);
        Assert.That(result.ResearchPasses, Is.EqualTo(3));
        result.EnsureOriginalsUnchanged();
        File.AppendAllText(result.Preparations[0].ProfilePath!, "external edit");
        Assert.Throws<InvalidDataException>(result.EnsureOriginalsUnchanged);
    }

    [Test]
    public void UpfrontEstimateCoversBothTwentyMinuteResearchAndAWholeHour()
    {
        var range = FusionResearchPlan.Estimate(2, true);
        Assert.That(range.Low, Is.EqualTo(TimeSpan.FromMinutes(20)));
        Assert.That(range.High, Is.EqualTo(TimeSpan.FromHours(1)));
        Assert.That(FusionResearchPlan.EstimateText(2, true), Does.Contain("20–60"));
        Assert.That(FusionResearchPlan.Estimate(2, false).High, Is.LessThan(range.High));
        Assert.That(FusionResearchPlan.Calls(3, 3),
            Is.GreaterThan(FusionResearchPlan.MaxPasses * 3 * (FusionResearchPlan.NavigationRounds - FusionResearchPlan.TrialRounds + 2)));
    }

    [Test]
    public async Task ReturningFromWritingRetainsTheLastPrivateCheckpointAndTheExperimentHistory()
    {
        var observed = new List<PrepareOptions>();
        var workflow = new FusionPreparationWorkflow { Prepare = (path, options, _, _) =>
        {
            observed.Add(options);
            var prepared = Prepared(path);
            prepared.Continuation = new("nes", prepared.Sha256, [1, 2, (byte)observed.Count], prepared.Boot, 500);
            prepared.Experiments.Add(new("known-plan", "Prueba anterior", "Comprobación anterior"));
            return Task.FromResult(prepared);
        } };
        var first = await workflow.RunAsync(Idea, _slots, _targets, "", null, null, false, null, CancellationToken.None);
        var targets = _targets.Select(t => t with { Source = first.Sources[t.Source.Profile.Id] }).ToArray();
        await workflow.RunAsync(Idea, first.Slots, targets, "", null, null, false, null, CancellationToken.None,
            priorResearch: first.Preparations);
        Assert.That(observed[2].Resume, Is.SameAs(first.Preparations[0].Continuation));
        Assert.That(observed[2].PreviousExperiments.Any(e => e.Signature == "known-plan"), Is.True);
    }

}
