using System.Text.Json;
using Retro.Ai;
using Retro.Core;
using Retro.Fusion;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture]
public sealed class OriginalReferenceTests
{
    private static readonly List<RecipeGame> Slots = [new() { Slot = "a", Profile = "invaders" }, new() { Slot = "b", Profile = "manicminer" }];
    private static OriginalReferences OwnReferences()
    {
        var picture = new VideoFrame(32, 32); Array.Fill(picture.Pixels, unchecked((int)0xFF123456));
        return new OriginalReferences([new("a", "Original A", "own-a", picture), new("b", "Original B", "own-b", picture)]);
    }

    private sealed class Observer(bool images) : IAiProvider
    {
        public string Name => "IA simulada";
        public bool SupportsImages => images;
        public bool Fail;
        public AiRequest? Request;
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Request = request;
            if (images)
                Assert.That(request.Images, Has.Count.EqualTo(2));
            foreach (string path in request.Images ?? [])
                Assert.That(File.ReadAllBytes(path).Take(8), Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            if (Fail) throw new AiException("Límite simulado");
            return Task.FromResult(JsonSerializer.Serialize(new { title = "Propio", script = "function draw() {}" }));
        }
    }

    [Test]
    public void AProviderFailureAlsoReleasesOnlyItsTemporaryPictures()
    {
        var ai = new Observer(true) { Fail = true };
        Assert.ThrowsAsync<AiException>(async () => await new StudioDesigner().WriteAsync(ai, Slots, ProfileLibrary.Find,
            new GameConcept("Prueba", "", "Conservar el mundo", ""), _ => throw new AssertionException("No debía llegar a probar"),
            CancellationToken.None, references: OwnReferences()));
        Assert.That(ai.Request!.Images!.All(p => !File.Exists(p)), Is.True);
    }

    [TestCase(true)] [TestCase(false)]
    public async Task WritingUsesRealAttachmentsOnlyWhenSupported_AndReleasesOwnTemporaryFiles(bool images)
    {
        var ai = new Observer(images);
        var concept = new GameConcept("Prueba", "", "Mantener el mundo", "", Direction: new("b"));
        await new StudioDesigner().WriteAsync(ai, Slots, ProfileLibrary.Find, concept,
            _ => new ValidationReport(true, null, [], 1, "Prueba simulada", null), CancellationToken.None,
            references: OwnReferences());
        string sent = ai.Request!.Turns[0].Text;
        Assert.That(sent, Does.Contain("Original B; BASE").And.Contain("32x32").And.Contain("#123456"));
        if (images)
        {
            Assert.That(ai.Request.Images!.All(p => !File.Exists(p)), Is.True, "las imágenes se conservan hasta recibir la respuesta y después se liberan");
            Assert.That(sent, Does.Contain("original-b.png"));
        }
        else
        {
            Assert.That(ai.Request.Images, Is.Null);
            Assert.That(sent, Does.Contain("sin imágenes adjuntas").And.Contain("No afirmes haber visto"));
        }
    }

    [Test]
    public void AgentFolderIncludesPortablePicturesAndTheirRoles()
    {
        string folder = Path.Combine(Path.GetTempPath(), "retro-reference-folder-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = new StudioGame { Games = Slots, BaseSlot = "b" };
            StudioWorkspace.Create(game, "Conservar el original", ProfileLibrary.Find, null, folder, OwnReferences());
            Assert.That(File.Exists(Path.Combine(folder, "original-a.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(folder, "original-b.png")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(folder, "ORIGINALES.md")), Does.Contain("Original B; BASE"));
            Assert.That(File.ReadAllText(Path.Combine(folder, "TAREA.md")), Does.Contain("ORIGINALES.md"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Test]
    public void AReferenceNamesTheEditionAndObservationWithoutClaimingItsPurpose()
    {
        var picture = new VideoFrame(8, 8);
        var survey = new RoutineSurvey(42, false, true, false,
            [new(0x8010, 3, 9, new string('B', 64), [new(0x8012, 0x4000, true, false, 3)])]);
        var references = new OriginalReferences([new("a", "Propio", new string('A', 64), picture, survey,
            [new("propio.nes", 24, new string('C', 64))])]);
        string text = references.Describe("a", false);
        Assert.That(text, Does.Contain("archivos + perfil + firmware").And.Contain(new string('A', 64)));
        Assert.That(text, Does.Contain("propio.nes").And.Contain("SHA-256 " + new string('C', 64)));
        Assert.That(text, Does.Contain("42 pasos").And.Contain("sin monitor de lecturas E/S").And.Contain("Resumen acotado"));
        Assert.That(text, Does.Contain("$8010").And.Contain("Sitio $8012").And.Contain("memoria $4000, 3 veces"));
        Assert.That(text, Does.Contain("no demuestra qué mecánica").And.Contain("No se entregan bytes"));
    }

    [Test]
    public void ReferencesAreObservationsOfOriginals_NeverExecutionOfTheGeneratedScript()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("Hacen falta los originales para la referencia nativa.");
        var game = StudioLibrary.BuiltIn.Single(g => g.Id == "manic-invaders").Copy();
        game.BaseSlot = "b"; game.Script = "throw new Error('este guion no se debe ejecutar');";
        var references = OriginalReferences.Build(game, found.sources, found.romDir);
        Assert.That(references.Items.Select(r => (r.Slot, r.Picture.Width, r.Picture.Height)),
            Is.EqualTo(new[] { ("a", 224, 256), ("b", 256, 192) }));
        Assert.That(references.Items.All(r => r.Picture.Pixels.Distinct().Count() > 1), Is.True);
        Assert.That(references.Items.All(r => r.Fingerprint.Length == 64), Is.True);
        Assert.That(game.Script, Does.Contain("throw new Error"), "no se modifica la versión del jugador");
    }
}
