using System.Net;
using System.Text;
using System.Text.Json;
using Retro.Ai;

namespace Fusion.Tests;

[TestFixture]
public class AiModelCatalogTests
{
    private string _dir = "";
    [SetUp] public void Setup() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro catalog " + Guid.NewGuid())).FullName;
    [TearDown] public void Cleanup() => Directory.Delete(_dir, true);
    private string Fake(string name, string body)
    {
        string path = Path.Combine(_dir, name + ".cmd");
        File.WriteAllText(path, "@echo off\r\n" + body.Replace("\n", "\r\n"));
        return path;
    }
    private string ReadLine(bool append) => $"set /p request=\n{(append ? ">>" : ">")}\"{Path.Combine(_dir, "requests.txt")}\" echo %request%\n";

    [Test, Platform("Win")]
    public async Task Codex_ListsEveryPageAndEachModelsOwnEfforts_WithoutStartingATurn()
    {
        string first = JsonSerializer.Serialize(new { id = 2, result = new { data = new[] {
            new { model = "future-a", displayName = "Nuevo A", hidden = false, isDefault = true,
                supportedReasoningEfforts = new[] { new { reasoningEffort = "deep" }, new { reasoningEffort = "light" } } } }, nextCursor = "page2" } });
        string second = "{\"id\":2,\"result\":{\"data\":[{\"model\":\"future-b\",\"displayName\":\"Nuevo B\",\"supportedReasoningEfforts\":[{\"reasoningEffort\":\"light\"}]},{\"model\":\"hidden\",\"hidden\":true}],\"nextCursor\":null}}";
        string exe = Fake("codex", ReadLine(false) + "echo {\"id\":1,\"result\":{}}\n" + ReadLine(true) + ReadLine(true) + "echo " + first + "\n" + ReadLine(true) + "echo " + second);
        var result = await AiModelCatalog.ReadCliAsync(CliTools.Codex, exe, CancellationToken.None);
        Assert.That(result.Select(m => m.Id), Is.EqualTo(new[] { "future-a", "future-b" }));
        Assert.That(result[0].Efforts.Select(e => e.Id), Is.EqualTo(new[] { "deep", "light" }));
        Assert.That(result[1].Efforts.Select(e => e.Id), Is.EqualTo(new[] { "light" }));
        string requests = File.ReadAllText(Path.Combine(_dir, "requests.txt"));
        Assert.That(requests, Does.Contain("model/list").And.Contain("page2").And.Not.Contain("turn/start").And.Not.Contain("thread/start"));
        Assert.That(result[0].IsDefault, Is.True);
    }

    [Test, Platform("Win")]
    public async Task Claude_UsesInitializationMetadataOnly_WithoutGuessingEffortLevels()
    {
        string response = "{\"type\":\"control_response\",\"response\":{\"subtype\":\"success\",\"response\":{\"models\":[{\"value\":\"future\",\"displayName\":\"Futuro\",\"supportedEffortLevels\":[\"light\",\"deep\"]},{\"value\":\"fixed\",\"supportsEffort\":true}]}}}";
        string exe = Fake("claude", ReadLine(false) + "echo " + response);
        var result = await AiModelCatalog.ReadCliAsync(CliTools.Claude, exe, CancellationToken.None);
        Assert.That(result[0].Name, Is.EqualTo("Futuro"));
        Assert.That(result[0].Efforts.Select(e => e.Id), Is.EqualTo(new[] { "light", "deep" }));
        Assert.That(result[1].Efforts, Is.Empty, "sin metadatos de niveles no se inventan");
        string request = File.ReadAllText(Path.Combine(_dir, "requests.txt"));
        Assert.That(request, Does.Contain("initialize").And.Not.Contain("\"type\":\"user\""));
    }

