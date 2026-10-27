using System.Numerics;
using ImGuiNET;

namespace Retro.App;

/// <summary>Keep floating workspaces reachable after moving or resizing the main window.</summary>
internal static class WorkspaceLayout
{
    /// <summary>Space between the menu bar and the status bar, with a small margin.</summary>
    public static Vector2 Available() =>
        Vector2.Max(new Vector2(100, 80), ImGui.GetMainViewport().WorkSize - new Vector2(16, 16 + UiTheme.StatusBarHeight));

    /// <summary>Call before Begin of an auto-sized window: its content scrolls instead of covering the status bar.</summary>
    public static void LimitNextWindow() => ImGui.SetNextWindowSizeConstraints(Vector2.Zero, Available());

    public static void KeepOnScreen()
    {
        if (ImGui.IsWindowDocked()) return;
        var viewport = ImGui.GetMainViewport();
        Vector2 minimum = viewport.WorkPos + new Vector2(8, 8);
        Vector2 available = Available();
        Vector2 size = Vector2.Min(ImGui.GetWindowSize(), available);
        if (size != ImGui.GetWindowSize()) ImGui.SetWindowSize(size);
        Vector2 maximum = minimum + available - size;
        Vector2 position = Vector2.Clamp(ImGui.GetWindowPos(), minimum, maximum);
        if (position != ImGui.GetWindowPos()) ImGui.SetWindowPos(position);
    }
}
