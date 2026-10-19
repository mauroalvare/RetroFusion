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
