using Retro.Core;

namespace Retro.Compat;

/// <summary>A way of playing a machine: the keys of the four directions and of fire.</summary>
public sealed record ControlSet(string Name, string Left, string Right, string Up, string Down, string Fire)
{
    public IEnumerable<string> Keys => new[] { Left, Right, Up, Down, Fire }.Distinct();
}

/// <summary>Whether the player is in control, with which controls and what showed it.</summary>
public sealed record PlayCheck(bool Playing, ControlSet? Controls, string Evidence);

/// <summary>
/// Tells a game in progress from a title, a menu or a demo: from the same state, holding
/// left and holding right (or up and down) must take some RAM address in opposite ways,
/// further the longer they are held, and leave different pictures. A demo moves things by
/// itself in both runs alike; a title ignores the directions.
/// </summary>
public static class PlayDetector
{
    public static readonly Dictionary<string, ControlSet[]> Sets = new()
    {
        ["c64"] = new[]
        {
            new ControlSet("joystick en el puerto 2", "JOY2 LEFT", "JOY2 RIGHT", "JOY2 UP", "JOY2 DOWN", "JOY2 FIRE"),
            new ControlSet("joystick en el puerto 1", "JOY1 LEFT", "JOY1 RIGHT", "JOY1 UP", "JOY1 DOWN", "JOY1 FIRE"),
        },
        ["zx48"] = new[]
        {
            new ControlSet("joystick Kempston", "KEMPSTON LEFT", "KEMPSTON RIGHT", "KEMPSTON UP", "KEMPSTON DOWN", "KEMPSTON FIRE"),
            new ControlSet("teclas Q A O P y espacio", "O", "P", "Q", "A", "SPACE"),
            new ControlSet("cursores 5 6 7 8 y 0", "5", "8", "7", "6", "0"),
            new ControlSet("joystick Sinclair 6 7 8 9 y 0", "6", "7", "9", "8", "0"),
        },
        ["nes"] = new[] { new ControlSet("mando 1", "P1 LEFT", "P1 RIGHT", "P1 UP", "P1 DOWN", "P1 A") },
        ["invaders"] = new[] { new ControlSet("jugador 1", "P1 LEFT", "P1 RIGHT", "P1 FIRE", "P1 FIRE", "P1 FIRE") },
    };

    public static ControlSet[] SetsFor(string machine) => Sets.TryGetValue(machine, out var s) ? s : Array.Empty<ControlSet>();

    /// <summary>Checks the current state of <paramref name="m"/> (left as it was found).</summary>
    public static PlayCheck Detect(IMachine m, string machine, ControlSet? only = null)
    {
        var (start, end) = MemoryLab.RangeFor(machine);
        byte[] state = m.SaveState();
        var sink = new short[8192];
        // How far each byte travelled, followed every few frames: a coordinate that moves 2
        // points a frame goes 160 in 80 frames, which a single byte difference would read as -96
        (int[] travel, int[] picture) Run(int frames, string? hold)
        {
            m.LoadState(state);
            m.ReleaseAllKeys();
            if (hold != null) m.SetKey(hold, true);
            byte[] last = LabExperiments.Snapshot(m, start, end);
            var travel = new int[last.Length];
            for (int i = 1; i <= frames; i++)
            {
                m.RunFrame(null);
                m.DrainAudio(sink);
                if (i % 8 != 0 && i != frames)
                    continue;
                byte[] now = LabExperiments.Snapshot(m, start, end);
                for (int j = 0; j < now.Length; j++)
                    travel[j] += (sbyte)(now[j] - last[j]);
                last = now;
            }
            if (hold != null) m.SetKey(hold, false);
            return (travel, (int[])m.Video.Pixels.Clone());
        }
        try
        {
            foreach (var set in only != null ? new[] { only } : SetsFor(machine))
                foreach (var (minus, plus) in new[] { (set.Left, set.Right), (set.Up, set.Down) })
                {
                    if (minus == plus)
                        continue;
                    var sense = new Dictionary<int, int>();
                    var reach = new Dictionary<int, int>();
                    var score = new Dictionary<int, int>();
                    bool picturesDiffer = false;
                    foreach (int hold in new[] { 40, 80 })
                    {
                        var idle = Run(hold, null);
                        var a = Run(hold, minus);
                        var b = Run(hold, plus);
                        if (hold == 80)
                            picturesDiffer = !a.picture.AsSpan().SequenceEqual(b.picture);
                        for (int i = 0; i < idle.travel.Length; i++)
                        {
                            int dm = a.travel[i] - idle.travel[i], dp = b.travel[i] - idle.travel[i], diff = dp - dm;
                            bool opposite = dm != 0 && dp != 0 && Math.Sign(dm) != Math.Sign(dp);
                            bool oneSided = diff != 0 && (dm == 0 || dp == 0);
                            if (!opposite && !oneSided)
                                continue;
                            if (sense.TryGetValue(i, out int s) && s != Math.Sign(diff))
                            {
                                score[i] = -99;
                                continue;
                            }
                            // held twice as long, a walking character gets at least as far
                            if (reach.TryGetValue(i, out int r) && Math.Abs(diff) < r)
                            {
                                score[i] = -99;
                                continue;
                            }
                            sense[i] = Math.Sign(diff);
                            reach[i] = Math.Abs(diff);
                            score[i] = score.GetValueOrDefault(i) + (opposite ? 2 : 1);
                        }
                    }
                    var moved = score.Where(kv => kv.Value >= 3).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
                    if (moved.Count > 0 && picturesDiffer)
                        return new PlayCheck(true, set,
                            $"con {set.Name}, {minus} y {plus} llevan {string.Join(", ", moved.Take(4).Select(i => $"${start + i:X4}"))} en sentidos opuestos " +
                            "(más lejos cuanto más tiempo se mantienen) y cambian la pantalla");
                }
            return new PlayCheck(false, null, "ningún control mueve algo de forma sostenida: todavía no es la partida (título, menú, demo o carga)");
        }
        finally
        {
            m.LoadState(state);
            m.ReleaseAllKeys();
        }
    }
}
