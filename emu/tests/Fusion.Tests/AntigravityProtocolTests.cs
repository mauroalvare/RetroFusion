using System.Text.Json;
using Retro.Ai;
using Retro.Core;

namespace Fusion.Tests;

[TestFixture, Platform("Win"), NonParallelizable]
public sealed class AntigravityProtocolTests
{
    private string _folder = "";
    [SetUp] public void SetUp() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-agy-" + Guid.NewGuid().ToString("N"))).FullName;
    [TearDown] public void TearDown() { UiText.Language = "es"; Directory.Delete(_folder, true); }
    private CliAgentProvider Fake(string stdout, string stderr = "", int code = 0, string? model = null, string? effort = null)
    {
        string output = Path.Combine(_folder, "stdout.jsonl"), error = Path.Combine(_folder, "stderr.txt"), exe = Path.Combine(_folder, "agy.cmd");
        File.WriteAllText(output, stdout, new System.Text.UTF8Encoding(false));
        File.WriteAllText(error, stderr, new System.Text.UTF8Encoding(false));
        File.WriteAllText(exe, $"@echo off\r\n>\"{Path.Combine(_folder, "arguments.txt")}\" echo %*\r\nmore > nul\r\ntype \"{output}\"\r\ntype \"{error}\" 1>&2\r\nexit /b {code}\r\n");
        return new(CliTools.Google, exe, model: model, effort: effort);
    }
    private static string Event(object result) => JsonSerializer.Serialize(new { @event="result", result }) + "\n";
    private static readonly AiRequest Request = new("Instrucciones de prueba", [new(true, "Respuesta de prueba")], null);
    private const string Denial = "jetski: no output produced — a tool required the \"command\" permission that headless mode cannot prompt for, so it was auto-denied. Add an allow-rule under permissions.allow in settings.json. Alternatively, re-run with --dangerously-skip-permissions to auto-approve all tools.";

    [Test]
    public async Task FinalStructuredOutputWinsOverProgressAndUsageIsCountedOnlyOnce()
    {
        string stream = "{\"event\":\"init\",\"init\":{\"permission_mode\":\"request-review\"}}\n"
            + "{\"event\":\"step_update\",\"step_update\":{\"text_delta\":\"incomplete\",\"usage\":{\"input_tokens\":99999,\"output_tokens\":88888}}}\n"
            + Event(new { status="SUCCESS", response="fallback", structured_output=new { title="Own game" },
                usage=new { input_tokens=120, output_tokens=30, thinking_tokens=25, cache_read_tokens=90, total_tokens=150 } });
        var ai = Fake(stream);
        string answer = await ai.CompleteAsync(Request, CancellationToken.None);
        using var json = JsonDocument.Parse(answer);
        Assert.That(json.RootElement.GetProperty("title").GetString(), Is.EqualTo("Own game"));
        Assert.That(answer, Does.Not.Contain("incomplete").And.Not.Contain("fallback"));
        Assert.That(ai.LastUsage, Is.EqualTo(new AiUsage(120, 30, 90)), "The terminal counters include thinking and cache; step counters are not additive.");
    }

    [TestCase("{\"event\":\"step_update\",\"step_update\":{\"text_delta\":\"partial\"}}\n")]
    [TestCase("{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":\"truncated")]
    [TestCase("{\"title\":\"looks valid but is not a terminal result\"}")]
    [TestCase("{\"event\":\"result\",\"result\":42}")]
    public void MissingOrMalformedTerminalResultNeverBecomesASuccess(string stream)
    {
        var ai = Fake(stream);
        var error = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("no confirmó una respuesta completa"));
        Assert.That(ai.LastUsage, Is.Null);
    }

    [TestCase(0, false, "es"), TestCase(2, false, "en"), TestCase(0, true, "es"), TestCase(0, true, "en")]
    public void PermissionErrorsHaveAnActionableLocalizedMessageWithoutRecommendingBlanketApproval(int code, bool inResult, string language)
    {
        UiText.Language = language;
        var ai = Fake(inResult ? Event(new { status="ERROR", error=Denial, response="" }) : "", inResult ? "" : Denial, code);
        var error = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("/permissions").And.Not.Contain("--dangerously-skip-permissions").And.Not.Contain("settings.json"));
        Assert.That(error.Message, Does.Contain(language == "en" ? "blocked a tool" : "bloqueó una herramienta"));
        Assert.That(ai.Activity!.Running, Is.False);
    }

    [Test]
    public async Task ARecoveredBenignToolDenialDoesNotDiscardTheConfirmedFinalAnswer()
    {
        Assert.That(await Fake(Event(new { status="SUCCESS", response="final answer" }), Denial).CompleteAsync(Request, CancellationToken.None), Is.EqualTo("final answer"));
    }

    [Test]
    public async Task ImagesKeepTheirReadWorkspaceAndModelAndEffortWithoutGrantingCommandPermissions()
    {
        string images = Directory.CreateDirectory(Path.Combine(_folder, "own images")).FullName;
        var ai = Fake(Event(new { status="SUCCESS", response="image answer" }), model: "own-model", effort: "high");
        var request = Request with { Images = [Path.Combine(images, "one.png"), Path.Combine(images, "two.png")] };
        Assert.That(await ai.CompleteAsync(request, CancellationToken.None), Is.EqualTo("image answer"));
        string args = File.ReadAllText(Path.Combine(_folder, "arguments.txt"));
        Assert.That(args, Does.Contain("own-model").And.Contain("high").And.Contain("plan").And.Contain("own images"));
        Assert.That(System.Text.RegularExpressions.Regex.Matches(args, "--add-dir").Count, Is.EqualTo(1));
        Assert.That(args, Does.Not.Contain("--dangerously-skip-permissions").And.Not.Contain("permissions.allow"));
        Directory.Delete(images);
    }

    [Test]
    public void NonzeroExitAndDuplicateResultsDoNotPublishAnAnswer()
    {
        string success = Event(new { status="SUCCESS", response="must not apply" });
        Assert.ThrowsAsync<AiException>(() => Fake(success, "process failure", 17).CompleteAsync(Request, CancellationToken.None));
        var duplicate = Assert.ThrowsAsync<AiException>(() => Fake(success + success).CompleteAsync(Request, CancellationToken.None));
        Assert.That(duplicate!.Message, Does.Contain("no confirmó"));
    }

    [TestCase("es"), TestCase("en")]
    public void AnOlderCliIsReportedWithoutAutomaticallyRetryingOrInstalling(string language)
    {
        UiText.Language = language;
        var ai = Fake("", "flag provided but not defined: -input-format", 2);
        var error = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("stream-json").And.Contain("Google"));
        Assert.That(error.Message, Does.Contain(language == "en" ? "did not download" : "no descargó"));
    }
}
