using System.Net;
using System.Text;
using System.Text.Json;
using Retro.Ai;

namespace Fusion.Tests;

public sealed class AiBoundaryTests
{
    private static readonly AiRequest Request = new("Own instructions", [new(true, "Own prompt")], null);
    private sealed class Reply(string text) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") });
    }

    [TestCase("in_progress"), TestCase("queued"), TestCase("cancelled"), TestCase("unknown"), TestCase("")]
    public void OpenAiDoesNotPublishTextWithoutCompletedResponse(string status)
    {
        string json = JsonSerializer.Serialize(new { status, output=new[] { new { type="message", role="assistant", status="completed", content=new[] {new {type="output_text",text="partial"}} } } });
        var ai = new OpenAiProvider("test-only", "own-model", new HttpClient(new Reply(json)));
        Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None));
    }

    [TestCase("incomplete", "assistant"), TestCase("in_progress", "assistant"), TestCase("completed", "user")]
    public void OpenAiDoesNotConfusePartialOrNonAssistantMessagesWithAnAnswer(string status, string role)
    {
        string json = JsonSerializer.Serialize(new { status="completed", output=new[] { new { type="message", role, status, content=new[] {new {type="output_text",text="partial"}} } } });
        var ai = new OpenAiProvider("test-only", "own-model", new HttpClient(new Reply(json)));
        Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None));
    }

    [TestCase(""), TestCase("FINISH_REASON_UNSPECIFIED")]
    public void GeminiRequiresAConfirmedStopBeforePublishingText(string finishReason)
    {
        string json = JsonSerializer.Serialize(new { candidates=new[] {new { finishReason, content=new {role="model",parts=new[]{new{text="partial"}}} } } });
        var ai = new GeminiProvider("test-only", "own-model", new HttpClient(new Reply(json)));
        Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None));
    }

    [TestCase("claude"), TestCase("gemini"), Platform("Win")]
    public void InvalidCliJsonIsNeverReturnedAsAnAnswer(string command)
    {
        InOwnFolder(folder =>
        {
            string exe = Script(folder, command, "more > nul\necho {broken");
            var provider = new CliAgentProvider(command == "claude" ? CliTools.Claude : CliTools.Google, exe);
            Assert.ThrowsAsync<AiException>(() => provider.CompleteAsync(Request, CancellationToken.None));
        });
    }

    [TestCase(false), TestCase(true), Platform("Win")]
    public void CodexNeedsBothTheFinalFileAndACompletedTurn(bool partialFile)
    {
        InOwnFolder(folder =>
        {
            string body = "more > nul\n" + (partialFile ? ">\"%~5\" echo partial draft\necho {\"type\":\"turn.failed\",\"error\":{\"message\":\"failed turn\"}}" : "echo {\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"partial draft\"}}");
            var provider = new CliAgentProvider(CliTools.Codex, Script(folder, "codex", body));
            Assert.ThrowsAsync<AiException>(() => provider.CompleteAsync(Request, CancellationToken.None));
        });
    }

    private static string Script(string folder, string name, string body)
    {
        string path = Path.Combine(folder, name + ".cmd");
        File.WriteAllText(path, "@echo off\r\n" + body.Replace("\n", "\r\n") + "\r\n"); return path;
    }
    private static void InOwnFolder(Action<string> test)
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-boundary-" + Guid.NewGuid().ToString("N"))).FullName;
        try { test(folder); } finally { Directory.Delete(folder, true); }
    }
}
