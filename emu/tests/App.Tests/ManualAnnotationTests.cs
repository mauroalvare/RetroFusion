using Retro.Ai;
using Retro.App;
using Retro.Core;
using Retro.Machines;

namespace App.Tests;

[NonParallelizable]
public sealed class ManualAnnotationTests
{
    private string _folder = "";
    private string? _previousProfiles;
    [SetUp] public void Setup()
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-own-notes-" + Guid.NewGuid().ToString("N"))).FullName;
        _previousProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_folder, "profiles"));
    }
    [TearDown] public void Cleanup()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _previousProfiles);
        string path = Path.GetFullPath(_folder);
        Assert.That(path.StartsWith(Path.GetFullPath(Path.GetTempPath()) + "retro-own-notes-", StringComparison.OrdinalIgnoreCase), Is.True);
        Directory.Delete(path, recursive: true);
    }
    private EmulatorHost Host(byte edition = 0, string name = "Own lighthouse.nes")
    {
        var host = new EmulatorHost();
        host.StartNes(MachineCatalog.Get("nes"), new MediaFile(name, NesBatteryTests.Image(edition), name));
        return host;
    }
    private CodeWindow Window(EmulatorHost host) => new(host, new AiSettings { Provider = "manual", ProvidersDisabled = true }, _ => { },
        history: new ModificationHistory(persist: false), annotationsDirectory: _folder);

    [Test]
    public void ManualNotesFollowContentAcrossRenamesAndStaySeparateForSameNameEditions()
    {
        var host = Host(); byte[] before = host.Machine!.SaveState();
        using (var code = Window(host))
        {
            Assert.That(code.WriteAnnotation(0xc000, "Entrada manual en español"), Is.Null);
            Assert.That(code.WriteAnnotation(0xc000, "entrada principal", label: true), Is.Null);
            Assert.That(code.ReadAnnotation(0xc000).Label, Is.EqualTo("entrada_principal"));
        }
        Assert.That(host.Machine.SaveState(), Is.EqualTo(before), "Annotating must not execute or patch the program.");
        using var renamed = Window(Host(name: "Renamed copy.nes"));
        Assert.That(renamed.ReadAnnotation(0xc000).Comment, Is.EqualTo("Entrada manual en español"));
        using var other = Window(Host(edition: 1));
        Assert.That(other.ReadAnnotation(0xc000).Comment, Is.Empty);
        Assert.That(other.WriteAnnotation(0xc000, "Other edition"), Is.Null);
        Assert.That(renamed.ReadAnnotation(0xc000).Comment, Is.EqualTo("Entrada manual en español"));
        Assert.That(Directory.GetFiles(_folder, "edition-*.json"), Has.Length.EqualTo(2));
    }

    [Test]
    public void LegacyImportIsExplicitAdditiveRememberedAndNeverAppliesItsPatchRecords()
    {
        string legacyPath = Path.Combine(_folder, "own-lighthouse.json");
        var legacy = new CodeAnnotations(); legacy.Comments["C000"] = "Legacy comment"; legacy.Labels["C000"] = "Legacy label";
        legacy.Comments["C003"] = "Another old note"; legacy.Patches["C002"] = "60|EA"; legacy.Save("own-lighthouse", _folder);
        byte[] legacyBytes = File.ReadAllBytes(legacyPath);
        var host = Host(); using var code = Window(host); byte[] machine = host.Machine!.SaveState();
        Assert.That(code.ReadAnnotation(0xc000).Comment, Is.Empty); Assert.That(code.HasLegacyAnnotations, Is.True);
        Assert.That(() => code.ImportLegacyAnnotations(false), Throws.TypeOf<InvalidOperationException>());
        code.WriteAnnotation(0xc000, "Current comment");
        Assert.That(code.ImportLegacyAnnotations(true), Is.Null);
        Assert.That(code.ReadAnnotation(0xc000).Comment, Is.EqualTo("Current comment"));
        Assert.That(code.ReadAnnotation(0xc000).Label, Is.EqualTo("Legacy label"));
        Assert.That(code.ReadAnnotation(0xc003).Comment, Is.EqualTo("Another old note"));
        Assert.That(host.Machine.SaveState(), Is.EqualTo(machine)); Assert.That(File.ReadAllBytes(legacyPath), Is.EqualTo(legacyBytes));
        using var reopened = Window(Host()); _ = reopened.ReadAnnotation(0xc000);
        Assert.That(reopened.HasLegacyAnnotations, Is.False, "The same source must not keep prompting after a successful import.");
    }

    [TestCase("{"), TestCase("{\"Comments\":{\"not-hex\":\"Old text\"}}")]
    public void InvalidLegacyDataIsNotImportedOrModified(string json)
    {
        string path = Path.Combine(_folder, "own-lighthouse.json"); File.WriteAllText(path, json);
        using var code = Window(Host()); code.WriteAnnotation(0xc000, "Current");
        Assert.That(() => code.ImportLegacyAnnotations(true), Throws.TypeOf<InvalidDataException>());
        Assert.That(File.ReadAllText(path), Is.EqualTo(json)); Assert.That(code.ReadAnnotation(0xc000).Comment, Is.EqualTo("Current"));
    }

    [TestCase(0x10000u, "Outside", false), TestCase(0xc000u, "", false), TestCase(0xc000u, "\u0001", false)]
    public void InvalidManualInputLeavesNotesAndMemoryIntact(uint address, string text, bool label)
    {
        var host = Host(); using var code = Window(host); code.WriteAnnotation(0xc000, "Before");
        byte[] memory = host.Machine!.SaveState(); byte[] notes = File.ReadAllBytes(Directory.GetFiles(_folder, "edition-*.json").Single());
        Assert.That(() => code.WriteAnnotation(address, text, label), Throws.InstanceOf<ArgumentException>());
        Assert.That(host.Machine.SaveState(), Is.EqualTo(memory));
        Assert.That(File.ReadAllBytes(Directory.GetFiles(_folder, "edition-*.json").Single()), Is.EqualTo(notes));
    }

    [Test]
    public void WriteFailureKeepsTheEditInSessionAndDoesNotAnnounceItAsSaved()
    {
        using var code = Window(Host()); code.WriteAnnotation(0xc000, "Before");
        string path = Directory.GetFiles(_folder, "edition-*.json").Single(); byte[] saved = File.ReadAllBytes(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.That(code.WriteAnnotation(0xc000, "After"), Is.Not.Null);
            Assert.That(code.ReadAnnotation(0xc000).Comment, Is.EqualTo("After"));
        }
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
        Assert.That(code.WriteAnnotation(0xc000, "After"), Is.Null);
        using var fresh = Window(Host()); Assert.That(fresh.ReadAnnotation(0xc000).Comment, Is.EqualTo("After"));
    }

    [Test]
    public void ConcurrentAnnotationWritersRequirePreservingTheOtherFileBeforeReplacingIt()
    {
        var first = CodeAnnotations.Load("own", _folder); var second = CodeAnnotations.Load("own", _folder);
        first.Comments["C000"] = "First session"; first.Save("own");
        byte[] expected = File.ReadAllBytes(Path.Combine(_folder, "own.json"));
        second.Comments["C000"] = "Second session"; second.Save("own");
        Assert.That(second.Problem, Does.Contain("Otra instancia")); Assert.That(second.CanRecover, Is.True);
        Assert.That(File.ReadAllBytes(Path.Combine(_folder, "own.json")), Is.EqualTo(expected));
        Assert.That(second.Recover("own"), Is.True); Assert.That(File.ReadAllBytes(second.RecoveryPath!), Is.EqualTo(expected));
        Assert.That(CodeAnnotations.Load("own", _folder).Comment(0xc000), Is.EqualTo("Second session"));
    }

    [TestCase("{\"Format\":99}"), TestCase("{\"Format\":1,\"GameIdentity\":\"another-game\"}")]
    public void FutureOrMismatchedAnnotationFilesArePreserved(string json)
    {
        string path = Path.Combine(_folder, "own.json"); File.WriteAllText(path, json);
        var notes = CodeAnnotations.Load("own", _folder); Assert.That(notes.Problem, Is.Not.Null);
        notes.Comments["C000"] = "Current session"; notes.Save("own");
        Assert.That(File.ReadAllText(path), Is.EqualTo(json));
    }

    [Test]
    public void UnsavedNotesBlockResetLoadAndGameChangesBeforeMemoryOrTimelineChanges()
    {
        var host = Host(); using var code = Window(host); code.WriteAnnotation(0xc000, "Before");
        host.Nes!.Ram[20] = 11; host.QuickSave(); host.Nes.Ram[20] = 22;
        byte[] before = host.Machine!.SaveState(); long revision = host.MediaRevision;
        string path = Directory.GetFiles(_folder, "edition-*.json").Single();
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.That(code.WriteAnnotation(0xc000, "After"), Is.Not.Null);
            Assert.That(() => host.Reset(true), Throws.TypeOf<IOException>());
            Assert.That(() => host.QuickLoad(), Throws.TypeOf<IOException>());
            Assert.That(() => host.StartNes(MachineCatalog.Get("nes"), new MediaFile("Another.nes", NesBatteryTests.Image(1), "Another.nes")), Throws.TypeOf<IOException>());
            Assert.That(host.MediaRevision, Is.EqualTo(revision)); Assert.That(host.Machine.SaveState(), Is.EqualTo(before));
            string exported = code.ExportAnnotations(Path.Combine(_folder, "copy.json"));
            byte[] recovery = File.ReadAllBytes(exported);
            Assert.DoesNotThrow(host.PreserveSession, "The exact current notes have been preserved in a verified export.");
            File.WriteAllBytes(exported, recovery[..20]); Assert.That(() => host.PreserveSession(), Throws.TypeOf<IOException>());
            File.WriteAllBytes(exported, recovery); Assert.DoesNotThrow(host.PreserveSession);
            File.Delete(exported); Assert.That(() => host.PreserveSession(), Throws.TypeOf<IOException>());
        }
        Assert.That(code.TryPreserveAnnotations(), Is.True);
        host.QuickLoad(); Assert.That(host.Nes.Ram[20], Is.EqualTo(11));
    }

    [Test]
    public void ExportsDoNotOverwriteAndBoundNotesRequireAnIdentityInTheirHeader()
    {
        var host = Host(); using var code = Window(host); code.WriteAnnotation(0xc000, "Own text");
        string copy = code.ExportAnnotations(Path.Combine(_folder, "copy.json")); byte[] expected = File.ReadAllBytes(copy);
        Assert.That(() => code.ExportAnnotations(copy), Throws.TypeOf<IOException>()); Assert.That(File.ReadAllBytes(copy), Is.EqualTo(expected));
        string primary = Directory.GetFiles(_folder, "edition-*.json").Single();
        File.WriteAllText(primary, "{\"Comments\":{\"C000\":\"Unidentified\"}}");
        using var reopened = Window(Host()); Assert.That(reopened.ReadAnnotation(0xc000).Comment, Is.Empty);
        Assert.That(reopened.AnnotationProblem, Is.Not.Empty);
    }

    [Test]
    public void ARefreshOfTheSameEditionDoesNotDiscardPendingNotes()
    {
        var host = Host(); using var code = Window(host); code.WriteAnnotation(0xc000, "Before");
        string path = Directory.GetFiles(_folder, "edition-*.json").Single();
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.That(code.WriteAnnotation(0xc000, "Pending"), Is.Not.Null);
        host.StateRestored();
        Assert.That(code.ReadAnnotation(0xc000).Comment, Is.EqualTo("Pending"));
        Assert.That(code.TryPreserveAnnotations(), Is.False);
    }
}
