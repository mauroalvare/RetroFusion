using ImGuiNET;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class TerminalPaletteNativeTests
{
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    [Explicit("Ventana oculta, audio silenciado y datos propios. Teclado y mouse reales en la consola y el editor.")]
    [TestCase(125, "es"), TestCase(175, "en")]
    public void FreeRgbAndPresetsReachTheRealTerminalAndTheEditorOnlySavesWhenApplied(int text, string language)
    {
        using var ui = new NativeHarness(1280, 840, text, "real", games: 1, "--language", language);
        var prefs = (AppPreferences)ui.Get("_prefs");
        ui.Enter("juegos"); ui.Enter("jugar 1"); ui.Frames(6);
        Assert.That(((EmulatorHost)ui.Get("_host")).LoadedMediaSource, Is.Not.Null);
        Assert.That(((EmulatorHost)ui.Get("_host")).Machine!.FrameCount, Is.GreaterThan(0));
        ui.Call("OpenTerminal"); ui.Frames(3);
        string? media = ((EmulatorHost)ui.Get("_host")).LoadedMediaSource;
        foreach (string id in new[] { "green", "amber", "ice", "paper" })
        {
            ui.Enter("paleta usar " + id); ui.Frames(2);
            Assert.That(prefs.ConsolePalette, Is.EqualTo(id));
            Assert.That(UiTheme.ConsoleColors, Is.EqualTo(TerminalPalettes.All.Single(palette => palette.Id == id).Colors));
            ui.Capture($"paleta-{id}-{text}-{language}");
        }
        ui.Enter("palette color text #F092FF"); ui.Enter("palette color background #100C22");
        Assert.That(prefs.ConsolePalette, Is.EqualTo("custom")); Assert.That(prefs.ConsoleColors.Text, Is.EqualTo("#F092FF"));
        Assert.That(prefs.Interface, Is.EqualTo(InterfaceStyle.Hardcore));
        var saved = prefs.ConsoleColors;
        ui.Enter("palette color text invalid"); Assert.That(prefs.ConsoleColors, Is.EqualTo(saved));
        ui.Enter("paleta editar"); ui.Frames(4);
        Reach("Color:text"); ui.Click("Color:text"); ui.Key(ImGuiKey.A, ctrl: true); ui.Type("CC88FF"); ui.Key(ImGuiKey.Tab); ui.Frames(2);
        Assert.That(prefs.ConsoleColors, Is.EqualTo(saved), "A preview does not save preferences.");
        ui.Capture($"paleta-editor-{text}-{language}");
        ui.Click("Cancelar paleta"); ui.Frames(3); Assert.That(prefs.ConsoleColors, Is.EqualTo(saved));
        ui.Enter("paleta editar"); ui.Frames(4);
        Reach("Color:text"); ui.Click("Color:text"); ui.Key(ImGuiKey.A, ctrl: true); ui.Type("CC88FF"); ui.Key(ImGuiKey.Tab); ui.Frames(2);
        ui.Click("Aplicar paleta"); ui.Frames(3);
        Assert.That(prefs.ConsoleColors.Text, Is.EqualTo("#CC88FF"));
        Assert.That((bool)ui.Get("_terminalOpen"), Is.True);
        ui.Enter("opcion contraste on"); Assert.That(UiTheme.ConsoleColors, Is.EqualTo(TerminalPalettes.HighContrast));
        ui.Enter("opcion contraste off"); Assert.That(UiTheme.ConsoleColors.Text, Is.EqualTo("#CC88FF"));
        ui.Enter("paleta restablecer"); Assert.That(prefs.ConsolePalette, Is.EqualTo("auto"));
        Assert.That(((EmulatorHost)ui.Get("_host")).LoadedMediaSource, Is.EqualTo(media));
        Assert.That(((ProgramTerminal)ui.Property("Terminal")).Output.AllText(), Does.Not.Contain("unknown command").And.Not.Contain("orden desconocida"));

        void Reach(string control)
        {
            for (int attempt = 0; attempt < 60 && (!ui.Bounds.TryGetValue(control, out var box) || !box.Visible); attempt++)
            {
                ui.MoveMouse(new(220, 270)); ImGui.GetIO().AddMouseWheelEvent(0, -1); ui.Frames(2);
            }
        }
    }

    [Test, Explicit("Ventana oculta y audio silenciado; verifica el pie del editor con texto al 200 %.")]
    public void PaletteApplyCancelAndResetRemainReachableInASmallWindow()
    {
        using var ui = new NativeHarness(640, 480, 200, "real", games: 1);
        ui.Enter("paleta editar"); ui.Frames(5);
        ui.Capture("paleta-editor-640-200");
        foreach (string control in new[] { "Color:background", "Elegir color", "Aplicar paleta", "Cancelar paleta", "Restaurar paleta" })
            Assert.That(ui.Bounds.TryGetValue(control, out var box) && box.Visible, Is.True, control + " " + box);
        ui.Click("Elegir color"); ui.Frames(3); ui.Click("Elegir color:text"); ui.Frames(3);
        ui.Click("Color:text"); ui.Key(ImGuiKey.A, ctrl: true); ui.Type("66CCEE"); ui.Key(ImGuiKey.Tab);
        ui.Capture("paleta-editor-640-200-color");
        ui.Click("Aplicar paleta");
        Assert.That(((AppPreferences)ui.Get("_prefs")).ConsoleColors.Text, Is.EqualTo("#66CCEE"));
        ui.Enter("paleta editar"); ui.Frames(3);
        ui.Click("Restaurar paleta"); ui.Click("Cancelar paleta");
        Assert.That((bool)ui.Get("_paletteEditorOpen"), Is.False);
    }
}
