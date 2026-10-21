using Retro.Ai;
using Retro.Core;
using Retro.Fusion;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture]
[NonParallelizable]
public sealed class WorkRecoveryTests
{
    private sealed class Fake(Func<CancellationToken, Task<string>> response) : IAiProvider
    {
        public string Name => "IA simulada";
        public int Calls;
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct) { Calls++; return response(ct); }
    }
    private static readonly List<RecipeGame> Slots = [new() { Slot = "a", Profile = "invaders" }, new() { Slot = "b", Profile = "manicminer" }];
    private static ValidationReport Ok(StudioGame game) => new(true, null, [], 1, "Prueba local simulada", new VideoFrame(8, 8));
    private static ValidationReport Bad(StudioGame game) => new(false, "falta corregir", [], 1, "Conservar el objetivo; falta corregir", null);

    [Test]
    public async Task ACompleteDraftSurvivesInterruptedValidation_AndResumesWithoutCallingTheProvider()
    {
        DesignCheckpoint? saved = null;
        var ai = new Fake(_ => Task.FromResult("""{"title":"Nuevo","script":"function draw() {}"}"""));
        var designer = new StudioDesigner { Checkpoint = state => saved = state };
        Assert.ThrowsAsync<OperationCanceledException>(async () => await designer.WriteAsync(ai, Slots, ProfileLibrary.Find,
            new("Pedido", "", "Regla", "", Direction: new("b"), PlayerRequest: "Conservar el recorrido"),
            _ => throw new OperationCanceledException(), CancellationToken.None));
        Assert.That(saved!.Draft, Is.Not.Null);
        Assert.That(saved.FirstPrompt, Is.Empty, "no conserva el código anterior cuando ya tiene un borrador");
        Assert.That(saved.Calls, Is.EqualTo(1));
        saved.Draft!.BaseSlot = "a"; saved.Draft.PlayerRequest = "Intento de cambiar el pedido"; saved.Draft.Kind = "inspired";
        var noAi = new Fake(_ => throw new AssertionException("No debía llamar a la IA"));
        var outcome = await designer.ResumeAsync(noAi, saved, Ok, CancellationToken.None);
        Assert.That(outcome.Game!.BaseSlot, Is.EqualTo("b"));
        Assert.That(outcome.Game.PlayerRequest, Is.EqualTo("Conservar el recorrido"));
        Assert.That(outcome.Game.Kind, Is.EqualTo("fusion"));
        Assert.That(noAi.Calls, Is.Zero);
        Assert.That(outcome.Attempts, Is.Zero);
    }

    [Test]
    public async Task AnInterruptedRevisionKeepsTheRequestButNeverApprovesTheUnchangedOldVersion()
    {
        DesignCheckpoint? saved = null;
        var designer = new StudioDesigner { Checkpoint = state => saved = state };
        var old = new StudioGame { Id = "existente", Games = Slots, BaseSlot = "b", Script = "oldVersion();", Version = 5, PlayerRequest = "Conservar mapas" };
        var limited = new Fake(_ => throw new AiException("Límite simulado"));
        Assert.ThrowsAsync<AiException>(async () => await designer.ReviseAsync(limited, old, ProfileLibrary.Find,
            "Cambiar la regla de los disparos", _ => throw new AssertionException("No hubo respuesta nueva"), CancellationToken.None));
        Assert.That(saved!.Draft, Is.Null);
        Assert.That(saved.Template.Script, Is.Empty);
        Assert.That(saved.FirstPrompt, Does.Contain(old.Script));
        var next = new Fake(_ => Task.FromResult("""{"script":"newVersion();"}"""));
        var result = await designer.ResumeAsync(next, saved, game =>
        { Assert.That(game.Script, Is.EqualTo("newVersion();")); return Ok(game); }, CancellationToken.None);
        Assert.That(next.Calls, Is.EqualTo(1));
        Assert.That(result.Game!.Version, Is.EqualTo(6));
        Assert.That(result.Game.Request, Is.EqualTo("Cambiar la regla de los disparos"));
    }

    [Test]
    public async Task ARepeatedFailedScriptIsRememberedAcrossResumption()
    {
        DesignCheckpoint? saved = null;
        var designer = new StudioDesigner { Checkpoint = state => saved = state };
        var ai = new Fake(_ => Task.FromResult("""{"script":"sameFailure();"}"""));
        await designer.WriteAsync(ai, Slots, ProfileLibrary.Find, new("Pedido", "", "Regla", ""), Bad, CancellationToken.None);
        var repeated = new Fake(_ => Task.FromResult("""{"script":"sameFailure();"}"""));
        int validations = 0;
        var result = await designer.ResumeAsync(repeated, saved!, game => { validations++; return Bad(game); }, CancellationToken.None);
        Assert.That(repeated.Calls, Is.EqualTo(1));
        Assert.That(validations, Is.EqualTo(1), "solo se revalida el borrador guardado, no la misma respuesta repetida");
        Assert.That(result.Error, Does.Contain("mismo guion"));
    }

    [Test]
    public async Task AnEconomicRunKeepsItsDraftAndUsage_WhenItsTextBudgetStopsTheNextRequest()
    {
        DesignCheckpoint? saved = null;
        var fake = new Fake(_ => Task.FromResult("""{"script":"stillBroken();"}"""));
        var concept = new GameConcept("Pedido", "", "Regla", "");
        long limit = AiEstimate.For(new(StudioDesigner.Guide, [new(true, StudioDesigner.WritePrompt(Slots, ProfileLibrary.Find, concept))], StudioDesigner.GameSchema())).Characters;
        var operation = new AiOperation(fake, 2, limit);
        var designer = new StudioDesigner { AttemptLimit = 2, Checkpoint = state => saved = state };
        Assert.ThrowsAsync<AiException>(() => designer.WriteAsync(operation, Slots, ProfileLibrary.Find,
            concept, Bad, CancellationToken.None));
        Assert.That(saved!.Draft!.Script, Is.EqualTo("stillBroken();"));
        Assert.That(saved.Usage!.Calls, Is.EqualTo(1));
        Assert.That(saved.Usage.TextCharacters, Is.GreaterThan(0));
        Assert.That(fake.Calls, Is.EqualTo(1));
    }

    [Test]
    public void StoredWorkIsAtomicPortableAndBoundToItsOriginals_WithoutPicturesOrApiDocumentation()
    {
        string before = StudioLibrary.UserFolder;
        string folder = Path.Combine(Path.GetTempPath(), "retro-work-" + Guid.NewGuid());
        try
        {
            StudioLibrary.UserFolder = folder;
            var picture = new VideoFrame(2, 2);
            var references = new OriginalReferences([new("a", "Propio", "fingerprint", picture)]);
            var work = new StudioWork { Originals = references.Items.ToDictionary(r => r.Slot, r => r.Fingerprint),
                State = new() { Template = new StudioGame { Games = Slots, BaseSlot = "b", PlayerRequest = "Conservar mapas" },
                    Draft = new StudioGame { Games = Slots, BaseSlot = "b", Script = "versionActual();" }, Brief = "Pedido estable", Report = Ok(new()) } };
            StudioWorkStore.Save(work);
            work.State.Draft.Script = "versionSiguiente();"; StudioWorkStore.Save(work);
            var loaded = StudioWorkStore.Pending().Single();
            Assert.That(loaded.State.Draft!.Script, Is.EqualTo("versionSiguiente();"));
            Assert.That(loaded.Matches(references), Is.True);
            Assert.That(loaded.Matches(new([new("a", "Propio", "changed", picture)])), Is.False);
            Assert.That(Directory.GetFiles(StudioWorkStore.Folder), Has.Length.EqualTo(1));
            string json = File.ReadAllText(Directory.GetFiles(StudioWorkStore.Folder).Single());
            Assert.That(json, Does.Not.Contain(StudioDesigner.ApiDoc).And.Not.Contain("apiKey").And.Not.Contain("pixels"));
            File.WriteAllText(Path.Combine(StudioWorkStore.Folder, new string('a', 32) + ".json"), "rotura simulada");
            Assert.That(StudioWorkStore.Pending(), Has.Count.EqualTo(1));
            Assert.Throws<ArgumentException>(() => StudioWorkStore.Remove("../otro"));
            StudioWorkStore.Remove(work.Id); Assert.That(StudioWorkStore.Pending(), Is.Empty);
            var game = work.State.Draft.Copy(); game.Id = "recuperado"; game.WorkId = work.Id;
            StudioLibrary.Save(game);
            var resumed = work.State.Draft.Copy(); resumed.Id = "otro-nombre"; resumed.WorkId = work.Id;
            StudioLibrary.Save(resumed);
            Assert.That(resumed.Id, Is.EqualTo("recuperado"));
            Assert.That(StudioLibrary.AllVersions().Count(g => g.WorkId == work.Id), Is.EqualTo(1), "un corte después de guardar no crea otro juego");
        }
        finally { StudioLibrary.UserFolder = before; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
