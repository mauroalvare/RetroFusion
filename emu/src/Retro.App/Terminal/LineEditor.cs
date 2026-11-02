using System.Globalization;
using System.Text;

namespace Retro.App;

/// <summary>Editable line of the console: cursor, selection, words and undo. It knows nothing about keys or drawing.</summary>
public sealed class LineEditor
{
    private const int UndoLimit = 64;
    private readonly List<(string Text, int Cursor)> _undo = [], _redo = [];
    private bool _typing;
    public string Text { get; private set; } = "";
    public int Cursor { get; private set; }
    public int? Anchor { get; private set; }
    public bool HasSelection => Anchor is { } anchor && anchor != Cursor;
    public (int Start, int End) Selection => HasSelection ? (Math.Min(Anchor!.Value, Cursor), Math.Max(Anchor!.Value, Cursor)) : (Cursor, Cursor);
    public string SelectedText => HasSelection ? Text[Selection.Start..Selection.End] : "";

    private void Remember(bool typing = false)
    {
        // A run of typed letters is undone as one edit.
        if (typing && _typing) return;
        _typing = typing;
        _undo.Add((Text, Cursor)); _redo.Clear();
        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
    }

    /// <summary>Replaces the whole line, as history and completion do.</summary>
    public void Set(string text, int? cursor = null)
    {
        if (text == Text && cursor == null) return;
        Remember(); Text = Clean(text); Cursor = Math.Clamp(cursor ?? Text.Length, 0, Text.Length); Anchor = null; _typing = false;
    }

    public void Clear() => Set("");

    /// <summary>Lines reach the editor one at a time: a tab is a space and control characters never enter.</summary>
    private static string Clean(string text)
    {
        var clean = new StringBuilder(text.Length);
        foreach (char c in text)
            if (c is '\t' or '\n' or '\r') clean.Append(' ');
            else if (!char.IsControl(c)) clean.Append(c);
        try { return clean.ToString().Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return clean.ToString(); }   // an unpaired surrogate stays as it is
    }

    public void Insert(string text)
    {
        string clean = Clean(text);
        if (clean.Length == 0) return;
        Remember(typing: clean.Length == 1 && !HasSelection && clean[0] != ' ');
        DeleteSelection();
        int room = CommandLine.MaxLength - Text.Length;
        if (room <= 0) return;
        if (clean.Length > room) clean = clean[..room];
        Text = Text.Insert(Cursor, clean); Cursor += clean.Length;
    }

    private void DeleteSelection()
    {
        if (!HasSelection) { Anchor = null; return; }
        var (start, end) = Selection;
        Text = Text.Remove(start, end - start); Cursor = start; Anchor = null;
    }

    private void DeleteTo(int other)
    {
        if (other == Cursor) return;
        Remember();
        int start = Math.Min(other, Cursor);
        Text = Text.Remove(start, Math.Abs(other - Cursor)); Cursor = start; Anchor = null;
    }

    public void Backspace() { if (HasSelection) { Remember(); DeleteSelection(); } else DeleteTo(PreviousElement(Cursor)); }
    public void Delete() { if (HasSelection) { Remember(); DeleteSelection(); } else DeleteTo(NextElement(Cursor)); }
    public void DeleteWordLeft() { if (HasSelection) { Remember(); DeleteSelection(); } else DeleteTo(WordLeft(Cursor)); }
    public void DeleteWordRight() { if (HasSelection) { Remember(); DeleteSelection(); } else DeleteTo(WordRight(Cursor)); }
    public void DeleteToStart() => DeleteTo(0);
    public void DeleteToEnd() => DeleteTo(Text.Length);

    public string Cut()
    {
        string text = SelectedText;
        if (text.Length > 0) { Remember(); DeleteSelection(); }
        return text;
    }

    private void Move(int position, bool select)
    {
        _typing = false;
        if (select) Anchor ??= Cursor; else Anchor = null;
        Cursor = Math.Clamp(position, 0, Text.Length);
    }

    public void Left(bool select = false) => Move(HasSelection && !select ? Selection.Start : PreviousElement(Cursor), select);
    public void Right(bool select = false) => Move(HasSelection && !select ? Selection.End : NextElement(Cursor), select);
    public void WordLeftMove(bool select = false) => Move(WordLeft(Cursor), select);
    public void WordRightMove(bool select = false) => Move(WordRight(Cursor), select);
    public void Home(bool select = false) => Move(0, select);
    public void End(bool select = false) => Move(Text.Length, select);
    public void MoveTo(int position, bool select = false) => Move(position, select);
    public void SelectAll() { Anchor = 0; Cursor = Text.Length; }
    public void Deselect() => Anchor = null;

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add((Text, Cursor)); (Text, Cursor) = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Anchor = null; _typing = false;
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add((Text, Cursor)); (Text, Cursor) = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); Anchor = null; _typing = false;
        return true;
    }

    /// <summary>A new line for the next order: nothing typed before can be undone into it.</summary>
    public void Reset() { Text = ""; Cursor = 0; Anchor = null; _undo.Clear(); _redo.Clear(); _typing = false; }

    // A composed letter or a surrogate pair is one step of the cursor.
    private int NextElement(int position) => position >= Text.Length ? Text.Length : position + StringInfo.GetNextTextElementLength(Text.AsSpan(position));

    private int PreviousElement(int position)
    {
        if (position <= 0) return 0;
        int walk = 0, last = 0;
        while (walk < position) { last = walk; walk += StringInfo.GetNextTextElementLength(Text.AsSpan(walk)); }
        return last;
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.';

    private int WordLeft(int position)
    {
        while (position > 0 && !IsWord(Text[position - 1])) position--;
        while (position > 0 && IsWord(Text[position - 1])) position--;
        return position;
    }

    private int WordRight(int position)
    {
        while (position < Text.Length && IsWord(Text[position])) position++;
        while (position < Text.Length && !IsWord(Text[position])) position++;
        return position;
    }
}
