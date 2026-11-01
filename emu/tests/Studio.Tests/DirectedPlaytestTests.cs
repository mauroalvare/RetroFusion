using System.Text.Json;
using Retro.Core;
using Retro.Ai;
using Retro.Fusion;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture]
public sealed class DirectedPlaytestTests
{
    private string _folder = "";
    private StudioGame _game = null!;
    private Dictionary<string, GameSource> _sources = null!;

    [SetUp]
    public void Prepare()
    {
        _folder = Path.Combine(Path.GetTempPath(), "retro-directed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder); File.WriteAllBytes(Path.Combine(_folder, "48.rom"), new byte[16384]);
        _sources = new();
        foreach (string id in new[] { "a", "b" })
        {
            var data = new byte[30 + 49152]; data[7] = 0x80; data[8] = 0xFE; data[9] = 0xFF;
            Array.Fill(data, (byte)0xAA, 30, 6144); Array.Fill(data, (byte)0x42, 30 + 6144, 768);
            var p = new GameProfile { Id = "own-directed-" + id + Guid.NewGuid().ToString("N"), Machine = "zx48", Boot = ["wait 1"],
                Variables = new() { ["counter"] = new() { Address = "C000", PurposeStatus = "hypothesis" },
                    ["untouched"] = new() { Address = "C001" } } };
            _sources[p.Id] = new(p, [new MediaFile(id + ".z80", data, "own-test")], null);
        }
        _game = new() { BaseSlot = "b", Games = _sources.Select((s, i) => new RecipeGame { Slot = i == 0 ? "a" : "b", Profile = s.Key }).ToList(),
            Script = "function init(){games.a.pause();games.b.pause();game.state('playing');games.a.pixel(0,0);}" +
                "function tick(i){game.score(999);if(i.fire)games.a.set('counter',games.a.get('counter')+1);}function draw(){}" };
    }

    [TearDown] public void Clean() => Directory.Delete(_folder, true);

    private static StudioPlaytest Plan(string variable = "counter", long min = 5, long max = 5) => new()
    {
        Name = "Mantener fuego", Steps = [new() { Ticks = 5, Inputs = ["fire"] }],
        Checks = [new() { Slot = "a", Variable = variable, FromTick = 0, AtTick = 5, Min = min, Max = max }],
    };
    private StudioSession Start(params StudioPlaytest[] plans)
    {
        _game.Playtests = plans.ToList();
        return StudioSession.Start(_game, _sources, _folder);
    }

    private sealed class Answers(params string[] answers) : IAiProvider
    {
        public string Name => "Respuestas locales de prueba";
        public int Calls;
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct) => Task.FromResult(answers[Calls++]);
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ARevisionCanFixChecksButCannotRetryTheSameFailureByRenamingIt(bool renameOnly)
    {
        string Answer(StudioPlaytest plan) => JsonSerializer.Serialize(new { title = "Propio", script = _game.Script, playtests = new[] { plan } }, GameProfile.Json);
        var second = Plan(renameOnly ? "untouched" : "counter"); second.Name = "Otra etiqueta";
        var ai = new Answers(Answer(Plan("untouched")), Answer(second));
        ValidationReport Validate(StudioGame game)
        {
            var checks = StudioPlaytests.Run(StudioSession.Start(game, _sources, _folder));
            bool ok = checks.All(c => c.Ok);
            return new(ok, null, ok ? [] : ["La variable no cambió"], 5, ok ? "Cambios observados" : "La variable no cambió", null, Playtests: checks);
        }
        var outcome = await new StudioDesigner().ReviseAsync(ai, _game, id => _sources.GetValueOrDefault(id)?.Profile,
            "Comprobar la regla", Validate, CancellationToken.None);
        Assert.That(ai.Calls, Is.EqualTo(2));
        if (renameOnly)
        {
            Assert.That(outcome.Game, Is.Null);
            Assert.That(outcome.Error, Does.Contain("mismo guion y comprobaciones"));
        }
        else
        {
            Assert.That(outcome.Game, Is.Not.Null, outcome.Error);
            Assert.That(outcome.Game!.Script, Is.EqualTo(_game.Script));
            Assert.That(outcome.Report!.Playtests!.Single().Ok, Is.True);
        }
    }

    [Test]
    public async Task CorrectingOnlyTheMeasurementDoesNotRepeatThePreviousFailure()
    {
        _game.Script += "var observedTick=0;function tick(){observedTick++;if(observedTick==2)games.a.set('counter',7);if(observedTick==4)games.a.set('counter',0);}";
        var endpoint = Plan(min: 7, max: 7); var peak = Plan(min: 7, max: 7); peak.Checks[0].Measure = "maxDelta";
        string Answer(StudioPlaytest plan) => JsonSerializer.Serialize(new { script = _game.Script, playtests = new[] { plan } }, GameProfile.Json);
        var ai = new Answers(Answer(endpoint), Answer(peak));
        var outcome = await new StudioDesigner().ReviseAsync(ai, _game, id => _sources.GetValueOrDefault(id)?.Profile,
            "Comprobar un efecto breve", game =>
            {
                var results = StudioPlaytests.Run(StudioSession.Start(game, _sources, _folder));
                bool ok = results.All(r => r.Ok);
                return new(ok, null, ok ? [] : ["Efecto no comprobado"], 5, "Prueba local", null, Playtests: results);
            }, CancellationToken.None);
        Assert.That(ai.Calls, Is.EqualTo(2));
        Assert.That(outcome.Game, Is.Not.Null, outcome.Error);
        Assert.That(outcome.Game!.Script, Is.EqualTo(_game.Script));
        Assert.That(outcome.Report!.Playtests!.Single().Observations.Single().Measure, Is.EqualTo("maxDelta"));
    }

    [Test]
    public async Task EndpointOnlyPlansStillRecognizeTheFingerprintWrittenByVersion036()
    {
        _game.Playtests = [Plan("untouched")];
        var ai = new Answers(JsonSerializer.Serialize(new { script = _game.Script, playtests = _game.Playtests }, GameProfile.Json));
        int validations = 0;
        // 0.3.6 used the platform's JSON line endings. Both values come from its endpoint-only fixture.
        string legacyFingerprint = Environment.NewLine == "\r\n"
            ? "playtest-v1:2D65A0F370A154D2E729DBAEBE0CB623FDC21666EA4FB0D260F433C0A30C98D9"
            : "playtest-v1:6620C30E6C207B8D2E3B9C74ADEB8B9DF3A91983D24F5377A663E8260372A09C";
        string? lastFingerprint = null;
        var outcome = await new StudioDesigner { AttemptLimit = 1, Checkpoint = c => lastFingerprint = c.TriedScripts.LastOrDefault() }.ResumeAsync(ai, new()
        {
            Template = _game.Copy(), Draft = _game.Copy(),
            TriedScripts = [legacyFingerprint],
        }, _ => { validations++; return new(false, null, ["La variable no cambió"], 5, "La variable no cambió", null); }, CancellationToken.None);
        Assert.That(outcome.Game, Is.Null);
        Assert.That(outcome.Error, Does.Contain("mismo guion y comprobaciones"), lastFingerprint);
        Assert.That(ai.Calls, Is.EqualTo(1));
        Assert.That(validations, Is.EqualTo(1), "Only the recovered draft is revalidated; the duplicate answer must not run");
    }

    [Test]
    public void TheHostReadsOriginalMemoryAndStartsEachPlanAtTheSameBoot()
    {
        var session = Start(Plan(), Plan());
        var results = StudioPlaytests.Run(session);
        Assert.That(results, Has.All.Property(nameof(PlaytestResult.Ok)).True);
        Assert.That(results.SelectMany(r => r.Observations).Select(o => (o.Before, o.After)), Is.EqualTo(new[] { (0, 5), (0, 5) }));
        Assert.That(results[0].Observations[0].Purpose, Does.Contain("propuesto"));
        session.Update(1.0 / 60);
        Assert.That(session.Slots[0].Machine.Memory.Peek(0xC000), Is.EqualTo(5), "Fire must be released after the replay");
    }

    [Test]
    public void ClaimedScoreAndLogsCannotSatisfyAnUnchangedOriginalVariable()
    {
        var session = Start(Plan("untouched"));
        var result = StudioPlaytests.Run(session).Single();
        Assert.That(session.Score, Is.GreaterThan(0));
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Observations.Single().Delta, Is.Zero);
    }

