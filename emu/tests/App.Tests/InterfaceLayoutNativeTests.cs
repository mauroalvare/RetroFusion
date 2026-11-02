using ImGuiNET;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class InterfaceLayoutNativeTests
{
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    [Test, Explicit("Compara los cuatro diseños con la misma paleta clara; ventana oculta, audio silenciado y medios inventados.")]
    public void TheSamePaletteStillProducesFourDifferentInterfaces()
    {
        using var ui = new NativeHarness(1280, 840, 100, "real", games: 4);
        ui.Enter("cd originales"); ui.Enter("jugar Faro del taller"); ui.Frames(5);
        foreach (var style in InterfaceStyles.All)
        {
            ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("interfaz " + style.Id); ui.Frames(3);
            ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("paleta usar paper");
            if (style.Style == InterfaceStyle.Hardcore) { ui.Enter("juegos"); ui.Enter("paleta"); }
            else ui.Enter("hacer library");
            ui.Frames(5);
            Assert.That(UiTheme.ConsoleColors, Is.EqualTo(TerminalPalettes.All.Single(palette => palette.Id == "paper").Colors));
            Assert.That(((AppPreferences)ui.Get("_prefs")).Interface, Is.EqualTo(style.Style));
            ui.Capture("comparar-" + style.Id);
        }
    }

    [Explicit("Biblioteca propia de 6000 archivos, ventana oculta, audio silenciado y sin IA. Comprueba virtualización y retorno a un juego lejano.")]
    [TestCase("explorer"), TestCase("lounge")]
    public void LargeLibrariesStayVirtualizedAndRevealAGameChosenFarFromTheFirstPage(string style)
    {
        using var ui = new NativeHarness(1280, 840, 100, style, games: 6000);
        var home = (HomeScreen)ui.Get("_home"); home.ShowOriginals(); ui.Frames(5);
        string prefix = style == "lounge" ? "Tarjeta:" : UiText.Get("Jugar:");
        ui.Bounds.Clear(); ui.Frames(2);
        Assert.That(ui.Bounds.Keys.Count(name => name.StartsWith(prefix)), Is.InRange(1, 199));
        var game = home.TerminalGames().Last(entry => entry.Systems == "nes");
        ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("jugar " + game.Title); ui.Frames(5);
        Assert.That(((EmulatorHost)ui.Get("_host")).LoadedMediaSource, Is.EqualTo(game.Path));
        ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("hacer library"); ui.Frames(8);
        ui.Bounds.Clear(); ui.Frames(3);
        ui.Capture("biblioteca-6000-" + style);
        string control = (style == "lounge" ? "Tarjeta archivo:" : "Jugar archivo:") + game.Key + "@" + game.Path;
        Assert.That(ui.Bounds.TryGetValue(control, out var area) && area.Visible, Is.True, "The chosen file must be revealed, including identical copies.");
        Assert.That(ui.Bounds.Keys.Count(name => name.StartsWith(prefix)), Is.LessThan(200));
    }

    [Explicit("Ventana oculta, audio silenciado y cartucho propio; selector, navegación, rueda, colores y juego con eventos reales.")]
    [TestCase(1280, 840, 100, "es"), TestCase(1280, 840, 175, "en"), TestCase(640, 480, 200, "es")]
    public void DistinctLayoutsAndIndependentColorsKeepTheGameAndCanBeUsedAtEverySize(int width, int height, int text, string language)
    {
        using var ui = new NativeHarness(width, height, text, "real", games: 4, "--language", language);
        var prefs = (AppPreferences)ui.Get("_prefs");
        var home = (HomeScreen)ui.Get("_home"); var host = (EmulatorHost)ui.Get("_host");
        ui.Enter("cd originales"); ui.Enter("jugar Faro del taller"); ui.Frames(8);
        Assert.That(host.Machine?.Id, Is.EqualTo("nes")); Assert.That(host.Machine!.FrameCount, Is.GreaterThan(0));
        string media = host.LoadedMediaSource!;
        string key = home.TerminalGames(filtered: false).Single(game => game.Title == "Faro del taller").Key;
        var styles = new[] { InterfaceStyle.Retro, InterfaceStyle.Terminal, InterfaceStyle.Amber };
        var palettes = new[] { "green", "paper", "ice" };
        for (int index = 0; index < styles.Length; index++)
        {
            Pick(styles[index]);
            Assert.That(host.LoadedMediaSource, Is.EqualTo(media));
            OpenLibrary();
            ui.Bounds.Clear(); ui.Frames(5);
            string control = (styles[index] == InterfaceStyle.Amber ? "Tarjeta:" : UiText.Get("Jugar:")) + key;
            ui.Capture($"antes-layout-{index}-{text}-{language}");
            Reach(control); Assert.That(ui.Bounds[control].Visible, Is.True, control);
            Assert.That(ui.Bounds.Keys.Any(name => name.StartsWith("Tarjeta:")), Is.EqualTo(styles[index] == InterfaceStyle.Amber));
            Assert.That(WorkspaceLayout.DockHeight > 0, Is.EqualTo(styles[index] == InterfaceStyle.Amber));
            if (styles[index] == InterfaceStyle.Terminal && width == 1280 && text == 100) Assert.That(WorkspaceLayout.SidebarWidth, Is.GreaterThan(0));
            var geometry = ui.Bounds[control]; var origin = WorkspaceLayout.Origin; var area = WorkspaceLayout.Size; long frame = host.Machine!.FrameCount;
            ui.Capture($"layout-{index}-{text}-{language}");
            ui.Call("OpenPaletteEditor"); ui.Bounds.Clear(); ui.Frames(4);
            if (UiTheme.CompactHeight)
                foreach (string editorControl in new[] { "Color:background", "Elegir color", "Aplicar paleta", "Cancelar paleta" })
                    Assert.That(ui.Bounds.TryGetValue(editorControl, out var box) && box.Visible, Is.True, editorControl);
            ui.Capture($"colores-layout-{index}-{text}-{language}");
            ui.Click("Cancelar paleta"); ui.Frames(3);
            ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("paleta usar " + palettes[index]);
            ui.Enter("hacer library"); ui.Bounds.Clear(); ui.Frames(5); Reach(control);
            Assert.That(prefs.Interface, Is.EqualTo(styles[index])); Assert.That(prefs.ConsolePalette, Is.EqualTo(palettes[index]));
            Assert.That(WorkspaceLayout.Origin, Is.EqualTo(origin), "Color does not change layout.");
            Assert.That(WorkspaceLayout.Size, Is.EqualTo(area));
            Assert.That(ui.Bounds[control].Max - ui.Bounds[control].Min, Is.EqualTo(geometry.Max - geometry.Min));
            Assert.That(host.LoadedMediaSource, Is.EqualTo(media)); Assert.That(host.Machine.FrameCount, Is.GreaterThanOrEqualTo(frame));
            ui.Capture($"layout-color-{index}-{text}-{language}");
            if (styles[index] != InterfaceStyle.Retro)
            {
                if (styles[index] == InterfaceStyle.Amber) ui.Click("Tarjeta:" + key);
                ui.Click(styles[index] == InterfaceStyle.Terminal && UiTheme.CompactHeight ? UiText.Get("Jugar:") + key : "Jugar selección"); ui.Frames(8);
                Assert.That(home.IsOpen, Is.False); Assert.That(host.Machine.FrameCount, Is.GreaterThan(0));
            }
        }
        for (int i = 0; i < styles.Length; i++) Assert.That(prefs.PaletteFor(styles[i]).Palette, Is.EqualTo(palettes[i]));
        ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("interfaz real"); ui.Frames(3);
        Assert.That(prefs.ConsolePalette, Is.EqualTo("auto")); Assert.That((bool)ui.Get("_terminalOpen"), Is.True);
        ui.Capture($"layout-real-{text}-{language}");

        void Pick(InterfaceStyle style)
        {
            ui.Call("OpenInterfacePicker"); ui.Bounds.Clear(); ui.Frames(4);
            string choice = "Elegir " + InterfaceStyles.Describe(style).Id;
            if (!ui.Bounds.ContainsKey(choice)) { ui.Click("Estilo"); ui.Frames(2); }
            ui.Click(choice); ui.Frames(2); ui.Click("Usar este estilo"); ui.Frames(4);
        }
        void OpenLibrary()
        {
            if (UiTheme.CompactHeight && prefs.Interface == InterfaceStyle.Retro)
            { ui.Click("Menú principal"); ui.Frames(2); ui.Click("Workspace:library"); ui.Frames(3); }
            else
            {
                ui.Bounds.Clear(); ui.Frames(3);
                if (!ui.Bounds.ContainsKey("Workspace:library")) { ui.Click("Cambiar sección"); ui.Frames(2); }
                ui.Capture("navegacion-" + text + "-" + prefs.Interface);
                ui.Click("Workspace:library"); ui.Frames(3);
            }
        }
        void Reach(string control)
        {
            for (int attempt = 0; attempt < 40 && (!ui.Bounds.TryGetValue(control, out var box) || !box.Visible); attempt++)
            { ui.MoveMouse(new(width * 0.65f, height * 0.55f)); ImGui.GetIO().AddMouseWheelEvent(0, -0.1f); ui.Frames(2); }
            ui.Capture("alcance-" + text + "-" + prefs.Interface);
            Assert.That(ui.Bounds.ContainsKey(control), Is.True, control);
        }
    }
}
