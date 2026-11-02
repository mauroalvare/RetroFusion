using System.Globalization;

namespace Retro.App;

/// <summary>Why an order could not run. Each one leads the user to a different next step.</summary>
public enum CommandProblem { UnknownOrder, InvalidArgument, Ambiguous, WrongContext, NotImplemented, MissingResource, Failed }

/// <summary>An expected failure of an order, with concrete things to try instead.</summary>
public sealed class CommandFailure(CommandProblem problem, string message, params string[] tryInstead) : Exception(message)
{
    public CommandProblem Problem { get; } = problem;
    public IReadOnlyList<string> TryInstead { get; } = tryInstead;
}

public enum ArgumentKind { Word, Text, Number, Hex, Choice, Toggle, Game, Path, Reference }

/// <summary>A value offered by autocompletion, with what it means.</summary>
public sealed record CompletionItem(string Value, string Detail = "");

/// <summary>An argument of an order. Syntax, validation, help and completion all come from this definition.</summary>
public sealed record CommandArgument(string Name, ArgumentKind Kind = ArgumentKind.Word, string Description = "")
{
    public bool Optional { get; init; }
    /// <summary>Takes the rest of the line, so a title can be written without quotes.</summary>
    public bool Rest { get; init; }
    public IReadOnlyList<string> Choices { get; init; } = [];
    public int Min { get; init; } = int.MinValue;
    public int Max { get; init; } = int.MaxValue;
    /// <summary>Candidates for this position, given the words already written after the order's name.</summary>
    public Func<IReadOnlyList<string>, IEnumerable<CompletionItem>>? Complete { get; init; }

    public string Usage
    {
        get
        {
            string inner = Kind switch
            {
                ArgumentKind.Choice when Choices.Count is > 0 and <= 6 => string.Join('|', Choices),
                ArgumentKind.Toggle => "on|off",
                _ => Name
            } + (Rest ? "…" : "");
            return Optional ? "[" + inner + "]" : "<" + inner + ">";
        }
    }

