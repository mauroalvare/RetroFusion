using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Retro.Update;

namespace Update.Tests;

[TestFixture]
public class PackageTests
{
    private string _folder = "";
    [SetUp] public void Setup() { _folder = Path.Combine(Path.GetTempPath(), "RetroEmu-package-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_folder); }
    [TearDown] public void Cleanup() { Directory.Delete(_folder, true); }

    internal static PackageManifest OwnPackage(string root, string version = "0.2.2")
    {
        Directory.CreateDirectory(root); var manifest = new PackageManifest { Version = version, Runtime = "win-x64" };
        foreach (string name in new[] { "RetroEmu.exe", "RetroEmu.dll", "RetroEmu.runtimeconfig.json", "RetroEmu.deps.json", "ROMs/LEEME.txt", "firmware/48.rom" })
        {
            byte[] bytes = Encoding.UTF8.GetBytes("Archivo propio de prueba " + name + " " + version);
            string path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
            manifest.Files[name] = Convert.ToHexString(SHA256.HashData(bytes));
        }
        File.WriteAllText(Path.Combine(root, PackageManifest.FileName), JsonSerializer.Serialize(manifest, PackageManifest.Json));
        return manifest;
    }

    private (string zip, ReleaseAsset asset) Zip()
    {
        string root = Path.Combine(_folder, "source", "RetroEmu-0.2.2-win-x64"); OwnPackage(root);
        string zip = Path.Combine(_folder, "package.zip"); ZipFile.CreateFromDirectory(Path.GetDirectoryName(root)!, zip);
        byte[] data = File.ReadAllBytes(zip);
        return (zip, new(new(0, 2, 2), "win-x64", "package.zip", new Uri("https://github.com/own.zip"), data.Length, Convert.ToHexString(SHA256.HashData(data))));
    }

    [Test]
    public void ExtractsAndVerifiesCompleteOwnPackage()
    {
        var (zip, asset) = Zip(); string result = SafePackage.Extract(zip, Path.Combine(_folder, "stage"), asset, CancellationToken.None);
        Assert.That(Path.GetFileName(result), Is.EqualTo("RetroEmu-0.2.2-win-x64"));
        PackageManifest.Read(result).Verify(result, exact: true);
        Assert.Throws<IOException>(() => SafePackage.Extract(zip, Path.Combine(_folder, "stage"), asset, CancellationToken.None));
    }

    [TestCase("RetroEmu-0.2.2-win-x64/../escape.txt")]
    [TestCase("RetroEmu-0.2.2-win-x64/C:/escape.txt")]
    [TestCase("otra/escape.txt")]
    [TestCase("RetroEmu-0.2.2-win-x64/CON.txt")]
    [TestCase("RetroEmu-0.2.2-win-x64/RetroEmu.DLL")]
    public void RejectsUnsafeAndCaseCollidingEntries(string name)
    {
        var (zip, asset) = Zip();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write("Contenido propio"); }
        Assert.Throws<InvalidDataException>(() => SafePackage.Extract(zip, Path.Combine(_folder, "stage"), asset, CancellationToken.None));
        Assert.That(File.Exists(Path.Combine(_folder, "escape.txt")), Is.False);
    }

    [Test]
    public void RejectsLinksAndUndeclaredFiles()
    {
        var (zip, asset) = Zip();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) { var link = archive.CreateEntry("RetroEmu-0.2.2-win-x64/link"); link.ExternalAttributes = unchecked((int)0xA1FF0000); }
        Assert.Throws<InvalidDataException>(() => SafePackage.Extract(zip, Path.Combine(_folder, "links"), asset, CancellationToken.None));
        string own = Path.Combine(_folder, "standalone"); var manifest = OwnPackage(own); File.WriteAllText(Path.Combine(own, "extra.txt"), "Dato propio");
        Assert.Throws<InvalidDataException>(() => manifest.Verify(own, exact: true));
    }

    [Test]
    public void ChangedFirmwareIsPreservedInInstalledValidationButNotInDownload()
    {
        var manifest = OwnPackage(_folder); File.WriteAllText(Path.Combine(_folder, "firmware/48.rom"), "Firmware de prueba del usuario");
        Assert.DoesNotThrow(() => manifest.Verify(_folder, exact: false));
        Assert.Throws<InvalidDataException>(() => manifest.Verify(_folder, exact: true));
        File.WriteAllText(Path.Combine(_folder, "RetroEmu.dll"), "Binario cambiado");
        Assert.Throws<InvalidDataException>(() => manifest.Verify(_folder, exact: false));
    }
}
