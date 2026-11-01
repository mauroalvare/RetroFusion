using System.Numerics;
using ImGuiNET;

namespace Retro.App;

/// <summary>A bounded, scrollable choice with a fixed footer, including at 200% text.</summary>
internal sealed class InterfacePicker
{
    public bool IsOpen { get; private set; }
    public InterfaceStyle Selected { get; private set; }
    private string? _updatedVersion;
    private bool _focus;
    private InterfaceStyle _current;
    internal Action<string, Vector2, Vector2>? ControlBounds { get; set; }

    public void Open(InterfaceStyle current, string? updatedVersion = null)
    { Selected = _current = current; _updatedVersion = updatedVersion; IsOpen = true; _focus = true; }

    public void Draw(Action<InterfaceStyle, string?> finish)
    {
        if (!IsOpen) return;
        var viewport = ImGui.GetMainViewport();
        var size = Vector2.Min(UiTheme.Px(740, 680), viewport.Size - new Vector2(16));
        ImGui.SetNextWindowPos(viewport.Pos + (viewport.Size - size) / 2, ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(size);
        if (_focus) { ImGui.SetNextWindowFocus(); _focus = false; }
        bool compact = UiTheme.CompactHeight;
        string title = compact && _updatedVersion != null ? $"Interfaz · versión {_updatedVersion}" : "Elegí tu interfaz";
        ImGui.Begin(title + "###interface-picker", ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
        string useLabel = compact ? "Usar estilo" : "Usar este estilo", keepLabel = compact ? "Conservar" : "Conservar el actual";
        float buttonsWidth = ImGui.CalcTextSize(useLabel).X + ImGui.CalcTextSize(keepLabel).X + ImGui.GetStyle().FramePadding.X * 4 + ImGui.GetStyle().ItemSpacing.X;
        float footer = ImGui.GetFrameHeightWithSpacing() * (buttonsWidth > ImGui.GetContentRegionAvail().X ? 2 : 1) + UiTheme.Px(12);
        if (_updatedVersion != null && !compact) UiTheme.Hint($"Versión {_updatedVersion} instalada.");
        ImGui.SetNextItemWidth(-1);
        bool choosing = ImGui.BeginCombo("##style", InterfaceStyles.Describe(Selected).Name.Split('·')[0].Trim());
        Track("Estilo");
        if (choosing)
        {
            foreach (var choice in InterfaceStyles.All)
            {
                if (ImGui.Selectable(choice.Name.Split('·')[0].Trim() + "###style-" + choice.Id, Selected == choice.Style)) Selected = choice.Style;
                Track("Elegir " + choice.Id);
            }
            ImGui.EndCombo();
        }
        ImGui.BeginChild("choices", new(0, -footer));
        DrawPreview(Selected, new(ImGui.GetContentRegionAvail().X, Math.Clamp(ImGui.GetContentRegionAvail().Y * 0.65f, 70, UiTheme.Px(168))));
        UiTheme.Section(InterfaceStyles.Describe(Selected).Name);
        UiTheme.Hint(InterfaceStyles.Describe(Selected).Description);
        UiTheme.Hint("Vista previa ilustrativa. Podés cambiar de estilo en cualquier momento; tus datos se conservan.");
        ImGui.EndChild();
        ImGui.Separator();
        if (UiTheme.PrimaryButton(useLabel)) Complete(Selected, finish);
        Track("Usar este estilo");
        UiTheme.SameLineIfFits(keepLabel);
        if (ImGui.Button(keepLabel)) Complete(null, finish);
        Track("Conservar el actual");
        ImGui.End();
    }

    private void Complete(InterfaceStyle? choice, Action<InterfaceStyle, string?> finish)
    {
        IsOpen = false;
        // The caller passes its current style for Keep, so selecting a preview never changes it.
        finish(choice ?? _current, _updatedVersion);
    }
    private void Track(string name) => ControlBounds?.Invoke(name, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

    internal static void DrawPreview(InterfaceStyle style, Vector2 size)
    {
        var at = ImGui.GetCursorScreenPos(); var draw = ImGui.GetWindowDrawList();
        ImGui.Dummy(size);
        uint bg = style == InterfaceStyle.Retro ? 0xFF172019u : 0xFF050905u;
        uint ink = style == InterfaceStyle.Retro ? 0xFFD0E0E6u : 0xFF81EF9Bu;
        uint accent = style == InterfaceStyle.Retro ? 0xFF5BABDFu : 0xFF81EF9Bu;
        draw.AddRectFilled(at, at + size, bg, 2);
        draw.AddRect(at, at + size, accent, 2);
        float unit = Math.Min(UiTheme.Scale, Math.Min(size.X / 400, size.Y / 168));
        Vector2 P(float x, float y) => at + new Vector2(x, y) * unit;
        float font = Math.Min(ImGui.GetFontSize(), 18 * unit);
        void Text(float x, float y, string value, uint color) => draw.AddText(ImGui.GetFont(), font, P(x, y), color, value);
        Text(12, 10, style == InterfaceStyle.Retro ? "RETROEMU / ARCHIVO" : "RETROEMU SYSTEM / READY", accent);
        draw.AddLine(P(12, 34), at + new Vector2(size.X - 12 * unit, 34 * unit), accent);
        if (style == InterfaceStyle.Hardcore)
        {
            Text(12, 46, "retro:/biblioteca> juegos", ink);
            Text(12, 72, "01  Demo de plataformas · 1986", ink);
            Text(12, 98, "retro:/biblioteca> jugar 1", ink);
            Text(12, 128, "Cargando máquina… _", accent);
        }
        else
        {
            Text(12, 46, "BIBLIOTECA  /  HERRAMIENTAS", ink);
            Text(12, 78, "Demo de plataformas · 1986", ink);
            draw.AddRectFilled(P(12, 115), P(94, 147), accent, 1);
            Text(23, 119, "Jugar", bg);
            Text(112, 119, "Ficha   /   Filtros", ink);
        }
    }
}
