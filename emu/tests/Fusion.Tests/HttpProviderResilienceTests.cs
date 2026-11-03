using System.Net;
using System.Text;
using System.Text.Json;
using Retro.Ai;

namespace Fusion.Tests;

public sealed class HttpProviderResilienceTests
{
    private static readonly AiRequest Request = new("Own instructions ñ😀", [new(true, "Own prompt\nwith lines áéíóú 😀")], null);
    private sealed class Handler(Func<CancellationToken, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls;
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Body = await request.Content!.ReadAsStringAsync(ct);
            ct.ThrowIfCancellationRequested();
            return reply(ct);
        }
    }
    private static IAiProvider Provider(string name, HttpClient http) => name switch
    {
        "claude" => new ClaudeProvider("test-only", "own-model", http),
        "openai" => new OpenAiProvider("test-only", "own-model", http),
        "gemini" => new GeminiProvider("test-only", "own-model", http),
        _ => new OllamaProvider("http://own.invalid", "own-model", http)
    };
    private static HttpResponseMessage Json(string body, int status = 200) => new((HttpStatusCode)status)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static string Success(string provider, string text = "own final answer") => provider switch
    {
        "claude" => JsonSerializer.Serialize(new { id="msg_own", type="message", role="assistant", model="own-model", content=new[] {new {type="text",text}},
            stop_reason="end_turn", stop_sequence=(string?)null, usage=new { input_tokens=30, output_tokens=30, cache_read_input_tokens=90, cache_creation_input_tokens=0 } }),
        "openai" => JsonSerializer.Serialize(new { status="completed", output=new[] {new {type="message",role="assistant",status="completed",content=new[]{new{type="output_text",text}}}},
            usage=new{input_tokens=120,output_tokens=30,input_tokens_details=new{cached_tokens=90}} }),
        "gemini" => JsonSerializer.Serialize(new { candidates=new[] {new {finishReason="STOP",content=new{role="model",parts=new[]{new{text}}}}},
            usageMetadata=new{promptTokenCount=120,candidatesTokenCount=20,thoughtsTokenCount=10,cachedContentTokenCount=90} }),
        _ => JsonSerializer.Serialize(new { done=true,done_reason="stop",message=new{role="assistant",content=text},prompt_eval_count=120,eval_count=30,prompt_eval_cached_count=90 })
    };

    [TestCase("claude"), TestCase("openai"), TestCase("gemini"), TestCase("ollama")]
    public async Task EveryHttpProviderSendsTheWholeRequestAndReturnsOnlyAConfirmedAnswerAndReportedUsage(string name)
    {
        var handler = new Handler(_ => Json(Success(name)));
        using var http = new HttpClient(handler);
        var ai = Provider(name, http);
        Assert.That(await ai.CompleteAsync(Request, CancellationToken.None), Is.EqualTo("own final answer"));
        Assert.That(ai.LastUsage, Is.EqualTo(new AiUsage(120, 30, 90)));
        Assert.That(handler.Calls, Is.EqualTo(1));
        using var body = JsonDocument.Parse(handler.Body);
        var strings = Strings(body.RootElement).ToArray();
        Assert.That(strings, Does.Contain(Request.System).And.Contain(Request.Turns[0].Text));
        Assert.That(handler.Body, Does.Not.Contain("test-only"), "The test credential belongs in a header, not in the prompt body.");
    }

    [TestCase("claude"), TestCase("openai"), TestCase("gemini"), TestCase("ollama")]
    public void MalformedWrongShapeAndEmptyRepliesFailWithoutRawParserExceptions(string name)
    {
        foreach (string bad in new[] { "", "{broken", "null", "[]", "{}", Success(name, " \r\n ") })
        {
            var handler = new Handler(_ => Json(bad)); using var http = new HttpClient(handler);
            var ai = Provider(name, http);
            Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None), name + ": " + bad);
            Assert.That(handler.Calls, Is.EqualTo(1));
        }
    }

    [TestCase("claude",401), TestCase("claude",429), TestCase("claude",503)]
    [TestCase("openai",401), TestCase("openai",429), TestCase("openai",503)]
    [TestCase("gemini",401), TestCase("gemini",429), TestCase("gemini",503)]
    [TestCase("ollama",401), TestCase("ollama",429), TestCase("ollama",503)]
    [TestCase("claude",400), TestCase("claude",404), TestCase("openai",400), TestCase("openai",404)]
    [TestCase("gemini",400), TestCase("gemini",404), TestCase("ollama",400), TestCase("ollama",404)]
    public void FailedHttpStatusDoesNotBecomeAnAnswerOrInventUsage(string name, int status)
    {
        var handler = new Handler(_ => Json("{\"type\":\"error\",\"error\":{\"type\":\"own_error\",\"message\":\"own failure\"}}", status));
        using var http = new HttpClient(handler); var ai = Provider(name, http);
        Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None));
        Assert.That(ai.LastUsage, Is.Null); Assert.That(handler.Calls, Is.EqualTo(1));
    }

    [TestCase("claude", "max_tokens"), TestCase("claude", "refusal"), TestCase("claude", "pause_turn"), TestCase("claude", "tool_use")]
    [TestCase("ollama", "length"), TestCase("ollama", "load"), TestCase("ollama", "unknown")]
    public void StopsThatNeedAnotherActionDoNotPublishThePartialText(string name, string stop)
    {
        string body = Success(name).Replace(name == "claude" ? "end_turn" : "\"stop\"", name == "claude" ? stop : '"' + stop + '"');
        var handler = new Handler(_ => Json(body)); using var http = new HttpClient(handler);
        var ai = Provider(name, http);
        Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None));
        Assert.That(ai.LastUsage, Is.EqualTo(new AiUsage(120, 30, 90)), "A rejected response can still report real usage.");
    }

    [TestCase("claude"), TestCase("openai"), TestCase("gemini"), TestCase("ollama")]
    public void PreCancellationNeverDispatches(string name)
    {
        var handler = new Handler(_ => throw new AssertionException("Must not send")); using var http = new HttpClient(handler);
        var ai = Provider(name, http); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.CatchAsync<OperationCanceledException>(() => ai.CompleteAsync(Request, cancel.Token));
        Assert.That(handler.Calls, Is.Zero);
    }

    [TestCase("claude"), TestCase("openai"), TestCase("gemini"), TestCase("ollama")]
    public void InvalidSchemasAndUnsupportedImagesAreRejectedBeforeDispatch(string name)
    {
        var handler = new Handler(_=>throw new AssertionException("Must not dispatch invalid input"));
        using var http=new HttpClient(handler);var ai=Provider(name,http);
        using var schema=JsonDocument.Parse("[]");
        Assert.ThrowsAsync<AiException>(()=>ai.CompleteAsync(Request with {Schema=schema.RootElement},CancellationToken.None));
        Assert.ThrowsAsync<AiException>(()=>ai.CompleteAsync(Request with {Images=["own.png"]},CancellationToken.None));
        Assert.That(handler.Calls,Is.Zero);
    }

    [TestCase("claude",true), TestCase("openai",true), TestCase("gemini",true), TestCase("ollama",true)]
    [TestCase("claude",false), TestCase("openai",false), TestCase("gemini",false), TestCase("ollama",false)]
    public async Task DeadlineAndCancellationCoverReadingTheBodyAfterHeaders(string name, bool deadline)
    {
        var stream = new StalledBody();
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        using var http = new HttpClient(handler) { Timeout = deadline ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(5) };
        using var cancel = new CancellationTokenSource(); var ai = Provider(name, http);
        var pending = ai.CompleteAsync(Request, cancel.Token);
        try
        {
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (!deadline) cancel.Cancel();
            if (deadline) Assert.ThrowsAsync<AiException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));
            else Assert.CatchAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { cancel.Cancel(); try { await pending; } catch (Exception) { } }
        Assert.That(handler.Calls, Is.EqualTo(1)); Assert.That(stream.Closed, Is.True);
    }

    [TestCase("claude"), TestCase("openai"), TestCase("gemini"), TestCase("ollama")]
    public void OversizedDeclaredBodyIsRejectedAndDisposedBeforeReading(string name)
    {
        var stream = new StalledBody();
        var handler = new Handler(_ =>
        {
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            result.Content.Headers.ContentLength = 17 * 1024 * 1024; return result;
        });
        using var http = new HttpClient(handler); var ai = Provider(name, http);
        Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None));
        Assert.That(stream.Entered.Task.IsCompleted, Is.False); Assert.That(stream.Closed, Is.True);
    }

    [TestCase("claude"), TestCase("openai"), TestCase("gemini"), TestCase("ollama")]
    public async Task AFaultAfterASuccessNeverReusesThePreviousTokenCounts(string name)
    {
        int reply = 0; var handler = new Handler(_ => Json(reply++ == 0 ? Success(name) : "{broken"));
        using var http = new HttpClient(handler); var ai = Provider(name, http);
        await ai.CompleteAsync(Request, CancellationToken.None); Assert.That(ai.LastUsage, Is.Not.Null);
        Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request, CancellationToken.None));
        Assert.That(ai.LastUsage, Is.Null); Assert.That(handler.Calls, Is.EqualTo(2));
    }

    [TestCase("claude","broken"),TestCase("openai","broken"),TestCase("gemini","broken"),TestCase("ollama","broken")]
    [TestCase("claude","invalid"),TestCase("openai","invalid"),TestCase("gemini","invalid"),TestCase("ollama","invalid")]
    [TestCase("claude","large"),TestCase("openai","large"),TestCase("gemini","large"),TestCase("ollama","large")]
    public void BrokenInvalidAndUnboundedStreamingBodiesNeverPublishPartialData(string name, string mode)
    {
        var stream = new StalledBody(mode);
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        using var http = new HttpClient(handler); var ai = Provider(name,http);
        Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(Request,CancellationToken.None));
        Assert.That(stream.Closed,Is.True); Assert.That(ai.LastUsage,Is.Null); Assert.That(handler.Calls,Is.EqualTo(1));
        if(mode=="large") Assert.That(stream.BytesRead,Is.LessThan(17*1024*1024),"Unknown Content-Length must still be bounded during the read.");
    }

    [TestCase("claude"), TestCase("openai"), TestCase("gemini"), TestCase("ollama")]
    public void NewInvalidResponseDiagnosticsRespectTheSelectedEnglishLanguage(string name)
    {
        Retro.Core.UiText.Language="en";
        try
        {
            var handler = new Handler(_=>Json("{broken")); using var http=new HttpClient(handler);
            var error=Assert.ThrowsAsync<AiException>(()=>Provider(name,http).CompleteAsync(Request,CancellationToken.None));
            Assert.That(error!.Message,Does.Not.Contain("devolvió").And.Not.Contain("respuesta"));
        }
        finally { Retro.Core.UiText.Language="es"; }
    }

    [TestCase("claude"),TestCase("openai"),TestCase("gemini")]
    public void MalformedDummyCredentialsFailBeforeDispatchAndNeverAppearInTheDiagnostic(string name)
    {
        const string dummy="own-key\nsecond-line";
        var handler=new Handler(_=>throw new AssertionException("Must not dispatch"));using var http=new HttpClient(handler);
        IAiProvider ai=name=="claude"?new ClaudeProvider(dummy,"own-model",http):name=="openai"?new OpenAiProvider(dummy,"own-model",http):new GeminiProvider(dummy,"own-model",http);
        var error=Assert.ThrowsAsync<AiException>(()=>ai.CompleteAsync(Request,CancellationToken.None));
        Assert.That(error!.Message,Does.Contain("caracteres de control").And.Not.Contain("own-key"));Assert.That(handler.Calls,Is.Zero);
    }

    [TestCase("localhost:11434"),TestCase("file:///own"),TestCase("not a server")]
    public void AnInvalidOllamaAddressHasAnActionableErrorBeforeDispatch(string address)
    {
        var handler=new Handler(_=>throw new AssertionException("Must not send"));using var http=new HttpClient(handler);
        var error=Assert.ThrowsAsync<AiException>(()=>new OllamaProvider(address,"own-model",http).CompleteAsync(Request,CancellationToken.None));
        Assert.That(error!.Message,Does.Contain("http://"));Assert.That(handler.Calls,Is.Zero);
    }

    [TestCase("claude"),TestCase("openai"),TestCase("gemini"),TestCase("ollama")]
    public void AConnectionFailureIsAnAiErrorWithoutInventedUsage(string name)
    {
        var handler=new Handler(_=>throw new HttpRequestException("Own connection refused"));using var http=new HttpClient(handler);
        var ai=Provider(name,http);
        Assert.ThrowsAsync<AiException>(()=>ai.CompleteAsync(Request,CancellationToken.None));
        Assert.That(ai.LastUsage,Is.Null);Assert.That(handler.Calls,Is.EqualTo(1));
    }

    private static IEnumerable<string> Strings(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String) yield return item.GetString()!;
        else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) foreach (string text in Strings(child)) yield return text;
        else if (item.ValueKind == JsonValueKind.Object) foreach (var child in item.EnumerateObject()) foreach (string text in Strings(child.Value)) yield return text;
    }
    private sealed class StalledBody(string mode="stall") : Stream
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Closed;
        public int BytesRead;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Entered.TrySetResult(); ct.ThrowIfCancellationRequested();
            if(mode=="broken") throw new IOException("Own interrupted response");
            if(mode=="invalid") { if(BytesRead>0)return 0; buffer.Span[0]=0xff; BytesRead++; return 1; }
            if(mode=="large") { int count=Math.Min(buffer.Length,17*1024*1024-BytesRead);buffer.Span[..count].Fill((byte)'x');BytesRead+=count;return count; }
            await Task.Delay(Timeout.Infinite, ct); return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset,count), ct).AsTask();
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer,int offset,int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset,SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer,int offset,int count) => throw new NotSupportedException();
    }
}