    [Test]
    public void ProbesCanMeasureAnIntervalAfterTheInitialActions()
    {
        var plan = Plan(min: 2, max: 2); plan.Checks[0].FromTick = 3;
        var result = StudioPlaytests.Run(Start(plan)).Single();
        Assert.That(result.Ok, Is.True);
        Assert.That((result.Observations[0].Before, result.Observations[0].After), Is.EqualTo((3, 5)));
    }

    [TestCase("maxDelta", 0, 7, 2)] [TestCase("minDelta", 3, -7, 4)]
    [TestCase("changes", 0, 2, 6)] [TestCase("changes", 3, 1, 6)]
    public void TransientChangesAreMeasuredEvenWhenTheFinalValueReturnsToItsStart(string measure, int from, long expected, int measuredTick)
    {
        _game.Script += "var observedTick=0;function tick(){observedTick++;if(observedTick==2)games.a.set('counter',7);if(observedTick==4)games.a.set('counter',0);}";
        var plan = Plan(min: expected, max: expected); plan.Steps[0].Ticks = 6;
        plan.Checks[0].FromTick = from; plan.Checks[0].AtTick = 6; plan.Checks[0].Measure = measure;
        var result = StudioPlaytests.Run(Start(plan)).Single();
        Assert.That(result.Ok, Is.True, result.Error);
        var observation = result.Observations.Single();
        Assert.That(observation.Measured, Is.EqualTo(expected));
        Assert.That(observation.MeasuredAtTick, Is.EqualTo(measuredTick));
        Assert.That(observation.Measure, Is.EqualTo(measure));
        Assert.That(observation.After, Is.Zero);
    }

