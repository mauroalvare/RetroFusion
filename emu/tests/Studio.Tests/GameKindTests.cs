using Retro.Ai;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture, NonParallelizable]
public sealed class GameKindTests
{
    [Test]
    public void LegacyGamesKeepTheirCategory_AndInspiredGamesLiveWithCreationsWithoutHidingOriginals()
    {
        string beforeFusion = StudioLibrary.UserFolder, beforeCreated = CreatedGames.UserFolder;
        string folder = Path.Combine(Path.GetTempPath(), "retro-modalidades-" + Guid.NewGuid());
        try
        {
            StudioLibrary.UserFolder = Path.Combine(folder,"fusionados"); CreatedGames.UserFolder = Path.Combine(folder,"creados");
            var legacy = StudioGame.Parse("""{"id":"legado-propio","title":"Legado","script":"function draw() {}"}""");
            Assert.That(legacy.IsInspired, Is.False, "BaseSlot null no reclasifica un juego viejo");
            StudioLibrary.Save(legacy);
            var inspired = legacy.Copy(); inspired.Id = "creacion-propia"; inspired.Kind = "inspired";
            string path = StudioLibrary.Save(inspired);
            Assert.That(path, Does.StartWith(CreatedGames.UserFolder));
            Assert.That(File.Exists(Path.ChangeExtension(path,".js")), Is.True);
            var games = StudioLibrary.AllVersions();
            Assert.That(games.Any(g=>g.Id==legacy.Id&&!g.IsInspired), Is.True);
            Assert.That(games.Any(g=>g.Id==inspired.Id&&g.IsInspired), Is.True);
            Assert.That(StudioLibrary.UniqueId(inspired.Id), Is.Not.EqualTo(inspired.Id));
            Assert.That(StudioLibrary.BuiltIn.Single(g=>g.Id=="manic-invaders").IsInspired, Is.False);
            var imported = StudioDesigner.ParseGame("""{"kind":"fusion","baseSlot":"a","script":"function draw() {}"}""", inspired, out _)!;
            Assert.That(imported.IsInspired, Is.True, "la respuesta no decide la modalidad");
        }
        finally
        {
            StudioLibrary.UserFolder=beforeFusion; CreatedGames.UserFolder=beforeCreated;
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
}
