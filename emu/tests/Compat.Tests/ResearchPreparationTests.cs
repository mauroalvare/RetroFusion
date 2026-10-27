using Retro.Compat;
using Retro.Core;
using Retro.Fusion;
using Retro.Systems.Nes;

namespace Compatibility.Tests;

[TestFixture, NonParallelizable]
public sealed class ResearchPreparationTests
{
    private string _folder = "", _oldProfiles = "";
    [SetUp]
    public void Setup()
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-research-" + Guid.NewGuid())).FullName;
        _oldProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES") ?? "";
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_folder, "profiles"));
    }
    [TearDown]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _oldProfiles.Length == 0 ? null : _oldProfiles);
        Directory.Delete(_folder, true);
    }
    private string Rom()
    {
        string path = Path.Combine(_folder, "own.nes");
        File.WriteAllBytes(path, PreparationNesFixture.Rom());
        return path;
    }
    private static LabExperiment Walk() => new()
    {
        Kind = "change", Description = "Caminar hacia la siguiente zona", Frames = 90,
        Steps = { new() { Frame = 0, Keys = "P1 RIGHT", Hold = 60 } },
    };
    private sealed class Advisor : IPlayAdvisor, IPreparationExperimentAdvisor
    {
        public string Name => "IA de prueba";
        public PreparationExperimentQuestion? Question;
        public Action? Planning;
        public LabExperiment Experiment = Walk();
        public Task<PlayAdvice> AdviseAsync(PlayObservation o, CancellationToken ct) => Task.FromResult(
            o.PlayCheck.Contains("Si esto ya es la partida") ? new PlayAdvice { Playing = true }
                : new PlayAdvice { Actions = { new("P1 START", 6, 150) } });
        public Task<List<LabExperiment>> PlanExperimentsAsync(PreparationExperimentQuestion q, CancellationToken ct)
        {
            Question = q; Planning?.Invoke();
            return Task.FromResult(new List<LabExperiment> { Experiment });
        }
    }
    private static PrepareOptions Options(Advisor advisor) => new()
    {
        Goal = "Llegar a otra zona y observar sus variables.", Advisor = advisor,
        MaxRounds = 4, TrialRounds = 0, LabFrames = 120, Save = false, ContinueResearch = true,
    };

    [Test]
    public async Task NextInvestigationContinuesActualPlayAndKeepsTheOriginalReplayableBoot()
    {
        string path = Rom(); var advisor = new Advisor();
        var first = await GamePreparer.PrepareAsync(path, Options(advisor), null, CancellationToken.None);
        Assert.That(first.Ok, Is.True, first.Problem);
        Assert.That(first.Continuation, Is.Not.Null);
        var probe = new Nes(NesCartridge.Load(File.ReadAllBytes(path)));
        probe.LoadState(first.Continuation!.State);
        byte firstX = probe.Memory.Peek(0x10);
        var options = Options(advisor);
        options.Resume = first.Continuation; options.PreviousProfile = first.Profile;
        options.PreviousExperiments = first.Experiments; options.InvestigationPass = 2;
        var second = await GamePreparer.PrepareAsync(path, options, null, CancellationToken.None);
        Assert.That(second.Ok, Is.True, second.Problem);
        probe.LoadState(second.Continuation!.State);
        Assert.That(probe.Memory.Peek(0x10), Is.GreaterThan(firstX));
        Assert.That(second.Boot, Is.EqualTo(first.Boot));
        Assert.That(second.Profile!.Id, Is.EqualTo(first.Profile!.Id));
        Assert.That(advisor.Question!.PreviousExperiments, Is.Not.Empty);
        Assert.That(advisor.Question.Pass, Is.EqualTo(2));
        Assert.That(advisor.Question.Describe(), Does.Contain(first.Experiments[0].Outcome));
        Assert.That(second.Experiments.Single().Signature, Is.Not.EqualTo(first.Experiments.Single().Signature));
    }

    [Test]
    public async Task ResearchFromTheSameStateDoesNotRunAnIdenticalExperimentWithANewDescription()
    {
        string path = Rom(); var advisor = new Advisor();
        var first = await GamePreparer.PrepareAsync(path, Options(advisor), null, CancellationToken.None);
        var options = Options(advisor);
        options.PreviousExperiments = first.Experiments;
        // No checkpoint: the same deterministic initial game state.
        var second = await GamePreparer.PrepareAsync(path, options, null, CancellationToken.None);
        Assert.That(second.Experiments, Has.Count.EqualTo(1));
        Assert.That(second.Experiments[0].Skipped, Is.True);
    }

    [Test]
    public void ACheckpointFromAnotherEditionIsRejectedBeforeItCanBeLoaded()
    {
        var options = Options(new Advisor());
        options.Resume = new("nes", new string('f', 64), [1, 2, 3], [], 0);
        Assert.ThrowsAsync<InvalidDataException>(() =>
            GamePreparer.PrepareAsync(Rom(), options, null, CancellationToken.None));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task NewWriteEvidenceIsKeptWithoutLosingOrRedirectingAnOlderObservation(bool sameField)
    {
        string path = Rom();
        var options = Options(new Advisor { Experiment = new() { Kind = "score", Description = "Segunda acción", Frames = 120,
            Steps = { new() { Frame = 0, Keys = "P1 B", Hold = 100 } } } });
        options.PreviousProfile = new()
        {
            Id = "known", Name = "Propio", Machine = "nes",
            Match = new() { Sha256 = [GamePreparer.Sha256Of(new("own.nes", File.ReadAllBytes(path), path))] },
            Variables = { ["score"] = new() { Address = sameField ? "0014" : "0013", ReadOnly = true } },
            Controls = { ["jump"] = "P1 B" },
        };
        var result = await GamePreparer.PrepareAsync(path, options, null, CancellationToken.None);
        Assert.That(result.Ok, Is.True, result.Problem);
        Assert.That(result.Profile!.Variables["score"].Address, Is.EqualTo(sameField ? "0014" : "0013"));
        Assert.That(result.Profile.Variables["score"].ReadOnly, Is.EqualTo(!sameField));
        Assert.That(result.Profile.Variables.Any(v => v.Value.Address == "0014" && !v.Value.ReadOnly), Is.True);
        Assert.That(result.Profile.Controls["jump"], Is.EqualTo("P1 A"));
    }

    [Test]
    public async Task AnExternalProfileEditDuringResearchIsPreserved()
    {
        string path = Rom(); var advisor = new Advisor();
        var options = Options(advisor); options.Save = true;
        var first = await GamePreparer.PrepareAsync(path, options, null, CancellationToken.None);
        string changed = File.ReadAllText(first.ProfilePath!) + "\nexternal edit";
        advisor.Planning = () => File.WriteAllText(first.ProfilePath!, changed);
        options.PreviousProfile = first.Profile;
        Assert.ThrowsAsync<InvalidDataException>(() =>
            GamePreparer.PrepareAsync(path, options, null, CancellationToken.None));
        Assert.That(File.ReadAllText(first.ProfilePath!), Is.EqualTo(changed));
    }

    [TestCase("compare")]
    [TestCase("verify")]
    [TestCase("advance")]
    public void CancellationDuringAnExperimentRestoresItsPrivateMachineAndReleasesKeys(string operation)
    {
        var machine = new Nes(NesCartridge.Load(PreparationNesFixture.Rom()));
        byte[] before = machine.SaveState();
        using var cancel = new CancellationTokenSource();
        cancel.CancelAfter(TimeSpan.FromMilliseconds(5));
        var experiment = Walk(); experiment.Frames = 10000;
        Assert.Throws<OperationCanceledException>(() =>
        {
            switch (operation)
            {
                case "compare": LabExperiments.Run(machine, "nes", experiment, ct: cancel.Token); break;
                case "verify": LabVerifier.Verify(machine, new() { Kind = "change", Address = "0010" }, experiment, frames: 10000, ct: cancel.Token); break;
                case "advance": PreparationResearch.Advance(machine, experiment, cancel.Token); break;
            }
        });
        Assert.That(machine.SaveState(), Is.EqualTo(before));
    }

    [Test]
    public void ExperimentIdentityUsesActualInputsAndStateRatherThanItsDescription()
    {
        var one = Walk(); var two = Walk(); two.Description = "Otra descripción";
        Assert.That(PreparationResearch.Signature(one, [1, 2]), Is.EqualTo(PreparationResearch.Signature(two, [1, 2])));
        Assert.That(PreparationResearch.Signature(one, [1, 2]), Is.Not.EqualTo(PreparationResearch.Signature(two, [1, 3])));
    }
}