    [Test, Platform("Win")]
    public async Task Antigravity_GroupsOnlyPublishedVariants_AndRefreshesInsteadOfUsingAFixedList()
    {
        string exe = Fake("agy", "echo Fetching available models...\necho future-deep\tModelo nuevo (Deep)\necho future-light\tModelo nuevo (Light)\necho solo-light\tModelo fijo (Light)");
        var result = await AiModelCatalog.ReadCliAsync(CliTools.Google, exe, CancellationToken.None);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0].Efforts.Select(e => e.Id), Is.EqualTo(new[] { "deep", "light" }));
        Assert.That(result[0].Efforts[1].ModelId, Is.EqualTo("future-light"));
        Assert.That(result[0].Matches("future-light"), Is.True);
        Fake("agy", "echo next-ultra\tSiguiente (Ultra)");
        var fresh = await AiModelCatalog.ReadCliAsync(CliTools.Google, exe, CancellationToken.None);
        Assert.That(fresh.Single().Id, Is.EqualTo("next-ultra"));
    }

    [Test, Platform("Win")]
    public void FailedOrStalledCatalog_DoesNotFallBackToInventedModels()
    {
        string failed = Fake("agy", "echo Not logged in 1>&2\nexit /b 1");
        Assert.ThrowsAsync<AiException>(() => AiModelCatalog.ReadCliAsync(CliTools.Google, failed, CancellationToken.None));
        string stalled = Fake("codex", "ping -n 20 127.0.0.1 > nul");
        Assert.ThrowsAsync<AiException>(() => AiModelCatalog.ReadCliAsync(CliTools.Codex, stalled, CancellationToken.None, TimeSpan.FromMilliseconds(150)));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.CatchAsync<OperationCanceledException>(() => AiModelCatalog.ReadCliAsync(CliTools.Codex, stalled, canceled.Token));
    }

    [Test]
    public async Task Ollama_OnlyReadsTheInstalledModelCatalog()
    {
        var handler = new CatalogHandler("{\"models\":[{\"name\":\"local-a:tiny\"},{\"name\":\"local-b:q4\"},{\"name\":\"local-a:tiny\"}]}");
        using var http = new HttpClient(handler);
        var result = await AiModelCatalog.ReadOllamaAsync("http://localhost:11434/", CancellationToken.None, http);
        Assert.That(result.Select(m => m.Id), Is.EqualTo(new[] { "local-a:tiny", "local-b:q4" }));
        Assert.That(handler.Requests, Is.EqualTo(new[] { "GET http://localhost:11434/api/tags" }));
        Assert.That(result.SelectMany(m => m.Efforts), Is.Empty);
    }

    [Test]
    public void InvalidEffortIsRejectedAndOldSettingsRemainCompatible()
    {
        Assert.That(CliAgentProvider.EffortArgs("sub-codex", "deep"), Is.EqualTo(new[] { "-c", "model_reasoning_effort='deep'" }));
        Assert.That(CliAgentProvider.EffortArgs("sub-claude", "light"), Is.EqualTo(new[] { "--effort", "light" }));
        Assert.That(CliAgentProvider.EffortArgs("sub-google", ""), Is.Empty);
        Assert.Throws<AiException>(() => CliAgentProvider.EffortArgs("sub-codex", "high' & command"));
        var old = JsonSerializer.Deserialize<AiSettings>("{}")!;
        Assert.That(old.CliEfforts, Is.Empty, "configuraciones anteriores siguen funcionando");
    }

    [TestCase("sub-claude", "--effort deep")]
    [TestCase("sub-codex", "model_reasoning_effort='deep'")]
    [Platform("Win")]
    public async Task ChosenEffortReachesTheProcess_AndASessionOverrideClearsIt(string id, string expected)
    {
        string args = Path.Combine(_dir, "args.txt");
        string exe = Fake("tool", $"echo %* > \"{args}\"\nmore > nul\necho {{\"is_error\":false,\"result\":\"listo\"}}");
        var settings = new AiSettings { Provider = id };
        settings.CliPaths[id] = exe;
        settings.CliModels[id] = "future";
        settings.CliEfforts[id] = "deep";
        var request = new AiRequest("Prueba", new[] { new AiTurn(true, "Mensaje simulado") }, null);
        await settings.CreateProvider()!.CompleteAsync(request, CancellationToken.None);
        Assert.That(File.ReadAllText(args), Does.Contain(expected));
        settings.SessionModel = "different";
        await settings.CreateProvider()!.CompleteAsync(request, CancellationToken.None);
        Assert.That(File.ReadAllText(args), Does.Contain("different").And.Not.Contain("deep"));
    }

    private sealed class CatalogHandler(string response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Method + " " + request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        }
    }
}
