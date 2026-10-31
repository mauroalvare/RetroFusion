using Retro.Core;
using Retro.Fusion;

namespace Retro.Compat;

internal static class PreparationBoot
{
    public static bool IsBounded(GameProfile profile, IMachine machine)
    {
        if (profile.Boot == null || profile.Boot.Count is < 1 or > 128) return false;
        var keys = machine.Keys.Select(k => k.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long frames = 0;
        foreach (string step in profile.Boot)
        {
            if (string.IsNullOrWhiteSpace(step)) return false;
            var parts = step.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts is ["load-tape"] && machine.Id == "zx48") continue;
            if (parts is ["wait", var count] && int.TryParse(count, out int wait) && wait is >= 0 and <= 9000) frames += wait;
            else if (parts.Length >= 3 && parts[0] == "press" && int.TryParse(parts[^1], out int hold) && hold is >= 1 and <= 120)
            {
                var pressed = string.Join(' ', parts[1..^1]).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (pressed.Length is < 1 or > 8 || pressed.Any(k => !keys.Contains(k))) return false;
                frames += hold;
            }
            else return false;
            if (frames > 36000) return false;
        }
        return true;
    }
}
