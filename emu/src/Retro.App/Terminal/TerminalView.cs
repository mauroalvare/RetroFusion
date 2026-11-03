using System.Numerics;
using ImGuiNET;

namespace Retro.App;

public enum TerminalCursor { Block, Bar, Underline }

/// <summary>Optional treatments. Reading comes first, so each one can be turned off.</summary>
public sealed record TerminalLook(TerminalCursor Cursor, bool Blink, bool Scanlines);

/// <summary>
/// Draws the console as a grid of fixed cells and turns ImGui events into editing calls. Only visible rows are
/// drawn, so the length of the scrollback does not change the cost of a frame. The prompt follows the output,
/// as in a real terminal: scrolling up leaves it behind, and typing returns to it.
/// </summary>
internal sealed class TerminalView(ProgramTerminal terminal, TerminalInput input, Func<TerminalLook> look)
{
    private static readonly (ImGuiKey Key, TerminalKey Action)[] Keys =
    [
        (ImGuiKey.Enter, TerminalKey.Enter), (ImGuiKey.KeypadEnter, TerminalKey.Enter), (ImGuiKey.Escape, TerminalKey.Escape), (ImGuiKey.Tab, TerminalKey.Tab),
        (ImGuiKey.Backspace, TerminalKey.Backspace), (ImGuiKey.Delete, TerminalKey.Delete), (ImGuiKey.Insert, TerminalKey.Insert),
        (ImGuiKey.LeftArrow, TerminalKey.Left), (ImGuiKey.RightArrow, TerminalKey.Right), (ImGuiKey.UpArrow, TerminalKey.Up), (ImGuiKey.DownArrow, TerminalKey.Down),
        (ImGuiKey.Home, TerminalKey.Home), (ImGuiKey.End, TerminalKey.End), (ImGuiKey.PageUp, TerminalKey.PageUp), (ImGuiKey.PageDown, TerminalKey.PageDown),
        (ImGuiKey.A, TerminalKey.A), (ImGuiKey.C, TerminalKey.C), (ImGuiKey.E, TerminalKey.E), (ImGuiKey.K, TerminalKey.K), (ImGuiKey.L, TerminalKey.L),
        (ImGuiKey.R, TerminalKey.R), (ImGuiKey.U, TerminalKey.U), (ImGuiKey.V, TerminalKey.V), (ImGuiKey.W, TerminalKey.W), (ImGuiKey.X, TerminalKey.X),
        (ImGuiKey.Y, TerminalKey.Y), (ImGuiKey.Z, TerminalKey.Z)
    ];
    private readonly Dictionary<char, bool> _regularGlyph = [];
    private float _glyphSize;
    private int _top, _floor;
    private bool _follow = true, _dragOutput, _dragThumb, _dragInput;
    private double _lastInput;
    private float _cell, _rowHeight;
    /// <summary>Where the prompt line sits, for layout checks.</summary>
    public (Vector2 Min, Vector2 Max) InputBounds { get; private set; }
    /// <summary>Whether any key or character reached the console during the last frame.</summary>
    public bool TookInput { get; private set; }
    public int VisibleRows { get; private set; }
    public int Top => _top;
    public bool Following => _follow;
    public void ResetFloor() => _floor = 0;

    /// <summary>The console's own keyboard handling, used by both the full view and the one-row bar.</summary>
    public void ProcessKeyboard()
    {
        var io = ImGui.GetIO();
        TookInput = false;
        // AltGr arrives as Ctrl+Alt and writes characters such as @ and \ on Spanish keyboards.
        bool ctrl = io.KeyCtrl && !io.KeyAlt;
        var mods = (ctrl ? TerminalMods.Ctrl : 0) | (io.KeyShift ? TerminalMods.Shift : 0) | (io.KeyAlt ? TerminalMods.Alt : 0);
        foreach (var (key, action) in Keys)
        {
            bool letter = action >= TerminalKey.A;
            if (letter && !ctrl) continue;
            if (ImGui.IsKeyPressed(key, repeat: action is not (TerminalKey.Enter or TerminalKey.Escape or TerminalKey.L or TerminalKey.R)))
            { input.Key(action, mods); TookInput = true; }
        }
        var queue = io.InputQueueCharacters;
        if (queue.Size > 0 && !ctrl)
        {
            var text = new System.Text.StringBuilder(queue.Size);
            for (int i = 0; i < queue.Size; i++) text.Append((char)queue[i]);
            input.Text(text.ToString()); TookInput = true;
        }
        if (TookInput) _lastInput = ImGui.GetTime();
    }

