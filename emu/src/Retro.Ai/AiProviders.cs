using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace Retro.Ai;

/// <summary>One turn of a conversation with an AI.</summary>
public sealed record AiTurn(bool FromUser, string Text);

/// <summary>A request: instructions, the conversation so far, the JSON schema of the answer and pictures (PNG files) to look at.</summary>
public sealed record AiRequest(string System, IReadOnlyList<AiTurn> Turns, JsonElement? Schema, IReadOnlyList<string>? Images = null);

public sealed class AiException : Exception
{
    public AiException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Something that can answer a request: Claude, a local model, a person pasting from a chat...</summary>
public interface IAiProvider
{
    string Name { get; }
    Task<string> CompleteAsync(AiRequest request, CancellationToken ct);
}

/// <summary>
/// Claude through the official Anthropic SDK. The answer is constrained to the recipe's
/// JSON schema (structured outputs). Refused requests are re-served by a fallback model
/// chosen by the API (server-side fallbacks).
/// </summary>
public sealed class ClaudeProvider : IAiProvider
{
    public const string DefaultModel = "claude-opus-5-5";
    private readonly AnthropicClient _client;
    private readonly string _model;

    public ClaudeProvider(string? apiKey, string? model = null)
    {
        // With no key the SDK uses ANTHROPIC_API_KEY or the profile from `ant auth login`.
        _client = string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey };
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
    }

    public string Name => $"Claude ({_model})";

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        var messages = request.Turns
            .Select(t => new BetaMessageParam { Role = t.FromUser ? Role.User : Role.Assistant, Content = t.Text })
            .ToList();
        var output = request.Schema is JsonElement schema
            ? new BetaOutputConfig
            {
                Effort = Effort.Medium,
                Format = new BetaJsonOutputFormat
                {
                    Schema = schema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
                },
            }
            : new BetaOutputConfig { Effort = Effort.Medium };
        BetaMessage response;
        try
        {
            response = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 16000,
                Betas = ["server-side-fallback-2026-06-01"],
                Fallbacks = new List<BetaFallbackParam> { new(Anthropic.Models.Messages.Model.ClaudeOpus4_8) },
                OutputConfig = output,
                System = request.System,
                Messages = messages,
            }, ct);
        }
        catch (AnthropicUnauthorizedException e)
        {
            throw new AiException("La clave de API de Anthropic no es válida.", e);
        }
        catch (AnthropicRateLimitException e)
        {
            throw new AiException("Claude está limitando los pedidos (demasiados seguidos). Probá en un minuto.", e);
        }
        catch (Anthropic5xxException e)
        {
            throw new AiException("El servicio de Claude tuvo un error temporal. Probá de nuevo.", e);
        }
        catch (AnthropicApiException e)
        {
            throw new AiException("Claude rechazó el pedido: " + e.Message, e);
        }
        if (response.StopReason == "refusal")
            throw new AiException("Claude se negó a responder este pedido.");
        if (response.StopReason == "max_tokens")
            throw new AiException("La respuesta de Claude se cortó por longitud.");
        var text = new StringBuilder();
        foreach (var block in response.Content)
            if (block.TryPickText(out BetaTextBlock? t))
                text.Append(t.Text);
        return text.ToString();
    }
}

