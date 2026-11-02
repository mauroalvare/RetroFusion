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
    public void ConsoleSettingsAndTheChosenFolderSurviveAndOlderOrFutureFilesStayReadable()
    {
        string path = Path.Combine(Path.GetTempPath(), "retro-preferences-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var saved = new AppPreferences(path) { ConsoleCursor = TerminalCursor.Underline, ConsoleBlink = false, ConsoleScanlines = true, ConsoleHistory = false, LibraryFolder = "C:\\Mis juegos" };
            Assert.That(saved.Save(), Is.True);
            var read = new AppPreferences(path);
            Assert.That(read.ConsoleCursor, Is.EqualTo(TerminalCursor.Underline)); Assert.That(read.ConsoleBlink, Is.False);
            Assert.That(read.ConsoleScanlines, Is.True); Assert.That(read.ConsoleHistory, Is.False); Assert.That(read.LibraryFolder, Is.EqualTo("C:\\Mis juegos"));
            // A file written before these settings existed.
            File.WriteAllText(path, "{\"Version\":1,\"TextPercent\":150,\"HighContrast\":false,\"IntegerScale\":true,\"Fullscreen\":false,\"PowerSaving\":true,\"ShowFps\":false,\"SpectrumJoystick\":\"Kempston\",\"C64Port\":2,\"TypeLoad\":true,\"TurboWhileLoading\":true}");
            var older = new AppPreferences(path);
            Assert.That(older.Problem, Is.Null); Assert.That(older.TextPercent, Is.EqualTo(150));
            Assert.That(older.ConsoleCursor, Is.EqualTo(TerminalCursor.Block)); Assert.That(older.ConsoleBlink, Is.True); Assert.That(older.LibraryFolder, Is.Null);
            // A cursor from a later version keeps the rest of the file.
            File.WriteAllText(path, File.ReadAllText(path).TrimEnd('}') + ",\"ConsoleCursor\":\"holograma\",\"ConsoleHistory\":false}");
            var future = new AppPreferences(path);
            Assert.That(future.Problem, Is.Null); Assert.That(future.ConsoleCursor, Is.EqualTo(TerminalCursor.Block));
            Assert.That(future.ConsoleHistory, Is.False); Assert.That(future.TextPercent, Is.EqualTo(150));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void OptionContractsSayTheirDefaultValuesAndScope()
    {
        int volume = 40; bool flag = true;
        var number = AppOption.Number("volumen", "Volumen", "Sonido", "d", () => volume, value => volume = value, 0, 100) with { Default = "60" };
        var toggle = AppOption.Toggle("marca", "Marca", "G", "d", () => flag, value => flag = value) with { Default = "on" };
        var choice = AppOption.Choice("modo", "Modo", "G", "d", () => "a", _ => { }, new("a", "Uno"), new("b", "Dos"));
        Assert.That(number.Modified, Is.True); Assert.That(number.Values, Is.EqualTo("0..100")); Assert.That(number.Write(number.Default!), Is.Null); Assert.That(number.Modified, Is.False);
        Assert.That(toggle.Modified, Is.False); Assert.That(toggle.Values, Is.EqualTo("on, off"));
        Assert.That(toggle.Completions().Select(item => item.Value), Is.EqualTo(new[] { "on", "off", "restablecer" }));
        Assert.That(choice.Modified, Is.False, "Without a known default nothing claims to be modified.");
        Assert.That(choice.Completions().Select(item => item.Detail), Is.EqualTo(new[] { "Uno", "Dos" }));
        Assert.That(choice.ScopeName, Does.Contain("todos los estilos"));
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
