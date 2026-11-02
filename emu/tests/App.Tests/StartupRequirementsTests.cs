using System.Text.Json;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class StartupRequirementsTests
{
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    [Test]
    public void MissingNativeFilesAreReportedWithoutLoadingThemOrCertifyingTheGraphicsDriver()
    {
        string root = Path.Combine(Path.GetTempPath(), "retro-requirements-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.That(StartupRequirements.Inspect(root).Where(check => check.Status == "missing").Select(check => check.Id), Does.Contain("raylib").And.Contain("imgui"));
            UiText.Language = "en";
            string path = Path.Combine(root, "report.json");
            Assert.That(StartupRequirements.Write(path, root, audioDisabled: true), Is.EqualTo(2));
            using var report = JsonDocument.Parse(File.ReadAllText(path));
            Assert.That(report.RootElement.GetProperty("graphicsDriverChecked").GetBoolean(), Is.False);
            Assert.That(File.ReadAllText(path), Does.Contain("optional").And.Contain("--no-audio").And.Not.Contain(root));
            Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
            // A file can be present without being loadable: the report must not call it a passed driver test.
            File.WriteAllText(Path.Combine(root, OperatingSystem.IsWindows() ? "raylib.dll" : "libraylib.so"), "not a library");
            File.WriteAllText(Path.Combine(root, OperatingSystem.IsWindows() ? "cimgui.dll" : "cimgui.so"), "not a library");
            Assert.That(StartupRequirements.Inspect(root).Single(check => check.Id == "graphics").Status, Is.EqualTo("needs-check"));
            Assert.That(StartupRequirements.Write(path, root), Is.Zero);
        }
        finally
        {
            Assert.That(Path.GetDirectoryName(root), Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
            Assert.That(Path.GetFileName(root), Does.StartWith("retro-requirements-")); Directory.Delete(root, true);
        }
    }

    [TestCase(false), TestCase(true)]
    public void OnlyNoAudioSkipsInitializingTheDeviceAndMuteRetainsTheOrdinaryInitialization(bool disabled)
    {
        Assert.That(StartupRequirements.WantsAudio(disabled ? ["--mute", "--no-audio"] : ["--mute"]), Is.EqualTo(!disabled));
    }
}
