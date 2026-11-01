using System.Numerics;
using ImGuiNET;

namespace Retro.App;

/// <summary>Keep floating workspaces reachable after moving or resizing the main window.</summary>
internal static class WorkspaceLayout
{
    public static float MenuHeight { get; set; } = 28;
    public static float NavigationHeight { get; set; }
    public static float FooterHeight { get; set; }
    public static Vector2 Origin => ImGui.GetMainViewport().Pos + new Vector2(0, MenuHeight + NavigationHeight);
    public static Vector2 Size => Vector2.Max(new(100, 80), ImGui.GetMainViewport().Size - new Vector2(0, MenuHeight + NavigationHeight + UiTheme.StatusBarHeight + FooterHeight));
    public const ImGuiWindowFlags EditorFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings;

    public static void NextEditor()
    {
        float margin=Size.Y<UiTheme.Px(360)?6:12;
        ImGui.SetNextWindowPos(Origin + UiTheme.Px(margin,margin));
        ImGui.SetNextWindowSize(Vector2.Max(UiTheme.Px(100, 80), Size - UiTheme.Px(margin*2,margin*2)));
        ImGui.SetNextWindowDockID(0);
    }

    /// <summary>Space between the menu bar and the status bar, with a small margin.</summary>
    public static Vector2 Available() =>
        Vector2.Max(new Vector2(100, 80), Size - new Vector2(16, 16));

    /// <summary>Call before Begin of an auto-sized window: its content scrolls instead of covering the status bar.</summary>
    public static void LimitNextWindow() => ImGui.SetNextWindowSizeConstraints(Vector2.Zero, Available());

    /// <summary>A larger font also widens an existing panel. Small screens use the full available width.</summary>
    public static void ToolWindowConstraints() => ImGui.SetNextWindowSizeConstraints(
        Vector2.Min(UiTheme.Px(430, 240), Available()), Available());

    public static void KeepOnScreen(bool modal = false)
    {
        if (ImGui.IsWindowDocked()) return;
        var viewport=ImGui.GetMainViewport();
        Vector2 minimum = (modal?viewport.Pos:Origin) + new Vector2(8, 8);
        Vector2 available = modal?Vector2.Max(new(100,80),viewport.Size-new Vector2(16,16+UiTheme.StatusBarHeight)):Available();
        Vector2 size = Vector2.Min(ImGui.GetWindowSize(), available);
        if (size != ImGui.GetWindowSize()) ImGui.SetWindowSize(size);
        Vector2 maximum = minimum + available - size;
        Vector2 position = Vector2.Clamp(ImGui.GetWindowPos(), minimum, maximum);
        if (position != ImGui.GetWindowPos()) ImGui.SetWindowPos(position);
    }
}
