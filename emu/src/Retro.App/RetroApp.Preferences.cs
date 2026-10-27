using ImGuiNET;
using Raylib_cs;

namespace Retro.App;

public sealed partial class RetroApp
{
    private bool _preferencesOpen;
    private static readonly string[] JoystickNames = ["Kempston (Alt = disparo)", "Cursor (5 6 7 8 0)", "Sinclair 1 (6 7 8 9 0)"];
    private static readonly string[] C64Ports = ["Puerto 1", "Puerto 2 (la mayoría de los juegos)"];
    private static readonly string[] TextSizeNames = AppPreferences.TextSizes.Select(size => $"{size} %").ToArray();

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
        float field = UiTheme.Px(360), wrap = UiTheme.Px(560);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrap);

        ImGui.SeparatorText("Lectura");
        int text = Array.IndexOf(AppPreferences.TextSizes, _prefs.TextPercent);
        ImGui.SetNextItemWidth(field);
        if (ImGui.Combo("Tamaño del texto", ref text, TextSizeNames, TextSizeNames.Length)) SetTextSize(AppPreferences.TextSizes[text]);
        bool contrast = _prefs.HighContrast;
        if (ImGui.Checkbox("Alto contraste", ref contrast)) SetHighContrast(contrast);
        ImGui.TextDisabled("Texto blanco sobre negro, bordes visibles y el foco del teclado en amarillo.");

        ImGui.SeparatorText("Pantalla");
        bool fullscreen = Raylib.IsWindowFullscreen();
        if (ImGui.Checkbox("Pantalla completa (F11)", ref fullscreen)) ToggleFullscreen();
        bool integer = _prefs.IntegerScale;
        if (ImGui.Checkbox("Escala entera (píxeles nítidos)", ref integer)) { _prefs.IntegerScale = integer; _prefs.Save(); }
        bool fps = _prefs.ShowFps;
        if (ImGui.Checkbox("Mostrar cuadros por segundo en la barra de estado", ref fps)) { _prefs.ShowFps = fps; _prefs.Save(); }

        ImGui.SeparatorText("Controles");
        int joystick = (int)_prefs.SpectrumJoystick;
        ImGui.SetNextItemWidth(field);
        if (ImGui.Combo("Joystick del Spectrum", ref joystick, JoystickNames, JoystickNames.Length)) SetSpectrumJoystick((JoystickMode)joystick);
        int port = _prefs.C64Port - 1;
        ImGui.SetNextItemWidth(field);
        if (ImGui.Combo("Joystick del C64", ref port, C64Ports, C64Ports.Length)) SetC64Port(port + 1);
        ImGui.TextDisabled("Las flechas y el mando manejan el joystick elegido.");

        ImGui.SeparatorText("Cintas del Spectrum");
        bool typeLoad = _prefs.TypeLoad;
        if (ImGui.Checkbox("Escribir LOAD \"\" al abrir cintas", ref typeLoad)) { _prefs.TypeLoad = typeLoad; _prefs.Save(); }
        bool turbo = _prefs.TurboWhileLoading;
        if (ImGui.Checkbox("Acelerar durante la carga", ref turbo)) SetTurboWhileLoading(turbo);

        ImGui.SeparatorText("Rendimiento");
        bool saving = _prefs.PowerSaving;
        if (ImGui.Checkbox("Ahorrar energía cuando no hay partida en curso", ref saving)) { _prefs.PowerSaving = saving; _prefs.Save(); }
        ImGui.TextDisabled($"Con la partida en pausa o en la biblioteca, la ventana se dibuja hasta {FramePacing.Interface} veces por segundo " +
            $"y {FramePacing.Background} sin foco. Un juego en curso usa siempre la frecuencia del monitor. Minimizada, la ventana espera sin dibujar.");
        bool background = _prefs.PauseInBackground;
        if (ImGui.Checkbox("Pausar el juego al pasar a otra ventana", ref background)) { _prefs.PauseInBackground = background; _prefs.Save(); }
        ImGui.TextDisabled("Al volver, sigue solo si la pausa fue automática. Para silenciar sin pausar, usá Sonido.");

        ImGui.Spacing();
        if (ImGui.Button("Restablecer valores"))
        {
            bool wasFullscreen = Raylib.IsWindowFullscreen();
            _prefs.Defaults();
            if (wasFullscreen) Raylib.ToggleFullscreen();
            _input.Joystick = _prefs.SpectrumJoystick; _input.C64Port = _prefs.C64Port;
            _host.TurboWhileLoading = _prefs.TurboWhileLoading;
            _prefs.Save(); _themeChanged = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Sonido...")) _soundWindowOpen = true;
        ImGui.SameLine();
        if (ImGui.Button("Cerrar")) _preferencesOpen = false;
        if (_prefs.Problem is { } problem) ImGui.TextWrapped(problem);
        ImGui.PopTextWrapPos();
        ImGui.End();
    }
}
