using System.Text.Json;
using Retro.Core;

namespace Retro.Compat;

/// <summary>
/// A candidate the lab found: what it probably is, why, how sure, and how to check it.
/// </summary>
public sealed class LabFinding
{
    /// <summary>lives, timer, position-x, position-y, frame-counter, input-state.</summary>
    public string Kind { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary>stored-value (a byte in RAM that can be read and written), derived (recomputed by the game), entity-table, code.</summary>
    public string Nature { get; set; } = "stored-value";
    public string Hypothesis { get; set; } = "";
    public List<string> Evidence { get; set; } = new();
    /// <summary>0..1.</summary>
    public double Confidence { get; set; }
    public string ConfidenceLabel => Confidence >= 0.75 ? "alta" : Confidence >= 0.45 ? "media" : "baja";
    public string ExpectedEffect { get; set; } = "";
    public string HowToVerify { get; set; } = "";
    public string HowToRevert { get; set; } = "Volver al estado guardado antes del experimento (el archivo original nunca se modifica).";
    /// <summary>Instructions that wrote it during the observation ("$8A3C ×12").</summary>
    public List<string> Writers { get; set; } = new();
}

/// <summary>Everything the lab learnt about one edition of one game.</summary>
public sealed class LabReport
{
    public string Game { get; set; } = "";
    public string Machine { get; set; } = "";
    public string Script { get; set; } = "";
    public string? Sha256 { get; set; }
    public string Generated { get; set; } = "";
    public string Method { get; set; } = "";
    public List<LabFinding> Findings { get; set; } = new();
    public List<string> Notes { get; set; } = new();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, CompatScript.Json) + "\n");
    }
}

/// <summary>
/// Reverse engineering by experiment. From a saved state in the middle of the game:
/// watches RAM while nobody touches the controls (time series), repeats the same frames
/// holding each direction (differences against the idle run reveal positions), classifies
/// the addresses (lives, timers, positions, counters) and records which instructions write
/// the best candidates. Every finding carries its evidence; nothing is assumed.
/// </summary>
public static class MemoryLab
{
    public sealed record Options(int IdleFrames = 3000, int SampleEvery = 5, int HoldShort = 30, int HoldLong = 60);

    /// <summary>RAM worth watching per machine (no ROM, screen bitmap or I/O).</summary>
    public static (int start, int end) RangeFor(string machine) => machine switch
    {
        "zx48" => (0x5B00, 0xFFFF),
        "c64" => (0x0002, 0xCFFF),
        "invaders" => (0x2000, 0x23FF),
        _ => (0x0000, 0xFFFF),
    };

    public static readonly Dictionary<string, (string left, string right, string up, string down)> Directions = new()
    {
        ["zx48"] = ("O", "P", "Q", "A"),
        ["c64"] = ("JOY2 LEFT", "JOY2 RIGHT", "JOY2 UP", "JOY2 DOWN"),
        ["invaders"] = ("P1 LEFT", "P1 RIGHT", "P1 FIRE", "P1 FIRE"),
    };

