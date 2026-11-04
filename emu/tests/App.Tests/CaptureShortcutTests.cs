using ImGuiNET;
using Raylib_cs;
using Retro.App;
using Retro.Core;
using StbImageSharp;

namespace App.Tests;

/// <summary>Configurable capture shortcut and one folder per game, without a native window.</summary>
[NonParallelizable]
public sealed class CaptureShortcutTests
{
    private string _folder = "";

    [SetUp]
    public void CreateFolder() { _folder = Path.Combine(Path.GetTempPath(), "retro-capture-folders-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_folder); }

    [TearDown]
    public void DeleteFolder()
    {
        UiText.Language = "es";
        foreach (string file in Directory.GetFiles(_folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_folder, recursive: true);
    }

    [TestCase("F1", "F1")]
    [TestCase("f1", "F1")]
    [TestCase("ctrl + shift + s", "Ctrl+Shift+S")]
    [TestCase("Control+F1", "Ctrl+F1")]
    [TestCase("Shift+F1", "Shift+F1")]
    [TestCase("Mayús+Ctrl+F1", "Ctrl+Shift+F1")]
    [TestCase("Pausa", "Pause")]
    [TestCase("Ctrl+9", "Ctrl+9")]
    [TestCase("Ctrl+Q", "Ctrl+Q")]
    public void FreeChordsAreAcceptedAndStoredCanonically(string text, string stored)
    {
        Assert.That(KeyChord.TryParse(text, out var chord), Is.Null);
        Assert.That(chord.ToString(), Is.EqualTo(stored));
        Assert.That(KeyChord.TryParse(chord.ToString(), out var again), Is.Null);
        Assert.That(again, Is.EqualTo(chord), "The stored text reads back as the same chord.");
    }

    [TestCase("", "Escribí una tecla")]
    [TestCase("F12", "depurador")]
    [TestCase("Ctrl+F12", "depurador")]
    [TestCase("Shift+F5", "reiniciar")]
    [TestCase("Ctrl+F2", "guardar el estado")]
    [TestCase("F11", "pantalla completa")]
    [TestCase("F9", "pausar")]
    [TestCase("S", "Sin Ctrl")]
    [TestCase("Shift+S", "Sin Ctrl")]
    [TestCase("7", "Sin Ctrl")]
    [TestCase("Ctrl+O", "abrir un juego")]
    [TestCase("Ctrl+Shift+T", "consola")]
    [TestCase("Ctrl+C", "editar texto")]
    [TestCase("Ctrl+F", "buscar en la biblioteca")]
    [TestCase("Alt+F1", "Alt abre los menús")]
    [TestCase("Win+F1", "modificador")]
    [TestCase("Impr Pant", "sistema operativo")]
    [TestCase("Ctrl+Pause", "Pausa sola")]
    [TestCase("Ctrl+Space", "maneja el juego")]
    [TestCase("Tab", "maneja el juego")]
    [TestCase("Ctrl+", "Escribí una tecla")]
    [TestCase("Ctrl+Shift", "Escribí una tecla")]
    public void ChordsThatReachTheGameOrAnotherActionAreRejected(string text, string reason)
    {
        Assert.That(KeyChord.TryParse(text, out _), Does.Contain(reason));
    }

    [Test]
    public void RejectionsAreExplainedInEnglishToo()
    {
        UiText.Language = "en";
        Assert.That(KeyChord.TryParse("F12", out _), Is.EqualTo("F12 is already used to open the debugger, also with Ctrl or Shift."));
        Assert.That(KeyChord.TryParse("Ctrl+L", out _), Is.EqualTo("Ctrl+L is already used to go to the library."));
        Assert.That(KeyChord.TryParse("S", out _), Does.StartWith("Without Ctrl"));
        Assert.That(new KeyChord("Pause").Label, Is.EqualTo("Pause"));
        UiText.Language = "es";
        Assert.That(new KeyChord("Pause").Label, Is.EqualTo("Pausa"));
    }

    [Test]
    public void TheShortcutTakesTheSameExtraModifierAsTheOtherEmulatorKeys()
    {
        var f1 = KeyChord.CaptureDefault;
        Assert.That(f1.ToString(), Is.EqualTo("F1"));
        Assert.That(f1.For(machineOwnsF1: false, ctrlIsMachineKey: false), Is.EqualTo(f1), "NES, arcade, Spectrum and fusions: F1 alone.");
        Assert.That(f1.For(machineOwnsF1: false, ctrlIsMachineKey: true), Is.EqualTo(f1), "On the Spectrum F1 is not a machine key.");
        Assert.That(f1.For(machineOwnsF1: true, ctrlIsMachineKey: true).ToString(), Is.EqualTo("Ctrl+F1"), "PC and C64 originals own F1.");
        Assert.That(new KeyChord("F1", Ctrl: true).For(true, true).ToString(), Is.EqualTo("Ctrl+F1"));
        Assert.That(new KeyChord("S", Ctrl: true).For(false, true).ToString(), Is.EqualTo("Ctrl+Shift+S"), "Ctrl alone types SYMBOL SHIFT or C=.");
        Assert.That(new KeyChord("S", Ctrl: true).For(false, false).ToString(), Is.EqualTo("Ctrl+S"));
        Assert.That(new KeyChord("Pause").For(true, true).ToString(), Is.EqualTo("Pause"), "No machine reads Pause.");
        foreach (var chord in new[] { f1, new KeyChord("S", true), new KeyChord("Pause"), new KeyChord("F1", false, true) })
            foreach (bool owns in new[] { false, true })
                foreach (bool ctrl in new[] { false, true })
                    Assert.That(chord.For(owns, ctrl).Problem(), Is.Null, "An adapted chord is still free: " + chord.For(owns, ctrl));
    }

    [Test]
    public void APressNeedsExactModifiersAndNeverRepeats()
    {
        var chord = new KeyChord("F1", Ctrl: true);
        bool PressedWith(bool ctrl, bool shift, bool other, ImGuiKey key) => chord.Pressed(ctrl, shift, other, pressed => pressed == key);
        Assert.That(PressedWith(true, false, false, ImGuiKey.F1), Is.True);
        Assert.That(PressedWith(false, false, false, ImGuiKey.F1), Is.False, "F1 alone stays a PC key.");
        Assert.That(PressedWith(true, true, false, ImGuiKey.F1), Is.False);
        Assert.That(PressedWith(true, false, true, ImGuiKey.F1), Is.False, "AltGr arrives as Ctrl+Alt.");
        Assert.That(PressedWith(true, false, false, ImGuiKey.F2), Is.False);
        // Pressed reads ImGui's edge with repeat disabled: a held key reports false after its first frame.
        Assert.That(chord.Pressed(true, false, false, _ => false), Is.False);
    }

    [Test]
    public void ThePickerWaitsForTheMainKeyAndExplainsInvalidOnes()
    {
        static (KeyChord? Chord, string? Problem, bool Cancel) Press(bool ctrl, bool shift, bool other, params ImGuiKey[] keys) =>
            KeyChord.Listen(ctrl, shift, other, keys.Contains);
        Assert.That(Press(false, false, false), Is.EqualTo(((KeyChord?)null, (string?)null, false)));
        Assert.That(Press(true, false, false, ImGuiKey.LeftCtrl).Chord, Is.Null, "Ctrl alone waits for the main key.");
        Assert.That(Press(false, false, false, ImGuiKey.Escape).Cancel, Is.True);
        var chosen = Press(true, true, false, ImGuiKey.LeftShift, ImGuiKey.S);
        Assert.That((chosen.Chord.ToString(), chosen.Problem), Is.EqualTo(("Ctrl+Shift+S", (string?)null)));
        Assert.That(Press(false, false, false, ImGuiKey.Pause).Chord, Is.EqualTo(new KeyChord("Pause")));
        var f12 = Press(false, false, false, ImGuiKey.F12);
        Assert.That(f12.Problem, Does.Contain("depurador"));
        Assert.That(Press(false, false, false, ImGuiKey.Space).Problem, Does.Contain("maneja el juego"));
        Assert.That(Press(false, false, true, ImGuiKey.F1).Problem, Does.Contain("Alt"));
        Assert.That(Press(false, false, false, ImGuiKey.Keypad5).Problem, Does.Contain("maneja el juego"));
    }

    [Test]
    public void TheShortcutOptionSharesValidationWithTheTerminal()
    {
        var stored = KeyChord.CaptureDefault;
        var option = AppOption.Shortcut("atajo-captura", "Atajo", "Controles", "d", () => stored, chord => stored = chord) with { Default = "F1" };
        Assert.That(option.Kind, Is.EqualTo(AppOptionKind.Shortcut));
        Assert.That((option.Read(), option.Modified), Is.EqualTo(("F1", false)));
        Assert.That(option.Write("ctrl+shift+q"), Is.Null);
        Assert.That((option.Read(), option.Modified), Is.EqualTo(("Ctrl+Shift+Q", true)));
        Assert.That(option.Write("F5"), Does.Contain("reiniciar"));
        Assert.That(option.Read(), Is.EqualTo("Ctrl+Shift+Q"), "A rejected chord keeps the previous one.");
        Assert.That(option.Write(option.Default!), Is.Null);
        Assert.That(option.Read(), Is.EqualTo("F1"));
        Assert.That(option.Values, Does.Contain("F1"));
        Assert.That(option.Completions().Select(item => item.Value), Does.Contain("Ctrl+F1").And.Contain("restablecer"));
    }

    [Test]
    public void PreferencesKeepTheShortcutOlderFilesAndValuesFromOtherVersions()
    {
        string path = Path.Combine(_folder, "preferencias.json");
        var prefs = new AppPreferences(path) { TextPercent = 150 };
        Assert.That(prefs.Save(), Is.True);
        Assert.That(File.ReadAllText(path), Does.Contain("\"CaptureShortcut\":null"), "The default is not pinned in the file.");
        prefs.CaptureShortcut = new KeyChord("S", Ctrl: true, Shift: true);
        Assert.That(prefs.Save(), Is.True);
        var reloaded = new AppPreferences(path);
        Assert.That((reloaded.CaptureShortcut.ToString(), reloaded.TextPercent, reloaded.Problem), Is.EqualTo(("Ctrl+Shift+S", 150, (string?)null)));

        // A file from 0.4.1 has no shortcut field: everything else loads and the shortcut is F1.
        string withShortcut = File.ReadAllText(path);
        Assert.That(System.Text.Json.JsonDocument.Parse(withShortcut).RootElement.GetProperty("CaptureShortcut").GetString(), Is.EqualTo("Ctrl+Shift+S"));
        File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(withShortcut, ",\"CaptureShortcut\":\"[^\"]*\"", ""));
        Assert.That(File.ReadAllText(path), Does.Not.Contain("CaptureShortcut"));
        var older = new AppPreferences(path);
        Assert.That((older.CaptureShortcut, older.TextPercent, older.Problem), Is.EqualTo((KeyChord.CaptureDefault, 150, (string?)null)));

        // A chord this version rejects (written by another one) is used as F1 now but survives unrelated saves.
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"TextPercent\":150", "\"TextPercent\":150,\"CaptureShortcut\":\"Alt+F13\""));
        var foreign = new AppPreferences(path);
        Assert.That((foreign.CaptureShortcut, foreign.Problem), Is.EqualTo((KeyChord.CaptureDefault, (string?)null)));
        foreign.ShowFps = true;
        Assert.That(foreign.Save(), Is.True);
        Assert.That(System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("CaptureShortcut").GetString(), Is.EqualTo("Alt+F13"));
        foreign.CaptureShortcut = new KeyChord("Pause");
        Assert.That(foreign.Save(), Is.True);
        Assert.That(new AppPreferences(path).CaptureShortcut, Is.EqualTo(new KeyChord("Pause")));

        foreign.Defaults();
        Assert.That(foreign.CaptureShortcut, Is.EqualTo(KeyChord.CaptureDefault), "Restablecer todo also restores the shortcut.");
        foreign.CaptureShortcut = new KeyChord("F12");
        Assert.That(foreign.CaptureShortcut, Is.EqualTo(KeyChord.CaptureDefault), "The setter never stores a reserved chord.");
    }

