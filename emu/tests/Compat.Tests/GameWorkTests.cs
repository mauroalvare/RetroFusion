using Retro.Ai;

namespace Compat.Tests;

[TestFixture, NonParallelizable]
public sealed class GameWorkTests
{
    [Test]
    public void WorkIsAtomicPortableAndSafeToDiscard_AndOneJobDoesNotSaveDuplicateGames()
    {
        string before=CreatedGames.UserFolder;
        string folder=Path.Combine(Path.GetTempPath(),"retro-creador-work-"+Guid.NewGuid());
        try
        {
            CreatedGames.UserFolder=folder;
            var work=new GameWork { State=new() { Machine="c64",Wish="Mantener la idea",Brief="Idea elegida",Draft=new("Propio","","","","source1"),Author="Prueba",Usage=new(1,0,1000,0,0,null) } };
            GameWorkStore.Save(work); work.State.Draft=work.State.Draft with { Source="source2" }; GameWorkStore.Save(work);
            var loaded=GameWorkStore.Pending().Single();
            Assert.That(loaded.State.Draft!.Source, Is.EqualTo("source2"));
            Assert.That(loaded.State.Wish, Is.EqualTo(work.State.Wish));
            Assert.That(loaded.State.Usage, Is.EqualTo(work.State.Usage));
            Assert.That(Directory.GetFiles(GameWorkStore.Folder), Has.Length.EqualTo(1));
            string json=File.ReadAllText(Directory.GetFiles(GameWorkStore.Folder).Single());
            Assert.That(json, Does.Not.Contain(GameDesignGuide.Compact).And.Not.Contain("apiKey"));
            var a=CreatedGames.Save(loaded.State.Draft,null,"c64",loaded.State.Wish,loaded.State.Author,workId:work.Id);
            var b=CreatedGames.Save(loaded.State.Draft with { Source="source3" },null,"c64",loaded.State.Wish,loaded.State.Author,workId:work.Id);
            Assert.That(a.Folder, Is.EqualTo(b.Folder));
            Assert.That(CreatedGames.All(), Has.Count.EqualTo(1));
            Assert.That(File.ReadAllText(b.SourcePath), Is.EqualTo("source3"));
            if (OperatingSystem.IsWindows())
                using (File.Open(b.SourcePath,FileMode.Open,FileAccess.Read,FileShare.None))
                    Assert.Throws(Is.TypeOf<IOException>().Or.TypeOf<UnauthorizedAccessException>(),()=>CreatedGames.Save(loaded.State.Draft with { Source="source4" },null,"c64",loaded.State.Wish,loaded.State.Author,workId:work.Id));
            Assert.That(File.ReadAllText(b.SourcePath), Is.EqualTo("source3"), "un archivo ocupado no recibe medio código");
            Assert.That(Directory.GetFiles(b.Folder,"*.tmp"), Is.Empty);
            File.WriteAllText(Path.Combine(GameWorkStore.Folder,new string('a',32)+".json"),"corte simulado");
            Assert.That(GameWorkStore.Pending(), Has.Count.EqualTo(1));
            Assert.Throws<ArgumentException>(()=>GameWorkStore.Remove("../otro"));
            GameWorkStore.Remove(work.Id); Assert.That(GameWorkStore.Pending(), Is.Empty);
            Assert.That(File.Exists(b.SourcePath), Is.True, "descartar un pendiente no borra el juego guardado");
        }
        finally { CreatedGames.UserFolder=before; if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }
}
