using Retro.Ai;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[TestFixture, NonParallelizable]
public sealed class PersistenceReviewTests
{
    private string _folder = "";
    [SetUp] public void Setup() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-persistence-review-" + Guid.NewGuid().ToString("N"))).FullName;
    [TearDown]
    public void Cleanup()
    {
        Assert.That(Path.GetDirectoryName(Path.GetFullPath(_folder)), Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
        Assert.That(Path.GetFileName(_folder), Does.StartWith("retro-persistence-review-"));
        Directory.Delete(_folder, recursive: true);
    }

    [TestCase(false), TestCase(true)]
    public void ExplicitSettingsRecoveryKeepsTheDamagedBytesAndMakesChangesPersistent(bool sound)
    {
        string path = Path.Combine(_folder, "settings.json"); const string broken = "{not complete\n"; File.WriteAllText(path, broken);
        if (sound)
        {
            var settings = new SoundSettings(path) { VolumePercent = 35, BufferFrames = 4 };
            Assert.That(settings.CanRecover, Is.True); Assert.That(settings.Save(), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(broken));
            Assert.That(settings.Recover(), Is.True); Assert.That(settings.CanRecover, Is.False);
            Assert.That(File.ReadAllText(settings.RecoveryPath!), Is.EqualTo(broken));
            Assert.That((new SoundSettings(path).VolumePercent, new SoundSettings(path).BufferFrames), Is.EqualTo((35, 4)));
            Assert.That(settings.Recover(), Is.True);
        }
        else
        {
            var settings = new AppPreferences(path) { TextPercent = 175, C64Port = 1, HighContrast = true };
            Assert.That(settings.CanRecover, Is.True); Assert.That(settings.Save(), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(broken));
            Assert.That(settings.Recover(), Is.True); Assert.That(settings.CanRecover, Is.False);
            Assert.That(File.ReadAllText(settings.RecoveryPath!), Is.EqualTo(broken));
            Assert.That((new AppPreferences(path).TextPercent, new AppPreferences(path).C64Port, new AppPreferences(path).HighContrast), Is.EqualTo((175, 1, true)));
            Assert.That(settings.Recover(), Is.True);
        }
        Assert.That(Directory.GetFiles(_folder, "*.respaldo-*"), Has.Length.EqualTo(1));
        Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
    }

    [TestCase(false), TestCase(true)]
    public void RecoveryCannotReplaceAFileWhileItsBackupIsUnavailable(bool sound)
    {
        string path = Path.Combine(_folder, "settings.json"); File.WriteAllText(path, "broken");
        var audio = new SoundSettings(path); var display = new AppPreferences(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.That(sound ? audio.Recover() : display.Recover(), Is.False);
            Assert.That(sound ? audio.CanRecover : display.CanRecover, Is.True);
        }
        Assert.That(File.ReadAllText(path), Is.EqualTo("broken"));
        Assert.That(sound ? audio.Recover() : display.Recover(), Is.True);
        Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
    }

    [TestCase(false), TestCase(true)]
    public void InvalidSessionSettingsNeverCreateARecoveryBackup(bool sound)
    {
        string path = Path.Combine(_folder, "settings.json"); File.WriteAllText(path, "broken");
        Assert.That(sound ? new SoundSettings(path) { VolumePercent = -1 }.Recover() : new AppPreferences(path) { TextPercent = 110 }.Recover(), Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo("broken"));
        Assert.That(Directory.GetFiles(_folder), Has.Length.EqualTo(1));
    }

    [TestCase(false), TestCase(true)]
    public void SessionOnlySettingsDoNotTouchEvenAnUnreadableFile(bool sound)
    {
        string path = Path.Combine(_folder, "settings.json"); File.WriteAllText(path, "broken");
        Assert.That(sound ? new SoundSettings(path, persist: false).Recover() : new AppPreferences(path, persist: false).Recover(), Is.True);
        Assert.That(File.ReadAllText(path), Is.EqualTo("broken"));
        Assert.That(Directory.GetFiles(_folder), Has.Length.EqualTo(1));
    }

    [TestCase("null"), TestCase("{"), TestCase("{\"cliModels\":null}"), TestCase("{\"cliEfforts\":{\"sub-codex\":null}}")]
    [TestCase("{\"provider\":\"not-supported\"}"), TestCase("{\"textLimitThousands\":-1}"), TestCase("{\"ollamaModel\":null}")]
    public void InvalidAiConfigurationCannotCrashThePickerOrBeSilentlyOverwritten(string json)
    {
        string path = Path.Combine(_folder, "ai.json"); File.WriteAllText(path, json);
        var settings = AiSettings.Load(path);
        Assert.That(settings.Problem, Is.Not.Null); Assert.That(settings.CanRecover, Is.True);
        Assert.That(settings.Provider, Is.EqualTo("manual")); Assert.That(settings.CreateProvider(), Is.Null);
        Assert.That(settings.CliModels, Is.Not.Null); Assert.That(settings.CliEfforts, Is.Not.Null);
        settings.Provider = "sub-codex"; settings.Save();
        Assert.That(File.ReadAllText(path), Is.EqualTo(json));
        Assert.That(settings.Recover(), Is.True);
        Assert.That(File.ReadAllText(settings.RecoveryPath!), Is.EqualTo(json));
        Assert.That(AiSettings.Load(path).Provider, Is.EqualTo("sub-codex"));
        Assert.That(AiSettings.Load(path).Problem, Is.Null);
        Assert.That(File.ReadAllText(path), Does.Not.Contain("recoveryPath").And.Not.Contain("problem"));
    }

    [Test]
    public void AiConfigurationSaveIsAtomicAndReportsAnUnavailableDestination()
    {
        string path = Path.Combine(_folder, "ai.json"); var settings = AiSettings.Load(path);
        settings.Provider = "sub-codex"; settings.CliModels["sub-codex"] = "own-model"; settings.CliEfforts["sub-codex"] = "high"; settings.Save();
        Assert.That(settings.Problem, Is.Null); byte[] previous = File.ReadAllBytes(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { settings.Economic = true; Assert.DoesNotThrow(settings.Save); Assert.That(settings.Problem, Is.Not.Null); }
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(previous));
        Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
        settings.Save(); var loaded = AiSettings.Load(path);
        Assert.That(loaded.Economic, Is.True); Assert.That(loaded.CliModels["sub-codex"], Is.EqualTo("own-model"));
        Assert.That(loaded.CliEfforts["sub-codex"], Is.EqualTo("high"));
    }

    [TestCase("null"), TestCase("{"), TestCase("{\"Labels\":null}"), TestCase("{\"Comments\":null}")]
    [TestCase("{\"Summaries\":null}"), TestCase("{\"Pseudocode\":null}"), TestCase("{\"Patches\":null}"), TestCase("{\"Comments\":{\"0010\":null}}")]
    public void DamagedAnnotationsStayAvailableForRecoveryWhileTheToolsKeepWorking(string json)
    {
        string path = Path.Combine(_folder, "own.json"); File.WriteAllText(path, json);
        var notes = CodeAnnotations.Load("own", _folder);
        Assert.That(notes.Problem, Is.Not.Null); Assert.That(notes.Label(0x10), Is.Null); Assert.That(notes.Comment(0x10), Is.Null);
        notes.Labels["0010"] = "Entry"; notes.Save("own");
        Assert.That(File.ReadAllText(path), Is.EqualTo(json));
        Assert.That(notes.Recover("own"), Is.True);
        Assert.That(File.ReadAllText(notes.RecoveryPath!), Is.EqualTo(json));
        Assert.That(CodeAnnotations.Load("own", _folder).Label(0x10), Is.EqualTo("Entry"));
        Assert.That(CodeAnnotations.Load("own", _folder).Problem, Is.Null);
        Assert.That(File.ReadAllText(path), Does.Not.Contain("RecoveryPath").And.Not.Contain("Problem"));
    }

    [Test]
    public void NotesReportWriteFailuresWithoutTruncatingPreviouslySavedWork()
    {
        var notes = new CodeAnnotations(); notes.Labels["0010"] = "Before"; notes.Save("own", _folder);
        string path = Path.Combine(_folder, "own.json"); byte[] previous = File.ReadAllBytes(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { notes.Labels["0010"] = "After"; Assert.DoesNotThrow(() => notes.Save("own")); Assert.That(notes.Problem, Is.Not.Null); }
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(previous));
        Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
        notes.Save("own"); Assert.That(notes.Problem, Is.Null);
        Assert.That(CodeAnnotations.Load("own", _folder).Label(0x10), Is.EqualTo("After"));
    }

    [TestCase("../outside"), TestCase("..\\outside"), TestCase("."), TestCase("..")]
    public void AnnotationIdentifiersCannotRedirectFilesOutsideTheirFolder(string id)
    {
        var notes = new CodeAnnotations(); notes.Labels["0010"] = "Entry";
        Assert.DoesNotThrow(() => notes.Save(id, _folder)); Assert.That(notes.Problem, Is.Not.Null);
        Assert.That(Directory.GetFiles(_folder), Is.Empty);
        Assert.That(CodeAnnotations.Load(id, _folder).Problem, Is.Not.Null);
    }

    [TestCase("display"), TestCase("sound"), TestCase("ai"), TestCase("notes")]
    public void ABackupRemainsDiscoverableIfPublishingTheRecoveredFileFails(string kind)
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows sharing semantics keep a readable file open without allowing replacement.");
        string path = Path.Combine(_folder, kind == "notes" ? "own.json" : "settings.json"); File.WriteAllText(path, "broken");
        var display = new AppPreferences(path); var sound = new SoundSettings(path); var ai = AiSettings.Load(path);
        var notes = CodeAnnotations.Load("own", _folder);
        bool Recover() => kind switch { "display" => display.Recover(), "sound" => sound.Recover(), "ai" => ai.Recover(), _ => notes.Recover("own") };
        string? Backup() => kind switch { "display" => display.RecoveryPath, "sound" => sound.RecoveryPath, "ai" => ai.RecoveryPath, _ => notes.RecoveryPath };
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.That(Recover(), Is.False);
            Assert.That(Backup(), Is.Not.Null, "A completed backup must stay visible even when replacing the destination fails.");
            Assert.That(File.ReadAllText(Backup()!), Is.EqualTo("broken"));
        }
        Assert.That(File.ReadAllText(path), Is.EqualTo("broken"));
        Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
        Assert.That(Recover(), Is.True);
    }
}
