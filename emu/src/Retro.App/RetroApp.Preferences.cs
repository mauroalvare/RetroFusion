using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace Retro.App;

public sealed partial class RetroApp
{
    private bool _preferencesOpen;
    private string _preferencesGroup = "";
    private string _preferencesSearch = "";
    private bool ConfigurationOpen => _preferencesOpen || _soundWindowOpen || _newsOpen || _paletteEditorOpen || _requirementsOpen || AiConnectionWindow.IsOpen;
    private static readonly string[] JoystickNames = ["Kempston (Alt = disparo)", "Cursor (5 6 7 8 0)", "Sinclair 1 (6 7 8 9 0)"];
    private static readonly string[] C64Ports = ["Puerto 1", "Puerto 2 (la mayoría de los juegos)"];

    private void SetTextSize(int percent) { _prefs.TextPercent = percent; _prefs.Save(); _themeChanged = true; }
    private void SetHighContrast(bool on) { _prefs.HighContrast = on; _prefs.Save(); _themeChanged = true; }
    private void SetSpectrumJoystick(JoystickMode mode) { _input.Joystick = _prefs.SpectrumJoystick = mode; _prefs.Save(); }
    private void SetC64Port(int port) { _input.C64Port = _prefs.C64Port = port; _prefs.Save(); }
    private void SetTurboWhileLoading(bool on) { _host.TurboWhileLoading = _prefs.TurboWhileLoading = on; _prefs.Save(); }

    /// <summary>Settings by group, with the fixed footer always in reach. The list comes entirely from the shared catalog.</summary>
    private void DrawPreferencesWindow()
    {
        if (!_preferencesOpen) return;
        var available = WorkspaceLayout.Available();
        // In a short window the settings come first: title and margins would leave them no room at large text sizes.
        bool compact = UiTheme.CompactHeight;
        if (compact)
        {
            ImGui.SetNextWindowPos(WorkspaceLayout.Origin); ImGui.SetNextWindowSize(WorkspaceLayout.Size);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, UiTheme.Px(8, 6));
        }
        else
        {
            ImGui.SetNextWindowSize(Vector2.Min(UiTheme.Px(820, 620), available), ImGuiCond.Appearing);
            ImGui.SetNextWindowSizeConstraints(Vector2.Min(UiTheme.Px(360, 240), available), available);
        }
        bool shown = compact ? ImGui.Begin(UiText.Label("Preferencias###preferencias"), WorkspaceLayout.EditorFlags | ImGuiWindowFlags.NoScrollbar)
            : ImGui.Begin(UiText.Label("Preferencias###preferencias"), ref _preferencesOpen, ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar);
        if (compact) ImGui.PopStyleVar();
        if (!shown) { ImGui.End(); return; }
        if (!compact) WorkspaceLayout.KeepOnScreen();
        string[] groups = Options.Select(option => option.Group).Distinct().ToArray();
        if (!groups.Contains(_preferencesGroup)) _preferencesGroup = groups[0];
        float footer = ImGui.GetFrameHeightWithSpacing() + (compact ? UiTheme.Px(6) : ImGui.GetTextLineHeightWithSpacing() + UiTheme.Px(14));
        if (ImGui.GetContentRegionAvail().X < UiTheme.Px(620))
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##grupo-preferencias", UiText.Get(_preferencesGroup)))
            {
                foreach (string group in groups) if (ImGui.Selectable(UiText.Label(group), group == _preferencesGroup)) _preferencesGroup = group;
                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.BeginChild("grupos-preferencias", new(UiTheme.Px(190), -footer));
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##buscar-ajuste", UiText.Get("Buscar ajuste"), ref _preferencesSearch, 60);
            ImGui.Spacing();
            foreach (string group in groups)
                if (ImGui.Selectable(UiText.Label(group), group == _preferencesGroup && _preferencesSearch.Length == 0)) { _preferencesGroup = group; _preferencesSearch = ""; }
            ImGui.EndChild();
            ImGui.SameLine();
        }
        ImGui.BeginChild("ajustes", new(0, -footer));
        ImGui.PushTextWrapPos(0);
        if (_preferencesSearch.Trim().Length > 0) DrawSharedOptions(search: _preferencesSearch);
        else DrawSharedOptions(_preferencesGroup);
        if (_preferencesGroup == UiText.Get("Interfaz") || _preferencesGroup == UiText.Get("Consola") || CommandLine.Fold(_preferencesSearch).Contains("palet") || CommandLine.Fold(_preferencesSearch).Contains("color"))
        {
            if (ImGui.Button(UiText.Label("Personalizar colores…"))) OpenPaletteEditor();
            InterfaceControlBounds?.Invoke("Personalizar paleta", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }
        ImGui.PopTextWrapPos();
        ImGui.EndChild();
        ImGui.Separator();
        // Settings apply as they change: the footer says what happened to them instead of asking for a save.
        string? problem = _prefs.Problem ?? _sound.Problem;
        if (!compact || problem != null) UiTheme.Hint(UiTheme.Fit(problem ?? UiText.Get("Los cambios se guardan al instante. Los colores pertenecen a la interfaz elegida."), ImGui.GetContentRegionAvail().X));
        if (ImGui.Button(UiText.Label("Restablecer todo"))) ResetPreferences();
        InterfaceControlBounds?.Invoke("Restablecer todo", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (ImGui.IsItemHovered()) UiTheme.Tooltip("Vuelve a los valores iniciales de pantalla, sonido, controles y consola. No cambia tus juegos, partidas ni fichas.");
        if (_prefs.CanRecover)
        {
            UiTheme.SameLineIfFits("Recuperar archivo");
            if (ImGui.Button(UiText.Label("Recuperar archivo"))) _prefs.Recover();
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Conserva el archivo anterior y guarda los valores de esta sesión en uno nuevo.");
        }
        if (_prefs.RecoveryPath is { } backup)
        { UiTheme.SameLineIfFits("Respaldo"); ImGui.TextDisabled(UiText.Get("Respaldo")); if (ImGui.IsItemHovered()) UiTheme.Tooltip(backup); }
        float close = ImGui.CalcTextSize(UiText.Get("Cerrar")).X + ImGui.GetStyle().FramePadding.X * 2;
        float right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - close;
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), right));
        if (ImGui.Button(UiText.Label("Cerrar"))) _preferencesOpen = false;
        InterfaceControlBounds?.Invoke("Cerrar preferencias", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.End();
    }

    private void ResetPreferences()
    {
        SetInterface(InterfaceStyle.Retro);
        bool wasFullscreen = Raylib.IsWindowFullscreen();
        _prefs.Defaults();
        UiText.Language = _prefs.Language;
        _commandCatalog = null; _optionCatalog = null; _terminalCatalog = null;
        if (wasFullscreen) Raylib.ToggleFullscreen();
        _input.Joystick = _prefs.SpectrumJoystick; _input.C64Port = _prefs.C64Port;
        _host.TurboWhileLoading = _prefs.TurboWhileLoading;
        _sound.Defaults(); _sound.Save();
        _prefs.Save(); _themeChanged = true;
    }
}
