using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class NesBatteryNativeTests
{
    private const string Reason = "Ventana oculta, audio deshabilitado, sin IA y con cartucho y datos propios.";
    [TearDown] public void SpanishAgain() => UiText.Language = "es";
    private static EmulatorHost Host(NativeHarness ui) => (EmulatorHost)ui.Get("_host");

    [TestCase("real", 1280, 840, 100, "es"), TestCase("real", 640, 480, 200, "en"), TestCase("explorer", 1280, 840, 125, "en")]
    [Explicit(Reason)]
    public void BatteryCanBeSavedRecoveredAndReloadedThroughRealOrdersAndARealAppRestart(string style, int width, int height, int text, string language)
    {
        using var ui = new NativeHarness(width, height, text, style, games: 1, "--language", language, "--no-audio");
        bool en = language == "en";
        string media = Path.Combine(ui.Folder, "Batería propia.nes"), copy = Path.Combine(ui.Folder, "Copia propia.rfbattery");
        File.WriteAllBytes(media, NesBatteryTests.Image());
        if (style != "real") { ui.Call("OpenTerminal"); ui.Frames(3); }
        ui.Enter((en ? "open" : "abrir") + " \"" + media + "\"");
        Assert.That(Host(ui).Nes!.Peek(0x6000), Is.EqualTo(1), "The real guest program must have written SRAM.");
        ui.Call("OpenTerminal"); ui.Frames(3);
        ui.Enter(en ? "BaTtErY SaVe" : "bAtErIa gUaRdAr");
        Assert.That(File.Exists(Host(ui).Battery!.Path), Is.True, "A mixed-case order must still save immediately.");
        ui.Enter((en ? "battery export" : "bateria exportar") + " \"" + copy + "\"");
        Assert.That(File.Exists(copy), Is.True);
        ui.Enter(en ? "reset hard" : "reiniciar duro");
        Assert.That(Host(ui).Nes!.Peek(0x6000), Is.EqualTo(1), "A hard reset must preserve cartridge battery RAM.");
        ui.Enter(en ? "continue" : "continuar"); ui.Frames(132);
        Assert.That(Host(ui).Nes!.Peek(0x6000), Is.EqualTo(2));
        Assert.That(Host(ui).Battery!.Pending, Is.False, "The real frame loop must autosave.");
        ui.Call("OpenTerminal"); ui.Frames(3);
        ui.Enter((en ? "battery restore" : "bateria recuperar") + " \"" + copy + "\"");
        Assert.That(Host(ui).Nes!.Peek(0x6000), Is.EqualTo(2), "Recovery requires explicit confirmation.");
        ui.Enter((en ? "battery restore" : "bateria recuperar") + " \"" + copy + "\" " + (en ? "replace" : "reemplazar"));
        Assert.That(Host(ui).Nes!.Peek(0x6000), Is.EqualTo(1)); Assert.That(Host(ui).Paused, Is.True);
        Assert.That(File.ReadAllBytes(Host(ui).Battery!.Path), Is.EqualTo(File.ReadAllBytes(copy)));
        Assert.That(File.Exists(Host(ui).Battery!.PreservedPath), Is.True);
        ui.Enter(en ? "battery open" : "bateria abrir"); ui.Frames(4);
        Assert.That(ui.Get("_batteryWindow"), Is.EqualTo(true)); Assert.That(ui.Property("ConsoleOwnsKeyboard"), Is.EqualTo(false));
        Assert.That(ui.Bounds["Cerrar batería"].Visible, Is.True, "The close button must fit even at 640x480/200%.");
        Reveal(ui, "Exportar batería sin sobrescribir");
        ui.Click("Exportar batería sin sobrescribir");
        Assert.That(Host(ui).Battery!.ExportedPath, Is.EqualTo(ui.Get("_batteryCopy")));
        Assert.That(File.Exists(Host(ui).Battery!.ExportedPath), Is.True);
        Reveal(ui, "Confirmar recuperación de batería"); ui.Click("Confirmar recuperación de batería");
        Reveal(ui, "Recuperar batería de la copia");
        long revision = Host(ui).MediaRevision; ui.Click("Recuperar batería de la copia");
        Assert.That(Host(ui).MediaRevision, Is.GreaterThan(revision), "The GUI must use the same recovery path as the terminal.");
        Assert.That(ui.Get("_batteryReplace"), Is.EqualTo(false));
        ui.Capture("bateria-" + style + "-" + text + "-" + language);
        ui.Click("Cerrar batería");
        ui.RestartApp();
        Assert.That(Host(ui).Nes, Is.Null, "The previous machine must not survive the app session.");
        ui.Call("OpenTerminal"); ui.Frames(3);
        ui.Enter((en ? "open" : "abrir") + " \"" + media + "\"");
        Assert.That(Host(ui).Nes!.Peek(0x6000), Is.EqualTo(2), "Fresh app loads 1 from disk, then the guest increments it once.");
        Assert.That(ui.Get("_errorPopup"), Is.Null);
        Assert.That((bool)Raylib.IsWindowHidden(), Is.True); Assert.That(ui.Get("_audioDisabled"), Is.EqualTo(true));
        Assert.That(Host(ui).SoundDeviceMissing, Is.True);
        Assert.That(((Retro.Ai.AiSettings)ui.Get("_ai")).ProvidersDisabled, Is.True);
    }

    [TestCase(1280, 840, 125, "es"), TestCase(640, 480, 200, "en"), Explicit(Reason)]
    public void QuittingWithAnUnwritableBatteryWarnsAndCanBeCancelledWithRealInput(int width, int height, int text, string language)
    {
        using var ui = new NativeHarness(width, height, text, "real", games: 1, "--no-audio", "--language", language);
        string media = Path.Combine(ui.Folder, "Faro.nes"); File.WriteAllBytes(media, NesBatteryTests.Image());
        ui.Enter((language == "en" ? "open" : "abrir") + " \"" + media + "\"");
        var host = Host(ui); string path = host.Battery!.Path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var busy = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            ui.App.RequestQuit(); ui.Frames(4);
            Assert.That(ui.App.QuitRequested, Is.False); Assert.That(ui.Get("_quitNotice"), Does.Contain(language == "en" ? "NES battery" : "batería NES"));
            foreach (string control in new[] { "Volver al programa", "Ver antes de salir", "Confirmar salida" })
                Assert.That(ui.Bounds[control].Visible, Is.True, control + " must fit without scrolling.");
            ui.Capture("bateria-salida-" + text + "-" + language);
            ui.Click("Volver al programa"); Assert.That(ui.App.QuitRequested, Is.False); Assert.That(ui.Get("_quitNotice"), Is.Null);
            Assert.That(host.Nes!.Peek(0x6000), Is.EqualTo(1));
        }
        ui.App.RequestQuit(); Assert.That(ui.App.QuitRequested, Is.True);
        Assert.That(File.Exists(path), Is.True); Assert.That(host.BatteryAtRisk, Is.False);
    }

    private static void Reveal(NativeHarness ui, string control)
    {
        for (int i = 0; i < 80 && !ui.Bounds[control].Visible; i++)
        {
            var body = ui.Bounds["Contenido batería"]; var item = ui.Bounds[control];
            Vector2 center = (body.Min + body.Max) / 2;
            float delta = (item.Min.Y + item.Max.Y) / 2 - center.Y;
            float amount = Math.Abs(delta) > body.Max.Y - body.Min.Y ? 1 : 0.2f;
            ui.MoveMouse(center);
            ImGui.GetIO().AddMouseWheelEvent(0, delta < 0 ? amount : -amount); ui.Frames(3);
        }
        Assert.That(ui.Bounds[control].Visible, Is.True, "Real scrolling must make the entire control reachable: " + control);
    }
}
