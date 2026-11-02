using System.Numerics;
using ImGuiNET;

namespace Retro.App;

public sealed partial class HomeScreen
{
    private void DrawAlternativeLibrary()
    {
        ImGui.SetNextWindowPos(WorkspaceLayout.Origin); ImGui.SetNextWindowSize(WorkspaceLayout.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, UiTheme.Px(10, 6));
        ImGui.Begin("##inicio", WorkspaceLayout.EditorFlags);
        bool explorer = UiTheme.Interface == InterfaceStyle.Terminal;
        bool tree = explorer && !UiTheme.CompactHeight && ImGui.GetContentRegionAvail().X >= UiTheme.Px(760);
        if (tree)
        {
            ImGui.BeginChild("library-tree", new(UiTheme.Px(170), 0));
            ImGui.TextUnformatted(UiText.Get("BIBLIOTECA")); ImGui.Separator();
            Nav("Todos los juegos", "all"); Nav("Favoritos", "favorites"); Nav("Recientes", "recent");
            if (ImGui.TreeNodeEx(UiText.Label("Sistemas"), ImGuiTreeNodeFlags.DefaultOpen))
            { foreach (var machine in Machines) Nav(Name(machine), "machine", machine.Id); ImGui.TreePop(); }
            if (ImGui.TreeNodeEx(UiText.Label("Proyectos"), ImGuiTreeNodeFlags.DefaultOpen))
            { Nav("Creados", "created"); Nav("Fusionados", "fusions"); ImGui.TreePop(); }
            ImGui.Separator(); DrawFileActions(primary: true);
            ImGui.EndChild(); ImGui.SameLine();
        }
        ImGui.BeginChild("layout-catalog", new(0, 0));
        if (UiTheme.CompactHeight) DrawCompactNavigation();
        else
        {
            if (!tree) DrawCompactNavigation();
            if (explorer) ImGui.TextDisabled(UiTheme.Fit(library.Folder + " / " + UiText.Get(SectionTitle()), ImGui.GetContentRegionAvail().X));
            else { using (UiTheme.Display()) ImGui.TextUnformatted(UiText.Get(SectionTitle())); }
            if (!tree) DrawFileActions(primary: false);
            DrawLibraryNotices();
        }
        DrawLibraryFilters(); DrawFilteredGames();
        ImGui.EndChild(); ImGui.End(); ImGui.PopStyleVar();
    }

    private LibraryCard SelectedCard(IReadOnlyList<LibraryCard> cards)
    {
        var selected = cards.FirstOrDefault(MatchesSelection) ?? cards.FirstOrDefault(card => card.Key == _selectedKey) ?? cards[0];
        _selectedKey = selected.Key; _selectedFile = selected.File; return selected;
    }
    private float SelectionHeight => ImGui.GetFrameHeightWithSpacing()
        + (UiTheme.CompactHeight ? 0 : ImGui.GetTextLineHeightWithSpacing());

    private void DrawSelectionActions(IReadOnlyList<LibraryCard> cards)
    {
        var card = SelectedCard(cards);
        if (!UiTheme.CompactHeight) ImGui.TextUnformatted(UiTheme.Fit(card.Title, ImGui.GetContentRegionAvail().X));
        ImGui.BeginDisabled(!card.Openable || library.Scanning);
        if (UiTheme.PrimaryButton("Jugar")) { Close(); card.Play(); }
        TrackLibraryControl("Jugar selección"); ImGui.EndDisabled();
        ImGui.SameLine(); if (ImGui.Button(UiText.Label("Ficha"))) OpenMetadata(card.Key);
        TrackLibraryControl("Ficha selección");
        ImGui.SameLine(); ImGui.PushID("selection-favorite"); FavoriteButton(card.Key, new(ImGui.GetFrameHeight(), 0)); ImGui.PopID();
        if (ImGui.IsItemHovered()) UiTheme.Tooltip(_activity.IsFavorite(card.Key) ? "Quitar de favoritos" : "Agregar a favoritos");
    }

