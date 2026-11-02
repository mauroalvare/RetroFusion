using System.Text.Json.Nodes;
using ImGuiNET;
using Retro.App;

namespace App.Tests;

[NonParallelizable]
public sealed class InterfacePaletteTests
{
    [TestCase(false), TestCase(true)]
    public void FuturePaletteEntriesArePreservedAndAmbiguousAliasesNeverOverwriteTheFile(bool ambiguous)
    {
        string path = Path.Combine(Path.GetTempPath(), "retro-future-layout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var prefs = new AppPreferences(path) { Interface = InterfaceStyle.Terminal, ConsolePalette = "paper" }; prefs.Save();
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            json["InterfacePalettes"]![ambiguous ? "terminal" : "future-layout"] = json["InterfacePalettes"]!["explorer"]!.DeepClone();
            File.WriteAllText(path, json.ToJsonString()); byte[] original = File.ReadAllBytes(path);
            var loaded = new AppPreferences(path);
            if (ambiguous)
            { Assert.That(loaded.CanRecover, Is.True); Assert.That(loaded.Save(), Is.False); Assert.That(File.ReadAllBytes(path), Is.EqualTo(original)); }
            else
            {
                loaded.ShowFps = true; Assert.That(loaded.Save(), Is.True);
                Assert.That(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(path))!["InterfacePalettes"]!["future-layout"], json["InterfacePalettes"]!["future-layout"]), Is.True);
            }
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void LayoutsKeepTheirOwnColorsAcrossSwitchesAndRestart()
    {
        string path = Path.Combine(Path.GetTempPath(), "retro-layout-colors-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var prefs = new AppPreferences(path);
            var colors = new Dictionary<InterfaceStyle, TerminalColors>();
            foreach (var style in InterfaceStyles.All)
            {
                prefs.Interface = style.Style;
                prefs.ConsoleColors = TerminalPalettes.Change(TerminalPalettes.DefaultFor(style.Style), "accent", $"{(int)style.Style + 1:X2}5599");
                prefs.ConsolePalette = "custom"; colors[style.Style] = prefs.ConsoleColors;
            }
            Assert.That(prefs.Save(), Is.True);
            var loaded = new AppPreferences(path);
            foreach (var style in InterfaceStyles.All)
            { loaded.Interface = style.Style; Assert.That(loaded.ConsoleColors, Is.EqualTo(colors[style.Style])); }
            loaded.Interface = InterfaceStyle.Retro; loaded.ConsolePalette = "paper"; Assert.That(loaded.Save(), Is.True);
            loaded.Interface = InterfaceStyle.Hardcore; Assert.That(loaded.ConsolePalette, Is.EqualTo("custom"));
            Assert.That(loaded.ConsoleColors, Is.EqualTo(colors[InterfaceStyle.Hardcore]));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void ThePreviousGlobalPaletteMigratesWithoutLosingAnyRgbAndThenBecomesIndependent()
    {
        string path = Path.Combine(Path.GetTempPath(), "retro-global-palette-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var prefs = new AppPreferences(path) { ConsolePalette = "custom", ConsoleColors = TerminalPalettes.Amber }; prefs.Save();
            var json = JsonNode.Parse(File.ReadAllText(path))!; json.AsObject().Remove("InterfacePalettes"); File.WriteAllText(path, json.ToJsonString());
            var migrated = new AppPreferences(path);
            foreach (var style in InterfaceStyles.All)
            { migrated.Interface = style.Style; Assert.That(migrated.ConsoleColors, Is.EqualTo(TerminalPalettes.Amber)); }
            migrated.ConsolePalette = "ice";
            migrated.Interface = InterfaceStyle.Hardcore; Assert.That(migrated.ConsolePalette, Is.EqualTo("custom"));
            Assert.That(migrated.Save(), Is.True);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void AHiddenDamagedPaletteAlsoPreventsOverwritingAndKeepsTheChosenLibrary()
    {
        string path = Path.Combine(Path.GetTempPath(), "retro-bad-layout-palette-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var prefs = new AppPreferences(path) { Interface = InterfaceStyle.Hardcore, ConsolePalette = "paper", LibraryFolder = "biblioteca elegida" }; prefs.Save();
            var json = JsonNode.Parse(File.ReadAllText(path))!; json["InterfacePalettes"]!["real"]!["Colors"]!["Warning"] = "not RGB";
            json["Interface"] = "retro"; File.WriteAllText(path, json.ToJsonString()); byte[] original = File.ReadAllBytes(path);
            var loaded = new AppPreferences(path); Assert.That(loaded.CanRecover, Is.True); Assert.That(loaded.LibraryFolder, Is.EqualTo("biblioteca elegida"));
            Assert.That(loaded.Save(), Is.False); Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void StoredPaletteReachesTheEntireInterfaceAndPreviewWithoutChangingLayoutOrOtherStyles()
    {
        nint context = ImGui.CreateContext();
        try
        {
            var prefs = new AppPreferences(persist: false) { Interface = InterfaceStyle.Retro, ConsolePalette = "paper" };
            var other = prefs.PaletteFor(InterfaceStyle.Terminal);
            UiTheme.Apply(appearance: prefs.Interface, consolePalette: prefs.ConsolePalette, customColors: prefs.ConsoleColors, savedPalettes: prefs.InterfacePalettes);
            Assert.That(UiTheme.Interface, Is.EqualTo(InterfaceStyle.Retro));
            Assert.That(ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg], Is.EqualTo(TerminalPalettes.Color("#F3F0E6")));
            Assert.That(UiTheme.PaletteOf(InterfaceStyle.Retro).Window, Is.EqualTo(ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg]));
            Assert.That(prefs.PaletteFor(InterfaceStyle.Terminal), Is.EqualTo(other));
        }
        finally { UiTheme.Apply(); ImGui.DestroyContext(context); }
    }
}