    public string? Validate(string value) => Kind switch
    {
        ArgumentKind.Number when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < Min || number > Max
            => Min != int.MinValue && Max != int.MaxValue ? $"{Name}: usá un número de {Min} a {Max}." : $"{Name}: usá un número.",
        ArgumentKind.Hex when !uint.TryParse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value.TrimStart('$'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            => $"{Name}: usá una dirección hexadecimal, por ejemplo 8000.",
        ArgumentKind.Toggle when CommandArguments.Toggle(value) == null => $"{Name}: usá on u off.",
        ArgumentKind.Choice when !Choices.Any(choice => CommandLine.Fold(choice) == CommandLine.Fold(value)) => $"{Name}: valores admitidos: {string.Join(", ", Choices)}.",
        _ => null
    };
}

public static class CommandArguments
{
    public static bool? Toggle(string value) => CommandLine.Fold(value) switch
    {
        "on" or "si" or "true" or "1" or "activar" => true,
        "off" or "no" or "false" or "0" or "desactivar" => false,
        _ => null
    };
}

/// <summary>
/// The definition of one order: everything the console needs to parse it, explain it and complete it.
/// Identifiers are stable; visible names and aliases can grow without breaking scripts or tests.
/// </summary>
public sealed class CommandSpec
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Group { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public IReadOnlyList<CommandArgument> Arguments { get; init; } = [];
    public IReadOnlyList<string> Examples { get; init; } = [];
    /// <summary>Words people may search for that do not appear in the name or summary.</summary>
    public string Keywords { get; init; } = "";
    /// <summary>What a successful run leaves behind.</summary>
    public string Result { get; init; } = "";
    public IReadOnlyList<string> Errors { get; init; } = [];
    /// <summary>What stops it once started; empty for instant orders.</summary>
    public string Cancellation { get; init; } = "";
    /// <summary>What must be true to run it, for the help.</summary>
    public string Requires { get; init; } = "";
    /// <summary>Why it cannot run now, with the way to change that. Checked when it runs.</summary>
    public Func<CommandFailure?>? Unavailable { get; init; }
    /// <summary>An operation without a command route yet; it explains the alternative instead of running.</summary>
    public string? Planned { get; init; }
    public Action<CommandContext>? Run { get; init; }

    public string Usage => string.Join(' ', Arguments.Select(argument => argument.Usage).Prepend(Name));
    public bool Answers(string word)
    {
        string folded = CommandLine.Fold(word);
        return CommandLine.Fold(Name) == folded || Aliases.Any(alias => CommandLine.Fold(alias) == folded);
    }
}

/// <summary>A column of a text table. Headers are never cut: a column that does not fit is dropped or the flexible one shrinks.</summary>
public sealed record TableColumn(string Title, bool Flexible = false, bool Right = false, bool Optional = false);

/// <summary>One running order: its validated words and where it writes.</summary>
public sealed class CommandContext(ProgramTerminal terminal, CommandSpec spec, IReadOnlyDictionary<string, string> values, IReadOnlyList<string> words)
{
    public ProgramTerminal Terminal { get; } = terminal;
    public CommandSpec Spec { get; } = spec;
    public IReadOnlyList<string> Words { get; } = words;
    public bool Has(string name) => values.ContainsKey(name);
    public string Text(string name) => values[name];
    public string? Optional(string name) => values.GetValueOrDefault(name);
    public int Number(string name) => int.Parse(values[name], NumberStyles.Integer, CultureInfo.InvariantCulture);
    public int Number(string name, int fallback) => values.ContainsKey(name) ? Number(name) : fallback;
    public bool Toggle(string name) => CommandArguments.Toggle(values[name]) ?? throw new CommandFailure(CommandProblem.InvalidArgument, "Usá on u off.");
    public uint Hex(string name)
    {
        string text = values[name];
        return uint.Parse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text.TrimStart('$'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public void Write(string text, TerminalKind kind = TerminalKind.Output) => Terminal.Write(text, kind);
    public void Heading(string text) => Write(text, TerminalKind.Heading);
    public void Detail(string text) => Write(text, TerminalKind.Detail);
    public void Success(string text) => Write(text, TerminalKind.Success);
    public void Warning(string text) => Write("aviso: " + text, TerminalKind.Warning);
    public void Table(IReadOnlyList<TableColumn> columns, IEnumerable<IReadOnlyList<string>> rows)
    {
        foreach (var (text, kind) in TextTable.Layout(columns, rows.ToList(), Terminal.Output.Columns)) Write(text, kind);
    }
}

public static class TextTable
{
    private const int Gap = 2;

    /// <summary>Aligned rows for a width in cells, with the widest possible headers kept whole.</summary>
    public static IEnumerable<(string Text, TerminalKind Kind)> Layout(IReadOnlyList<TableColumn> columns, IReadOnlyList<IReadOnlyList<string>> rows, int width)
    {
        var shown = Enumerable.Range(0, columns.Count).ToList();
        int[] Widths() => shown.Select(column => Math.Max(columns[column].Title.Length, rows.Select(row => column < row.Count ? row[column].Length : 0).DefaultIfEmpty(0).Max())).ToArray();
        var widths = Widths();
        int Total() => widths.Sum() + Gap * Math.Max(0, shown.Count - 1);
        // Measured from the column's own text, so that shrinking one column does not change what the next can give up.
        int Floor(int index) => Math.Max(columns[shown[index]].Title.Length, Math.Min(widths[index], 8));
        int MinimumOf(int index) => columns[shown[index]].Flexible ? Floor(index) : widths[index];
        int MinimumTotal() => Enumerable.Range(0, shown.Count).Sum(MinimumOf) + Gap * Math.Max(0, shown.Count - 1);
        // The last optional column goes first: a table that fits beats one that wraps every row.
        while (MinimumTotal() > width && shown.FindLastIndex(column => columns[column].Optional) is >= 0 and var drop) { shown.RemoveAt(drop); widths = Widths(); }
        for (int i = 0; i < shown.Count && Total() > width; i++)
            if (columns[shown[i]].Flexible) widths[i] = Math.Max(Floor(i), widths[i] - (Total() - width));
        string Row(Func<int, string> cell)
        {
            var parts = new List<string>();
            for (int i = 0; i < shown.Count; i++)
            {
                string text = cell(shown[i]);
                if (text.Length > widths[i]) text = text[..Math.Max(0, widths[i] - 1)] + "…";
                bool last = i == shown.Count - 1;
                parts.Add(columns[shown[i]].Right ? text.PadLeft(widths[i]) : last ? text : text.PadRight(widths[i]));
            }
            return string.Join(new string(' ', Gap), parts);
        }
        yield return (Row(column => columns[column].Title), TerminalKind.Heading);
        yield return (string.Join(new string(' ', Gap), widths.Select(size => new string('─', size))), TerminalKind.Rule);
        foreach (var row in rows) yield return (Row(column => column < row.Count ? row[column] : ""), TerminalKind.Output);
    }
}
