using System.Globalization;
using System.Numerics;
using System.Text;
using ImGuiNET;

namespace Retro.App;

public sealed record AppCommand(string Id, string Title, string Group, string Keywords, string Shortcut,
    Action Execute, Func<bool>? CanExecute = null, string Unavailable = "")
{
    private string _title = Title, _group = Group;
    public string Title { get => UiText.Get(_title); init => _title = value; }
    public string Group { get => UiText.Get(_group); init => _group = value; }
    public bool Enabled => CanExecute?.Invoke() ?? true;
}

/// <summary>Searchable actions. Search never executes a command or changes the machine.</summary>
public sealed class CommandPalette
{
    private bool _request, _open, _focus;
    private string _query = "";
    private int _selected;
    public bool IsOpen => _open || _request;
    public void Open() { _request = true; _focus = true; _query = ""; _selected = 0; }

    public static IReadOnlyList<AppCommand> Search(IEnumerable<AppCommand> commands, string query)
    {
        string[] words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return commands.ToList();
        return commands.Where(command => words.All(word => Normalize(command.Title + " " + UiText.Get(command.Group) + " " + UiText.Get(command.Keywords) + " " + command.Id).Contains(word)))
            .OrderByDescending(command => words.Length > 0 && Normalize(command.Title).StartsWith(string.Join(' ', words)))
            .ThenBy(command => command.Group, StringComparer.Ordinal).ThenBy(command => command.Title, StringComparer.Ordinal).ToList();
    }

    private static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();

    public void Draw(IReadOnlyList<AppCommand> commands)
    {
        const string title = "Buscar acción###acciones";
        if (_request) { ImGui.OpenPopup(UiText.Label(title)); _request = false; _open = true; }
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.Pos + new Vector2(vp.Size.X*0.5f,12), ImGuiCond.Appearing, new Vector2(0.5f, 0));
        ImGui.SetNextWindowSize(new Vector2(Math.Min(UiTheme.Px(620), vp.Size.X - 24), Math.Min(UiTheme.Px(520), vp.Size.Y - UiTheme.StatusBarHeight - 24)), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(UiText.Label(title), ref _open, ImGuiWindowFlags.NoDocking)) return;
        // A modal owns the dimmed navigation area too; reserving it can leave no result row at 200%.
        WorkspaceLayout.KeepOnScreen(modal:true);
        bool roomy = ImGui.GetWindowHeight() >= UiTheme.Px(400);
        if (roomy) UiTheme.Hint("Encontrá una herramienta, una opción o una acción de la partida.");
        if (_focus) { ImGui.SetKeyboardFocusHere(); _focus = false; }
        ImGui.SetNextItemWidth(-1);
        string before = _query;
        bool submit = ImGui.InputTextWithHint("##buscar-accion", UiText.Get(roomy ? "Buscar: volumen, guardar, crear, código…" : "Buscar acción…"), ref _query, 120, ImGuiInputTextFlags.EnterReturnsTrue);
        bool searchFocused = ImGui.IsItemActive() || ImGui.IsItemFocused();
        if (_query != before) _selected = 0;
        var matches = Search(commands, _query);
        if (searchFocused && ImGui.IsKeyPressed(ImGuiKey.DownArrow)) _selected++;
        if (searchFocused && ImGui.IsKeyPressed(ImGuiKey.UpArrow)) _selected--;
        _selected = Math.Clamp(_selected, 0, Math.Max(0, matches.Count - 1));
        AppCommand? chosen = null;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) { _open = false; ImGui.CloseCurrentPopup(); }
        if (submit && matches.Count > 0 && matches[_selected].Enabled) chosen = matches[_selected];
        float footer = roomy ? ImGui.GetFrameHeightWithSpacing() + ImGui.GetTextLineHeightWithSpacing() : 0;
        ImGui.BeginChild("resultados-acciones", new Vector2(0, -footer));
        if (matches.Count == 0) UiTheme.EmptyState("No encontré esa acción", "Probá con otra palabra: pantalla, biblioteca, sonido o IA.");
        for (int i = 0; i < matches.Count; i++)
        {
            var command = matches[i];
            ImGui.PushID(command.Id);
            ImGui.BeginDisabled(!command.Enabled);
            if (ImGui.Selectable(UiText.Label(UiTheme.Fit(command.Title, ImGui.GetContentRegionAvail().X) + UiText.Get("###command")), i == _selected, ImGuiSelectableFlags.None, new Vector2(0, Math.Max(UiTheme.Px(30), ImGui.GetTextLineHeight())))) chosen = command;
            if (ImGui.IsItemFocused()) _selected = i;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiTheme.Tooltip(command.Title + "\n" + command.Group +
                (command.Shortcut.Length > 0 ? " · " + command.Shortcut : "") + (!command.Enabled ? "\n" + command.Unavailable : ""));
            if (searchFocused && i == _selected && (ImGui.IsKeyPressed(ImGuiKey.UpArrow) || ImGui.IsKeyPressed(ImGuiKey.DownArrow))) ImGui.SetScrollHereY();
            if (roomy) UiTheme.Hint(command.Group + (command.Shortcut.Length > 0 ? " · " + command.Shortcut : "") + (!command.Enabled ? " · " + command.Unavailable : ""));
            ImGui.PopID();
        }
        ImGui.EndChild();
        if (roomy) UiTheme.Hint("Flechas: recorrer · Enter: elegir · Esc: cerrar");
        if (roomy && ImGui.Button(UiText.Label("Cerrar"))) { _open = false; ImGui.CloseCurrentPopup(); }
        if (chosen != null) { _open = false; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
        chosen?.Execute();
    }
}
