using System.Text.Json;
using Retro.Ai;

namespace Fusion.Tests;

/// <summary>
/// The subscription tools (Claude Code, Codex CLI, Antigravity CLI) are run as programs.
/// These tests use small fake programs, so nothing real is installed or called.
/// </summary>
[TestFixture]
[Platform("Win")]
public class CliProviderTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "retroemu cli test " + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Fake(string name, string body)
    {
        string path = Path.Combine(_dir, name + ".cmd");
        File.WriteAllText(path, "@echo off\r\n" + body.Replace("\n", "\r\n"));
        return path;
    }

    private static AiRequest Request(bool schema = false) => new(
        "Sos un diseñador de juegos.",
        new[] { new AiTurn(true, "Proponé una idea"), new AiTurn(false, "{}"), new AiTurn(true, "Otra, mejor") },
        schema ? JsonSerializer.SerializeToElement(new { type = "object", properties = new { title = new { type = "string" } } }) : null);

    [Test]
    public void TheChosenModel_GoesWithEachToolsOwnFlag()
    {
        Assert.That(CliAgentProvider.ModelArgs("sub-claude", "opus"), Is.EqualTo(new[] { "--model", "opus" }));
        Assert.That(CliAgentProvider.ModelArgs("sub-codex", " gpt-x "), Is.EqualTo(new[] { "-m", "gpt-x" }));
        Assert.That(CliAgentProvider.ModelArgs("sub-google", "flash"), Is.EqualTo(new[] { "--model", "flash" }));
        Assert.That(CliAgentProvider.ModelArgs("sub-claude", ""), Is.Empty, "vacío = el modelo de la herramienta");
        var settings = new AiSettings { Provider = "sub-claude" };
        settings.CliModels["sub-claude"] = "sonnet";
        Assert.That(settings.CreateProvider()!.Name, Does.Contain("sonnet"));
        settings.SessionModel = "haiku";
        Assert.That(settings.CreateProvider()!.Name, Does.Contain("haiku"), "--modelo manda sobre lo guardado");
    }

    [Test]
    public void Flatten_KeepsInstructionsTurnsAndSchema()
    {
        string text = CliAgentProvider.Flatten(Request(schema: true));
        Assert.That(text, Does.Contain("Sos un diseñador de juegos."));
        Assert.That(text.IndexOf("Proponé una idea"), Is.LessThan(text.IndexOf("Otra, mejor")));
        Assert.That(text, Does.Contain("\"title\""));
        Assert.That(text, Does.Contain("SOLO un objeto JSON"));
    }

    [Test]
    public async Task ClaudeCode_ReadsTheResultFromItsJson()
    {
        // consumes the prompt from stdin, answers like `claude -p --output-format json`
        string exe = Fake("claude", "more > nul\necho {\"type\":\"result\",\"is_error\":false,\"result\":\"{\\\"title\\\":\\\"Caverna\\\"}\"}");
        var ai = new CliAgentProvider(CliTools.Claude, exe);
        string answer = await ai.CompleteAsync(Request(schema: true), CancellationToken.None);
        Assert.That(answer, Is.EqualTo("{\"title\":\"Caverna\"}"));
        Assert.That(ai.Name, Does.Contain("suscripción"));
    }

    [Test]
    public void ClaudeCode_WithoutLogin_SaysWhatToDo()
    {
        string exe = Fake("claude", "more > nul\necho {\"type\":\"result\",\"is_error\":true,\"result\":\"Not logged in. Please run /login\"}\nexit /b 1");
        var ai = new CliAgentProvider(CliTools.Claude, exe);
        var e = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("sesión iniciada"));
        Assert.That(e.Message, Does.Contain("Claude Pro o Max"));
    }

    [Test]
    public async Task Codex_ReadsTheLastMessageFile()
    {
        // codex exec --skip-git-repo-check -o <file> -   (the file is the 4th argument)
        string exe = Fake("codex", "more > nul\necho progreso 1>&2\n>\"%~4\" echo respuesta final de codex\necho respuesta final de codex");
        var ai = new CliAgentProvider(CliTools.Codex, exe);
        string answer = await ai.CompleteAsync(Request(), CancellationToken.None);
        Assert.That(answer.Trim(), Is.EqualTo("respuesta final de codex"));
    }

    [Test]
    public async Task Antigravity_ReturnsItsText()
    {
        string exe = Fake("agy", "more > nul\necho idea de antigravity");
        var ai = new CliAgentProvider(CliTools.Google, exe);
        string answer = await ai.CompleteAsync(Request(), CancellationToken.None);
        Assert.That(answer.Trim(), Is.EqualTo("idea de antigravity"));
    }

    [Test]
    public void MissingTool_ExplainsHowToInstallIt()
    {
        var ai = new CliAgentProvider(CliTools.Codex, Path.Combine(_dir, "no-existe.cmd"));
        var e = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("npm install -g @openai/codex"));
    }

    private static async Task WaitForOutput(CliAgentProvider ai)
    {
        for (int i = 0; i < 150 && ai.Activity?.OutputCharacters is not > 0; i++)
            await Task.Delay(20);
        Assert.That(ai.Activity?.OutputCharacters, Is.GreaterThan(0), "salida observable antes de que termine el CLI");
    }

    [Test]
    public async Task Activity_ShowsOutputBeforeTheCliFinishes()
    {
        string exe = Fake("agy", "echo preparando el juego 1>&2\nping -n 3 127.0.0.1 > nul\necho respuesta final");
        var ai = new CliAgentProvider(CliTools.Google, exe);
        var task = ai.CompleteAsync(Request(), CancellationToken.None);
        await WaitForOutput(ai);
        Assert.That(task.IsCompleted, Is.False);
        Assert.That(ai.Activity!.Running, Is.True);
        Assert.That(ai.Activity.Output, Does.Contain("preparando el juego"));
        Assert.That(await task, Does.Contain("respuesta final"));
        Assert.That(ai.Activity!.Running, Is.False);
        Assert.That(ai.Activity.Status, Is.EqualTo("CLI finalizado"));
        Assert.That(ai.Activity.Output, Does.Contain("respuesta final"));
        TimeSpan elapsed = ai.Activity.Elapsed;
        await Task.Delay(30);
        Assert.That(ai.Activity.Elapsed, Is.EqualTo(elapsed), "el reloj se detiene al salir el proceso");
    }

    [Test]
    public async Task Activity_CancelStopsTheProcessAndUpdatesTheStatus()
    {
        string exe = Fake("agy", "echo trabajando 1>&2\nping -n 30 127.0.0.1 > nul\necho no deberia llegar");
        var ai = new CliAgentProvider(CliTools.Google, exe);
        using var cancellation = new CancellationTokenSource();
        var task = ai.CompleteAsync(Request(), cancellation.Token);
        await WaitForOutput(ai);
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await task);
        Assert.That(ai.Activity!.Running, Is.False);
        Assert.That(ai.Activity.Status, Is.EqualTo("CLI cancelado"));
        Assert.That(ai.Activity.Output, Does.Not.Contain("no deberia llegar"));
    }

    [Test]
    public async Task Activity_IsBoundedWithoutTruncatingTheActualAnswer()
    {
        string exe = Fake("agy", "for /l %%i in (1,1,250) do echo 012345678901234567890123456789012345678901234567890123456789\necho ultima linea");
        var ai = new CliAgentProvider(CliTools.Google, exe);
        string answer = await ai.CompleteAsync(Request(), CancellationToken.None);
        Assert.That(answer.Length, Is.GreaterThan(CliActivity.MaxOutputLength));
        Assert.That(ai.Activity!.Output.Length, Is.LessThanOrEqualTo(CliActivity.MaxOutputLength));
        Assert.That(ai.Activity.Output, Does.Contain("ultima linea"));
    }

    [Test]
    public void Settings_CreateTheSubscriptionProviders()
    {
        var settings = new AiSettings { Provider = "sub-claude" };
        Assert.That(settings.CreateProvider(), Is.InstanceOf<CliAgentProvider>());
        settings.Provider = "sub-google";
        Assert.That(settings.CreateProvider()!.Name, Does.Contain("Antigravity"));
    }
}
