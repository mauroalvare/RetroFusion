using System.Diagnostics;
using Retro.App;

namespace App.Tests;

public sealed class ProgramTerminalTests
{
    private static string Text(ProgramTerminal terminal) => terminal.Output.AllText();
    private static CommandSpec Order(string name, Action<CommandContext>? run = null, params CommandArgument[] arguments) => new()
    { Id = "test." + name, Name = name, Group = "Biblioteca", Summary = "fixture " + name, Arguments = arguments, Run = run ?? (_ => { }) };

    [TestCase("abrir \"C:\\Mis juegos\\Faro.nes\"", "abrir", "C:\\Mis juegos\\Faro.nes")]
    [TestCase("buscar 'La aventura'", "buscar", "La aventura")]
    [TestCase("buscar \"\"", "buscar", "")]
    [TestCase("buscar Faro;Remove-Item", "buscar", "Faro;Remove-Item")]
    [TestCase("carpeta \"C:\\Mis juegos\\\"", "carpeta", "C:\\Mis juegos\\")]
    [TestCase("buscar Dragon's", "buscar", "Dragon's")]
    [TestCase("buscar \"dijo \\\"hola\\\" fuerte\"", "buscar", "dijo \"hola\" fuerte")]
    [TestCase("abrir C:\\Juegos\\ñandú.nes", "abrir", "C:\\Juegos\\ñandú.nes")]
    [TestCase("abrir \"C:\\A b\\\" x", "abrir", "C:\\A b\\", "x")]
    public void ArgumentsAreLiteralAndWindowsPathsAreNotShellEscaped(string command, params string[] expected)
    {
        Assert.That(ProgramTerminal.TryParse(command, out var words, out var error), Is.True, error);
        Assert.That(error, Is.Null); Assert.That(words, Is.EqualTo(expected));
    }

    [Test]
    public void TokensKnowWhereTheySitInTheLine()
    {
        var tokens = CommandLine.Tokenize("abrir  \"mi juego\" x", out bool open);
        Assert.That(open, Is.False);
        Assert.That(tokens.Select(token => (token.Text, token.Start, token.End)), Is.EqualTo(new[] { ("abrir", 0, 5), ("mi juego", 7, 17), ("x", 18, 19) }));
        Assert.That(CommandLine.Tokenize("abrir \"mi ju", out open).Last().Text, Is.EqualTo("mi ju")); Assert.That(open, Is.True);
    }

    [TestCase("abrir \"missing"), TestCase("buscar x\0y"), TestCase("ayuda\r\nsalir")]
    public void BrokenInputDoesNotExecuteAnyAction(string input)
    {
        int count = 0;
        var terminal = new ProgramTerminal(() => [Order("abrir", _ => count++)]);
        Assert.That(terminal.Submit(input), Is.False); Assert.That(count, Is.Zero);
        Assert.That(terminal.Output.LineText(terminal.Output.NextLine - 1), Does.StartWith("error:"));
    }

    [Test]
    public void OrdersDefinedAfterTheConsoleExistAppearEverywhere()
    {
        var commands = new List<CommandSpec> { Order("juegos") };
        var terminal = new ProgramTerminal(() => commands);
        commands.Add(Order("nuevo", context => context.Write("registrada después")) );
        terminal.Submit("ayuda nuevo"); Assert.That(Text(terminal), Does.Contain("fixture nuevo"));
        terminal.Submit("ayuda"); Assert.That(Text(terminal), Does.Contain("nuevo"));
        terminal.Submit("nuevo"); Assert.That(Text(terminal), Does.Contain("registrada después"));
        Assert.That(terminal.Complete("nue", 3).Items.Select(item => item.Value), Is.EqualTo(new[] { "nuevo" }));
    }

    [Test]
    public void FailuresAreToldApartAndAlwaysSayWhatToDoNext()
    {
        var terminal = new ProgramTerminal(() =>
        [
            Order("jugar", null, new CommandArgument("juego")),
            new() { Id = "t.reg", Name = "registros", Group = "Análisis", Summary = "s", Unavailable = () => new(CommandProblem.WrongContext, "No hay una máquina encendida.", "juegos lista tu biblioteca."), Run = _ => Assert.Fail("must not run") },
            new() { Id = "t.plan", Name = "mezclar", Group = "Proyectos", Summary = "s", Planned = "hacer fusion abre el taller." },
            Order("elegir", _ => throw new CommandFailure(CommandProblem.Ambiguous, "2 juegos coinciden.", "1. Uno", "2. Dos")),
            Order("faltar", _ => throw new CommandFailure(CommandProblem.MissingResource, "No existe ese archivo."))
        ]);
        terminal.Submit("jugra 1"); Assert.That(Text(terminal), Does.Contain("orden desconocida: jugra").And.Contain("¿Quisiste decir jugar?"));
        terminal.Submit("jugar"); Assert.That(Text(terminal), Does.Contain("argumento inválido: Falta juego").And.Contain("Uso: jugar <juego>"));
        terminal.Submit("jugar 1 2"); Assert.That(Text(terminal), Does.Contain("Sobran datos: 2"));
        terminal.Submit("registros"); Assert.That(Text(terminal), Does.Contain("no disponible ahora: No hay una máquina").And.Contain("juegos lista tu biblioteca"));
        terminal.Submit("mezclar"); Assert.That(Text(terminal), Does.Contain("todavía no implementado").And.Contain("hacer fusion abre el taller"));
        terminal.Submit("elegir"); Assert.That(Text(terminal), Does.Contain("selección ambigua").And.Contain("1. Uno"));
        terminal.Submit("faltar"); Assert.That(Text(terminal), Does.Contain("falta un recurso"));
    }

