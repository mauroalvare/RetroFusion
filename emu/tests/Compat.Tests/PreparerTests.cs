using Retro.Ai;
using Retro.Compat;
using Retro.Core;
using Retro.Fusion;

namespace Compatibility.Tests;

/// <summary>
/// Preparing a game for the fusions with nothing but the file: the key trial, an advisor
/// that reads the screen (an AI stand-in), the lab and the profile tied to the hash.
/// </summary>
[TestFixture]
public class PreparerTests
{
    private string _dir = "", _firmware = "";

    [SetUp]
    public void SetUp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms.");
        _firmware = Path.Combine(dir!.FullName, "roms");
        _dir = Path.Combine(Path.GetTempPath(), "retro-prep-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private PrepareOptions Options(IPlayAdvisor? advisor = null, int rounds = 8) => new()
    {
        FirmwareDirectory = _firmware, Advisor = advisor, MaxRounds = rounds, TrialRounds = 2,
        ProfilesFolder = Path.Combine(_dir, "perfiles"), LabFrames = 1300,
    };

    [Test]
    public async Task KeyTrial_GetsPastTheTrainerAndTheTitle_AndSavesAProfileTiedToTheHash()
    {
        string game = Path.Combine(_dir, "juego.prg");
        File.WriteAllBytes(game, C64TestProgram.Game(C64TestProgram.TrainerQuestion, C64TestProgram.PressFire));
        var r = await GamePreparer.PrepareAsync(game, Options(), null, CancellationToken.None);
        Assert.That(r.Ok, Is.True, r.Problem + "\n" + string.Join("\n", r.Log));
        Assert.That(r.Boot, Does.Contain("press N 6"), "contestó N a la pregunta de trucos");
        Assert.That(r.Boot, Does.Contain("press JOY2 FIRE 6"));
        Assert.That(r.Controls!.Left, Is.EqualTo("JOY2 LEFT"));

        var p = r.Profile!;
        Assert.That(p.Variables["x"].Address, Is.EqualTo("C001"));
        Assert.That(p.Variables["x"].ReadOnly, Is.False, "la posición guardada se puede escribir");
        Assert.That((p.Variables["score"].Address, p.Variables["score"].Encoding, p.Variables["score"].Bytes), Is.EqualTo(("C020", "bcd", 2)));
        Assert.That(p.Variables["lives"].Address, Is.EqualTo("C002"));
        Assert.That(p.Variables.Values.Select(v => v.Address), Has.None.EqualTo("C005"), "la copia que el juego reescribe no es una variable");
        Assert.That(p.Verified, Does.Contain("Comprobado: una máquina nueva"));

        // the profile file is found again by the hash of the copy, whatever its name
        var saved = GameProfile.Parse(File.ReadAllText(r.ProfilePath!));
        var renamed = new List<MediaFile> { new("otro-nombre.prg", File.ReadAllBytes(game), "otro-nombre.prg") };
        Assert.That(saved.Matches(renamed), Is.True);
        Assert.That(saved.Match.Sha256, Has.Count.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(_dir, "perfiles", "lab", p.Id + ".json")), "guarda el informe del laboratorio");
        var other = new List<MediaFile> { new("juego.prg", C64TestProgram.Game(), "juego.prg") };
        Assert.That(saved.Matches(other), Is.False, "otra edición no usa este perfil");
    }

    /// <summary>Stands in for the user's AI: reads the screen text and answers what it asks for.</summary>
    private sealed class ReadingAdvisor : IPlayAdvisor
    {
        public readonly List<PlayObservation> Seen = new();
        public string Name => "IA de prueba";

        public Task<PlayAdvice> AdviseAsync(PlayObservation o, CancellationToken ct)
        {
            Seen.Add(o);
            var advice = o.ScreenText != null && o.ScreenText.Contains("TYPE G")
                ? new PlayAdvice { Reason = "pide la G", Actions = { new PlayAction("G", 8, 120) } }
                : new PlayAdvice { Reason = "espero", Actions = { new PlayAction("", 0, 100) } };
            return Task.FromResult(advice);
        }
    }

    [Test]
    public async Task WhenTheKeyTrialGetsNowhere_TheAdvisorReadsTheScreen()
    {
        string game = Path.Combine(_dir, "juego-g.prg");
        File.WriteAllBytes(game, C64TestProgram.Game(C64TestProgram.TypeG));
        var advisor = new ReadingAdvisor();
        var r = await GamePreparer.PrepareAsync(game, Options(advisor), null, CancellationToken.None);
        Assert.That(r.Ok, Is.True, r.Problem + "\n" + string.Join("\n", r.Log));
        Assert.That(r.Advisors, Is.EqualTo(new[] { "prueba de teclas", "IA de prueba" }));
        Assert.That(r.Boot, Does.Contain("press G 8"));
        var first = advisor.Seen[0];
        Assert.That(first.Round, Is.EqualTo(3), "la IA entra después de las rondas de prueba de teclas");
        Assert.That(first.Describe(), Does.Contain("TYPE G TO START").And.Contain("JOY2 FIRE: no cambia nada").And.Contain("Ronda 1 (prueba de teclas)"));
        Assert.That(first.Picture, Is.Not.Null);
    }

