using ImGuiNET;
using Retro.App;

namespace App.Tests;

/// <summary>Real ImGui events over the actual console. Explicit: hidden window, muted audio and invented cartridges.</summary>
[NonParallelizable]
public sealed class TerminalNativeTests
{
    private const string Reason = "Requiere gráficos nativos: usa una ventana oculta, audio silenciado y datos propios.";
    private static ProgramTerminal Console(NativeHarness ui) => (ProgramTerminal)ui.Property("Terminal");
    private static EmulatorHost Host(NativeHarness ui) => (EmulatorHost)ui.Get("_host");
    private static bool Captures(NativeHarness ui) => (bool)ui.Call("CaptureGuiKeyboard")!;
    private static TerminalInput Input(NativeHarness ui) => (TerminalInput)ui.Get("_terminalInput");

    [TestCase(1280, 840, 100), TestCase(640, 480, 200)]
    [Explicit(Reason)]
    public void PlayingAndContinuingFromTheActualInputReleaseTheGuestKeyboard(int width, int height, int text)
    {
        using var ui = new NativeHarness(width, height, text, "hardcore");
        var terminal = Console(ui);
        ui.Frames(3);
        Assert.That(Captures(ui), Is.True, "The open console owns the keyboard.");
        Assert.That(ui.Bounds["Entrada terminal"].Visible, Is.True);
        ui.Enter("cd originales"); ui.Enter("juegos"); ui.Enter("opcion volumen 37");
        Assert.That(terminal.History.Entries.Last(), Is.EqualTo("opcion volumen 37"));
        ui.Key(ImGuiKey.UpArrow); Assert.That(Input(ui).Editor.Text, Is.EqualTo("opcion volumen 37"));
        ui.Key(ImGuiKey.DownArrow); Assert.That(Input(ui).Editor.Text, Is.Empty);
        ui.Type("jue"); ui.Key(ImGuiKey.Tab); ui.Key(ImGuiKey.Tab); Assert.That(Input(ui).Editor.Text, Does.StartWith("jue"));
        ui.Key(ImGuiKey.Escape); ui.Key(ImGuiKey.Escape); ui.Key(ImGuiKey.Escape);
        ui.Enter("jugar 1"); ui.Frames(3);
        Assert.That(Host(ui).LoadedMediaSource, Is.Not.Null);
        Assert.That(Captures(ui), Is.False, "The command bar must not reclaim the keyboard after playing.");
        Assert.That(Host(ui).Paused, Is.False);
        Assert.That((bool)ui.Property("ConsoleOwnsKeyboard"), Is.False);
    }

