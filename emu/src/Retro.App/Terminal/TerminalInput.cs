namespace Retro.App;

public enum TerminalKey { Enter, Escape, Tab, Backspace, Delete, Insert, Left, Right, Up, Down, Home, End, PageUp, PageDown, A, C, E, F, K, L, R, U, V, W, X, Y, Z }

[Flags] public enum TerminalMods { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

public enum TerminalScroll { None, LineUp, LineDown, PageUp, PageDown, Top, Bottom }

/// <summary>A selection of scrollback text. It follows the logical lines, so resizing does not change what is selected.</summary>
public sealed class TerminalSelection(TerminalBuffer buffer)
{
    public TerminalPoint Anchor { get; private set; }
    public TerminalPoint Head { get; private set; }
    public bool Active { get; private set; }
    public (TerminalPoint Start, TerminalPoint End) Range => Anchor < Head ? (Anchor, Head) : (Head, Anchor);
    public void Begin(TerminalPoint point) { Anchor = Head = point; Active = false; }
    public void Extend(TerminalPoint point) { Head = point; Active = Anchor != Head; }
    public void SelectLine(TerminalPoint point)
    {
        Anchor = new(point.Line, 0); Head = new(point.Line, buffer.LineText(point.Line).Length); Active = Head.Column > 0;
    }
    public void SelectWord(TerminalPoint point)
    {
        string text = buffer.LineText(point.Line);
        int column = Math.Min(point.Column, text.Length), start = column, end = column;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        Anchor = new(point.Line, start); Head = new(point.Line, end); Active = end > start;
    }
    public void Clear() => Active = false;
    /// <summary>Old output can be trimmed away while a selection is alive; then there is nothing left to copy.</summary>
    public string Text => Active && Range.End.Line >= buffer.FirstLine ? buffer.Text(Range.Start, Range.End) : "";
}

/// <summary>
/// Everything the keyboard does to the console, apart from drawing: line editing, history, reverse search,
/// completion and the review of pasted orders. The view only turns ImGui events into these calls.
/// </summary>
public sealed class TerminalInput(ProgramTerminal terminal, Func<string> readClipboard, Action<string> writeClipboard)
{
    public const int MaxPasteLines = 200, MaxPasteCharacters = 64 * 1024;
    public LineEditor Editor { get; } = new();
    public TerminalSelection Selection { get; } = new(terminal.Output);
    public TerminalScroll PendingScroll { get; set; }
    /// <summary>Set when an action must take the view back to the prompt.</summary>
    public bool FollowPrompt { get; set; }
    /// <summary>Text shown briefly under the prompt, such as a copy confirmation.</summary>
    public string Notice { get; private set; } = "";
    /// <summary>An order left the line during the last frame; the one-row bar then returns the keyboard.</summary>
    public bool Submitted { get; set; }
    /// <summary>Asked for by Ctrl+L: older output stays in the scrollback, above the view.</summary>
    public bool ClearViewRequested { get; set; }
    /// <summary>Stops following a task with Ctrl+C. Returns false when nothing was being followed.</summary>
    public Func<bool>? StopFollowing { get; set; }
    /// <summary>Whether cancellable work exists, to say how to reach it from an empty line.</summary>
    public Func<int>? RunningTasks { get; set; }

    // ---- completion
    public IReadOnlyList<CompletionItem> Choices { get; private set; } = [];
    public int ChoiceIndex { get; private set; } = -1;
    public bool Choosing => Choices.Count > 0;
    private string _beforeChoosing = "";
    private int _cursorBeforeChoosing, _choiceStart, _choiceEnd;

    // ---- reverse search
    public bool Searching { get; private set; }
    public string SearchText { get; private set; } = "";
    public bool SearchFailed { get; private set; }
    private int _searchIndex;
    private string _beforeSearch = "";

    // ---- multi-line paste
    public IReadOnlyList<string> PendingPaste { get; private set; } = [];
    public bool ReviewingPaste => PendingPaste.Count > 0;

    public void Text(string text)
    {
        if (ReviewingPaste) return;
        if (Searching) { SearchText += text; RunSearch(terminal.History.Entries.Count); return; }
        CloseChoices(keep: true); Selection.Clear(); Notice = "";
        Editor.Insert(text); FollowPrompt = true;
    }

    public void Paste(string text)
    {
        if (ReviewingPaste || Searching) return;
        CloseChoices(keep: true); Notice = "";
        string normal = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (normal.Length == 0) return;
        if (!normal.Contains('\n')) { Editor.Insert(normal); FollowPrompt = true; return; }
        // Nothing pasted ever runs by itself; several lines wait for a look and an explicit Enter.
        var lines = normal.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        if (lines.Length > MaxPasteLines || normal.Length > MaxPasteCharacters)
        { Notice = UiText.Format($"El pegado es demasiado largo: hasta {MaxPasteLines} líneas y {MaxPasteCharacters / 1024} KB."); return; }
        if (lines.Length == 1) { Editor.Insert(lines[0]); FollowPrompt = true; return; }
        PendingPaste = lines; FollowPrompt = true;
    }

    public void AcceptPaste()
    {
        var lines = PendingPaste; PendingPaste = [];
        terminal.Output.BeginBlock();
        foreach (string line in lines)
            if (!terminal.Submit(line))
            {
                terminal.Write(UiText.Format($"aviso: se detuvo el pegado en «{line}». Las líneas siguientes no se ejecutaron."), TerminalKind.Warning);
                break;
            }
    }

    public void CancelPaste() { PendingPaste = []; Notice = "Pegado descartado; no se ejecutó nada."; }

    public void Key(TerminalKey key, TerminalMods mods = TerminalMods.None)
    {
        bool ctrl = (mods & TerminalMods.Ctrl) != 0, shift = (mods & TerminalMods.Shift) != 0;
        if (ReviewingPaste)
        {
            if (key == TerminalKey.Enter) AcceptPaste();
            else if (key == TerminalKey.Escape) CancelPaste();
            return;
        }
        if (Searching) { SearchKey(key, ctrl); return; }
        if (Choosing)
        {
            switch (key)
            {
                case TerminalKey.Tab: case TerminalKey.Down: MoveChoice(shift && key == TerminalKey.Tab ? -1 : 1); return;
                case TerminalKey.Up: MoveChoice(-1); return;
                case TerminalKey.Enter: CloseChoices(keep: true); Editor.End(); return;
                case TerminalKey.Escape: CloseChoices(keep: false); return;
                case TerminalKey.Left: case TerminalKey.Right: CloseChoices(keep: true); break;
            }
        }
        Notice = "";
        switch (key)
        {
            case TerminalKey.Enter: Submit(); break;
            case TerminalKey.Escape: Escape(); break;
            case TerminalKey.Tab: Complete(shift); break;
            case TerminalKey.Backspace: Edit(() => { if (ctrl) Editor.DeleteWordLeft(); else Editor.Backspace(); }); break;
            case TerminalKey.Delete: if (shift && !ctrl) Cut(); else Edit(() => { if (ctrl) Editor.DeleteWordRight(); else Editor.Delete(); }); break;
            case TerminalKey.Insert: if (ctrl) Copy(); else if (shift) Paste(readClipboard()); break;
            case TerminalKey.Left: Navigate(() => { if (ctrl) Editor.WordLeftMove(shift); else Editor.Left(shift); }); break;
            case TerminalKey.Right: Navigate(() => { if (ctrl) Editor.WordRightMove(shift); else Editor.Right(shift); }); break;
            case TerminalKey.Home: if (ctrl) PendingScroll = TerminalScroll.Top; else Navigate(() => Editor.Home(shift)); break;
            case TerminalKey.End: if (ctrl) PendingScroll = TerminalScroll.Bottom; else Navigate(() => Editor.End(shift)); break;
            case TerminalKey.Up: if (ctrl) PendingScroll = TerminalScroll.LineUp; else Recall(-1); break;
            case TerminalKey.Down: if (ctrl) PendingScroll = TerminalScroll.LineDown; else Recall(1); break;
            case TerminalKey.PageUp: PendingScroll = TerminalScroll.PageUp; break;
            case TerminalKey.PageDown: PendingScroll = TerminalScroll.PageDown; break;
            case TerminalKey.A when ctrl: if (shift) Editor.SelectAll(); else Navigate(() => Editor.Home()); break;
            case TerminalKey.E when ctrl: Navigate(() => Editor.End()); break;
            case TerminalKey.C when ctrl: Interrupt(); break;
            case TerminalKey.K when ctrl: Edit(Editor.DeleteToEnd); break;
            case TerminalKey.U when ctrl: Edit(Editor.DeleteToStart); break;
            case TerminalKey.W when ctrl: Edit(Editor.DeleteWordLeft); break;
            case TerminalKey.L when ctrl: ClearViewRequested = true; break;
            case TerminalKey.R when ctrl: StartSearch(); break;
            case TerminalKey.V when ctrl: Paste(readClipboard()); break;
            case TerminalKey.X when ctrl: Cut(); break;
            case TerminalKey.Y when ctrl: Editor.Redo(); break;
            case TerminalKey.Z when ctrl: if (shift) Editor.Redo(); else Editor.Undo(); break;
        }
    }

    private void Edit(Action change) { Selection.Clear(); change(); FollowPrompt = true; }
    private void Navigate(Action move) { move(); FollowPrompt = true; }

    private void Submit()
    {
        string line = Editor.Text;
        Selection.Clear(); Editor.Reset(); FollowPrompt = true; Submitted = true;
        if (line.Trim().Length == 0) { terminal.History.Rewind(); terminal.Output.BeginBlock(); terminal.Write(terminal.Prompt, TerminalKind.Command); return; }
        terminal.Submit(line);
    }

    private void Escape()
    {
        if (Selection.Active) { Selection.Clear(); return; }
        if (Editor.HasSelection) { Editor.Deselect(); return; }
        if (Editor.Text.Length > 0) { Editor.Clear(); terminal.History.Rewind(); }
    }

    /// <summary>Ctrl+C: copies a selection; else stops following a task; else abandons the line. It never cancels work by itself.</summary>
    private void Interrupt()
    {
        if (Selection.Active || Editor.HasSelection) { Copy(); return; }
        if (StopFollowing?.Invoke() == true) { terminal.Write("Dejaste de seguir la tarea; sigue en segundo plano.", TerminalKind.Detail); return; }
        if (Editor.Text.Length > 0)
        {
            terminal.Output.BeginBlock();
            terminal.Write(terminal.Prompt + " " + Editor.Text + UiText.Get("^C"), TerminalKind.Command);
            Editor.Reset(); terminal.History.Rewind(); return;
        }
        if (RunningTasks?.Invoke() is > 0) Notice = "Ctrl+C no detiene tareas. Usá tareas y cancelar <id>.";
    }

    public void Copy()
    {
        string text = Selection.Active ? Selection.Text : Editor.SelectedText;
        if (text.Length == 0) return;
        writeClipboard(text); Notice = "Copiado al portapapeles.";
        Selection.Clear(); Editor.Deselect();
    }

    private void Cut()
    {
        string text = Editor.Cut();
        if (text.Length > 0) writeClipboard(text);
    }

    private void Recall(int direction)
    {
        Selection.Clear();
        Editor.Set(terminal.History.Move(direction, Editor.Text)); FollowPrompt = true;
    }

    private void Complete(bool backwards)
    {
        var found = terminal.Complete(Editor.Text, Editor.Cursor);
        if (found.Items.Count == 0) { Notice = "Sin sugerencias aquí."; return; }
        _beforeChoosing = Editor.Text; _cursorBeforeChoosing = Editor.Cursor;
        _choiceStart = found.Start; _choiceEnd = found.End;
        if (found.Items.Count == 1) { Apply(found.Items[0], closing: true); return; }
        string shared = SharedPrefix(found.Items.Select(item => item.Value));
        if (CommandLine.Fold(shared).Length > CommandLine.Fold(found.Prefix).Length)
        {
            // Writing what all the alternatives share is the first useful step; the next Tab lists them.
            Editor.Set(Editor.Text[..found.Start] + Quoted(shared, open: true) + Editor.Text[found.End..], found.Start + Quoted(shared, open: true).Length);
            return;
        }
        Choices = found.Items; ChoiceIndex = backwards ? Choices.Count - 1 : 0;
        Apply(Choices[ChoiceIndex], closing: false);
    }

    private static string Quoted(string value, bool open) =>
        open && value.Any(char.IsWhiteSpace) ? "\"" + value : CommandLine.Quote(value);

    private static string SharedPrefix(IEnumerable<string> values)
    {
        string? shared = null;
        foreach (string value in values)
        {
            if (shared == null) { shared = value; continue; }
            int length = 0;
            while (length < shared.Length && length < value.Length && char.ToLowerInvariant(shared[length]) == char.ToLowerInvariant(value[length])) length++;
            shared = shared[..length];
        }
        return shared ?? "";
    }

    private void Apply(CompletionItem item, bool closing)
    {
        string value = CommandLine.Quote(item.Value);
        bool folder = item.Value.EndsWith('\\') || item.Value.EndsWith('/');
        // A chosen word is finished, so the next argument can be typed right away; a folder keeps going.
        string tail = closing && !folder && _choiceEnd >= _beforeChoosing.Length ? " " : "";
        if (folder && value.EndsWith('"')) value = value[..^1];
        Editor.Set(_beforeChoosing[.._choiceStart] + value + tail + _beforeChoosing[_choiceEnd..], _choiceStart + value.Length + tail.Length);
        FollowPrompt = true;
    }

    private void MoveChoice(int direction)
    {
        ChoiceIndex = (ChoiceIndex + direction + Choices.Count) % Choices.Count;
        Apply(Choices[ChoiceIndex], closing: false);
    }

    public void CloseChoices(bool keep)
    {
        if (!Choosing) return;
        var chosen = Choices[ChoiceIndex];
        Choices = []; ChoiceIndex = -1;
        if (keep) Apply(chosen, closing: true); else Editor.Set(_beforeChoosing, _cursorBeforeChoosing);
    }

    private void StartSearch()
    {
        Searching = true; SearchText = ""; SearchFailed = false; _beforeSearch = Editor.Text;
        _searchIndex = terminal.History.Entries.Count; FollowPrompt = true;
    }

    private void RunSearch(int before)
    {
        int found = terminal.History.FindBefore(SearchText, before);
        SearchFailed = found < 0 && SearchText.Length > 0;
        if (found < 0) return;
        _searchIndex = found;
        Editor.Set(terminal.History.Entries[found]);
    }

    private void SearchKey(TerminalKey key, bool ctrl)
    {
        switch (key)
        {
            case TerminalKey.R when ctrl: RunSearch(_searchIndex); break;
            case TerminalKey.Backspace:
                if (SearchText.Length > 0) { SearchText = SearchText[..^1]; RunSearch(terminal.History.Entries.Count); }
                break;
            // The found order is left on the line to be read or edited, not run behind the user's back.
            case TerminalKey.Enter: case TerminalKey.Left: case TerminalKey.Right: case TerminalKey.Home: case TerminalKey.End: Searching = false; terminal.History.Rewind(); break;
            case TerminalKey.Escape: Searching = false; Editor.Set(_beforeSearch); break;
            case TerminalKey.C when ctrl: Searching = false; Editor.Set(_beforeSearch); break;
        }
    }

    /// <summary>The order as a user would see it after Enter, for the hint under the prompt.</summary>
    public string? Hint => Editor.Text.Length == 0 ? null : terminal.HintFor(Editor.Text);
}
