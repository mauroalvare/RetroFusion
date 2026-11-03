namespace Retro.App;

/// <summary>Candidates for the word under the cursor. Start..End is the part of the line they replace.</summary>
public sealed record CompletionSet(int Start, int End, IReadOnlyList<CompletionItem> Items, string Prefix);

/// <summary>A literal in-app command interpreter. It has no host shell or process execution facility.</summary>
public sealed class ProgramTerminal
{
    // Names that would run something in a system shell: they get a clear answer instead of a spelling suggestion.
    private static readonly string[] HostShellWords = ["powershell", "pwsh", "cmd", "bash", "sh", "wsl", "del", "rm", "rmdir", "format", "start", "python", "curl", "wget", "git"];
    private readonly Func<IReadOnlyList<CommandSpec>> _commands;
    private readonly List<CommandSpec> _builtIn;
    public TerminalBuffer Output { get; }
    public CommandHistory History { get; }
    public string Location { get; set; } = "biblioteca";
    /// <summary>Extra state shown in the prompt, such as the loaded machine; empty when there is nothing to say.</summary>
    public Func<string>? PromptDetail { get; set; }
    internal string PromptForColumns(int columns) => columns < 48 ? "/" + TerminalLanguage.Choice(Location) + ">" : Prompt;
    /// <summary>More places where a capability can be discovered, such as options and actions.</summary>
    public Func<string, IEnumerable<(string Order, string Description)>>? Capabilities { get; set; }
    public string Prompt
    {
        get
        {
            string detail = PromptDetail?.Invoke() ?? "";
            return "retro:/" + TerminalLanguage.Choice(Location) + (detail.Length == 0 ? "" : " " + detail) + ">";
        }
    }

    public ProgramTerminal(Func<IReadOnlyList<CommandSpec>> commands, TerminalBuffer? output = null, CommandHistory? history = null)
    {
        _commands = commands; Output = output ?? new(); History = history ?? new();
        _builtIn = BuildBuiltIns();
        Write("RETROEMU · consola interna del taller", TerminalKind.Heading);
        Write("Procesa órdenes de RetroEmu; no ejecuta programas de Windows, Linux ni macOS.", TerminalKind.Detail);
        Write("Empezá con: juegos · ayuda · como guardar · Tab completa · ↑/↓ historial · Ctrl+R busca.", TerminalKind.Detail);
    }

    /// <summary>The whole catalog as it is right now: orders added later appear without any change here.</summary>
    public IReadOnlyList<CommandSpec> Commands => _commands().Concat(_builtIn).ToList();

    public static bool TryParse(string line, out IReadOnlyList<string> arguments, out string? error)
    {
        bool valid = CommandLine.TryParse(line, out var tokens, out error);
        arguments = tokens.Select(token => token.Text).ToArray();
        return valid;
    }

    public void Write(string text, TerminalKind kind = TerminalKind.Output) => Output.Append(kind == TerminalKind.Command ? text : UiText.Get(text), kind);

    /// <summary>Runs one line. False means nothing happened or the order failed, which also stops a pasted sequence.</summary>
    public bool Submit(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        Output.BeginBlock();
        string shown = line.Length <= TerminalBuffer.MaxLineLength ? line : line[..TerminalBuffer.MaxLineLength];
        Write(Prompt + " " + shown, TerminalKind.Command);
        History.Add(line);
        if (!CommandLine.TryParse(line, out var tokens, out string? error)) { Write(UiText.Get("error: ") + error, TerminalKind.Error); return false; }
        if (tokens.Count == 0) return false;
        var words = tokens.Select(token => token.Text).ToArray();
        try
        {
            var spec = Find(words[0]) ?? throw Unknown(words[0]);
            if (spec.Planned != null) throw new CommandFailure(CommandProblem.NotImplemented, spec.Name + UiText.Get(" todavía no tiene recorrido por órdenes."), spec.Planned);
            if (spec.Unavailable?.Invoke() is { } unavailable) throw unavailable;
            var values = Bind(spec, words.Skip(1).ToArray());
            spec.Run!(new CommandContext(this, spec, values, words.Skip(1).ToArray()));
            return true;
        }
        catch (CommandFailure failure) { Report(failure); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException)
        {
            Report(new CommandFailure(CommandProblem.Failed, failure.Message, "El programa sigue funcionando. estado muestra la situación actual."));
        }
        return false;
    }

