using System.Text;

namespace Retro.App;

/// <summary>What a scrollback line is. Colors follow it, but every warning and error also says so in words.</summary>
public enum TerminalKind { Output, Command, Detail, Heading, Success, Warning, Error, Rule }

/// <summary>A place in the scrollback that survives trimming and rewrapping: a line number that never repeats, and a column in its text.</summary>
public readonly record struct TerminalPoint(long Line, int Column) : IComparable<TerminalPoint>
{
    public int CompareTo(TerminalPoint other) => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);
    public static bool operator <(TerminalPoint left, TerminalPoint right) => left.CompareTo(right) < 0;
    public static bool operator >(TerminalPoint left, TerminalPoint right) => left.CompareTo(right) > 0;
}

/// <summary>One visible row: a slice of a line, drawn from the Indent column.</summary>
public readonly record struct TerminalRow(long Line, int Start, int Length, int Indent, TerminalKind Kind);

/// <summary>
/// Bounded scrollback. Lines keep their logical text; soft wrapping only records where rows break, so
/// a resize rewraps without touching the text and copying never gains line breaks that were not there.
/// </summary>
public sealed class TerminalBuffer
{
    public const int MaxLineLength = 4096;
    private sealed class Entry(string text, TerminalKind kind, int block)
    {
        public readonly string Text = text;
        public readonly TerminalKind Kind = kind;
        public readonly int Block = block;
        public int[]? Breaks;
        public int Indent;
        public int Rows => (Breaks?.Length ?? 0) + 1;
    }

    private readonly List<Entry> _lines = [];
    private int[] _firstRow = new int[1024];
    private long _firstSequence;
    private int _block;
    public int MaxLines { get; }
    public long MaxCharacters { get; }
    public int Columns { get; private set; } = 80;
    public int RowCount { get; private set; }
    public int Count => _lines.Count;
    public long Characters { get; private set; }
    public long Revision { get; private set; }
    /// <summary>Lines dropped so far, so a user can be told that old output is gone.</summary>
    public long Dropped => _firstSequence;
    public long FirstLine => _firstSequence;
    public long NextLine => _firstSequence + _lines.Count;

    public TerminalBuffer(int maxLines = 20000, long maxCharacters = 4_000_000)
    {
        MaxLines = Math.Max(16, maxLines); MaxCharacters = Math.Max(1024, maxCharacters);
    }

    /// <summary>Groups the following lines as the output of one order.</summary>
    public void BeginBlock() => _block++;

    public void Append(string text, TerminalKind kind = TerminalKind.Output)
    {
        foreach (string line in Clean(text).Split('\n'))
        {
            string kept = line.Length <= MaxLineLength ? line : line[..MaxLineLength] + "…";
            var entry = new Entry(kept, kind, _block);
            Wrap(entry);
            if (_lines.Count == _firstRow.Length) Array.Resize(ref _firstRow, _firstRow.Length * 2);
            _firstRow[_lines.Count] = RowCount;
            _lines.Add(entry); RowCount += entry.Rows; Characters += kept.Length;
        }
        Trim(); Revision++;
    }

    public void Clear()
    {
        _firstSequence += _lines.Count; _lines.Clear(); RowCount = 0; Characters = 0; Revision++;
    }

    /// <summary>Control characters never reach the screen; accents compose so that one letter is one cell.</summary>
    internal static string Clean(string text)
    {
        var clean = new StringBuilder(text.Length);
        foreach (char c in text.Replace("\r\n", "\n"))
            clean.Append(c == '\n' ? c : c == '\t' ? ' ' : c == '\r' || char.IsControl(c) ? ' ' : c);
        string result = clean.ToString();
        try { return result.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return result; }   // an unpaired surrogate from a damaged name stays as it is
    }

    private void Trim()
    {
        if (_lines.Count <= MaxLines && Characters <= MaxCharacters) return;
        // Dropping a batch keeps the cost of the shift away from every appended line.
        int excess = Math.Max(_lines.Count - MaxLines, 0), batch = excess + MaxLines / 16;
        long remaining = Characters;
        int drop = 0;
        for (; drop < _lines.Count - 1 && (drop < batch || remaining > MaxCharacters); drop++) remaining -= _lines[drop].Text.Length;
        if (drop == 0) return;
        _lines.RemoveRange(0, drop); _firstSequence += drop; Characters = remaining;
        Reindex();
    }

    private void Reindex()
    {
        int row = 0;
        for (int i = 0; i < _lines.Count; i++) { _firstRow[i] = row; row += _lines[i].Rows; }
        RowCount = row;
    }

    public void SetColumns(int columns)
    {
        columns = Math.Max(8, columns);
        if (columns == Columns) return;
        Columns = columns;
        foreach (var entry in _lines) Wrap(entry);
        Reindex(); Revision++;
    }

    private void Wrap(Entry entry) => entry.Breaks = Breaks(entry.Text, Columns, out entry.Indent);

