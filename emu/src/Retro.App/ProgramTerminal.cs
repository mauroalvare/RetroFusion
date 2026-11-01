using System.Text;

namespace Retro.App;

public sealed record TerminalCommand(string Name, string Usage, string Description, Func<IReadOnlyList<string>, IEnumerable<string>> Run);

/// <summary>A literal in-app command interpreter. It has no host shell or process execution facility.</summary>
public sealed class ProgramTerminal
{
    private readonly Func<IReadOnlyList<TerminalCommand>> _commands;
    private readonly Func<IEnumerable<string>> _extraCompletions;
    private readonly List<string> _lines = [];
    private readonly List<string> _history = [];
    private int _historyIndex;
    private string _draft = "";
    public string Location { get; set; } = "biblioteca";
    public string Prompt => "retro:/" + Location + ">";
    public IReadOnlyList<string> Lines => _lines.AsReadOnly();
    public IReadOnlyList<string> History => _history.AsReadOnly();
    public int Revision { get; private set; }

    public ProgramTerminal(Func<IReadOnlyList<TerminalCommand>> commands, Func<IEnumerable<string>>? completions = null)
    {
        _commands = commands; _extraCompletions = completions ?? (() => []);
        Write("RETROEMU / SISTEMA DE OPERACIÓN DEL TALLER");
        Write("Consola interna · no ejecuta órdenes de Windows, Linux ni macOS.");
        Write("Escribí ayuda. Ejemplo: juegos → jugar 1. Tab completa; ↑/↓ recorren el historial.");
        Write("Para cambiar de panel: interfaz retro. Durante una partida: Ctrl+Shift+T vuelve a la consola.");
    }

    public static bool TryParse(string line, out IReadOnlyList<string> arguments, out string? error)
    {
        var words = new List<string>(); var word = new StringBuilder();
        arguments = words; error = null;
        if (line.Length > 2048 || line.Any(character => char.IsControl(character) && character != '\t'))
        { error = "La orden es demasiado larga o contiene caracteres de control."; return false; }
        char quote = '\0'; bool started = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else if (c == '\\' && i + 1 < line.Length && line[i + 1] == quote) word.Append(line[++i]);
                else word.Append(c);
            }
            else if (c is '"' or '\'') { quote = c; started = true; }
            else if (char.IsWhiteSpace(c))
            {
                if (started) { words.Add(word.ToString()); word.Clear(); started = false; }
            }
            else { word.Append(c); started = true; }
        }
        if (quote != '\0') { error = "Falta cerrar una comilla. Usá comillas para nombres con espacios."; return false; }
        if (started) words.Add(word.ToString());
        return true;
    }

    public void Submit(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Write(Prompt + " " + line[..Math.Min(line.Length, 2048)]);
        if (line.Length <= 2048 && (_history.Count == 0 || _history[^1] != line))
        { _history.Add(line); if (_history.Count > 100) _history.RemoveAt(0); }
        _historyIndex = _history.Count; _draft = "";
        if (!TryParse(line, out var words, out string? error)) { Write(error!); return; }
        if (words.Count == 0) return;
        string command = words[0].ToLowerInvariant();
        if (command is "limpiar" or "clear") { _lines.Clear(); Revision++; return; }
        if (command is "ayuda" or "help" or "?")
        {
            string filter = string.Join(' ', words.Skip(1));
            var matches = _commands().Where(item => filter.Length == 0 || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var item in matches) { Write(item.Usage); Write("  " + item.Description); }
            Write("ayuda [orden] · limpiar · historial. Las acciones nuevas aparecen en acciones; los ajustes, en opciones."); return;
        }
        if (command == "historial") { foreach (string entry in _history) Write(entry); return; }
        var selected = _commands().FirstOrDefault(item => item.Name.Equals(command, StringComparison.OrdinalIgnoreCase));
        if (selected == null) { Write("Orden desconocida: " + command + ". Escribí ayuda o acciones."); return; }
        try { foreach (string output in selected.Run(words.Skip(1).ToArray()).Take(240)) Write(output); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        { Write("No se pudo completar: " + failure.Message); }
    }

    public void Write(string text)
    {
        foreach (string line in text.Replace('\r', ' ').Split('\n')) _lines.Add(line[..Math.Min(line.Length, 1000)]);
        if (_lines.Count > 600) _lines.RemoveRange(0, _lines.Count - 600);
        Revision++;
    }

    public string Recall(int direction, string current)
    {
        if (_historyIndex == _history.Count) _draft = current;
        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count);
        return _historyIndex == _history.Count ? _draft : _history[_historyIndex];
    }

    public string Complete(string input)
    {
        if (!TryParse(input, out var args, out _) || args.Count > 2) return input;
        string[] choices;
        int at = input.LastIndexOf(' ') + 1;
        string prefix = input[at..];
        if (at == 0) choices = _commands().Select(command => command.Name).Concat(["ayuda", "limpiar", "historial"]).Distinct().ToArray();
        else choices = _extraCompletions().ToArray();
        var matches = choices.Where(choice => choice.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        if (matches.Length == 1) return input[..at] + matches[0] + " ";
        if (matches.Length > 1) Write(string.Join("  ", matches));
        return input;
    }
}
