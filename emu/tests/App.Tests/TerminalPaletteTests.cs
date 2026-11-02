using ImGuiNET;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class TerminalPaletteTests
{
    [Test]
    public void CustomRgbSurvivesRestartAndDoesNotChangeTheInterfaceOrItsOtherPreferences()
    {
        string path = Path.Combine(Path.GetTempPath(), "retro-palette-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var colors = TerminalPalettes.Change(TerminalPalettes.Amber, "texto", "abc123");
            var prefs = new AppPreferences(path) { Interface = InterfaceStyle.Hardcore, ConsolePalette = "custom", ConsoleColors = colors, TextPercent = 175 };
            Assert.That(prefs.Save(), Is.True);
            var loaded = new AppPreferences(path);
            Assert.That(loaded.ConsoleColors, Is.EqualTo(colors)); Assert.That(loaded.ConsolePalette, Is.EqualTo("custom"));
            Assert.That(loaded.Interface, Is.EqualTo(InterfaceStyle.Hardcore)); Assert.That(loaded.TextPercent, Is.EqualTo(175));
            Assert.That(loaded.ConsoleColors.Text, Is.EqualTo("#ABC123"));
            Assert.That(File.ReadAllText(path), Does.Not.Contain("Values"));
            loaded.ConsoleColors = loaded.ConsoleColors with { Background = "not RGB" };
            byte[] original = File.ReadAllBytes(path);
            Assert.That(loaded.Save(), Is.False); Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
        }
        finally { File.Delete(path); }
    }

    [TestCase("#00000"), TestCase("#0000000"), TestCase("#GG0000"), TestCase("##000000"), TestCase("$(cmd)"), TestCase("red")]
    public void InvalidColorsAreRejectedBeforeChangingAnyPalette(string invalid)
    {
        Assert.Throws<ArgumentException>(() => TerminalPalettes.Change(TerminalPalettes.Green, "text", invalid));
        Assert.That(TerminalPalettes.Green.Text, Is.EqualTo("#91F09C"));
    }

    [Test]
    public void AColorDamagedOnDiskKeepsTheFileUntilExplicitRecoveryPreservesItsFullContents()
    {
        string path = Path.Combine(Path.GetTempPath(), "retro-palette-damaged-" + Guid.NewGuid().ToString("N") + ".json");
        string? backup = null;
        try
        {
            var prefs = new AppPreferences(path) { ConsolePalette = "custom", TextPercent = 175, LibraryFolder = "biblioteca propia" };
            Assert.That(prefs.Save(), Is.True);
            string damaged = File.ReadAllText(path).Replace(prefs.ConsoleColors.Background, "invalid color"); File.WriteAllText(path, damaged);
            var loaded = new AppPreferences(path); Assert.That(loaded.CanRecover, Is.True); Assert.That(loaded.TextPercent, Is.EqualTo(175));
            Assert.That(loaded.LibraryFolder, Is.EqualTo("biblioteca propia"), "A damaged color must not change the selected library.");
            Assert.That(loaded.Save(), Is.False); Assert.That(File.ReadAllText(path), Is.EqualTo(damaged));
            Assert.That(loaded.Recover(), Is.True); backup = loaded.RecoveryPath;
            Assert.That(File.ReadAllText(backup!), Is.EqualTo(damaged));
            Assert.That(new AppPreferences(path).ConsoleColors, Is.EqualTo(TerminalPalettes.DefaultFor(InterfaceStyle.Retro)));
        }
        finally { File.Delete(path); if (backup != null) File.Delete(backup); }
    }

    [Test]
    public void AllEightEditorFieldsHaveEnglishLabels()
    {
        UiText.Language = "en";
        try
        {
            Assert.That(TerminalPalettes.Fields.Select(field => UiText.Get(field.Label)), Is.EqualTo(new[] {
                "Background", "Text", "Accent and cursor", "Secondary text", "Selection", "Successful results", "Warnings", "Errors" }));
        }
        finally { UiText.Language = "es"; }
    }

    [Test]
    public void EveryPresetAndCustomColorActuallyReachesTheConsoleAndHighContrastCanBeReversed()
    {
        nint context = ImGui.CreateContext();
        try
        {
            var custom = TerminalPalettes.Change(TerminalPalettes.Green, "background", "#102233");
            foreach (var preset in TerminalPalettes.All)
            {
                UiTheme.Apply(appearance: InterfaceStyle.Retro, consolePalette: preset.Id, customColors: custom);
                var expected = TerminalPalettes.Resolve(preset.Id, custom, InterfaceStyle.Retro);
                var graphical = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
                using (UiTheme.PushConsoleColors())
                {
                    Assert.That(ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg], Is.EqualTo(TerminalPalettes.Color(expected.Background)));
                    Assert.That(UiTheme.TerminalInk(TerminalKind.Error), Is.EqualTo(ImGui.ColorConvertFloat4ToU32(TerminalPalettes.Color(expected.Error))));
                }
                Assert.That(ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg], Is.EqualTo(graphical));
            }
            UiTheme.Apply(1, true, InterfaceStyle.Hardcore, "custom", custom);
            Assert.That(UiTheme.ConsoleColors, Is.EqualTo(TerminalPalettes.HighContrast));
            UiTheme.Apply(appearance: InterfaceStyle.Hardcore, consolePalette: "custom", customColors: custom);
            Assert.That(UiTheme.ConsoleColors, Is.EqualTo(custom));
            Assert.That(UiTheme.PaletteOf(InterfaceStyle.Hardcore).Window, Is.EqualTo(TerminalPalettes.Color(custom.Background)));
        }
        finally { UiTheme.Apply(); ImGui.DestroyContext(context); }
    }
}
