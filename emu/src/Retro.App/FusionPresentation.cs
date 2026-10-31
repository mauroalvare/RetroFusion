using Retro.Fusion;

namespace Retro.App;

internal static class FusionPresentation
{
    public static string Status(string hud, IReadOnlyList<FusionEvent> events, long tick)
    {
        string? notice = events.LastOrDefault(e => tick >= e.Tick && tick - e.Tick <= 180)?.Text;
        return string.IsNullOrWhiteSpace(notice) ? hud : string.IsNullOrWhiteSpace(hud) ? notice : notice + " · " + hud;
    }
}
