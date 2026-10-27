namespace Retro.App;

/// <summary>
/// Host frame rate. A running game keeps the monitor rate (the emulation and its audio are paced per
/// host frame); without one, the interface is drawn less often so an idle window spends little.
/// While minimized, raylib itself waits for window events between frames (no FLAG_WINDOW_ALWAYS_RUN),
/// so the Minimized rate only applies to the frames those events wake up.
/// </summary>
public static class FramePacing
{
    public const int Interface = 60, Background = 15, Minimized = 5;

    public static int TargetFps(int monitorRate, bool saving, bool emulating, bool focused, bool minimized)
    {
        int full = Math.Max(Interface, monitorRate);
        if (!saving || emulating) return full;
        if (minimized) return Minimized;
        return focused ? Interface : Background;
    }
}
