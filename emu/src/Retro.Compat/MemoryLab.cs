using System.Text.Json;
using Retro.Core;

namespace Retro.Compat;

/// <summary>
/// A candidate the lab found: what it probably is, why, how sure, and how to check it.
/// </summary>
public sealed class LabFinding
{
    /// <summary>lives, timer, score, level, ammo, item, position-x, position-y, entity-table.</summary>
    public string Kind { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary>stored-value (a byte in RAM that can be read and written), derived (recomputed by the game), entity-table, code.</summary>
    public string Nature { get; set; } = "stored-value";
    /// <summary>How the value is kept, as in the profiles: uint8, uint16 (little endian), bcd (little endian), ascii or digits (most significant first).</summary>
    public string Encoding { get; set; } = "uint8";
    public int Bytes { get; set; } = 1;
    /// <summary>hypothesis until an experiment checks it: verified, refuted or inconclusive.</summary>
    public string Status { get; set; } = "hypothesis";
    /// <summary>The experiment that changed the status, with what it showed.</summary>
    public LabVerification? Verification { get; set; }
    /// <summary>entity-table: distance between records, number of records and what each moving field does.</summary>
    public int? Stride { get; set; }
    public int? Count { get; set; }
    public List<string>? Fields { get; set; }
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
    /// <summary>Frame of the observation where it changed the telling way (a life lost): verification runs past it.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? EventFrame { get; set; }
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
    /// <summary>Controls whose effect was measured, rather than inferred from the button name.</summary>
    public Dictionary<string, string> CheckedControls { get; set; } = new();

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
        "nes" => (0x0000, 0x07FF),
        _ => (0x0000, 0xFFFF),
    };

    public static readonly Dictionary<string, (string left, string right, string up, string down)> Directions = new()
    {
        ["zx48"] = ("O", "P", "Q", "A"),
        ["c64"] = ("JOY2 LEFT", "JOY2 RIGHT", "JOY2 UP", "JOY2 DOWN"),
        ["invaders"] = ("P1 LEFT", "P1 RIGHT", "P1 FIRE", "P1 FIRE"),
        ["nes"] = ("P1 LEFT", "P1 RIGHT", "P1 UP", "P1 DOWN"),
    };

