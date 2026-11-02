using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Retro.Update;

namespace Update.Tests;

public sealed class StartupUpdaterTests
{
    private string _root = "", _installed = "";
    private byte[] _zip = [];
    [SetUp] public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "RetroEmu-startup-update-" + Guid.NewGuid().ToString("N"));
        _installed = Path.Combine(_root, "instalada");
        PackageTests.OwnPackage(_installed, "0.3.6");
        File.WriteAllText(Path.Combine(_installed, "ROMs/propio.txt"), "Datos propios");
        string source = Path.Combine(_root, "source/RetroEmu-0.3.7-win-x64"); PackageTests.OwnPackage(source, "0.3.7");
        string archive = Path.Combine(_root, "package.zip"); ZipFile.CreateFromDirectory(Path.GetDirectoryName(source)!, archive);
        _zip = File.ReadAllBytes(archive);
    }
    [TearDown] public void Cleanup() => Directory.Delete(_root, recursive: true);
    private sealed class Transport(byte[] package) : HttpMessageHandler
    {
        public int Checks, Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request.RequestUri == ReleaseClient.Latest)
            {
                Checks++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                {
                    draft = false, prerelease = false, tag_name = "v0.3.7", assets = new[] { new
                    {
                        name = "RetroEmu-0.3.7-win-x64.zip", state = "uploaded", size = package.Length,
                        digest = "sha256:" + Convert.ToHexString(SHA256.HashData(package)),
                        browser_download_url = $"https://github.com/{ReleaseClient.Repository}/releases/download/v0.3.7/RetroEmu-0.3.7-win-x64.zip"
                    } }
                })) });
            }
            Downloads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) });
        }
    }

    [TestCase(false, false, 0, 0)]
    [TestCase(true, false, 1, 0)]
    [TestCase(true, true, 1, 1)]
    public async Task FailedGraphicsNeedNoGraphicsLibraryToCheckAndPrepare(bool check, bool install, int checks, int downloads)
    {
        using var transport = new Transport(_zip); using var http = new HttpClient(transport);
        UpdatePlan? started = null;
        var result = await new StartupUpdater(new(http)).RunAsync(_installed, () => check, _ => install, plan => started = plan, null, CancellationToken.None);
        Assert.That(transport.Checks, Is.EqualTo(checks)); Assert.That(transport.Downloads, Is.EqualTo(downloads));
        Assert.That(started != null, Is.EqualTo(check && install));
        Assert.That(result, Is.EqualTo(check && install ? StartupUpdateResult.Started : StartupUpdateResult.Declined));
        Assert.That(PackageManifest.Read(_installed).Version, Is.EqualTo("0.3.6"));
        Assert.That(File.ReadAllText(Path.Combine(_installed, "ROMs/propio.txt")), Is.EqualTo("Datos propios"));
        if (started != null) PackageManifest.Read(started.PayloadRoot).Verify(started.PayloadRoot, exact: true);
    }

    [Test]
    public void CancellationBeforeTheQuestionDoesNotCheckOrDownload()
    {
        using var transport = new Transport(_zip); using var http = new HttpClient(transport);
        Assert.ThrowsAsync<OperationCanceledException>(() => new StartupUpdater(new(http)).RunAsync(_installed,
            () => throw new AssertionException("No question after cancellation"), _ => true, _ => Assert.Fail(), null, new CancellationToken(true)));
        Assert.That(transport.Checks + transport.Downloads, Is.Zero);
    }
}
