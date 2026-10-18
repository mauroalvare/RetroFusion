using Retro.Ai;
using Retro.Core;
using Retro.Fusion;

namespace Fusion.Tests;

[TestFixture]
public class CodeViewTests
{
    [Test]
    public void Explanation_IsReadFromChatStyleAnswers()
    {
        string answer = """
            Claro, acá va:
            ```json
            {"name": "LoseLife", "summary": "Resta una vida.", "pseudocode": "lives--;", "comments": [{"address": "$0011", "text": "resta"}, {"address": "9999", "text": "fuera"}]}
            ```
            """;
        var x = CodeExplainer.Parse(answer);
        Assert.That(x.Name, Is.EqualTo("LoseLife"));
        var r = new Routine { Start = 0x10 };
        r.Instructions.Add(0x10);
        r.Instructions.Add(0x11);
        var notes = new CodeAnnotations();
        CodeExplainer.Apply(x, r, notes);
        Assert.That(notes.Label(0x10), Is.EqualTo("LoseLife"));
        Assert.That(notes.Comment(0x11), Is.EqualTo("resta"));
        Assert.That(notes.Comments, Has.Count.EqualTo(1), "los comentarios fuera de la rutina se descartan");
        Assert.That(notes.Pseudocode["0010"], Is.EqualTo("lives--;"));
    }

    [Test]
    public void Explanation_WithoutJson_IsAnError()
    {
        Assert.Throws<AiException>(() => CodeExplainer.Parse("No puedo ayudarte con eso."));
        var x = CodeExplainer.Parse("""{"name": "A", "summary": "b", "pseudocode": "c"}""");
        Assert.That(x.Comments, Is.Empty);
    }

    [Test]
    [Category("Compat")]
    public void ManicMiner_GameCodeIsFoundAndTouchesItsVariables()
    {
        var found = FusionPlayTests.FindGames();
        if (found == null)
            Assert.Ignore("Hacen falta roms/48.rom, roms/invaders.zip y Manic Miner en ROMs/.");
        var src = found.Value.sources["manicminer"];
        var m = MachineFactory.Create(src, found.Value.romDir);
        m.ExecutedMap = new bool[0x10000];
        MachineFactory.Boot(m, src.Profile);
        for (int i = 0; i < 300; i++)
            m.RunFrame(null);
        var map = CodeMap.Build(a => m.Memory.Peek(a), m.Flow, m.CodeEntryPoints, m.ExecutedMap);
        var game = map.Routines.Values.Where(r => r.Start >= 0x8000).ToList();
        TestContext.Out.WriteLine($"rutinas: {map.Routines.Count}, del juego: {game.Count}, ejecutadas del juego: {game.Count(r => r.Executed)}");
        Assert.That(game.Count(r => r.Executed), Is.GreaterThan(10), "el código del juego (cargado en RAM) se separa en rutinas");

        // The instructions that touch the lives and air counters are in the analysed code,
        // so the code view can show them by name.
        foreach (var variable in new[] { "lives", "air" })
        {
            string address = "$" + src.Profile.Variables[variable].Address;
            var users = map.Code.Keys.Where(a => m.Disassembler.Disassemble(x => m.Memory.Peek(x), a).Text.Contains(address)).ToList();
            TestContext.Out.WriteLine($"{variable} ({address}): {string.Join(", ", users.Select(a => a.ToString("X4")))}");
            Assert.That(users, Is.Not.Empty, $"alguna instrucción usa {variable}");
        }
    }
}