    [TestCase("powershell Remove-Item anything"), TestCase("cmd /c del x"), TestCase("del juego.nes"), TestCase("bash -c ls")]
    public void SystemCommandsAreNeverRunAndSaySo(string line)
    {
        int ran = 0;
        var terminal = new ProgramTerminal(() => [Order("juegos", _ => ran++)]);
        Assert.That(terminal.Submit(line), Is.False);
        Assert.That(ran, Is.Zero); Assert.That(Text(terminal), Does.Contain("Esta consola solo procesa órdenes de RetroEmu"));
    }

    [Test]
    public void OrderNamesIgnoreCaseAndAccents()
    {
        int ran = 0;
        var terminal = new ProgramTerminal(() => [new() { Id = "o", Name = "opcion", Group = "Sistema", Summary = "s", Aliases = ["ajuste"], Run = _ => ran++ }]);
        terminal.Submit("OPCIÓN"); terminal.Submit("ajuste"); terminal.Submit("Opcion");
        Assert.That(ran, Is.EqualTo(3));
    }

    [Test]
    public void ArgumentsAreValidatedFromTheDefinition()
    {
        var seen = new List<string>();
        var terminal = new ProgramTerminal(() =>
        [
            Order("ranura", context => seen.Add("n" + context.Number("n")), new CommandArgument("n", ArgumentKind.Number) { Min = 1, Max = 5 }),
            Order("hex", context => seen.Add("h" + context.Hex("dir")), new CommandArgument("dir", ArgumentKind.Hex)),
            Order("modo", context => seen.Add(context.Text("m")), new CommandArgument("m", ArgumentKind.Choice) { Choices = ["uno", "dos"] }),
            Order("marca", context => seen.Add(context.Toggle("t").ToString()), new CommandArgument("t", ArgumentKind.Toggle)),
            Order("titulo", context => seen.Add(context.Text("t")), new CommandArgument("t", ArgumentKind.Text) { Rest = true })
        ]);
        foreach (string bad in new[] { "ranura 6", "ranura x", "hex zz", "modo tres", "marca quizas" }) Assert.That(terminal.Submit(bad), Is.False, bad);
        foreach (string good in new[] { "ranura 5", "hex 0x10", "hex $20", "modo DOS", "marca sí", "titulo La mina de Ohm" }) Assert.That(terminal.Submit(good), Is.True, good);
        Assert.That(seen, Is.EqualTo(new[] { "n5", "h16", "h32", "DOS", "True", "La mina de Ohm" }));
    }

    [Test]
    public void HelpIsGeneratedFromTheDefinition()
    {
        var terminal = new ProgramTerminal(() =>
        [
            new()
            {
                Id = "t.play", Name = "jugar", Group = "Partida", Summary = "Abre un juego.", Aliases = ["play"], Examples = ["jugar 1"], Result = "La partida toma el teclado.",
                Arguments = [new("juego", ArgumentKind.Word, "Número o título.")], Errors = ["el archivo no existe"], Cancellation = "Ctrl+Shift+T vuelve.", Requires = "un juego",
                Unavailable = () => new(CommandProblem.WrongContext, "Sin biblioteca."), Run = _ => { }
            }
        ]);
        terminal.Submit("ayuda jugar");
        foreach (string expected in new[] { "jugar · Partida", "Uso: jugar <juego>", "juego: Número o título.", "Ejemplo: jugar 1", "También: play", "Requiere: un juego", "ahora no disponible: Sin biblioteca", "Resultado: La partida toma el teclado.", "Puede fallar si el archivo no existe", "Cancelar: Ctrl+Shift+T vuelve." })
            Assert.That(Text(terminal), Does.Contain(expected));
        terminal.Submit("ayuda Partida"); Assert.That(Text(terminal), Does.Contain("Abre un juego."));
        terminal.Submit("ayuda inexistente"); Assert.That(Text(terminal), Does.Contain("como inexistente busca por tema"));
    }

    [Test]
    public void CapabilitiesAreFoundByWhatYouWantToDo()
    {
        var terminal = new ProgramTerminal(() =>
        [
            new() { Id = "t.save", Name = "guardar", Group = "Partida", Summary = "Guarda la partida.", Keywords = "progreso", Run = _ => { } },
            new() { Id = "t.other", Name = "otra", Group = "Partida", Summary = "Nada que ver.", Run = _ => { } }
        ]) { Capabilities = _ => [("opcion volumen", "Volumen del sonido")] };
        terminal.Submit("como guardar mi progreso"); Assert.That(Text(terminal), Does.Contain("guardar").And.Not.Contain("Nada que ver"));
        terminal.Submit("como subir el volumen"); Assert.That(Text(terminal), Does.Contain("opcion volumen"));
        terminal.Submit("como xyzzy"); Assert.That(Text(terminal), Does.Contain("No encontré funciones sobre xyzzy"));
    }

    [Test]
    public void CompletionFollowsTheArgumentBeingWritten()
    {
        var terminal = new ProgramTerminal(() =>
        [
            Order("jugar", null, new CommandArgument("juego", ArgumentKind.Game) { Rest = true, Complete = _ => [new("La mina de Ohm"), new("Faro del taller")] }),
            Order("filtrar", null, new CommandArgument("campo", ArgumentKind.Choice) { Choices = ["genero", "año"] }, new CommandArgument("valor") { Complete = earlier => earlier[0] == "genero" ? [new("Puzles")] : [new("1985")] })
        ]);
        Assert.That(terminal.Complete("ju", 2).Items.Select(item => item.Value), Is.EqualTo(new[] { "jugar" }));
        Assert.That(terminal.Complete("jugar ", 6).Items, Has.Count.EqualTo(2));
        Assert.That(terminal.Complete("jugar mina", 10).Items.Single().Value, Is.EqualTo("La mina de Ohm"), "A title is found by a word inside it.");
        Assert.That(terminal.Complete("filtrar ", 8).Items.Select(item => item.Value), Is.EquivalentTo(new[] { "genero", "año" }));
        Assert.That(terminal.Complete("filtrar genero ", 15).Items.Single().Value, Is.EqualTo("Puzles"));
        Assert.That(terminal.Complete("filtrar año ", 12).Items.Single().Value, Is.EqualTo("1985"));
        Assert.That(terminal.Complete("filtrar año 1985 ", 17).Items, Is.Empty, "An order without more arguments offers nothing.");
    }

