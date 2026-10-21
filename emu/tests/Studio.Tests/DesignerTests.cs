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
    public void FreeReinterpretation_UsesSeedsOnlyWhenExplicitlyChosen()
    {
        var free = new FusionDirection(Reimagine: true);
        var prompts = Enumerable.Range(0, 6).Select(_ => StudioDesigner.ConceptPrompt(Slots, ProfileLibrary.Find, "", free)).ToList();
        Assert.That(prompts[0], Does.Contain("Semilla 1:").And.Contain("Semilla 3:"));
        Assert.That(prompts.Distinct().Count(), Is.GreaterThan(1), "las semillas cambian de un pedido a otro");
        Assert.That(prompts[0], Does.Contain("\"Manic Invaders\""), "nombra la fusión que ya existe para no repetirla");
        Assert.That(prompts[0], Does.Contain("presentación"));
        Assert.That(StudioDesigner.Guide, Does.Contain("NO es un estilo a copiar"));
        Assert.That(StudioDesigner.Guide, Does.Not.Contain(StudioDesigner.ExampleDesign.Split('\n')[0]), "el diseño del ejemplo no viaja como molde");
        string asked = StudioDesigner.ConceptPrompt(Slots, ProfileLibrary.Find, "que sea de carreras", free);
        Assert.That(asked, Does.Contain("que sea de carreras").And.Contain("Llevá los juegos más lejos"));
        Assert.That(StudioDesigner.DesignFields.Select(f => f.Key), Does.Contain("presentation"));
    }

    [Test]
    public void BaseFusion_DoesNotRandomizeTheGenreOrPresentation_AndCanReverseItsDirection()
    {
        string first = StudioDesigner.ConceptPrompt(Slots, ProfileLibrary.Find, "");
        Assert.That(first, Does.Contain("BASE ELEGIDA: games.a = Space Invaders"));
        Assert.That(first, Does.Contain("mapas").And.Contain("paleta").And.Contain("interfaz"));
        Assert.That(first, Does.Not.Contain("Semilla 1:").And.Not.Contain("Llevá los juegos más lejos"));
        string reverse = StudioDesigner.ManualConceptPrompt(Slots, ProfileLibrary.Find, "conservar el recorrido", new FusionDirection("b"));
        Assert.That(reverse, Does.Contain("BASE ELEGIDA: games.b = Manic Miner").And.Contain("screen.base(games.b)"));
        Assert.That(reverse, Does.Contain("conservar el recorrido"));
    }

    [Test]
    public async Task BaseChoiceSurvivesIdeasWritingCorrectionsAndAnAnswerThatTriesToChangeIt()
    {
        var ai = new ScriptedAi("""{"ideas":[{"title":"Dirección","pitch":"Aporte","baseSlot":"a"}]}""",
            Answer("Dirección", "fallo"), Answer("Dirección corregida", "// bien"));
        var ideas = await new StudioDesigner().ConceptsAsync(ai, Slots, ProfileLibrary.Find, "Conservar mapas", CancellationToken.None, new FusionDirection("b"));
        Assert.That(ideas.Single().Direction!.BaseSlot, Is.EqualTo("b"));
        var outcome = await new StudioDesigner().WriteAsync(ai, Slots, ProfileLibrary.Find, ideas.Single(), Fake, CancellationToken.None);
        Assert.That(outcome.Game!.BaseSlot, Is.EqualTo("b"));
        Assert.That(outcome.Attempts, Is.EqualTo(2));
        Assert.That(ai.Requests[1].Turns[0].Text, Does.Contain("BASE ELEGIDA: games.b"));
        var parsed = StudioDesigner.ParseGame("""{"baseSlot":"a","script":"// bien"}""", outcome.Game, out _)!;
        Assert.That(parsed.BaseSlot, Is.EqualTo("b"));
        string revision = StudioDesigner.RevisePrompt(parsed, Slots, ProfileLibrary.Find, "más mapas");
        Assert.That(revision, Does.Contain("BASE ELEGIDA: games.b").And.Contain("más mapas"));
    }

    [Test]
    public async Task FreeReinterpretationDoesNotSilentlyBecomeANativeBaseGame()
    {
        var ai = new ScriptedAi(Answer("Libre", "// bien"));
        var concept = new GameConcept("Libre", "", "Una escena nueva", "", Direction: new FusionDirection(Reimagine: true));
        var outcome = await new StudioDesigner().WriteAsync(ai, Slots, ProfileLibrary.Find, concept, Fake, CancellationToken.None);
        Assert.That(outcome.Game!.BaseSlot, Is.Null);
        Assert.That(ai.Requests.Single().Turns[0].Text, Does.Contain("reinterpretación libre"));
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
        Assert.That(ai.Requests[0].System, Does.Contain(GameDesignGuide.Text), "la conexión directa recibe el contrato común");
    }

    [Test]
    [NonParallelizable]
    public async Task DirectWriting_ReceivesEarlierMechanicsButNotAnExampleToClone()
    {
        string previous = StudioLibrary.UserFolder;
        string dir = Path.Combine(Path.GetTempPath(), "retro-designs-" + Guid.NewGuid());
        try
        {
            StudioLibrary.UserFolder = dir;
            StudioLibrary.Save(new StudioGame { Id = "senderos", Title = "Senderos vivos", Games = Slots,
                Pitch = "Las balas construyen rutas para escapar.",
                Design = "El mapa decide el rebote de los proyectiles. Avisos mediante luces en el terreno.",
                Script = "function draw() {}" });
            var ai = new ScriptedAi(Answer("Otra unión", "// bien"));
            await new StudioDesigner().WriteAsync(ai, Slots, ProfileLibrary.Find,
                new GameConcept("", "", "otra mezcla", ""), Fake, CancellationToken.None);
            var sent = ai.Requests.Single();
            Assert.That(sent.Turns[0].Text, Does.Contain("Las balas construyen rutas").And.Contain("luces en el terreno"));
            Assert.That(sent.System, Does.Not.Contain(StudioDesigner.ExampleScript.Trim()), "no enviar un juego entero como molde");
            var parsed = StudioDesigner.ParseConcepts("""{"ideas":[{"title":"Nueva","mechanicLink":"robar altera el mapa","uniqueRule":"dos riesgos inseparables","difference":"el espacio deja de ser fijo"}]}""");
            Assert.That(parsed.Single().Design, Does.Contain("robar altera el mapa").And.Contain("el espacio deja de ser fijo"));
        }
        finally
        {
            StudioLibrary.UserFolder = previous;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
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

    [Test]
    public void ManualWritingAndCorrections_KeepTheContractAndLatestDesign()
    {
        string start = StudioDesigner.ManualWritePrompt(Slots, ProfileLibrary.Find,
            new GameConcept("Unión", "Propia", "Una regla que une decisiones", "Dos acciones"));
        var current = new StudioGame { Title = "Actual", Design = "Diseño corregido", Script = "function draw() {}", Games = Slots };
        string correction = start + "\n\n" + StudioDesigner.ManualFixPrompt(Fake(current), current);
        Assert.That(correction, Does.Contain(GameDesignGuide.Text).And.Contain(current.ToJson()));
        Assert.That(correction, Does.Contain("Una regla que une decisiones"), "el pedido inicial acompaña a la última versión");
    }

    [Test]
    public void ProfilesWithoutVerifiedVariables_DoNotInviteInventedRomData()
    {
        var profile = new GameProfile { Id = "sin-datos", Name = "Edición local", Machine = "zx48" };
        var slots = new[] { new RecipeGame { Slot = "a", Profile = profile.Id } };
        string prompt = StudioDesigner.GamesText(slots, _ => profile);
        Assert.That(prompt, Does.Contain("No hay variables verificadas").And.Contain("no declara huellas SHA-256"));
        profile.Match.Sha256.Add(new string('a', 64));
        Assert.That(StudioDesigner.GamesText(slots, _ => profile), Does.Contain(new string('a', 64)).And.Not.Contain("no declara huellas"));
    }

    [Test]
    public void AgentWorkspace_UsesTheSameContractAndImportsDesignWithoutChangingOriginals()
    {
        string dir = Path.Combine(Path.GetTempPath(), "retro-design-workspace-" + Guid.NewGuid().ToString("N"));
        var template = new StudioGame { Id = "propio", Title = "Pedido", Games = Slots.ToList(), Design = "Diseño inicial", BaseSlot = "b" };
        try
        {
            StudioWorkspace.Create(template, "Mantener la decisión central", ProfileLibrary.Find, null, dir);
            Assert.That(File.ReadAllText(Path.Combine(dir, GameDesignGuide.FileName)), Is.EqualTo(GameDesignGuide.Text));
            Assert.That(File.ReadAllText(Path.Combine(dir, "TAREA.md")), Does.Contain(GameDesignGuide.FileName).And.Contain("regla inseparable"));
            Assert.That(File.Exists(Path.Combine(dir, "ejemplo.js")), Is.False, "no entregar un juego completo como molde");
            Assert.That(File.ReadAllText(Path.Combine(dir, "TAREA.md")), Does.Contain("BASE ELEGIDA: games.b").And.Contain("baseSlot"));
            var edited = template.Copy(); edited.Title = "Nueva versión"; edited.Design = "Diseño actualizado con regla propia";
            edited.BaseSlot = "x";
            edited.Games = new() { new RecipeGame { Slot = "x", Profile = "otro-juego" } };
            File.WriteAllText(Path.Combine(dir, StudioWorkspace.GameFile), edited.ToJson());
            File.WriteAllText(Path.Combine(dir, StudioWorkspace.ScriptFile), "function draw() {}");
            var read = StudioWorkspace.Read(dir, template)!.Value.game;
            Assert.That(read.Design, Is.EqualTo(edited.Design), "el diseño escrito por el agente debe llegar a la versión importada");
            Assert.That(read.Games.Select(g => g.Profile), Is.EqualTo(Slots.Select(g => g.Profile)), "los originales seleccionados se conservan");
            Assert.That(read.BaseSlot, Is.EqualTo("b"), "el agente no cambia la base elegida al editar el JSON");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
