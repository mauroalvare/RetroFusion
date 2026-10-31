using System.Text.Json;
using System.Text.Json.Nodes;
using Retro.Compat;
using Retro.Fusion;
using Retro.Systems.Nes;

namespace Compat.Tests;

[TestFixture]
public sealed class AutonomousResearchTests
{
    private string _folder = "", _path = "";
    private GameProfile _profile = null!;

    [SetUp]
    public void Setup()
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-autonomous-" + Guid.NewGuid())).FullName;
        _path = Path.Combine(_folder, "own.nes");
        File.WriteAllBytes(_path, Compatibility.Tests.PreparationNesFixture.Rom());
        _profile = new GameProfile { Id = "own-autonomous", Name = "Programa propio", Machine = "nes" };
    }

    [TearDown]
    public void Cleanup() => Directory.Delete(_folder, true);

    private AutonomousResearchStore Store() => new(Path.Combine(_folder, "research"));
    private static AutonomousResearchOptions Options(int passes = 1, int calls = 0, int target = 12) =>
        new(TargetVariables: target, Minutes: 2, AiCalls: calls, MaxPasses: passes, LabFrames: 120,
            Goal: "Contrastar el reloj y el movimiento; conservar hipótesis sin inventar significados.");
    private static LabFinding Finding(string address = "0010", string status = "hypothesis") =>
        new() { Address = address, Kind = "position-x", Status = status, Hypothesis = "Posición propuesta; significado pendiente.", Evidence = ["Cambió bajo la entrada de prueba."] };
    private static LabReport Report(params LabFinding[] findings) => new() { Game = "Programa propio", Machine = "nes", Findings = findings.ToList() };
    private static PrepareResult Result(LabReport? report = null) => new() { Ok = true, Lab = report };

    [Test]
    public async Task TheRequestedQuantityDoesNotBecomeFabricatedFindingsAndStagnationStopsFurtherRounds()
    {
        int passes = 0;
        var options = Options(passes: 8, target: 24);
        var engine = new AutonomousResearch { Prepare = (_, input, _, _) =>
        {
            passes++;
            Assert.That(input.Goal, Is.EqualTo(options.Goal));
            Assert.That(input.Save, Is.False, "The private investigation must not write a final profile implicitly");
            Assert.That(input.ContinueResearch, Is.True);
            Assert.That(input.LabFrames, Is.EqualTo(options.LabFrames));
            return Task.FromResult(Result(Report(Finding())));
        } };

        var work = await engine.RunAsync(_path, _profile, _folder, options);

        Assert.Multiple(() =>
        {
            Assert.That(passes, Is.EqualTo(3), "One new finding followed by two rounds without new evidence");
            Assert.That(work.CompletedPasses, Is.EqualTo(passes));
            Assert.That(work.Candidates, Is.EqualTo(1));
            Assert.That(work.VerifiedWrites, Is.Zero);
            Assert.That(work.Options.TargetVariables, Is.EqualTo(24));
            Assert.That(work.StopReason, Does.Contain("sin evidencia nueva").And.Contain("no garantiza"));
            Assert.That(_profile.Variables, Is.Empty);
        });
    }

    [Test]
    public async Task ProgressSeparatesCandidatesFromVerifiedWriteEffectsAndDoesNotDowngradeEvidence()
    {
        var progress = new List<AutonomousResearchProgress>();
        var engine = new AutonomousResearch { Prepare = (_, input, _, _) =>
        {
            var observed = Report(Finding("0010", "verified"), Finding("0011"));
            observed.CheckedControls["right"] = "P1 RIGHT: movimiento observado";
            observed.Notes.Add("El control se comparó con una corrida sin entradas.");
            input.EvidenceUpdated!(observed);
            return Task.FromResult(Result(Report(Finding("0010"), Finding("0012", "inconclusive"))));
        } };

        var work = await engine.RunAsync(_path, _profile, _folder, Options(), progress: progress.Add);

        Assert.Multiple(() =>
        {
            Assert.That(work.Candidates, Is.EqualTo(3));
            Assert.That(work.VerifiedWrites, Is.EqualTo(1));
            Assert.That(work.Evidence.Findings.Single(f => f.Address == "0010").Status, Is.EqualTo("verified"));
            Assert.That(progress.Any(p => p.Candidates == 2 && p.VerifiedWrites == 1), Is.True);
            Assert.That(progress.Last().Candidates, Is.EqualTo(3));
            Assert.That(progress.Last().VerifiedWrites, Is.EqualTo(1));
            Assert.That(work.Evidence.Findings[0].Hypothesis, Does.Contain("pendiente"), "A write effect does not certify the proposed meaning");
            Assert.That(work.Evidence.CheckedControls["right"], Does.Contain("movimiento observado"));
            Assert.That(work.Evidence.Notes, Does.Contain("El control se comparó con una corrida sin entradas."));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ANewVerifiedRefutationCanReplaceAnEarlierVerifiedWriteButAnUnsupportedClaimCannot(bool verifiedRefutation)
    {
        var earlier = Finding("0010", "verified");
        earlier.Verification = new() { Result = "verified", Experiment = "Contraste inicial", Observations = ["Efecto inicialmente observado"] };
        var later = Finding("0010", "refuted");
        if (verifiedRefutation)
            later.Verification = new() { Result = "refuted", Experiment = "Contraste posterior", Observations = ["El original recalcula el dato"] };
        var engine = new AutonomousResearch { Prepare = (_, input, _, _) =>
        {
            input.EvidenceUpdated!(Report(earlier));
            return Task.FromResult(Result(Report(later)));
        } };

        var work = await engine.RunAsync(_path, _profile, _folder, Options());

        Assert.That(work.Evidence.Findings.Single().Status, Is.EqualTo(verifiedRefutation ? "refuted" : "verified"));
        Assert.That(work.VerifiedWrites, Is.EqualTo(verifiedRefutation ? 0 : 1));
        if (verifiedRefutation) Assert.That(work.Evidence.Findings.Single().Verification!.Experiment, Is.EqualTo("Contraste posterior"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AChangedEngineOrProfileKeepsHistoricalEvidenceButRequiresFreshVerificationForTheTarget(bool engineChanged)
    {
        var original = Finding("0010", "verified");
        original.Verification = new() { Result = "verified", Experiment = "Contraste del contexto anterior", Observations = ["Observación anterior conservada"] };
        var first = await new AutonomousResearch { Prepare = (_, _, _, _) => Task.FromResult(Result(Report(original))) }
            .RunAsync(_path, _profile, _folder, Options(target: 1));
        if (engineChanged) first.Engine = "another-engine";
        else _profile.Description = "Descripción del perfil modificada";
        int calls = 0;
        int? verifiedAtRestart = null;
        var next = new AutonomousResearch { Prepare = (_, input, _, _) =>
        {
            calls++;
            Assert.That(input.Resume, Is.Null);
            if (calls == 1) return Task.FromResult(Result(Report(Finding())));
            var fresh = Finding("0010", "verified");
            fresh.Verification = new() { Result = "verified", Experiment = "Nuevo contraste", Observations = ["Efecto contrastado de nuevo"] };
            return Task.FromResult(Result(Report(fresh)));
        } };

        var work = await next.RunAsync(_path, _profile, _folder, Options(passes: 2, target: 1), previous: first, checkpoint: point =>
        {
            if (verifiedAtRestart.HasValue) return;
            verifiedAtRestart = point.VerifiedWrites;
            var history = point.Evidence.Findings.Single();
            Assert.That(history.Status, Is.EqualTo("inconclusive"));
            Assert.That(history.Evidence, Has.Some.Contains("revalid"));
            Assert.That(history.Verification!.Observations, Does.Contain("Observación anterior conservada"));
        });

        Assert.That(verifiedAtRestart, Is.Zero);
        Assert.That(calls, Is.EqualTo(2), "An unverified later hypothesis must not satisfy the current target");
        Assert.That(work.VerifiedWrites, Is.EqualTo(1));
        Assert.That(work.Evidence.Findings.Single().Verification!.Experiment, Is.EqualTo("Nuevo contraste"));
    }

    [Test]
    public async Task APreparationThatDidNotConfirmPlayableBootEndsAsAnErrorAndRetainsItsPartialEvidence()
    {
        var work = await new AutonomousResearch { Prepare = (_, _, _, _) => Task.FromResult(new PrepareResult
            { Ok = false, Problem = "No se confirmó el arranque", Lab = Report(Finding()) }) }
            .RunAsync(_path, _profile, _folder, Options(passes: 3));

        Assert.That(work.Status, Is.EqualTo("error"));
        Assert.That(work.StopReason, Does.Contain("No se confirmó el arranque"));
        Assert.That(work.Candidates, Is.EqualTo(1));
    }

    [Test]
    public async Task ExhaustingTheAdvisorBudgetEndsAsALimitWithPartialEvidenceRatherThanACompletedObjective()
    {
        var advisor = new Advisor();
        var work = await new AutonomousResearch { Prepare = async (_, input, _, ct) =>
        {
            await input.Advisor!.AdviseAsync(new(), ct);
            return Result(Report(Finding()));
        } }.RunAsync(_path, _profile, _folder, Options(passes: 3, calls: 1, target: 2), advisor);

        Assert.That(work.Status, Is.EqualTo("limit"));
        Assert.That(work.StopReason, Does.Contain("presupuesto de pedidos"));
        Assert.That(work.Candidates, Is.EqualTo(1));
        Assert.That(work.VerifiedWrites, Is.Zero);
        Assert.That(advisor.Calls, Has.Count.EqualTo(1));
    }

    [TestCase("lab-sha")]
    [TestCase("lab-machine")]
    [TestCase("profile-sha")]
    [TestCase("profile-machine")]
    public async Task AReturnedLabOrProfileFromAnotherEditionOrMachineIsRejectedBeforeItCanEnterTheCheckpoint(string mismatch)
    {
        var store = Store();
        var lab = Report(Finding("0010", "verified"));
        var profile = GameProfile.Parse(_profile.ToJson());
        profile.Match.Sha256 = [AutonomousResearch.FileHash(_path)];
        if (mismatch == "lab-sha") lab.Sha256 = new string('F', 64);
        if (mismatch == "lab-machine") lab.Machine = "c64";
        if (mismatch == "profile-sha") profile.Match.Sha256 = [new string('F', 64)];
        if (mismatch == "profile-machine") profile.Machine = "c64";
        var engine = new AutonomousResearch { Prepare = (_, _, _, _) => Task.FromResult(new PrepareResult { Ok = true, Lab = lab, Profile = profile }) };

        var work = await engine.RunAsync(_path, _profile, _folder, Options(), checkpoint: store.Save);

        Assert.That(work.Status, Is.EqualTo("error"));
        Assert.That(work.CompletedPasses, Is.Zero);
        Assert.That(work.Candidates, Is.Zero);
        Assert.That(store.Read().Single().Profile!.Machine, Is.EqualTo("nes"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PausingOrReachingTheDeadlineSavesTheBriefAndPartialEvidenceBeforeTheFirstRoundCompletes(bool deadline)
    {
        var store = Store();
        var clock = new ManualClock();
        using var cancellation = new CancellationTokenSource();
        var options = Options();
        bool initialSaved = false, observationSaved = false;
        var engine = new AutonomousResearch { Clock = clock, Prepare = async (_, input, _, ct) =>
        {
            var initial = store.Read().Single();
            initialSaved = initial.Options.Goal == options.Goal && initial.CompletedPasses == 0 && initial.Candidates == 0;
            input.EvidenceUpdated!(Report(Finding()));
            var partial = store.Read().Single();
            observationSaved = partial.Candidates == 1 && partial.CompletedPasses == 0;
            if (deadline) clock.Advance(TimeSpan.FromMinutes(options.Minutes));
            else { clock.Advance(TimeSpan.FromSeconds(7)); cancellation.Cancel(); }
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new AssertionException("The canceled round must not complete");
        } };

        var work = await engine.RunAsync(_path, _profile, _folder, options, checkpoint: store.Save, ct: cancellation.Token);
        var saved = store.Read().Single();

        Assert.Multiple(() =>
        {
            Assert.That(initialSaved, Is.True);
            Assert.That(observationSaved, Is.True);
            Assert.That(work.Status, Is.EqualTo(deadline ? "limit" : "paused"));
            Assert.That(saved.Status, Is.EqualTo(work.Status));
            Assert.That(saved.Options.Goal, Is.EqualTo(options.Goal));
            Assert.That(saved.CompletedPasses, Is.Zero);
            Assert.That(saved.Candidates, Is.EqualTo(1));
            Assert.That(saved.ElapsedSeconds, Is.EqualTo(deadline ? 120 : 7));
            Assert.That(saved.Id, Is.EqualTo(work.Id));
        });
    }

    [Test]
    public async Task CancellationWhenPreparationReturnsDoesNotApproveItsLateResultOrLoseEarlierEvidence()
    {
        using var cancellation = new CancellationTokenSource();
        var store = Store();
        var engine = new AutonomousResearch { Prepare = (_, input, _, _) =>
        {
            input.EvidenceUpdated!(Report(Finding("0010")));
            cancellation.Cancel();
            return Task.FromResult(Result(Report(Finding("0011", "verified"))));
        } };

        var work = await engine.RunAsync(_path, _profile, _folder, Options(), checkpoint: store.Save, ct: cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(work.Status, Is.EqualTo("paused"));
            Assert.That(work.CompletedPasses, Is.Zero);
            Assert.That(work.Evidence.Findings.Select(f => f.Address), Is.EqualTo(new[] { "0010" }));
            Assert.That(store.Read().Single().Status, Is.EqualTo("paused"));
        });
    }

    [Test]
    public async Task ResumingPassesTheSavedMachineContinuationProfileAndExperimentHistoryToPreparation()
    {
        var machine = new Nes(NesCartridge.Load(File.ReadAllBytes(_path)));
        var continuation = new PreparationContinuation("nes", AutonomousResearch.FileHash(_path), machine.SaveState(), ["wait 1"], 1);
        var known = GameProfile.Parse(_profile.ToJson());
        known.Variables["clock"] = new() { Address = "0014", ReadOnly = true };
        var previousExperiment = new PreparationExperimentResult("first", "Comparar reloj", "Solo candidato; falta contraste.");
        var engine = new AutonomousResearch { Prepare = (_, _, _, _) => Task.FromResult(new PrepareResult
        {
            Ok = true, Profile = known, Continuation = continuation, Lab = Report(Finding()),
            Experiments = [previousExperiment], CheckedData = ["Control observado"],
        }) };
        var first = await engine.RunAsync(_path, _profile, _folder, Options());
        bool resumed = false;
        var next = new AutonomousResearch { Prepare = (_, input, _, _) =>
        {
            resumed = true;
            Assert.That(input.Resume!.State, Is.EqualTo(continuation.State));
            Assert.That(input.Resume.Boot, Is.EqualTo(continuation.Boot));
            Assert.That(input.InvestigationPass, Is.EqualTo(2));
            Assert.That(input.PreviousExperiments, Is.EqualTo(new[] { previousExperiment }));
            Assert.That(input.PreviousProfile!.Variables["clock"].Address, Is.EqualTo("0014"));
            return Task.FromResult(new PrepareResult { Ok = true, Lab = Report(Finding("0011")),
                Continuation = continuation, Experiments = [new("second", "Caminar", "Diferencia observada.")],
                CheckedData = ["Control observado", "Segunda comparación"] });
        } };

        var work = await next.RunAsync(_path, _profile, _folder, Options(), previous: first);

        Assert.Multiple(() =>
        {
            Assert.That(resumed, Is.True);
            Assert.That(work.Id, Is.EqualTo(first.Id));
            Assert.That(work.CompletedPasses, Is.EqualTo(2));
            Assert.That(work.Candidates, Is.EqualTo(2));
            Assert.That(work.Experiments.Select(e => e.Signature), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(work.Checked, Is.EqualTo(new[] { "Control observado", "Segunda comparación" }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AChangedOriginalOrFirmwareIsRejectedBeforeContinuingWithoutMutatingSavedEvidence(bool firmware)
    {
        if (firmware) UseSpectrum();
        int preparations = 0;
        var engine = new AutonomousResearch { Prepare = (_, _, _, _) =>
        { preparations++; return Task.FromResult(Result(Report(Finding()))); } };
        var work = await engine.RunAsync(_path, _profile, _folder, Options());
        string before = JsonSerializer.Serialize(work, GameProfile.Json);
        string changed = firmware ? Path.Combine(_folder, "48.rom") : _path;
        byte[] bytes = File.ReadAllBytes(changed); bytes[^1] ^= 1; File.WriteAllBytes(changed, bytes);

        Assert.ThrowsAsync<InvalidDataException>(() => engine.RunAsync(_path, _profile, _folder, Options(), previous: work));

        Assert.That(preparations, Is.EqualTo(1));
        Assert.That(JsonSerializer.Serialize(work, GameProfile.Json), Is.EqualTo(before));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EvidenceFromAnOriginalOrFirmwareChangedDuringPreparationIsNotMerged(bool firmware)
    {
        if (firmware) UseSpectrum();
        var store = Store();
        var engine = new AutonomousResearch { Prepare = (_, input, _, _) =>
        {
            string changed = firmware ? Path.Combine(_folder, "48.rom") : _path;
            byte[] bytes = File.ReadAllBytes(changed); bytes[^1] ^= 1; File.WriteAllBytes(changed, bytes);
            input.EvidenceUpdated!(Report(Finding("0015", "verified")));
            return Task.FromResult(Result());
        } };

        var work = await engine.RunAsync(_path, _profile, _folder, Options(), checkpoint: store.Save);

        Assert.Multiple(() =>
        {
            Assert.That(work.Status, Is.EqualTo("error"));
            Assert.That(work.StopReason, Does.Contain("Cambió el original o el firmware"));
            Assert.That(work.CompletedPasses, Is.Zero);
            Assert.That(work.Candidates, Is.Zero, "A checkpoint must not mix observations from another edition");
            Assert.That(store.Read().Single().Candidates, Is.Zero);
        });
    }

    private void UseSpectrum()
    {
        _profile.Machine = "zx48";
        _path = Path.Combine(_folder, "own.z80");
        File.WriteAllBytes(_path, new byte[30 + 49152]);
        File.WriteAllBytes(Path.Combine(_folder, "48.rom"), new byte[16384]);
    }

    [Test]
    public async Task PublishedEvidenceDoesNotRetainMutableReferencesFromThePreparationResult()
    {
        var observed = Finding("0010", "verified");
        var engine = new AutonomousResearch { Prepare = (_, input, _, _) =>
        {
            input.EvidenceUpdated!(Report(observed));
            observed.Status = "hypothesis";
            observed.Evidence.Clear();
            observed.Hypothesis = "Texto posterior que no debe reemplazar la observación guardada";
            return Task.FromResult(Result());
        } };

        var work = await engine.RunAsync(_path, _profile, _folder, Options());

        Assert.That(work.VerifiedWrites, Is.EqualTo(1));
        Assert.That(work.Evidence.Findings[0].Evidence, Is.EqualTo(new[] { "Cambió bajo la entrada de prueba." }));
        Assert.That(work.Evidence.Findings[0].Hypothesis, Does.Contain("significado pendiente"));
    }

    private sealed class Advisor : IPlayAdvisor, IVariableNamer, IPreparationExperimentAdvisor
    {
        public string Name => "Asesor simulado";
        public List<string> Calls { get; } = [];
        public bool FailNavigation;
        public Task<PlayAdvice> AdviseAsync(PlayObservation observation, CancellationToken ct)
        {
            Calls.Add("navigation");
            if (FailNavigation) throw new InvalidOperationException("Respuesta fallida del asesor");
            return Task.FromResult(new PlayAdvice());
        }
        public Task<List<VariableName>> NameVariablesAsync(VariableQuestion question, CancellationToken ct)
        { Calls.Add("naming"); return Task.FromResult(new List<VariableName>()); }
        public Task<List<LabExperiment>> PlanExperimentsAsync(PreparationExperimentQuestion question, CancellationToken ct)
        { Calls.Add("experiments"); return Task.FromResult(new List<LabExperiment>()); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AllAdvisorInterfacesAndFailedRepliesShareTheSameCallBudget(bool navigationFails)
    {
        var advisor = new Advisor { FailNavigation = navigationFails };
        int preparations = 0;
        var engine = new AutonomousResearch { Prepare = async (_, input, _, ct) =>
        {
            preparations++;
            try { await input.Advisor!.AdviseAsync(new(), ct); }
            catch (InvalidOperationException) when (navigationFails) { }
            await ((IVariableNamer)input.Advisor!).NameVariablesAsync(new(), ct);
            await ((IPreparationExperimentAdvisor)input.Advisor!).PlanExperimentsAsync(new(), ct);
            await input.Advisor!.AdviseAsync(new(), ct);
            throw new AssertionException("A fourth request must not reach the provider");
        } };

        var work = await engine.RunAsync(_path, _profile, _folder, Options(passes: 8, calls: 3), advisor);

        Assert.Multiple(() =>
        {
            Assert.That(preparations, Is.EqualTo(1));
            Assert.That(advisor.Calls, Is.EqualTo(new[] { "navigation", "naming", "experiments" }));
            Assert.That(work.AiCallsUsed, Is.EqualTo(3));
            Assert.That(work.Status, Is.EqualTo("error"));
            Assert.That(work.StopReason, Does.Contain("Límite de pedidos"));
        });
    }

    [Test]
    public async Task ZeroAiBudgetDoesNotPassAnAdvisorAndAnUnsuccessfulPreparationIsNotRetriedIndefinitely()
    {
        var advisor = new Advisor(); int preparations = 0;
        var engine = new AutonomousResearch { Prepare = (_, input, _, _) =>
        {
            preparations++;
            Assert.That(input.Advisor, Is.Null);
            return Task.FromResult(new PrepareResult { Ok = false, Problem = "No se confirmó una partida controlable", Lab = Report(Finding()) });
        } };

        var work = await engine.RunAsync(_path, _profile, _folder, Options(passes: 8), advisor);

        Assert.That(preparations, Is.EqualTo(1));
        Assert.That(work.StopReason, Does.Contain("No se confirmó"));
        Assert.That(work.Candidates, Is.EqualTo(1));
        Assert.That(work.VerifiedWrites, Is.Zero);
        Assert.That(work.AiCallsUsed, Is.Zero);
        Assert.That(advisor.Calls, Is.Empty);
    }

    [Test]
    public async Task ExplicitContinuationAddsItsBudgetWhileKeepingAccumulatedTimeAndCallCounts()
    {
        var advisor = new Advisor();
        var clock = new ManualClock();
        int pass = 0;
        var engine = new AutonomousResearch { Clock = clock, Prepare = async (_, input, _, ct) =>
        {
            pass++;
            await input.Advisor!.AdviseAsync(new(), ct);
            clock.Advance(TimeSpan.FromSeconds(10));
            return Result(Report(Finding($"00{pass:X2}")));
        } };
        var options = Options(calls: 1);
        var first = await engine.RunAsync(_path, _profile, _folder, options, advisor);
        Assert.That(first.AiCallsUsed, Is.EqualTo(1));
        Assert.That(first.ElapsedSeconds, Is.EqualTo(10));

        var resumed = await engine.RunAsync(_path, _profile, _folder, options, advisor, previous: first);

        Assert.That(pass, Is.EqualTo(2));
        Assert.That(advisor.Calls, Has.Count.EqualTo(2));
        Assert.That(resumed.AiCallsUsed, Is.EqualTo(2));
        Assert.That(resumed.ElapsedSeconds, Is.EqualTo(20));
        Assert.That(resumed.CompletedPasses, Is.EqualTo(2));
        Assert.That(resumed.Candidates, Is.EqualTo(2));
    }

    [Test]
    public async Task AnOversizedReplacementKeepsThePreviousCheckpointAndLeavesNoTemporaryFile()
    {
        var store = Store();
        var work = await new AutonomousResearch { Prepare = (_, _, _, _) => Task.FromResult(Result()) }
            .RunAsync(_path, _profile, _folder, Options());
        store.Save(work);
        string path = Path.Combine(store.Folder, work.Id + ".json");
        byte[] before = File.ReadAllBytes(path);
        work.StopReason = new string('X', AutonomousResearchStore.MaxBytes);

        Assert.Throws<IOException>(() => store.Save(work));

        Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        Assert.That(Directory.GetFiles(store.Folder, "*.json"), Is.EqualTo(new[] { path }));
        Assert.That(Directory.GetFiles(store.Folder, "*.tmp"), Is.Empty);
        Assert.That(new FileInfo(Path.Combine(store.Folder, ".store.lock")).Length, Is.Zero);
    }

    [TestCase("syntax")]
    [TestCase("oversize")]
    [TestCase("null-finding")]
    [TestCase("null-goal")]
    public async Task AnInvalidStoredFileDoesNotHideAValidCheckpointAndBothFilesArePreserved(string corruption)
    {
        var store = Store();
        var valid = await new AutonomousResearch { Prepare = (_, _, _, _) => Task.FromResult(Result(Report(Finding()))) }
            .RunAsync(_path, _profile, _folder, Options());
        store.Save(valid);
        string validPath = Path.Combine(store.Folder, valid.Id + ".json");
        byte[] validBytes = File.ReadAllBytes(validPath);
        string path = Path.Combine(store.Folder, new string('a', 32) + ".json");
        var broken = JsonNode.Parse(JsonSerializer.Serialize(valid, GameProfile.Json))!;
        broken["id"] = new string('a', 32);
        if (corruption == "null-finding") broken["evidence"]!["findings"] = new JsonArray { null };
        if (corruption == "null-goal") broken["options"]!["goal"] = null;
        string content = corruption switch
        {
            "syntax" => "{ damaged research",
            "oversize" => new string('X', AutonomousResearchStore.MaxBytes + 1),
            _ => broken.ToJsonString(),
        };
        File.WriteAllText(path, content);

        var recovered = store.Read();

        Assert.That(recovered.Select(w => w.Id), Is.EqualTo(new[] { valid.Id }));
        Assert.That(store.Problem, Is.Not.Null.And.Not.Empty, "A skipped private checkpoint must have a visible diagnostic");
        Assert.That(File.ReadAllText(path), Is.EqualTo(content));
        Assert.That(File.ReadAllBytes(validPath), Is.EqualTo(validBytes));
        Assert.That(Directory.GetFiles(store.Folder, "*.json"), Has.Length.EqualTo(2));
        Assert.That(Directory.GetFiles(store.Folder, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task StoredIdsCannotRedirectSaveReadOrRemovalOutsideTheResearchFolder()
    {
        var store = Store();
        var work = await new AutonomousResearch { Prepare = (_, _, _, _) => Task.FromResult(Result()) }
            .RunAsync(_path, _profile, _folder, Options());
        store.Save(work);
        string safePath = Path.Combine(store.Folder, work.Id + ".json");
        string outside = Path.Combine(_folder, "keep.json");
        File.WriteAllText(outside, "keep this player file");
        work.Id = "../keep";

        Assert.Throws<InvalidDataException>(() => store.Save(work));
        Assert.Throws<InvalidDataException>(() => store.Remove(work.Id));
        File.WriteAllText(safePath, JsonSerializer.Serialize(work, GameProfile.Json));
        Assert.That(store.Read(), Is.Empty);
        Assert.That(store.Problem, Is.Not.Null.And.Not.Empty);

        Assert.That(File.ReadAllText(outside), Is.EqualTo("keep this player file"));
        Assert.That(File.Exists(safePath), Is.True);
    }

    [Test]
    public void OriginalResolutionUsesTheSelectedExistingContainerRatherThanAStoredGameLabel()
    {
        string selected = _path + "!folder!member.nes";
        Assert.That(AutonomousResearch.ResolveOriginal(selected), Is.EqualTo(Path.GetFullPath(_path)));
        string missing = Path.Combine(_folder, "missing.nes") + "!member.nes";
        Assert.Throws<FileNotFoundException>(() => AutonomousResearch.ResolveOriginal(missing));
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        private readonly List<Timer> _timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state); _timers.Add(timer); timer.Change(dueTime, period); return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            Interlocked.Add(ref _ticks, elapsed.Ticks);
            foreach (var timer in _timers.ToArray()) timer.Fire();
        }
        private sealed class Timer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            private long _due = long.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            { _due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.GetTimestamp() + dueTime.Ticks; return true; }
            public void Fire() { if (_due <= clock.GetTimestamp()) { _due = long.MaxValue; callback(state); } }
            public void Dispose() => _due = long.MaxValue;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
