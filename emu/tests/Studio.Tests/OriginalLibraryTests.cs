using Retro.App;
using Retro.Compat;
using Retro.Fusion;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture]
[NonParallelizable]
public class OriginalLibraryTests
{
    [Test]
    public async Task DroppedRomsAreOrganizedAndAppearAutomatically_WithoutBreakingSavedFusions()
    {
        string root = Path.Combine(Path.GetTempPath(), "retro-drop-" + Guid.NewGuid());
        string dir = Directory.CreateDirectory(Path.Combine(root, "ROMs")).FullName;
        string previous = StudioLibrary.UserFolder;
        try
        {
            string source = Path.Combine(dir, "Original.prg");
            File.WriteAllBytes(source, [1, 8, 0x60, 77]);
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-1));
            Directory.CreateDirectory(Path.Combine(dir, "Commodore 64"));
            File.WriteAllBytes(Path.Combine(dir, "Commodore 64", "Original.prg"), [1, 8, 0x60, 88]);
            var catalog = GameCatalog.Scan(dir);
            StudioLibrary.UserFolder = Path.Combine(root, "fusiones");
            var fusion = new StudioGame { Id = "conservar", Title = "Conservar originales", BaseSlot = "a",
                Script = "function draw() {}",
                Games = catalog.Select((g, i) => new RecipeGame { Slot = i == 0 ? "a" : "b", Profile = g.Profile.Id }).ToList() };
            StudioLibrary.Save(fusion);
            using var library = new GameLibrary { AutoOrganize = true };
            library.Rescan(dir);
            async Task Until(Func<bool> ready)
            {
                for (int i = 0; i < 1500; i++)
                {
                    library.Poll();
                    if (ready()) return;
                    await Task.Delay(10);
                }
                Assert.Fail("La biblioteca no recibió el cambio: " + library.OrganizationError);
            }
            await Until(() => library.Originals().Count() == 2 && !library.Scanning);
            var available = library.Originals().Select(g => g.ProfileId).ToHashSet();
            Assert.That(StudioLibrary.Versions(fusion.Id).Single().ProfileIds.All(available.Contains), Is.True);
            Assert.That(library.Originals().All(g => File.Exists(g.Path)), Is.True);
            Assert.That(File.Exists(source), Is.False);
            string dropped = Path.Combine(dir, "Nuevo.tap");
            File.WriteAllBytes(dropped, [3, 0, 255, 0, 255]);
            await Until(() => library.Originals(machineId: "zx48").Any() && !library.Scanning);
            Assert.That(library.Originals(machineId: "zx48").Single().Path, Is.EqualTo(Path.Combine(dir, "ZX Spectrum", "Nuevo.tap")));
            Assert.That(library.Originals().Count(), Is.EqualTo(3));
            Assert.That(library.UnclassifiedCount, Is.Zero);
        }
        finally { StudioLibrary.UserFolder = previous; Directory.Delete(root, true); }
    }

    [Test]
    public async Task AutomaticOrganizationNeverMovesFilesFromTheFallbackFolder()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-fallback-" + Guid.NewGuid())).FullName;
        try
        {
            string path = Path.Combine(folder, "Propio.prg"); File.WriteAllBytes(path, [1, 8, 0x60]);
            using var library = new GameLibrary { AutoOrganize = true };
            library.Rescan(folder);
            for (int i = 0; i < 200 && library.Scanning; i++) await Task.Delay(10);
            library.Poll();
            Assert.That(library.Originals().Single().Path, Is.EqualTo(path));
            Assert.That(File.Exists(path), Is.True);
            Assert.That(library.OrganizedCount, Is.Zero);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Test]
    public async Task MachineSelectionAndSearch_KeepTheCompleteLibraryAvailable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "retro-machines-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string c64 = Path.Combine(dir, "Demo_C64.prg");
            string zx = Path.Combine(dir, "Demo_ZX.tap");
            string nes = Path.Combine(dir, "Demo_NES.nes");
            File.WriteAllBytes(c64, new byte[] { 1, 8, 0x60 });
            File.WriteAllBytes(zx, new byte[] { 3, 0, 255, 0, 255 });
            var cartridge = new byte[16 + 16384 + 8192];
            new byte[] { 0x4E, 0x45, 0x53, 0x1A, 1, 1 }.CopyTo(cartridge, 0);
            File.WriteAllBytes(nes, cartridge);
            var host = new EmulatorHost();
            host.StartNes(Retro.Machines.MachineCatalog.Get("nes"), new Retro.Core.MediaFile("cartucho.nes", cartridge, nes + "!cartucho.nes"));
            Assert.That(host.UsesMedia(nes), Is.True, "el archivo contenedor también queda protegido durante la partida");
            Assert.That(host.UsesMedia(zx), Is.False);
            var library = new GameLibrary();
            library.Rescan(dir);
            for (int i = 0; i < 200 && library.Scanning; i++) await Task.Delay(10);
            Assert.That(library.Scanning, Is.False);
            library.Poll();
            Assert.That(library.Originals(machineId: "c64").Single().Path, Is.EqualTo(c64));
            Assert.That(library.Originals(" dEmO ", "zx48").Single().Path, Is.EqualTo(zx));
            Assert.That(library.Originals(machineId: "nes").Single().Path, Is.EqualTo(nes));
            Assert.That(library.Originals("NES", "c64"), Is.Empty);
            Assert.That(library.Originals(machineId: "pcxt"), Is.Empty);
            Assert.That(library.Originals().Select(g => g.Path), Is.EquivalentTo(new[] { c64, zx, nes }),
                "volver a Todos conserva los tres originales");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public async Task SavingAFusion_KeepsBothOriginalsSearchableAndTheirFilesIntact()
    {
        string dir = Path.Combine(Path.GetTempPath(), "retro-originals-" + Guid.NewGuid().ToString("N"));
        string previous = StudioLibrary.UserFolder;
        Directory.CreateDirectory(dir);
        try
        {
            string first = Path.Combine(dir, "Spy_vs_Spy_C64_" + Guid.NewGuid().ToString("N") + ".prg");
            string second = Path.Combine(dir, "Otro_juego_" + Guid.NewGuid().ToString("N") + ".prg");
            byte[] bytes = { 1, 8, 17, 53, 99 };
            File.WriteAllBytes(first, bytes);
            File.WriteAllBytes(second, new byte[] { 1, 8, 28, 66, 103 });
            var library = new GameLibrary();
            async Task Scan()
            {
                library.Rescan(dir);
                for (int i = 0; i < 200 && library.Scanning; i++) await Task.Delay(10);
                Assert.That(library.Scanning, Is.False);
                library.Poll();
            }
            await Scan();
            Assert.That(library.Originals().Count(), Is.EqualTo(2));
            var catalog = GameCatalog.Scan(dir);
            StudioLibrary.UserFolder = Path.Combine(dir, "fusiones");
            StudioLibrary.Save(new StudioGame { Id = "mezcla", Title = "Un juego nuevo",
                Games = catalog.Select((g, i) => new RecipeGame { Slot = i == 0 ? "a" : "b", Profile = g.Profile.Id }).ToList(),
                Script = "function draw() {}" });
            await Scan();
            Assert.That(library.Originals().Count(), Is.EqualTo(2));
            Assert.That(library.Originals("sPy").Single().Path, Is.EqualTo(first));
            Assert.That(library.Originals("Otro").Single().Path, Is.EqualTo(second));
            Assert.That(library.Originals(machineId: "c64").Count(), Is.EqualTo(2));
            Assert.That(StudioLibrary.Versions("mezcla"), Has.Count.EqualTo(1));
            Assert.That(File.ReadAllBytes(first), Is.EqualTo(bytes));
        }
        finally
        {
            StudioLibrary.UserFolder = previous;
            Directory.Delete(dir, true);
        }
    }
}