    public static LabReport Analyze(IMachine m, string machine, Options? options = null, (string left, string right, string up, string down)? directions = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
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
                ct.ThrowIfCancellationRequested();
                m.RunFrame(null);
                m.DrainAudio(sink);
            }
            if (hold != null) m.SetKey(hold, false);
        }

        try
        {
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
                        EventFrame = downAt[0] * o.SampleEvery,
                    });
                    deathSamples.AddRange(downAt);
                }
                // timers/energy: goes down regularly; may jump back up when a level (re)starts, a few
                // times at most (a value that refills all the time is a cyclic counter)
                else if (downs >= 5 && ups <= Math.Min(3, Math.Max(1, downs / 5)) && downsBy1 >= downs * 0.8)
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

            // ---------------------------------------------------------------- entity tables
            var tables = EntityTables(samples, start);
            findings.AddRange(tables);

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
                            int dm = (sbyte)(a[i] - idle[i]), dp = (sbyte)(b[i] - idle[i]), diff = dp - dm;   // both sides can go far: no wrap
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

            if (machine == "nes" && m is Retro.Systems.Nes.Nes nes)
            {
                m.LoadState(state);
                NesActionProbe.Observe(nes, findings, report, ct);
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

            // a position inside a table is one entity among several; outside one, a loose variable
            foreach (var p in findings.Where(f => f.Kind.StartsWith("position")))
            {
                int a = Convert.ToInt32(p.Address, 16);
                var t = tables.FirstOrDefault(t => a >= Convert.ToInt32(t.Address, 16) && a < Convert.ToInt32(t.Address, 16) + t.Stride!.Value * t.Count!.Value);
                if (t == null)
                    p.Evidence.Add("variable suelta: no está dentro de ninguna tabla de entidades que se mueva sola");
                else
                {
                    int offset = a - Convert.ToInt32(t.Address, 16);
                    p.Evidence.Add($"está dentro de la tabla de entidades ${t.Address} (registro {offset / t.Stride!.Value}, campo +{offset % t.Stride.Value})");
                }
            }

            // ---------------------------------------------------------------- who writes the candidates
            if (m is IWriteWatch watchable)
            {
                var watched = findings.Select(f => Convert.ToUInt16(f.Address, 16)).ToHashSet();
                var writers = new Dictionary<ushort, Dictionary<ushort, int>>();
                using var writeObservation = CpuWriteObservers.Subscribe(watchable, (address, _, pc) =>
                {
                    if (!watched.Contains(address)) return;
                    if (!writers.TryGetValue(address, out var byPc)) writers[address] = byPc = new();
                    byPc[pc] = byPc.GetValueOrDefault(pc) + 1;
                });
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
                writeObservation.Dispose();
                int observed = Math.Min(o.IdleFrames, 1500) + o.HoldLong * 2;
                foreach (var f in findings)
                {
                    if (!writers.TryGetValue(Convert.ToUInt16(f.Address, 16), out var byPc))
                        continue;
                    f.Writers = byPc.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"${kv.Key:X4} ×{kv.Value}").ToList();
                    int total = byPc.Values.Sum();
                    // written from many places: the stack or a work buffer
                    if (byPc.Count >= 5 && total > observed && f.Kind != "entity-table")
                    {
                        f.Nature = "derived";
                        f.Confidence = Math.Max(0.05, f.Confidence - 0.3);
                        f.Evidence.Add($"lo escriben {byPc.Count} rutinas distintas {total} veces: parece la pila o un buffer de trabajo");
                    }
                    // rewritten every frame or so: a screen buffer or a value recomputed from another variable
                    else if (total > observed / 8 && f.Kind.StartsWith("position"))
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
        finally { m.LoadState(state); m.ReleaseAllKeys(); }
    }

    /// <summary>
    /// Lives in a series of samples: a small number (1-9) that only goes down by one, a few
    /// times. Used on the idle observation and on runs where somebody plays.
    /// </summary>
    public static List<LabFinding> LivesIn(IReadOnlyList<byte[]> samples, int start, int sampleEvery, string what)
    {
        var found = new List<LabFinding>();
        for (int i = 0; i < samples[0].Length; i++)
        {
            int first = samples[0][i], downs = 0, other = 0;
            var downAt = new List<int>();
            for (int k = 1; k < samples.Count; k++)
            {
                int a = samples[k - 1][i], b = samples[k][i];
                if (a == b) continue;
                if (b == a - 1) { downs++; downAt.Add(k); }
                else other++;
            }
            if (first is < 1 or > 9 || downs is < 1 or > 3 || other > 0)
                continue;
            found.Add(new LabFinding
            {
                Kind = "lives", Address = $"{start + i:X4}",
                Hypothesis = "Vidas (o intentos): valor chico que solo baja de a uno.",
                Evidence = { $"empieza en {first} y baja de a 1 {downs} vez/veces {what} (cuadros {string.Join(", ", downAt.Select(k => k * sampleEvery))})" },
                Confidence = 0.5,
                ExpectedEffect = "Subirlo da vidas extra; congelarlo da vidas infinitas.",
                HowToVerify = $"Escribir {first + 2} en ${start + i:X4} y ver si el contador de vidas en pantalla cambia; perder una vida y ver que baja.",
                EventFrame = downAt[0] * sampleEvery,
            });
        }
        return found;
    }

    /// <summary>
    /// Records of the same size that move on their own (enemies, shots, platforms): at least
    /// 3 addresses at a fixed distance whose values change in small steps many times, each
    /// at its own pace. Fields of the same records are merged into one table. Blocks where
    /// every byte moves (screen or sound buffers) are left out.
    /// </summary>
    public static List<LabFinding> EntityTables(IReadOnlyList<byte[]> samples, int start)
    {
        int len = samples[0].Length, n = samples.Count;
        var moving = new bool[len];
        var changes = new int[len];
        var meanStep = new double[len];
        var reverses = new bool[len];
        for (int i = 0; i < len; i++)
        {
            int ch = 0, small = 0, lastSign = 0;
            double sum = 0;
            for (int k = 1; k < n; k++)
            {
                int d = (sbyte)(samples[k][i] - samples[k - 1][i]);
                if (d == 0)
                    continue;
                ch++;
                sum += Math.Abs(d);
                if (Math.Abs(d) <= 16)
                    small++;
                if (lastSign != 0 && Math.Sign(d) != lastSign && Math.Abs(d) <= 16)
                    reverses[i] = true;
                lastSign = Math.Sign(d);
            }
            changes[i] = ch;
            meanStep[i] = ch == 0 ? 0 : sum / ch;
            moving[i] = ch >= Math.Max(4, n / 25) && small >= ch * 0.7;
        }
        // runs of consecutive moving bytes: buffers, not records
        var dense = new bool[len];
        for (int i = 0, run = 0; i < len; i++)
        {
            run = moving[i] ? run + 1 : 0;
            if (run >= 6)
                for (int j = i - run + 1; j <= i; j++)
                    dense[j] = true;
        }
        bool Member(int a) => a >= 0 && a < len && moving[a] && !dense[a];
        bool SameSequence(int a, int b)
        {
            for (int k = 0; k < n; k++)
                if (samples[k][a] != samples[k][b])
                    return false;
            return true;
        }
        var groups = new List<(int first, int stride, int count)>();
        for (int s = 2; s <= 32; s++)
            for (int i = 0; i < len; i++)
            {
                if (!Member(i) || Member(i - s))
                    continue;
                int c = 1;
                while (Member(i + c * s))
                    c++;
                if (c < 3)
                    continue;
                // copies of one value (the same sequence) are one variable, not entities
                if (Enumerable.Range(1, c - 1).All(r => SameSequence(i, i + r * s)))
                    continue;
                // similar behaviour: the busiest record changes at most 4 times as often as the quietest
                var ch = Enumerable.Range(0, c).Select(r => changes[i + r * s]).ToList();
                if (ch.Max() > ch.Min() * 4)
                    continue;
                groups.Add((i, s, c));
            }
        // a group that is every k-th record of a group with a smaller stride is that same table
        groups.RemoveAll(g => groups.Any(h => h.stride < g.stride && g.stride % h.stride == 0
            && g.first >= h.first && (g.first - h.first) % h.stride == 0
            && g.first + (g.count - 1) * g.stride <= h.first + (h.count - 1) * h.stride));
        // fields of the same records (same stride, starting within one record) form one table
        var candidates = new List<List<(int first, int stride, int count)>>();
        foreach (var byStride in groups.GroupBy(g => g.stride))
        {
            var pending = byStride.OrderBy(g => g.first).ToList();
            while (pending.Count > 0)
            {
                var seed = pending[0];
                var fields = pending.Where(g => g.first < seed.first + seed.stride).ToList();
                pending.RemoveAll(fields.Contains);
                candidates.Add(fields);
            }
        }
        // three moving bytes at some distance happen by chance in a busy RAM: a table needs two
        // moving fields per record or four records; then the best ones keep their addresses
        var taken = new HashSet<int>();
        var tables = new List<LabFinding>();
        foreach (var fields in candidates
                     .Where(c => c.Count >= 2 || c[0].count >= 4)
                     .OrderByDescending(c => c.Count * c.Max(f => f.count)).ThenBy(c => c[0].stride))
        {
            var members = fields.SelectMany(f => Enumerable.Range(0, f.count).Select(r => f.first + r * f.stride)).ToList();
            if (members.Any(taken.Contains))
                continue;
            taken.UnionWith(members);
            var seed = fields[0];
            {
                int stride = seed.stride, count = fields.Max(f => f.count);
                var lines = new List<string>();
                foreach (var f in fields)
                {
                    var column = Enumerable.Range(0, f.count).Select(r => f.first + r * stride).ToList();
                    bool back = column.Count(a => reverses[a]) * 2 >= column.Count;
                    lines.Add($"+{f.first - seed.first}: cambia sola en pasos de {column.Average(a => meanStep[a]):F1} de media en {f.count} registros" +
                              (back ? ", va y vuelve (posición o dirección)" : ""));
                }
                var records = Enumerable.Range(0, seed.count).Select(r => seed.first + r * stride).ToList();
                tables.Add(new LabFinding
                {
                    Kind = "entity-table", Address = $"{start + seed.first:X4}", Nature = "entity-table",
                    Stride = stride, Count = count, Fields = lines,
                    Hypothesis = $"Tabla de {count} entidades de {stride} bytes (enemigos, disparos u objetos que se mueven solos).",
                    Evidence =
                    {
                        $"sin tocar nada, {string.Join(", ", records.Select(a => $"${start + a:X4}"))} cambian cada uno a su ritmo " +
                        $"({string.Join(" / ", records.Select(a => changes[a]))} cambios en {n} muestras)",
                    },
                    Confidence = Math.Min(0.8, 0.4 + 0.1 * Math.Min(count, 4)),
                    ExpectedEffect = "Leerla da las posiciones de los enemigos; escribir un campo mueve o desactiva una sola entidad.",
                    HowToVerify = $"Escribir otro valor en el campo +0 de un registro (${start + seed.first:X4}) y ver si se mueve una sola entidad en pantalla.",
                });
            }
        }
        return tables;
    }
}

/// <summary>Spanish words for the lab's values (reports, CLI, app).</summary>
public static class LabReportText
{
    public static string Status(string status) => status switch
    {
        "verified" => "verificada",
        "refuted" => "descartada",
        "inconclusive" => "sin efecto",
        _ => "hipótesis",
    };
}