    [Test]
    public void TablesStayAlignedAndNeverCutAHeader()
    {
        TableColumn[] columns = [new("#", Right: true), new("Título", Flexible: true), new("Sistema"), new("Género", Optional: true)];
        string[][] rows = [["1", "Un título bastante largo para esta tabla", "nes", "Plataformas"], ["12", "Corto", "zx48", ""]];
        var wide = TextTable.Layout(columns, rows, 100).ToArray();
        Assert.That(wide[0].Kind, Is.EqualTo(TerminalKind.Heading)); Assert.That(wide[1].Kind, Is.EqualTo(TerminalKind.Rule));
        Assert.That(wide[2].Text.IndexOf("nes", StringComparison.Ordinal), Is.EqualTo(wide[3].Text.IndexOf("zx48", StringComparison.Ordinal)));
        Assert.That(wide[2].Text, Does.StartWith(" 1 "));
        var narrow = TextTable.Layout(columns, rows, 30).ToArray();
        Assert.That(narrow[0].Text, Does.Contain("Título").And.Not.Contain("…").And.Not.Contain("Género"), "The optional column goes first; a header is never ellipsized.");
        Assert.That(narrow.All(line => line.Text.Length <= 30), Is.True);
        Assert.That(narrow[2].Text, Does.Contain("…"));
    }

    [Test]
    public void SharedOptionValidationAndActionAvailabilityAreUsedAtExecutionTime()
    {
        int volume = 40;
        var options = new List<AppOption> { AppOption.Number("volumen", "Volumen", "Sonido", "fixture", () => volume, value => volume = value, 0, 100) };
        bool ready = false; int played = 0;
        var action = new AppCommand("play", "Jugar", "Partida", "", "", () => played++, () => ready, "Falta juego.");
        var terminal = new ProgramTerminal(() => [
            Order("opcion", context => { var option = options.Single(option => option.Id == context.Text("id")); if (option.Write(context.Text("v")) is { } problem) throw new CommandFailure(CommandProblem.InvalidArgument, problem); }, new CommandArgument("id"), new CommandArgument("v")),
            Order("hacer", context => { if (!action.Enabled) throw new CommandFailure(CommandProblem.WrongContext, action.Unavailable); action.Execute(); }, new CommandArgument("id"))]);
        terminal.Submit("opcion volumen 75"); Assert.That(volume, Is.EqualTo(75));
        volume = 18; Assert.That(options[0].Read(), Is.EqualTo("18"));
        terminal.Submit("opcion volumen 101"); Assert.That(volume, Is.EqualTo(18));
        terminal.Submit("hacer play"); Assert.That(played, Is.Zero); ready = true; terminal.Submit("hacer play"); Assert.That(played, Is.EqualTo(1));
        bool value = false; options.Add(AppOption.Toggle("nueva", "Nueva", "Opciones", "new", () => value, next => value = next));
        terminal.Submit("opcion nueva on"); Assert.That(value, Is.True, "New options do not require changing the terminal parser.");
    }

    [Test]
    public void ACrashingHandlerNeverTakesTheConsoleDown()
    {
        var terminal = new ProgramTerminal(() => [Order("roto", _ => throw new IOException("disco lleno"))]);
        Assert.That(terminal.Submit("roto"), Is.False);
        Assert.That(Text(terminal), Does.Contain("error: disco lleno").And.Contain("sigue funcionando"));
    }
}

public sealed class TerminalBufferTests
{
    [Test]
    public void LinesWrapAtWordsAndKeepTheirLogicalText()
    {
        var buffer = new TerminalBuffer(); buffer.SetColumns(10);
        buffer.Append("uno dos tres cuatro");
        Assert.That(Enumerable.Range(0, buffer.RowCount).Select(row => buffer.RowText(buffer.Row(row)).TrimEnd()), Is.EqualTo(new[] { "uno dos", "tres", "cuatro" }));
        Assert.That(buffer.AllText(), Is.EqualTo("uno dos tres cuatro"), "Copying gains no line breaks from soft wrapping.");
        buffer.SetColumns(30);
        Assert.That(buffer.RowCount, Is.EqualTo(1));
    }

    [Test]
    public void AWordLongerThanTheRowIsCutAndContinuationsKeepTheIndent()
    {
        var buffer = new TerminalBuffer(); buffer.SetColumns(10);
        buffer.Append(new string('x', 25));
        Assert.That(buffer.RowCount, Is.EqualTo(3));
        buffer.Clear(); buffer.Append("  aaaa bbbb cccc");
        Assert.That(buffer.Row(1).Indent, Is.EqualTo(2));
        Assert.That(buffer.Row(1).Length + buffer.Row(1).Indent, Is.LessThanOrEqualTo(10));
    }

