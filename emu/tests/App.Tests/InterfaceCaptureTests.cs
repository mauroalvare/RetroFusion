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