    public static LabReport Analyze(IMachine m, string machine, Options? options = null, (string left, string right, string up, string down)? directions = null)
    {
        var o = options ?? new Options();
        var (start, end) = RangeFor(machine);
        int len = end - start + 1;
        var report = new LabReport { Machine = machine, Generated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'") };
        byte[] state = m.SaveState();
        var sink = new short[8192];

        byte[] Snapshot()
        {
            var b = new byte[len];
            for (int i = 0; i < len; i++)
                b[i] = m.Memory.Peek((uint)(start + i));
            return b;
        }
        void Run(int frames, string? hold = null)
        {
            if (hold != null) m.SetKey(hold, true);
            for (int i = 0; i < frames; i++)
            {
                m.RunFrame(null);
                m.DrainAudio(sink);
            }
            if (hold != null) m.SetKey(hold, false);
        }

        // ---------------------------------------------------------------- idle time series
        m.LoadState(state);
        var samples = new List<byte[]> { Snapshot() };
        for (int f = 0; f < o.IdleFrames; f += o.SampleEvery)
        {
            Run(o.SampleEvery);
            samples.Add(Snapshot());
        }
        report.Method = $"Serie de {samples.Count} muestras cada {o.SampleEvery} cuadros sin tocar nada ({o.IdleFrames} cuadros); " +
                        $"corridas desde el mismo estado con cada dirección apretada {o.HoldShort} y {o.HoldLong} cuadros, comparadas con la corrida sin entradas; " +
                        $"rango ${start:X4}-${end:X4}.";

        var findings = new List<LabFinding>();
        var deathSamples = new List<int>();
        for (int i = 0; i < len; i++)
        {
            int ups = 0, downs = 0, downsBy1 = 0, changes = 0, incBy1 = 0;
            var downAt = new List<int>();
            for (int k = 1; k < samples.Count; k++)
            {
                int a = samples[k - 1][i], b = samples[k][i];
                if (a == b) continue;
                changes++;
                if (b > a) { ups++; if (b == a + 1) incBy1++; }
                else { downs++; if (b == a - 1) { downsBy1++; downAt.Add(k); } }
            }
            int first = samples[0][i];
            uint addr = (uint)(start + i);
            // lives: a small number that only goes down by one, a few times
            if (first is >= 1 and <= 9 && downs >= 1 && downs <= 3 && downsBy1 == downs && ups == 0)
            {
                findings.Add(new LabFinding
                {
                    Kind = "lives", Address = $"{addr:X4}",
                    Hypothesis = "Vidas (o intentos): valor chico que solo baja de a uno.",
                    Evidence = { $"empieza en {first} y baja de a 1 {downs} vez/veces en {o.IdleFrames} cuadros sin tocar nada (muestras {string.Join(", ", downAt.Select(k => k * o.SampleEvery))})" },
                    Confidence = 0.5,
                    ExpectedEffect = "Subirlo da vidas extra; congelarlo da vidas infinitas.",
                    HowToVerify = $"Escribir {first + 2} en ${addr:X4} y ver si el contador de vidas en pantalla cambia; perder una vida y ver que baja.",
                });
                deathSamples.AddRange(downAt);
            }
            // timers/energy: goes down regularly; may jump back up when a level (re)starts
            else if (downs >= 5 && ups <= Math.Max(1, downs / 5) && downsBy1 >= downs * 0.8)
            {
                findings.Add(new LabFinding
                {
                    Kind = "timer", Address = $"{addr:X4}",
                    Hypothesis = ups == 0 ? "Tiempo, aire o energía que se consume solo." : "Tiempo, aire o energía que se consume solo y se recarga al (re)empezar.",
                    Evidence = { $"baja de a 1 {downsBy1} veces y sube {ups} en {o.IdleFrames} cuadros sin tocar nada (de {first} a {samples[^1][i]})" },
                    Confidence = ups == 0 ? 0.55 : 0.5,
                    ExpectedEffect = "Congelarlo detiene el tiempo o da energía infinita.",
                    HowToVerify = $"Congelar ${addr:X4} en {first} y ver si la barra o el número de tiempo deja de bajar.",
                });
            }
        }

        // a death resets things: the more timers jump up when a lives candidate drops, the surer both are
        foreach (var f in findings.Where(f => f.Kind == "lives"))
        {
            int i = Convert.ToInt32(f.Address, 16) - start;
            var drops = Enumerable.Range(1, samples.Count - 1).Where(k => samples[k][i] < samples[k - 1][i]).ToList();
            int coinciding = findings.Count(t => t.Kind == "timer" &&
                drops.Any(k => Enumerable.Range(Math.Max(1, k - 40), 81).Where(j => j < samples.Count)
                    .Any(j => samples[j][Convert.ToInt32(t.Address, 16) - start] > samples[j - 1][Convert.ToInt32(t.Address, 16) - start])));
            if (coinciding > 0)
            {
                f.Confidence = Math.Min(0.85, f.Confidence + 0.15 + 0.05 * Math.Min(coinciding, 3));
                f.Evidence.Add($"cuando baja, {coinciding} candidato(s) de tiempo se recargan cerca (reinicio de nivel o vida)");
            }
        }
        foreach (var t in findings.Where(f => f.Kind == "timer"))
        {
            int i = Convert.ToInt32(t.Address, 16) - start;
            bool resetsOnDeath = deathSamples.Any(k => Enumerable.Range(Math.Max(1, k - 40), 81).Where(j => j < samples.Count)
                .Any(j => samples[j][i] > samples[j - 1][i]));
            if (resetsOnDeath)
            {
                t.Confidence = Math.Min(0.85, t.Confidence + 0.2);
                t.Evidence.Add("se recarga cuando baja un candidato de vidas");
            }
        }

        // ---------------------------------------------------------------- directions
        var dirs = directions ?? (Directions.TryGetValue(machine, out var d) ? d : default);
        if (dirs.left != null)
        {
            foreach (var (minus, plus, kind) in new[] { (dirs.left, dirs.right, "position-x"), (dirs.up, dirs.down, "position-y") })
            {
                if (minus == plus)
                    continue;
                // score per address: 2 when the two directions move it in opposite senses from the
                // idle run, 1 when only one direction changes it (the player was already moving
                // that way), with the same sense in both hold lengths
                var score = new Dictionary<int, int>();
                var sense = new Dictionary<int, int>();
                var detail = new Dictionary<int, string>();
                foreach (int hold in new[] { o.HoldShort, o.HoldLong })
                {
                    m.LoadState(state); Run(hold); var idle = Snapshot();
                    m.LoadState(state); Run(hold, minus); var a = Snapshot();
                    m.LoadState(state); Run(hold, plus); var b = Snapshot();
                    for (int i = 0; i < len; i++)
                    {
                        int dm = (sbyte)(a[i] - idle[i]), dp = (sbyte)(b[i] - idle[i]), diff = (sbyte)(b[i] - a[i]);
                        bool opposite = dm != 0 && dp != 0 && Math.Sign(dm) != Math.Sign(dp);
                        bool oneSided = diff != 0 && (dm == 0 || dp == 0);
                        if (!opposite && !oneSided)
                            continue;
                        if (sense.TryGetValue(i, out int s) && s != Math.Sign(diff))
                        {
                            score[i] = -99;     // contradicts the other run: not a position
                            continue;
                        }
                        sense[i] = Math.Sign(diff);
                        score[i] = score.GetValueOrDefault(i) + (opposite ? 2 : 1);
                        detail[i] = (detail.GetValueOrDefault(i) ?? "") +
                                    $"{hold} cuadros: {minus} {idle[i]}→{a[i]}, {plus} {idle[i]}→{b[i]}; ";
                    }
                }
                foreach (var (i, v) in score.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(8))
                {
                    uint addr = (uint)(start + i);
                    findings.Add(new LabFinding
                    {
                        Kind = kind, Address = $"{addr:X4}",
                        Hypothesis = kind == "position-x" ? "Posición horizontal del jugador (o algo que se mueve con él)." : "Posición vertical del jugador (o algo que se mueve con él).",
                        Evidence = { $"comparado con la misma corrida sin entradas, {minus} y {plus} lo llevan a valores distintos, en el mismo sentido en cada prueba: {detail[i].TrimEnd(' ', ';')}" },
                        Confidence = v switch { >= 4 => 0.8, 3 => 0.65, 2 => 0.5, _ => 0.35 },
                        ExpectedEffect = "Escribirlo mueve al personaje (si el juego no lo recalcula desde otra variable).",
                        HowToVerify = $"Escribir otro valor en ${addr:X4} y ver si el personaje aparece en otro lugar.",
                        Nature = "stored-value",
                    });
                }
            }
        }

        // a "timer" that the controls also move is the position of a player who keeps walking
        foreach (var p in findings.Where(f => f.Kind.StartsWith("position")).ToList())
        {
            var timer = findings.FirstOrDefault(f => f.Kind == "timer" && f.Address == p.Address);
            if (timer == null)
                continue;
            findings.Remove(timer);
            p.Confidence = Math.Min(0.85, p.Confidence + 0.15);
            p.Evidence.Add($"sin tocar nada también cambia sola ({timer.Evidence[0]}): el personaje sigue moviéndose solo");
        }

        // ---------------------------------------------------------------- who writes the candidates
        if (m is IWriteWatch watchable)
        {
            var watched = findings.Select(f => Convert.ToUInt16(f.Address, 16)).ToHashSet();
            var writers = new Dictionary<ushort, Dictionary<ushort, int>>();
            watchable.WriteWatch = (address, _, pc) =>
            {
                if (!watched.Contains(address)) return;
                if (!writers.TryGetValue(address, out var byPc)) writers[address] = byPc = new();
                byPc[pc] = byPc.GetValueOrDefault(pc) + 1;
            };
            m.LoadState(state);
            Run(Math.Min(o.IdleFrames, 1500));
            if (dirs.right != null)
            {
                m.LoadState(state);
                Run(o.HoldLong, dirs.right);
                if (dirs.down != dirs.right)
                {
                    m.LoadState(state);
                    Run(o.HoldLong, dirs.down);
                }
            }
            watchable.WriteWatch = null;
            int observed = Math.Min(o.IdleFrames, 1500) + o.HoldLong * 2;
            foreach (var f in findings)
            {
                if (!writers.TryGetValue(Convert.ToUInt16(f.Address, 16), out var byPc))
                    continue;
                f.Writers = byPc.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"${kv.Key:X4} ×{kv.Value}").ToList();
                int total = byPc.Values.Sum();
                // rewritten every frame or so: a screen buffer or a value recomputed from another variable
                if (total > observed / 8 && f.Kind.StartsWith("position"))
                {
                    f.Nature = "derived";
                    f.Confidence = Math.Max(0.1, f.Confidence - 0.25);
                    f.Evidence.Add($"se reescribe {total} veces en {observed} cuadros: parece un buffer de pantalla o un valor recalculado, no la variable original");
                }
            }
        }
        m.LoadState(state);

        report.Findings = findings.Where(f => f.Kind != "frame-counter")
            .OrderByDescending(f => f.Confidence).ThenBy(f => f.Kind).ThenBy(f => f.Address).ToList();
        if (!findings.Any(f => f.Kind == "lives"))
            report.Notes.Add("No apareció ningún candidato de vidas: durante la observación no murió el personaje. Probar con más cuadros o un guion que lo haga morir.");
        return report;
    }
}
