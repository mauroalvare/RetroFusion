using System.Numerics;
using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private readonly SoundSettings _sound;
    private bool _soundWindowOpen;
    private static readonly string[] AudioMargins = ["Corto", "Equilibrado", "Amplio"];

    private void DrawSoundWindow()
    {
        if (!_soundWindowOpen) return;
        ImGui.SetNextWindowSize(new Vector2(460, 0), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Sonido", ref _soundWindowOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking))
        { ImGui.End(); return; }
        WorkspaceLayout.KeepOnScreen();
        bool changed = false;
        int volume = _sound.VolumePercent;
        ImGui.SetNextItemWidth(280);
        if (ImGui.SliderInt("Volumen", ref volume, 0, 100, "%d %%")) { _sound.VolumePercent = volume; changed = true; }
        bool muted = _sound.Muted;
        if (ImGui.Checkbox("Silenciar", ref muted)) { _sound.Muted = muted; changed = true; }
        bool background = _sound.MuteInBackground;
        if (ImGui.Checkbox("Silenciar al perder el foco", ref background)) { _sound.MuteInBackground = background; changed = true; }
        ImGui.Spacing();
        int margin = _sound.BufferFrames == 1 ? 0 : _sound.BufferFrames == 4 ? 2 : 1;
        ImGui.SetNextItemWidth(230);
        if (ImGui.Combo("Margen de audio", ref margin, AudioMargins, AudioMargins.Length))
        { _sound.BufferFrames = margin == 0 ? 1 : margin == 2 ? 4 : 2; changed = true; }
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 420);
        ImGui.TextDisabled(margin switch {
            0 => "Menor demora. Si escuchás cortes, probá Equilibrado.",
            2 => "Más margen ante pausas del equipo; puede aumentar la demora.",
            _ => "Equilibra respuesta y estabilidad del sonido."
        });
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (ImGui.Button("Restablecer valores")) { _sound.Defaults(); changed = true; }
        if (changed) _sound.Save();
        if (s_forceMute) ImGui.TextDisabled("Este inicio está silenciado.");
        if (_sound.Problem is { } problem) ImGui.TextWrapped(problem);
        ImGui.End();
    }
}