    [Test]
    public void ScrollbackHasExplicitLimitsOnLinesAndCharacters()
    {
        var lines = new TerminalBuffer(maxLines: 100);
        for (int i = 0; i < 1000; i++) lines.Append("línea " + i);
        Assert.That(lines.Count, Is.LessThanOrEqualTo(100)); Assert.That(lines.Dropped, Is.GreaterThan(0));
        Assert.That(lines.LineText(lines.NextLine - 1), Is.EqualTo("línea 999"));
        var characters = new TerminalBuffer(maxLines: 100000, maxCharacters: 5000);
        for (int i = 0; i < 2000; i++) characters.Append(new string('z', 100));
        Assert.That(characters.Characters, Is.LessThanOrEqualTo(5000));
        var long_ = new TerminalBuffer();
        long_.Append(new string('q', TerminalBuffer.MaxLineLength * 2));
        Assert.That(long_.LineText(long_.FirstLine).Length, Is.EqualTo(TerminalBuffer.MaxLineLength + 1));
    }

    [Test]
    public void RowLookupsAreCheapWithAHugeScrollback()
    {
        var buffer = new TerminalBuffer(); buffer.SetColumns(40);
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 200_000; i++) buffer.Append("Una línea larga de salida con varias palabras que se envuelve en dos o tres filas " + i);
        long append = clock.ElapsedMilliseconds; clock.Restart();
        for (int i = 0; i < 2_000_000; i++) _ = buffer.Row(i % buffer.RowCount);
        long lookups = clock.ElapsedMilliseconds; clock.Restart();
        buffer.SetColumns(80);
        long rewrap = clock.ElapsedMilliseconds;
        TestContext.Out.WriteLine($"append={append} ms lookups={lookups} ms rewrap={rewrap} ms rows={buffer.RowCount}");
        Assert.That(buffer.Count, Is.LessThanOrEqualTo(20000));
        Assert.That(lookups, Is.LessThan(2000), "2M row lookups must stay far under a frame budget each.");
        Assert.That(rewrap, Is.LessThan(500));
    }

    [Test]
    public void SelectionFollowsTextAcrossResizeAndLeavesOutRules()
    {
        var buffer = new TerminalBuffer(); buffer.SetColumns(20);
        buffer.Append("Primera línea de la salida"); buffer.Append("─────", TerminalKind.Rule); buffer.Append("Segunda");
        var selection = new TerminalSelection(buffer);
        selection.Begin(buffer.PointAt(0, 8)); selection.Extend(new(buffer.NextLine - 1, 7));
        string before = selection.Text;
        Assert.That(before, Is.EqualTo("línea de la salida" + Environment.NewLine + "Segunda"));
        buffer.SetColumns(10);
        Assert.That(selection.Text, Is.EqualTo(before));
        selection.SelectWord(new(buffer.FirstLine, 10)); Assert.That(selection.Text, Is.EqualTo("línea"));
        selection.SelectLine(new(buffer.FirstLine, 3)); Assert.That(selection.Text, Is.EqualTo("Primera línea de la salida"));
    }

    [Test]
    public void LastOutputIsTheResultWithoutTheEchoAndWithoutDecoration()
    {
        var buffer = new TerminalBuffer();
        buffer.BeginBlock(); buffer.Append("retro:/x> juegos", TerminalKind.Command); buffer.Append("Título"); buffer.Append("──", TerminalKind.Rule); buffer.Append("Faro");
        Assert.That(buffer.LastOutput(), Is.EqualTo("Título" + Environment.NewLine + "Faro"));
        buffer.BeginBlock(); buffer.Append("retro:/x> copiar", TerminalKind.Command);
        Assert.That(buffer.LastOutput(previous: true), Is.EqualTo("Título" + Environment.NewLine + "Faro"), "copiar copies what came before it.");
    }

    [Test]
    public void ControlCharactersNeverReachTheScreenAndAccentsCompose()
    {
        var buffer = new TerminalBuffer();
        buffer.Append("a\u001b[31mb\tc\r\nd");
        Assert.That(buffer.LineText(buffer.FirstLine), Is.EqualTo("a [31mb c")); Assert.That(buffer.LineText(buffer.FirstLine + 1), Is.EqualTo("d"));
        buffer.Clear(); buffer.Append("cafe\u0301");
        Assert.That(buffer.LineText(buffer.FirstLine), Is.EqualTo("café"));
    }
}

public sealed class LineEditorTests
{
    [Test]
    public void MovesAndDeletesByCharacterAndWord()
    {
        var editor = new LineEditor(); editor.Insert("jugar \"Faro del taller\" 12");
        editor.WordLeftMove(); Assert.That(editor.Cursor, Is.EqualTo(24));
        editor.WordLeftMove(); Assert.That(editor.Text[editor.Cursor..], Does.StartWith("taller"));
        editor.DeleteWordLeft(); Assert.That(editor.Text, Is.EqualTo("jugar \"Faro taller\" 12"));
        editor.End(); editor.DeleteWordLeft(); Assert.That(editor.Text, Is.EqualTo("jugar \"Faro taller\" "));
        editor.Home(); editor.DeleteWordRight(); Assert.That(editor.Text, Is.EqualTo("Faro taller\" "));
        editor.End(); editor.Backspace(); editor.Home(); editor.Delete();
        Assert.That(editor.Text, Is.EqualTo("aro taller\""));
    }

    [Test]
    public void SelectionReplacesCopiesAndCuts()
    {
        var editor = new LineEditor(); editor.Insert("hola mundo");
        editor.Home(); for (int i = 0; i < 4; i++) editor.Right(select: true);
        Assert.That(editor.SelectedText, Is.EqualTo("hola"));
        editor.Insert("chau"); Assert.That(editor.Text, Is.EqualTo("chau mundo"));
        editor.WordLeftMove(select: true); Assert.That(editor.Cut(), Is.EqualTo("chau")); Assert.That(editor.Text, Is.EqualTo(" mundo"));
        editor.SelectAll(); editor.Backspace(); Assert.That(editor.Text, Is.Empty);
    }