    [TestCase("minDelta")] [TestCase("maxDelta")]
    public void PeakIntervalsIncludeTheirStartingValue(string measure)
    {
        var plan = Plan("untouched", 0, 0); plan.Checks[0].Measure = measure;
        var result = StudioPlaytests.Run(Start(plan)).Single();
        Assert.That(result.Ok, Is.True);
        Assert.That(result.Observations.Single().Measured, Is.Zero);
        Assert.That(result.Observations.Single().MeasuredAtTick, Is.Zero);
    }

    [TestCase("unknown", 0, 5)] [TestCase("changes", -1, 5)] [TestCase("changes", 0, 6)]
    public void UnknownMeasuresAndImpossibleChangeCountsAreRejected(string measure, long min, long max)
    {
        var plan = Plan(min: min, max: max); plan.Checks[0].Measure = measure;
        Assert.That(StudioPlaytests.CheckShape([plan]), Is.Not.Null);
    }

    [Test]
    public void AnUnreadableProfileVariableReportsTheFailedProbeAndReleasesInputs()
    {
        var session = Start(Plan()); session.Slots[0].Profile.Variables["counter"].Address = "invalid-hex";
        session.SetInput("fire", true);
        var result = StudioPlaytests.Run(session).Single();
        Assert.That(result.Ok, Is.False); Assert.That(result.Ticks, Is.Zero);
        Assert.That(result.Error, Does.Contain("games.a.counter"));
        session.Slots[0].Profile.Variables["counter"].Address = "C000";
        session.Update(1.0 / 60);
        Assert.That(session.Slots[0].Machine.Memory.Peek(0xC000), Is.Zero);
    }

    [Test]
    public void CorrectionReportsDescribeThePeakRatherThanTheUnchangedEndpoint()
    {
        _game.Script += "var observedTick=0;function tick(){observedTick++;if(observedTick==2)games.a.set('counter',7);if(observedTick==4)games.a.set('counter',0);}";
        var plan = Plan(min: 8, max: 8); plan.Checks[0].Measure = "maxDelta";
        var report = StudioValidator.Run(Start(plan), 1);
        Assert.That(report.Ok, Is.False);
        Assert.That(StudioDesigner.FixRequest(report), Does.Contain("cambio máximo 7, observado en tick 2").And.Contain("[8, 8]"));
    }

    [Test]
    public void PreviousEndpointPlansAndNewMeasurementSchemaRemainReadable()
    {
        var plan = JsonSerializer.Deserialize<StudioPlaytest>("""
            {"name":"Anterior", "steps":[{"ticks":5,"inputs":["fire"]}],
             "checks":[{"slot":"a","variable":"counter","fromTick":0,"atTick":5,"min":5,"max":5}]}
            """, GameProfile.Json)!;
        Assert.That(StudioPlaytests.Run(Start(plan)).Single().Ok, Is.True);
        Assert.That(plan.Checks.Single().Measure, Is.EqualTo("delta"));
        var checks = StudioDesigner.GameSchema().GetProperty("properties").GetProperty("playtests").GetProperty("items")
            .GetProperty("properties").GetProperty("checks").GetProperty("items");
        Assert.That(checks.GetProperty("properties").GetProperty("measure").GetProperty("enum").EnumerateArray().Select(e => e.GetString()),
            Is.EquivalentTo(new[] { "delta", "minDelta", "maxDelta", "changes" }));
    }

    [TestCase("missing", "counter")] [TestCase("a", "C000")]
    public void UnknownSlotsAndRawAddressesAreRejectedBeforePlaying(string slot, string variable)
    {
        var plan = Plan(variable); plan.Checks[0].Slot = slot;
        var result = StudioPlaytests.Run(Start(plan)).Single();
        Assert.That(result.Ok, Is.False); Assert.That(result.Ticks, Is.Zero);
        Assert.That(result.Error, Does.Contain("No existe"));
    }

