using System.Text.Json;
using Retro.Ai;
using Retro.Fusion;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture, NonParallelizable]
public sealed class LibraryPersistenceTests
{
    private string _root = "", _before = "", _createdBefore = "";
    [SetUp]
    public void Setup()
    {
        _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-persistence-" + Guid.NewGuid())).FullName;
        _before = StudioLibrary.UserFolder; _createdBefore = CreatedGames.UserFolder;
        StudioLibrary.UserFolder = Path.Combine(_root, "fusion"); CreatedGames.UserFolder = Path.Combine(_root, "created");
    }
    [TearDown]
    public void Cleanup()
    {
        StudioLibrary.UserFolder = _before; CreatedGames.UserFolder = _createdBefore;
        Assert.That(Path.GetDirectoryName(Path.GetFullPath(_root)), Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
        Assert.That(Path.GetFileName(_root), Does.StartWith("retro-persistence-"));
        Directory.Delete(_root, true);
    }
    private static StudioGame Game() => new()
    {
        Id = "persist-own", Title = "Juego propio", Script = "function init(){} function tick(input){} function draw(){}",
        WorkId = Guid.NewGuid().ToString("N"), Games = [new() { Slot = "a", Profile = "one" }, new() { Slot = "b", Profile = "two" }], BaseSlot = "a"
    };
    [TestCase("title")]
    [TestCase("pitch")]
    [TestCase("approach")]
    [TestCase("howToPlay")]
    [TestCase("design")]
    [TestCase("request")]
    [TestCase("changes")]
    public void AChangedCardFromTheSameWorkIsSavedEvenWhenTheScriptIsUnchanged(string field)
    {
        var original = Game(); string oldPath = StudioLibrary.Save(original); byte[] oldBytes = File.ReadAllBytes(oldPath);
        var revised = original.Copy();
        var property = typeof(StudioGame).GetProperties().Single(p => string.Equals(p.Name, field, StringComparison.OrdinalIgnoreCase));
        property.SetValue(revised, "Dato nuevo de la ficha");
        string path = StudioLibrary.Save(revised);
        Assert.That(path, Is.Not.EqualTo(oldPath));
        Assert.That(revised.Version, Is.EqualTo(original.Version + 1));
        Assert.That(File.ReadAllBytes(oldPath), Is.EqualTo(oldBytes));
        Assert.That(property.GetValue(StudioLibrary.Versions(original.Id).Last()), Is.EqualTo("Dato nuevo de la ficha"));
    }

    [Test]
    public void CorrectedPlaytestsAreNotLostWhenTheirScriptAlreadyExists()
    {
        var game = Game(); game.Playtests = [new() { Name = "Entrada", Steps = [new() { Ticks = 1 }],
            Checks = [new() { Slot = "a", Variable = "counter", FromTick = 0, AtTick = 1, Min = 1, Max = 1 }] }];
        StudioLibrary.Save(game);
        var changed = game.Copy(); changed.Playtests[0].Checks[0].Max = 2;
        StudioLibrary.Save(changed);
        Assert.That(StudioLibrary.Versions(game.Id), Has.Count.EqualTo(2));
        Assert.That(StudioLibrary.Versions(game.Id).Last().Playtests[0].Checks[0].Max, Is.EqualTo(2));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ChangedOriginalsOrBaseCannotReuseAnOlderSavedBinding(bool changeBase)
    {
        var game = Game(); StudioLibrary.Save(game);
        var changed = game.Copy();
        if (changeBase) changed.BaseSlot = "b"; else changed.Games[1].Profile = "different-edition";
        StudioLibrary.Save(changed);
        Assert.That(StudioLibrary.Versions(game.Id), Has.Count.EqualTo(2));
        var last = StudioLibrary.Versions(game.Id).Last();
        Assert.That(changeBase ? last.BaseSlot : last.Games[1].Profile, Is.EqualTo(changeBase ? "b" : "different-edition"));
    }

    [Test]
    public void AnIdenticalRetryKeepsItsSavedIdAndVersionDespiteNewAccounting()
    {
        var game = Game(); string path = StudioLibrary.Save(game);
        var retry = game.Copy(); retry.Id = "another-proposal"; retry.Version = 42; retry.Created = retry.Created.AddHours(1);
        retry.Usage = new AiOperationStats(3, 0, 500, 0, 0, null);
        Assert.That(StudioLibrary.Save(retry), Is.EqualTo(path));
        Assert.That(retry.Id, Is.EqualTo(game.Id)); Assert.That(retry.Version, Is.EqualTo(game.Version));
    }

    [Test]
    public void RetryingASavedGameFoundInAnotherLibraryReturnsItsActualExistingPath()
    {
        var game = Game(); string saved = StudioLibrary.Save(game);
        CreatedGames.UserFolder = StudioLibrary.UserFolder;
        StudioLibrary.UserFolder = Path.Combine(_root, "new-fusion");
        var retry = game.Copy();
        string path = StudioLibrary.Save(retry);
        Assert.That(File.Exists(path), Is.True, "Recovery must not report a synthesized location as a successful save.");
        Assert.That(path, Is.EqualTo(saved));
    }

    [Test]
    public void AnUnsafeIdInAnOlderCardCannotRedirectARecoveredSave()
    {
        var game = Game(); string saved = StudioLibrary.Save(game);
        var card = StudioGame.Parse(File.ReadAllText(saved)); card.Id = "../escaped";
        File.WriteAllText(saved, card.ToJson());
        var revised = game.Copy(); revised.Script += "\n// new revision";
        Assert.DoesNotThrow(() => StudioLibrary.Save(revised));
        Assert.That(Directory.Exists(Path.Combine(_root, "escaped")), Is.False);
        Assert.That(revised.Id, Is.EqualTo(game.Id));
    }

    [Test]
    public void AChangedRecoveredSourceStaysInTheSameGameInsteadOfCreatingAnotherFolder()
    {
        var game = Game(); StudioLibrary.Save(game);
        var retry = game.Copy(); retry.Id = "another-proposal"; retry.Script += "\n// corrected source";
        StudioLibrary.Save(retry);
        Assert.That(retry.Id, Is.EqualTo(game.Id)); Assert.That(retry.Version, Is.EqualTo(2));
        Assert.That(StudioLibrary.Versions(game.Id), Has.Count.EqualTo(2));
    }

    [Test]
    public void AnEmptyWorkIdDoesNotMergeIndependentCreations()
    {
        var game = Game(); game.WorkId = ""; StudioLibrary.Save(game);
        var other = game.Copy(); other.Id = "independent"; StudioLibrary.Save(other);
        Assert.That(other.Id, Is.EqualTo("independent"));
        Assert.That(StudioLibrary.AllVersions().Count(g => g.Id == game.Id || g.Id == other.Id), Is.EqualTo(2));
    }

    [TestCase("js")]
    [TestCase("json")]
    public void PreexistingIncompleteVersionFilesArePreserved(string extension)
    {
        var game = Game(); string dir = Path.Combine(StudioLibrary.UserFolder, game.Id); Directory.CreateDirectory(dir);
        string occupied = Path.Combine(dir, "v1." + extension); const string marker = "Un archivo previo que debe conservarse"; File.WriteAllText(occupied, marker);
        string saved = StudioLibrary.Save(game);
        Assert.That(game.Version, Is.EqualTo(2));
        Assert.That(File.ReadAllText(occupied), Is.EqualTo(marker));
        Assert.That(StudioGame.Parse(File.ReadAllText(saved)).Version, Is.EqualTo(2));
    }

    [Test]
    public void AFailurePublishingTheScriptDoesNotPublishItsMetadata()
    {
        var game = Game(); string dir = Path.Combine(StudioLibrary.UserFolder, game.Id);
        Directory.CreateDirectory(Path.Combine(dir, "v1.js"));
        Assert.Catch<IOException>(() => StudioLibrary.Save(game));
        Assert.That(File.Exists(Path.Combine(dir, "v1.json")), Is.False);
        Assert.That(Directory.GetFiles(dir, "*.tmp"), Is.Empty);
    }

    [TestCase("..")]
    [TestCase(".")]
    [TestCase("../escaped")]
    [TestCase("..\\escaped")]
    [TestCase("absolute")]
    public void StorageIdsCannotEscapeTheSelectedLibrary(string id)
    {
        var game = Game(); game.Id = id == "absolute" ? Path.Combine(_root, "escaped") : id;
        Assert.Throws<InvalidDataException>(() => StudioLibrary.Save(game));
        Assert.That(File.Exists(Path.Combine(_root, "v1.json")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(_root, "escaped")), Is.False);
    }

    [Test]
    public void CorruptStudioWorkDoesNotHideAnotherRecoverableJob()
    {
        var good = new StudioWork { State = new() { Template = Game(), Brief = "Pedido propio" } };
        StudioWorkStore.Save(good);
        var bad = new StudioWork { State = new() { Template = Game(), Brief = "Ficha dañada" } }; bad.State.Template.Kind = "not-a-kind";
        string path = Path.Combine(StudioWorkStore.Folder, bad.Id + ".json"); string json = JsonSerializer.Serialize(bad, GameProfile.Json); File.WriteAllText(path, json);
        Assert.That(StudioWorkStore.Pending().Select(w => w.Id), Is.EquivalentTo(new[] { good.Id }));
        Assert.That(File.ReadAllText(path), Is.EqualTo(json));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NewerBrokenFilesDoNotPushAValidJobOutOfRecovery(bool studio)
    {
        string folder, id;
        if (studio)
        {
            var good = new StudioWork { State = new() { Template = Game(), Brief = "Pedido propio" } }; StudioWorkStore.Save(good);
            folder = StudioWorkStore.Folder; id = good.Id;
        }
        else
        {
            var good = new GameWork { State = new() { Machine = "c64", Wish = "Pedido propio", Brief = "Consigna completa" } }; GameWorkStore.Save(good);
            folder = GameWorkStore.Folder; id = good.Id;
        }
        File.SetLastWriteTimeUtc(Path.Combine(folder, id + ".json"), DateTime.UtcNow.AddHours(-2));
        for (int i = 0; i < 31; i++) File.WriteAllText(Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json"), "{");
        var found = studio ? StudioWorkStore.Pending().Select(w => w.Id) : GameWorkStore.Pending().Select(w => w.Id);
        Assert.That(found, Does.Contain(id));
    }
}
