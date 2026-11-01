using System.Text.Json;
using Retro.Ai;

namespace Compat.Tests;

[TestFixture, NonParallelizable]
public sealed class WriterResponseTests
{
    private string _folder = "", _previousGames = "";
    [SetUp]
    public void Prepare()
    {
        _folder = Path.Combine(Path.GetTempPath(), "retro-writer-response-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _previousGames = CreatedGames.UserFolder; CreatedGames.UserFolder = _folder;
    }
    [TearDown]
    public void Clean()
    {
        CreatedGames.UserFolder = _previousGames;
        Assert.That(Path.GetFullPath(_folder), Does.StartWith(Path.GetTempPath()));
        Directory.Delete(_folder, true);
    }

    [TestCase("{\"title\":")] [TestCase("{\"title\":bad}")]
    [TestCase("{\"title\":37}")] [TestCase("{\"source\":null}")]
    [TestCase("[{\"title\":\"Array\"}]")]
    [TestCase("```json\n[{\"title\":\"Array\"}]\n```")]
    [TestCase("```json\n\"ficha sin objeto\"\n```")]
    public void InvalidMetadataCannotBeBypassedByAddingACompleteCodeBlock(string metadata)
    {
        Assert.That(GameWriter.ParseDraft(metadata + "\n```asm\n nop\n```", out var problem), Is.Null);
        Assert.That(problem, Is.Not.Empty);
    }

    [Test]
    public void AmbiguousProgramsAndOversizedMetadataAreRejected()
    {
        string both = JsonSerializer.Serialize(new { title = "Uno", source = "nop" }) + "\n```asm\n rts\n```";
        Assert.That(GameWriter.ParseDraft(both, out _), Is.Null);
        Assert.That(GameWriter.ParseDraft("```asm\nnop\n```\n```asm\nrts\n```", out _), Is.Null);
        string longTitle = JsonSerializer.Serialize(new { title = new string('x', 61), source = "nop" });
        Assert.That(GameWriter.ParseDraft(longTitle, out _), Is.Null);
    }

    [TestCase("asm")] [TestCase("")]
    public void AFencedJsonCardAndAProgramAreReadAsTwoDifferentBlocks(string language)
    {
        string answer = "```json\n{\"title\":\"Propio\",\"pitch\":\"Regla\"}\n```\n```" + language + "\n nop\n```";
        var draft = GameWriter.ParseDraft(answer, out var problem);
        Assert.That(draft, Is.Not.Null, problem);
        Assert.That(draft!.Title, Is.EqualTo("Propio"));
        Assert.That(draft.Source.Trim(), Is.EqualTo("nop"));
    }

    [Test]
    public void MetadataOnlyEditsAreNoticedAndIncompleteEditsWait()
    {
        string code = Path.Combine(_folder, GameWorkspace.SourceFile), info = Path.Combine(_folder, GameWorkspace.InfoFile);
        File.WriteAllText(code, "nop"); File.WriteAllText(info, "{\"title\":\"Primero\"}");
        var first = GameWorkspace.Read(_folder)!.Value;
        File.WriteAllText(info, "{\"title\":\"Segundo\",\"changes\":\"Regla aclarada\"}");
        File.SetLastWriteTimeUtc(info, first.written.AddSeconds(2));
        var second = GameWorkspace.Read(_folder)!.Value;
        Assert.That(second.written, Is.GreaterThan(first.written));
        Assert.That(second.draft.Title, Is.EqualTo("Segundo"));
        Assert.That(second.draft.Changes, Is.EqualTo("Regla aclarada"));
        File.WriteAllText(info, "{\"title\":");
        Assert.That(GameWorkspace.Read(_folder), Is.Null, "Do not approve an older/default card while the replacement is incomplete");
    }

    [TestCase("[]")] [TestCase("{\"title\":false}")]
    public void InvalidAgentCardsDoNotBecomeDefaultMetadata(string metadata)
    {
        File.WriteAllText(Path.Combine(_folder, GameWorkspace.SourceFile), "nop");
        File.WriteAllText(Path.Combine(_folder, GameWorkspace.InfoFile), metadata);
        Assert.That(GameWorkspace.Read(_folder, out var problem), Is.Null);
        Assert.That(problem, Does.Contain(GameWorkspace.InfoFile));
    }

    private sealed class InvalidAnswers : IAiProvider
    {
        public string Name => "Respuestas locales incompletas";
        public int Calls;
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct) { Calls++; return Task.FromResult("sin programa"); }
    }
    [TestCase(0, 1)] [TestCase(99, 5)]
    public async Task AConfiguredAttemptLimitIsAlwaysFiniteAndAllowsAnInitialAttempt(int requested, int expected)
    {
        var provider = new InvalidAnswers();
        var result = await new GameWriter { MaxAttempts = requested }.WriteAsync(provider, "c64", "Conservar la regla", _folder, CancellationToken.None);
        Assert.That(provider.Calls, Is.EqualTo(expected));
        Assert.That(result.Attempts, Is.EqualTo(expected));
        Assert.That(result.Ok, Is.False);
    }
}
