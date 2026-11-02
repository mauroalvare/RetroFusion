using System.Globalization;
using System.Text;

namespace Retro.App;

/// <summary>One word of an order and where it sits in the line. An apostrophe inside a word is a letter: Dragon's stays one word.</summary>
public readonly record struct CommandToken(string Text, int Start, int End, bool Quoted);

/// <summary>Splits an order into literal words. Nothing here reaches an operating system shell.</summary>
public static class CommandLine
{
    public const int MaxLength = 8192;

    public static bool TryParse(string line, out IReadOnlyList<CommandToken> tokens, out string? error)
    {
        tokens = Tokenize(line, out bool openQuote);
        error = null;
        if (line.Length > MaxLength || line.Any(c => char.IsControl(c) && c != '\t')) { error = "La orden es demasiado larga o contiene caracteres de control."; return false; }
        if (openQuote) { error = "Falta cerrar una comilla. Usá comillas para nombres con espacios."; return false; }
        return true;
    }

    /// <summary>Also accepts an unfinished line, for the autocompletion of what is being typed.</summary>
    public static IReadOnlyList<CommandToken> Tokenize(string line, out bool openQuote)
    {
        var tokens = new List<CommandToken>();
        var word = new StringBuilder();
        char quote = '\0'; bool started = false, quoted = false; int start = 0;
        void Finish(int end)
        {
            tokens.Add(new(word.ToString(), start, end, quoted));
            word.Clear(); started = quoted = false;
        }
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quote != '\0')
            {
                // \" is a literal quote only while another one is still needed to close: "C:\Juegos\" closes, so folders need no doubled bars.
                if (c == '\\' && i + 1 < line.Length && line[i + 1] == quote && LiteralQuote(line, i + 2, quote)) word.Append(line[++i]);
                else if (c == quote) quote = '\0';
                else word.Append(c);
            }
            else if (c is '"' or '\'' && (!started || quoted || word.Length == 0))
            {
                if (!started) start = i;
                quote = c; started = quoted = true;
            }
            else if (char.IsWhiteSpace(c)) { if (started) Finish(i); }
            else
            {
                if (!started) start = i;
                word.Append(c); started = true;
            }
        }
        openQuote = quote != '\0';
        if (started) Finish(line.Length);
        return tokens;
    }

    /// <summary>Before a space the quote closes unless an odd number follows; inside a word it needs one later to close.</summary>
    private static bool LiteralQuote(string line, int from, char quote)
    {
        if (from >= line.Length) return false;
        int later = 0;
        for (int i = from; i < line.Length; i++) if (line[i] == quote) later++;
        return char.IsWhiteSpace(line[from]) ? later % 2 == 1 : later > 0;
    }

    /// <summary>Quotes a value that needs it, so a completion stays one word.</summary>
    public static string Quote(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'')) return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    /// <summary>Compares names ignoring case and accents: opción, opcion and OPCION are the same word.</summary>
    public static string Fold(string value)
    {
        var folded = new StringBuilder(value.Length);
        foreach (char c in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) folded.Append(char.ToLowerInvariant(c));
        return folded.ToString().Replace('ñ', 'n');
    }

    /// <summary>Edit distance over folded names, to suggest the order that was probably meant.</summary>
    public static int Distance(string left, string right)
    {
        left = Fold(left); right = Fold(right);
        var previous = new int[right.Length + 1]; var current = new int[right.Length + 1];
        for (int j = 0; j <= right.Length; j++) previous[j] = j;
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}
