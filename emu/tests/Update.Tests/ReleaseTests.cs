using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Retro.Update;

namespace Update.Tests;

[TestFixture]
public class ReleaseTests
{
    private string _folder = "";
    [SetUp] public void Setup() { _folder = Path.Combine(Path.GetTempPath(), "RetroEmu-update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_folder); }
    [TearDown] public void Cleanup() { Directory.Delete(_folder, true); }
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("Paquete de prueba propio");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Release(string version = "0.2.2", string? url = null, string? digest = null, bool prerelease = false) => JsonSerializer.Serialize(new
    {
        draft = false, prerelease, tag_name = "v" + version,
        assets = new[] { new { name = $"RetroEmu-{version}-win-x64.zip", state = "uploaded", size = Bytes.Length,
            digest = digest ?? "sha256:" + Hash(Bytes), browser_download_url = url ?? $"https://github.com/{ReleaseClient.Repository}/releases/download/v{version}/RetroEmu-{version}-win-x64.zip" } }
    });
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(respond(request)); }
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };

    [TestCase("0.2.0", false)]
    [TestCase("0.2.1", false)]
    [TestCase("0.2.2", true)]
    public async Task OnlyNewStablePlatformAssetIsSelected(string version, bool available)
    {
        using var handler = new FakeHandler(request => { Assert.That(request.RequestUri, Is.EqualTo(ReleaseClient.Latest)); return Json(Release(version)); });
        using var http = new HttpClient(handler); var release = await new ReleaseClient(http).CheckAsync(new(0, 2, 1), "win-x64", CancellationToken.None);
        Assert.That(release != null, Is.EqualTo(available));
        Assert.That(handler.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task PrereleaseIsIgnored()
    {
        using var http = new HttpClient(new FakeHandler(_ => Json(Release(prerelease: true))));
        Assert.That(await new ReleaseClient(http).CheckAsync(new(0, 2, 1), "win-x64", CancellationToken.None), Is.Null);
    }

    [TestCase("https://example.com/program.zip", null)]
    [TestCase(null, "sha256:bad")]
    [TestCase("https://github.com/otra/copia/releases/download/v0.2.2/RetroEmu-0.2.2-win-x64.zip", null)]
    public void RejectsUnverifiedOrForeignDownload(string? url, string? digest)
    {
        using var http = new HttpClient(new FakeHandler(_ => Json(Release(url: url, digest: digest))));
        Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseClient(http).CheckAsync(new(0, 2, 1), "win-x64", CancellationToken.None));
    }

    [Test]
    public async Task DownloadMustMatchBothHashAndSize()
    {
        using var http = new HttpClient(new FakeHandler(request => request.RequestUri == ReleaseClient.Latest ? Json(Release())
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) }));
        var client = new ReleaseClient(http);
        var asset = (await client.CheckAsync(new(0, 2, 1), "win-x64", CancellationToken.None))!;
        string path = Path.Combine(_folder, "good.part"); await client.DownloadAsync(asset, path, null, CancellationToken.None);
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(Bytes));
        Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(asset with { Sha256 = new string('0', 64) }, Path.Combine(_folder, "bad.part"), null, CancellationToken.None));
        Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(asset with { Size = Bytes.Length + 1 }, Path.Combine(_folder, "short.part"), null, CancellationToken.None));
    }

    [Test]
    public void NetworkFailureDoesNotBecomeAnUpdate()
    {
        using var http = new HttpClient(new FakeHandler(_ => new(HttpStatusCode.ServiceUnavailable)));
        Assert.ThrowsAsync<HttpRequestException>(() => new ReleaseClient(http).CheckAsync(new(0, 2, 1), "win-x64", CancellationToken.None));
    }
}
