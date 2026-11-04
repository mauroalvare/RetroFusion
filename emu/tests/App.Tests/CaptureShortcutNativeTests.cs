using System.Security.Cryptography;
using ImGuiNET;
using Retro.App;
using Retro.Core;
using Retro.Fusion;
using Retro.Studio;
using StbImageSharp;

namespace App.Tests;

/// <summary>
/// The capture shortcut and the per-game folders through the real frame loop: hidden window, muted audio,
/// own games and preferences in temporary folders, keys delivered as ImGui events.
/// </summary>
[NonParallelizable]
public sealed class CaptureShortcutNativeTests
{
    private const string Reason = "Requiere gráficos nativos: ventana oculta, audio silenciado y datos propios.";
    private string _qa = "";

    [SetUp] public void Folder() => _qa = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-capturas-qa-" + Guid.NewGuid().ToString("N"))).FullName;
    [TearDown]
    public void Clean()
    {
        UiText.Language = "es";
        Assert.That(Path.GetFileName(_qa), Does.StartWith("retro-capturas-qa-"));
        Directory.Delete(_qa, true);
    }

    private static string Root(NativeHarness ui) => Path.Combine(ui.Folder, "out", "autotest-capturas");
    private static string[] Captures(NativeHarness ui) => Directory.Exists(Root(ui))
        ? Directory.GetFiles(Root(ui), "*.png", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray() : [];
    private static Dictionary<string, string> Hashes(NativeHarness ui) =>
        Captures(ui).ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    private static string NewCapture(NativeHarness ui, string[] before)
    {
        var added = Captures(ui).Except(before).ToArray();
        Assert.That(added, Has.Length.EqualTo(1), "Exactly one new capture: " + string.Join(", ", added));
        return added[0];
    }

    private static void Open(NativeHarness ui, string path)
    {
        ui.Call("OpenFile", path); ui.Frames(4); ui.Call("OpenPlay"); ui.Frames(3);
        ((EmulatorHost)ui.Get("_host")).Paused = true; ui.Frames(2);
    }

    /// <summary>The PNG holds exactly the emulated frame on screen when the key was pressed.</summary>
    private static void AssertSameAsScreen(NativeHarness ui, string png, int[] pixels, int width, int height)
    {
        var image = ImageResult.FromMemory(File.ReadAllBytes(png), ColorComponents.RedGreenBlue);
        Assert.That((image.Width, image.Height), Is.EqualTo((width, height)));
        for (int i = 0; i < pixels.Length; i++)
            Assert.That((image.Data[i * 3], image.Data[i * 3 + 1], image.Data[i * 3 + 2]),
                Is.EqualTo(((byte)(pixels[i] >> 16), (byte)(pixels[i] >> 8), (byte)pixels[i])), "Pixel " + i + " of " + png);
    }

    private static (int[] Pixels, int Width, int Height) Screen(NativeHarness ui)
    {
        var video = ((EmulatorHost)ui.Get("_host")).CurrentVideo!;
        return ((int[])video.Pixels.Clone(), video.Width, video.Height);
    }

    private static CaptureSubject Subject(NativeHarness ui) => (CaptureSubject)ui.Call("CaptureSubjectNow")!;
    // Table cells wrap to the console width; compare the words, not the line breaks.
    private static string Flat(ProgramTerminal console) => System.Text.RegularExpressions.Regex.Replace(console.Output.AllText(), @"\s+", " ");

    [Explicit(Reason)]
    [Test]
    public void TheShortcutSavesEachGameInItsOwnFolderAndCanBeChangedRestartedAndReset()
    {
        string prefsPath = Path.Combine(_qa, "preferencias.json");
        using var ui = new NativeHarness(1280, 840, 100, "retro", games: 12, "--autotest-preferences", prefsPath);
        var host = (EmulatorHost)ui.Get("_host");
        var prefs = (AppPreferences)ui.Get("_prefs");
        Assert.That(prefs.CaptureShortcut, Is.EqualTo(KeyChord.CaptureDefault));
        Assert.That(File.Exists(prefsPath), Is.False, "Nothing is written until a setting changes.");

        // No game yet: the default Spectrum runs alone. The command palette (same action as the menu) uses the general folder.
        Assert.That((host.LoadedMedia, host.Machine?.Id), Is.EqualTo(((string?)null, "zx48")));
        var palette = (CommandPalette)ui.Get("_commands");
        string[] before = Captures(ui);
        palette.Open(); ui.Frames(3); ui.Type("Guardar captura"); ui.Key(ImGuiKey.Enter); ui.Frames(3);
        string general = NewCapture(ui, before);
        Assert.That(Path.GetFileName(Path.GetDirectoryName(general)), Is.EqualTo(CaptureSubject.GeneralFolder));
        Assert.That(Path.GetFileName(general), Does.StartWith("ZX Spectrum 48K-"));

        // A library original with a problematic title: invalid characters and a device name.
        string gamelist = Path.Combine(ui.Games, "gamelist.xml");
        File.WriteAllText(gamelist, File.ReadAllText(gamelist).Replace("<name>Faro del taller</name>", "<name>CON: Faro &quot;del&quot; taller/&lt;1&gt;?</name>"));
        var library = (GameLibrary)ui.Get("_library");
        library.Rescan(ui.Games);
        Assert.That(SpinWait.SpinUntil(() => { library.Poll(); return !library.Scanning; }, TimeSpan.FromSeconds(10)), Is.True);
        ui.Frames(10);
        Open(ui, Path.Combine(ui.Games, "Faro_del_taller.nes"));
        Assert.That(host.Machine?.Id, Is.EqualTo("nes"));
        Assert.That(((KeyChord)ui.Property("CaptureChord")).ToString(), Is.EqualTo("F1"), "NES: F1 alone.");
        var nesSubject = Subject(ui);
        Assert.That(nesSubject.Title, Is.EqualTo("CON: Faro \"del\" taller/<1>?"));
        var screen = Screen(ui);
        before = Captures(ui);
        ui.Key(ImGuiKey.F1);
        string nesCapture = NewCapture(ui, before);
        Assert.That(Path.GetDirectoryName(nesCapture), Is.EqualTo(Path.Combine(Root(ui), nesSubject.Folder)));
        Assert.That(nesSubject.Folder, Does.Match(@"^CON_ Faro _del_ taller__1__ \[nes-[0-9a-f]{6}\]$"), "Only the exact device name is reserved; the colon becomes _.");
        AssertSameAsScreen(ui, nesCapture, screen.Pixels, screen.Width, screen.Height);
        Assert.That(host.Status, Does.Contain(nesCapture));

        // Holding the key saves once; the same second gets -2 and never replaces the first file.
        before = Captures(ui);
        ImGui.GetIO().AddKeyEvent(ImGuiKey.F1, true); ui.Frames(40); ImGui.GetIO().AddKeyEvent(ImGuiKey.F1, false); ui.Frames(3);
        string held = NewCapture(ui, before);
        Assert.That(Path.GetDirectoryName(held), Is.EqualTo(Path.GetDirectoryName(nesCapture)));
        // While the console owns the keyboard, neither F1 nor typed letters capture; its order does.
        before = Captures(ui);
        ui.Call("OpenTerminal"); ui.Frames(3);
        ui.Key(ImGuiKey.F1); ui.Type("captura");
        Assert.That(Captures(ui), Is.EqualTo(before), "Typing never captures.");
        ui.Key(ImGuiKey.Enter); ui.Frames(3);
        string ordered = NewCapture(ui, before);
        Assert.That(Path.GetDirectoryName(ordered), Is.EqualTo(Path.GetDirectoryName(nesCapture)), "The order shares the game folder.");
        var console = (ProgramTerminal)ui.Property("Terminal");
        Assert.That(console.Output.AllText(), Does.Contain("Captura guardada"));
        ui.Enter("atajos"); Assert.That(Flat(console), Does.Contain("F1").And.Contain("guarda una captura en la carpeta del juego"));
        ui.Enter("opcion atajo-captura");
        Assert.That(console.Output.AllText(), Does.Contain("atajo-captura = F1"));
        ui.Enter("opcion atajo-captura F12");
        Assert.That(console.Output.AllText(), Does.Contain("F12 ya se usa para abrir el depurador"));
        Assert.That(prefs.CaptureShortcut, Is.EqualTo(KeyChord.CaptureDefault));
        ui.Call("LeaveTerminal"); ui.Call("OpenPlay"); host.Paused = true; ui.Frames(3);

        // Preferencias: pressing a reserved key explains why and keeps listening; a free chord replaces F1.
        int beforePreferences = Captures(ui).Length;
        ui.Set("_preferencesOpen", true); ui.Set("_preferencesSearch", "atajo-captura"); ui.Frames(4);
        ui.Click("Cambiar atajo:atajo-captura"); ui.Frames(2);
        Assert.That(ui.Get("_listeningShortcut"), Is.EqualTo("atajo-captura"));
        ui.Key(ImGuiKey.F12); ui.Frames(2);
        Assert.That(((string?)ui.Get("_listeningShortcut"), (bool)ui.Get("_debugMode")), Is.EqualTo(("atajo-captura", false)), "F12 neither changes the shortcut nor opens the debugger.");
        Assert.That(ui.Get("_shortcutNotice")!.ToString(), Does.Contain("depurador"));
        ui.Key(ImGuiKey.Space); ui.Frames(2);
        Assert.That(ui.Get("_listeningShortcut"), Is.EqualTo("atajo-captura"), "Space does not press the Cancel button.");
        ui.Capture("capturas-preferencias-escuchando");
        ui.Key(ImGuiKey.S, ctrl: true, shift: true); ui.Frames(3);
        Assert.That(ui.Get("_listeningShortcut"), Is.Null);
        Assert.That(prefs.CaptureShortcut.ToString(), Is.EqualTo("Ctrl+Shift+S"));
        Assert.That(System.Text.Json.JsonDocument.Parse(File.ReadAllText(prefsPath)).RootElement.GetProperty("CaptureShortcut").GetString(), Is.EqualTo("Ctrl+Shift+S"));
        Assert.That(Captures(ui), Has.Length.EqualTo(beforePreferences), "Choosing the chord does not capture.");
        ui.Capture("capturas-preferencias-guardado");
        ui.Click("Cerrar preferencias"); ui.Frames(2);
        before = Captures(ui);
        ui.Key(ImGuiKey.F1);
        Assert.That(Captures(ui), Is.EqualTo(before), "The previous shortcut stops capturing.");
        ui.Key(ImGuiKey.S, ctrl: true, shift: true);
        string custom = NewCapture(ui, before);
        Assert.That(Path.GetDirectoryName(custom), Is.EqualTo(Path.GetDirectoryName(nesCapture)));
        var commands = (IReadOnlyList<AppCommand>)ui.Property("Commands");
        Assert.That(commands.Single(command => command.Id == "capture").Shortcut, Is.EqualTo("Ctrl+Shift+S"), "Menus and palette show the chosen chord.");

        // Restart: the chord comes back from the file and the same game keeps its folder.
        ui.RestartApp();
        host = (EmulatorHost)ui.Get("_host"); prefs = (AppPreferences)ui.Get("_prefs");
        Assert.That(prefs.CaptureShortcut.ToString(), Is.EqualTo("Ctrl+Shift+S"));
        Open(ui, Path.Combine(ui.Games, "Faro_del_taller.nes"));
        before = Captures(ui);
        ui.Key(ImGuiKey.S, ctrl: true, shift: true);
        Assert.That(Path.GetDirectoryName(NewCapture(ui, before)), Is.EqualTo(Path.GetDirectoryName(nesCapture)));

        // Escape cancels a change; Volver al valor inicial restores F1 and stops pinning it in the file.
        ui.Set("_preferencesOpen", true); ui.Set("_preferencesSearch", "atajo-captura"); ui.Frames(4);
        ui.Click("Cambiar atajo:atajo-captura"); ui.Frames(2); ui.Key(ImGuiKey.Escape); ui.Frames(2);
        Assert.That(((string?)ui.Get("_listeningShortcut"), prefs.CaptureShortcut.ToString()), Is.EqualTo(((string?)null, "Ctrl+Shift+S")));
        ui.Click("Restablecer:atajo-captura"); ui.Frames(2);
        Assert.That(prefs.CaptureShortcut, Is.EqualTo(KeyChord.CaptureDefault));
        Assert.That(System.Text.Json.JsonDocument.Parse(File.ReadAllText(prefsPath)).RootElement.GetProperty("CaptureShortcut").ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Null));
        ui.Click("Cerrar preferencias"); ui.Frames(2);
        ui.RestartApp();
        Assert.That(((AppPreferences)ui.Get("_prefs")).CaptureShortcut, Is.EqualTo(KeyChord.CaptureDefault));
    }

