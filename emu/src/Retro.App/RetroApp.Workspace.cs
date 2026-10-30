using System.Numerics;
using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private bool ShowEmptyPreview => (_creator.IsOpen || _studio.IsOpen) && _host.LoadedMedia == null && _host.Fusion == null;

    private void DrawEmptyPreview()
    {
        if (!ShowEmptyPreview || _home.IsOpen || _debugMode) return;
        var vp = ImGui.GetMainViewport();
        float reserve = Math.Max(_creator.RightReserve(vp.WorkSize.X), _studio.RightReserve(vp.WorkSize.X));
        float width = vp.WorkSize.X - reserve - UiTheme.Px(44);
        if (width < UiTheme.Px(200)) return;
        ImGui.SetNextWindowPos(vp.WorkPos + UiTheme.Px(20, 20));
        ImGui.SetNextWindowSize(new Vector2(width, vp.WorkSize.Y - UiTheme.StatusBarHeight - UiTheme.Px(40)));
        ImGui.Begin("##vista-previa-vacia", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.SetCursorPosY(Math.Max(UiTheme.Px(30), ImGui.GetWindowHeight() * 0.36f));
        UiTheme.Heading("Tu próxima partida", "La vista previa aparecerá acá cuando el juego esté listo para probar.");
        UiTheme.Hint("Podés empezar con una idea, combinar originales o editar una plantilla.");
        ImGui.End();
    }
}
