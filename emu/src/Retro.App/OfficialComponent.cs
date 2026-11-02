using System.Diagnostics;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Retro.App;

internal enum ComponentInstallResult { Declined, Installed, AlreadyPresent, RestartRequired }

/// <summary>Fixed catalog: neither an error message nor a downloaded manifest can supply an installer URL or arguments.</summary>
internal static class OfficialComponent
{
    internal static readonly Uri VisualCppSource = new("https://aka.ms/vc14/vc_redist.x64.exe");
    internal static readonly Uri VisualCppGuide = new("https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist");
    internal static readonly Uri WindowsGuide = new("https://learn.microsoft.com/en-us/dotnet/core/install/windows");
    internal static readonly Uri LinuxGuide = new("https://learn.microsoft.com/en-us/dotnet/core/install/linux");
    internal const long MaximumInstallerBytes = 64 * 1024 * 1024;
    internal static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(5) };
    internal static readonly string[] VcLibraries = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll"];

    /// <summary>Read the import table of an owned PE file. This never loads or executes a library.</summary>
    internal static IReadOnlyList<string> Imports(string path)
    {
        using var file = File.OpenRead(path); using var pe = new PEReader(file);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64) throw new BadImageFormatException();
        var directory = pe.PEHeaders.PEHeader?.ImportTableDirectory ?? default;
        if (directory.RelativeVirtualAddress == 0 || directory.Size < 20) return [];
        var section = pe.GetSectionData(directory.RelativeVirtualAddress);
        if (directory.Size > section.Length) throw new BadImageFormatException();
        var reader = section.GetReader(0, directory.Size);
        var names = new List<string>();
        for (int count = 0; count < 512 && reader.RemainingBytes >= 20; count++)
        {
            uint original = reader.ReadUInt32(), timestamp = reader.ReadUInt32(), chain = reader.ReadUInt32(), name = reader.ReadUInt32(), thunk = reader.ReadUInt32();
            if ((original | timestamp | chain | name | thunk) == 0) return names;
            if (name > int.MaxValue) throw new BadImageFormatException();
            var text = pe.GetSectionData((int)name).GetReader();
            var bytes = new List<byte>(); bool terminated = false;
            while (text.RemainingBytes > 0 && bytes.Count <= 256)
            { byte value = text.ReadByte(); if (value == 0) { terminated = true; break; } bytes.Add(value); }
            if (!terminated || bytes.Count is 0 or > 256 || bytes.Any(value => value < 32 || value > 126)) throw new BadImageFormatException();
            names.Add(System.Text.Encoding.ASCII.GetString(bytes.ToArray()));
        }
        throw new BadImageFormatException();
    }

    internal static IReadOnlyList<string> MissingVisualCpp(string root, Func<string, bool>? systemHasLibrary = null)
    {
        systemHasLibrary ??= SystemHasLibrary;
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in new[] { "raylib.dll", "cimgui.dll" })
        {
            string path = Path.Combine(root, name);
            if (!File.Exists(path)) path = Path.Combine(root, "runtimes", "win-x64", "native", name);
            if (!File.Exists(path)) continue;
            try
            {
                foreach (string import in Imports(path).Where(import => VcLibraries.Contains(import, StringComparer.OrdinalIgnoreCase)))
                    if (!File.Exists(Path.Combine(root, import)) && !File.Exists(Path.Combine(Path.GetDirectoryName(path)!, import)) && !systemHasLibrary(import)) needed.Add(import);
            }
            // A damaged package is repaired as a package, never guessed to need a system installer.
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentOutOfRangeException) { }
        }
        return needed.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool SystemHasLibrary(string name)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (!NativeLibrary.TryLoad(name, typeof(OfficialComponent).Assembly, DllImportSearchPath.System32, out nint handle)) return false;
        NativeLibrary.Free(handle); return true;
    }

    internal static bool AllowedDownload(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && (uri == VisualCppSource || uri.Host is "download.visualstudio.microsoft.com" or "download.microsoft.com");

    /// <summary>The caller supplies two explicit choices: download, then execute the verified Microsoft installer.</summary>
    internal static async Task<ComponentInstallResult> InstallVisualCppAsync(Func<bool> allowDownload, Func<bool> allowInstall,
        HttpClient http, Func<string, bool>? verify = null, Func<string, CancellationToken, Task<int>>? execute = null,
        CancellationToken cancellation = default)
    {
        if (!allowDownload()) return ComponentInstallResult.Declined;
        cancellation.ThrowIfCancellationRequested();
        string folder = Path.Combine(Path.GetTempPath(), "RetroEmu-component-" + Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(folder, "vc_redist.x64.exe");
        try
        {
            Directory.CreateDirectory(folder);
            await DownloadAsync(http, payload, cancellation);
            // Deny replacement while identity is checked and the approved process runs.
            using var held = new FileStream(payload, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!(verify ?? MicrosoftSignature.Verify)(payload)) throw new InvalidDataException(UiText.Get("El instalador no tiene una firma Microsoft válida. No se ejecutó."));
            cancellation.ThrowIfCancellationRequested();
            if (!allowInstall()) return ComponentInstallResult.Declined;
            cancellation.ThrowIfCancellationRequested();
            int result = await (execute ?? RunInstaller)(payload, cancellation);
            return result switch { 0 => ComponentInstallResult.Installed, 1638 => ComponentInstallResult.AlreadyPresent, 3010 or 1641 => ComponentInstallResult.RestartRequired,
                _ => throw new IOException(UiText.Format($"El instalador oficial terminó con código {result}.")) };
        }
        finally
        {
            // Only these two paths, generated here. Never recursively delete an installation or a caller's folder.
            try { File.Delete(payload); Directory.Delete(folder, recursive: false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task DownloadAsync(HttpClient http, string destination, CancellationToken cancellation)
    {
        Uri address = VisualCppSource;
        for (int redirect = 0; redirect < 6; redirect++)
        {
            if (!AllowedDownload(address)) throw new InvalidDataException(UiText.Get("La descarga salió de los servidores oficiales de Microsoft. Se canceló."));
            using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (response.RequestMessage?.RequestUri is { } actual && !AllowedDownload(actual)) throw new InvalidDataException(UiText.Get("La descarga salió de los servidores oficiales de Microsoft. Se canceló."));
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther)
            {
                var next = response.Headers.Location ?? throw new InvalidDataException("Missing redirect location.");
                address = next.IsAbsoluteUri ? next : new Uri(address, next); continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumInstallerBytes) throw new InvalidDataException(UiText.Get("El instalador excede el tamaño permitido. Se canceló la descarga."));
            await using var input = await response.Content.ReadAsStreamAsync(cancellation);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            byte[] buffer = new byte[81920]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
            {
                total += read;
                if (total > MaximumInstallerBytes) throw new InvalidDataException(UiText.Get("El instalador excede el tamaño permitido. Se canceló la descarga."));
                await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
            }
            if (total == 0 || response.Content.Headers.ContentLength is { } expected && total != expected) throw new InvalidDataException(UiText.Get("La descarga del componente quedó incompleta. No se ejecutó."));
            return;
        }
        throw new InvalidDataException(UiText.Get("Demasiadas redirecciones al descargar el componente oficial."));
    }

    private static async Task<int> RunInstaller(string path, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var start = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("/install"); start.ArgumentList.Add("/norestart");
        using var process = Process.Start(start) ?? throw new IOException(UiText.Get("No se pudo abrir el instalador oficial."));
        // Once elevated, Windows and the official installer own cancellation; do not terminate a system installation.
        await process.WaitForExitAsync(); return process.ExitCode;
    }
}

/// <summary>Authenticode chain, code-signing policy and publisher organization; no UI or certificate download during the check.</summary>
internal static class MicrosoftSignature
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo { public uint Size; public nint Name, File, Subject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size; public nint Policy, Sip; public uint Ui, Revocation, Union; public nint Info;
        public uint StateAction; public nint State, Url; public uint Flags, Context; public nint Signature;
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptQueryObject(uint kind, string file, uint content, uint format, uint flags,
        out uint encoding, out uint actualContent, out uint actualFormat, out nint store, out nint message, nint context);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptMsgGetParam(nint message, uint parameter, uint index, nint data, ref uint size);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern nint CertFindCertificateInStore(nint store, uint encoding, uint flags, uint type, nint find, nint previous);
    [DllImport("crypt32.dll")] private static extern bool CryptMsgClose(nint message);
    [DllImport("crypt32.dll")] private static extern bool CertCloseStore(nint store, uint flags);
    [DllImport("crypt32.dll")] private static extern bool CertFreeCertificateContext(nint certificate);
    [StructLayout(LayoutKind.Sequential)] private struct CertificateContext { public uint Encoding; public nint Data; public uint Size; public nint Info, Store; }

    private static bool MicrosoftPublisher(string path)
    {
        if (!CryptQueryObject(1, path, 1 << 10, 2, 0, out _, out _, out _, out nint store, out nint message, 0)) return false;
        nint info = 0, certificate = 0;
        try
        {
            uint size = 0;
            if (!CryptMsgGetParam(message, 7, 0, 0, ref size) || size is 0 or > 1048576) return false;
            info = Marshal.AllocHGlobal((int)size);
            if (!CryptMsgGetParam(message, 7, 0, info, ref size)) return false;
            certificate = CertFindCertificateInStore(store, 0x10001, 0, 0xB0000, info, 0);
            if (certificate == 0) return false;
            var context = Marshal.PtrToStructure<CertificateContext>(certificate);
            if (context.Size is 0 or > 1048576) return false;
            byte[] encoded = new byte[context.Size]; Marshal.Copy(context.Data, encoded, 0, encoded.Length);
            using var parsed = X509CertificateLoader.LoadCertificate(encoded);
            return parsed.SubjectName.Decode(X500DistinguishedNameFlags.UseNewLines).Split('\n')
                .Any(part => part.Trim().Equals("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (certificate != 0) CertFreeCertificateContext(certificate);
            if (info != 0) Marshal.FreeHGlobal(info);
            if (message != 0) CryptMsgClose(message);
            if (store != 0) CertCloseStore(store, 0);
        }
    }

    internal static bool Verify(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        nint name = Marshal.StringToCoTaskMemUni(path), info = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Union = 1, Info = info, StateAction = 1,
            Flags = 0x1000 | 0x80 }; // Cache-only retrieval; check the chain, excluding the root.
        try
        {
            Marshal.StructureToPtr(new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Name = name }, info, false);
            if (WinVerifyTrust(new nint(-1), ref action, ref data) != 0) return false;
            return MicrosoftPublisher(path);
        }
        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            data.StateAction = 2; WinVerifyTrust(new nint(-1), ref action, ref data);
            Marshal.FreeHGlobal(info); Marshal.FreeCoTaskMem(name);
        }
    }
}
