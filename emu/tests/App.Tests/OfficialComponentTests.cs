using System.Net;
using System.Text;
using Retro.App;

namespace App.Tests;

public sealed class OfficialComponentTests
{
    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Requests++; return Task.FromResult(respond(request)); }
    }
    private static HttpResponseMessage Payload(HttpRequestMessage request) => new(HttpStatusCode.OK)
        { RequestMessage = request, Content = new ByteArrayContent([1, 2, 3, 4]) };

    [Test]
    public async Task RefusingTheDownloadNeverTouchesTheNetworkOrRunsAnything()
    {
        using var transport = new Transport(_ => throw new AssertionException("A refused operation reached the network."));
        using var http = new HttpClient(transport); int confirmations = 0, executions = 0;
        Assert.That(await OfficialComponent.InstallVisualCppAsync(() => false, () => { confirmations++; return true; }, http,
            _ => throw new AssertionException("Unexpected verification."), (_, _) => { executions++; return Task.FromResult(0); }), Is.EqualTo(ComponentInstallResult.Declined));
        Assert.That(transport.Requests, Is.Zero); Assert.That(confirmations, Is.Zero); Assert.That(executions, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task InstallationHasASeparateDecisionAfterSignatureVerificationAndCleansItsOwnFile(bool consent)
    {
        using var transport = new Transport(Payload); using var http = new HttpClient(transport);
        string? path = null; bool verified = false; int executions = 0;
        var result = await OfficialComponent.InstallVisualCppAsync(() => true, () => { Assert.That(verified, Is.True); return consent; }, http,
            file => { path = file; verified = true; Assert.That(File.ReadAllBytes(file), Is.EqualTo(new byte[] { 1, 2, 3, 4 })); return true; },
            (file, _) => { executions++; Assert.That(file, Is.EqualTo(path)); return Task.FromResult(0); });
        Assert.That(result, Is.EqualTo(consent ? ComponentInstallResult.Installed : ComponentInstallResult.Declined));
        Assert.That(executions, Is.EqualTo(consent ? 1 : 0)); Assert.That(File.Exists(path), Is.False);
        Assert.That(Directory.Exists(Path.GetDirectoryName(path!)), Is.False);
    }

    [Test]
    public void AnUnsignedPayloadIsNeverOfferedForExecution()
    {
        using var transport = new Transport(Payload); using var http = new HttpClient(transport); int approvals = 0;
        Assert.ThrowsAsync<InvalidDataException>(async () => await OfficialComponent.InstallVisualCppAsync(() => true,
            () => { approvals++; return true; }, http, _ => false, (_, _) => throw new AssertionException("Unexpected execution.")));
        Assert.That(approvals, Is.Zero);
    }

    [TestCase("http://download.microsoft.com/installer.exe")]
    [TestCase("https://download.microsoft.com.evil.invalid/file")]
    [TestCase("https://user@download.microsoft.com/file")]
    [TestCase("https://download.microsoft.com:444/file")]
    [TestCase("file:///C:/untrusted.exe")]
    public void RedirectsCannotReachAnotherHostProtocolUserOrPort(string address)
    {
        using var transport = new Transport(request => new(HttpStatusCode.Redirect) { RequestMessage = request, Headers = { Location = new Uri(address) } });
        using var http = new HttpClient(transport);
        Assert.ThrowsAsync<InvalidDataException>(async () => await OfficialComponent.InstallVisualCppAsync(() => true, () => true, http,
            _ => throw new AssertionException("Unexpected verification."), (_, _) => throw new AssertionException("Unexpected execution.")));
        Assert.That(transport.Requests, Is.EqualTo(1));
    }

    [TestCase(true), TestCase(false)]
    public void OversizedOrIncompletePayloadsDoNotReachTheVerifier(bool oversized)
    {
        using var transport = new Transport(request =>
        {
            var response = Payload(request); response.Content.Headers.ContentLength = oversized ? OfficialComponent.MaximumInstallerBytes + 1 : 20; return response;
        });
        using var http = new HttpClient(transport);
        Assert.ThrowsAsync<InvalidDataException>(async () => await OfficialComponent.InstallVisualCppAsync(() => true, () => true, http,
            _ => throw new AssertionException("Unexpected verification."), (_, _) => throw new AssertionException("Unexpected execution.")));
    }

    [Test]
    public void CancellingAfterVerificationDoesNotPromptOrExecuteAnInstaller()
    {
        using var transport = new Transport(Payload); using var http = new HttpClient(transport); using var cancellation = new CancellationTokenSource();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await OfficialComponent.InstallVisualCppAsync(() => true, () => throw new AssertionException("Unexpected prompt."), http,
            _ => { cancellation.Cancel(); return true; }, (_, _) => throw new AssertionException("Unexpected execution."), cancellation.Token));
    }

    [TestCase(1638, "AlreadyPresent"), TestCase(3010, "RestartRequired")]
    public async Task ExistingComponentsAndRequiredRestartsAreReportedAccurately(int code, string expected)
    {
        using var transport = new Transport(Payload); using var http = new HttpClient(transport);
        Assert.That((await OfficialComponent.InstallVisualCppAsync(() => true, () => true, http, _ => true, (_, _) => Task.FromResult(code))).ToString(), Is.EqualTo(expected));
    }

    [Test]
    public void PeImportsDetermineWhetherTheComponentIsActuallyMissing()
    {
        string folder = Path.Combine(Path.GetTempPath(), "retro-pe-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string dll = Path.Combine(folder, "raylib.dll");
            using (var file = File.Create(dll)) using (var writer = new BinaryWriter(file))
            {
                file.SetLength(1024); writer.Write((ushort)0x5A4D); file.Position = 0x3c; writer.Write(0x80);
                file.Position = 0x80; writer.Write(0x4550); writer.Write((ushort)0x8664); writer.Write((ushort)1);
                file.Position = 0x94; writer.Write((ushort)240); writer.Write((ushort)0x2022);
                file.Position = 0x98; writer.Write((ushort)0x20b);
                file.Position = 0x98 + 56; writer.Write(0x2000); writer.Write(0x200);
                file.Position = 0x98 + 108; writer.Write(16);
                file.Position = 0x98 + 120; writer.Write(0x1000); writer.Write(40);
                file.Position = 0x188; writer.Write(Encoding.ASCII.GetBytes(".rdata\0\0")); writer.Write(512); writer.Write(0x1000); writer.Write(512); writer.Write(0x200);
                file.Position = 0x200; writer.Write(0x1060); writer.Write(0); writer.Write(0); writer.Write(0x1040); writer.Write(0x1060);
                file.Position = 0x240; writer.Write(Encoding.ASCII.GetBytes("vcruntime140.dll\0"));
            }
            Assert.That(OfficialComponent.Imports(dll), Does.Contain("vcruntime140.dll"));
            Assert.That(OfficialComponent.MissingVisualCpp(folder, _ => false), Is.EqualTo(new[] { "vcruntime140.dll" }));
            Assert.That(OfficialComponent.MissingVisualCpp(folder, _ => true), Is.Empty);
            string bundled = Path.Combine(folder, "vcruntime140.dll"); File.WriteAllText(bundled, "Own placeholder: the component is part of the package.");
            Assert.That(OfficialComponent.MissingVisualCpp(folder, _ => false), Is.Empty);
            File.Delete(bundled);
            using (var file = File.OpenWrite(dll)) { file.Position = 0x84; file.Write([0x4c, 0x01]); }
            Assert.That(OfficialComponent.MissingVisualCpp(folder, _ => false), Is.Empty, "An x86 DLL in an x64 package is a wrong package file, not a missing x64 installer.");
            File.Delete(dll);
            Assert.That(OfficialComponent.MissingVisualCpp(folder, _ => false), Is.Empty, "A missing program DLL must not be guessed to need VC++.");
        }
        finally { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
    }

    [Test]
    public void TheExistingNativeLibrariesHaveReadableX64ImportTables()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows PE libraries only.");
        foreach (string name in new[] { "raylib.dll", "cimgui.dll" })
        {
            string path = Path.Combine(AppContext.BaseDirectory, name);
            if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", name);
            Assert.That(File.Exists(path), Is.True, "The local test build must include its native library.");
            Assert.That(OfficialComponent.Imports(path).Any(import => import.Equals("KERNEL32.dll", StringComparison.OrdinalIgnoreCase)), Is.True, name);
        }
    }

    [Test]
    public void TheSignatureCheckRejectsUnsignedFilesAndRecognizesTheExistingMicrosoftHost()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Authenticode is Windows-specific.");
        string host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        if (!File.Exists(host)) Assert.Ignore("No existing Microsoft host to inspect; nothing is downloaded.");
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, "Not an executable"); Assert.That(MicrosoftSignature.Verify(path), Is.False); Assert.That(MicrosoftSignature.Verify(host), Is.True); }
        finally { File.Delete(path); }
    }
}
