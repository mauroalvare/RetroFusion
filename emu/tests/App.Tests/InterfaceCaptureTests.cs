using ImGuiNET;
using Retro.App;

namespace App.Tests;

/// <summary>Captures every principal page for visual review. Select explicitly: nothing here runs in the general filter.</summary>
[NonParallelizable]
public sealed class InterfaceCaptureTests
{
    private const string Reason = "Requiere gráficos nativos; escribe capturas en RETROEMU_QA_SHOTS.";

    [Explicit(Reason)]
    [TestCase(1280, 840, 100), TestCase(640, 480, 200), TestCase(1920, 1080, 100), TestCase(1024, 700, 150)]
    public void TheConsoleRenders(int width, int height, int text)
    {
        using var ui = new NativeHarness(width, height, text, "hardcore", games: 16);
        string name = $"hardcore-{width}x{height}-{text}";
        ui.Capture(name + "-inicio");
        ui.Enter("juegos"); ui.Capture(name + "-juegos");
        ui.Enter("ficha 1"); ui.Enter("ayuda jugar"); ui.Capture(name + "-ayuda");
        ui.Enter("ayuda"); ui.Capture(name + "-indice");
        ui.Enter("registros"); ui.Enter("jugra 1"); ui.Type("op"); ui.Key(ImGuiKey.Tab); ui.Capture(name + "-completar");
    }

    [Explicit(Reason)]
    [TestCase("retro"), TestCase("terminal"), TestCase("hardcore")]
    public unsafe void EveryCharacterTheProgramWritesExistsInEveryFont(string style)
    {
        using var ui = new NativeHarness(1280, 840, 100, style);
        var used = new SortedSet<char>();
        string source = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "src", "Retro.App"));
        foreach (string file in Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories))
            foreach (System.Text.RegularExpressions.Match literal in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "\"(?:[^\"\\\n]|\\.)*\""))
                foreach (char c in literal.Value) if (c > 126 && !char.IsSurrogate(c)) used.Add(c);
        Assert.That(used, Has.Count.GreaterThan(20), "The scan must have read the interface texts.");
        foreach (var (name, font) in UiTheme.Fonts)
        {
            var missing = used.Where(c => font.NativePtr->FallbackGlyph != null && (nint)ImGuiNative.ImFont_FindGlyphNoFallback(font.NativePtr, c) == 0).ToArray();
            Assert.That(new string(missing), Is.Empty, "Glyphs missing in the " + name + " font of style " + style);
        }
    }

    [Explicit(Reason)]
    [TestCase("retro", 1280, 840, 100), TestCase("retro", 640, 480, 200), TestCase("retro", 1024, 700, 150), TestCase("terminal", 640, 480, 200)]
    public void TheEssentialControlsCanBeReachedAtEverySize(string style, int width, int height, int text)
    {
        using var ui = new NativeHarness(width, height, text, style);
        var home = (HomeScreen)ui.Get("_home");
        var bounds = new Dictionary<string, (bool Visible, System.Numerics.Vector2 Min, System.Numerics.Vector2 Max)>();
        home.LibraryControlBounds = (name, min, max) =>
        {
            var clipMin = ImGui.GetWindowDrawList().GetClipRectMin(); var clipMax = ImGui.GetWindowDrawList().GetClipRectMax();
            bounds[name] = (min.X >= clipMin.X && max.X <= clipMax.X && min.Y >= clipMin.Y && max.Y <= clipMax.Y, min, max);
        };
        home.ShowOriginals(); ui.Frames(6);
        var visible = bounds.Where(pair => pair.Value.Visible).Select(pair => pair.Key).ToArray();
        int rows = visible.Count(name => name.StartsWith("Jugar:", StringComparison.Ordinal));
        TestContext.Out.WriteLine($"{style} {width}x{height} {text}%: {rows} filas con Jugar visible");
        Assert.That(rows, Is.GreaterThanOrEqualTo(2), "The list must show several games, not just its headers.");
        Assert.That(visible.Any(name => name.StartsWith("Ficha:", StringComparison.Ordinal)), Is.True);
        Assert.That(visible, Has.Member("Filtros"));
        ui.Set("_preferencesOpen", true); ui.Frames(6);
        Assert.That(ui.Bounds.TryGetValue("Cerrar preferencias", out var close) && close.Visible, Is.True, "Close must be reachable without scrolling.");
        Assert.That(ui.Bounds["Restablecer todo"].Visible, Is.True);
        ui.Capture($"preferencias-{style}-{width}x{height}-{text}");
        ui.Click("Cerrar preferencias"); Assert.That((bool)ui.Get("_preferencesOpen"), Is.False);
        ui.Call("OpenInterfacePicker"); ui.Frames(6);
        ui.Capture($"selector-{style}-{width}x{height}-{text}");
    }

    private static readonly string[] Pages = ["inicio", "originales", "partida", "crear", "herramientas", "ayuda", "preferencias", "creador", "estudio", "codigo", "depurador", "partidas", "acciones"];

    [Explicit("Requiere gráficos nativos; escribe capturas en RETROEMU_QA_SHOTS.")]
    [TestCase("retro", 1280, 840, 100), TestCase("retro", 640, 480, 200), TestCase("retro", 1920, 1080, 100), TestCase("retro", 1024, 700, 150),
     TestCase("terminal", 1280, 840, 100), TestCase("terminal", 640, 480, 200)]
    public void EveryPrincipalPageRenders(string style, int width, int height, int text)
    {
        foreach (string page in Pages)
        {
            using var ui = new NativeHarness(width, height, text, style);
            var host = (EmulatorHost)ui.Get("_host");
            var home = (HomeScreen)ui.Get("_home");
            switch (page)
            {
                case "originales": home.ShowOriginals(); break;
                case "partida": case "codigo": case "depurador": case "partidas":
                    string cartridge = Directory.GetFiles(ui.Games, "*.nes").First();
                    ui.Call("OpenFile", cartridge); ui.Frames(4);
                    if (page == "codigo") ui.Call("OpenAnalyzer");
                    if (page == "depurador") ui.Call("OpenDebugger");
                    if (page == "partidas") ui.Call("OpenStateWindow");
                    break;
                case "crear": ui.Call("OpenHub", Enum.Parse(typeof(RetroApp).GetNestedType("HubPage", System.Reflection.BindingFlags.NonPublic)!, "Create")); break;
                case "herramientas": ui.Call("OpenHub", Enum.Parse(typeof(RetroApp).GetNestedType("HubPage", System.Reflection.BindingFlags.NonPublic)!, "Tools")); break;
                case "ayuda": ui.Call("OpenHub", Enum.Parse(typeof(RetroApp).GetNestedType("HubPage", System.Reflection.BindingFlags.NonPublic)!, "Help")); break;
                case "preferencias": ui.Set("_preferencesOpen", true); break;
                case "creador": ui.Call("OpenCreator"); break;
                case "estudio": ui.Call("OpenFusionStudio"); break;
                case "acciones": ((CommandPalette)ui.Get("_commands")).Open(); break;
            }
            ui.Frames(8);
            ui.Capture($"{style}-{width}x{height}-{text}-{page}");
        }
    }
}
