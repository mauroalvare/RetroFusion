using System.Net;
using System.Text;
using System.Text.Json;
using Retro.Ai;
using Retro.Fusion;

namespace Fusion.Tests;

/// <summary>
/// The OpenAI and Gemini providers against a fake HTTP handler: what they send, how they read
/// the answer and how they explain errors. Nothing here reaches a real API (that costs money).
/// </summary>
[TestFixture]
public class AiProviderTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _answer;
        public int Calls;
        public Uri? Url;
        public string Body = "";
        public string? Authorization, GoogleKey;

        public FakeHandler(HttpStatusCode status, string answer)
        {
            _status = status;
            _answer = answer;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Url = request.RequestUri;
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Authorization = request.Headers.Authorization?.ToString();
            GoogleKey = request.Headers.TryGetValues("x-goog-api-key", out var v) ? v.Single() : null;
            return new HttpResponseMessage(_status) { Content = new StringContent(_answer, Encoding.UTF8, "application/json") };
        }
    }

    private static IReadOnlyList<GameProfile> Games() =>
        new[] { ProfileLibrary.BuiltIn.Single(p => p.Id == "invaders"), ProfileLibrary.BuiltIn.Single(p => p.Id == "manicminer") };

    private static AiRequest RecipeRequest() => new(
        "Diseñás FUSIONES",
        new[] { new AiTurn(true, "Fusioná estos juegos"), new AiTurn(false, "{\"name\":\"Mal\"}"), new AiTurn(true, "Corregí la receta") },
        FusionDesigner.Schema(Games().Select(g => g.Id)));

    private static (OpenAiProvider ai, FakeHandler http) OpenAi(HttpStatusCode status, string answer, string key = "sk-prueba")
    {
        var http = new FakeHandler(status, answer);
        return (new OpenAiProvider(key, "gpt-prueba", new HttpClient(http)), http);
    }

    private static (GeminiProvider ai, FakeHandler http) Gemini(HttpStatusCode status, string answer, string key = "clave-prueba", string model = "gemini-prueba")
    {
        var http = new FakeHandler(status, answer);
        return (new GeminiProvider(key, model, new HttpClient(http)), http);
    }

    /// <summary>A Responses API answer whose message carries the given content parts.</summary>
    private static string OpenAiAnswer(string status, params object[] content) => JsonSerializer.Serialize(new
    {
        id = "resp_1",
        @object = "response",
        status,
        output = new object[]
        {
            new { id = "rs_1", type = "reasoning", summary = Array.Empty<object>() },
            new { id = "msg_1", type = "message", role = "assistant", status = "completed", content },
        },
    });

    private static string GeminiAnswer(string finishReason, params object[] parts) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { role = "model", parts }, finishReason } },
    });

    private static IEnumerable<JsonElement> AllObjects(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            yield return e;
            foreach (var p in e.EnumerateObject())
                foreach (var x in AllObjects(p.Value))
                    yield return x;
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
                foreach (var x in AllObjects(item))
                    yield return x;
        }
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(x => x.GetString()!).ToArray();

    // ------------------------------------------------------------------ OpenAI

    [Test]
    public async Task OpenAi_SendsSystemTurnsAndStrictSchema()
    {
        var (ai, http) = OpenAi(HttpStatusCode.OK, OpenAiAnswer("completed", new { type = "output_text", text = "{}" }));
        await ai.CompleteAsync(RecipeRequest(), CancellationToken.None);

        Assert.That(http.Url, Is.EqualTo(new Uri("https://api.openai.com/v1/responses")));
        Assert.That(http.Authorization, Is.EqualTo("Bearer sk-prueba"));
        using var doc = JsonDocument.Parse(http.Body);
        var body = doc.RootElement;
        Assert.That(body.GetProperty("model").GetString(), Is.EqualTo("gpt-prueba"));
        Assert.That(body.GetProperty("instructions").GetString(), Is.EqualTo("Diseñás FUSIONES"));
        Assert.That(body.GetProperty("store").GetBoolean(), Is.False);
        var input = body.GetProperty("input").EnumerateArray().ToList();
        Assert.That(input.Select(m => m.GetProperty("role").GetString()), Is.EqualTo(new[] { "user", "assistant", "user" }));
        Assert.That(input[2].GetProperty("content").GetString(), Is.EqualTo("Corregí la receta"));

        var format = body.GetProperty("text").GetProperty("format");
        Assert.That(format.GetProperty("type").GetString(), Is.EqualTo("json_schema"));
        Assert.That(format.GetProperty("strict").GetBoolean(), Is.True);
        var schema = format.GetProperty("schema");
        // strict mode: every object closed and with all its properties required...
        foreach (var o in AllObjects(schema).Where(o => o.TryGetProperty("properties", out _)))
        {
            Assert.That(o.GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(Strings(o.GetProperty("required")), Is.EquivalentTo(o.GetProperty("properties").EnumerateObject().Select(p => p.Name)));
        }
        // ...the optional ones accept null, the required ones keep their type
        var props = schema.GetProperty("properties");
        Assert.That(Strings(props.GetProperty("id").GetProperty("type")), Is.EqualTo(new[] { "string", "null" }));
        Assert.That(props.GetProperty("name").GetProperty("type").GetString(), Is.EqualTo("string"));
        var rule = props.GetProperty("rules").GetProperty("items").GetProperty("properties");
        Assert.That(Strings(rule.GetProperty("every").GetProperty("type")), Is.EqualTo(new[] { "integer", "null" }));
        Assert.That(rule.GetProperty("do").GetProperty("type").GetString(), Is.EqualTo("array"));
        Assert.That(Strings(props.GetProperty("controls").GetProperty("properties").GetProperty("up").GetProperty("type")), Is.EqualTo(new[] { "array", "null" }));
    }

    [Test]
    public async Task OpenAi_WithoutSchema_SendsNoFormat()
    {
        var (ai, http) = OpenAi(HttpStatusCode.OK, OpenAiAnswer("completed", new { type = "output_text", text = "hola" }));
        string answer = await ai.CompleteAsync(new AiRequest("sistema", new[] { new AiTurn(true, "decí hola") }, null), CancellationToken.None);

        Assert.That(answer, Is.EqualTo("hola"));
        using var doc = JsonDocument.Parse(http.Body);
        Assert.That(doc.RootElement.TryGetProperty("text", out _), Is.False);
    }

    [Test]
    public async Task OpenAi_ReadsTheAnswerAndDropsTheNullsAddedForStrictMode()
    {
        const string recipe = """
            {"id":null,"name":"Cañón minero","description":"Si Willy junta una llave, el cañón dispara solo.","author":null,
             "games":[{"slot":"a","profile":"invaders"},{"slot":"b","profile":"manicminer"}],
             "layout":{"mode":"side","base":"a","opacity":0.5},
             "controls":{"left":["a.left","b.left"],"right":["a.right","b.right"],"up":null,"down":null,"fire":["a.fire"],"jump":["b.jump"],"start":null,"coin":null},
             "rules":[{"when":"b.score increases","every":null,"do":["press a.fire 4"],"message":"¡Llave! Disparo automático","cooldown":30}]}
            """;
        var (ai, _) = OpenAi(HttpStatusCode.OK, OpenAiAnswer("completed", new { type = "output_text", text = recipe, annotations = Array.Empty<object>() }));
        string answer = await ai.CompleteAsync(RecipeRequest(), CancellationToken.None);

        using var doc = JsonDocument.Parse(answer);
        var root = doc.RootElement;
        Assert.That(root.TryGetProperty("id", out _), Is.False);
        Assert.That(root.TryGetProperty("author", out _), Is.False);
        Assert.That(root.GetProperty("controls").TryGetProperty("up", out _), Is.False);
        var rule = root.GetProperty("rules")[0];
        Assert.That(rule.TryGetProperty("every", out _), Is.False);
        Assert.That(rule.GetProperty("when").GetString(), Is.EqualTo("b.score increases"));
        Assert.That(answer, Does.Contain("Cañón minero"));
        // and the engine's validator accepts it as is
        var (parsed, errors) = FusionDesigner.Check(answer, Games(), ProfileLibrary.Find);
        Assert.That(parsed, Is.Not.Null, string.Join("\n", errors));
    }

    [Test]
    public void OpenAi_Refusal_IsAnAiException()
    {
        var (ai, _) = OpenAi(HttpStatusCode.OK, OpenAiAnswer("completed", new { type = "refusal", refusal = "No puedo ayudar con eso." }));
        var e = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(RecipeRequest(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("se negó").And.Contain("No puedo ayudar con eso."));
    }

    [Test]
    public void OpenAi_IncompleteOrEmptyAnswers_AreAiExceptions()
    {
        string cut = JsonSerializer.Serialize(new { status = "incomplete", incomplete_details = new { reason = "max_output_tokens" }, output = Array.Empty<object>() });
        var e = Assert.ThrowsAsync<AiException>(() => OpenAi(HttpStatusCode.OK, cut).ai.CompleteAsync(RecipeRequest(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("se cortó"));

        var empty = OpenAiAnswer("completed", new { type = "output_text", text = "" });
        e = Assert.ThrowsAsync<AiException>(() => OpenAi(HttpStatusCode.OK, empty).ai.CompleteAsync(RecipeRequest(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("vacía"));
    }

    [TestCase(401, "invalid_request_error", "invalid_api_key", "Incorrect API key provided", "clave de API de OpenAI no es válida")]
    [TestCase(429, "requests", "rate_limit_exceeded", "Rate limit reached", "limitando")]
    [TestCase(429, "insufficient_quota", "credit_balance_exhausted", "No credits", "sin crédito")]
    [TestCase(429, "insufficient_quota", "insufficient_quota", "You exceeded your current quota", "sin crédito")]
    [TestCase(500, "server_error", null, "The server had an error", "error temporal")]
    [TestCase(503, "service_unavailable_error", "server_is_overloaded", "Overloaded", "error temporal")]
    [TestCase(404, "invalid_request_error", "model_not_found", "The model `gpt-prueba` does not exist", "rechazó el pedido (404): The model `gpt-prueba` does not exist")]
    public void OpenAi_HttpErrors_AreClearAiExceptions(int status, string type, string? code, string message, string expected)
    {
        string error = JsonSerializer.Serialize(new { error = new { message, type, param = (string?)null, code } });
        var (ai, _) = OpenAi((HttpStatusCode)status, error);
        var e = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(RecipeRequest(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain(expected));
    }

    [Test]
    public void OpenAi_WithoutKey_FailsBeforeCallingTheApi()
    {
        var (ai, http) = OpenAi(HttpStatusCode.OK, "{}", key: "  ");
        var e = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(RecipeRequest(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("OPENAI_API_KEY"));
        Assert.That(http.Calls, Is.EqualTo(0));
    }

    // ------------------------------------------------------------------ Gemini

    [Test]
    public async Task Gemini_SendsSystemTurnsAndASchemaItAccepts()
    {
        var (ai, http) = Gemini(HttpStatusCode.OK, GeminiAnswer("STOP", new { text = "{}" }));
        await ai.CompleteAsync(RecipeRequest(), CancellationToken.None);

        Assert.That(http.Url, Is.EqualTo(new Uri("https://generativelanguage.googleapis.com/v1beta/models/gemini-prueba:generateContent")));
        Assert.That(http.GoogleKey, Is.EqualTo("clave-prueba"));
        Assert.That(http.Url!.Query, Does.Not.Contain("key="), "the key goes in a header, never in the address");
        using var doc = JsonDocument.Parse(http.Body);
        var body = doc.RootElement;
        Assert.That(body.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString(), Is.EqualTo("Diseñás FUSIONES"));
        var contents = body.GetProperty("contents").EnumerateArray().ToList();
        Assert.That(contents.Select(c => c.GetProperty("role").GetString()), Is.EqualTo(new[] { "user", "model", "user" }));
        Assert.That(contents[1].GetProperty("parts")[0].GetProperty("text").GetString(), Is.EqualTo("{\"name\":\"Mal\"}"));

        var config = body.GetProperty("generationConfig");
        Assert.That(config.GetProperty("responseMimeType").GetString(), Is.EqualTo("application/json"));
        var schema = config.GetProperty("responseSchema");
        Assert.That(http.Body, Does.Not.Contain("additionalProperties"));
        Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("OBJECT"));
        Assert.That(Strings(schema.GetProperty("required")), Is.EquivalentTo(new[] { "name", "description", "games", "layout", "controls", "rules" }));
        Assert.That(Strings(schema.GetProperty("propertyOrdering")).Take(3), Is.EqualTo(new[] { "id", "name", "description" }));
        var game = schema.GetProperty("properties").GetProperty("games").GetProperty("items");
        Assert.That(game.GetProperty("type").GetString(), Is.EqualTo("OBJECT"));
        Assert.That(Strings(game.GetProperty("properties").GetProperty("slot").GetProperty("enum")), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(Strings(game.GetProperty("properties").GetProperty("profile").GetProperty("enum")), Is.EqualTo(new[] { "invaders", "manicminer" }));
        Assert.That(schema.GetProperty("properties").GetProperty("rules").GetProperty("items").GetProperty("properties")
            .GetProperty("every").GetProperty("type").GetString(), Is.EqualTo("INTEGER"));
    }

    [Test]
    public async Task Gemini_ReadsTheTextAndSkipsThoughts()
    {
        var (ai, http) = Gemini(HttpStatusCode.OK, GeminiAnswer("STOP",
            new { text = "pensando en voz alta", thought = true }, new { text = "{\"a\":" }, new { text = "1}" }), model: "models/gemini-prueba");
        string answer = await ai.CompleteAsync(new AiRequest("sistema", new[] { new AiTurn(true, "hola") }, null), CancellationToken.None);

        Assert.That(answer, Is.EqualTo("{\"a\":1}"));
        Assert.That(http.Url!.AbsolutePath, Does.EndWith("/v1beta/models/gemini-prueba:generateContent"));
        using var doc = JsonDocument.Parse(http.Body);
        Assert.That(doc.RootElement.GetProperty("generationConfig").TryGetProperty("responseSchema", out _), Is.False);
        Assert.That(ai.Name, Is.EqualTo("Gemini (gemini-prueba)"));
    }

    [Test]
    public void Gemini_BlockedCutOrEmptyAnswers_AreAiExceptions()
    {
        string blocked = JsonSerializer.Serialize(new { promptFeedback = new { blockReason = "SAFETY" } });
        var cases = new (string answer, string expected)[]
        {
            (blocked, "bloqueó el pedido (motivo: SAFETY)"),
            (GeminiAnswer("SAFETY", new { text = "" }), "se negó a responder este pedido (motivo: SAFETY)"),
            (GeminiAnswer("RECITATION", new { text = "{" }), "se negó"),
            (GeminiAnswer("MAX_TOKENS", new { text = "{\"name\":" }), "se cortó"),
            (GeminiAnswer("STOP", new { text = "  " }), "vacía"),
            ("{\"candidates\":[]}", "vacía"),
        };
        foreach (var (answer, expected) in cases)
        {
            var e = Assert.ThrowsAsync<AiException>(() => Gemini(HttpStatusCode.OK, answer).ai.CompleteAsync(RecipeRequest(), CancellationToken.None));
            Assert.That(e!.Message, Does.Contain(expected), answer);
        }
    }

    [TestCase(400, "INVALID_ARGUMENT", "API_KEY_INVALID", "API key not valid. Please pass a valid API key.", "clave de API de Gemini no es válida")]
    [TestCase(402, "FAILED_PRECONDITION", null, "Prepay credits depleted", "sin crédito")]
    [TestCase(403, "PERMISSION_DENIED", null, "Method doesn't allow unregistered callers", "no tiene permiso")]
    [TestCase(429, "RESOURCE_EXHAUSTED", null, "Resource has been exhausted (e.g. check quota).", "limitando")]
    [TestCase(500, "INTERNAL", null, "Internal error", "error temporal")]
    [TestCase(503, "UNAVAILABLE", null, "The model is overloaded.", "error temporal")]
    [TestCase(400, "INVALID_ARGUMENT", null, "Invalid JSON payload received.", "rechazó el pedido (400): Invalid JSON payload received.")]
    public void Gemini_HttpErrors_AreClearAiExceptions(int status, string grpcStatus, string? reason, string message, string expected)
    {
        var details = reason == null
            ? Array.Empty<object>()
            : new object[] { new Dictionary<string, object> { ["@type"] = "type.googleapis.com/google.rpc.ErrorInfo", ["reason"] = reason, ["domain"] = "googleapis.com" } };
        string error = JsonSerializer.Serialize(new { error = new { code = status, message, status = grpcStatus, details } });
        var (ai, _) = Gemini((HttpStatusCode)status, error);
        var e = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(RecipeRequest(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain(expected));
    }

    [Test]
    public void Gemini_WithoutKey_FailsBeforeCallingTheApi()
    {
        var (ai, http) = Gemini(HttpStatusCode.OK, "{}", key: "");
        var e = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(RecipeRequest(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("GEMINI_API_KEY"));
        Assert.That(http.Calls, Is.EqualTo(0));
    }

    [Test]
    public void ErrorPagesThatAreNotJson_StillGiveAMessage()
    {
        var (ai, _) = Gemini(HttpStatusCode.BadGateway, "<html>Bad gateway</html>");
        var e = Assert.ThrowsAsync<AiException>(() => ai.CompleteAsync(RecipeRequest(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("error temporal"));

        var (openAi, _) = OpenAi(HttpStatusCode.BadRequest, "<html>Bad request</html>");
        e = Assert.ThrowsAsync<AiException>(() => openAi.CompleteAsync(RecipeRequest(), CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("<html>Bad request</html>"));
    }

    // ------------------------------------------------------------------ settings

    [Test]
    public void Settings_CreateTheNewProviders()
    {
        Assert.That(new AiSettings { Provider = "openai", OpenAiModel = "gpt-prueba" }.CreateProvider()!.Name, Is.EqualTo("ChatGPT (gpt-prueba)"));
        Assert.That(new AiSettings { Provider = "gemini", GeminiModel = "" }.CreateProvider()!.Name, Is.EqualTo($"Gemini ({GeminiProvider.DefaultModel})"));
        Assert.That(new AiSettings().OpenAiModel, Is.EqualTo(OpenAiProvider.DefaultModel));
    }
}