    [Test]
    public void UndoTreatsTypedRunsAsOneEditAndRedoReturns()
    {
        var editor = new LineEditor();
        foreach (char c in "hola") editor.Insert(c.ToString());
        editor.Insert(" "); foreach (char c in "vos") editor.Insert(c.ToString());
        Assert.That(editor.Undo(), Is.True); Assert.That(editor.Text, Is.EqualTo("hola "));
        editor.Undo(); editor.Undo(); Assert.That(editor.Text, Is.Empty);
        editor.Redo(); Assert.That(editor.Text, Is.EqualTo("hola"));
    }

    [Test]
    public void CursorTreatsCombinedLettersAndSurrogatesAsOneStepAndLinesStayClean()
    {
        var editor = new LineEditor(); editor.Insert("a\U0001F3AEb");
        editor.Left(); editor.Left(); Assert.That(editor.Cursor, Is.EqualTo(1));
        editor.Delete(); Assert.That(editor.Text, Is.EqualTo("ab"));
        editor.Clear(); editor.Insert("cafe\u0301"); Assert.That(editor.Text, Is.EqualTo("café")); Assert.That(editor.Text.Length, Is.EqualTo(4));
        editor.Clear(); editor.Insert("uno\ndos\tx\u0007"); Assert.That(editor.Text, Is.EqualTo("uno dos x"));
        editor.Clear(); editor.Insert(new string('a', CommandLine.MaxLength + 500)); Assert.That(editor.Text.Length, Is.EqualTo(CommandLine.MaxLength));
    }
}

public sealed class CommandHistoryTests
{
    [Test]
    public void BrowsingKeepsTheUnsentDraft()
    {
        var history = new CommandHistory();
        history.Add("juegos"); history.Add("juegos"); history.Add("ficha 1");
        Assert.That(history.Entries, Has.Count.EqualTo(2), "An immediate repeat is one entry.");
        Assert.That(history.Move(-1, "borrador sin enviar"), Is.EqualTo("ficha 1"));
        Assert.That(history.Move(-1, "ficha 1"), Is.EqualTo("juegos"));
        Assert.That(history.Move(-1, "juegos"), Is.EqualTo("juegos"));
        Assert.That(history.Move(1, "juegos"), Is.EqualTo("ficha 1"));
        Assert.That(history.Move(1, "ficha 1"), Is.EqualTo("borrador sin enviar"));
    }

    [Test]
    public void SurvivesASessionAndADamagedFile()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-history-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string path = Path.Combine(folder, "historial.json");
            var first = new CommandHistory(path); first.Add("jugar 1"); first.Save();
            Assert.That(new CommandHistory(path).Entries, Is.EqualTo(new[] { "jugar 1" }));
            File.WriteAllText(path, "{ dañado");
            var damaged = new CommandHistory(path);
            Assert.That(damaged.Entries, Is.Empty); Assert.That(damaged.Problem, Is.Not.Null);
            damaged.Add("x"); damaged.Save();
            Assert.That(File.ReadAllText(path), Is.EqualTo("{ dañado"), "A file that could not be read is left for recovery.");
        }
        finally { Directory.Delete(folder, true); }
    }

    [Test]
    public void OptingOutWritesNothing()
    {
        string path = Path.Combine(Path.GetTempPath(), "retro-history-" + Guid.NewGuid().ToString("N") + ".json");
        var history = new CommandHistory(path) { Persist = false };
        history.Add("jugar 1"); history.Save();
        Assert.That(File.Exists(path), Is.False);
    }
}

public sealed class TerminalInputTests
{
    private string _clipboard = "";
    private readonly List<string> _ran = [];
    private TerminalInput Input(out ProgramTerminal terminal, params string[] titles)
    {
        _ran.Clear();
        string[] titleList = titles;
        terminal = new ProgramTerminal(() =>
        [
            new() { Id = "t.j", Name = "jugar", Group = "Partida", Summary = "s", Arguments = [new("juego", ArgumentKind.Game) { Rest = true, Complete = _ => titleList.Select(title => new CompletionItem(title)) }], Run = context => _ran.Add("jugar " + context.Text("juego")) },
            new() { Id = "t.g", Name = "juegos", Group = "Biblioteca", Summary = "s", Run = context => { _ran.Add("juegos"); context.Write("Faro"); } },
            new() { Id = "t.f", Name = "fallar", Group = "Consola", Summary = "s", Run = _ => throw new CommandFailure(CommandProblem.Failed, "no") },
        ]);
        return new(terminal, () => _clipboard, value => _clipboard = value);
    }

    private static void Type(TerminalInput input, string text) { foreach (char c in text) input.Text(c.ToString()); }

    [Test]
    public void EnterRunsAndClearsAndHistoryReturnsTheDraft()
    {
        var input = Input(out _);
        Type(input, "juegos"); input.Key(TerminalKey.Enter);
        Assert.That(_ran, Is.EqualTo(new[] { "juegos" })); Assert.That(input.Editor.Text, Is.Empty);
        Type(input, "borrador"); input.Key(TerminalKey.Up); Assert.That(input.Editor.Text, Is.EqualTo("juegos"));
        input.Key(TerminalKey.Down); Assert.That(input.Editor.Text, Is.EqualTo("borrador"));
    }

