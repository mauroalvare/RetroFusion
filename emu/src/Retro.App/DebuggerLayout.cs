using System.Numerics;

namespace Retro.App;

/// <summary>
/// Placement of the debugger panels between the menu and the status bar: screen and memory on the
/// left, CPU and stack (with the tape below it on the Spectrum) in the middle, disassembly and the
/// test program on the right. Fractions of the area keep every panel visible at any window or text size.
/// </summary>
public static class DebuggerLayout
{
    public enum Panel { Screen, Cpu, Disassembly, Memory, Stack, Tape, TestProgram }
    public const float Gap = 6;

    public static (Vector2 Position, Vector2 Size) Place(Panel panel, Vector2 origin, Vector2 area, bool tape)
    {
        float left = MathF.Round(area.X * 0.46f), middle = MathF.Round(area.X * 0.24f), right = area.X - left - middle;
        float top = MathF.Round(area.Y * 0.6f), bottom = area.Y - top, stack = tape ? MathF.Round(bottom / 2) : bottom;
        float x1 = origin.X + left, x2 = x1 + middle, y1 = origin.Y + top;
        var (x, y, w, h) = panel switch
        {
            Panel.Screen => (origin.X, origin.Y, left, top),
            Panel.Memory => (origin.X, y1, left, bottom),
            Panel.Cpu => (x1, origin.Y, middle, top),
            Panel.Stack => (x1, y1, middle, stack),
            Panel.Tape => (x1, y1 + stack, middle, bottom - stack),
            Panel.Disassembly => (x2, origin.Y, right, top),
            _ => (x2, y1, right, bottom),
        };
        return (new Vector2(x + Gap / 2, y + Gap / 2), new Vector2(Math.Max(1, w - Gap), Math.Max(1, h - Gap)));
    }
}
