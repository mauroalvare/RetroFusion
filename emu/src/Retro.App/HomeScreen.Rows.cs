using System.Numerics;
using ImGuiNET;
using Retro.Core;

namespace Retro.App;

/// <summary>The game list: uniform rows drawn only while visible, and a detail panel that keeps the selection in view.</summary>
public sealed partial class HomeScreen
{
    private string? _selectedKey;
    private bool _scrollToSelection, _shortActions;
    private string PlayLabel => _shortActions ? "▶" : "Jugar";
    private string CardLabel => _shortActions ? "i" : "Ficha";

    private float RowHeight => _compactRows
        ? ImGui.GetFrameHeight() + UiTheme.Px(6)
        : Math.Max(UiTheme.Px(46), ImGui.GetTextLineHeight() * 2 + UiTheme.Px(10));

    private void DrawGameList(IReadOnlyList<LibraryCard> cards)
    {
        float available = ImGui.GetContentRegionAvail().X;
        bool detail = available >= UiTheme.Px(860) && !UiTheme.CompactHeight;
        float detailWidth = detail ? Math.Clamp(available * 0.30f, UiTheme.Px(270), UiTheme.Px(350)) : 0;
        // A choice that was filtered out is replaced, so the panel never describes something that is not on the list.
        var selected = cards.FirstOrDefault(card => card.Key == _selectedKey);
        if (selected == null && cards.Count > 0) { selected = cards[0]; _selectedKey = selected.Key; }
        if (detail) ImGui.BeginChild("lista-juegos", new(available - detailWidth - ImGui.GetStyle().ItemSpacing.X, 0));
        DrawRows(cards);
        if (detail)
        {
            ImGui.EndChild(); ImGui.SameLine();
            if (selected != null) DrawGameDetail(selected);
        }
    }