    [Test]
    public void RuntimeErrorsDoNotLeaveInputsHeldOrApproveEarlierObservations()
    {
        _game.Script += "var failureTick=0;function tick(i){game.score(i.fire?1:0);if(++failureTick==3)throw new Error('rotura');}";
        var session = Start(Plan());
        var result = StudioPlaytests.Run(session).Single();
        Assert.That(result.Ok, Is.False); Assert.That(result.Error, Does.Contain("rotura"));
        session.Restart(); session.Update(1.0 / 60);
        Assert.That(session.Score, Is.Zero);
    }

    [TestCase(false)] [TestCase(true)]
    public void CancellationOrAnInvalidPlanReleasesInputsEvenBeforeTheFirstPlan(bool invalidPlan)
    {
        var session = Start(Plan()); session.SetInput("fire", true);
        if (invalidPlan)
        {
            session.Game.Playtests[0].Steps[0].Ticks = -1;
            Assert.That(StudioPlaytests.Run(session).Single().Ok, Is.False);
        }
        else
        {
            using var stop = new CancellationTokenSource(); stop.Cancel();
            Assert.Throws<OperationCanceledException>(() => StudioPlaytests.Run(session, stop.Token));
        }
        session.Update(1.0 / 60);
        Assert.That(session.Slots[0].Machine.Memory.Peek(0xC000), Is.Zero);
    }

    [Test]
    public void AFailedDirectedCheckFailsTheIntegratedValidatorAndItsCorrectionRequest()
    {
        var report = StudioValidator.Run(Start(Plan("untouched")), 1);
        Assert.That(report.Ok, Is.False);
        Assert.That(report.Playtests, Has.Count.EqualTo(1));
        Assert.That(StudioDesigner.FixRequest(report), Does.Contain("cambio 0").And.Contain("[5, 5]").And.Contain("games.a.untouched"));
    }

    [Test]
    public void PlansRoundTripButAreNotSilentlyInheritedByRevisions()
    {
        _game.Playtests = [Plan()];
        Assert.That(_game.Copy().Playtests.Single().Checks.Single().Max, Is.EqualTo(5));
        var parsed = StudioDesigner.ParseGame(JsonSerializer.Serialize(new { script = _game.Script, playtests = _game.Playtests }, GameProfile.Json), _game, out var error);
        Assert.That(error, Is.Null); Assert.That(parsed!.Playtests, Has.Count.EqualTo(1));
        var oldAnswer = StudioDesigner.ParseGame(JsonSerializer.Serialize(new { script = _game.Script }), _game, out _);
        Assert.That(oldAnswer!.Playtests, Is.Empty);
        Assert.That(StudioDesigner.ParseGame("{\"playtests\":{}}\n```js\nfunction draw(){}\n```", _game, out _), Is.Null,
            "A code fence cannot bypass malformed directed observations");
    }

    [Test]
    public void WorkspaceImportsDirectedChecksAndNoticesMetadataOnlyEdits()
    {
        StudioWorkspace.Create(_game, "Probar la regla", _ => null, null, _folder);
        var first = StudioWorkspace.Read(_folder, _game)!.Value;
        var edited = _game.Copy(); edited.Playtests = [Plan()];
        string path = Path.Combine(_folder, StudioWorkspace.GameFile);
        File.WriteAllText(path, edited.ToJson()); File.SetLastWriteTimeUtc(path, first.written.AddSeconds(2));
        var read = StudioWorkspace.Read(_folder, _game)!.Value;
        Assert.That(read.game.Playtests.Single().Checks.Single().Variable, Is.EqualTo("counter"));
        Assert.That(read.written, Is.GreaterThan(first.written));
        File.WriteAllText(path, "{\"playtests\":[");
        Assert.That(StudioWorkspace.Read(_folder, _game), Is.Null, "Do not validate a stale plan while its replacement is incomplete");
    }

    [TestCase("input")] [TestCase("negative")] [TestCase("range")] [TestCase("duration")] [TestCase("missing-checks")] [TestCase("null-step")]
    public void InvalidPlansAreRejectedBeforeAnyMachineRuns(string fault)
    {
        var plan = Plan();
        switch (fault)
        {
            case "input": plan.Steps[0].Inputs = ["P1 A"]; break;
            case "negative": plan.Checks[0].FromTick = -1; break;
            case "range": plan.Checks[0].Min = 6; break;
            case "duration": plan.Steps = Enumerable.Repeat(new PlaytestStep { Ticks = 600 }, 10).ToList(); break;
            case "missing-checks": plan.Checks = []; break;
            case "null-step": plan.Steps = [null!]; break;
        }
        string answer = JsonSerializer.Serialize(new { script = "// valid", playtests = new[] { plan } }, GameProfile.Json);
        Assert.That(StudioDesigner.ParseGame(answer, _game, out var error), Is.Null);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }
}
