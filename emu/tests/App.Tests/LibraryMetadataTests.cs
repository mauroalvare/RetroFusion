using System.Text.Json;
using Retro.Core;

namespace App.Tests;

public sealed class LibraryMetadataTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private string _folder = "";
    private string PathOf => Path.Combine(_folder, "catalogo.json");
    private static string Key(string id) => LibraryKey.Named("original", id);
    [SetUp] public void Setup() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-library-meta-" + Guid.NewGuid().ToString("N"))).FullName;
    [TearDown]
    public void Cleanup()
    {
        Assert.That(Path.GetDirectoryName(Path.GetFullPath(_folder)), Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
        Assert.That(Path.GetFileName(_folder), Does.StartWith("retro-library-meta-"));
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public void GenreYearAndFirstSeenPersistWithoutChangingAnOriginalAndSurviveRenaming()
    {
        byte[] bytes = [1, 8, 0x60]; string original = Path.Combine(_folder, "before.prg"); File.WriteAllBytes(original, bytes);
        string key = LibraryKey.Original("c64", [new MediaFile("before.prg", bytes, original)]);
        var clock = new Clock(); var metadata = new LibraryMetadata(PathOf, clock: clock);
        metadata.Observe([key]); DateTimeOffset? first = metadata.Get(key).AddedAt;
        clock.Now = clock.Now.AddDays(7);
        Assert.That(metadata.Update(key, [" Aventura ", "Plataformas", "AVENTURA"], 1987), Is.True);
        var loaded = new LibraryMetadata(PathOf); var info = loaded.Get(key);
        Assert.That(info.Genres, Is.EqualTo(new[] { "Aventura", "Plataformas" }));
        Assert.That(info.ReleaseYear, Is.EqualTo(1987)); Assert.That(info.AddedAt, Is.EqualTo(first));
        string renamed = Path.Combine(_folder, "after.prg"); File.Move(original, renamed);
        string renamedKey = LibraryKey.Original("c64", [new MediaFile("after.prg", File.ReadAllBytes(renamed), renamed)]);
        Assert.That(renamedKey, Is.EqualTo(key)); Assert.That(loaded.Get(renamedKey).ReleaseYear, Is.EqualTo(1987));
        loaded.Observe([renamedKey]); Assert.That(loaded.Get(renamedKey).AddedAt, Is.EqualTo(first));
        Assert.That(File.ReadAllBytes(renamed), Is.EqualTo(bytes));
    }

    [Test]
    public void ReadingMissingDataNeverInventsAReleaseYearOrAnAddedDate()
    {
        var metadata = new LibraryMetadata(PathOf); var info = metadata.Get(Key("missing"));
        Assert.That(info.Genres, Is.Empty); Assert.That(info.ReleaseYear, Is.Null); Assert.That(info.AddedAt, Is.Null);
        Assert.That(File.Exists(PathOf), Is.False);
        metadata.Observe([Key("missing")]); info = metadata.Get(Key("missing"));
        Assert.That(info.Genres, Is.Empty); Assert.That(info.ReleaseYear, Is.Null); Assert.That(info.AddedAt, Is.Not.Null);
    }

    [Test]
    public void CallerArraysAndSnapshotsCannotChangeSavedGenresBehindTheStore()
    {
        var metadata = new LibraryMetadata(PathOf); string[] genres = ["Acción"];
        metadata.Update(Key("a"), genres, 1990); genres[0] = "Otro";
        var info = metadata.Get(Key("a"));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)info.Genres)[0] = "Cambio");
        Assert.That(metadata.Get(Key("a")).Genres, Is.EqualTo(new[] { "Acción" }));
        Assert.That(new LibraryMetadata(PathOf).Get(Key("a")).Genres, Is.EqualTo(new[] { "Acción" }));
    }

    [Test]
    public void RepeatedObservationAndIdenticalEditsDoNotRewriteTheFileOrInvalidateTheCache()
    {
        var metadata = new LibraryMetadata(PathOf); string key = Key("a"); metadata.Observe([key]);
        metadata.Update(key, ["Rol"], 1980); int revision = metadata.Revision; byte[] before = File.ReadAllBytes(PathOf);
        DateTime stamp = File.GetLastWriteTimeUtc(PathOf);
        metadata.Observe([key, key]); metadata.Update(key, ["Rol"], 1980);
        Assert.That(metadata.Revision, Is.EqualTo(revision)); Assert.That(File.ReadAllBytes(PathOf), Is.EqualTo(before));
        Assert.That(File.GetLastWriteTimeUtc(PathOf), Is.EqualTo(stamp));
    }

    [Test]
    public void ClearingThePersonalClassificationKeepsTheRecordedAdditionDate()
    {
        var metadata = new LibraryMetadata(PathOf); string key = Key("a"); metadata.Update(key, ["Rol"], 1980);
        var added = metadata.Get(key).AddedAt; metadata.Update(key, [], null);
        var loaded = new LibraryMetadata(PathOf).Get(key);
        Assert.That(loaded.Genres, Is.Empty); Assert.That(loaded.ReleaseYear, Is.Null); Assert.That(loaded.AddedAt, Is.EqualTo(added));
    }

    [TestCase(1899), TestCase(2101), TestCase(0)]
    public void InvalidYearsDoNotOverwriteAValidEntry(int year)
    {
        var metadata = new LibraryMetadata(PathOf); metadata.Update(Key("a"), ["Rol"], 1980); byte[] previous = File.ReadAllBytes(PathOf);
        Assert.That(metadata.Update(Key("a"), ["Aventura"], year), Is.False); Assert.That(metadata.Problem, Is.Not.Null);
        Assert.That(File.ReadAllBytes(PathOf), Is.EqualTo(previous)); Assert.That(metadata.Get(Key("a")).ReleaseYear, Is.EqualTo(1980));
    }

    [TestCase(""), TestCase(" "), TestCase("A\nB"), TestCase("01234567890123456789012345678901234567890")]
    public void InvalidGenreValuesAreNotSilentlyTruncated(string genre)
    {
        var metadata = new LibraryMetadata(PathOf);
        Assert.That(metadata.Update(Key("a"), [genre], 1980), Is.False); Assert.That(File.Exists(PathOf), Is.False);
    }

    [Test]
    public void TooManyGenresAndUnsafeIdentitiesAreRejectedBeforeChangingAnything()
    {
        var metadata = new LibraryMetadata(PathOf);
        Assert.That(metadata.Update(Key("a"), Enumerable.Range(0, 9).Select(number => number.ToString()), null), Is.False);
        Assert.That(metadata.Update("../outside", ["Rol"], null), Is.False);
        Assert.Throws<ArgumentException>(() => metadata.Observe([Key("good"), "invalid"]));
        Assert.That(metadata.Get(Key("good")).AddedAt, Is.Null); Assert.That(Directory.GetFiles(_folder), Is.Empty);
    }

    [TestCase("null"), TestCase("{"), TestCase("{\"version\":2,\"games\":{}}"), TestCase("{\"version\":1,\"games\":null}")]
    public void DamagedOrFutureFilesArePreservedUntilExplicitRecovery(string content)
    {
        File.WriteAllText(PathOf, content); var metadata = new LibraryMetadata(PathOf);
        Assert.That(metadata.CanRecover, Is.True); Assert.That(metadata.Problem, Is.Not.Null);
        metadata.Update(Key("a"), ["Rol"], 1980); Assert.That(File.ReadAllText(PathOf), Is.EqualTo(content));
        Assert.That(metadata.Recover(), Is.True); Assert.That(metadata.CanRecover, Is.False);
        Assert.That(File.ReadAllText(metadata.RecoveryPath!), Is.EqualTo(content));
        Assert.That(new LibraryMetadata(PathOf).Get(Key("a")).ReleaseYear, Is.EqualTo(1980));
    }

    [TestCase(true), TestCase(false)]
    public void InvalidNestedEntriesDoNotPartiallyPopulateTheStore(bool nullGenres)
    {
        var entries = new Dictionary<string, object?>
        {
            [Key("valid")] = new { genres = new[] { "Rol" }, releaseYear = 1980, addedAt = DateTimeOffset.UtcNow },
            [Key("invalid")] = nullGenres ? new { genres = (string[]?)null, releaseYear = 1980, addedAt = DateTimeOffset.UtcNow } : null
        };
        string content = JsonSerializer.Serialize(new { version = 1, games = entries }); File.WriteAllText(PathOf, content);
        var metadata = new LibraryMetadata(PathOf);
        Assert.That(metadata.CanRecover, Is.True); Assert.That(metadata.Get(Key("valid")).Genres, Is.Empty);
        metadata.Observe([Key("new")]); Assert.That(File.ReadAllText(PathOf), Is.EqualTo(content));
    }

    [Test]
    public void UnavailableStorageKeepsEditsInSessionAndCleansOnlyItsTemporaryFile()
    {
        var metadata = new LibraryMetadata(PathOf); metadata.Update(Key("a"), ["Rol"], 1980); byte[] previous = File.ReadAllBytes(PathOf);
        using (var held = new FileStream(PathOf, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.That(metadata.Update(Key("a"), ["Aventura"], 1984), Is.True);
            Assert.That(metadata.Problem, Is.Not.Null); Assert.That(metadata.Get(Key("a")).ReleaseYear, Is.EqualTo(1984));
        }
        Assert.That(File.ReadAllBytes(PathOf), Is.EqualTo(previous)); Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
        metadata.Update(Key("a"), ["Aventura"], 1984);
        Assert.That(metadata.Problem, Is.Null, "Saving the same in-session edit retries after the file becomes available.");
        Assert.That(new LibraryMetadata(PathOf).Get(Key("a")).ReleaseYear, Is.EqualTo(1984));
    }

    [Test]
    public void SessionOnlyCatalogueNeverReadsOrWritesThePlayersFile()
    {
        File.WriteAllText(PathOf, "broken"); var metadata = new LibraryMetadata(PathOf, persist: false);
        metadata.Observe([Key("a")]); metadata.Update(Key("a"), ["Rol"], 1980);
        Assert.That(metadata.Get(Key("a")).ReleaseYear, Is.EqualTo(1980)); Assert.That(metadata.CanRecover, Is.False);
        Assert.That(File.ReadAllText(PathOf), Is.EqualTo("broken")); Assert.That(Directory.GetFiles(_folder), Has.Length.EqualTo(1));
    }

    [Test]
    public void ActivityRevisionTracksChangesUsedByTheFilterCache()
    {
        var activity = new LibraryActivity(Path.Combine(_folder, "activity.json")); int revision = activity.Revision;
        activity.SetFavorite(Key("a"), true); Assert.That(activity.Revision, Is.GreaterThan(revision)); revision = activity.Revision;
        activity.SetFavorite(Key("a"), true); Assert.That(activity.Revision, Is.EqualTo(revision));
        activity.Played(Key("a")); Assert.That(activity.Revision, Is.GreaterThan(revision)); revision = activity.Revision;
        activity.ClearRecents(); Assert.That(activity.Revision, Is.GreaterThan(revision));
    }

    [Test]
    public void OversizedMetadataIsPreservedWithoutAttemptingToParseIt()
    {
        using (var file = new FileStream(PathOf, FileMode.CreateNew)) file.SetLength(4 * 1024 * 1024 + 1);
        var metadata = new LibraryMetadata(PathOf);
        Assert.That(metadata.CanRecover, Is.True); metadata.Update(Key("a"), ["Rol"], null);
        Assert.That(new FileInfo(PathOf).Length, Is.EqualTo(4 * 1024 * 1024 + 1));
    }

    [Test]
    public void AFullCatalogueRejectsAnAdditionalBatchWithoutHidingKnownGames()
    {
        var metadata = new LibraryMetadata(PathOf, persist: false);
        string[] keys = Enumerable.Range(0, LibraryMetadata.GameLimit).Select(number => Key(number.ToString())).ToArray();
        metadata.Observe(keys); int revision = metadata.Revision;
        metadata.Observe([Key("extra")]); Assert.That(metadata.Problem, Is.Not.Null);
        Assert.That(metadata.Revision, Is.EqualTo(revision)); Assert.That(metadata.Get(Key("extra")).AddedAt, Is.Null);
        Assert.That(metadata.Get(keys[0]).AddedAt, Is.Not.Null);
        Assert.That(metadata.Update(keys[0], ["Rol"], 1980), Is.True);
    }

    [Test]
    public void IndependentCreationsWithAnEmptyWorkIdDoNotShareTheSamePersonalFile()
    {
        var key = typeof(Retro.App.HomeScreen).GetMethod("CreatedKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var first = new Retro.Ai.CreatedGame { Machine = "c64", WorkId = "", Created = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc) };
        var second = new Retro.Ai.CreatedGame { Machine = "c64", WorkId = "", Created = first.Created.AddSeconds(1) };
        string a = (string)key.Invoke(null, [first])!, b = (string)key.Invoke(null, [second])!;
        Assert.That(a, Is.Not.EqualTo(b));
        first.Title = "Renamed"; Assert.That(key.Invoke(null, [first]), Is.EqualTo(a));
        first.WorkId = second.WorkId = "same-work";
        Assert.That(key.Invoke(null, [first]), Is.EqualTo(key.Invoke(null, [second])));
    }
}
