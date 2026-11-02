using ImGuiNET;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class AmberThemeNativeTests
{
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    [Explicit("Ventana oculta, audio silenciado y datos propios; eventos reales de teclado y mouse.")]
    [TestCase(1280, 840, 100, "es"), TestCase(1280, 840, 175, "en"), TestCase(640, 480, 200, "en")]
    public void PreviewApplyCommandsAndReturnKeepTheGameAtEverySize(int width, int height, int text, string language)
    {
        using var ui = new NativeHarness(width, height, text, "real", games: 4, "--language", language);
        ui.Enter("jugar Faro del taller"); ui.Frames(8);
        var host = (EmulatorHost)ui.Get("_host"); var prefs = (AppPreferences)ui.Get("_prefs");
        string? media = host.LoadedMediaSource; long frame = host.Machine!.FrameCount;
        Assert.That(media, Is.Not.Null); Assert.That(frame, Is.GreaterThan(0));
        ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("interfaz preview"); ui.Frames(3);
        if (!ui.Bounds.ContainsKey("Elegir amber")) { ui.Click("Estilo"); ui.Frames(2); }
        ui.Click("Elegir amber"); ui.Frames(2); ui.Capture($"ambar-preview-{text}-{language}");
        Assert.That(prefs.Interface, Is.EqualTo(InterfaceStyle.Hardcore));
        ui.Click("Usar este estilo"); ui.Frames(4);
        Assert.That(prefs.Interface, Is.EqualTo(InterfaceStyle.Amber));
        Assert.That(ImGui.GetStyle().Colors[(int)ImGuiCol.Text], Is.EqualTo(UiTheme.PaletteOf(InterfaceStyle.Amber).Text));
        Assert.That(host.LoadedMediaSource, Is.EqualTo(media)); Assert.That(host.Machine.FrameCount, Is.GreaterThanOrEqualTo(frame));
        ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("requisitos");
        string output = ((ProgramTerminal)ui.Property("Terminal")).Output.AllText();
        Assert.That(output, Does.Contain("OpenGL 3.3").And.Contain("Python"));
        ui.Capture($"ambar-requisitos-{text}-{language}");
        ui.Enter("hacer library"); ui.Frames(4); ui.Capture($"ambar-biblioteca-{text}-{language}");
        Assert.That(((HomeScreen)ui.Get("_home")).IsOpen, Is.True);
        Assert.That(ui.Get("_terminalOpen"), Is.EqualTo(false)); Assert.That(prefs.Interface, Is.EqualTo(InterfaceStyle.Amber));
    }
}
