using System.Text.Json;
using Retro.Ai;
using Retro.Fusion;

namespace Fusion.Tests;

/// <summary>The AI side without calling a real AI: a scripted provider plays the model.</summary>
[TestFixture]
public class DesignerTests
{
    private sealed class ScriptedAi : IAiProvider
    {
        private readonly Queue<string> _answers;
        public readonly List<AiRequest> Requests = new();
        public ScriptedAi(params string[] answers) => _answers = new Queue<string>(answers);
        public string Name => "IA de prueba";

        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_answers.Dequeue());
        }
    }

    private static IReadOnlyList<GameProfile> Games() =>
        new[] { ProfileLibrary.BuiltIn.Single(p => p.Id == "invaders"), ProfileLibrary.BuiltIn.Single(p => p.Id == "manicminer") };

    private const string Broken = """
        {"name":"Mal","description":"x","games":[{"slot":"a","profile":"invaders"},{"slot":"b","profile":"manicminer"}],
         "layout":{"mode":"overlay","base":"b","opacity":0.5},"controls":{"left":["a.left"],"right":["a.right"]},
         "rules":[{"when":"b.oxygen decreases","do":["a.ships += 1"]}]}
        """;

    private const string Good = """
        {"name":"Cañón minero","description":"Si Willy junta una llave, el cañón dispara solo.","games":[{"slot":"a","profile":"invaders"},{"slot":"b","profile":"manicminer"}],
         "layout":{"mode":"side","base":"a","opacity":0.5},"controls":{"left":["a.left","b.left"],"right":["a.right","b.right"],"fire":["a.fire"],"jump":["b.jump"]},
         "rules":[{"when":"b.score increases","do":["press a.fire 4"],"message":"¡Llave! Disparo automático","cooldown":30}]}
        """;

    [Test]
    public async Task Designer_SendsErrorsBackUntilTheRecipeIsValid()
    {
        var ai = new ScriptedAi(Broken, Good);
        var result = await new FusionDesigner().DesignAsync(ai, Games(), "que juntar llaves ayude al cañón", ProfileLibrary.Find, CancellationToken.None);

        Assert.That(result.Recipe, Is.Not.Null, string.Join("\n", result.Errors));
        Assert.That(result.Attempts, Is.EqualTo(2));
        Assert.That(result.Recipe!.Name, Is.EqualTo("Cañón minero"));
        Assert.That(result.Recipe.Author, Is.EqualTo("IA de prueba"));

        // first request: the guide, the verified profiles, the player's wish and the schema
        var first = ai.Requests[0];
        Assert.That(first.System, Does.Contain("FUSIONES"));
        Assert.That(first.Turns[0].Text, Does.Contain("80BC").Or.Contain("air"));
        Assert.That(first.Turns[0].Text, Does.Contain("que juntar llaves ayude al cañón"));
        Assert.That(first.Schema, Is.Not.Null);
        // second request: the broken answer and the validator's explanation
        var second = ai.Requests[1];
        Assert.That(second.Turns, Has.Count.EqualTo(3));
        Assert.That(second.Turns[1].FromUser, Is.False);
        Assert.That(second.Turns[2].Text, Does.Contain("no tiene la variable \"oxygen\""));
    }

    [Test]
    public async Task Designer_GivesUpAfterThreeBadAnswers()
    {
        var ai = new ScriptedAi(Broken, Broken, "no sé");
        var result = await new FusionDesigner().DesignAsync(ai, Games(), "", ProfileLibrary.Find, CancellationToken.None);
        Assert.That(result.Recipe, Is.Null);
        Assert.That(result.Attempts, Is.EqualTo(3));
        Assert.That(result.Errors, Is.Not.Empty);
    }

    [Test]
    public void Schema_FollowsStructuredOutputRules()
    {
        var schema = FusionDesigner.Schema(new[] { "invaders", "manicminer" });
        int objects = 0;
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "object")
                {
                    objects++;
                    Assert.That(e.GetProperty("additionalProperties").GetBoolean(), Is.False, "todo objeto necesita additionalProperties: false");
                }
                foreach (var p in e.EnumerateObject())
                    Walk(p.Value);
            }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray())
                    Walk(x);
        }
        Walk(schema);
        Assert.That(objects, Is.GreaterThanOrEqualTo(5));
        Assert.That(schema.ToString(), Does.Contain("manicminer"));
    }

    [Test]
    public void AgentWorkspace_ValidatesWhatTheAgentWrites()
    {
        string dir = AgentWorkspace.Create(Games(), "probá algo raro");
        try
        {
            Assert.That(File.ReadAllText(Path.Combine(dir, "TAREA.md")), Does.Contain("probá algo raro"));
            Assert.That(File.Exists(Path.Combine(dir, "AGENTS.md")) && File.Exists(Path.Combine(dir, "GEMINI.md")), Is.True);
            Assert.That(AgentWorkspace.Poll(dir, Games(), ProfileLibrary.Find), Is.Null, "todavía no hay receta");

            File.WriteAllText(Path.Combine(dir, AgentWorkspace.RecipeFile), Broken);
            var bad = AgentWorkspace.Poll(dir, Games(), ProfileLibrary.Find);
            Assert.That(bad!.Value.recipe, Is.Null);
            Assert.That(File.ReadAllText(Path.Combine(dir, AgentWorkspace.ErrorsFile)), Does.Contain("oxygen"));

            File.WriteAllText(Path.Combine(dir, AgentWorkspace.RecipeFile), Good);
            var ok = AgentWorkspace.Poll(dir, Games(), ProfileLibrary.Find);
            Assert.That(ok!.Value.recipe, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(dir, AgentWorkspace.ErrorsFile)), Is.False, "los errores se borran al corregir");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void ManualPrompt_HasEverythingForAnyChat()
    {
        string prompt = FusionDesigner.ManualPrompt(Games(), "modo difícil");
        Assert.That(prompt, Does.Contain("modo difícil"));
        Assert.That(prompt, Does.Contain("additionalProperties"));
        Assert.That(prompt, Does.Contain("air"));
        Assert.That(prompt, Does.Contain("aliens"));
    }
}