/// <summary>A model running locally with Ollama (http://localhost:11434). No cost per use.</summary>
public sealed class OllamaProvider : IAiProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly string _url, _model;

    public OllamaProvider(string? url, string? model)
    {
        _url = string.IsNullOrWhiteSpace(url) ? "http://localhost:11434" : url.TrimEnd('/');
        _model = string.IsNullOrWhiteSpace(model) ? "llama3.1" : model;
    }

    public string Name => $"Ollama ({_model})";

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        var messages = new List<object> { new { role = "system", content = request.System } };
        messages.AddRange(request.Turns.Select(t => new { role = t.FromUser ? "user" : "assistant", content = t.Text }));
        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["stream"] = false,
        };
        if (request.Schema is JsonElement schema)
            body["format"] = schema;
        HttpResponseMessage resp;
        try
        {
            resp = await Http.PostAsJsonAsync(_url + "/api/chat", body, ct);
        }
        catch (HttpRequestException e)
        {
            throw new AiException($"No pude conectar con Ollama en {_url}. ¿Está abierto? (ollama serve)", e);
        }
        string json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new AiException($"Ollama respondió {(int)resp.StatusCode}: {json}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}

/// <summary>
/// ChatGPT models through the OpenAI Responses API, with plain HttpClient. The answer is
/// constrained to the schema with Structured Outputs in strict mode. Strict mode wants every
/// property listed as required, so optional properties are sent as nullable and the nulls
/// the model writes for them are removed from the answer.
/// </summary>
public sealed class OpenAiProvider : IAiProvider
{
    public const string DefaultModel = "gpt-6.1-sol";
    public const string Endpoint = "https://api.openai.com/v1/responses";
    private readonly HttpClient _http;
    private readonly string _key, _model;

    public OpenAiProvider(string? apiKey, string? model = null, HttpClient? http = null)
    {
        _key = apiKey?.Trim() ?? "";
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _http = http ?? JsonApi.Http;
    }

    public string Name => $"ChatGPT ({_model})";

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        if (_key.Length == 0)
            throw new AiException("Falta la clave de API de OpenAI: escribila en Fusión de juegos o definí la variable OPENAI_API_KEY.");
        var input = new JsonArray();
        foreach (var t in request.Turns)
            input.Add(new JsonObject { ["role"] = t.FromUser ? "user" : "assistant", ["content"] = t.Text });
        var body = new JsonObject
        {
            ["model"] = _model,
            ["instructions"] = request.System,
            ["input"] = input,
            ["max_output_tokens"] = JsonApi.MaxOutputTokens,
            ["store"] = false,
        };
        JsonObject? schema = request.Schema is JsonElement s ? JsonApi.ToObject(s) : null;
        if (schema != null)
        {
            var strict = (JsonObject)schema.DeepClone();
            MakeStrict(strict);
            body["text"] = new JsonObject
            {
                ["format"] = new JsonObject { ["type"] = "json_schema", ["name"] = "respuesta", ["strict"] = true, ["schema"] = strict },
            };
        }
        using var msg = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonApi.Content(body) };
        msg.Headers.Authorization = new("Bearer", _key);
        var (status, json) = await JsonApi.SendAsync(_http, msg, "OpenAI", ct);
        if ((int)status is < 200 or > 299)
            throw Error(status, json);

        using var doc = JsonApi.Parse(json, "OpenAI");
        var root = doc.RootElement;
        switch (JsonApi.Str(root, "status"))
        {
            case "failed":
                throw new AiException("OpenAI no pudo completar el pedido: " +
                                      (JsonApi.Has(root, "error", out var err) ? JsonApi.Str(err, "message") : "sin detalle"));
            case "incomplete":
                string reason = JsonApi.Has(root, "incomplete_details", out var d) ? JsonApi.Str(d, "reason") : "";
                throw new AiException(reason == "content_filter"
                    ? "OpenAI cortó la respuesta con su filtro de contenido."
                    : "La respuesta de ChatGPT se cortó por longitud.");
        }
        var text = new StringBuilder();
        if (JsonApi.Has(root, "output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                // reasoning items and intermediate commentary are not the answer
                if (JsonApi.Str(item, "type") != "message" || JsonApi.Str(item, "phase") == "commentary" ||
                    !JsonApi.Has(item, "content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (JsonApi.Str(part, "type") == "refusal")
                        throw new AiException("ChatGPT se negó a responder este pedido: " + JsonApi.Str(part, "refusal"));
                    if (JsonApi.Str(part, "type") == "output_text")
                        text.Append(JsonApi.Str(part, "text"));
                }
            }
        }
        if (string.IsNullOrWhiteSpace(text.ToString()))
            throw new AiException("ChatGPT devolvió una respuesta vacía.");
        return schema != null ? RemoveAddedNulls(text.ToString(), schema) : text.ToString();
    }

    private static AiException Error(HttpStatusCode status, string body)
    {
        var (message, codes) = JsonApi.ErrorOf(body);
        bool billing = codes.Contains("quota") || codes.Contains("credit") || codes.Contains("spend_limit") || codes.Contains("usage_limit");
        return (int)status switch
        {
            401 => new AiException("La clave de API de OpenAI no es válida."),
            429 when billing => new AiException("Tu cuenta de OpenAI se quedó sin crédito o llegó a su límite de gasto. Revisalo en platform.openai.com (la API se paga aparte de ChatGPT Plus)."),
            429 => new AiException("OpenAI está limitando los pedidos (demasiados seguidos). Probá en un minuto."),
            >= 500 => new AiException("El servicio de OpenAI tuvo un error temporal. Probá de nuevo."),
            _ => new AiException($"OpenAI rechazó el pedido ({(int)status}): {message}"),
        };
    }

    /// <summary>Strict mode: every object closed and with all its properties required; the optional ones accept null.</summary>
    private static void MakeStrict(JsonObject s)
    {
        if (s["properties"] is JsonObject props)
        {
            var required = JsonApi.Names(s["required"]);
            foreach (var (name, sub) in props)
            {
                if (sub is not JsonObject p)
                    continue;
                MakeStrict(p);
                if (!required.Contains(name))
                    AllowNull(p);
            }
            s["required"] = new JsonArray(props.Select(p => (JsonNode?)p.Key).ToArray());
            s["additionalProperties"] = false;
        }
        else if (s["type"]?.GetValueKind() == JsonValueKind.String && (string?)s["type"] == "object")
        {
            s["required"] = new JsonArray();
            s["additionalProperties"] = false;
        }
        if (s["items"] is JsonObject items)
            MakeStrict(items);
        if (s["anyOf"] is JsonArray anyOf)
            foreach (var x in anyOf.OfType<JsonObject>())
                MakeStrict(x);
        if (s["$defs"] is JsonObject defs)
            foreach (var (_, x) in defs)
                if (x is JsonObject o)
                    MakeStrict(o);
    }

    private static void AllowNull(JsonObject p)
    {
        switch (p["type"])
        {
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                p["type"] = new JsonArray((string?)v, "null");
                break;
            case JsonArray a:
                if (!JsonApi.Names(a).Contains("null"))
                    a.Add(JsonValue.Create("null"));
                break;
            default:   // anyOf, $ref...: one more alternative
                var copy = p.DeepClone();
                p.Clear();
                p["anyOf"] = new JsonArray(copy, new JsonObject { ["type"] = "null" });
                return;
        }
        if (p["enum"] is JsonArray e && !e.Any(x => x is null))
            e.Add(null);
    }

    /// <summary>Takes out the nulls written for optional properties, as if the model had left them out.</summary>
    private static string RemoveAddedNulls(string answer, JsonObject schema)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(answer);
        }
        catch (JsonException)
        {
            return answer;   // the recipe validator explains what is wrong
        }
        Strip(node, schema);
        return node?.ToJsonString(JsonApi.Readable) ?? answer;

        static void Strip(JsonNode? value, JsonObject? schema)
        {
            if (schema == null)
                return;
            if (value is JsonObject obj && schema["properties"] is JsonObject props)
            {
                var required = JsonApi.Names(schema["required"]);
                foreach (var (name, v) in obj.ToList())
                {
                    if (v is null && !required.Contains(name))
                        obj.Remove(name);
                    else
                        Strip(v, props[name] as JsonObject);
                }
            }
            else if (value is JsonArray arr && schema["items"] is JsonObject items)
            {
                foreach (var x in arr)
                    Strip(x, items);
            }
        }
    }
}

