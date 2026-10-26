using Retro.Ai;
using Retro.Fusion;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture]
public sealed class ConceptReassessmentTests
{
    private sealed class FakeAi(Func<AiRequest, CancellationToken, Task<string>> reply) : IAiProvider
    {
        public string Name => "IA simulada";
        public List<AiRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return reply(request, ct);
        }
    }

    private static FakeAi Answer(string answer) => new((_, _) => Task.FromResult(answer));
    private static readonly List<RecipeGame> Slots = [new() { Slot = "a", Profile = "arcade" }, new() { Slot = "b", Profile = "platform" }];
    private static GameConcept Blocked(bool inspired = false) => new("Unión elegida", "Un aporte dentro del recorrido", "Premisa original",
        "Controles originales", "Conservar la mecánica y el mundo", new("b", inspired), "Mantener el recorrido y sumar una acción", "Posición comprobada del protagonista");

    private static Dictionary<string, GameProfile> Profiles() => new()
    {
        ["arcade"] = new GameProfile { Id = "arcade", Name = "Arcade propio", Machine = "invaders", Verified = "Arranque repetido en una máquina nueva.",
            Boot = ["press P1 START 6"], Controls = { ["fire"] = "P1 FIRE" } },
        ["platform"] = new GameProfile { Id = "platform", Name = "Plataformas propio", Machine = "nes", Verified = "Experimento propio: los cambios alteran la posición.",
            Boot = ["press P1 START 6"], Controls = { ["right"] = "P1 RIGHT" },
            Variables = { ["heroX"] = new() { Address = "0020", Description = "Posición comprobada recién preparada." } } },
    };

    private const string Resolved = """
        {"missingData":"","evidence":[{"slot":"b","kind":"variable","name":"heroX","reason":"La posición requerida está comprobada en el perfil actual."}]}
        """;

    [Test]
    public async Task ResolvesOnlyAgainstCurrentProfilesAndPreservesTheChosenIdea()
    {
        var profiles = Profiles();
        var original = Blocked();
        profiles["platform"].Description = "NUEVA_EVIDENCIA_DEL_LABORATORIO";
        var ai = Answer(Resolved);
        var result = await new StudioDesigner().ReassessConceptAsync(ai, Slots, id => profiles.GetValueOrDefault(id), original, CancellationToken.None);

        Assert.That(result.MissingData, Is.Empty);
        Assert.That((result.Title, result.Approach, result.Pitch, result.HowToPlay, result.PlayerRequest, result.Direction),
            Is.EqualTo((original.Title, original.Approach, original.Pitch, original.HowToPlay, original.PlayerRequest, original.Direction)));
        Assert.That(result.Design, Does.StartWith(original.Design).And.Contain("heroX"));
        Assert.That(original.MissingData, Is.Not.Empty);
        string sent = ai.Requests.Single().Turns.Single().Text;
        Assert.That(sent, Does.Contain("NUEVA_EVIDENCIA_DEL_LABORATORIO").And.Contain("Posición comprobada recién preparada."));
        Assert.That(sent, Does.Contain("BASE ELEGIDA: games.b").And.Contain(original.PlayerRequest).And.Contain(original.MissingData));
        Assert.That(sent, Does.Contain("No inventes direcciones").IgnoreCase);
    }

    [Test]
    public async Task APreparedProfileWithoutTheNeededDataStaysBlockedAndCannotStartWriting()
    {
        var profiles = Profiles();
        var ai = Answer("""{"missingData":"Todavía falta comprobar los límites de colisión.","evidence":[]}""");
        var result = await new StudioDesigner().ReassessConceptAsync(ai, Slots, id => profiles.GetValueOrDefault(id), Blocked(), CancellationToken.None);
        Assert.That(result.MissingData, Does.Contain("límites de colisión"));
        var outcome = await new StudioDesigner().WriteAsync(ai, Slots, id => profiles.GetValueOrDefault(id), result,
            _ => throw new AssertionException("No debe validar ni ejecutar una idea bloqueada."), CancellationToken.None);
        Assert.That(outcome.Game, Is.Null);
        Assert.That(outcome.Attempts, Is.Zero);
        Assert.That(ai.Requests, Has.Count.EqualTo(1), "Revisar no debe escribir, reintentar ni reiniciar el presupuesto.");
    }

    [TestCase("""{"missingData":"","evidence":[]}""")]
    [TestCase("""{"missingData":"","evidence":[{"slot":"b","kind":"variable","name":"inventada","reason":"Ya está."}]}""")]
    [TestCase("""{"missingData":"","evidence":[{"slot":"c","kind":"variable","name":"heroX","reason":"Otra ranura."}]}""")]
    [TestCase("""{"missingData":"","evidence":[{"slot":"b","kind":"variable","name":"heroX","reason":""}]}""")]
    [TestCase("""{"missingData":"","evidence":[{"slot":"b","kind":"helper","name":"mapaInventado()","reason":"Un mapa."}]}""")]
    public async Task EmptyMissingDataDoesNotBypassMissingOrInventedEvidence(string answer)
    {
        var profiles = Profiles();
        var original = Blocked();
        var result = await new StudioDesigner().ReassessConceptAsync(Answer(answer), Slots, id => profiles.GetValueOrDefault(id), original, CancellationToken.None);
        Assert.That(result.MissingData, Does.StartWith(original.MissingData).And.Contain("evidencia"));
        Assert.That(result.Design, Is.EqualTo(original.Design));
    }

    [Test]
    public async Task ControlsFromAnUnpreparedBasicProfileAreNotProofOfAPlayableBoot()
    {
        var profiles = Profiles();
        profiles["platform"].Verified = "";
        profiles["platform"].Boot.Clear();
        var ai = Answer("""{"missingData":"","evidence":[{"slot":"b","kind":"control","name":"right","reason":"Moverse."}]}""");
        var result = await new StudioDesigner().ReassessConceptAsync(ai, Slots, id => profiles.GetValueOrDefault(id), Blocked(), CancellationToken.None);
        Assert.That(result.MissingData, Is.Not.Empty);
    }

    [Test]
    public async Task AnAnswerCannotChangeTheBaseModalityOrIdentity()
    {
        var profiles = Profiles();
        var original = Blocked(inspired: true);
        var ai = Answer(Resolved.Replace("\"missingData\":", "\"title\":\"Otra idea\",\"pitch\":\"Otro juego\",\"baseSlot\":\"a\",\"playerRequest\":\"Otro pedido\",\"missingData\":"));
        var result = await new StudioDesigner().ReassessConceptAsync(ai, Slots, id => profiles.GetValueOrDefault(id), original, CancellationToken.None);
        Assert.That(result.MissingData, Is.Empty);
        Assert.That(result.Direction, Is.EqualTo(original.Direction));
        Assert.That(result.Direction!.Reimagine, Is.True);
        Assert.That(result.Direction.Resolve(Slots), Is.Null);
        Assert.That((result.Title, result.Pitch, result.PlayerRequest), Is.EqualTo((original.Title, original.Pitch, original.PlayerRequest)));
    }

    [Test]
    public async Task ProfilesChangedDuringTheRequestRequireAnotherReview()
    {
        var profiles = Profiles();
        var ai = new FakeAi((_, _) =>
        {
            profiles["platform"].Variables.Clear();
            return Task.FromResult(Resolved);
        });
        var result = await new StudioDesigner().ReassessConceptAsync(ai, Slots, id => profiles.GetValueOrDefault(id), Blocked(), CancellationToken.None);
        Assert.That(result.MissingData, Does.Contain("cambiaron durante"));
    }

    [Test]
    public void MissingCurrentProfileFailsBeforeCallingTheProvider()
    {
        var profiles = Profiles();
        profiles.Remove("platform");
        var ai = Answer(Resolved);
        Assert.ThrowsAsync<AiException>(() => new StudioDesigner().ReassessConceptAsync(ai, Slots, id => profiles.GetValueOrDefault(id), Blocked(), CancellationToken.None));
        Assert.That(ai.Requests, Is.Empty);
    }

    [Test]
    public void APreCancelledReviewDoesNotLookUpProfilesOrCallTheProvider()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ai = Answer(Resolved);
        Assert.ThrowsAsync<OperationCanceledException>(() => new StudioDesigner().ReassessConceptAsync(ai, Slots,
            _ => throw new AssertionException("No debe consultar perfiles después de cancelar."), Blocked(), cts.Token));
        Assert.That(ai.Requests, Is.Empty);
    }

    [Test]
    public void CancellationAfterTheProviderAnswerDoesNotUnlockTheConcept()
    {
        var profiles = Profiles();
        using var cts = new CancellationTokenSource();
        var ai = new FakeAi((_, _) => { cts.Cancel(); return Task.FromResult(Resolved); });
        Assert.ThrowsAsync<OperationCanceledException>(() => new StudioDesigner().ReassessConceptAsync(ai, Slots, id => profiles.GetValueOrDefault(id), Blocked(), cts.Token));
        Assert.That(ai.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void ProviderFailureAndInvalidAnswersAreNotRetriedOrTurnedIntoReadyIdeas()
    {
        var profiles = Profiles();
        var ai = new FakeAi((_, _) => Task.FromException<string>(new AiException("Fallo simulado")));
        Assert.ThrowsAsync<AiException>(() => new StudioDesigner().ReassessConceptAsync(ai, Slots, id => profiles.GetValueOrDefault(id), Blocked(), CancellationToken.None));
        Assert.That(ai.Requests, Has.Count.EqualTo(1));
        ai = Answer("{}");
        Assert.ThrowsAsync<AiException>(() => new StudioDesigner().ReassessConceptAsync(ai, Slots, id => profiles.GetValueOrDefault(id), Blocked(), CancellationToken.None));
        Assert.That(ai.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task UsesTheCallersExistingBudgetWithoutCreatingAnotherAllowance()
    {
        var profiles = Profiles();
        var ai = Answer(Resolved);
        var operation = new AiOperation(ai, 1);
        await operation.CompleteAsync(new AiRequest("Prueba", [new AiTurn(true, "Primer pedido")], null), CancellationToken.None);
        Assert.ThrowsAsync<AiException>(() => new StudioDesigner().ReassessConceptAsync(operation, Slots, id => profiles.GetValueOrDefault(id), Blocked(), CancellationToken.None));
        Assert.That(ai.Requests, Has.Count.EqualTo(1));
        Assert.That(operation.Stats.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task AnAlreadyReadyConceptDoesNotSpendAnotherCall()
    {
        var profiles = Profiles();
        var original = Blocked() with { MissingData = "" };
        var ai = Answer(Resolved);
        var result = await new StudioDesigner().ReassessConceptAsync(ai, Slots, id => profiles.GetValueOrDefault(id), original, CancellationToken.None);
        Assert.That(result, Is.SameAs(original));
        Assert.That(ai.Requests, Is.Empty);
    }

    [Test]
    public void ManualReviewUsesTheSameEvidenceAndIdentityChecks()
    {
        var profiles = Profiles();
        var concept = Blocked();
        string prompt = StudioDesigner.ManualReassessPrompt(Slots, id => profiles.GetValueOrDefault(id), concept);
        Assert.That(prompt, Does.Contain("heroX").And.Contain(concept.MissingData).And.Contain(concept.PlayerRequest));
        var result = StudioDesigner.ParseReassessment(Resolved, Slots, id => profiles.GetValueOrDefault(id), concept);
        Assert.That(result.MissingData, Is.Empty);
        Assert.That((result.Title, result.PlayerRequest, result.Direction), Is.EqualTo((concept.Title, concept.PlayerRequest, concept.Direction)));
        result = StudioDesigner.ParseReassessment("""{"missingData":"","evidence":[]}""", Slots, id => profiles.GetValueOrDefault(id), concept);
        Assert.That(result.MissingData, Is.Not.Empty);
    }

    [Test]
    public void ManualAnswersCannotCiteResourcesRemovedSinceThePrompt()
    {
        var profiles = Profiles();
        var concept = Blocked();
        _ = StudioDesigner.ManualReassessPrompt(Slots, id => profiles.GetValueOrDefault(id), concept);
        profiles["platform"].Variables.Clear();
        var result = StudioDesigner.ParseReassessment(Resolved, Slots, id => profiles.GetValueOrDefault(id), concept);
        Assert.That(result.MissingData, Is.Not.Empty);
    }

    [TestCase("""{"missingData":"","evidence":[}""")]
    [TestCase("""{"missingData":{"incorrecto":true},"evidence":[]}""")]
    [TestCase("""{"missingData":"","evidence":[{"slot":42,"kind":"variable","name":"heroX","reason":"Posición"}]}""")]
    [TestCase("""{"missingData":"","evidence":[{"slot":"b","kind":"variable","name":false,"reason":"Posición"}]}""")]
    [TestCase("""{"missingData":"","evidence":[{"slot":"b","kind":{},"name":"heroX","reason":"Posición"}]}""")]
    public void MalformedManualAnswersBecomeRecoverableAiErrors(string answer)
    {
        var profiles = Profiles();
        var original = Blocked();
        Assert.Throws<AiException>(() => StudioDesigner.ParseReassessment(answer, Slots, id => profiles.GetValueOrDefault(id), original));
        Assert.That(original.MissingData, Is.Not.Empty);
    }
}