    [Test]
    public void TabCompletesRecordsSharedTextAndCyclesWithoutLosingTheLine()
    {
        var input = Input(out _, "Mina uno", "Mina dos", "Faro");
        Type(input, "ju"); input.Key(TerminalKey.Tab);
        Assert.That(input.Choosing, Is.True); Assert.That(input.Editor.Text, Is.EqualTo("juegos"));
        input.Key(TerminalKey.Escape); Assert.That(input.Editor.Text, Is.EqualTo("ju"));
        input.Editor.Clear(); Type(input, "jug"); input.Key(TerminalKey.Tab); Assert.That(input.Editor.Text, Is.EqualTo("jugar "));
        Type(input, "Mi"); input.Key(TerminalKey.Tab);
        Assert.That(input.Editor.Text, Is.EqualTo("jugar \"Mina "), "What every alternative shares is written first.");
        input.Key(TerminalKey.Tab); Assert.That(input.Choosing, Is.True);
        Assert.That(input.Editor.Text, Is.EqualTo("jugar \"Mina dos\""));
        input.Key(TerminalKey.Tab); Assert.That(input.Editor.Text, Is.EqualTo("jugar \"Mina uno\""));
        input.Key(TerminalKey.Tab, TerminalMods.Shift); Assert.That(input.Editor.Text, Is.EqualTo("jugar \"Mina dos\""));
        input.Key(TerminalKey.Escape); Assert.That(input.Choosing, Is.False);
        Assert.That(input.Editor.Text, Is.EqualTo("jugar \"Mina "), "Escape leaves the line as it was.");
    }

    [Test]
    public void ChoosingWithEnterKeepsTheChoiceWithoutRunningIt()
    {
        var input = Input(out _, "Mina uno", "Mina dos");
        Type(input, "jugar \"Mina "); input.Key(TerminalKey.Tab); Assert.That(input.Choosing, Is.True);
        input.Key(TerminalKey.Enter);
        Assert.That(input.Choosing, Is.False); Assert.That(_ran, Is.Empty); Assert.That(input.Editor.Text, Does.StartWith("jugar \"Mina dos\""));
        input.Key(TerminalKey.Enter); Assert.That(_ran.Single(), Is.EqualTo("jugar Mina dos"));
    }

    [Test]
    public void ReverseSearchFindsLeavesAndCancels()
    {
        var input = Input(out var terminal);
        terminal.History.Add("jugar Faro"); terminal.History.Add("juegos"); terminal.History.Add("jugar Mina");
        Type(input, "borrador");
        input.Key(TerminalKey.R, TerminalMods.Ctrl); Assert.That(input.Searching, Is.True);
        Type(input, "jugar"); Assert.That(input.Editor.Text, Is.EqualTo("jugar Mina"));
        input.Key(TerminalKey.R, TerminalMods.Ctrl); Assert.That(input.Editor.Text, Is.EqualTo("jugar Faro"));
        input.Key(TerminalKey.Enter); Assert.That(input.Searching, Is.False);
        Assert.That(_ran, Is.Empty, "A found order is shown, not run."); Assert.That(input.Editor.Text, Is.EqualTo("jugar Faro"));
        input.Editor.Set("borrador"); input.Key(TerminalKey.R, TerminalMods.Ctrl); Type(input, "nada"); Assert.That(input.SearchFailed, Is.True);
        input.Key(TerminalKey.Escape); Assert.That(input.Editor.Text, Is.EqualTo("borrador"));
    }

    [Test]
    public void PastingOneLineNeverRunsIt()
    {
        var input = Input(out _);
        _clipboard = "juegos\r\n"; input.Key(TerminalKey.V, TerminalMods.Ctrl);
        Assert.That(input.Editor.Text, Is.EqualTo("juegos")); Assert.That(_ran, Is.Empty); Assert.That(input.ReviewingPaste, Is.False);
    }

    [Test]
    public void SeveralPastedLinesWaitForReviewAndStopAtTheFirstFailure()
    {
        var input = Input(out var terminal);
        input.Paste("juegos\n\nfallar\njugar 1\n");
        Assert.That(input.ReviewingPaste, Is.True); Assert.That(input.PendingPaste, Is.EqualTo(new[] { "juegos", "fallar", "jugar 1" })); Assert.That(_ran, Is.Empty);
        input.Text("x"); Assert.That(input.Editor.Text, Is.Empty, "Typing does not slip into a review.");
        input.Key(TerminalKey.Enter);
        Assert.That(_ran, Is.EqualTo(new[] { "juegos" }), "The sequence stops at the failure.");
        Assert.That(terminal.Output.AllText(), Does.Contain("se detuvo el pegado en «fallar»"));
        input.Paste("juegos\njuegos"); input.Key(TerminalKey.Escape);
        Assert.That(input.ReviewingPaste, Is.False); Assert.That(_ran, Has.Count.EqualTo(1)); Assert.That(input.Notice, Does.Contain("no se ejecutó"));
        input.Paste(string.Join('\n', Enumerable.Repeat("juegos", TerminalInput.MaxPasteLines + 1)));
        Assert.That(input.ReviewingPaste, Is.False); Assert.That(input.Notice, Does.Contain("demasiado largo"));
    }

    [Test]
    public void CtrlCCopiesThenAbandonsTheLineAndNeverStopsWork()
    {
        var input = Input(out var terminal); int running = 1; bool following = true;
        input.RunningTasks = () => running; input.StopFollowing = () => { bool was = following; following = false; return was; };
        terminal.Write("Faro del taller");
        input.Selection.SelectLine(new(terminal.Output.NextLine - 1, 0));
        input.Key(TerminalKey.C, TerminalMods.Ctrl);
        Assert.That(_clipboard, Is.EqualTo("Faro del taller")); Assert.That(input.Selection.Active, Is.False);
        input.Key(TerminalKey.C, TerminalMods.Ctrl); Assert.That(following, Is.False, "Without selection it first stops following.");
        Type(input, "jugar x"); input.Key(TerminalKey.C, TerminalMods.Ctrl);
        Assert.That(input.Editor.Text, Is.Empty); Assert.That(terminal.Output.AllText(), Does.Contain("jugar x^C")); Assert.That(_ran, Is.Empty);
        input.Key(TerminalKey.C, TerminalMods.Ctrl); Assert.That(input.Notice, Does.Contain("cancelar <id>")); Assert.That(running, Is.EqualTo(1));
    }

