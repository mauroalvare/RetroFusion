using System.Text.Json;
using Retro.Studio;

namespace Studio.Tests;

public sealed class ResponseContractTests
{
    [TestCase("{\"title\":")] [TestCase("{\"title\":37}")]
    [TestCase("{\"missingData\":false}")] [TestCase("[{\"title\":\"Array\"}]")]
    [TestCase("```json\n[{\"title\":\"Array\"}]\n```")]
    [TestCase("```json\n\"ficha sin objeto\"\n```")]
    public void InvalidMetadataCannotBeBypassedWithJavascript(string metadata)
    {
        var game = StudioDesigner.ParseGame(metadata + "\n```js\nfunction draw(){}\n```", new(), out var error);
        Assert.That(game, Is.Null); Assert.That(error, Is.Not.Empty);
    }
    [Test]
    public void DifferentProgramsInTheSameAnswerAndOversizedLabelsAreRejected()
    {
        string answer = JsonSerializer.Serialize(new { script = "function draw(){screen.rect(0,0,1,1,1);}" }) + "\n```js\nfunction draw(){}\n```";
        Assert.That(StudioDesigner.ParseGame(answer, new(), out _), Is.Null);
        answer = "```js\nfunction draw(){}\n```\n```js\nfunction draw(){screen.rect(0,0,1,1,1);}\n```";
        Assert.That(StudioDesigner.ParseGame(answer, new(), out _), Is.Null);
        answer = JsonSerializer.Serialize(new { title = new string('x', 61), script = "function draw(){}" });
        Assert.That(StudioDesigner.ParseGame(answer, new(), out _), Is.Null);
    }

    [Test]
    public void TheManualOutputExampleIsValidJsonAndIncludesTheExecutablePlans()
    {
        string prompt = StudioDesigner.ManualWritePrompt([new() { Slot = "a", Profile = "own-a" }, new() { Slot = "b", Profile = "own-b" }],
            _ => null, new("Propio", "", "Regla", "Controles", Direction: new FusionDirection("a")));
        var matches = System.Text.RegularExpressions.Regex.Matches(prompt, "```json\\s*\\n(.*?)```", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.That(matches.Count, Is.GreaterThan(0), "The manual contract needs an executable JSON example");
        var match = matches[^1];
        using var document = JsonDocument.Parse(match.Groups[1].Value);
        Assert.That(document.RootElement.GetProperty("playtests").ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(document.RootElement.TryGetProperty("script", out _), Is.False, "The program belongs in the second code block for this mode");
    }
}
