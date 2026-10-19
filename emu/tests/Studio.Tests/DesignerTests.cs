using Retro.Ai;
using Retro.Fusion;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture]
public class DesignerTests
{
    private sealed class ScriptedAi : IAiProvider
    {
        private readonly Queue<string> _answers;
        public List<AiRequest> Requests { get; } = new();
        public ScriptedAi(params string[] answers) => _answers = new Queue<string>(answers);
        public string Name => "IA de prueba";
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_answers.Dequeue());
        }
    }

    private static readonly List<RecipeGame> Slots = new()
    {
        new RecipeGame { Slot = "a", Profile = "invaders" },
        new RecipeGame { Slot = "b", Profile = "manicminer" },
    };

    private static ValidationReport Fake(StudioGame g) => g.Script.Contains("bien")
        ? new ValidationReport(true, null, Array.Empty<string>(), 900, "Corrió 900 ticks sin errores.", null)
        : new ValidationReport(false, "Error de JavaScript (juego.js, línea 1): x is not defined", Array.Empty<string>(), 3, "Se detuvo en el tick 3: x is not defined", null);

    private static string Answer(string title, string script) =>
        System.Text.Json.JsonSerializer.Serialize(new { title, approach = "Invasión", pitch = "p", howToPlay = "h", changes = "c", script });

    [Test]
    public void Guide_HasTheApiTheGamesAndAnExample()
    {
        Assert.That(StudioDesigner.Guide, Does.Contain("screen.sprite"));
        Assert.That(StudioDesigner.Guide, Does.Contain("function tick"));
        Assert.That(StudioDesigner.Guide, Does.Contain("Lista de control"), "la IA recibe el método");
        Assert.That(StudioDesigner.Guide, Does.Contain("game.state('title')"));
        Assert.That(StudioDesigner.Guide, Does.Contain("Manic Invaders"), "y un ejemplo que lo sigue");
        Assert.That(StudioDesigner.ExampleDesign, Does.Contain("Juego base"));
        var text = StudioDesigner.GamesText(Slots, ProfileLibrary.Find);
        Assert.That(text, Does.Contain("games.a: Space Invaders"));
        Assert.That(text, Does.Contain("g.aliens()"));
        Assert.That(text, Does.Contain("g.willy()"));
        Assert.That(text, Does.Contain("alienA (16x8)"));
    }

    [Test]
    public void EveryRequest_StartsFromAnotherSeed_AndAvoidsTheExistingFusions()
    {
        var prompts = Enumerable.Range(0, 6).Select(_ => StudioDesigner.ConceptPrompt(Slots, ProfileLibrary.Find, "")).ToList();
        Assert.That(prompts[0], Does.Contain("Semilla 1:").And.Contain("Semilla 3:"));
        Assert.That(prompts.Distinct().Count(), Is.GreaterThan(1), "las semillas cambian de un pedido a otro");
        Assert.That(prompts[0], Does.Contain("\"Manic Invaders\""), "nombra la fusión que ya existe para no repetirla");
        Assert.That(prompts[0], Does.Contain("presentación"));
        Assert.That(StudioDesigner.Guide, Does.Contain("NO es un estilo a copiar"));
        Assert.That(StudioDesigner.Guide, Does.Not.Contain(StudioDesigner.ExampleDesign.Split('\n')[0]), "el diseño del ejemplo no viaja como molde");
        string asked = StudioDesigner.ConceptPrompt(Slots, ProfileLibrary.Find, "que sea de carreras");
        Assert.That(asked, Does.Contain("que sea de carreras").And.Contain("Llevá los juegos más lejos"));
        Assert.That(StudioDesigner.DesignFields.Select(f => f.Key), Does.Contain("presentation"));
    }

    [Test]
    public async Task Concepts_AreParsed()
    {
        var ai = new ScriptedAi("""{"ideas": [{"title": "Uno", "approach": "Invasión", "pitch": "p1", "howToPlay": "h1", "baseGame": "Manic Miner", "levels": "las 20 cavernas"}, {"title": "Dos", "approach": "Poderes", "pitch": "p2", "howToPlay": "h2"}]}""");
        var ideas = await new StudioDesigner().ConceptsAsync(ai, Slots, ProfileLibrary.Find, "cooperativo", CancellationToken.None);
        Assert.That(ideas.Select(i => i.Title), Is.EqualTo(new[] { "Uno", "Dos" }));
        Assert.That(ideas[0].Design, Does.Contain("Juego base: Manic Miner"));
        Assert.That(ideas[0].Design, Does.Contain("las 20 cavernas"));
        Assert.That(ai.Requests[0].Turns[0].Text, Does.Contain("cooperativo"));
        Assert.That(ai.Requests[0].Schema, Is.Not.Null);
    }

    [Test]
    public async Task Write_SendsTheTestFailureBackAndKeepsTheFixedVersion()
    {
        var ai = new ScriptedAi(Answer("Mi juego", "function tick() { x(); }"), Answer("Mi juego", "// bien\nfunction tick() {}"));
        var outcome = await new StudioDesigner().WriteAsync(ai, Slots, ProfileLibrary.Find,
            new GameConcept("Mi juego", "Invasión", "p", "h"), Fake, CancellationToken.None);
        Assert.That(outcome.Game, Is.Not.Null);
        Assert.That(outcome.Attempts, Is.EqualTo(2));
        Assert.That(outcome.Game!.Script, Does.Contain("bien"));
        Assert.That(outcome.Game.Games.Select(g => g.Profile), Is.EqualTo(new[] { "invaders", "manicminer" }));
        var fix = ai.Requests[1].Turns.Last().Text;
        Assert.That(fix, Does.Contain("x is not defined"), "el error de la prueba vuelve a la IA");
    }

    [Test]
    public async Task Revise_MakesTheNextVersion()
    {
        var current = new StudioGame { Id = "mi-juego", Title = "Mi juego", Games = Slots, Script = "// v1", Version = 3 };
        var ai = new ScriptedAi(Answer("Mi juego cooperativo", "// bien v4"));
        var outcome = await new StudioDesigner().ReviseAsync(ai, current, ProfileLibrary.Find, "que sea cooperativo", Fake, CancellationToken.None);
        Assert.That(outcome.Game!.Version, Is.EqualTo(4));
        Assert.That(outcome.Game.Id, Is.EqualTo("mi-juego"));
        Assert.That(outcome.Game.Request, Is.EqualTo("que sea cooperativo"));
        Assert.That(outcome.Game.Title, Is.EqualTo("Mi juego cooperativo"));
        Assert.That(ai.Requests[0].Turns[0].Text, Does.Contain("// v1"), "la IA recibe el guion actual");
    }

    [Test]
    public async Task SimulatedAi_ReplacesAFullGameScreenWithManicInvaders()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("Hacen falta los juegos del usuario para la prueba del estudio.");
        var manic = StudioLibrary.BuiltIn.Single(g => g.Id == "manic-invaders");
        var ai = new ScriptedAi(Answer("Superpuesto", "function draw() { screen.game(games.b); }"),
            Answer("Escena propia", manic.Script));
        var outcome = await new StudioDesigner().WriteAsync(ai, Slots, ProfileLibrary.Find,
            new GameConcept("Mi juego", "Escena propia", "p", "h"),
            game => StudioValidator.Run(game, found.sources, found.romDir), CancellationToken.None);
        Assert.That(outcome.Attempts, Is.EqualTo(2));
        Assert.That(outcome.Game, Is.Not.Null, outcome.Error);
        Assert.That(outcome.Report!.Ok, Is.True, outcome.Report.Summary);
        Assert.That(ai.Requests[1].Turns.Last().Text, Does.Contain("sprites y tiles").And.Contain("escena"));
        Assert.That(outcome.Game!.Script, Does.Not.Contain("screen.game"));
    }

    [Test]
    public void ChatAnswers_WithJsonAndCodeBlocks_AreRead()
    {
        string answer = """
            ¡Acá va tu juego!
            ```json
            {"title": "Caverna Espacial", "approach": "Remix", "pitch": "Willy en el espacio", "howToPlay": "Flechas", "changes": "nuevo"}
            ```
            ```javascript
            function init() { screen.size(256, 192); }
            function draw() { screen.text("hola {}", 10, 10); }
            ```
            Que lo disfrutes.
            """;
        var game = StudioDesigner.ParseGame(answer, new StudioGame { Games = Slots }, out var error);
        Assert.That(error, Is.Null);
        Assert.That(game!.Title, Is.EqualTo("Caverna Espacial"));
        Assert.That(game.Script, Does.StartWith("function init()"));
        Assert.That(game.Script, Does.Contain("hola {}"));
        Assert.That(game.Id, Is.EqualTo("caverna-espacial"));

        Assert.That(StudioDesigner.ParseGame("no sé", new StudioGame(), out error), Is.Null);
        Assert.That(error, Does.Contain("guion"));
    }
}
