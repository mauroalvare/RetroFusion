using System.Text.Json;
using Retro.Ai;
using Retro.Asm;

namespace Compat.Tests;

/// <summary>The loop where an AI writes a game from scratch, with a scripted AI instead of a real one.</summary>
[TestFixture]
public class GameWriterTests
{
    private string _firmware = "";

    [OneTimeSetUp]
    public void FindFirmware()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms (48.rom y c64/).");
        _firmware = Path.Combine(dir!.FullName, "roms");
    }

    /// <summary>Answers in order and keeps every request (and whether its picture existed when it arrived).</summary>
    private sealed class ScriptedAi : IAiProvider
    {
        private readonly Queue<string> _answers;
        public readonly List<AiRequest> Requests = new();
        public readonly List<bool> PictureExisted = new();
        public ScriptedAi(params string[] answers) => _answers = new Queue<string>(answers);
        public string Name => "IA de prueba";

        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            PictureExisted.Add(request.Images is { Count: > 0 } && File.Exists(request.Images[0]));
            return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : "no sé");
        }
    }

    private static string Answer(string title, string source) =>
        JsonSerializer.Serialize(new { title, pitch = "un juego de prueba", howToPlay = "joystick 2", changes = "primera versión", source });

    private static string C64 => Templates.For("c64")!.Source;

    [Test]
    public async Task AnAssemblerError_GoesBackWithItsLine_AndTheFixedGamePasses()
    {
        var ai = new ScriptedAi(Answer("Roto", C64.Replace("inc $D020", "inx $D020")), Answer("Arreglado", C64));
        var outcome = await new GameWriter().WriteAsync(ai, "c64", "un juego de un faro", _firmware, CancellationToken.None);
        Assert.That(outcome.Ok, Is.True, outcome.Problem);
        Assert.That(outcome.Attempts, Is.EqualTo(2));
        Assert.That(outcome.Game!.Title, Is.EqualTo("Arreglado"));
        Assert.That(outcome.Report!.File, Is.Not.Null.And.Not.Empty, "queda el .prg listo para cargar");
        Assert.That(ai.Requests[0].System, Does.Contain("Commodore 64").And.Contain("$DC00").And.Contain("ORIGINAL"), "la guía de la máquina va en el sistema");
        Assert.That(ai.Requests[0].Schema, Is.Not.Null);
        Assert.That(ai.Requests[0].Turns.Single().Text, Does.Contain("un juego de un faro"));
        string fix = ai.Requests[1].Turns.Single().Text;
        Assert.That(fix, Does.Contain("inx $D020").And.Contain("línea").And.Contain("[FALLA] ensambla"));
        Assert.That(ai.Requests[1].Turns, Has.Count.EqualTo(1), "cada pedido es un solo turno: la conversación no crece");
    }

    [Test]
    public async Task AGameThatRunsButIgnoresTheControls_GetsTheReportAndAPicture()
    {
        var ai = new ScriptedAi(Answer("Quieto", C64.Replace("lda $DC00", "lda #$FF")), Answer("Se mueve", C64));
        var outcome = await new GameWriter().WriteAsync(ai, "c64", "", _firmware, CancellationToken.None);
        Assert.That(outcome.Ok, Is.True, outcome.Problem);
        Assert.That(ai.Requests[1].Turns.Single().Text, Does.Contain("[FALLA] controles").And.Contain("RETROEMU"));
        Assert.That(ai.PictureExisted[1], Is.True, "la imagen de la pantalla acompaña el informe");
        Assert.That(File.Exists(ai.Requests[1].Images![0]), Is.False, "y se borra después");
    }

    [Test]
    public async Task AnAiThatNeverGetsItRight_EndsWithTheReason()
    {
        var ai = new ScriptedAi("no tengo idea", Answer("Roto", "  lda #\n"));
        var outcome = await new GameWriter { MaxAttempts = 3 }.WriteAsync(ai, "zx48", "", _firmware, CancellationToken.None);
        Assert.That(outcome.Ok, Is.False);
        Assert.That(outcome.Attempts, Is.EqualTo(3));
        Assert.That(outcome.Problem, Is.Not.Empty);
        Assert.That(ai.Requests[1].Turns.Single().Text, Does.StartWith("No pude leer tu respuesta"));
    }

    [Test]
    public void TheAnswer_IsReadFromTheJsonOrFromACodeBlock()
    {
        var d = GameWriter.ParseDraft("Acá va:\n```asm\n  nop\n```\n", out var error);
        Assert.That(d!.Source, Is.EqualTo("  nop\n"));
        Assert.That(error, Is.Null);
        d = GameWriter.ParseDraft(Answer("Faro", "start: rts\n"), out _);
        Assert.That(d!.Title, Is.EqualTo("Faro"));
        Assert.That(d.Source, Is.EqualTo("start: rts\n"));
        Assert.That(GameWriter.ParseDraft("{\"title\": \"sin código\"}", out error), Is.Null);
        Assert.That(error, Does.Contain("source"));
    }

    [Test]
    public void WithoutARequest_EachTimeStartsFromAnotherSeed()
    {
        var t = Templates.For("zx48")!;
        var prompts = Enumerable.Range(0, 6).Select(i => GameWriter.FirstPrompt(t, "", new Random(i))).ToList();
        Assert.That(prompts.Distinct().Count(), Is.GreaterThanOrEqualTo(5));
        Assert.That(prompts[0], Does.Contain("género:").And.Contain("ZX Spectrum 48K"));
        Assert.That(GameWriter.FirstPrompt(t, "una carrera de caracoles", new Random(1)), Does.StartWith("Pedido del jugador: una carrera de caracoles"));
        Assert.That(GameWriter.Guide(t), Does.Contain("no cambies IY").And.Contain("org $8000"));
    }
}
