using System.IO.Compression;
using Retro.Compat;
using Retro.Core;
using Retro.Machines;

namespace Compat.Tests;

[TestFixture]
public sealed class RomOrganizerTests
{
    private string _root = "";
    [SetUp] public void Setup() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-organizer-" + Guid.NewGuid())).FullName;
    [TearDown] public void Cleanup() => Directory.Delete(_root, true);
    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, new DateTime(2023, 5, 4, 3, 2, 0, DateTimeKind.Utc));
        return path;
    }
    private static byte[] Nes()
    {
        var bytes = new byte[16 + 16384 + 8192];
        new byte[] { 78, 69, 83, 26, 1, 1 }.CopyTo(bytes, 0);
        return bytes;
    }
    private string Zip(string name, params (string Name, byte[] Data)[] entries)
    {
        string path = Path.Combine(_root, name);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var entry in entries)
                using (var stream = archive.CreateEntry(entry.Name).Open()) stream.Write(entry.Data);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
        return path;
    }

    [Test]
    public void ContentWinsOverTheName_AndCollisionsKeepBytesDatesPicturesAndProfileIdentity()
    {
        byte[] original = [1, 8, 0x60, 44];
        string path = Write("Juego.prg", original);
        string picture = Write("Juego.png", [11, 22, 33]);
        string collision = Write("Commodore 64/Juego.prg", [1, 8, 0x60, 55]);
        var before = GameCatalog.Scan(_root).Single(g => g.Path == path).Profile;
        DateTime written = File.GetLastWriteTimeUtc(path), created = File.GetCreationTimeUtc(path);
        string misleading = Write("Parece_de_Spectrum.rom", Nes());
        var result = RomOrganizer.Organize(_root);
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Moved, Has.Count.EqualTo(2));
        string moved = result.Moved.Single(m => m.From == path).To;
        Assert.That(moved, Is.EqualTo(Path.Combine(_root, "Commodore 64", "Juego (2).prg")));
        Assert.That(File.ReadAllBytes(moved), Is.EqualTo(original));
        Assert.That(File.GetLastWriteTimeUtc(moved), Is.EqualTo(written));
        Assert.That(File.GetCreationTimeUtc(moved), Is.EqualTo(created));
        Assert.That(File.ReadAllBytes(collision), Is.EqualTo(new byte[] { 1, 8, 0x60, 55 }));
        Assert.That(File.ReadAllBytes(Path.ChangeExtension(moved, ".png")), Is.EqualTo(new byte[] { 11, 22, 33 }));
        Assert.That(File.Exists(picture), Is.False);
        Assert.That(GameCatalog.Scan(_root).Single(g => g.Path == moved).Profile.Id, Is.EqualTo(before.Id));
        var prepared = GamePreparer.BuildProfile(new PrepareResult { Game = GamePreparer.GameNameOf(moved, "c64"), Machine = "c64",
            Sha256 = before.Match.Sha256.Single(), File = Path.GetFileName(moved) }, "Commodore 64", new LabReport(),
            new ControlSet("Prueba", "L", "R", "U", "D", "F"), sourcePath: moved);
        Assert.That(prepared.Id, Is.EqualTo(before.Id), "Preparar después de ordenar tampoco cambia la identidad usada por las fusiones");
        Assert.That(result.Moved.Single(m => m.From == misleading).Machine, Is.EqualTo("nes"));
        Assert.That(RomOrganizer.Organize(_root).Moved, Is.Empty, "no vuelve a recorrer ni mover las carpetas organizadas");
    }

    [Test]
    public void MixedArchivesWeakFormatsAndPartialFilesRemainInPlace()
    {
        string mixed = Zip("Mezclados.zip", ("demo.nes", Nes()), ("demo.prg", [1, 8, 0x60]));
        string weak = Write("posible.com", [0xC3, 0x00, 0x01]);
        string partial = Write("copia.nes", Nes()[..1024]);
        string snapshot = Write("incompleto.z80", new byte[40]);
        string chip = Write("invaders.h", new byte[2048]);
        string curated = Write("Mi carpeta personal/juego.prg", [1, 8, 0x60]);
        byte[] windows = new byte[512]; windows[0] = 77; windows[1] = 90; windows[4] = 1; windows[8] = 4; windows[60] = 128;
        windows[128] = (byte)'P'; windows[129] = (byte)'E';
        string exe = Write("instalador.exe", windows);
        var paths = new[] { mixed, weak, partial, snapshot, chip, curated, exe };
        var hashes = paths.ToDictionary(p => p, File.ReadAllBytes);
        var result = RomOrganizer.Organize(_root);
        Assert.That(result.Moved, Is.Empty);
        Assert.That(result.Unclassified, Is.EqualTo(6));
        foreach (string path in paths) Assert.That(File.ReadAllBytes(path), Is.EqualTo(hashes[path]), path);
        Assert.That(MachineCatalog.Identify(new("instalador.exe", windows, "prueba")).Any(c => c.Confidence >= 80), Is.False);
    }

    [Test]
    public void CompleteArcadeSetsMoveTogether_ButAnExtraDifferentSystemMakesTheZipAmbiguous()
    {
        var chips = new[] { "invaders.h", "invaders.g", "invaders.f", "invaders.e" }.Select(n => (n, new byte[2048])).ToArray();
        string complete = Zip("Arcade.zip", chips);
        string mixed = Zip("Arcade_y_consola.zip", chips.Append(("demo.nes", Nes())).ToArray());
        string nested = Zip("Consola.zip", ("Carpeta/demo.nes", Nes()), ("README.txt", new byte[] { 65 }));
        var result = RomOrganizer.Organize(_root);
        Assert.That(result.Moved.Select(m => m.Machine), Is.EquivalentTo(new[] { "invaders", "nes" }));
        Assert.That(result.Moved.Single(m => m.From == complete).To, Does.Contain("Arcade - Space Invaders"));
        Assert.That(result.Moved.Single(m => m.From == nested).To, Does.Contain("Nintendo NES"));
        Assert.That(File.Exists(mixed), Is.True);
    }

    [Test]
    public void CopiesAndFilesInUseAreDeferred_ThenMoveWhenReleased()
    {
        string path = Write("Copiando.prg", [1, 8, 0x60]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        Assert.That(RomOrganizer.Organize(_root).Deferred, Is.EqualTo(1));
        using (var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var result = RomOrganizer.Organize(_root, TimeSpan.Zero);
            Assert.That(result.Moved, Is.Empty);
            Assert.That(result.Deferred, Is.EqualTo(1));
            Assert.That(File.Exists(path), Is.True);
        }
        Assert.That(RomOrganizer.Organize(_root, TimeSpan.Zero, inUse: p => p == path).Deferred, Is.EqualTo(1), "tampoco mueve el juego abierto aunque la emulación ya tenga sus bytes en RAM");
        Assert.That(RomOrganizer.Organize(_root, TimeSpan.Zero).Moved, Has.Count.EqualTo(1));
    }

    [Test]
    public void BrokenMetadataOrAnUnusableDestinationNeverRemovesTheOriginal()
    {
        string path = Write("Seguro.prg", [1, 8, 0x60]);
        string metadata = Write(".retroemu/identidades.json", "invalid JSON"u8.ToArray());
        Assert.That(RomOrganizer.Organize(_root).Error, Is.Not.Null);
        Assert.That(File.Exists(path), Is.True);
        File.Delete(metadata);
        Write("Commodore 64", [99]); // A file occupying the intended directory.
        var result = RomOrganizer.Organize(_root);
        Assert.That(result.Moved, Is.Empty);
        Assert.That(result.Deferred, Is.EqualTo(1));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1, 8, 0x60 }));
    }

    [Test]
    public void DosHeadersAreRecognizedButDoNotClaimTheMachineCanAlreadyRunThem()
    {
        byte[] dos = new byte[512]; dos[0] = 77; dos[1] = 90; dos[4] = 1; dos[8] = 2;
        string path = Write("Programa.exe", dos);
        var result = RomOrganizer.Organize(_root);
        Assert.That(result.Moved.Single().To, Is.EqualTo(Path.Combine(_root, "PC - DOS", "Programa.exe")));
        Assert.That(MachineCatalog.Get("pcxt").Status, Is.EqualTo(MachineStatus.Available));
        var candidate = MachineCatalog.Identify(new Retro.Core.MediaFile("Programa.exe", dos, path)).First(c => c.Machine.Id == "pcxt");
        Assert.That(MachineCatalog.NotLoadableYet(candidate), Does.Contain("DOS"));
    }
}
