using System.Text.Json;
using Retro.Fusion;
using Retro.Core;

namespace Fusion.Tests;

[TestFixture, NonParallelizable]
public class ProfileRecoveryTests
{
    private string _folder = "";
    private string? _previous;
    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "retro-profiles-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _previous = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _folder);
    }
    [TearDown] public void Cleanup()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _previous);
        Directory.Delete(_folder, true);
    }
    [TestCase("null")]
    [TestCase("{\"id\":\"bad\",\"match\":null}")]
    [TestCase("{\"id\":\"bad\",\"variables\":null}")]
    [TestCase("{\"id\":\"bad\",\"match\":{\"namePattern\":\"[\"}}")]
    public void BrokenProfileDoesNotHideValidProfilesOrBlockMediaDetection(string text)
    {
        var media = new MediaFile("own.tap", [1, 2, 3], "own.tap");
        var profile = new GameProfile { Id = "good", Name = "Perfil propio", Machine = "zx48", Match = new() { Sha256 = [ProfileMatch.HashOf(media)] } };
        ProfileLibrary.Save(profile);
        string bad = Path.Combine(_folder, "bad.json"); File.WriteAllText(bad, text);
        var all = ProfileLibrary.All();
        Assert.That(all.Any(p => p.Id == "good"), Is.True);
        Assert.That(all.Any(p => p.Id == "bad"), Is.False);
        Assert.That(ProfileLibrary.ForFiles([media])?.Id, Is.EqualTo("good"));
        Assert.That(File.ReadAllText(bad), Is.EqualTo(text));
    }
    [Test]
    public void AtomicSaveDoesNotReplaceThePreviousFileWhenItsDestinationIsUnavailable()
    {
        var profile = new GameProfile { Id = "own", Name = "Antes", Machine = "zx48" };
        string file = ProfileLibrary.SaveTo(profile, _folder); byte[] before = File.ReadAllBytes(file);
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            profile.Name = "Después";
            if (OperatingSystem.IsWindows()) Assert.That(() => ProfileLibrary.SaveTo(profile, _folder), Throws.Exception.TypeOf<IOException>().Or.TypeOf<UnauthorizedAccessException>());
        }
        if (OperatingSystem.IsWindows()) Assert.That(File.ReadAllBytes(file), Is.EqualTo(before));
        Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
    }
}