/// <summary>
/// Gemini models through the Google Gemini API (generateContent), with plain HttpClient.
/// The answer is constrained with responseMimeType application/json plus responseSchema,
/// which takes an OpenAPI subset of JSON Schema: keywords outside it (additionalProperties,
/// $ref...) are left out, and type names go in upper case.
/// </summary>
public sealed class GeminiProvider : IAiProvider
{
    public const string DefaultModel = "gemini-3.8-flash";
    public const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
    private static readonly HashSet<string> SchemaKeywords = new()
    {
        "type", "format", "title", "description", "nullable", "enum", "maxItems", "minItems", "properties", "required",
        "minProperties", "maxProperties", "minLength", "maxLength", "pattern", "example", "anyOf", "propertyOrdering",
        "default", "items", "minimum", "maximum",
    };
    private readonly HttpClient _http;
    private readonly string _key, _model;

    public GeminiProvider(string? apiKey, string? model = null, HttpClient? http = null)
    {
        _key = apiKey?.Trim() ?? "";
        string m = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _model = m.StartsWith("models/") ? m["models/".Length..] : m;
        _http = http ?? JsonApi.Http;
    }

    public string Name => $"Gemini ({_model})";

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        if (_key.Length == 0)
            throw new AiException("Falta la clave de API de Gemini: escribila en Fusión de juegos o definí la variable GEMINI_API_KEY.");
        var contents = new JsonArray();
        foreach (var t in request.Turns)
            contents.Add(new JsonObject { ["role"] = t.FromUser ? "user" : "model", ["parts"] = new JsonArray(new JsonObject { ["text"] = t.Text }) });
        var config = new JsonObject { ["maxOutputTokens"] = JsonApi.MaxOutputTokens };
        if (request.Schema is JsonElement s)
        {
            config["responseMimeType"] = "application/json";
            config["responseSchema"] = ToGeminiSchema(JsonApi.ToObject(s));
        }
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.System }) },
            ["contents"] = contents,
            ["generationConfig"] = config,
        };
        // the key goes in a header, not in the address, so it never shows up in logs
        using var msg = new HttpRequestMessage(HttpMethod.Post, BaseUrl + Uri.EscapeDataString(_model) + ":generateContent")
        {
            Content = JsonApi.Content(body),
        };
        msg.Headers.Add("x-goog-api-key", _key);
        var (status, json) = await JsonApi.SendAsync(_http, msg, "Gemini", ct);
        if ((int)status is < 200 or > 299)
            throw Error(status, json);

        using var doc = JsonApi.Parse(json, "Gemini");
        var root = doc.RootElement;
        if (JsonApi.Has(root, "promptFeedback", out var feedback) && JsonApi.Str(feedback, "blockReason") is { Length: > 0 } block)
            throw new AiException($"Gemini bloqueó el pedido (motivo: {block}).");
        if (!JsonApi.Has(root, "candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            throw new AiException("Gemini devolvió una respuesta vacía.");
        var candidate = candidates[0];
        string finish = JsonApi.Str(candidate, "finishReason");
        if (finish is "MAX_TOKENS" or "CONTINUATION")
            throw new AiException("La respuesta de Gemini se cortó por longitud.");
        if (finish is not ("" or "STOP" or "FINISH_REASON_UNSPECIFIED"))
            throw new AiException($"Gemini se negó a responder este pedido (motivo: {finish}).");
        var text = new StringBuilder();
        if (JsonApi.Has(candidate, "content", out var content) && JsonApi.Has(content, "parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
                if (!(JsonApi.Has(part, "thought", out var thought) && thought.ValueKind == JsonValueKind.True))
                    text.Append(JsonApi.Str(part, "text"));
        }
        if (string.IsNullOrWhiteSpace(text.ToString()))
            throw new AiException("Gemini devolvió una respuesta vacía.");
        return text.ToString();
    }

    private static AiException Error(HttpStatusCode status, string body)
    {
        var (message, codes) = JsonApi.ErrorOf(body);
        if (status == HttpStatusCode.Unauthorized || codes.Contains("api_key"))
            return new AiException("La clave de API de Gemini no es válida.");
        return (int)status switch
        {
            402 => new AiException("Tu cuenta de Gemini se quedó sin crédito prepago. Cargá saldo en Google AI Studio."),
            403 => new AiException("Tu clave de Gemini no tiene permiso para este pedido: " + message),
            429 => new AiException("Gemini está limitando los pedidos o se agotó tu cuota (la capa gratuita tiene límites por minuto y por día). Probá en un rato."),
            >= 500 => new AiException("El servicio de Gemini tuvo un error temporal. Probá de nuevo."),
            _ => new AiException($"Gemini rechazó el pedido ({(int)status}): {message}"),
        };
    }

    /// <summary>Copies the schema keeping only what responseSchema accepts.</summary>
    private static JsonObject ToGeminiSchema(JsonObject s)
    {
        var r = new JsonObject();
        foreach (var (key, v) in s)
        {
            if (v is null || !SchemaKeywords.Contains(key))
                continue;
            switch (key)
            {
                case "type" when v is JsonArray types:   // ["string", "null"] -> STRING + nullable
                    var names = JsonApi.Names(types);
                    if (names.Remove("null"))
                        r["nullable"] = true;
                    if (names.Count > 0)
                        r["type"] = names.First().ToUpperInvariant();
                    break;
                case "type" when v.GetValueKind() == JsonValueKind.String:
                    r["type"] = ((string)v!).ToUpperInvariant();
                    break;
                case "properties" when v is JsonObject props:
                    var copy = new JsonObject();
                    foreach (var (name, p) in props)
                        if (p is JsonObject po)
                            copy[name] = ToGeminiSchema(po);
                    r["properties"] = copy;
                    // keep the schema's order (name before description...) instead of alphabetical
                    if (!s.ContainsKey("propertyOrdering"))
                        r["propertyOrdering"] = new JsonArray(props.Select(p => (JsonNode?)p.Key).ToArray());
                    break;
                case "items" when v is JsonObject items:
                    r["items"] = ToGeminiSchema(items);
                    break;
                case "anyOf" when v is JsonArray anyOf:
                    r["anyOf"] = new JsonArray(anyOf.OfType<JsonObject>().Select(x => (JsonNode?)ToGeminiSchema(x)).ToArray());
                    break;
                case "enum" when v is JsonArray values:   // only string enums
                    if (values.All(x => x?.GetValueKind() == JsonValueKind.String))
                        r["enum"] = values.DeepClone();
                    break;
                case "type" or "properties" or "items" or "anyOf" or "enum":
                    break;
                default:
                    r[key] = v.DeepClone();
                    break;
            }
        }
        return r;
    }
}

/// <summary>Plumbing shared by the providers that call a JSON API with plain HttpClient (OpenAI, Gemini).</summary>
internal static class JsonApi
{
    public const int MaxOutputTokens = 16000;
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    public static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static JsonObject ToObject(JsonElement schema) =>
        JsonNode.Parse(schema.GetRawText()) as JsonObject ?? throw new AiException("El esquema de la respuesta tiene que ser un objeto JSON.");

    public static StringContent Content(JsonNode body) => new(body.ToJsonString(Readable), Encoding.UTF8, "application/json");

    /// <summary>Sends the request and reads the whole answer; a service that can't be reached is an AiException.</summary>
    public static async Task<(HttpStatusCode status, string body)> SendAsync(HttpClient http, HttpRequestMessage msg, string who, CancellationToken ct)
    {
        try
        {
            using var resp = await http.SendAsync(msg, ct);
            return (resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException e)
        {
            throw new AiException($"No pude conectar con {who}. Revisá tu conexión a internet.", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new AiException($"{who} tardó demasiado en responder. Probá de nuevo.", e);
        }
    }

    public static JsonDocument Parse(string json, string who)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new AiException($"{who} devolvió algo que no es JSON.", e);
        }
    }

    public static bool Has(JsonElement e, string name, out JsonElement value)
    {
        value = default;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value);
    }

    public static string Str(JsonElement e, string name) =>
        Has(e, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    public static HashSet<string> Names(JsonNode? array) =>
        array is JsonArray a ? a.Where(x => x?.GetValueKind() == JsonValueKind.String).Select(x => (string)x!).ToHashSet() : new();

    /// <summary>The message of an error answer and its codes (type, code, status, reasons) in lower case.</summary>
    public static (string message, string codes) ErrorOf(string body)
    {
        string raw = body.Trim();
        if (raw.Length > 300)
            raw = raw[..300] + "...";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (Has(doc.RootElement, "error", out var e))
            {
                var codes = new List<string> { Str(e, "type"), Str(e, "code"), Str(e, "status") };
                if (Has(e, "details", out var details) && details.ValueKind == JsonValueKind.Array)
                    codes.AddRange(details.EnumerateArray().Select(x => Str(x, "reason")));
                string message = Str(e, "message");
                return (message.Length > 0 ? message : raw, string.Join(" ", codes).ToLowerInvariant());
            }
        }
        catch (JsonException)
        {
        }
        return (raw, "");
    }
}
