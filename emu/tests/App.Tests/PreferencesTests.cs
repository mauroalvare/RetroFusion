using ImGuiNET;
using Raylib_cs;
using Retro.App;
using Retro.Systems.Spectrum;

namespace App.Tests;

public sealed class PreferencesTests
{
    private string _folder = "";
    private string PathOf(string name) => Path.Combine(_folder, name);

    [SetUp]
    public void CreateFolder() { _folder = Path.Combine(Path.GetTempPath(), "retro-prefs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_folder); }

    [TearDown]
    public void DeleteFolder() { foreach (string file in Directory.GetFiles(_folder)) File.Delete(file); Directory.Delete(_folder); }

    [Test]
    public void PreferencesPersistOnlyInTheirOwnFileAndUnreadableFilesArePreserved()
    {
        string path = PathOf("preferencias.json");
        var prefs = new AppPreferences(path)
        {
            TextPercent = 150, HighContrast = true, IntegerScale = false, Fullscreen = true, PowerSaving = false, ShowFps = true,
            SpectrumJoystick = JoystickMode.Sinclair1, C64Port = 1, TypeLoad = false, TurboWhileLoading = false,
        };
        Assert.That(prefs.Save(), Is.True);
        var loaded = new AppPreferences(path);
        Assert.That(loaded.Problem, Is.Null);
        Assert.That((loaded.TextPercent, loaded.HighContrast, loaded.IntegerScale, loaded.Fullscreen, loaded.PowerSaving, loaded.ShowFps),
            Is.EqualTo((150, true, false, true, false, true)));
        Assert.That((loaded.SpectrumJoystick, loaded.C64Port, loaded.TypeLoad, loaded.TurboWhileLoading),
            Is.EqualTo((JoystickMode.Sinclair1, 1, false, false)));
        Assert.That(loaded.TextScale, Is.EqualTo(1.5f));

        loaded.TextPercent = 110; Assert.That(loaded.Save(), Is.False);
        loaded.TextPercent = 100; loaded.C64Port = 3; Assert.That(loaded.Save(), Is.False);
        Assert.That(new AppPreferences(path).TextPercent, Is.EqualTo(150));

        File.WriteAllText(path, "broken");
        var unreadable = new AppPreferences(path);
        Assert.That(unreadable.Problem, Is.Not.Null);
        Assert.That((unreadable.TextPercent, unreadable.IntegerScale, unreadable.PowerSaving), Is.EqualTo((100, true, true)));
        unreadable.TextPercent = 200; Assert.That(unreadable.Save(), Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo("broken"));

        var temporary = new AppPreferences(path, persist: false) { TextPercent = 175 };
        Assert.That(temporary.Save(), Is.True);
        Assert.That(File.ReadAllText(path), Is.EqualTo("broken"));
        Assert.That(Directory.GetFiles(_folder), Has.Length.EqualTo(1));
    }

    [TestCase("{\"Version\":2,\"TextPercent\":100,\"C64Port\":2,\"SpectrumJoystick\":\"Kempston\"}")]
    [TestCase("{\"Version\":1,\"TextPercent\":100,\"C64Port\":2,\"SpectrumJoystick\":\"1\"}")]
    [TestCase("{\"Version\":1,\"TextPercent\":100,\"C64Port\":2,\"SpectrumJoystick\":\"Atari\"}")]
    [TestCase("{\"Version\":1,\"TextPercent\":100,\"C64Port\":2}")]
    [TestCase("{\"Version\":1,\"TextPercent\":0,\"C64Port\":2,\"SpectrumJoystick\":\"Kempston\"}")]
    public void UnknownVersionsAndValuesAreNotLoadedNorOverwritten(string json)
    {
        string path = PathOf("preferencias.json");
        File.WriteAllText(path, json);
        var prefs = new AppPreferences(path);
        Assert.That(prefs.Problem, Is.Not.Null);
        Assert.That((prefs.TextPercent, prefs.SpectrumJoystick, prefs.C64Port), Is.EqualTo((100, JoystickMode.Kempston, 2)));
        Assert.That(prefs.Save(), Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo(json));
    }

    [Test]
    public void OversizedFilesAreIgnoredAndDefaultsRestoreEveryOption()
    {
        string path = PathOf("preferencias.json");
        File.WriteAllText(path, new string(' ', 20000));
        Assert.That(new AppPreferences(path).Problem, Is.Not.Null);
        var prefs = new AppPreferences(PathOf("otra.json"))
        { TextPercent = 200, HighContrast = true, ShowFps = true, PowerSaving = false, SpectrumJoystick = JoystickMode.Cursor, C64Port = 1 };
        prefs.Defaults();
        Assert.That((prefs.TextPercent, prefs.HighContrast, prefs.IntegerScale, prefs.Fullscreen, prefs.PowerSaving, prefs.ShowFps),
            Is.EqualTo((100, false, true, false, true, false)));
        Assert.That((prefs.SpectrumJoystick, prefs.C64Port, prefs.TypeLoad, prefs.TurboWhileLoading), Is.EqualTo((JoystickMode.Kempston, 2, true, true)));
    }

    [TestCase(60)] [TestCase(144)] [TestCase(240)]
    public void ARunningGameKeepsTheMonitorRateWhileAnIdleWindowSlowsDown(int monitor)
    {
        int full = Math.Max(60, monitor);
        foreach (bool focused in new[] { true, false })
            foreach (bool minimized in new[] { true, false })
            {
                Assert.That(FramePacing.TargetFps(monitor, saving: true, emulating: true, focused, minimized), Is.EqualTo(full));
                Assert.That(FramePacing.TargetFps(monitor, saving: false, emulating: false, focused, minimized), Is.EqualTo(full));
            }
        Assert.That(FramePacing.TargetFps(monitor, true, emulating: false, focused: true, minimized: false), Is.EqualTo(FramePacing.Interface));
        Assert.That(FramePacing.TargetFps(monitor, true, emulating: false, focused: false, minimized: false), Is.EqualTo(FramePacing.Background));
        Assert.That(FramePacing.TargetFps(monitor, true, emulating: false, focused: true, minimized: true), Is.EqualTo(FramePacing.Minimized));
        Assert.That(FramePacing.TargetFps(0, true, emulating: true, focused: true, minimized: false), Is.EqualTo(60));
    }

    [Test]
    public void ToolShortcutsNeedShiftWhereCtrlIsAKeyOfTheEmulatedComputer()
    {
        var zx = new Spectrum48(new byte[0x4000]);
        Assert.That(InputMapper.ToolShortcutsNeedShift(zx, fusion: false), Is.True);
        Assert.That(InputMapper.ToolShortcutsNeedShift(zx, fusion: true), Is.False);
        Assert.That(InputMapper.ToolShortcutsNeedShift(null, fusion: false), Is.False);
    }

    [Test]
    public void OnlyTheCompleteToolChordIsKeptFromTheMachine()
    {
        bool Held(params KeyboardKey[] keys) => InputMapper.ToolChordHeld(keys.Contains);
        Assert.That(Held(KeyboardKey.LeftControl, KeyboardKey.LeftShift, KeyboardKey.M), Is.True);
        Assert.That(Held(KeyboardKey.RightControl, KeyboardKey.RightShift, KeyboardKey.K), Is.True);
        Assert.That(Held(KeyboardKey.LeftControl, KeyboardKey.M), Is.False, "SYMBOL SHIFT + M types a full stop on the Spectrum.");
        Assert.That(Held(KeyboardKey.LeftShift, KeyboardKey.L), Is.False);
        Assert.That(Held(KeyboardKey.LeftControl, KeyboardKey.LeftShift, KeyboardKey.A), Is.False);
    }

    [TestCase("3E 02 D3 FE", new byte[] { 0x3E, 0x02, 0xD3, 0xFE })]
    [TestCase("0x3e,$02\tff", new byte[] { 0x3E, 0x02, 0xFF })]
    [TestCase("", new byte[0])]
    public void TestProgramBytesAcceptHexPairs(string text, byte[] expected) =>
        Assert.That(TestProgram.ParseHex(text), Is.EqualTo(expected));

    [TestCase("123")] [TestCase("ZZ")] [TestCase("0x")] [TestCase("$")] [TestCase("3E -1")] [TestCase("+1")]
    public void InvalidTestProgramBytesAreRejectedInsteadOfThrowing(string text) =>
        Assert.That(TestProgram.ParseHex(text), Is.Null);

    [Test]
    public void TextSizeScalesFromTheUnscaledStyleEveryTime()
    {
        var context = ImGui.CreateContext();
        try
        {
            UiTheme.Apply(1, false);
            var style = ImGui.GetStyle();
            var padding = style.WindowPadding; float scrollbar = style.ScrollbarSize; var text = style.Colors[(int)ImGuiCol.Text];
            UiTheme.Apply(2, true);
            Assert.That(style.WindowPadding, Is.EqualTo(padding * 2));
            Assert.That(style.ScrollbarSize, Is.EqualTo(scrollbar * 2));
            Assert.That(style.FrameBorderSize, Is.EqualTo(1));
            Assert.That(UiTheme.StatusBarHeight, Is.EqualTo(48));
            Assert.That(style.Colors[(int)ImGuiCol.Text], Is.Not.EqualTo(text));
            UiTheme.Apply(2, true);
            Assert.That(style.WindowPadding, Is.EqualTo(padding * 2), "Applying the same size again must not grow the layout.");
            UiTheme.Apply(1, false);
            Assert.That((style.WindowPadding, style.ScrollbarSize, style.FrameBorderSize), Is.EqualTo((padding, scrollbar, 0f)));
            Assert.That(style.Colors[(int)ImGuiCol.Text], Is.EqualTo(text));
            Assert.That(UiTheme.Px(10), Is.EqualTo(10));
        }
        finally { UiTheme.Apply(1, false); ImGui.DestroyContext(context); }
    }
}