    [Test]
    public void FamiliarShortcutsEditTheLine()
    {
        var input = Input(out _);
        Type(input, "jugar faro");
        input.Key(TerminalKey.A, TerminalMods.Ctrl); Assert.That(input.Editor.Cursor, Is.Zero);
        input.Key(TerminalKey.E, TerminalMods.Ctrl); Assert.That(input.Editor.Cursor, Is.EqualTo(10));
        input.Key(TerminalKey.W, TerminalMods.Ctrl); Assert.That(input.Editor.Text, Is.EqualTo("jugar "));
        input.Key(TerminalKey.Z, TerminalMods.Ctrl); Assert.That(input.Editor.Text, Is.EqualTo("jugar faro"));
        input.Key(TerminalKey.Left, TerminalMods.Ctrl); input.Key(TerminalKey.K, TerminalMods.Ctrl); Assert.That(input.Editor.Text, Is.EqualTo("jugar "));
        input.Key(TerminalKey.U, TerminalMods.Ctrl); Assert.That(input.Editor.Text, Is.Empty);
        Type(input, "x"); input.Key(TerminalKey.L, TerminalMods.Ctrl); Assert.That(input.ClearViewRequested, Is.True); Assert.That(input.Editor.Text, Is.EqualTo("x"), "Clearing the view keeps the line.");
        input.Key(TerminalKey.PageUp); Assert.That(input.PendingScroll, Is.EqualTo(TerminalScroll.PageUp));
        input.Key(TerminalKey.Home, TerminalMods.Ctrl); Assert.That(input.PendingScroll, Is.EqualTo(TerminalScroll.Top));
        Type(input, "y"); Assert.That(input.FollowPrompt, Is.True, "Typing returns to the prompt.");
    }

    [Test]
    public void EscapeCancelsInOrderSelectionThenLine()
    {
        var input = Input(out var terminal);
        terminal.Write("Faro"); Type(input, "hola");
        input.Selection.SelectLine(new(terminal.Output.NextLine - 1, 0));
        input.Key(TerminalKey.Escape); Assert.That(input.Selection.Active, Is.False); Assert.That(input.Editor.Text, Is.EqualTo("hola"));
        input.Key(TerminalKey.Escape); Assert.That(input.Editor.Text, Is.Empty);
        input.Key(TerminalKey.Z, TerminalMods.Ctrl); Assert.That(input.Editor.Text, Is.EqualTo("hola"), "Even that can be undone.");
    }
}