    private void Report(CommandFailure failure)
    {
        string label = failure.Problem switch
        {
            CommandProblem.UnknownOrder => "orden desconocida",
            CommandProblem.InvalidArgument => "argumento inválido",
            CommandProblem.Ambiguous => "selección ambigua",
            CommandProblem.WrongContext => "no disponible ahora",
            CommandProblem.NotImplemented => "todavía no implementado",
            CommandProblem.MissingResource => "falta un recurso",
            _ => "error"
        };
        Write(UiText.Get(label) + ": " + UiText.Message(failure.Message), TerminalKind.Error);
        foreach (string suggestion in failure.TryInstead) Write("  " + UiText.Message(suggestion), TerminalKind.Detail);
    }

    private CommandSpec? Find(string word) => Commands.FirstOrDefault(command => command.Answers(word));

    private CommandFailure Unknown(string word)
    {
        string folded = CommandLine.Fold(word);
        if (HostShellWords.Contains(folded))
            return new(CommandProblem.UnknownOrder, word + UiText.Get(" es un programa del sistema. Esta consola solo procesa órdenes de RetroEmu."), "ayuda muestra todo lo que puede hacer.");
        var near = Commands.SelectMany(command => command.Aliases.Prepend(command.Name).Select(name => (command.Name, Distance: CommandLine.Distance(word, name))))
            .Where(item => item.Distance <= Math.Max(2, word.Length / 3)).OrderBy(item => item.Distance).ThenBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => item.Name).Distinct().Take(3).ToArray();
        return new(CommandProblem.UnknownOrder, word + ".",
            near.Length > 0 ? UiText.Get("¿Quisiste decir ") + string.Join(", ", near) + "?" : UiText.Get("como ") + word + UiText.Get(" busca funciones por tema."), "ayuda lista todas las órdenes.");
    }

    /// <summary>Validates the words against the definition, so a handler only sees well-formed values.</summary>
    private static Dictionary<string, string> Bind(CommandSpec spec, IReadOnlyList<string> words)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        CommandFailure Misuse(string message) => new(CommandProblem.InvalidArgument, message, [UiText.Get("Uso: ") + spec.Usage, .. spec.Examples.Take(2).Select(example => UiText.Get("Ejemplo: ") + example)]);
        int word = 0;
        foreach (var argument in spec.Arguments)
        {
            if (word >= words.Count)
            {
                if (!argument.Optional) throw Misuse(UiText.Get("Falta ") + argument.Name + ".");
                continue;
            }
            string value = argument.Rest ? string.Join(' ', words.Skip(word)) : words[word];
            if (argument.Kind == ArgumentKind.Choice) value = TerminalLanguage.CanonicalChoice(value, argument.Choices);
            word = argument.Rest ? words.Count : word + 1;
            if (argument.Validate(value) is { } problem) throw Misuse(problem);
            values[argument.Name] = value;
        }
        if (word < words.Count) throw Misuse(UiText.Get("Sobran datos: ") + string.Join(' ', words.Skip(word)) + ".");
        return values;
    }

    /// <summary>What can replace the word at the cursor, using the definition of the order being written.</summary>
    public CompletionSet Complete(string line, int cursor)
    {
        cursor = Math.Clamp(cursor, 0, line.Length);
        var tokens = CommandLine.Tokenize(line[..cursor], out bool openQuote);
        bool inside = tokens.Count > 0 && (openQuote || tokens[^1].End == cursor && !char.IsWhiteSpace(line[cursor - 1]));
        int position = inside ? tokens.Count - 1 : tokens.Count;
        string prefix = inside ? tokens[^1].Text : "";
        int start = inside ? tokens[^1].Start : cursor;
        // The rest of a word to the right of the cursor is replaced too.
        int end = cursor;
        while (end < line.Length && !char.IsWhiteSpace(line[end])) end++;
        IEnumerable<CompletionItem> candidates;
        if (position == 0) candidates = Commands.SelectMany(command => (UiText.English ? [command.Name] : command.Aliases.Prepend(command.Name)).Select(name => new CompletionItem(name, UiText.Get(command.Summary))));
        else
        {
            var spec = Find(tokens[0].Text);
            var earlier = tokens.Skip(1).Take(position - 1).Select(token => token.Text).ToArray();
            if (spec != null)
                for (int i = 0; i < earlier.Length && i < spec.Arguments.Count; i++)
                    if (spec.Arguments[i].Kind == ArgumentKind.Choice) earlier[i] = TerminalLanguage.CanonicalChoice(earlier[i], spec.Arguments[i].Choices);
                    else if (spec.Id == "system.option" && i == 0) earlier[i] = TerminalLanguage.CanonicalOption(earlier[i]);
            int index = spec == null ? -1 : Math.Min(position - 1, spec.Arguments.Count - 1);
            bool beyond = spec != null && position - 1 >= spec.Arguments.Count && !(spec.Arguments.Count > 0 && spec.Arguments[^1].Rest);
            candidates = index < 0 || beyond ? [] : Candidates(spec!.Arguments[index], earlier, prefix);
        }
        var all = candidates.DistinctBy(item => item.Value).ToArray();
        string folded = CommandLine.Fold(prefix);
        var matches = all.Where(item => CommandLine.Fold(item.Value).StartsWith(folded, StringComparison.Ordinal)).ToArray();
        // A title can be found by a word inside it.
        if (matches.Length == 0 && folded.Length > 0) matches = all.Where(item => CommandLine.Fold(item.Value).Contains(folded, StringComparison.Ordinal)).ToArray();
        return new(start, end, matches.OrderBy(item => item.Value, StringComparer.OrdinalIgnoreCase).Take(256).ToArray(), prefix);
    }

    private static IEnumerable<CompletionItem> Candidates(CommandArgument argument, IReadOnlyList<string> earlier, string prefix)
    {
        if (argument.Complete != null) return argument.Complete(earlier).Select(item => item with { Value = TerminalLanguage.Choice(item.Value), Detail = UiText.Get(item.Detail) });
        return argument.Kind switch
        {
            ArgumentKind.Toggle => [new("on", "activar"), new("off", "desactivar")],
            ArgumentKind.Choice => argument.Choices.Select(choice => new CompletionItem(TerminalLanguage.Choice(choice))),
            ArgumentKind.Path => PathCompletion(prefix),
            _ => []
        };
    }

    /// <summary>Reads directory names only, and never more than a screenful of them.</summary>
    internal static IEnumerable<CompletionItem> PathCompletion(string prefix)
    {
        if (prefix.Length == 0) return [];
        try
        {
            string? folder = prefix.EndsWith('\\') || prefix.EndsWith('/') ? prefix : Path.GetDirectoryName(prefix);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return [];
            string name = prefix[(prefix.LastIndexOfAny(['\\', '/']) + 1)..];
            return new DirectoryInfo(folder).EnumerateFileSystemInfos(name + "*").Take(64)
                .Select(item => new CompletionItem(item.FullName + (item is DirectoryInfo ? Path.DirectorySeparatorChar.ToString() : ""), item is DirectoryInfo ? "carpeta" : "archivo")).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return []; }
    }

    /// <summary>The syntax of the order being written, shown while typing.</summary>
    public string? HintFor(string line)
    {
        var tokens = CommandLine.Tokenize(line, out _);
        if (tokens.Count == 0 || Find(tokens[0].Text) is not { } spec) return null;
        return spec.Usage + " · " + spec.Summary;
    }

    private List<CommandSpec> BuildBuiltIns() =>
    [
        new()
        {
            Id = "console.help", Name = "ayuda", Group = "Consola", Summary = "Explica una orden, un grupo o lista todas.",
            Aliases = ["help", "?", "man"], Keywords = "manual guia sintaxis ordenes comandos",
            Arguments = [new("tema", ArgumentKind.Word, "Una orden o un grupo.") { Optional = true, Complete = _ => Commands.Select(command => new CompletionItem(command.Name, command.Summary)).Concat(Commands.Select(command => command.Group).Distinct().Select(group => new CompletionItem(group, "grupo"))) }],
            Examples = ["ayuda", "ayuda jugar", "ayuda Partida"], Result = "Texto generado desde las definiciones de las órdenes.",
            Run = context => Help(context, context.Optional("tema"))
        },
        new()
        {
            Id = "console.apropos", Name = "como", Group = "Consola", Summary = "Busca cómo hacer algo sin conocer el nombre de la orden.",
            Aliases = ["apropos"], Keywords = "encontrar descubrir funciones capacidades tema",
            Arguments = [new("tema", ArgumentKind.Text, "Lo que querés hacer.") { Rest = true }],
            Examples = ["como guardar", "como subir el volumen", "como cambiar tema"], Result = "Órdenes, acciones y ajustes que resuelven ese tema.",
            Run = context => Apropos(context, context.Text("tema"))
        },
        new()
        {
            Id = "console.clear", Name = "limpiar", Group = "Consola", Summary = "Borra la salida guardada de la consola.",
            Aliases = ["clear", "cls"], Keywords = "borrar pantalla", Result = "La salida se vacía; el historial de órdenes se conserva. Ctrl+L solo despeja la vista.",
            Run = _ => Output.Clear()
        },
        new()
        {
            Id = "console.history", Name = "historial", Group = "Consola", Summary = "Lista, busca o borra las órdenes anteriores.",
            Aliases = ["history"], Keywords = "ordenes anteriores repetir",
            Arguments = [new("texto", ArgumentKind.Text, "Filtra las órdenes, o escribí borrar para vaciar el historial.") { Optional = true, Rest = true }],
            Examples = ["historial", "historial jugar", "historial borrar"], Result = "Numera las órdenes guardadas, de la más antigua a la más nueva.",
            Run = context =>
            {
                string filter = context.Optional("texto") ?? "";
                if (filter is "borrar" or "clear") { History.Clear(); context.Success("Historial de órdenes borrado."); return; }
                var entries = History.Entries.Select((entry, index) => (entry, index)).Where(item => filter.Length == 0 || CommandLine.Fold(item.entry).Contains(CommandLine.Fold(filter))).ToArray();
                foreach (var (entry, index) in entries) context.Write(UiText.Format($"{index + 1,4}  {entry}"));
                if (entries.Length == 0) context.Detail("No hay órdenes que coincidan.");
            }
        }
    ];

    private void Help(CommandContext context, string? topic)
    {
        var all = Commands;
        if (topic == null)
        {
            foreach (var group in all.GroupBy(command => command.Group).OrderBy(group => GroupOrder(group.Key)))
            {
                context.Heading(group.Key);
                context.Write("  " + string.Join("  ", group.Select(command => command.Name)));
            }
            context.Detail("ayuda <orden> explica una orden. como <tema> busca por lo que querés hacer.");
            return;
        }
        var spec = all.FirstOrDefault(command => command.Answers(topic));
        if (spec == null)
        {
            var group = all.Where(command => CommandLine.Fold(command.Group) == CommandLine.Fold(topic) || CommandLine.Fold(UiText.Get(command.Group)) == CommandLine.Fold(topic)).ToArray();
            if (group.Length == 0)
            {
                var near = all.Where(command => CommandLine.Fold(command.Name).Contains(CommandLine.Fold(topic))).Take(8).ToArray();
                if (near.Length == 0) throw new CommandFailure(CommandProblem.UnknownOrder, UiText.Get("No hay ayuda para ") + topic + ".", UiText.Get("como ") + topic + UiText.Get(" busca por tema."));
                group = near;
            }
            context.Table([new("Orden"), new("Función", Flexible: true)], group.Select(command => new[] { command.Name, command.Summary }));
            return;
        }
        context.Heading(spec.Name + " · " + spec.Group);
        context.Write(spec.Summary);
        context.Write(UiText.Get("Uso: ") + spec.Usage);
        foreach (var argument in spec.Arguments.Where(argument => argument.Description.Length > 0 || argument.Choices.Count > 0))
            context.Detail(UiText.Format($"  {UiText.Get(argument.Name)}: {UiText.Get(argument.Description)}") + (argument.Choices.Count > 0 ? UiText.Get(" Valores: ") + string.Join(", ", argument.Choices.Select(TerminalLanguage.Choice)) + "." : "")
                + (argument.Kind == ArgumentKind.Number && argument.Min != int.MinValue ? UiText.Format($" De {argument.Min} a {argument.Max}.") : "") + (argument.Optional ? UiText.Get(" Opcional.") : ""));
        foreach (string example in spec.Examples) context.Write(UiText.Get("Ejemplo: ") + TerminalLanguage.Example(example));
        if (spec.Aliases.Count > 0) context.Detail(UiText.Get("También: ") + string.Join(", ", spec.Aliases));
        if (spec.Requires.Length > 0) context.Detail(UiText.Get("Requiere: ") + spec.Requires);
        if (spec.Unavailable?.Invoke() is { } blocked) context.Warning(UiText.Get("ahora no disponible: ") + blocked.Message);
        if (spec.Result.Length > 0) context.Detail(UiText.Get("Resultado: ") + spec.Result);
        foreach (string error in spec.Errors) context.Detail(UiText.Get("Puede fallar si ") + UiText.Get(error));
        if (spec.Cancellation.Length > 0) context.Detail(UiText.Get("Cancelar: ") + spec.Cancellation);
        if (spec.Planned != null) context.Warning(UiText.Get("todavía sin recorrido por órdenes. ") + spec.Planned);
    }

    private static int GroupOrder(string group) => Array.IndexOf<string>(["Biblioteca", "Partida", "Análisis", "Proyectos", "Tareas", "Sistema", "Consola"], group) is >= 0 and var order ? order : 99;

    private void Apropos(CommandContext context, string topic)
    {
        string[] words = CommandLine.Fold(topic).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length > 2 || word.All(char.IsDigit)).ToArray();
        if (words.Length == 0) words = [CommandLine.Fold(topic)];
        int Score(string text, string name)
        {
            string folded = CommandLine.Fold(text), title = CommandLine.Fold(name);
            return words.Count(folded.Contains) * 2 + words.Count(title.Contains) * 3;
        }
        var found = Commands.Select(command => (Score: Score(string.Join(' ', [command.Name, .. command.Aliases, command.Summary, command.Group, command.Keywords]), command.Name), Order: command.Usage, command.Summary))
            .Where(item => item.Score > 0).ToList();
        if (Capabilities != null)
            found.AddRange(Capabilities(topic).Select(item => (Score: Score(item.Order + " " + item.Description, item.Order), item.Order, Summary: item.Description)).Where(item => item.Score > 0));
        var best = found.OrderByDescending(item => item.Score).ThenBy(item => item.Order, StringComparer.Ordinal).DistinctBy(item => item.Order).Take(10).ToArray();
        if (best.Length == 0)
            throw new CommandFailure(CommandProblem.UnknownOrder, UiText.Get("No encontré funciones sobre ") + topic + ".", "Probá con otra palabra, o mirá ayuda, acciones y opciones.");
        context.Table([new("Orden"), new("Para qué sirve", Flexible: true)], best.Select(item => new[] { item.Order, item.Summary }));
    }
}