    [Test]
    public void EachGameGetsItsOwnValidFolderAndSameTitlesStayApart()
    {
        var nesA = new CaptureSubject("Faro del taller", "nes", "aaaa");
        var nesB = new CaptureSubject("Faro del taller", "nes", "bbbb");
        var c64 = new CaptureSubject("Faro del taller", "c64", "aaaa");
        var fusion = new CaptureSubject("Faro del taller", "fusion", "fusion\nfaro");
        var created = new CaptureSubject("Faro del taller", "creado", "8bits\nfaro");
        string[] folders = [nesA.Folder, nesB.Folder, c64.Folder, fusion.Folder, created.Folder];
        Assert.That(folders, Is.Unique);
        Assert.That(nesA.Folder, Does.Match(@"^Faro del taller \[nes-[0-9a-f]{6}\]$"));
        Assert.That(new CaptureSubject("Faro del taller", "nes", "aaaa").Folder, Is.EqualTo(nesA.Folder), "Stable across sessions.");
        Assert.That(fusion.Folder, Does.Contain("[fusion-")); Assert.That(created.Folder, Does.Contain("[creado-"));
        Assert.That(CaptureSubject.NoGame("ZX Spectrum 48K").Folder, Is.EqualTo("Sin juego activo"));
        Assert.That(CaptureSubject.NoGame(null).Title, Is.EqualTo("captura"));

        string[] problematic = ["Juego: prueba/1? <final>|*\"", "CON", "nul.txt", "  .. espacios ..  ", "Tab\tde\ncontrol", string.Concat(Enumerable.Repeat("🎮", 40)), "a" + string.Concat(Enumerable.Repeat("🎮", 40)), "...", "Ñandú — versión 2"];
        foreach (string title in problematic)
        {
            string folder = new CaptureSubject(title, "nes", title).Folder;
            Assert.That(folder.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']), Is.EqualTo(-1), folder);
            Assert.That(folder.Any(char.IsControl), Is.False, folder);
            Assert.That(folder, Does.Not.EndWith(".").And.Not.EndWith(" "));
            Assert.That(ScreenCapture.SafeName(title), Does.Not.Match("^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\..*)?$"));
            Assert.That(ScreenCapture.SafeName(title).Length, Is.LessThanOrEqualTo(60));
            Assert.That(char.IsHighSurrogate(ScreenCapture.SafeName(title)[^1]), Is.False, "Half an emoji is not a valid name: " + title);
            Directory.CreateDirectory(Path.Combine(_folder, folder));
        }
        Assert.That(Directory.GetDirectories(_folder), Has.Length.EqualTo(problematic.Length));
    }

    [Test]
    public void CapturesGoToTheGameFolderAndNeverReplaceEarlierOnes()
    {
        var frame = Frame(5, 3, 7);
        var now = new DateTime(2026, 11, 3, 22, 10, 5);
        File.WriteAllBytes(Path.Combine(_folder, "anterior-20261001-101010.png"), [1, 2, 3]);
        var subject = new CaptureSubject("Faro del taller", "nes", "hash-a");
        string first = ScreenCapture.Save(frame, _folder, subject, now);
        string expectedFolder = Path.Combine(_folder, subject.Folder);
        Assert.That(first, Is.EqualTo(Path.Combine(expectedFolder, "Faro del taller-20261103-221005.png")));
        string occupied = Path.Combine(expectedFolder, "Faro del taller-20261103-221005-2.png");
        File.WriteAllBytes(occupied, [9, 9]);
        string second = ScreenCapture.Save(Frame(5, 3, 8), _folder, subject, now);
        Assert.That(Path.GetFileName(second), Is.EqualTo("Faro del taller-20261103-221005-3.png"));
        Assert.That(File.ReadAllBytes(occupied), Is.EqualTo(new byte[] { 9, 9 }), "A file already there is never replaced.");
        Assert.That(File.ReadAllBytes(Path.Combine(_folder, "anterior-20261001-101010.png")), Is.EqualTo(new byte[] { 1, 2, 3 }), "Older flat captures stay in the root.");

        var image = ImageResult.FromMemory(File.ReadAllBytes(first), ColorComponents.RedGreenBlue);
        Assert.That((image.Width, image.Height), Is.EqualTo((5, 3)));
        for (int i = 0; i < frame.Pixels.Length; i++)
            Assert.That(image.Data[i * 3], Is.EqualTo((byte)(frame.Pixels[i] >> 16)));

        string general = ScreenCapture.Save(frame, _folder, CaptureSubject.NoGame("ZX Spectrum 48K"), now);
        Assert.That(general, Is.EqualTo(Path.Combine(_folder, "Sin juego activo", "ZX Spectrum 48K-20261103-221005.png")));
        var twin = new CaptureSubject("Faro del taller", "nes", "hash-b");
        Assert.That(Path.GetDirectoryName(ScreenCapture.Save(frame, _folder, twin, now)), Is.Not.EqualTo(expectedFolder));
    }

    [Test]
    public void AFailedWriteKeepsEveryEarlierCaptureAndLeavesNoPartialFile()
    {
        var frame = Frame(4, 4, 3);
        var subject = new CaptureSubject("Sonda 4", "zx48", "hash");
        string kept = ScreenCapture.Save(frame, _folder, subject, new DateTime(2026, 11, 3, 1, 2, 3));
        byte[] before = File.ReadAllBytes(kept);

        // The game's folder name is taken by a file: nothing can be created there.
        var blocked = new CaptureSubject("Pulso de Marte", "nes", "hash");
        File.WriteAllBytes(Path.Combine(_folder, blocked.Folder), [7]);
        Assert.That(() => ScreenCapture.Save(frame, _folder, blocked, DateTime.Now), Throws.InstanceOf<IOException>());
        Assert.That(File.ReadAllBytes(Path.Combine(_folder, blocked.Folder)), Is.EqualTo(new byte[] { 7 }));

        // The root itself is a file.
        string fileRoot = Path.Combine(_folder, "raiz-archivo");
        File.WriteAllBytes(fileRoot, [5]);
        Assert.That(() => ScreenCapture.Save(frame, fileRoot, subject, DateTime.Now), Throws.InstanceOf<IOException>());

        // A frame whose pixels do not match its size fails while writing: the partial file is removed.
        var broken = new VideoFrame(4, 4);
        typeof(VideoFrame).GetField("<Width>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(broken, 5);
        Assert.That(broken.Width, Is.EqualTo(5));
        Assert.That(() => ScreenCapture.Save(broken, _folder, subject, new DateTime(2026, 11, 3, 1, 2, 4)), Throws.ArgumentException);
        Assert.That(Directory.GetFiles(Path.Combine(_folder, subject.Folder)), Is.EqualTo(new[] { kept }));
        Assert.That(File.ReadAllBytes(kept), Is.EqualTo(before));
    }

    private static VideoFrame Frame(int width, int height, int seed)
    {
        var frame = new VideoFrame(width, height);
        var random = new Random(seed);
        for (int i = 0; i < frame.Pixels.Length; i++) frame.Pixels[i] = unchecked((int)0xFF000000) | random.Next(0x1000000);
        return frame;
    }
}
