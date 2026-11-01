using ImGuiNET;
using Retro.App;

namespace App.Tests;

[TestFixture, NonParallelizable]
public sealed class InterfaceStyleTests
{
    [TestCase(InterfaceStyle.Retro), TestCase(InterfaceStyle.Terminal), TestCase(InterfaceStyle.Hardcore)]
    public void StyleAndUpdateOfferPersistWithoutChangingOtherPreferences(InterfaceStyle style)
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-style-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string path = Path.Combine(root, "preferencias.json");
            var prefs = new AppPreferences(path) { Interface = style, InterfaceOfferedVersion = "0.4.0", TextPercent = 175, C64Port = 1, OnlineGameInfo = true };
            Assert.That(prefs.Save(), Is.True);
            var loaded = new AppPreferences(path);
            Assert.That(loaded.Interface, Is.EqualTo(style)); Assert.That(loaded.InterfaceOfferedVersion, Is.EqualTo("0.4.0"));
            Assert.That(loaded.TextPercent, Is.EqualTo(175)); Assert.That(loaded.C64Port, Is.EqualTo(1)); Assert.That(loaded.OnlineGameInfo, Is.True);
            loaded.Defaults(); Assert.That(loaded.Interface, Is.EqualTo(InterfaceStyle.Retro)); Assert.That(loaded.InterfaceOfferedVersion, Is.EqualTo("0.4.0"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void OlderSettingsKeepThePrincipalStyleAndUnknownFutureStylesDoNotDiscardSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), "retro-legacy-style-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var prefs = new AppPreferences(path) { TextPercent = 150 }; prefs.Save();
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!; json.AsObject().Remove("Interface");
            File.WriteAllText(path, json.ToJsonString()); var legacy = new AppPreferences(path);
            Assert.That(legacy.Interface, Is.EqualTo(InterfaceStyle.Retro)); Assert.That(legacy.TextPercent, Is.EqualTo(150)); Assert.That(legacy.Problem, Is.Null);
            json["Interface"] = "future-interface"; File.WriteAllText(path, json.ToJsonString());
            var future = new AppPreferences(path); Assert.That(future.Interface, Is.EqualTo(InterfaceStyle.Retro)); Assert.That(future.TextPercent, Is.EqualTo(150)); Assert.That(future.Problem, Is.Null);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void GreenStylesActuallyChangeThePaletteAndSwitchingBackRestoresThePrincipalStyle()
    {
        nint context = ImGui.CreateContext();
        try
        {
            UiTheme.Apply(); var original = ImGui.GetStyle().Colors[(int)ImGuiCol.Text]; var accent = UiTheme.Accent;
            UiTheme.Apply(appearance: InterfaceStyle.Terminal);
            var green = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
            Assert.That(green.Y, Is.GreaterThan(green.X * 1.5f)); Assert.That(ImGui.GetStyle().WindowRounding, Is.Zero);
            UiTheme.Apply(appearance: InterfaceStyle.Hardcore); Assert.That(UiTheme.Terminal, Is.True);
            UiTheme.Apply(1, true, InterfaceStyle.Hardcore); Assert.That(ImGui.GetStyle().Colors[(int)ImGuiCol.Text].X, Is.EqualTo(1));
            UiTheme.Apply(); Assert.That(ImGui.GetStyle().Colors[(int)ImGuiCol.Text], Is.EqualTo(original)); Assert.That(UiTheme.Accent, Is.EqualTo(accent));
        }
        finally { ImGui.DestroyContext(context); }
    }
}