    [Test]
    [Explicit(Reason)]
    public void ConsoleRoundTripKeepsThePauseAndKeyboardConsistent()
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        ui.Enter("juegos"); ui.Enter("jugar 1"); ui.Frames(3);
        Assert.That(Captures(ui), Is.False);
        ui.Call("OpenTerminal"); ui.Frames(3);
        Assert.That(Captures(ui), Is.True); Assert.That(Host(ui).Paused, Is.True, "Going back to the console pauses the game.");
        ui.Type("hola"); Assert.That(Input(ui).Editor.Text, Is.EqualTo("hola"));
        ui.Key(ImGuiKey.Escape);
        ui.Enter("estado"); Assert.That(Console(ui).Output.AllText(), Does.Contain("en pausa"));
        ui.Enter("continuar"); ui.Frames(3);
        Assert.That(Captures(ui), Is.False); Assert.That(Host(ui).Paused, Is.False);
    }

    [Test]
    [Explicit(Reason)]
    public void TypingInTheConsoleNeverReachesTheMachine()
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        ui.Enter("juegos"); ui.Enter("jugar 1"); ui.Frames(3); ui.Call("OpenTerminal"); ui.Frames(3);
        var sent = new List<string>();
        var input = (InputMapper)ui.Get("_input");
        var previous = input.KeySink; input.KeySink = (key, pressed) => { if (pressed) sent.Add(key); previous?.Invoke(key, pressed); };
        ui.Type("zxzxzx"); ui.Key(ImGuiKey.Enter); ui.Frames(4);
        Assert.That(sent, Is.Empty);
    }

    [Test]
    [Explicit(Reason)]
    public void OpenedToolsReleaseTheKeyboardAndTheConsoleComesBack()
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        ui.Enter("juegos"); ui.Enter("jugar 1"); ui.Frames(3); ui.Call("OpenTerminal"); ui.Frames(2);
        ui.Enter("hacer code"); ui.Frames(3);
        Assert.That((bool)ui.Property("ConsoleOwnsKeyboard"), Is.False, "A panel takes over.");
        ui.Call("OpenTerminal"); ui.Frames(3);
        Assert.That((bool)ui.Property("ConsoleOwnsKeyboard"), Is.True);
        ui.Enter("interfaz retro"); ui.Frames(4);
        Assert.That((bool)ui.Property("ConsoleOwnsKeyboard"), Is.False);
        Assert.That(((AppPreferences)ui.Get("_prefs")).Interface, Is.EqualTo(InterfaceStyle.Retro));
    }

    [TestCase(1280, 840, 100), TestCase(640, 480, 200), TestCase(1024, 700, 150)]
    [Explicit(Reason)]
    public void ALongOutputStaysResponsiveScrollsAndCanBeSelected(int width, int height, int text)
    {
        using var ui = new NativeHarness(width, height, text, "hardcore", games: 40);
        var terminal = Console(ui); var view = (TerminalView)ui.Get("_terminalView");
        for (int i = 0; i < 30000; i++) terminal.Write("Línea de salida número " + i + " con texto suficiente para envolver en ventanas chicas");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ui.Frames(20);
        TestContext.Out.WriteLine($"20 frames with 20k lines: {clock.ElapsedMilliseconds} ms");
        Assert.That(terminal.Output.Count, Is.LessThanOrEqualTo(20000));
        Assert.That(view.Following, Is.True);
        int bottom = view.Top;
        ui.Key(ImGuiKey.PageUp); Assert.That(view.Top, Is.LessThan(bottom)); Assert.That(view.Following, Is.False);
        ui.Type("x"); Assert.That(view.Following, Is.True, "Typing returns to the prompt.");
        ui.Key(ImGuiKey.Backspace);
        Assert.That(ui.Bounds["Entrada terminal"].Visible, Is.True);
        ui.Capture($"consola-larga-{width}x{height}-{text}");
    }

    [Test]
    [Explicit(Reason)]
    public void ClickAndDragSelectOutputAndCtrlCCopiesIt()
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        var input = Input(ui); var view = (TerminalView)ui.Get("_terminalView");
        ui.Enter("limpiar"); ui.Enter("pwd"); ui.Frames(2);
        var io = ImGui.GetIO();
        var (min, _) = view.InputBounds;
        ui.MoveMouse(new(min.X + 2, min.Y - 30)); ui.Frames(2);
        io.AddMouseButtonEvent(0, true); ui.Frames(2);
        ui.MoveMouse(new(min.X + 200, min.Y - 30)); ui.Frames(2);
        io.AddMouseButtonEvent(0, false); ui.Frames(2);
        Assert.That(input.Selection.Active, Is.True);
        string selected = input.Selection.Text;
        ui.Key(ImGuiKey.C, ctrl: true);
        Assert.That(input.Selection.Active, Is.False); Assert.That(selected.Length, Is.GreaterThan(0));
        Assert.That(input.Notice, Does.Contain("Copiado"));
    }

    [Test]
    [Explicit(Reason)]
    public void OptionsChangedInOneStyleAreSeenInTheOthersWithoutRestarting()
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        ui.Enter("opcion consola-cursor barra"); ui.Enter("opcion texto 125"); ui.Frames(3);
        var prefs = (AppPreferences)ui.Get("_prefs");
        Assert.That(prefs.ConsoleCursor, Is.EqualTo(TerminalCursor.Bar)); Assert.That(prefs.TextPercent, Is.EqualTo(125));
        ui.Enter("interfaz terminal"); ui.Frames(4);
        var options = (IReadOnlyList<AppOption>)ui.Property("Options");
        Assert.That(options.Single(option => option.Id == "texto").Read(), Is.EqualTo("125"));
        options.Single(option => option.Id == "consola-crt").Write("on");
        ui.Call("SetInterface", InterfaceStyle.Hardcore); ui.Frames(4);
        ui.Enter("opcion consola-crt");
        Assert.That(Console(ui).Output.AllText(), Does.Contain("consola-crt = on"));
        ui.Enter("opcion consola-crt restablecer");
        Assert.That(prefs.ConsoleScanlines, Is.False);
    }

    [Test]
    [Explicit(Reason)]
    public void GameOrdersWorkOverTheRealServices()
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        var terminal = Console(ui);
        ui.Enter("ranuras"); Assert.That(terminal.Output.AllText(), Does.Contain("no disponible ahora"));
        ui.Enter("filtrar genero Plataformas"); ui.Enter("juegos"); ui.Enter("favorito 1 on"); ui.Enter("favoritos");
        Assert.That(terminal.Output.AllText(), Does.Contain("Faro del taller"));
        ui.Enter("ficha Faro"); Assert.That(terminal.Output.AllText(), Does.Contain("gamelist.xml"));
        ui.Enter("corregir 1 año 1999"); ui.Enter("ficha 1"); Assert.That(terminal.Output.AllText(), Does.Contain("Año: 1999"));
        ui.Enter("jugar 1"); ui.Frames(3); ui.Call("OpenTerminal"); ui.Frames(2);
        ui.Enter("guardar 1 reemplazar"); ui.Enter("guardar 1"); Assert.That(terminal.Output.AllText(), Does.Contain("ya tiene una partida"));
        ui.Enter("cargar 1"); ui.Enter("registros"); ui.Enter("memoria 8000 32"); ui.Enter("codigo"); ui.Enter("paso"); ui.Enter("bp agregar 8000"); ui.Enter("bp");
        string all = terminal.Output.AllText();
        Assert.That(all, Does.Contain("Ranura 1 cargada").And.Contain("Breakpoints: 8000").And.Contain("edición"));
        ui.Enter("bp limpiar"); ui.Enter("escribir 0040 09"); ui.Enter("memoria 0040 1");
        Assert.That(terminal.Output.AllText(), Does.Contain("0040  09"));
    }

    [Test]
    [Explicit(Reason)]
    public void BackgroundValidationLeavesThePromptAvailable()
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        var terminal = Console(ui); var tasks = (AppTaskCenter)ui.Get("_tasks");
        ui.Enter("juegos"); ui.Enter("preparar 1"); ui.Frames(2);
        Assert.That(tasks.All.Any(task => task.Kind == "preparación"), Is.True);
        ui.Enter("estado"); Assert.That(terminal.Output.AllText(), Does.Contain("Máquina:"));
        var task = tasks.All.First(task => task.Kind == "preparación");
        ui.Enter("seguir " + task.Id); ui.Enter("cancelar " + task.Id);
        Assert.That(SpinWait.SpinUntil(() => { ui.Frames(); return !task.Active; }, TimeSpan.FromSeconds(60)), Is.True);
        ui.Frames(2);
        Assert.That(terminal.Output.AllText(), Does.Contain("[" + task.Id + "]"));
        Assert.That(task.State, Is.AnyOf(AppTaskState.Cancelled, AppTaskState.PartlyCompleted, AppTaskState.Completed, AppTaskState.Failed));
    }
}
