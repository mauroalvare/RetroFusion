using System.Numerics;
using ImGuiNET;

namespace Retro.App;

/// <summary>
/// Choosing a style. Looking is free: only the apply button changes anything. The footer stays visible at
/// any text size, and the preview can fill the window when there is little room to read it.
/// </summary>
internal sealed class InterfacePicker
{
    public bool IsOpen { get; private set; }
    public InterfaceStyle Selected { get; private set; }
    private string? _updatedVersion;
    private bool _focus, _expanded;
    private InterfaceStyle _current;
    internal Action<string, Vector2, Vector2>? ControlBounds { get; set; }

    public void Open(InterfaceStyle current, string? updatedVersion = null)
    { Selected = _current = current; _updatedVersion = updatedVersion; IsOpen = true; _focus = true; _expanded = false; }

    public void Draw(Action<InterfaceStyle, string?> finish)
    {
        if (!IsOpen) return;
        var viewport = ImGui.GetMainViewport();
        var size = Vector2.Min(UiTheme.Px(920, 760), viewport.Size - new Vector2(16));
        ImGui.SetNextWindowPos(viewport.Pos + (viewport.Size - size) / 2, ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(size);
        if (_focus) { ImGui.SetNextWindowFocus(); _focus = false; }
        bool compact = UiTheme.CompactHeight;
        string title = _updatedVersion != null ? UiText.Format($"Interfaz · versión {_updatedVersion} instalada") : "Elegí tu interfaz";
        ImGui.Begin(UiText.Label(title + UiText.Get("###interface-picker")), ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
        string useLabel = compact ? "Usar estilo" : "Usar este estilo", keepLabel = compact ? "Conservar" : "Conservar el actual", largeLabel = _expanded ? "Reducir" : "Ampliar";
        var style = ImGui.GetStyle();
        float buttonsWidth = ImGui.CalcTextSize(UiText.Get(useLabel) + UiText.Get(keepLabel) + UiText.Get(largeLabel)).X + style.FramePadding.X * 6 + style.ItemSpacing.X * 2;
        float footer = ImGui.GetFrameHeightWithSpacing() * (buttonsWidth > ImGui.GetContentRegionAvail().X ? 2 : 1) + UiTheme.Px(10);
        ImGui.BeginChild("choices", new(0, -footer));
        if (_expanded) InterfaceScene.Draw(ImGui.GetWindowDrawList(), Selected, ImGui.GetCursorScreenPos(), ImGui.GetContentRegionAvail());
        else
        {
            DrawChoices(compact);
            var description = InterfaceStyles.Describe(Selected);
            float room = ImGui.GetContentRegionAvail().Y;
            var preview = new Vector2(ImGui.GetContentRegionAvail().X, Math.Clamp(room * 0.66f, UiTheme.Px(90), UiTheme.Px(400)));
            var at = ImGui.GetCursorScreenPos();
            ImGui.Dummy(preview);
            InterfaceScene.Draw(ImGui.GetWindowDrawList(), Selected, at, preview);
            ImGui.TextUnformatted(description.ShortName + UiText.Get(Selected == _current ? " · estilo actual" : " · vista previa, todavía sin aplicar"));
            UiTheme.Hint(description.Description);
            foreach (string feature in description.Features) { ImGui.Bullet(); ImGui.TextWrapped(UiText.Get(feature)); }
            UiTheme.Hint("Podés cambiar de estilo cuando quieras. Tus juegos, partidas y ajustes se conservan.");
        }
        ImGui.EndChild();
        ImGui.Separator();
        if (UiTheme.PrimaryButton(useLabel)) Complete(Selected, finish);
        Track("Usar este estilo");
        UiTheme.SameLineIfFits(keepLabel);
        if (ImGui.Button(UiText.Label(keepLabel))) Complete(null, finish);
        Track("Conservar el actual");
        UiTheme.SameLineIfFits(largeLabel);
        if (ImGui.Button(UiText.Label(largeLabel + UiText.Get("###ampliar")))) _expanded = !_expanded;
        Track("Ampliar");
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false)) { if (_expanded) _expanded = false; else Complete(null, finish); }
        ImGui.End();
    }

    private void DrawChoices(bool compact)
    {
        float width = ImGui.GetContentRegionAvail().X;
        if (compact || width < UiTheme.Px(620))
        {
            ImGui.SetNextItemWidth(-1);
            bool choosing = ImGui.BeginCombo("##style", UiText.Get(InterfaceStyles.Describe(Selected).ShortName));
            Track("Estilo");
            if (choosing)
            {
                foreach (var choice in InterfaceStyles.All)
                {
                    if (ImGui.Selectable(UiText.Label(choice.ShortName + UiText.Get("###style-") + choice.Id), Selected == choice.Style)) Selected = choice.Style;
                    Track("Elegir " + choice.Id);
                }
                ImGui.EndCombo();
            }
            return;
        }
        float card = (width - ImGui.GetStyle().ItemSpacing.X * 2) / 3;
        foreach (var choice in InterfaceStyles.All)
        {
            ImGui.PushID(choice.Id);
            if (choice.Style != InterfaceStyles.All[0].Style) ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, Selected == choice.Style ? ImGui.GetStyle().Colors[(int)ImGuiCol.Header] : UiTheme.Panel);
            var size = new Vector2(card, ImGui.GetTextLineHeight() * 2 + UiTheme.Px(14));
            if (ImGui.Button("##elegir", size)) Selected = choice.Style;
            ImGui.PopStyleColor();
            var at = ImGui.GetItemRectMin(); var draw = ImGui.GetWindowDrawList();
            Track("Elegir " + choice.Id);
            draw.AddText(at + UiTheme.Px(10, 6), ImGui.GetColorU32(ImGuiCol.Text), UiTheme.Fit(choice.ShortName + (choice.Style == _current ? UiText.Get(" · actual") : ""), card - UiTheme.Px(20)));
            draw.AddText(at + new Vector2(UiTheme.Px(10), UiTheme.Px(8) + ImGui.GetTextLineHeight()), ImGui.ColorConvertFloat4ToU32(UiTheme.Muted), UiText.Get(UiTheme.Fit(choice.Handling, card - UiTheme.Px(20))));
            if (Selected == choice.Style) draw.AddRect(at, ImGui.GetItemRectMax(), ImGui.ColorConvertFloat4ToU32(UiTheme.Accent), 0, ImDrawFlags.None, UiTheme.Px(2));
            ImGui.PopID();
        }
        ImGui.Spacing();
    }

    private void Complete(InterfaceStyle? choice, Action<InterfaceStyle, string?> finish)
    {
        IsOpen = false;
        // The caller passes its current style for Keep, so selecting a preview never changes it.
        finish(choice ?? _current, _updatedVersion);
    }
    private void Track(string name) => ControlBounds?.Invoke(name, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
}
