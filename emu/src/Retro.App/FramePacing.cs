namespace Retro.App;

/// <summary>
/// Host frame rate. A running game keeps the monitor rate (the emulation and its audio are paced per
/// host frame); without one, the interface is drawn less often so an idle window spends little.
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
