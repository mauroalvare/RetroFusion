using Retro.Ai;
using Retro.Asm;

namespace Compat.Tests;

/// <summary>The folder of created games: examples, saving what an AI wrote, the template and trying again after an edit.</summary>
[TestFixture]
[NonParallelizable]
public class CreatedGamesTests
{
    private string _firmware = "";
    private string _folder = "";
    private string _saved = "";

    [SetUp]
    public void UseATemporaryFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms (48.rom y c64/).");
        _firmware = Path.Combine(dir!.FullName, "roms");
        _saved = CreatedGames.UserFolder;
        _folder = Path.Combine(Path.GetTempPath(), "retroemu-creados-" + Guid.NewGuid().ToString("N"));
        CreatedGames.UserFolder = _folder;
    }

    [TearDown]
    public void Restore()
    {
        CreatedGames.UserFolder = _saved;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    [Test]
    public void TheExamples_AreCopiedOnce_ReadyToPlay()
    {
        Assert.That(CreatedGames.ExportExamples(_firmware), Is.EqualTo(2));
        Assert.That(CreatedGames.ExportExamples(_firmware), Is.EqualTo(0), "la segunda vez no pisa nada");
        var games = CreatedGames.All();
        Assert.That(games.Select(g => g.Title), Is.EquivalentTo(new[] { "Tormenta", "El jardín de los topos" }));
        foreach (var g in games)
        {
            Assert.That(g.Passed, Is.True, g.Trial);
            Assert.That(g.Author, Is.EqualTo(CreatedGames.ExampleAuthor));
            Assert.That(File.Exists(g.SourcePath) && File.Exists(g.ProgramPath) && File.Exists(g.PicturePath), Is.True, g.Folder);
        }
        Assert.That(games.Single(g => g.Machine == "zx48").ProgramPath, Does.EndWith(".tap"));
        Assert.That(File.Exists(Path.Combine(_folder, "LEEME.txt")), Is.True);
    }

    [Test]
    public void AnEditThatBreaksTheGame_ShowsInTheNextTrial()
    {
        var g = CreatedGames.FromTemplate("c64", _firmware);
        Assert.That(g.Passed, Is.True, g.Trial);
        File.WriteAllText(g.SourcePath, File.ReadAllText(g.SourcePath).Replace("lda $DC00", "lda #$FF"));
        var report = CreatedGames.TryAgain(g, _firmware);
        Assert.That(report.Ok, Is.False);
        var again = CreatedGames.All().Single();
        Assert.That(again.Passed, Is.False, "el json guarda la última prueba");
        Assert.That(again.Trial, Does.Contain("[FALLA] controles"));
    }

    [Test]
    public void TwoGamesWithTheSameTitle_GetTheirOwnFolders()
    {
        var draft = new GameDraft("Ñandú veloz", "corre", "joystick", "", Templates.For("c64")!.Source);
        var a = CreatedGames.Save(draft, null, "c64", "un ñandú", "IA de prueba");
        var b = CreatedGames.Save(draft, null, "c64", "otro ñandú", "IA de prueba");
        Assert.That(Path.GetFileName(a.Folder), Is.EqualTo("nandu-veloz-c64"));
        Assert.That(Path.GetFileName(b.Folder), Is.EqualTo("nandu-veloz-c64-2"));
        Assert.That(CreatedGames.All().Select(g => g.Request), Is.EquivalentTo(new[] { "un ñandú", "otro ñandú" }));
    }
}
