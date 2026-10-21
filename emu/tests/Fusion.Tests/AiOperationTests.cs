using System.Text.Json;
using Retro.Ai;

namespace Fusion.Tests;

[TestFixture]
public sealed class AiOperationTests
{
    private sealed class Fake : IAiProvider
    {
        public string Name => "IA simulada";
        public AiUsage? LastUsage { get; set; }
        public List<AiRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct) { Requests.Add(request); return Task.FromResult("respuesta"); }
    }
    private static AiRequest Request() => new("Contrato", [new(true, "Pedido"), new(false, "Código previo")],
        JsonSerializer.SerializeToElement(new { type = "object" }), ["original-a.png", "original-b.png"]);

    [Test]
    public async Task TextLimitStopsBeforeTheProvider_WithoutRemovingAnyInstructionsSourceOrImages()
    {
        var request = Request(); var estimate = AiEstimate.For(request);
        Assert.That(estimate.Characters, Is.EqualTo(request.System.Length + request.Turns.Sum(t => t.Text.Length) + request.Schema!.Value.GetRawText().Length));
        Assert.That(estimate.Images, Is.EqualTo(2));
        var fake = new Fake(); var operation = new AiOperation(fake, 3, estimate.Characters);
        await operation.CompleteAsync(request, CancellationToken.None);
        Assert.ThrowsAsync<AiException>(() => operation.CompleteAsync(request, CancellationToken.None));
        Assert.That(fake.Requests, Has.Count.EqualTo(1));
        Assert.That(fake.Requests[0], Is.SameAs(request));
        Assert.That(operation.Stats.CallsWithUsage, Is.Zero, "consumo desconocido no es consumo cero");
        var next = new AiOperation(fake, 2, estimate.Characters, operation.Stats);
        Assert.ThrowsAsync<AiException>(() => next.CompleteAsync(request, CancellationToken.None), "continuar no reinicia el límite de texto");
        var enlarged = new AiOperation(fake, 2, estimate.Characters * 2, operation.Stats);
        await enlarged.CompleteAsync(request, CancellationToken.None);
        Assert.That(enlarged.Stats.TextCharacters, Is.EqualTo(estimate.Characters * 2));
    }

    [Test]
    public async Task UsageStaysPartialWhenOneCallDoesNotReportCounts_AndCachedInputIsNotAddedTwice()
    {
        var fake = new Fake { LastUsage = new(120, 30, 90) };
        var operation = new AiOperation(fake, 2);
        await operation.CompleteAsync(Request(), CancellationToken.None);
        fake.LastUsage = null;
        await operation.CompleteAsync(Request(), CancellationToken.None);
        Assert.That(operation.Stats, Is.EqualTo(new AiOperationStats(2, 1, AiEstimate.For(Request()).Characters * 2, 120, 30, 90)));
        Assert.ThrowsAsync<AiException>(() => operation.CompleteAsync(Request(), CancellationToken.None));
        Assert.That(fake.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public void ProviderMetadataIsOptional_AndItsDifferentCacheConventionsAreRespected()
    {
        using var response = JsonDocument.Parse("""{"usage":{"input_tokens":120,"output_tokens":30,"input_tokens_details":{"cached_tokens":90}}} """);
        Assert.That(AiUsage.Responses(response.RootElement), Is.EqualTo(new AiUsage(120, 30, 90)));
        using var claude = JsonDocument.Parse("""{"usage":{"input_tokens":20,"output_tokens":30,"cache_read_input_tokens":90,"cache_creation_input_tokens":10}}""");
        Assert.That(AiUsage.Claude(claude.RootElement), Is.EqualTo(new AiUsage(120, 30, 90)));
        using var gemini = JsonDocument.Parse("""{"usageMetadata":{"promptTokenCount":120,"candidatesTokenCount":20,"thoughtsTokenCount":10}}""");
        Assert.That(AiUsage.Gemini(gemini.RootElement), Is.EqualTo(new AiUsage(120, 30)));
        using var ollama = JsonDocument.Parse("""{"prompt_eval_count":120,"eval_count":30}""");
        Assert.That(AiUsage.Ollama(ollama.RootElement), Is.EqualTo(new AiUsage(120, 30)));
        using var missing = JsonDocument.Parse("""{"usage":{"input_tokens":120}}""");
        Assert.That(AiUsage.Responses(missing.RootElement), Is.Null);
        Assert.That(AiUsage.Cli("sub-google", "respuesta sin contadores"), Is.Null);
        Assert.That(AiUsage.Cli("sub-google", """{"stats":{"models":{"modelo-local":{"tokens":{"prompt":120,"input":30,"candidates":20,"thoughts":10,"cached":90,"tool":0}}}}}"""), Is.EqualTo(new AiUsage(120, 30, 90)), "prompt ya incluye caché; input es solo la parte sin caché");
    }
}