    [Explicit(Reason)]
    [Test]
    public void SameTitlesFusionsCreationsAndFailedWritesKeepEveryCapture()
    {
        using var ui = new NativeHarness(1280, 840, 100, "retro", games: 12);
        var host = (EmulatorHost)ui.Get("_host");
        string romDir = (string)ui.Get("_romDir");

        // Two 8-bit creations from the same template share the title "Mi juego" but not their folder.
        var first = Retro.Ai.CreatedGames.FromTemplate("zx48", romDir);
        var second = Retro.Ai.CreatedGames.FromTemplate("zx48", romDir);
        Assert.That((first.Title, second.Title), Is.EqualTo(("Mi juego", "Mi juego")));
        Assert.That(first.Folder, Is.Not.EqualTo(second.Folder));
        var folders = new List<string>();
        foreach (var created in new[] { first, second })
        {
            Open(ui, created.ProgramPath);
            host.Paused = false; ui.Frames(90); host.Paused = true; ui.Frames(2);
            Assert.That(((KeyChord)ui.Property("CaptureChord")).ToString(), Is.EqualTo("F1"), "On the Spectrum F1 is not a machine key.");
            var subject = Subject(ui);
            Assert.That((subject.Title, subject.Kind), Is.EqualTo(("Mi juego", "creado")));
            var screen = Screen(ui);
            string[] before = Captures(ui);
            ui.Key(ImGuiKey.F1);
            string png = NewCapture(ui, before);
            AssertSameAsScreen(ui, png, screen.Pixels, screen.Width, screen.Height);
            Assert.That(screen.Pixels.Distinct().Count(), Is.GreaterThan(1), "The created game drew something of its own.");
            folders.Add(Path.GetDirectoryName(png)!);
        }
        Assert.That(folders, Is.Unique);
        Assert.That(folders.Select(Path.GetFileName), Has.All.StartWith("Mi juego [creado-"));

        // A C64 original owns F1: Ctrl+F1 captures and F1 alone stays with the machine.
        Open(ui, Directory.GetFiles(ui.Games, "*.prg").Order().First());
        Assert.That(host.C64, Is.Not.Null);
        Assert.That(((KeyChord)ui.Property("CaptureChord")).ToString(), Is.EqualTo("Ctrl+F1"));
        string[] beforeC64 = Captures(ui);
        ui.Key(ImGuiKey.F1);
        Assert.That(Captures(ui), Is.EqualTo(beforeC64));
        ui.Key(ImGuiKey.F1, ctrl: true);
        Assert.That(Path.GetFileName(Path.GetDirectoryName(NewCapture(ui, beforeC64))), Does.Contain("[c64-"));

        // A fusion and a studio creation with the same title, both over an own NES program.
        byte[] rom = new byte[16 + 16384 + 8192];
        rom[0] = 0x4e; rom[1] = 0x45; rom[2] = 0x53; rom[3] = 0x1a; rom[4] = 1; rom[5] = 1;
        rom[16] = 0x4c; rom[17] = 0; rom[18] = 0x80; rom[16 + 0x3ffd] = 0x80;
        var profile = new GameProfile { Id = "capturas-propio", Machine = "nes", Name = "Programa propio" };
        foreach (string kind in new[] { "fusion", "inspired" })
        {
            var game = new StudioGame { Id = "capturas-" + kind, Title = "Taller doble", Kind = kind,
                Script = "function init(){game.state('playing');} function tick(input){} function draw(){}" };
            game.Games = [new() { Slot = "a", Profile = profile.Id }];
            var session = StudioSession.Start(game, new Dictionary<string, GameSource> { [profile.Id] = new(profile, [new("own.nes", rom, "generated-own.nes")], null) }, "");
            Assert.That(session.Error, Is.Null);
            host.StartFusion(session); ui.Call("OpenPlay"); ui.Frames(3); host.Paused = true; ui.Frames(2);
            string[] before = Captures(ui);
            ui.Key(ImGuiKey.F1);
            string png = NewCapture(ui, before);
            Assert.That(Path.GetFileName(Path.GetDirectoryName(png)), Does.StartWith("Taller doble [" + (kind == "fusion" ? "fusion-" : "creado-")));
            Assert.That(Path.GetFileName(png), Does.StartWith("Taller doble-"));
            host.StopFusion();
        }

        // Write failure: a file takes the game's folder name. The error is reported, the game keeps going
        // and every earlier capture is byte for byte the same; once the obstacle is gone it works again.
        Open(ui, Path.Combine(ui.Games, "Bajo_el_puente.nes"));
        Assert.That(host.Machine?.Id, Is.EqualTo("nes"));
        var blocked = Subject(ui);
        string obstacle = Path.Combine(Root(ui), blocked.Folder);
        File.WriteAllBytes(obstacle, [42]);
        var hashes = Hashes(ui);
        ui.Key(ImGuiKey.F1);
        Assert.That(ui.Get("_errorPopup")!.ToString(), Does.Contain("No se pudo guardar la captura").And.Contain("Las capturas anteriores no se modificaron"));
        Assert.That(Hashes(ui), Is.EqualTo(hashes));
        Assert.That(File.ReadAllBytes(obstacle), Is.EqualTo(new byte[] { 42 }));
        ui.Set("_errorPopup", null);
        ui.Call("OpenTerminal"); ui.Frames(2);
        ui.Enter("captura"); ui.Frames(2);
        var console = (ProgramTerminal)ui.Property("Terminal");
        Assert.That(console.Output.AllText(), Does.Contain("No se pudo guardar la captura").And.Not.Contain("Captura guardada"));
        Assert.That(ui.Get("_errorPopup"), Is.Null, "The console reports the failure itself, without a popup over it.");
        Assert.That(Hashes(ui), Is.EqualTo(hashes));
        ui.Call("LeaveTerminal"); ui.Call("OpenPlay"); ui.Frames(2);
        long frames = host.Machine!.FrameCount;
        host.Paused = false; ui.Frames(10);
        Assert.That(host.Machine!.FrameCount, Is.GreaterThan(frames), "The game keeps running after the failure.");
        host.Paused = true; ui.Frames(2);
        File.Delete(obstacle);
        string[] retry = Captures(ui);
        ui.Key(ImGuiKey.F1);
        Assert.That(Path.GetDirectoryName(NewCapture(ui, retry)), Is.EqualTo(obstacle));
        Assert.That(hashes.All(pair => File.Exists(pair.Key)), Is.True);
    }

