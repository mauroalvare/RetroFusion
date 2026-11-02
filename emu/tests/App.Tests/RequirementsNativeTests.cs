using ImGuiNET;
using Retro.App;
using Retro.Core;

namespace App.Tests;

[NonParallelizable]
public sealed class RequirementsNativeTests
{
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    [Explicit("Ventana oculta, audio silenciado y datos propios. No abre enlaces ni descarga ni instala componentes.")]
    [TestCase("retro", 125, "es"), TestCase("explorer", 175, "en"), TestCase("lounge", 200, "es"), TestCase("real", 200, "en")]
    public void RequirementsCanBeOpenedByAnOrderAndClosedWithRealInputWithoutRequiringAnInstaller(string style, int text, string language)
    {
        using var ui = new NativeHarness(640, 480, text, style, games: 1, "--language", language);
        if (style != "real") { ui.Call("OpenTerminal"); ui.Frames(3); }
        ui.Enter(language == "en" ? "do requirements" : "hacer requirements"); ui.Frames(5);
        Assert.That(ui.Get("_requirementsOpen"), Is.EqualTo(true));
        Assert.That(ui.Property("ConsoleOwnsKeyboard"), Is.EqualTo(false));
        Assert.That(ui.Bounds.TryGetValue("Cerrar requisitos", out var close) && close.Visible, Is.True, "The footer must remain reachable.");
        Assert.That(ui.Bounds.ContainsKey("Reparar componente oficial"), Is.False, "A complete package must not ask for VC++ without a missing dependency.");
        ui.Capture("requisitos-" + style + "-" + text + "-" + language);
        ui.Click("Cerrar requisitos"); ui.Frames(3);
        Assert.That(ui.Get("_requirementsOpen"), Is.EqualTo(false));
        // Visual instruments release the console; its documented shortcut reopens it.
        ui.Call("OpenTerminal"); ui.Frames(3);
        Assert.That(ui.Property("ConsoleOwnsKeyboard"), Is.EqualTo(true));
        Assert.That(((ProgramTerminal)ui.Property("Terminal")).Output.AllText(), Does.Not.Contain("unknown command").And.Not.Contain("orden desconocida"));
        Assert.That((bool)Raylib_cs.Raylib.IsWindowHidden(), Is.True);
        Assert.That(((Retro.Ai.AiSettings)ui.Get("_ai")).ProvidersDisabled, Is.True);
    }

    [Test, Explicit("Ventana oculta y tarea de reparación ficticia. No abre UAC ni instaladores.")]
    public void ClosingAsksAboutAPendingComponentRepairAndCancelsItsPendingWork()
    {
        using var ui = new NativeHarness(1280, 840, 125, "retro", games: 1);
        using var cancellation = new CancellationTokenSource();
        var finished = new TaskCompletionSource<bool>();
        ui.Set("_componentCancellation", cancellation); ui.Set("_componentRepair", finished.Task);
        ui.App.RequestQuit();
        Assert.That(ui.App.QuitRequested, Is.False);
        Assert.That(ui.Get("_quitNotice"), Does.Contain("instalador de Windows").And.Contain("No se termina por la fuerza"));
        _ = ui.Call("CancelComponentRepair");
        Assert.That(cancellation.IsCancellationRequested, Is.True);
        finished.SetResult(false); ui.Set("_quitNotice", null); ui.Frames(3);
        Assert.That(ui.Get("_componentRepair"), Is.Null);
    }
}
