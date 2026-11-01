using ImGuiNET;
using Raylib_cs;

namespace Retro.App;

public sealed partial class RetroApp
{
    private bool _preferencesOpen;
    private bool ConfigurationOpen => _preferencesOpen || _soundWindowOpen || AiConnectionWindow.IsOpen;
    private static readonly string[] JoystickNames = ["Kempston (Alt = disparo)", "Cursor (5 6 7 8 0)", "Sinclair 1 (6 7 8 9 0)"];
    private static readonly string[] C64Ports = ["Puerto 1", "Puerto 2 (la mayoría de los juegos)"];

    private void SetTextSize(int percent) { _prefs.TextPercent = percent; _prefs.Save(); _themeChanged = true; }
    private void SetHighContrast(bool on) { _prefs.HighContrast = on; _prefs.Save(); _themeChanged = true; }
    private void SetSpectrumJoystick(JoystickMode mode) { _input.Joystick = _prefs.SpectrumJoystick = mode; _prefs.Save(); }
    private void SetC64Port(int port) { _input.C64Port = _prefs.C64Port = port; _prefs.Save(); }
    private void SetTurboWhileLoading(bool on) { _host.TurboWhileLoading = _prefs.TurboWhileLoading = on; _prefs.Save(); }

    private void DrawPreferencesWindow()
    {
        if (!_preferencesOpen) return;
        ImGui.SetNextWindowSize(UiTheme.Px(620, 0), ImGuiCond.FirstUseEver);
        WorkspaceLayout.LimitNextWindow();
        if (!ImGui.Begin("Preferencias", ref _preferencesOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking))
        { ImGui.End(); return; }
        WorkspaceLayout.KeepOnScreen();
        ImGui.PushTextWrapPos(0);

        DrawSharedOptions();

        ImGui.Spacing();
        if (ImGui.Button("Restablecer valores"))
        {
            SetInterface(InterfaceStyle.Retro);
            bool wasFullscreen = Raylib.IsWindowFullscreen();
            _prefs.Defaults();
            if (wasFullscreen) Raylib.ToggleFullscreen();
            _input.Joystick = _prefs.SpectrumJoystick; _input.C64Port = _prefs.C64Port;
            _host.TurboWhileLoading = _prefs.TurboWhileLoading;
            _sound.Defaults(); _sound.Save();
            _prefs.Save(); _themeChanged = true;
        }
        UiTheme.SameLineIfFits("Sonido…");
        if (ImGui.Button("Sonido...")) _soundWindowOpen = true;
        UiTheme.SameLineIfFits("Cerrar");
        if (ImGui.Button("Cerrar")) _preferencesOpen = false;
        if (_prefs.Problem is { } problem) ImGui.TextWrapped(problem);
        if (_prefs.CanRecover)
        {
            UiTheme.Hint("Podés conservar el archivo anterior y guardar los valores de esta sesión en uno nuevo.");
            if (ImGui.Button("Recuperar archivo")) _prefs.Recover();
        }
        if (_prefs.RecoveryPath is { } backup)
        { UiTheme.Hint("Se conservó un respaldo del archivo anterior."); if (ImGui.IsItemHovered()) UiTheme.Tooltip(backup); }
        ImGui.PopTextWrapPos();
        ImGui.End();
    }
}