    public void Draw(bool keyboard)
    {
        ImGui.PushFont(UiTheme.ConsoleFont);
        _cell = ImGui.CalcTextSize(UiText.Get("M")).X; _rowHeight = MathF.Round(ImGui.GetFontSize() * 1.2f);
        var origin = ImGui.GetCursorScreenPos(); var area = ImGui.GetContentRegionAvail();
        float scrollbar = UiTheme.Px(10), pad = UiTheme.Px(UiTheme.CompactHeight ? 4 : 8);
        int columns = Math.Max(8, (int)((area.X - scrollbar - pad * 2) / _cell));
        VisibleRows = Math.Max(1, (int)((area.Y - pad) / _rowHeight));
        terminal.Output.SetColumns(columns);
        ImGui.InvisibleButton("##terminal-grid", area, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool hovered = ImGui.IsItemHovered();
        if (keyboard) ProcessKeyboard();
        var output = terminal.Output;
        if (input.ClearViewRequested) { input.ClearViewRequested = false; _floor = output.RowCount; _follow = true; }
        _floor = Math.Min(_floor, output.RowCount);
        string prompt = input.Searching ? UiText.Format($"(buscar{(input.SearchFailed ? UiText.Get(", sin resultado") : UiText.Get(""))} «{input.SearchText}») ")
            : terminal.PromptForColumns(columns) + " ";
        string line = prompt + input.Editor.Text;
        int cursor = prompt.Length + input.Editor.Cursor;
        int inputRows = Math.Max(1, (line.Length + columns) / columns);
        var paste = input.PendingPaste;
        int reviewRows = paste.Count == 0 ? 0 : paste.Count + 2;
        int inputRow = Math.Max(output.RowCount, _floor);
        int total = inputRow + inputRows + reviewRows;
        Scroll(hovered, total, area, origin, scrollbar);
        if (_follow || input.FollowPrompt) { _follow = true; input.FollowPrompt = false; _top = Math.Max(_floor, total - VisibleRows); }
        _top = Math.Clamp(_top, 0, Math.Max(0, total - 1));
        Pointer(hovered, origin, pad, columns, inputRow, inputRows, prompt.Length, total);

        var draw = ImGui.GetWindowDrawList();
        var clipMax = origin + area;
        draw.PushClipRect(origin, clipMax, true);
        var font = ImGui.GetFont(); float size = ImGui.GetFontSize();
        if (_glyphSize != size) { _glyphSize = size; _regularGlyph.Clear(); }
        var selection = input.Selection;
        uint selected = ImGui.GetColorU32(ImGuiCol.TextSelectedBg);
        Vector2 Cell(int row, int column) => origin + new Vector2(pad + column * _cell, pad / 2 + (row - _top) * _rowHeight);
        int lastRow = Math.Min(total, _top + VisibleRows + 1);
        for (int row = _top; row < lastRow; row++)
        {
            if (row < output.RowCount)
            {
                var slice = output.Row(row);
                if (selection.Active)
                {
                    var (from, to) = selection.Range;
                    if (slice.Line >= from.Line && slice.Line <= to.Line)
                    {
                        int start = slice.Line == from.Line ? Math.Max(from.Column - slice.Start, 0) : 0;
                        int end = slice.Line == to.Line ? Math.Min(to.Column - slice.Start, slice.Length) : slice.Length;
                        if (end > start) draw.AddRectFilled(Cell(row, slice.Indent + start), Cell(row, slice.Indent + end) + new Vector2(0, _rowHeight), selected);
                    }
                }
                DrawText(draw, font, size, Cell(row, slice.Indent), UiTheme.TerminalInk(slice.Kind), output.RowText(slice));
            }
            else if (row >= inputRow && row < inputRow + inputRows)
            {
                int part = row - inputRow, start = part * columns;
                if (start < line.Length)
                {
                    string text = line.Substring(start, Math.Min(columns, line.Length - start));
                    int promptCells = Math.Clamp(prompt.Length - start, 0, text.Length);
                    if (promptCells > 0) DrawText(draw, font, size, Cell(row, 0), UiTheme.TerminalInk(TerminalKind.Heading), text[..promptCells]);
                    if (promptCells < text.Length) DrawText(draw, font, size, Cell(row, promptCells), UiTheme.TerminalInk(TerminalKind.Command), text[promptCells..]);
                }
                if (input.Editor.HasSelection)
                {
                    var (from, to) = input.Editor.Selection;
                    int first = Math.Max(from + prompt.Length, start), last = Math.Min(to + prompt.Length, start + columns);
                    if (last > first) draw.AddRectFilled(Cell(row, first - start), Cell(row, last - start) + new Vector2(0, _rowHeight), selected);
                }
            }
            else if (row >= inputRow + inputRows)
            {
                int review = row - inputRow - inputRows;
                string text = review == 0 ? UiText.Format($"Pegaste {paste.Count} órdenes. Enter las ejecuta en orden · Esc las descarta") : review <= paste.Count ? "  " + paste[review - 1] : "";
                DrawText(draw, font, size, Cell(row, 0), UiTheme.TerminalInk(review == 0 ? TerminalKind.Warning : TerminalKind.Output), text.Length > columns ? text[..(columns - 1)] + "…" : text);
            }
        }
        var inputMin = Cell(inputRow, 0);
        InputBounds = (inputMin, inputMin + new Vector2(columns * _cell, inputRows * _rowHeight));
        if (keyboard && !input.ReviewingPaste) DrawCursor(draw, font, size, Cell(inputRow + cursor / columns, cursor % columns), cursor < line.Length ? line[cursor] : ' ');
        DrawScrollbar(draw, origin, area, scrollbar, total);
        if (input.Choosing) DrawChoices(draw, font, size, Cell(inputRow + cursor / columns, cursor % columns), origin, area, columns);
        if (look().Scanlines && !UiTheme.HighContrast)
            for (float y = origin.Y; y < clipMax.Y; y += 3) draw.AddLine(new(origin.X, y), new(clipMax.X, y), 0x1A000000);
        draw.PopClipRect();
        ImGui.PopFont();
    }

    /// <summary>One row for use while a game has the screen. Long lines slide sideways so the cursor stays visible.</summary>
    public void DrawBar(bool keyboard)
    {
        ImGui.PushFont(UiTheme.ConsoleFont);
        _cell = ImGui.CalcTextSize(UiText.Get("M")).X;
        var origin = ImGui.GetCursorScreenPos(); var area = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFontSize() * 1.2f);
        ImGui.InvisibleButton("##terminal-bar", area);
        if (keyboard) ProcessKeyboard();
        string prompt = keyboard ? terminal.Prompt + " " : terminal.Prompt + UiText.Get(" Clic para escribir · Ctrl+Shift+T abre la consola");
        string line = prompt + (keyboard ? input.Editor.Text : "");
        int columns = Math.Max(8, (int)(area.X / _cell)), cursor = prompt.Length + input.Editor.Cursor;
        int shift = Math.Max(0, cursor - columns + 2);
        var draw = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont(); float size = ImGui.GetFontSize();
        draw.PushClipRect(origin, origin + area, true);
        string visible = shift < line.Length ? line[shift..] : "";
        DrawText(draw, font, size, origin, UiTheme.TerminalInk(keyboard ? TerminalKind.Command : TerminalKind.Detail), visible.Length > columns ? visible[..columns] : visible);
        if (keyboard) DrawCursor(draw, font, size, origin + new Vector2((cursor - shift) * _cell, 0), cursor < line.Length ? line[cursor] : ' ');
        draw.PopClipRect();
        InputBounds = (origin, origin + area);
        ImGui.PopFont();
    }

