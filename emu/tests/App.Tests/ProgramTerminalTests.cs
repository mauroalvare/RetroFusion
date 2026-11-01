using Retro.App;

namespace App.Tests;

public sealed class ProgramTerminalTests
{
    [TestCase("abrir \"C:\\Mis juegos\\Faro.nes\"", "abrir", "C:\\Mis juegos\\Faro.nes")]
    [TestCase("buscar 'La aventura'", "buscar", "La aventura")]
    [TestCase("buscar \"\"", "buscar", "")]
    [TestCase("buscar Faro;Remove-Item", "buscar", "Faro;Remove-Item")]
    public void ArgumentsAreLiteralAndWindowsPathsAreNotShellEscaped(string command, string first, string second)
    {
        Assert.That(ProgramTerminal.TryParse(command, out var words, out var error), Is.True);
        Assert.That(error, Is.Null); Assert.That(words, Is.EqualTo(new[] { first, second }));
    }

    [TestCase("abrir \"missing"), TestCase("buscar x\0y"), TestCase("ayuda\r\nsalir")]
    public void BrokenInputDoesNotExecuteAnyAction(string input)
    {
        int count = 0;
        var terminal = new ProgramTerminal(() => [new("abrir", "abrir ruta", "fixture", args => { count++; return []; })]);
        terminal.Submit(input); Assert.That(count, Is.Zero); Assert.That(terminal.Lines.Last(), Does.Not.StartWith("retro:/"));
    }

    [Test]
    public void CommandsHaveHistoryDraftCompletionBoundedOutputAndNoExternalShellFallback()
    {
        int calls = 0;
        var commands = new List<TerminalCommand> { new("juegos", "juegos", "fixture", args => { calls++; return ["Faro"]; }) };
        var terminal = new ProgramTerminal(() => commands);
        terminal.Submit("juegos"); terminal.Submit("juegos"); terminal.Submit("powershell Remove-Item anything");
        Assert.That(calls, Is.EqualTo(2)); Assert.That(terminal.History, Has.Count.EqualTo(2));
        Assert.That(terminal.Recall(-1, "borrador"), Is.EqualTo("powershell Remove-Item anything"));
        Assert.That(terminal.Recall(-1, ""), Is.EqualTo("juegos"));
        Assert.That(terminal.Recall(1, ""), Does.StartWith("powershell")); Assert.That(terminal.Recall(1, ""), Is.EqualTo("borrador"));
        Assert.That(terminal.Complete("jue"), Is.EqualTo("juegos "));
        commands.Add(new("nuevo", "nuevo", "new catalog action", _ => ["registered after terminal creation"]));
        terminal.Submit("ayuda nuevo"); Assert.That(terminal.Lines, Has.Some.Contains("new catalog action"));
        Assert.That(terminal.Complete("nue"), Is.EqualTo("nuevo "));
        for (int i = 0; i < 1000; i++) terminal.Write(i.ToString());
        Assert.That(terminal.Lines, Has.Count.EqualTo(600));
        terminal.Submit("limpiar"); Assert.That(terminal.Lines, Is.Empty); Assert.That(terminal.History, Is.Not.Empty);
    }

    [Test]
    public void SharedOptionValidationAndActionAvailabilityAreUsedAtExecutionTime()
    {
        int volume = 40;
        var options = new List<AppOption> { AppOption.Number("volumen", "Volumen", "Sonido", "fixture", () => volume, value => volume = value, 0, 100) };
        bool ready = false; int played = 0;
        var action = new AppCommand("play", "Jugar", "Partida", "", "", () => played++, () => ready, "Falta juego.");
        var terminal = new ProgramTerminal(() => [
            new("opcion", "opcion id valor", "fixture", args => { var option = options.Single(option => option.Id == args[0]); return [option.Write(args[1]) ?? option.Read()]; }),
            new("hacer", "hacer id", "fixture", args => { if (!action.Enabled) return [action.Unavailable]; action.Execute(); return ["done"]; })]);
        terminal.Submit("opcion volumen 75"); Assert.That(volume, Is.EqualTo(75));
        volume = 18; Assert.That(options[0].Read(), Is.EqualTo("18"));
        terminal.Submit("opcion volumen 101"); Assert.That(volume, Is.EqualTo(18));
        terminal.Submit("hacer play"); Assert.That(played, Is.Zero); ready = true; terminal.Submit("hacer play"); Assert.That(played, Is.EqualTo(1));
        bool value = false; options.Add(AppOption.Toggle("nueva", "Nueva", "Opciones", "new", () => value, next => value = next));
        terminal.Submit("opcion nueva on"); Assert.That(value, Is.True, "New options do not require changing the terminal parser.");
    }
}