    private void DrawExplorerFiles(IReadOnlyList<LibraryCard> cards)
    {
        SelectedCard(cards);
        if (UiTheme.CompactHeight)
        {
            // Row actions already include Play and Card. A duplicate footer would hide the files.
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(ImGui.GetStyle().CellPadding.X, 0));
            DrawRows(cards); ImGui.PopStyleVar(); return;
        }
        ImGui.BeginChild("explorer-files", new(0, -SelectionHeight));
        DrawRows(cards); ImGui.EndChild();
        DrawSelectionActions(cards);
    }

    private unsafe void DrawLoungeCards(IReadOnlyList<LibraryCard> cards)
    {
        SelectedCard(cards);
        bool compact = UiTheme.CompactHeight || ImGui.GetContentRegionAvail().Y - SelectionHeight < UiTheme.Px(220);
        float height = compact ? ImGui.GetFrameHeight() : UiTheme.Px(210);
        int columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / UiTheme.Px(230)), 1, 6);
        float footer = SelectionHeight;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, UiTheme.Px(5, compact ? 0 : 5));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0));
        if (ImGui.BeginTable("lounge-cards", columns, ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchSame, new(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - footer))))
        {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            try
            {
                clipper.Begin((cards.Count + columns - 1) / columns, height + ImGui.GetStyle().CellPadding.Y * 2);
                if (_scrollToSelection)
                {
                    int selected = cards.ToList().FindIndex(MatchesSelection);
                    if (selected >= 0) clipper.IncludeItemByIndex(selected / columns);
                }
                while (clipper.Step())
                    for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                    {
                        ImGui.TableNextRow(ImGuiTableRowFlags.None, height);
                        for (int column = 0; column < columns && row * columns + column < cards.Count; column++)
                        { ImGui.TableNextColumn(); DrawLoungeCard(cards[row * columns + column], height, compact); }
                    }
            }
            finally { ImGuiNative.ImGuiListClipper_destroy(clipper.NativePtr); }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar(2); DrawSelectionActions(cards);
    }

    private void DrawLoungeCard(LibraryCard card, float height, bool compact)
    {
        ImGui.PushID(card.Key);
        ImGui.PushID(card.File ?? "");
        bool selected = MatchesSelection(card);
        Vector2 at = ImGui.GetCursorScreenPos(), size = new(ImGui.GetContentRegionAvail().X, height);
        bool chosen = ImGui.Selectable("##game-card", selected, ImGuiSelectableFlags.AllowDoubleClick, size);
        LibraryControlBounds?.Invoke("Tarjeta:" + card.Key, at, at + size);
        if (card.File != null) LibraryControlBounds?.Invoke("Tarjeta archivo:" + card.Key + "@" + card.File, at, at + size);
        if (chosen || ImGui.IsItemFocused()) { _selectedKey = card.Key; _selectedFile = card.File; }
        if (chosen && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && card.Openable && !library.Scanning) { Close(); card.Play(); }
        if (selected && _scrollToSelection) { ImGui.SetScrollHereY(0); _scrollToSelection = false; }
        if (ImGui.IsItemHovered()) UiTheme.Tooltip(card.Title + " · " + UiText.Get(card.Detail) + (!card.Openable ? "\n" + UiText.Get(card.Hint ?? "Este formato todavía no se puede abrir.") : ""));
        var draw = ImGui.GetWindowDrawList();
        draw.AddRect(at, at + size, ImGui.ColorConvertFloat4ToU32(selected ? UiTheme.Accent : UiTheme.Muted), UiTheme.Px(5), ImDrawFlags.None, selected ? UiTheme.Px(2) : 1);
        Vector2 picture = compact ? new(Math.Min(size.X * 0.22f, height), height - UiTheme.Px(12)) : new(size.X - UiTheme.Px(16), height - ImGui.GetTextLineHeight() * 2 - UiTheme.Px(28));
        Picture(draw, card, at + UiTheme.Px(8, 6), picture);
        var text = compact ? at + new Vector2(picture.X + UiTheme.Px(18), (height - ImGui.GetTextLineHeight()) / 2) : at + new Vector2(UiTheme.Px(8), picture.Y + UiTheme.Px(12));
        float width = size.X - (text.X - at.X) - UiTheme.Px(8);
        draw.AddText(text, ImGui.GetColorU32(ImGuiCol.Text), UiTheme.Fit(card.Title, width));
        if (!compact) draw.AddText(text + new Vector2(0, ImGui.GetTextLineHeight()), ImGui.ColorConvertFloat4ToU32(card.Openable ? UiTheme.Muted : UiTheme.Warning), UiTheme.Fit(UiText.Get(card.Detail), width));
        ImGui.PopID(); ImGui.PopID();
    }
}
