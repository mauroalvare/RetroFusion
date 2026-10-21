using Retro.Core;

namespace Compat.Tests;

[TestFixture]
public class PlayerFoldersTests
{
    private string _root = "";
    [SetUp] public void Setup() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-folders-" + Guid.NewGuid())).FullName;
    [TearDown] public void Cleanup() => Directory.Delete(_root, true);

    [TestCase("ROMs")]
    [TestCase("Juegos fusionados")]
    [TestCase("Juegos creados")]
    public void PackageInstructions_DoNotHideExistingParentLibrary(string name)
    {
        string start = Directory.CreateDirectory(Path.Combine(_root, "Programa")).FullName;
        string empty = Directory.CreateDirectory(Path.Combine(start, name)).FullName;
        File.WriteAllText(Path.Combine(empty, "LEEME.txt"), "Ayuda");
        string library = Directory.CreateDirectory(Path.Combine(_root, name, "mi-juego")).FullName;
        string file = Path.Combine(library, "juego.bin");
        File.WriteAllText(file, "datos propios");
        Assert.That(PlayerFolders.Find(name, startDirectory: start), Is.EqualTo(Path.GetDirectoryName(library)));
        Assert.That(File.ReadAllText(file), Is.EqualTo("datos propios"));
        File.WriteAllText(Path.Combine(empty, "propio.bin"), "nuevo");
        Assert.That(PlayerFolders.Find(name, startDirectory: start), Is.EqualTo(empty));
    }

    [Test]
    public void DocumentsLibrary_WinsOverInstructions_ThenFreshInstallUsesPortableFolder()
    {
        string start = Directory.CreateDirectory(Path.Combine(_root, "Programa")).FullName;
        string portable = Directory.CreateDirectory(Path.Combine(start, "Juegos creados")).FullName;
        File.WriteAllText(Path.Combine(portable, "LEEME.txt"), "Ayuda");
        string fallback = Directory.CreateDirectory(Path.Combine(_root, "Documentos", "Juegos creados")).FullName;
        string data = Path.Combine(fallback, "juego.prg");
        File.WriteAllText(data, "juego");
        Assert.That(PlayerFolders.Find("Juegos creados", fallback, start), Is.EqualTo(fallback));
        File.Delete(data);
        Assert.That(PlayerFolders.Find("Juegos creados", fallback, start), Is.EqualTo(portable));
    }

    [Test]
    public void FirmwareAndMissingFolders_AreNotGameLibraries()
    {
        Directory.CreateDirectory(Path.Combine(_root, "roms"));
        File.WriteAllText(Path.Combine(_root, "roms", "48.rom"), "firmware");
        Assert.That(PlayerFolders.Find("ROMs", startDirectory: _root), Is.Null);
        string fallback = Path.Combine(_root, "Documentos", "Juegos creados");
        Assert.That(PlayerFolders.Find("Juegos creados", fallback, _root), Is.EqualTo(fallback));
        Assert.That(Directory.Exists(fallback), Is.False);
    }
}
