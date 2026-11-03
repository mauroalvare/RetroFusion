using ImGuiNET;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class ReleaseNewsNativeTests
{
    private const string Current = "0.4.1";
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    [Explicit("Ventana oculta, audio silenciado, entradas reales y datos propios; no usa la instalación del usuario.")]
    [TestCase("es", 100, 1280, 840, null)]
    [TestCase("en", 100, 1280, 840, "0.2.6")]
    [TestCase("es", 200, 640, 480, "0.2.6")]
    [TestCase("en", 200, 640, 480, null)]
    [TestCase("es", 100, 1280, 840, "0.4.0")]
    [TestCase("en", 200, 640, 480, "0.4.0")]
    public void FirstLaunchAndSkippedUpdatesOfferReadableNewsAfterTheInterface(string language, int text, int width, int height, string? previous)
    {
        using var ui = new NativeHarness(width, height, text, "retro", games: 3, "--language", language);
        var prefs = (AppPreferences)ui.Get("_prefs");
        prefs.LastStartedVersion = previous; prefs.InterfaceOfferedVersion = previous;
        Assert.That(ui.App.OfferInterfaceOnStartup(Current), Is.True);
        ui.Frames(5);
        Assert.That(ui.Get("_newsOpen"), Is.EqualTo(true));
        Assert.That(prefs.LastNewsVersion, Is.Null);
        ui.Click("Conservar el actual"); ui.Frames(5);
        Assert.That(prefs.InterfaceOfferedVersion, Is.EqualTo(Current));
        Assert.That(prefs.PendingNewsFromVersion, Is.EqualTo(previous ?? ""));
        var versions = ReleaseNotes.Since(prefs.PendingNewsFromVersion, prefs.PendingNewsVersion!).Select(note => note.Version).ToArray();
        Assert.That(versions[0], Is.EqualTo(Current));
        if (previous == "0.4.0") Assert.That(versions, Is.EqualTo(new[] { Current }));
        Assert.That(((EmulatorHost)ui.Get("_host")).Paused, Is.True);
        var body = ui.Bounds["Cuerpo novedades"];
        Assert.That(body.Max.Y - body.Min.Y, Is.GreaterThanOrEqualTo(ImGui.GetTextLineHeight() * 2 + ImGui.GetStyle().WindowPadding.Y * 2),
            "News must leave space for readable lines as well as the footer at the current DPI.");
        ui.Capture($"news-{language}-{text}-{previous ?? "first"}");
        ui.Click("Idioma novedades"); ui.Frames(3);
        Assert.That(prefs.Language, Is.EqualTo(language == "es" ? "en" : "es"));
        ui.Click("Historial novedades"); Assert.That(ui.Get("_newsHistory"), Is.EqualTo(true));
        ui.Click("Historial novedades"); Assert.That(ui.Get("_newsHistory"), Is.EqualTo(false));
        ui.Key(ImGuiKey.O, ctrl: true); // Guest/application shortcuts must not open a picker behind the news.
        ui.Capture($"news-switched-{language}-{text}-{previous ?? "first"}");
        ui.MoveMouse(new(300, 200)); ui.Frames(2); ImGui.GetIO().AddMouseWheelEvent(0, -8); ui.Frames(5);
        ui.Capture($"news-scroll-{language}-{text}-{previous ?? "first"}");
        ui.Click("Cerrar novedades"); ui.Frames(3);
        Assert.That(ui.Get("_newsOpen"), Is.EqualTo(false));
        Assert.That(prefs.LastNewsVersion, Is.EqualTo(Current));
        Assert.That(prefs.PendingNewsVersion, Is.Null);
        Assert.That(ui.App.OfferInterfaceOnStartup(Current), Is.False);
        Assert.That(ui.Get("_newsOpen"), Is.EqualTo(false));
        ui.Call("OpenNews"); ui.Frames(3); ui.Click("Cerrar novedades");
        Assert.That(prefs.LastNewsVersion, Is.EqualTo(Current));
    }

    [Test, Explicit("Ventana oculta, audio silenciado y cartucho sintético propio.")]
    public void NewsPausesAPlayingGameAndClosingResumesItWithoutConsumingTheShortcut()
    {
        using var ui = new NativeHarness(1280, 840, 100, "real", games: 3, "--language", "en");
        ui.Enter("filter system nes"); ui.Enter("games"); ui.Enter("play 1"); ui.Frames(20);
        var host = (EmulatorHost)ui.Get("_host");
        Assert.That(host.Machine?.Id, Is.EqualTo("nes")); Assert.That(host.Paused, Is.False);
        ui.Call("OpenNews"); ui.Frames(3);
        long frame = host.Machine!.FrameCount;
        ui.Key(ImGuiKey.O, ctrl: true); ui.Frames(20);
        Assert.That(host.Machine.FrameCount, Is.EqualTo(frame)); Assert.That(host.Paused, Is.True);
        Assert.That(((AppPreferences)ui.Get("_prefs")).LastNewsVersion, Is.Null, "Manual history is not an automatic delivery.");
        ui.Click("Cerrar novedades"); ui.Frames(10);
        Assert.That(host.Paused, Is.False); Assert.That(host.Machine.FrameCount, Is.GreaterThan(frame));
    }
}