public sealed class AppTaskTests
{
    [Test]
    public void AFailedRetryRemainsAvailableInsteadOfLosingTheContinuation()
    {
        int attempts = 0; var center = new AppTaskCenter();
        var task = center.Run("propia", "x", (_, _) => throw new IOException("prueba"), () => { if (++attempts == 1) throw new IOException("reintento"); });
        Assert.That(SpinWait.SpinUntil(() => !task.Active, TimeSpan.FromSeconds(5)), Is.True);
        Assert.Throws<IOException>(() => task.Resume()); Assert.That(task.CanResume, Is.True);
        Assert.That(task.Resume(), Is.True); Assert.That(task.CanResume, Is.False); Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void RetryCannotDuplicateARunningTaskAndAFinishedRetryIsConsumedOnce()
    {
        var center = new AppTaskCenter(); int resumed = 0;
        var task = center.Run("propia", "x", async (_, token) => await Task.Delay(Timeout.Infinite, token), () => resumed++);
        try
        {
            Assert.That(task.CanResume, Is.False);
            Assert.That(task.Resume(), Is.False);
            Assert.That(resumed, Is.Zero);
        }
        finally { task.Cancel(); SpinWait.SpinUntil(() => !task.Active, TimeSpan.FromSeconds(5)); }
        Assert.That(task.Resume(), Is.True); Assert.That(resumed, Is.EqualTo(1));
        Assert.That(task.CanResume, Is.False); Assert.That(task.Resume(), Is.False);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Test]
    public async Task BackgroundWorkEndsWithHonestStatesOnTheInterfaceThread()
    {
        var center = new AppTaskCenter(); var ended = new List<AppTask>();
        center.TaskFinished += ended.Add;
        var good = center.Run("prueba", "a", (task, _) => { task.Report("etapa", done: 1, total: 2, unit: "pasos"); task.Result("listo"); return Task.CompletedTask; });
        var partial = center.Run("prueba", "b", (task, _) => { task.Error("sin datos"); task.Partial = true; return Task.CompletedTask; });
        var bad = center.Run("prueba", "c", (_, _) => throw new IOException("disco"));
        Assert.That(SpinWait.SpinUntil(() => center.Active.Count == 0, TimeSpan.FromSeconds(5)), Is.True);
        await Task.Delay(50);
        Assert.That(ended, Is.Empty, "Endings are delivered by Poll, on the interface thread.");
        center.Poll();
        Assert.That(ended, Has.Count.EqualTo(3));
        Assert.That(good.State, Is.EqualTo(AppTaskState.Completed)); Assert.That(good.Results, Has.Member("listo"));
        Assert.That(AppTaskCenter.Progress(good), Is.EqualTo("1 de 2 pasos"));
        Assert.That(partial.State, Is.EqualTo(AppTaskState.PartlyCompleted));
        Assert.That(bad.State, Is.EqualTo(AppTaskState.Failed)); Assert.That(bad.Errors.Single(), Is.EqualTo("disco"));
    }

    [Test]
    public void CancellingReachesTheWorkAndKeepsItsResults()
    {
        var center = new AppTaskCenter(); int resumed = 0;
        var task = center.Run("larga", "x", async (task, token) => { task.Result("parcial"); await Task.Delay(Timeout.Infinite, token); }, resume: () => resumed++);
        Assert.That(SpinWait.SpinUntil(() => task.Results.Count > 0, TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(task.CanCancel, Is.True); Assert.That(task.Cancel(), Is.True); Assert.That(task.Cancel(), Is.False, "A second request is not a second cancellation.");
        Assert.That(SpinWait.SpinUntil(() => !task.Active, TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(task.State, Is.EqualTo(AppTaskState.Cancelled)); Assert.That(task.Results, Has.Member("parcial"));
        Assert.That(task.CanResume, Is.True); Assert.That(task.Resume(), Is.True); Assert.That(resumed, Is.EqualTo(1));
    }

    [Test]
    public void NoProgressIsInventedWithoutACount()
    {
        var center = new AppTaskCenter();
        var task = center.Run("x", "y", (task, _) => { task.Report("sin conteo"); return Task.Delay(Timeout.Infinite, _); });
        SpinWait.SpinUntil(() => task.Stage.Length > 0, TimeSpan.FromSeconds(5));
        Assert.That(task.Fraction, Is.Null); Assert.That(AppTaskCenter.Progress(task), Is.Empty);
        task.Report("con conteo", done: 3); Assert.That(task.Fraction, Is.Null, "A count without a total is not a percentage."); Assert.That(AppTaskCenter.Progress(task), Is.EqualTo("3"));
        task.Cancel();
    }

    [Test]
    public void QuietTasksAreDetectedExplainedAndWaitingForTheUserDoesNotCount()
    {
        var clock = new Clock(); var center = new AppTaskCenter(clock) { LocalQuietLimit = TimeSpan.FromSeconds(10), ProviderQuietLimit = TimeSpan.FromMinutes(2) };
        var report = new WorkReport("construcción", "x", "esperando", AppTaskState.WaitingProvider, Cancel: () => { });
        center.Track("estudio", report);
        var task = center.Active.Single();
        clock.Now = clock.Now.AddMinutes(1); center.Track("estudio", report);
        Assert.That(center.IsQuiet(task), Is.False, "A provider may take longer than local work.");
        clock.Now = clock.Now.AddMinutes(2); center.Track("estudio", report);
        Assert.That(center.IsQuiet(task), Is.True, "Repeating the same report is not activity.");
        Assert.That(center.Describe(task), Does.Contain("sin actividad")); Assert.That(center.Advice(task), Does.Contain("proveedor").And.Contain("cancelar"));
        center.Track("estudio", report with { Stage = "otra" });
        Assert.That(center.IsQuiet(task), Is.False);
        clock.Now = clock.Now.AddHours(1); center.Track("estudio", report with { Stage = "otra", State = AppTaskState.WaitingUser });
        Assert.That(center.IsQuiet(task), Is.False, "Nobody is late while the program waits for a person.");
    }

    [Test]
    public void TrackedJobsEndWhenTheReportDisappearsAndANewJobIsANewTask()
    {
        var center = new AppTaskCenter(); var ended = new List<AppTask>(); center.TaskFinished += ended.Add;
        center.Track("x", new("uno", "a", "e"));
        var first = center.Active.Single();
        center.Track("x", new("dos", "a", "e"));
        Assert.That(first.Active, Is.False); Assert.That(center.Active.Single().Kind, Is.EqualTo("dos"));
        center.Track("x", null); center.Track("y", null);
        center.Poll();
        Assert.That(center.Active, Is.Empty); Assert.That(ended, Has.Count.EqualTo(2));
    }

    [Test]
    public void FinishedTasksDoNotGrowWithoutLimit()
    {
        var center = new AppTaskCenter();
        for (int i = 0; i < 300; i++) { center.Track("x", new("k", "t" + i, "e")); center.Track("x", null); }
        Assert.That(center.All.Count, Is.LessThanOrEqualTo(65));
    }
}

public sealed class ConsoleKeyboardHoldTests
{
    [Test]
    public void TheEnterThatRanAnOrderDoesNotReachTheMachine()
    {
        var input = new InputMapper(); bool enterDown = true;
        bool Down(Raylib_cs.KeyboardKey key) => key == Raylib_cs.KeyboardKey.Enter && enterDown;
        Assert.That(input.KeyboardReady(Down), Is.True);
        input.HoldKeyboardUntilReleased(Raylib_cs.KeyboardKey.Enter, Raylib_cs.KeyboardKey.KpEnter);
        Assert.That(input.KeyboardReady(Down), Is.False, "Enter is still held.");
        enterDown = false;
        Assert.That(input.KeyboardReady(Down), Is.True);
    }

    [TestCase(Raylib_cs.KeyboardKey.T)]
    [TestCase(Raylib_cs.KeyboardKey.L)]
    public void ConsoleChordIsAnEmulatorShortcut(Raylib_cs.KeyboardKey key)
    {
        bool Down(Raylib_cs.KeyboardKey held) => held == Raylib_cs.KeyboardKey.LeftControl || held == Raylib_cs.KeyboardKey.LeftShift || held == key;
        Assert.That(InputMapper.ToolChordHeld(Down), Is.True);
    }
}
