using ImGuiNET;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class LocalizationNativeTests
{
    private const string Reason = "Requiere gráficos nativos: ventana oculta, audio silenciado y datos propios.";
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    [Explicit(Reason)]
    [TestCase(125), TestCase(175)]
    public void EnglishCommandsFiltersPreferencesAndNewsUseTheActualInput(int text)
    {
        using var ui = new NativeHarness(1280, 840, text, "real", games: 12, "--language", "en");
        var console = (ProgramTerminal)ui.Property("Terminal");
        var host = (EmulatorHost)ui.Get("_host");
        var prefs = (AppPreferences)ui.Get("_prefs");
        Assert.That(ui.Get("_ai"), Is.TypeOf<Retro.Ai.AiSettings>());
        Assert.That(((Retro.Ai.AiSettings)ui.Get("_ai")).Provider, Is.EqualTo("manual"));
        Assert.That(((Retro.Ai.AiSettings)ui.Get("_ai")).ProvidersDisabled, Is.True);
        Assert.That(prefs.OnlineGameInfo, Is.False);
        ui.Enter("games"); ui.Enter("filter genre Platformers"); ui.Enter("games");
        Assert.That(console.Output.AllText(), Does.Not.Contain("unknown command").And.Not.Contain("invalid argument"));
        ui.Capture("en-terminal-" + text);
        ui.Enter("help play"); Assert.That(console.Output.AllText(), Does.Contain("Usage: play"));
        ui.Enter("option volume 40"); Assert.That(((SoundSettings)ui.Get("_sound")).VolumePercent, Is.EqualTo(40));
        ui.Enter("option language es"); Assert.That(prefs.Language, Is.EqualTo("es"));
        ui.Enter("opcion idioma en"); Assert.That(prefs.Language, Is.EqualTo("en"));
        ui.Enter("cd favorites"); Assert.That(console.Location, Is.EqualTo("favoritos"));
        ui.Enter("cd library"); ui.Enter("filter remove"); ui.Enter("filter system nes"); ui.Enter("games");
        ui.Enter("play 1"); ui.Frames(30);
        Assert.That(host.Machine?.Id, Is.EqualTo("nes")); Assert.That(host.Machine!.FrameCount, Is.GreaterThan(0));
        Assert.That(host.Status, Does.Contain("NES cartridge").And.Not.Contain("Cartucho"));
        string media = host.LoadedMediaSource!;
        ui.Call("OpenTerminal"); ui.Frames(3); ui.Enter("news"); ui.Frames(4);
        Assert.That(ui.Get("_newsOpen"), Is.EqualTo(true)); ui.Capture("en-novedades-" + text);
        ui.Click("Cerrar novedades"); Assert.That(ui.Get("_newsOpen"), Is.EqualTo(false));
        ui.Call("OpenTerminal"); ui.Frames(2); ui.Enter("do preferences"); ui.Frames(5);
        ui.Capture("en-preferencias-" + text);
        ui.Click("Cerrar preferencias"); Assert.That(ui.Get("_preferencesOpen"), Is.EqualTo(false));
        Assert.That(host.LoadedMediaSource, Is.EqualTo(media));
        Assert.That(console.Output.AllText(), Does.Not.Contain("unknown command"));
    }

    [Explicit(Reason)]
    [TestCase(125), TestCase(175)]
    public void EveryPageRendersInEnglishAtIntermediateTextSizes(int text)
    {
        string[] pages = ["library", "create", "tools", "help", "creator", "studio", "code", "analysis", "automatic", "debugger", "states", "sound", "preferences", "news", "updates", "interface"];
        foreach (string page in pages)
        {
            using var ui = new NativeHarness(1280, 840, text, "retro", games: 12, "--language", "en");
            switch (page)
            {
                case "create": case "tools": case "help":
                    ui.Call("OpenHub", Enum.Parse(typeof(RetroApp).GetNestedType("HubPage", System.Reflection.BindingFlags.NonPublic)!, page == "create" ? "Create" : page == "tools" ? "Tools" : "Help")); break;
                case "creator": ui.Call("OpenCreator"); break;
                case "studio": ui.Call("OpenFusionStudio"); break;
                case "code": case "debugger": case "analysis": case "automatic": case "states":
                    ui.Call("OpenFile", Directory.GetFiles(ui.Games, "*.nes").First()); ui.Frames(3);
                    ui.Call(page switch { "code" => "OpenAnalyzer", "debugger" => "OpenDebugger", "analysis" => "OpenModifications", "automatic" => "OpenAutomaticAnalysis", _ => "OpenStateWindow" }); break;
                case "sound": ui.Set("_soundWindowOpen", true); break;
                case "preferences": ui.Set("_preferencesOpen", true); break;
                case "news": ui.Call("OpenNews"); break;
                case "updates": ((UpdateWindow)ui.Get("_updates")).Open(); break;
                case "interface": ui.Call("OpenInterfacePicker"); break;
            }
            ui.Frames(6); ui.Capture("en-" + text + "-" + page);
            if (page == "updates")
            {
                ui.Click("Cerrar actualizaciones"); ui.Frames(2);
                Assert.That(((UpdateWindow)ui.Get("_updates")).Blocking, Is.False);
            }
            if (page == "interface") ui.Click("Conservar el actual");
            Assert.That((bool)Raylib_cs.Raylib.IsWindowHidden(), Is.True);
            Assert.That(((Retro.Ai.AiSettings)ui.Get("_ai")).Provider, Is.EqualTo("manual"));
        }
    }
}