    [Explicit(Reason)]
    [TestCase(100), TestCase(175)]
    public void EnglishShowsTheConfiguredKeyAndExplainsRejections(int text)
    {
        using var ui = new NativeHarness(1280, 840, text, "retro", games: 12, "--language", "en");
        Open(ui, Path.Combine(ui.Games, "Faro_del_taller.nes"));
        ui.Set("_preferencesOpen", true); ui.Set("_preferencesSearch", "atajo-captura"); ui.Frames(4);
        ui.Click("Cambiar atajo:atajo-captura"); ui.Frames(2);
        ui.Key(ImGuiKey.F12); ui.Frames(2);
        Assert.That(ui.Get("_shortcutNotice")!.ToString(), Does.Contain("F12 is already used to open the debugger"));
        ui.Capture("capturas-en-rechazo-" + text);
        ui.Key(ImGuiKey.Pause); ui.Frames(3);
        Assert.That(ui.Get("_shortcutNotice")!.ToString(), Does.Contain("Shortcut saved: Pause."));
        Assert.That(((KeyChord)ui.Property("CaptureChord")).Label, Is.EqualTo("Pause"));
        ui.Capture("capturas-en-guardado-" + text);
        ui.Click("Cerrar preferencias"); ui.Frames(2);
        var commands = (IReadOnlyList<AppCommand>)ui.Property("Commands");
        Assert.That(commands.Single(command => command.Id == "capture").Shortcut, Is.EqualTo("Pause"));
        string[] before = Captures(ui);
        ui.Key(ImGuiKey.Pause);
        Assert.That(((EmulatorHost)ui.Get("_host")).Status, Does.StartWith("Screenshot saved: "));
        NewCapture(ui, before);
        ui.Call("OpenTerminal"); ui.Frames(2); ui.Enter("shortcuts");
        Assert.That(Flat((ProgramTerminal)ui.Property("Terminal")), Does.Contain("Pause While playing: saves a"), "The narrow console shortens the description with an ellipsis.");
        UiText.Language = "es";
        Assert.That(((KeyChord)ui.Property("CaptureChord")).Label, Is.EqualTo("Pausa"));
    }
}