    /// <summary>Breaks at spaces, or inside a word that does not fit; continuation rows keep the first row's indentation.</summary>
    internal static int[]? Breaks(string text, int columns, out int indent)
    {
        indent = 0;
        if (text.Length <= columns) return null;
        int leading = 0;
        while (leading < text.Length && text[leading] == ' ') leading++;
        indent = leading < text.Length ? Math.Min(leading, columns / 3) : 0;
        var breaks = new List<int>();
        int start = 0, width = columns;
        while (text.Length - start > width)
        {
            int limit = start + width, space = text.LastIndexOf(' ', limit, width);
            // A long run of indentation or a space at the row's start is not a useful place to break.
            int cut = space > start + (start == 0 ? leading : 0) ? space + 1 : limit;
            if (cut < text.Length && char.IsLowSurrogate(text[cut]) && cut > start + 1) cut--;
            breaks.Add(cut); start = cut; width = columns - indent;
        }
        return breaks.ToArray();
    }

    private int LineAtRow(int row)
    {
        int low = 0, high = _lines.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (_firstRow[middle] <= row) low = middle; else high = middle - 1;
        }
        return low;
    }

    public TerminalRow Row(int row)
    {
        int index = LineAtRow(Math.Clamp(row, 0, Math.Max(0, RowCount - 1)));
        if (_lines.Count == 0) return default;
        var entry = _lines[index];
        int part = Math.Clamp(row - _firstRow[index], 0, entry.Rows - 1);
        int start = part == 0 ? 0 : entry.Breaks![part - 1];
        int end = part == entry.Rows - 1 ? entry.Text.Length : entry.Breaks![part];
        return new(_firstSequence + index, start, end - start, part == 0 ? 0 : entry.Indent, entry.Kind);
    }

    public string RowText(in TerminalRow row)
    {
        if (!TryEntry(row.Line, out var entry) || row.Start + row.Length > entry!.Text.Length) return "";
        return entry.Text.Substring(row.Start, row.Length);
    }

    public string LineText(long line) => TryEntry(line, out var entry) ? entry!.Text : "";
    public TerminalKind LineKind(long line) => TryEntry(line, out var entry) ? entry!.Kind : TerminalKind.Output;

    private bool TryEntry(long line, out Entry? entry)
    {
        long index = line - _firstSequence;
        entry = index >= 0 && index < _lines.Count ? _lines[(int)index] : null;
        return entry != null;
    }

    public TerminalPoint PointAt(int row, int cell)
    {
        var slice = Row(row);
        return new(slice.Line, slice.Start + Math.Clamp(cell - slice.Indent, 0, slice.Length));
    }

    /// <summary>The first row showing a point, or -1 once it has been trimmed.</summary>
    public int RowOf(TerminalPoint point)
    {
        long index = point.Line - _firstSequence;
        if (index < 0 || index >= _lines.Count) return -1;
        var entry = _lines[(int)index];
        int part = 0;
        while (part < entry.Rows - 1 && entry.Breaks![part] <= point.Column) part++;
        return _firstRow[(int)index] + part;
    }

    /// <summary>The text between two points. Decorative rules are left out and soft wraps do not add line breaks.</summary>
    public string Text(TerminalPoint from, TerminalPoint to)
    {
        if (to < from) (from, to) = (to, from);
        var text = new StringBuilder();
        bool first = true;
        for (long line = Math.Max(from.Line, _firstSequence); line <= to.Line && line < NextLine; line++)
        {
            var entry = _lines[(int)(line - _firstSequence)];
            if (entry.Kind == TerminalKind.Rule) continue;
            int start = line == from.Line ? Math.Clamp(from.Column, 0, entry.Text.Length) : 0;
            int end = line == to.Line ? Math.Clamp(to.Column, start, entry.Text.Length) : entry.Text.Length;
            if (!first) text.Append(Environment.NewLine);
            text.Append(entry.Text.AsSpan(start, end - start).TrimEnd()); first = false;
        }
        return text.ToString();
    }

    /// <summary>Results of the last finished order, without its echoed command line or rules.</summary>
    /// <param name="previous">Skips the block being written, for an order that copies what came before it.</param>
    public string LastOutput(bool previous = false)
    {
        var lines = new List<string>();
        int block = -1;
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            var entry = _lines[i];
            if (previous && entry.Block == _block) continue;
            if (entry.Kind is TerminalKind.Command or TerminalKind.Rule) { if (lines.Count > 0 && entry.Block != block) break; continue; }
            if (block >= 0 && entry.Block != block) break;
            block = entry.Block; lines.Add(entry.Text.TrimEnd());
        }
        lines.Reverse();
        return string.Join(Environment.NewLine, lines);
    }

    public string AllText() => _lines.Count == 0 ? "" : Text(new(_firstSequence, 0), new(NextLine - 1, int.MaxValue));

    /// <summary>Lines of the scrollback containing a text, for find-in-output.</summary>
    public IEnumerable<long> Find(string text)
    {
        for (int i = _lines.Count - 1; i >= 0; i--)
            if (_lines[i].Text.Contains(text, StringComparison.OrdinalIgnoreCase)) yield return _firstSequence + i;
    }
}
