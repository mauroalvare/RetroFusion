using System.Text.Json.Nodes;
using Retro.Ai;
using Retro.Compat;
using Retro.Core;

namespace Compat.Tests;

[TestFixture]
public sealed class PreparationAdvisorTests
{
    private const string Plan = """
        {"experiments":[{"kind":"change","description":"Comparar salto y avance",
        "frames":120,"repeatEvery":0,"steps":[{"frame":2,"keys":"p1 right+P1 A","hold":8}]}]}
        """;

    private static PreparationExperimentQuestion Question(VideoFrame? picture = null) => new()
    {
        Goal = "Comprobar salto y avance sin exigir una partida manual.",
        Game = "Programa propio",
        Machine = "nes",
        MachineName = "NES",
        Picture = picture,
        Controls = new("Mando", "P1 LEFT", "P1 RIGHT", "P1 UP", "P1 DOWN", "P1 A"),
        Keys = ["P1 LEFT", "P1 RIGHT", "P1 A", "P1 B"],
        Candidates = [new("0060", "y", "binary", 1, 45, false, "Cambió al pulsar A; significado pendiente.")],
    };

    private sealed class Simulated(Func<AiRequest, CancellationToken, Task<string>> response) : IAiProvider
    {
        public string Name => "IA simulada";
        public List<AiRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return response(request, ct);
        }
    }

    [Test]
    public async Task GoalAndObservedCandidatesReachTheSameProviderAndOnlyInputsAreReturned()
    {
        var simulated = new Simulated((_, _) => Task.FromResult(Plan));
        var advisor = new AiPlayAdvisor(simulated);
        var question = Question();
        var result = await advisor.PlanExperimentsAsync(question, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(simulated.Requests, Has.Count.EqualTo(1));
            Assert.That(simulated.Requests[0].Turns.Single().Text, Does.Contain(question.Goal).And.Contain("0060").And.Contain("P1 B"));
            Assert.That(simulated.Requests[0].Schema, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Steps.Single().Keys, Is.EqualTo("P1 RIGHT+P1 A"));
            Assert.That(result[0].Steps.Single().Frame, Is.EqualTo(2));
            Assert.That(result[0].Steps.Single().Hold, Is.EqualTo(8));
            Assert.That(result[0].Frames, Is.EqualTo(120));
        });
    }

    [Test]
    public async Task PreparationGoalAlsoReachesNavigationAndVariableNaming()
    {
        var answers = new Queue<string>([
            """{"playing":false,"reason":"Título","actions":[]}""",
            """{"variables":[]}""",
        ]);
        var simulated = new Simulated((_, _) => Task.FromResult(answers.Dequeue()));
        var advisor = new AiPlayAdvisor(simulated);
        const string goal = "Investigar la altura al saltar.";
        await advisor.AdviseAsync(new PlayObservation { Goal = goal }, CancellationToken.None);
        await advisor.NameVariablesAsync(new VariableQuestion { Goal = goal }, CancellationToken.None);
        Assert.That(simulated.Requests, Has.Count.EqualTo(2));
        Assert.That(simulated.Requests.All(request => request.Turns.Single().Text.Contains(goal)), Is.True);
    }

    [TestCase("0060")]
    [TestCase("$0060")]
    [TestCase("0x0060")]
    [TestCase("60")]
    public void VariableAddressesMatchObservedHexRegardlessOfTheProvidersPrefix(string address)
    {
        var names = AiPlayAdvisor.ParseNames(System.Text.Json.JsonSerializer.Serialize(new
        {
            variables = new[] { new { address, name = "candidateY", description = "Hipótesis", keep = true } },
        }));
        Assert.That(names.Single().Address, Is.EqualTo("0060"));
    }

    [Test]
    public void NoEvidenceCanReturnNoExperimentsWithoutClaimingAnyFinding()
    {
        Assert.That(AiPlayAdvisor.ParseExperiments("""{"experiments":[]}""", Question().Keys), Is.Empty);
    }

    [TestCase("not-json")]
    [TestCase("""{"experiments":null}""")]
    [TestCase("""{"experiments":[],"code":"run()"}""")]
    [TestCase("""{"experiments":[],"experiments":[]}""")]
    [TestCase("""{"experiments":[{"kind":"change","description":"","frames":60,"repeatEvery":0,"steps":[{"frame":0,"keys":"P1 A","hold":1,"address":"0060"}]}]}""")]
    public void InvalidOrExtendedPlansAreRejected(string answer) =>
        Assert.That(() => AiPlayAdvisor.ParseExperiments(answer, Question().Keys), Throws.InstanceOf<AiException>());

    [TestCase("frames", -1)]
    [TestCase("frames", 901)]
    [TestCase("repeatEvery", -1)]
    [TestCase("repeatEvery", 121)]
    public void DurationsOutsideTheExecutionBudgetAreRejected(string property, int value)
    {
        var answer = JsonNode.Parse(Plan)!;
        answer["experiments"]![0]![property] = value;
        Assert.That(() => AiPlayAdvisor.ParseExperiments(answer.ToJsonString(), Question().Keys), Throws.InstanceOf<AiException>());
    }

    [TestCase("frame", -1)]
    [TestCase("frame", 120)]
    [TestCase("hold", 0)]
    [TestCase("hold", 181)]
    [TestCase("hold", 119)]
    public void InputDurationsCannotCrossTheExperimentBoundary(string property, int value)
    {
        var answer = JsonNode.Parse(Plan)!;
        answer["experiments"]![0]!["steps"]![0]![property] = value;
        Assert.That(() => AiPlayAdvisor.ParseExperiments(answer.ToJsonString(), Question().Keys), Throws.InstanceOf<AiException>());
    }

    [TestCase("RUN")]
    [TestCase("P1 A+")]
    [TestCase("P1 A+P2 RIGHT")]
    [TestCase("")]
    public void OnlyKeysOfTheObservedMachineAreAccepted(string keys)
    {
        var answer = JsonNode.Parse(Plan)!;
        answer["experiments"]![0]!["steps"]![0]!["keys"] = keys;
        Assert.That(() => AiPlayAdvisor.ParseExperiments(answer.ToJsonString(), Question().Keys), Throws.InstanceOf<AiException>());
    }

    [Test]
    public void ExcessExperimentsAndInputStepsAreRejectedInsteadOfSilentlyExtendingWork()
    {
        var plan = JsonNode.Parse(Plan)!;
        var item = plan["experiments"]![0]!.DeepClone();
        plan["experiments"] = new JsonArray(item.DeepClone(), item.DeepClone(), item.DeepClone(), item.DeepClone());
        Assert.That(() => AiPlayAdvisor.ParseExperiments(plan.ToJsonString(), Question().Keys), Throws.InstanceOf<AiException>());
        plan["experiments"] = new JsonArray(item);
        var step = item["steps"]![0]!.DeepClone();
        item["steps"] = new JsonArray(Enumerable.Range(0, 25).Select(_ => step.DeepClone()).ToArray());
        Assert.That(() => AiPlayAdvisor.ParseExperiments(plan.ToJsonString(), Question().Keys), Throws.InstanceOf<AiException>());
    }

    [Test]
    public async Task PreparationPhasesShareTheCallLimitInsteadOfStartingAnotherBudget()
    {
        var simulated = new Simulated((_, _) => Task.FromResult("""{"playing":false,"reason":"Título","actions":[]}"""));
        var operation = new AiOperation(simulated, 1);
        var advisor = new AiPlayAdvisor(operation);
        await advisor.AdviseAsync(new PlayObservation(), CancellationToken.None);
        Assert.ThrowsAsync<AiException>(() => advisor.PlanExperimentsAsync(Question(), CancellationToken.None));
        Assert.That(simulated.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void CancelledRequestDoesNotCallTheProvider()
    {
        var simulated = new Simulated((_, _) => Task.FromResult(Plan));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var advisor = new AiPlayAdvisor(simulated);
        Assert.ThrowsAsync<OperationCanceledException>(() => advisor.PlanExperimentsAsync(Question(new(2, 2)), cancellation.Token));
        Assert.That(simulated.Requests, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailureOrCancellationRemovesTheTemporaryPicture(bool cancel)
    {
        string? image = null;
        using var cancellation = new CancellationTokenSource();
        var simulated = new Simulated((request, _) =>
        {
            image = request.Images!.Single();
            Assert.That(File.Exists(image), Is.True);
            if (!cancel) throw new AiException("Fallo simulado");
            cancellation.Cancel();
            return Task.FromResult(Plan);
        });
        var advisor = new AiPlayAdvisor(simulated);
        if (cancel)
            Assert.ThrowsAsync<OperationCanceledException>(() => advisor.PlanExperimentsAsync(Question(new(2, 2)), cancellation.Token));
        else
            Assert.ThrowsAsync<AiException>(() => advisor.PlanExperimentsAsync(Question(new(2, 2)), cancellation.Token));
        Assert.That(image, Is.Not.Null);
        Assert.That(File.Exists(image), Is.False);
    }
    [Test]
    public async Task FolderAgentGetsTheGoalAndCanCorrectAnInvalidInputPlan()
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string directory = Path.Combine(temp, "retro-preparation-advisor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var advisor = new FolderPlayAdvisor(directory, TimeSpan.FromSeconds(10));
            var question = Question();
            var pending = advisor.PlanExperimentsAsync(question, CancellationToken.None);
            string request = await File.ReadAllTextAsync(Path.Combine(directory, "experimentos.md"));
            Assert.That(request, Does.Contain(question.Goal).And.Contain("0060").And.Contain("experimentos.json"));
            Assert.That(request, Does.Contain(Environment.NewLine).Or.Contain("\\n"));
            await File.WriteAllTextAsync(Path.Combine(directory, "experimentos.json"), """{"experiments":[],"code":"not allowed"}""");
            string errorPath = Path.Combine(directory, "experimentos-error.txt");
            for (int i = 0; i < 50 && !File.Exists(errorPath); i++) await Task.Delay(50);
            Assert.That(File.Exists(errorPath), Is.True, "The agent needs feedback to correct its plan.");
            await File.WriteAllTextAsync(Path.Combine(directory, "experimentos.json"), Plan);
            var experiments = await pending;
            Assert.That(experiments, Has.Count.EqualTo(1));
            Assert.That(File.Exists(errorPath), Is.False);
        }
        finally
        {
            if (!Path.GetFullPath(directory).StartsWith(temp, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cleanup outside temporary directory.");
            Directory.Delete(directory, true);
        }
    }
}

