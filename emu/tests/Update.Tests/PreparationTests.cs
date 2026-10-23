using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Retro.Update;

namespace Update.Tests;

[TestFixture]
public class PreparationTests
{
    private string _root = "", _old = "", _archive = "";
    private ReleaseAsset _asset = null!;
    [SetUp] public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "RetroEmu-prepare-" + Guid.NewGuid().ToString("N"));
        _old = Path.Combine(_root, "instalada"); PackageTests.OwnPackage(_old, "0.2.1");
        string source = Path.Combine(_root, "source/RetroEmu-0.2.2-win-x64"); PackageTests.OwnPackage(source);
        _archive = Path.Combine(_root, "package.zip"); ZipFile.CreateFromDirectory(Path.GetDirectoryName(source)!, _archive);
        byte[] bytes = File.ReadAllBytes(_archive);
        _asset = new(new(0, 2, 2), "win-x64", "package.zip", new Uri("https://github.com/own.zip"), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        File.WriteAllText(Path.Combine(_old, "ROMs/dato-propio.txt"), "Datos personales de prueba");
    }
    [TearDown] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class Transport(byte[] bytes) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
    }
    [Test]
    public async Task PreparationNeverMovesCurrentDataAndCanResumeWithoutAnotherDownload()
    {
        using var handler = new Transport(File.ReadAllBytes(_archive)); using var http = new HttpClient(handler);
        var client = new ReleaseClient(http);
        var plan = await UpdatePreparation.PrepareAsync(client, _asset, _old, null, CancellationToken.None);
        Assert.That(plan.State, Is.EqualTo("prepared"));
        Assert.That(File.ReadAllText(Path.Combine(_old, "ROMs/dato-propio.txt")), Is.EqualTo("Datos personales de prueba"));
        var again = await UpdatePreparation.PrepareAsync(client, _asset, _old, null, CancellationToken.None);
        Assert.That(again.Id, Is.EqualTo(plan.Id)); Assert.That(handler.Calls, Is.EqualTo(1));
    }
    [Test]
    public async Task PreparedPayloadStaysInternalAndTheInstalledDirectoryIsUnchanged()
    {
        using var handler=new Transport(File.ReadAllBytes(_archive));using var http=new HttpClient(handler);
        var plan=await UpdatePreparation.PrepareAsync(new ReleaseClient(http),_asset,_old,null,CancellationToken.None);
        Assert.That(plan.NewRoot,Is.EqualTo(_old));Assert.That(plan.Format,Is.EqualTo(2));
        Assert.That(plan.PayloadRoot,Does.StartWith(plan.Job+Path.DirectorySeparatorChar));
        Assert.That(Directory.Exists(plan.LegacyRoot),Is.False);
        Assert.That(PackageManifest.Read(_old).Version,Is.EqualTo("0.2.1"));
        if(OperatingSystem.IsWindows())Assert.That(File.GetAttributes(UpdatePlan.Metadata(_root)).HasFlag(FileAttributes.Hidden),Is.False);
    }

    [Test]
    public async Task SavedPlanBeforeFinalRenameIsRecoveredWithoutNetwork()
    {
        var plan = UpdatePlan.Create(_old, _asset); plan.Save();
        string stage = plan.PayloadRoot; PackageTests.OwnPackage(stage);
        using var handler = new Transport([]); using var http = new HttpClient(handler);
        var recovered = await UpdatePreparation.PrepareAsync(new ReleaseClient(http), _asset, _old, null, CancellationToken.None);
        Assert.That(recovered.Id, Is.EqualTo(plan.Id)); Assert.That(Directory.Exists(recovered.PayloadRoot), Is.True); Assert.That(handler.Calls, Is.Zero);
    }
    [Test]
    public async Task FailedInstallationCanBeRetriedWithoutLosingDataOrDownloadingAgain()
    {
        using var handler = new Transport(File.ReadAllBytes(_archive)); using var http = new HttpClient(handler);
        var client = new ReleaseClient(http);
        var plan = await UpdatePreparation.PrepareAsync(client, _asset, _old, null, CancellationToken.None);
        Assert.ThrowsAsync<IOException>(() => UpdateInstaller.ApplyAsync(plan, () => throw new IOException("Fallo propio de prueba")));
        Assert.That(plan.State, Is.EqualTo("rolled-back"));
        var retry = await UpdatePreparation.PrepareAsync(client, _asset, _old, null, CancellationToken.None);
        Assert.That(retry.State, Is.EqualTo("prepared")); Assert.That(retry.Moves, Is.Empty); Assert.That(retry.RollbackNext, Is.Null);
        Assert.That(handler.Calls, Is.EqualTo(1));
        await UpdateInstaller.ApplyAsync(retry, () => Task.CompletedTask);
        Assert.That(File.ReadAllText(Path.Combine(retry.NewRoot, "ROMs/dato-propio.txt")), Is.EqualTo("Datos personales de prueba"));
    }
    [Test]
    public async Task ExistingUnrelatedVersionFolderIsIgnoredAndNeverOverwritten()
    {
        string existing = Path.Combine(_root, "RetroEmu-0.2.2-win-x64"); Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "personal.txt"), "No reemplazar");
        using var handler = new Transport(File.ReadAllBytes(_archive)); using var http = new HttpClient(handler);
        var plan = await UpdatePreparation.PrepareAsync(new ReleaseClient(http), _asset, _old, null, CancellationToken.None);
        Assert.That(plan.NewRoot, Is.EqualTo(_old)); Assert.That(Directory.Exists(plan.PayloadRoot), Is.True);
        Assert.That(handler.Calls, Is.EqualTo(1)); Assert.That(File.ReadAllText(Path.Combine(existing, "personal.txt")), Is.EqualTo("No reemplazar"));
    }
    [Test]
    public void InvalidDownloadLeavesNoUpdaterFolder()
    {
        using var handler = new Transport(File.ReadAllBytes(_archive)); using var http = new HttpClient(handler);
        Assert.ThrowsAsync<InvalidDataException>(() => UpdatePreparation.PrepareAsync(new ReleaseClient(http),
            _asset with { Sha256 = new string('0',64) }, _old, null, CancellationToken.None));
        Assert.That(Directory.Exists(UpdatePlan.Metadata(_root)), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(_old,"ROMs/dato-propio.txt")), Is.EqualTo("Datos personales de prueba"));
    }

    [Test]
    public void CancelledDownloadLeavesNoUpdaterFolder()
    {
        using var handler = new Transport(File.ReadAllBytes(_archive)); using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        Assert.CatchAsync<OperationCanceledException>(() => UpdatePreparation.PrepareAsync(new ReleaseClient(http),
            _asset, _old, new CancelProgress(cancellation), cancellation.Token));
        Assert.That(Directory.Exists(UpdatePlan.Metadata(_root)), Is.False);
        PackageManifest.Read(_old).Verify(_old, exact: false);
    }

    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<UpdateProgress>
    { public void Report(UpdateProgress value) => cancellation.Cancel(); }

}
