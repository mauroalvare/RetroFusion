using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Retro.Core;

namespace Fusion.Tests;

[TestFixture]
public class LibraryActivityTests
{
    private string _root = "", _path = "";
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Next() => _now = _now.AddSeconds(1);
    }
    private static string Key(int n, string kind = "original") => LibraryKey.Named(kind, n.ToString());

    [SetUp] public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "RetroEmu-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root); _path = Path.Combine(_root, "biblioteca.json");
    }
    [TearDown] public void Cleanup() => Directory.Delete(_root, true);

    [Test]
    public void FavoriteAndRecentSurviveRestartAndClearingKeepsFavorites()
    {
        var clock = new Clock(); var data = new LibraryActivity(_path, clock);
        data.SetFavorite(Key(1), true); data.Played(Key(1)); clock.Next(); data.Played(Key(2));
        data = new LibraryActivity(_path);
        Assert.That(data.Recents, Is.EqualTo(new[] { Key(2), Key(1) }));
        Assert.That(data.IsFavorite(Key(1)), Is.True);
        data.ClearRecents(); data = new LibraryActivity(_path);
        Assert.That(data.Recents, Is.Empty);
        Assert.That(data.IsFavorite(Key(1)), Is.True);
        data.SetFavorite(Key(1), false);
        Assert.That(new LibraryActivity(_path).IsFavorite(Key(1)), Is.False);
    }

    [Test]
    public void ReplayingMovesOneEntryToTopAndOldRecentsAreBoundedWithoutLosingFavorites()
    {
        var clock = new Clock(); var data = new LibraryActivity(_path, clock);
        data.SetFavorite(Key(0), true);
        for (int n = 0; n < LibraryActivity.RecentLimit + 5; n++) { data.Played(Key(n)); clock.Next(); }
        Assert.That(data.Recents.Count, Is.EqualTo(LibraryActivity.RecentLimit));
        Assert.That(data.IsFavorite(Key(0)), Is.True); Assert.That(data.LastPlayed(Key(0)), Is.Null);
        data.Played(Key(20));
        data = new LibraryActivity(_path);
        Assert.That(data.Recents.First(), Is.EqualTo(Key(20)));
        Assert.That(data.Recents.Count, Is.EqualTo(LibraryActivity.RecentLimit));
        Assert.That(data.Recents.Distinct().Count(), Is.EqualTo(data.Recents.Count));
        Assert.That(data.IsFavorite(Key(0)), Is.True);
    }

    [Test]
    public void OriginalIdentityIgnoresFolderNameZipPackagingAndMemberOrder()
    {
        byte[] bytes = [0, 1, 2, 3, 4];
        var raw = new MediaFile("old.prg", bytes, @"C:\old\ROMs\old.prg");
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("leeme.txt").Open())) writer.Write("ayuda");
            using var payload = zip.CreateEntry("renamed.prg").Open(); payload.Write(bytes);
        }
        var packed = MediaArchive.Open(stream.ToArray(), "otro.zip", "/new/version/ROMs/c64/otro.zip");
        Assert.That(LibraryKey.Original("c64", packed), Is.EqualTo(LibraryKey.Original("c64", [raw])));
        var chip = new MediaFile("a.rom", [9, 8, 7], "fixture");
        Assert.That(LibraryKey.Original("arcade", [raw, chip]),
            Is.EqualTo(LibraryKey.Original("arcade", [chip with { Name = "b.rom" }, raw])));
    }

    [Test]
    public void MachineContentAndGameKindsCannotSharePreferences()
    {
        var rom = new MediaFile("a.bin", [1, 2], "fixture");
        Assert.That(LibraryKey.Original("c64", [rom]), Is.Not.EqualTo(LibraryKey.Original("nes", [rom])));
        Assert.That(LibraryKey.Original("c64", [rom]), Is.Not.EqualTo(LibraryKey.Original("c64", [rom with { Data = [1, 3] }])));
        var data = new LibraryActivity(_path); data.SetFavorite(Key(1, "fusion"), true);
        Assert.That(data.IsFavorite(Key(1, "created")), Is.False);
        Assert.That(data.IsFavorite(Key(1)), Is.False);
        Assert.That(LibraryKey.Named("fusion", "id"), Is.EqualTo(LibraryKey.Named("fusion", "id")));
    }

    [Test]
    public void PersistenceContainsNoPathsNamesOrGameBytes()
    {
        var data = new LibraryActivity(_path);
        var key = LibraryKey.Original("c64", [new("Nombre privado.prg", Encoding.UTF8.GetBytes("contenido privado"), @"C:\usuario\juegos")]);
        data.SetFavorite(key, true); data.Played(key);
        string json = File.ReadAllText(_path);
        Assert.That(json, Does.Not.Contain("privado").And.Not.Contain("usuario").And.Not.Contain("prg"));
        Assert.That(json, Does.Contain(key)); Assert.That(data.Problem, Is.Null);
    }

    [TestCase("{roto")]
    [TestCase("null")]
    [TestCase("{\"Version\":2,\"Games\":{}}")]
    [TestCase("{\"Version\":1,\"Games\":null}")]
    [TestCase("{\"Version\":1,\"Games\":{\"bad-key\":null}}")]
    public void InvalidOrFutureMetadataIsPreservedAndSessionStillWorks(string content)
    {
        File.WriteAllText(_path, content);
        var data = new LibraryActivity(_path);
        Assert.That(data.Problem, Is.Not.Null);
        data.SetFavorite(Key(1), true); data.Played(Key(1)); data.ClearRecents();
        Assert.That(data.IsFavorite(Key(1)), Is.True); Assert.That(data.Recents, Is.Empty);
        Assert.That(File.ReadAllText(_path), Is.EqualTo(content));
    }

    [Test]
    public void LockedFileRetainsLastGoodDataAndCanSaveAfterUnlocking()
    {
        var data = new LibraryActivity(_path); data.SetFavorite(Key(1), true);
        byte[] previous = File.ReadAllBytes(_path);
        using (var held = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            data.Played(Key(2)); Assert.That(data.Problem, Is.Not.Null);
            Assert.That(File.ReadAllBytes(_path), Is.EqualTo(previous));
            Assert.That(data.LastPlayed(Key(2)), Is.Not.Null);
        }
        data.SetFavorite(Key(3), true);
        var restored = new LibraryActivity(_path);
        Assert.That(data.Problem, Is.Null); Assert.That(restored.IsFavorite(Key(1)), Is.True);
        Assert.That(restored.IsFavorite(Key(3)), Is.True); Assert.That(restored.LastPlayed(Key(2)), Is.Not.Null);
        Assert.That(Directory.GetFiles(_root, "*.tmp"), Is.Empty);
    }

    [Test]
    public void AutomatedRunsKeepActivityInMemoryWithoutReadingOrChangingPlayerData()
    {
        File.WriteAllText(_path, "existing player data");
        var data = new LibraryActivity(_path, persist: false);
        Assert.That(data.Problem, Is.Null);
        data.SetFavorite(Key(1), true); data.Played(Key(1));
        Assert.That(data.IsFavorite(Key(1)), Is.True);
        Assert.That(data.Recents, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllText(_path), Is.EqualTo("existing player data"));
    }

    [Test]
    public void FavoriteCanBeRestoredWhenAnUnavailableGameReturns()
    {
        var rom = new MediaFile("old.tap", [1, 2, 3], "old-folder");
        var data = new LibraryActivity(_path);
        data.SetFavorite(LibraryKey.Original("zx48", [rom]), true);
        data = new LibraryActivity(_path);
        var returned = rom with { Name = "renamed.tap", Source = "new-folder" };
        Assert.That(data.IsFavorite(LibraryKey.Original("zx48", [returned])), Is.True);
        Assert.That(data.Recents, Is.Empty);
    }
}