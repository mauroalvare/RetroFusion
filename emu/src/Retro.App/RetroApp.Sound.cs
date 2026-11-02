using System.Numerics;
using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private readonly SoundSettings _sound;
    private bool _soundWindowOpen;

    private void DrawSoundWindow()
    {
        if (!_soundWindowOpen) return;
        ImGui.SetNextWindowSize(UiTheme.Px(460, 0), ImGuiCond.FirstUseEver);
        WorkspaceLayout.LimitNextWindow();
        if (!ImGui.Begin(UiText.Label("Sonido"), ref _soundWindowOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking))
        { ImGui.End(); return; }
        WorkspaceLayout.KeepOnScreen();
        if (_host.SoundDeviceMissing)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Warning);
            ImGui.TextWrapped(UiText.Get(_audioDisabled ? "Audio desactivado para este inicio (--no-audio). Podés activarlo buscando la salida desde Sonido."
                : "No se encontró una salida de audio. Los juegos funcionan sin sonido y a su velocidad normal."));
            ImGui.PopStyleColor();
            if (!_audioDisabled) UiTheme.Hint(_audioRetried ? "Sigue sin aparecer. Revisá que Windows tenga un dispositivo de salida activo y que ningún programa lo use en modo exclusivo."
                : "Conectá unos auriculares o parlantes, o activá la salida en la configuración de sonido del equipo.");
            if (ImGui.Button(UiText.Label("Buscar la salida de audio"))) RetryAudio();
            ImGui.Spacing();
        }
        DrawSharedOptions("Sonido");
        ImGui.Spacing();
        if (ImGui.Button(UiText.Label("Restablecer valores"))) { _sound.Defaults(); _sound.Save(); }
        if (s_forceMute) ImGui.TextDisabled(UiText.Get("Este inicio está silenciado."));
        if (_sound.Problem is { } problem) ImGui.TextWrapped(UiText.Get(problem));
        if (_sound.CanRecover)
        {
            UiTheme.Hint("Podés conservar el archivo anterior y guardar los valores de esta sesión en uno nuevo.");
            if (ImGui.Button(UiText.Label("Recuperar archivo"))) _sound.Recover();
        }
        if (_sound.RecoveryPath is { } backup)
        { UiTheme.Hint("Se conservó un respaldo del archivo anterior."); if (ImGui.IsItemHovered()) UiTheme.Tooltip(backup); }
        if (ImGui.Button(UiText.Label("Cerrar"))) _soundWindowOpen = false;
        ImGui.End();
    }
}