    /// <summary>An advisor that knows the game: names the hero's position and the score, and drops the lives.</summary>
    private sealed class NamingAdvisor : IPlayAdvisor, IVariableNamer
    {
        public VariableQuestion? Asked;
        public string Name => "IA que nombra";
        public Task<PlayAdvice> AdviseAsync(PlayObservation o, CancellationToken ct) =>
            Task.FromResult(new PlayAdvice { Actions = { new PlayAction("", 0, 100) } });
        public Task<List<VariableName>> NameVariablesAsync(VariableQuestion q, CancellationToken ct)
        {
            Asked = q;
            return Task.FromResult(new List<VariableName>
            {
                new("C001", "heroX", "Posición horizontal del héroe.", true),
                new("$C020", "score", "Puntaje en BCD.", true),
                new("C002", "lives", "No estoy seguro.", false),
            });
        }
    }

    [Test]
    public async Task TheAdvisorNamesTheVerifiedVariables()
    {
        string game = Path.Combine(_dir, "juego.prg");
        File.WriteAllBytes(game, C64TestProgram.Game(C64TestProgram.PressFire));
        var namer = new NamingAdvisor();
        var r = await GamePreparer.PrepareAsync(game, Options(namer), null, CancellationToken.None);
        Assert.That(r.Ok, Is.True, r.Problem + "\n" + string.Join("\n", r.Log));
        Assert.That(namer.Asked, Is.Not.Null);
        Assert.That(namer.Asked!.Candidates.Select(c => c.Address), Does.Contain("C001").And.Contain("C020"));
        Assert.That(namer.Asked.Describe(), Does.Contain("provisorio").And.Contain("vale"));
        Assert.That(namer.Asked.Picture, Is.Not.Null);
        var p = r.Profile!;
        Assert.That(p.Variables.Keys, Is.EquivalentTo(new[] { "heroX", "score" }), "solo las que la IA conserva, con sus nombres");
        Assert.That(p.Variables["score"].Encoding, Is.EqualTo("bcd"));
        Assert.That(p.Verified, Does.Contain("los nombres los puso la IA"));
    }

    [Test]
    public async Task WithoutAWayIn_ItSaysWhyAndSavesNothing()
    {
        string game = Path.Combine(_dir, "juego-g.prg");
        File.WriteAllBytes(game, C64TestProgram.Game(C64TestProgram.TypeG));
        var r = await GamePreparer.PrepareAsync(game, Options(rounds: 3), null, CancellationToken.None);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Problem, Does.Contain("No llegué a la partida"));
        Assert.That(Directory.Exists(Path.Combine(_dir, "perfiles")), Is.False);
    }

    private sealed class CannedAi : IAiProvider
    {
        public readonly List<AiRequest> Requests = new();
        public string Name => "IA enlatada";

        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult("Veo la pregunta de trucos.\n```json\n{\"playing\": false, \"reason\": \"trainer\", \"actions\": [{\"keys\": \"N\", \"hold\": 6, \"wait\": 200}, {\"keys\": \"JOY2 FIRE\", \"hold\": 6, \"wait\": 100}]}\n```");
        }
    }

    [Test]
    public async Task AiAdvisor_SendsTheScreenAndTheSchema_AndReadsTheActions()
    {
        var ai = new CannedAi();
        var advisor = new AiPlayAdvisor(ai);
        var advice = await advisor.AdviseAsync(new PlayObservation { Game = "Prueba", ScreenText = "TRAINER? Y/N", Round = 1 }, CancellationToken.None);
        Assert.That(advice.Actions, Is.EqualTo(new[] { new PlayAction("N", 6, 200), new PlayAction("JOY2 FIRE", 6, 100) }));
        Assert.That(ai.Requests[0].Schema, Is.Not.Null);
        Assert.That(ai.Requests[0].System, Does.Contain("PARTIDA"));
        Assert.That(ai.Requests[0].Turns[^1].Text, Does.Contain("TRAINER? Y/N"));
        Assert.That(() => AiPlayAdvisor.Parse("no sé"), Throws.InstanceOf<AiException>());
    }

    [Test]
    public async Task FolderAdvisor_WritesTheRoundAndWaitsForTheAnswer()
    {
        string folder = Path.Combine(_dir, "agente");
        var advisor = new FolderPlayAdvisor(folder, TimeSpan.FromSeconds(20));
        var pending = advisor.AdviseAsync(new PlayObservation { Game = "Prueba", Round = 4 }, CancellationToken.None);
        for (int i = 0; i < 50 && !File.Exists(Path.Combine(folder, "ronda-04.md")); i++)
            await Task.Delay(100);
        Assert.That(File.ReadAllText(Path.Combine(folder, "LEEME.md")), Does.Contain("respuesta-NN.json"));
        File.WriteAllText(Path.Combine(folder, "respuesta-04.json"), "{\"playing\": true, \"reason\": \"ya juega\", \"actions\": []}");
        var advice = await pending;
        Assert.That(advice.Playing, Is.True);
    }
}

[TestFixture, Explicit("solo para depurar: vuelca los programas de prueba")]
public class DumpTestPrograms
{
    [Test]
    public void Dump()
    {
        string dir = Environment.GetEnvironmentVariable("RETRO_DUMP") ?? Path.GetTempPath();
        File.WriteAllBytes(Path.Combine(dir, "trainer-fire.prg"), C64TestProgram.Game(C64TestProgram.TrainerQuestion, C64TestProgram.PressFire));
        File.WriteAllBytes(Path.Combine(dir, "plain.prg"), C64TestProgram.Game());
    }
}