    private unsafe void DrawRows(IReadOnlyList<LibraryCard> cards)
    {
        _compactRows = ImGui.GetContentRegionAvail().X < UiTheme.Px(640) || UiTheme.CompactHeight;
        bool showSystem = ImGui.GetContentRegionAvail().X >= UiTheme.Px(520);
        // Only a list narrower than the buttons themselves leaves the star to Ficha.
        bool showFavorite = ImGui.GetContentRegionAvail().X >= UiTheme.Px(200);
        // In a very narrow list the actions shrink to signs, explained on hover, so that a title can still be read.
        _shortActions = ImGui.GetContentRegionAvail().X < UiTheme.Px(340);
        float actions = ImGui.CalcTextSize(UiText.Get(PlayLabel)).X + ImGui.CalcTextSize(UiText.Get(CardLabel)).X + ImGui.GetStyle().FramePadding.X * 4 + ImGui.GetStyle().ItemSpacing.X + UiTheme.Px(8);
        int columns = 2 + (showSystem ? 1 : 0) + (showFavorite ? 1 : 0);
        if (!ImGui.BeginTable("juegos", columns, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.NoPadOuterX, new(0, Math.Max(RowHeight, ImGui.GetContentRegionAvail().Y)))) return;
        ImGui.TableSetupScrollFreeze(0, _compactRows ? 0 : 1);
        ImGui.TableSetupColumn(UiText.Get("Juego"), ImGuiTableColumnFlags.WidthStretch);
        if (showSystem) ImGui.TableSetupColumn(UiText.Get("Sistema"), ImGuiTableColumnFlags.WidthFixed, UiTheme.Px(_compactRows ? 112 : 136));
        if (showFavorite) ImGui.TableSetupColumn("##favorito", ImGuiTableColumnFlags.WidthFixed, UiTheme.Px(30));
        ImGui.TableSetupColumn("##acciones", ImGuiTableColumnFlags.WidthFixed, actions);
        if (!_compactRows) ImGui.TableHeadersRow();
        var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        try
        {
            clipper.Begin(cards.Count, RowHeight + ImGui.GetStyle().CellPadding.Y * 2);
            if (_scrollToSelection)
            {
                int index = cards.ToList().FindIndex(card => card.Key == _selectedKey);
                if (index >= 0) clipper.IncludeItemByIndex(index);
            }
            while (clipper.Step())
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++) Row(cards[i], showSystem, showFavorite);
        }
        finally { ImGuiNative.ImGuiListClipper_destroy(clipper.NativePtr); }
        ImGui.EndTable();
    }

    private void Row(LibraryCard card, bool showSystem, bool showFavorite)
    {
        float height = RowHeight;
        ImGui.TableNextRow(ImGuiTableRowFlags.None, height);
        ImGui.PushID(card.Key);
        ImGui.TableNextColumn();
        var start = ImGui.GetCursorScreenPos();
        bool selected = _selectedKey == card.Key;
        if (ImGui.Selectable("##fila", selected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap | ImGuiSelectableFlags.AllowDoubleClick, new(0, height)))
        {
            bool enter = ImGui.IsItemFocused() && (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false));
            _selectedKey = card.Key;
            if ((ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) || enter && selected) && card.Openable && !library.Scanning) { Close(); card.Play(); }
        }
        if (ImGui.IsItemFocused() && !selected) _selectedKey = card.Key;
        if (selected && _scrollToSelection) { ImGui.SetScrollHereY(); _scrollToSelection = false; }
        if (ImGui.IsItemHovered()) UiTheme.Tooltip(card.Title + (string.IsNullOrWhiteSpace(card.Hint) ? "" : "\n" + card.Hint));
        LibraryControlBounds?.Invoke("Fila:" + card.Key, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var size = _compactRows ? new Vector2(0, 0) : new Vector2(height * 1.35f, height - UiTheme.Px(6));
        var draw = ImGui.GetWindowDrawList();
        float textX = start.X;
        if (size.X > 0)
        {
            var picture = start + new Vector2(0, UiTheme.Px(3));
            Picture(draw, card, picture, size);
            textX += size.X + UiTheme.Px(10);
        }
        float width = Math.Max(20, start.X + ImGui.GetContentRegionAvail().X - textX);
        var info = EffectiveInfo(card.Key);
        string meta = !card.Openable ? UiText.Get(card.Hint ?? "Formato sin confirmar")
            : string.Join(" · ", info.Genres.Select(UiText.Get).Concat(info.ReleaseYear != null ? [info.YearLabel] : []));
        uint ink = ImGui.GetColorU32(ImGuiCol.Text), muted = ImGui.ColorConvertFloat4ToU32(card.Openable ? UiTheme.Muted : UiTheme.Warning);
        float line = ImGui.GetTextLineHeight();
        if (_compactRows || meta.Length == 0)
            draw.AddText(new(textX, start.Y + (height - line) / 2), ink, UiTheme.Fit(card.Title, width));
        else
        {
            draw.AddText(new(textX, start.Y + height / 2 - line - UiTheme.Px(1)), ink, UiTheme.Fit(card.Title, width));
            draw.AddText(new(textX, start.Y + height / 2 + UiTheme.Px(1)), muted, UiTheme.Fit(meta, width));
        }
        if (showSystem)
        {
            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (height - line) / 2);
            ImGui.TextUnformatted(UiText.Get(UiTheme.Fit(card.Detail, ImGui.GetContentRegionAvail().X)));
        }
        ImGui.TableNextColumn();
        float buttonY = ImGui.GetCursorPosY() + (height - ImGui.GetFrameHeight()) / 2;
        if (showFavorite)
        {
            ImGui.SetCursorPosY(buttonY);
            FavoriteButton(card.Key, new(UiTheme.Px(28), ImGui.GetFrameHeight()));
            ImGui.TableNextColumn();
        }
        ImGui.SetCursorPosY(buttonY);
        ImGui.BeginDisabled(!card.Openable || library.Scanning);
        if (UiTheme.PrimaryButton(PlayLabel + UiText.Get("###jugar"))) { Close(); card.Play(); }
        LibraryControlBounds?.Invoke(UiText.Get("Jugar:") + card.Key, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiTheme.Tooltip(card.Openable ? "Jugar" : card.Hint ?? "Este formato todavía no se puede abrir.");
        ImGui.SameLine();
        if (ImGui.Button(UiText.Label(CardLabel + UiText.Get("###ficha")))) OpenMetadata(card.Key);
        LibraryControlBounds?.Invoke(UiText.Get("Ficha:") + card.Key, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (_shortActions && ImGui.IsItemHovered()) UiTheme.Tooltip("Ficha");
        ImGui.PopID();
    }

    private void Picture(ImDrawListPtr draw, LibraryCard card, Vector2 at, Vector2 size)
    {
        draw.AddRectFilled(at, at + size, ImGui.GetColorU32(ImGuiCol.FrameBg), UiTheme.Px(2));
        if (_thumbnails.TryGet(card.Key, card.Picture, out var texture))
        {
            float scale = Math.Min(size.X / texture.Width, size.Y / texture.Height);
            var fitted = new Vector2(texture.Width, texture.Height) * scale;
            var corner = at + (size - fitted) / 2;
            draw.AddImage((nint)texture.Id, corner, corner + fitted);
        }
        else HardwareArt.Draw(draw, card.Machine, at + UiTheme.Px(2, 1), size - UiTheme.Px(4, 2));
    }

    private void FavoriteButton(string key, Vector2 size)
    {
        bool favorite = _activity.IsFavorite(key);
        if (ImGui.Button("##favorito", size)) _activity.SetFavorite(key, !favorite);
        LibraryControlBounds?.Invoke(UiText.Get("Favorito:") + key, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        Span<Vector2> star = stackalloc Vector2[10];
        for (int i = 0; i < star.Length; i++)
        {
            float angle = -MathF.PI / 2 + i * MathF.PI / 5, radius = UiTheme.Px((i & 1) == 0 ? 9 : 4);
            star[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        var draw = ImGui.GetWindowDrawList();
        uint color = favorite ? ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0.78f, 0.25f, 1)) : ImGui.GetColorU32(ImGuiCol.TextDisabled);
        for (int i = 0; i < star.Length; i++) draw.AddLine(star[i], star[(i + 1) % star.Length], color, favorite ? 2 : 1);
        if (ImGui.IsItemHovered()) UiTheme.Tooltip(favorite ? "Quitar de favoritos" : "Agregar a favoritos");
    }

    /// <summary>What the selection is, where its data came from and what can be done with it. Unknown stays unknown.</summary>
    private void DrawGameDetail(LibraryCard card)
    {
        UiTheme.BeginPanel("detalle-juego", new(0, 0));
        float width = ImGui.GetContentRegionAvail().X;
        var at = ImGui.GetCursorScreenPos();
        var picture = new Vector2(width, Math.Min(width * 0.62f, UiTheme.Px(170)));
        Picture(ImGui.GetWindowDrawList(), card, at, picture);
        ImGui.Dummy(picture);
        using (UiTheme.Display()) { ImGui.PushTextWrapPos(0); ImGui.TextUnformatted(card.Title); ImGui.PopTextWrapPos(); }
        var info = EffectiveInfo(card.Key);
        UiTheme.Hint(card.Detail);
        ImGui.BeginDisabled(!card.Openable || library.Scanning);
        if (UiTheme.PrimaryButton("Jugar##detalle", new(-1, 0))) { Close(); card.Play(); }
        LibraryControlBounds?.Invoke("Jugar detalle", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.EndDisabled();
        if (ImGui.Button(UiText.Label("Ficha##detalle"), new(width - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X, 0))) OpenMetadata(card.Key);
        LibraryControlBounds?.Invoke("Ficha detalle", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine();
        ImGui.PushID("detalle"); FavoriteButton(card.Key, new(ImGui.GetFrameHeight(), ImGui.GetFrameHeight())); ImGui.PopID();
        ImGui.Spacing();
        Fact("AÑO", info.ReleaseYear != null ? info.YearLabel : "Desconocido");
        Fact("GÉNERO", info.Genres.Count > 0 ? string.Join(", ", info.Genres) : "Sin clasificar");
        if (_identification.Get(card.Key) is { } detected)
        {
            if (detected.Publisher != null) Fact("EDITORA", detected.Publisher);
            Fact("ORIGEN DE LOS DATOS", detected.Source);
        }
        Fact("ESTADO", card.Openable ? card.Preparation ?? "Disponible" : card.Hint ?? "No se puede abrir");
        if (_activity.LastPlayed(card.Key) is { } played) Fact("ÚLTIMA PARTIDA", played.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
        UiTheme.EndPanel();

        static void Fact(string label, string value)
        {
            using (UiTheme.Label()) ImGui.TextDisabled(UiText.Get(label));
            ImGui.PushTextWrapPos(0); ImGui.TextUnformatted(UiText.Get(value)); ImGui.PopTextWrapPos();
        }
    }
}
