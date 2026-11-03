using Retro.App;

namespace App.Tests;

public sealed class ReleaseNewsTests
{
    private string _folder = "";
    private string FilePath => Path.Combine(_folder, "preferencias.json");
    [SetUp] public void SetUp() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-news-" + Guid.NewGuid().ToString("N"))).FullName;
    [TearDown] public void TearDown() => Directory.Delete(_folder, true);

    [Test]
    public void SkippingFrom026CollectsEveryPublishedIntermediateReleaseInVersionOrder()
    {
        Assert.That(ReleaseNotes.Since("0.2.6", "0.4.1").Select(note => note.Version), Is.EqualTo(new[]
            { "0.4.1", "0.4.0", "0.3.7", "0.3.6", "0.3.5", "0.3.4", "0.3.3", "0.3.2", "0.3.1", "0.3.0", "0.2.9", "0.2.8", "0.2.7" }));
        Assert.That(ReleaseNotes.Since("0.4.0", "0.4.1").Select(note => note.Version), Is.EqualTo(new[] { "0.4.1" }));
        Assert.That(ReleaseNotes.Since("0.2.8", "0.2.9").Select(note => note.Version), Is.EqualTo(new[] { "0.2.9" }));
        Assert.That(ReleaseNotes.Since("bad", "bad"), Is.Empty);
        Assert.That(ReleaseNotes.Since("0.4.0", "0.3.7").Select(note => note.Version), Is.EqualTo(new[] { "0.3.7" }));
    }

    [Test]
    public void FirstInstallationHasTheCompleteOfflineBilingualHistoryWithoutFutureVersions()
    {
        var notes = ReleaseNotes.Since(null, "0.4.1");
        Assert.That(notes, Has.Count.EqualTo(29));
        Assert.That(ReleaseNotes.Since(null, "0.4.0").Any(note => note.Version == "0.4.1"), Is.False);
        Assert.That(notes.Select(note => note.Version), Is.Unique);
        Assert.That(ReleaseNotes.Since(null, "0.3.7").Any(note => note.Version == "0.4.0"), Is.False);
        foreach (var item in notes.SelectMany(note => note.Items))
        {
            Assert.That(item.Title.Spanish, Is.Not.Empty); Assert.That(item.Title.English, Is.Not.Empty);
            Assert.That(item.Description.Spanish, Is.Not.Empty.And.Not.EqualTo(item.Description.English));
            Assert.That(item.Description.In("en"), Is.EqualTo(item.Description.English));
            Assert.That(item.KindIn("en"), Is.AnyOf("New feature", "Improvement", "Fix"));
            Assert.That(item.KindIn("es"), Is.AnyOf("Nueva función", "Mejora", "Corrección"));
        }
    }

    [Test]
    public void UnreadNewsSurvivesTheInterfaceChoiceAndAProcessRestart()
    {
        var prefs = new AppPreferences(FilePath) { LastStartedVersion = "0.2.6", Language = "en" };
        Assert.That(new ReleaseNews(prefs).Queue("0.4.0", prefs.LastStartedVersion, false), Is.True);
        prefs.LastStartedVersion = prefs.InterfaceOfferedVersion = "0.4.0";
        Assert.That(prefs.Save(), Is.True);
        var restarted = new AppPreferences(FilePath);
        Assert.That(restarted.LastNewsVersion, Is.Null, "Offering the interface does not acknowledge unread news.");
        var news = new ReleaseNews(restarted);
        Assert.That(news.Queue("0.4.0", restarted.LastStartedVersion, false), Is.True);
        Assert.That(restarted.PendingNewsFromVersion, Is.EqualTo("0.2.6"));
        news.Acknowledge("0.3.7"); Assert.That(restarted.LastNewsVersion, Is.Null);
        news.Acknowledge("0.4.0");
        var read = new AppPreferences(FilePath);
        Assert.That((read.LastNewsVersion, read.PendingNewsVersion, read.PendingNewsFromVersion, read.Language), Is.EqualTo(("0.4.0", (string?)null, (string?)null, "en")));
        Assert.That(new ReleaseNews(read).Queue("0.4.0", read.LastStartedVersion, false), Is.False);
    }

    [Test]
    public void AnotherUpdateKeepsTheOldestUnreadBaselineAndCanReofferAReinstallation()
    {
        var prefs = new AppPreferences(FilePath) { LastNewsVersion = "0.2.6" };
        var news = new ReleaseNews(prefs);
        news.Queue("0.3.7", "0.2.9", true);
        news.Queue("0.4.0", "0.3.7", true);
        Assert.That(prefs.PendingNewsFromVersion, Is.EqualTo("0.2.6"));
        news.Acknowledge("0.4.0");
        Assert.That(news.Queue("0.4.0", "0.4.0", true), Is.True);
        Assert.That(ReleaseNotes.Since(prefs.PendingNewsFromVersion, prefs.PendingNewsVersion!).Select(note => note.Version), Is.EqualTo(new[] { "0.4.0" }));
    }

    [Test]
    public void AnUnknownFirstSourceRemainsUnknownUntilAcknowledgedAndDowngradesKeepFutureNews()
    {
        var prefs = new AppPreferences(FilePath);
        new ReleaseNews(prefs).Queue("0.3.7", null, true);
        var loaded = new AppPreferences(FilePath);
        var news = new ReleaseNews(loaded);
        news.Queue("0.4.0", "0.3.7", true);
        Assert.That(loaded.PendingNewsFromVersion, Is.Empty);
        Assert.That(news.Queue("0.3.7", "0.4.0", true), Is.False);
        Assert.That(loaded.PendingNewsVersion, Is.EqualTo("0.4.0"));
        Assert.That(loaded.LastNewsVersion, Is.Null);
    }

    [Test]
    public void UnreadablePreferencesAreNeverReplacedByNewsMetadata()
    {
        File.WriteAllText(FilePath, "unreadable original");
        var prefs = new AppPreferences(FilePath);
        var news = new ReleaseNews(prefs);
        Assert.That(news.Queue("0.4.0", "0.2.6", true), Is.True);
        news.Acknowledge("0.4.0");
        Assert.That(File.ReadAllText(FilePath), Is.EqualTo("unreadable original"));
        Assert.That(prefs.Problem, Is.Not.Null);
    }
}
