using System.Diagnostics;
using System.Text;
using ImGuiNET;
using Retro.App;
using Retro.Core;

namespace App.Tests;

/// <summary>Whole journeys through the real program: hidden, muted and over invented data. Select explicitly.</summary>
[NonParallelizable]
public sealed class ProductFlowNativeTests
{
    private const string Reason = "Requiere gráficos nativos: usa una ventana oculta, audio silenciado y datos propios.";
    private static ProgramTerminal Console(NativeHarness ui) => (ProgramTerminal)ui.Property("Terminal");
    private static EmulatorHost Host(NativeHarness ui) => (EmulatorHost)ui.Get("_host");
    private static bool Captures(NativeHarness ui) => (bool)ui.Call("CaptureGuiKeyboard")!;
    private static AppPreferences Prefs(NativeHarness ui) => (AppPreferences)ui.Get("_prefs");

    [Explicit(Reason)]
    [TestCase("zx48"), TestCase("c64"), TestCase("pcxt"), TestCase("nes")]
    public void EachKeyboardGoesBackAndForthBetweenConsoleAndMachine(string machine)
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        var input = (InputMapper)ui.Get("_input");
        var sent = new List<string>(); var sink = input.KeySink;
        input.KeySink = (key, pressed) => { if (pressed) sent.Add(key); sink?.Invoke(key, pressed); };
        if (machine == "nes") { ui.Enter("juegos"); ui.Enter("jugar 1"); ui.Frames(3); ui.Call("OpenTerminal"); ui.Frames(2); }
        else ui.Enter("maquina " + machine);
        Assert.That(Console(ui).Output.AllText(), Does.Not.Contain("no disponible").And.Not.Contain("error:"), machine);
        Assert.That(Host(ui).Machine?.Id, Is.EqualTo(machine));
        ui.Type("hola mundo"); ui.Key(ImGuiKey.Escape);
        Assert.That(sent, Is.Empty, "Typing in the console must not reach " + machine);
        Assert.That(Captures(ui), Is.True);
        ui.Enter("continuar"); ui.Frames(4);
        Assert.That(Captures(ui), Is.False, machine + " must own the keyboard after continuar.");
        Assert.That(Host(ui).Paused, Is.False);
        Assert.That(sent, Is.Empty, "The Enter of continuar does not reach the machine.");
        ui.Call("OpenTerminal"); ui.Frames(3);
        Assert.That(Captures(ui), Is.True); Assert.That(Host(ui).Paused, Is.True);
    }

    /// <summary>Run alone with RETROEMU_QA_NO_AUDIO=1: the process then starts with no audio device, as on a computer without one.</summary>
    [Test]
    [Explicit(Reason)]
    public void AComputerWithoutSoundStillStartsPlaysAndRecoversTheDevice()
    {
        Assume.That(Environment.GetEnvironmentVariable("RETROEMU_QA_NO_AUDIO"), Is.EqualTo("1"));
        using var ui = new NativeHarness(1280, 840, 100, "retro");
        Assert.That(Host(ui).SoundDeviceMissing, Is.True);
        ui.Set("_soundWindowOpen", true); ui.Frames(3); ui.Capture("sin-audio-sonido"); ui.Set("_soundWindowOpen", false);
        ui.Call("OpenFile", Directory.GetFiles(ui.Games, "*.nes").First()); ui.Frames(30);
        Assert.That(Host(ui).Machine!.FrameCount, Is.GreaterThan(0), "The game runs."); Assert.That(Host(ui).Paused, Is.False);
        Assert.That((string)ui.Call("DiagnosticReport")!, Does.Contain("Audio: sin dispositivo"));
        var retry = ((IReadOnlyList<AppCommand>)ui.Property("Commands")).Single(command => command.Id == "audio-retry");
        Assert.That(retry.Enabled, Is.True); retry.Execute();
        Assert.That(Host(ui).SoundDeviceMissing, Is.False, "Asking again finds the device of this computer.");
        Assert.That(retry.Enabled, Is.False);
        ui.Frames(10); Assert.That(Host(ui).Machine!.FrameCount, Is.GreaterThan(0));
    }

    [Test]
    [Explicit(Reason)]
    public void ChangingStyleWithAGameOpenKeepsTheGameThePauseAndTheData()
    {
        using var ui = new NativeHarness(1280, 840, 100, "retro");
        string cartridge = Directory.GetFiles(ui.Games, "*.nes").First();
        ui.Call("OpenFile", cartridge); ui.Frames(4);
        Assert.That(Host(ui).Paused, Is.False);
        string media = Host(ui).LoadedMediaSource!;
        ui.Call("SetInterface", InterfaceStyle.Terminal); ui.Frames(4);
        Assert.That(Host(ui).Paused, Is.False, "A graphical style does not pause the game."); Assert.That(Captures(ui), Is.False);
        ui.Call("SetInterface", InterfaceStyle.Hardcore); ui.Frames(4);
        Assert.That(Host(ui).Paused, Is.True); Assert.That(Captures(ui), Is.True, "The console opens with the keyboard.");
        ui.Call("SetInterface", InterfaceStyle.Retro); ui.Frames(4);
        Assert.That(Host(ui).Paused, Is.False, "Leaving the console restores what the game was doing.");
        Assert.That(Captures(ui), Is.False); Assert.That(Host(ui).LoadedMediaSource, Is.EqualTo(media));
        Assert.That(Prefs(ui).Interface, Is.EqualTo(InterfaceStyle.Retro));
    }

    [Test]
    [Explicit(Reason)]
    public void ARunningTaskSurvivesAStyleChangeAndCanBeCancelledFromTheGraphicalList()
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        var tasks = (AppTaskCenter)ui.Get("_tasks");
        var blocker = new TaskCompletionSource();
        var task = tasks.Run("prueba", "tarea larga", async (work, token) => { work.Report("Esperando"); await blocker.Task.WaitAsync(token); });
        ui.Frames(3);
        ui.Call("SetInterface", InterfaceStyle.Retro); ui.Frames(4);
        Assert.That(task.Active, Is.True, "Changing style does not touch running work.");
        ui.Call("SetInterface", InterfaceStyle.Hardcore); ui.Frames(4);
        ui.Enter("tareas"); Assert.That(Console(ui).Output.AllText(), Does.Contain("tarea larga"));
        ui.Enter("tarea " + task.Id); Assert.That(Console(ui).Output.AllText(), Does.Contain("Esperando"));
        ui.Enter("cancelar " + task.Id);
        Assert.That(SpinWait.SpinUntil(() => { ui.Frames(); return !task.Active; }, TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(task.State, Is.EqualTo(AppTaskState.Cancelled));
        ui.Frames(3); Assert.That(Console(ui).Output.AllText(), Does.Contain("cancelada"));
    }

    [Test]
    [Explicit(Reason)]
    public void OptionsAndActionsAddedLaterReachEveryAccessWithoutChangingTheInterpreter()
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        bool indicator = false; int ran = 0;
        var options = ((IReadOnlyList<AppOption>)ui.Property("Options")).ToList();
        options.Add(AppOption.Toggle("indicador", "Indicador nuevo", "Consola", "Agregada después de crear la consola.", () => indicator, value => indicator = value) with { Default = "off" });
        ui.Set("_optionCatalog", options);
        var commands = ((IReadOnlyList<AppCommand>)ui.Property("Commands")).ToList();
        commands.Add(new("accion-nueva", "Acción recién agregada", "Herramientas", "novedad", "", () => ran++));
        ui.Set("_commandCatalog", commands);
        ui.Enter("hacer accion-nueva"); Assert.That(ran, Is.EqualTo(1));
        ui.Call("OpenTerminal"); ui.Frames(2);
        ui.Enter("opcion indicador on"); Assert.That(indicator, Is.True);
        ui.Enter("como novedad"); Assert.That(Console(ui).Output.AllText(), Does.Contain("hacer accion-nueva"));
        ui.Type("opcion indi"); ui.Key(ImGuiKey.Tab); Assert.That(((TerminalInput)ui.Get("_terminalInput")).Editor.Text, Is.EqualTo("opcion indicador "));
        ui.Key(ImGuiKey.Escape); ui.Key(ImGuiKey.Escape);
        ui.Call("SetInterface", InterfaceStyle.Terminal); ui.Frames(3);
        ui.Set("_preferencesGroup", "Consola"); ui.Set("_preferencesOpen", true); ui.Frames(4);
        Assert.That(ui.Bounds.ContainsKey("Opción:indicador"), Is.True, "The generic form presents the new option.");
        ui.Click("Opción:indicador"); Assert.That(indicator, Is.False, "The checkbox writes through the same contract.");
        Assert.That(CommandPalette.Search(commands, "novedad").Select(command => command.Id), Has.Member("accion-nueva"));
    }

    [Test]
    [Explicit(Reason)]
    public void ThePickerOnlyChangesAnythingWhenApplied()
    {
        using var ui = new NativeHarness(1280, 840, 100, "retro");
        var picker = ui.Get("_interfacePicker");
        var prefs = Prefs(ui);
        void Choose(string name) { Assert.That(ui.Bounds.ContainsKey(name), Is.True, name); ui.Click(name); }
        Assert.That(ui.App.OfferInterfaceAfterUpdate("not-a-version"), Is.False, "An invalid receipt offers nothing.");
        Assert.That(ui.App.OfferInterfaceAfterUpdate("9.0.1"), Is.True); ui.Frames(4);
        Choose("Elegir real"); ui.Frames(3);
        Assert.That(prefs.Interface, Is.EqualTo(InterfaceStyle.Retro), "Looking at a preview does not apply it.");
        Choose("Ampliar"); ui.Frames(3); ui.Capture("selector-ampliado"); Choose("Ampliar"); ui.Frames(2);
        Choose("Conservar el actual"); ui.Frames(3);
        Assert.That(prefs.Interface, Is.EqualTo(InterfaceStyle.Retro)); Assert.That(prefs.InterfaceOfferedVersion, Is.EqualTo("9.0.1"), "The answer is remembered.");
        Assert.That(ui.App.OfferInterfaceOnStartup("9.0.1"), Is.False, "An ordinary restart does not ask again.");
        Assert.That(ui.App.OfferInterfaceAfterUpdate("9.0.1"), Is.True, "Every validated update asks again.");
        Assert.That(ui.App.OfferInterfaceAfterUpdate("9.0.2"), Is.True); ui.Frames(4);
        Choose("Elegir terminal"); ui.Frames(2); Choose("Usar este estilo"); ui.Frames(4);
        Assert.That(prefs.Interface, Is.EqualTo(InterfaceStyle.Terminal));
        ui.Call("OpenInterfacePicker"); ui.Frames(4);
        Assert.That(picker.GetType().GetProperty("IsOpen")!.GetValue(picker), Is.EqualTo(true), "The choice stays available from the settings.");
    }

    [Test]
    [Explicit(Reason)]
    public void ProblemsOpeningAGameLeaveThePreviousStateAndAWayForward()
    {
        using var ui = new NativeHarness(1280, 840, 100, "hardcore");
        string broken = Path.Combine(ui.Games, "roto.xyz"); File.WriteAllBytes(broken, [1, 2, 3]);
        string empty = Path.Combine(ui.Games, "faltante.nes");
        ui.Enter($"abrir \"{broken}\"");
        Assert.That(Console(ui).Output.AllText(), Does.Contain("falta un recurso").And.Contain("no se modificaron"));
        Assert.That(ui.Get("_terminalOpen"), Is.EqualTo(true), "A failed open keeps the console.");
        ui.Enter($"abrir \"{empty}\""); Assert.That(Console(ui).Output.AllText(), Does.Contain("No existe ese archivo"));
        Assert.That((string?)typeof(RetroApp).GetField("_errorPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.App), Is.Null);
    }

    [TestCase(200), TestCase(100)]
    [Explicit(Reason)]
    public void TheFontAtlasFitsOlderGraphicsHardware(int text)
    {
        using var ui = new NativeHarness(1280, 840, text, "retro");
        var fonts = ImGui.GetIO().Fonts;
        TestContext.Out.WriteLine($"{text} %: {fonts.TexWidth}x{fonts.TexHeight}");
        Assert.That(Math.Max(fonts.TexWidth, fonts.TexHeight), Is.LessThanOrEqualTo(8192), "8192 is the common limit of graphics hardware that only offers OpenGL 3.3.");
    }

    /// <summary>The measured budgets. The evidence file goes next to the captures.</summary>
    [Test]
    [Explicit(Reason)]
    public void LargeLibrariesAndLongSessionsStayWithinTheirBudgets()
    {
        var report = new StringBuilder();
        var clock = Stopwatch.StartNew();
        using var ui = new NativeHarness(1280, 840, 100, "retro", games: 6000);
        report.AppendLine($"arranque y escaneo de 6000 juegos: {clock.ElapsedMilliseconds} ms");
        var home = (HomeScreen)ui.Get("_home");
        home.ShowOriginals(); ui.Frames(4);
        double Frames(int count)
        {
            var frames = Stopwatch.StartNew();
            ui.Frames(count);
            return frames.Elapsed.TotalMilliseconds / count;
        }
        // Only the interface's own cost: pacing would otherwise hide it behind the frame limiter.
        Raylib_cs.Raylib.SetTargetFPS(0);
        double list = Frames(120);
        report.AppendLine($"biblioteca con 6000 filas, por cuadro: {list:F2} ms");
        ui.Type("mina"); double typing = Frames(30);
        report.AppendLine($"con búsqueda escrita, por cuadro: {typing:F2} ms");
        ui.Call("SetInterface", InterfaceStyle.Hardcore); ui.Frames(4);
        var terminal = Console(ui);
        clock.Restart(); ui.Enter("juegos");
        report.AppendLine($"juegos con 6000 filas en la consola: {clock.ElapsedMilliseconds} ms");
        Raylib_cs.Raylib.SetTargetFPS(0);
        for (int i = 0; i < 40000; i++) terminal.Write("Línea número " + i + " de una sesión larga de trabajo en la consola interna");
        double console = Frames(120);
        report.AppendLine($"consola con {terminal.Output.Count} líneas, por cuadro: {console:F2} ms");
        report.AppendLine($"memoria administrada: {GC.GetTotalMemory(true) / (1024 * 1024)} MB");
        report.AppendLine($"atlas de fuentes al 100 %: {ImGui.GetIO().Fonts.TexWidth}x{ImGui.GetIO().Fonts.TexHeight}");
        TestContext.Out.WriteLine(report);
        string? folder = Environment.GetEnvironmentVariable("RETROEMU_QA_SHOTS");
        if (!string.IsNullOrWhiteSpace(folder)) File.WriteAllText(Path.Combine(folder, "rendimiento.txt"), report.ToString());
        Assert.That(list, Is.LessThan(16), "The list must not cost more than a frame at 60 Hz, whatever its length.");
        Assert.That(console, Is.LessThan(16));
    }
}