    private void Scroll(bool hovered, int total, Vector2 area, Vector2 origin, float scrollbar)
    {
        int maximum = Math.Max(0, total - VisibleRows);
        var io = ImGui.GetIO();
        int before = _top;
        if (hovered && io.MouseWheel != 0) _top -= (int)MathF.Round(io.MouseWheel * 3);
        switch (input.PendingScroll)
        {
            case TerminalScroll.LineUp: _top--; break;
            case TerminalScroll.LineDown: _top++; break;
            case TerminalScroll.PageUp: _top -= Math.Max(1, VisibleRows - 1); break;
            case TerminalScroll.PageDown: _top += Math.Max(1, VisibleRows - 1); break;
            case TerminalScroll.Top: _top = 0; break;
            case TerminalScroll.Bottom: _top = maximum; break;
        }
        input.PendingScroll = TerminalScroll.None;
        var mouse = io.MousePos;
        bool overBar = maximum > 0 && mouse.X >= origin.X + area.X - scrollbar && hovered;
        if (overBar && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) _dragThumb = true;
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) _dragThumb = false;
        if (_dragThumb) _top = (int)MathF.Round(Math.Clamp((mouse.Y - origin.Y) / area.Y, 0, 1) * maximum);
        _top = Math.Clamp(_top, 0, maximum);
        if (_top != before) _follow = _top >= maximum;
    }

    private void Pointer(bool hovered, Vector2 origin, float pad, int columns, int inputRow, int inputRows, int promptLength, int total)
    {
        var io = ImGui.GetIO();
        var buffer = terminal.Output;
        (int Row, int Column) At()
        {
            var local = io.MousePos - origin - new Vector2(pad, pad / 2);
            return (_top + (int)MathF.Floor(local.Y / _rowHeight), (int)MathF.Round(local.X / _cell));
        }
        if (_dragThumb) return;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var (row, column) = At();
            if (row >= inputRow && row < inputRow + inputRows)
            {
                input.Selection.Clear();
                input.Editor.MoveTo((row - inputRow) * columns + Math.Clamp(column, 0, columns) - promptLength);
                _dragInput = true;
            }
            else if (row < buffer.RowCount && buffer.RowCount > 0)
            {
                var point = buffer.PointAt(row, column);
                int clicks = io.MouseClickedCount[0];
                input.Editor.Deselect();
                if (clicks >= 3) input.Selection.SelectLine(point);
                else if (clicks == 2) input.Selection.SelectWord(point);
                else { input.Selection.Begin(point); _dragOutput = true; }
            }
            else input.Selection.Clear();
        }
        if (_dragOutput && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var (row, column) = At();
            // Dragging past an edge keeps selecting through the scrollback.
            if (row < _top && _top > 0) { _top--; _follow = false; }
            else if (row >= _top + VisibleRows && _top + VisibleRows < total) _top++;
            if (buffer.RowCount > 0) input.Selection.Extend(buffer.PointAt(Math.Clamp(row, 0, buffer.RowCount - 1), column));
        }
        if (_dragInput && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var (row, column) = At();
            input.Editor.MoveTo((Math.Clamp(row, inputRow, inputRow + inputRows - 1) - inputRow) * columns + Math.Clamp(column, 0, columns) - promptLength, select: true);
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) _dragOutput = _dragInput = false;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            // As in Windows consoles: right click copies a selection, otherwise it pastes.
            if (input.Selection.Active || input.Editor.HasSelection) input.Copy();
            else input.Paste(ImGui.GetClipboardText() ?? "");
        }
    }

    /// <summary>Font fallback can bring a glyph of another width: such a row is drawn cell by cell to keep the grid.</summary>
    private void DrawText(ImDrawListPtr draw, ImFontPtr font, float size, Vector2 at, uint color, string text)
    {
        if (text.Length == 0) return;
        bool regular = true;
        foreach (char c in text)
        {
            if (c < 0x100) continue;
            if (!_regularGlyph.TryGetValue(c, out bool same))
                _regularGlyph[c] = same = MathF.Abs(font.GetCharAdvance(c) - _cell) < 0.5f;
            if (!same) { regular = false; break; }
        }
        if (regular) { draw.AddText(font, size, at, color, UiText.Get(text)); return; }
        for (int i = 0; i < text.Length; i++)
        {
            string glyph = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? text.Substring(i++, 2) : text[i].ToString();
            draw.AddText(font, size, at, color, UiText.Get(glyph)); at.X += _cell;
        }
    }

    private void DrawCursor(ImDrawListPtr draw, ImFontPtr font, float size, Vector2 at, char under)
    {
        var style = look();
        if (style.Blink && (ImGui.GetTime() - _lastInput) % 1.06 > 0.53) return;
        uint ink = UiTheme.ConsoleCursorInk;
        var cell = new Vector2(_cell, _rowHeight);
        switch (style.Cursor)
        {
            case TerminalCursor.Bar: draw.AddRectFilled(at, at + new Vector2(MathF.Max(2, UiTheme.Px(2)), cell.Y), ink); break;
            case TerminalCursor.Underline: draw.AddRectFilled(at + new Vector2(0, cell.Y - UiTheme.Px(3)), at + cell, ink); break;
            default:
                draw.AddRectFilled(at, at + cell, ink);
                if (!char.IsWhiteSpace(under)) draw.AddText(font, size, at, ImGui.GetColorU32(ImGuiCol.WindowBg), UiText.Get(under.ToString()));
                break;
        }
    }

    private void DrawScrollbar(ImDrawListPtr draw, Vector2 origin, Vector2 area, float width, int total)
    {
        if (total <= VisibleRows) return;
        float x = origin.X + area.X - width, height = area.Y;
        float thumb = Math.Max(UiTheme.Px(24), height * VisibleRows / total);
        float y = origin.Y + (height - thumb) * _top / Math.Max(1, total - VisibleRows);
        draw.AddRectFilled(new(x, origin.Y), new(x + width, origin.Y + height), ImGui.GetColorU32(ImGuiCol.ScrollbarBg));
        draw.AddRectFilled(new(x + 2, y), new(x + width - 2, y + thumb), ImGui.GetColorU32(_dragThumb ? ImGuiCol.ScrollbarGrabActive : ImGuiCol.ScrollbarGrab));
    }

    private void DrawChoices(ImDrawListPtr draw, ImFontPtr font, float size, Vector2 cursor, Vector2 origin, Vector2 area, int columns)
    {
        var choices = input.Choices;
        const int Shown = 8;
        int first = Math.Clamp(input.ChoiceIndex - Shown / 2, 0, Math.Max(0, choices.Count - Shown)), count = Math.Min(Shown, choices.Count);
        int width = Math.Min(columns, Math.Max(24, choices.Skip(first).Take(count).Max(item => item.Value.Length + (item.Detail.Length > 0 ? item.Detail.Length + 3 : 0)) + 2));
        var box = new Vector2(width * _cell, (count + 1) * _rowHeight);
        float x = Math.Clamp(cursor.X, origin.X, Math.Max(origin.X, origin.X + area.X - box.X - UiTheme.Px(12)));
        float below = cursor.Y + _rowHeight, top = below + box.Y <= origin.Y + area.Y ? below : Math.Max(origin.Y, cursor.Y - box.Y);
        var at = new Vector2(x, top);
        draw.AddRectFilled(at, at + box, ImGui.GetColorU32(ImGuiCol.PopupBg));
        draw.AddRect(at, at + box, ImGui.GetColorU32(ImGuiCol.Border));
        for (int i = 0; i < count; i++)
        {
            var row = at + new Vector2(_cell / 2, i * _rowHeight);
            var item = choices[first + i];
            if (first + i == input.ChoiceIndex) draw.AddRectFilled(row - new Vector2(_cell / 2, 0), row + new Vector2(box.X, _rowHeight), ImGui.GetColorU32(ImGuiCol.TextSelectedBg));
            string text = item.Detail.Length > 0 ? item.Value + " · " + item.Detail : item.Value;
            if (text.Length > width - 1) text = text[..Math.Max(1, width - 2)] + "…";
            DrawText(draw, font, size, row, UiTheme.TerminalInk(first + i == input.ChoiceIndex ? TerminalKind.Command : TerminalKind.Output), text);
        }
        DrawText(draw, font, size, at + new Vector2(_cell / 2, count * _rowHeight), UiTheme.TerminalInk(TerminalKind.Detail), UiText.Format($"{input.ChoiceIndex + 1} de {choices.Count} · Tab sigue · Esc deja como estaba"));
    }
}
