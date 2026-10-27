using Retro.Core;

namespace Retro.Compat;

/// <summary>
/// Presses keys frame by frame with SetKey. The machines' own key queues are not part of
/// their saved states, so experiments that reload a state many times use this instead.
/// </summary>
public sealed class LabKeys
{
    private readonly IMachine _m;
    private readonly Dictionary<string, int> _releaseAt = new();

    public LabKeys(IMachine m)
    {
        _m = m;
        m.ReleaseAllKeys();
    }

    /// <summary>Holds the keys ("SPACE", "JOY2 FIRE", "CAPS SHIFT+5", spaces may be written _) from frame <paramref name="now"/> for <paramref name="hold"/> frames.</summary>
    public void Press(string keys, int hold, int now)
    {
        foreach (var k in keys.Replace('_', ' ').Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            _m.SetKey(k, true);
            _releaseAt[k] = Math.Max(_releaseAt.GetValueOrDefault(k), now + hold);
        }
    }

    /// <summary>Call at the start of every frame: releases the keys whose time is up.</summary>
    public void Tick(int now)
    {
        foreach (var (k, at) in _releaseAt.Where(kv => kv.Value <= now).ToList())
        {
            _m.SetKey(k, false);
            _releaseAt.Remove(k);
        }
    }

    public void ReleaseAll()
    {
        _releaseAt.Clear();
        _m.ReleaseAllKeys();
    }
}

/// <summary>
/// An experiment of a script: from the state at the end of the script, inputs that should
/// change one kind of variable (shooting adds points, finishing a level, picking up an
/// object). The lab compares the run with the same frames without inputs.
/// </summary>
public sealed class LabExperiment
{
    /// <summary>score, level, ammo, item or change (anything that the inputs change).</summary>
    public string Kind { get; set; } = "score";
    public string Description { get; set; } = "";
    public int Frames { get; set; } = 1200;
    /// <summary>When above 0, the steps start again every this many frames.</summary>
    public int RepeatEvery { get; set; }
    /// <summary>Key presses, with frames counted from the start of the experiment.</summary>
    public List<CompatStep> Steps { get; set; } = new();

    /// <summary>The presses that start on frame <paramref name="f"/> (repeated steps included).</summary>
    public IEnumerable<CompatStep> DueAt(int f) =>
        Steps.Where(s => f >= s.Frame && (RepeatEvery > 0 ? (f - s.Frame) % RepeatEvery == 0 : f == s.Frame));
}

/// <summary>
/// Finds the variables an experiment changes: from the same state, the experiment's inputs
/// and no inputs at all, sampled every few frames; what changes only with the inputs and
/// the way it changes (a number that only goes up, a counter that goes down by one...)
/// tells what it is. Scores are tried as ASCII digits, one digit per byte, packed BCD and
/// binary, over several bytes.
/// </summary>
public static class LabExperiments
{
    /// <summary>Plays the experiment and the same frames without inputs; RAM samples of both. The machine is left at its starting state.</summary>
    public static (List<byte[]> played, List<byte[]> idle) Run(IMachine m, string machine, LabExperiment e, int sampleEvery = 5, CancellationToken ct = default)
    {
        var (start, end) = MemoryLab.RangeFor(machine);
        byte[] state = m.SaveState();
        var sink = new short[8192];
        List<byte[]> Play(bool withInputs)
        {
            m.LoadState(state);
            var keys = new LabKeys(m);
            var samples = new List<byte[]> { Snapshot(m, start, end) };
            for (int f = 0; f < e.Frames; f++)
            {
                ct.ThrowIfCancellationRequested();
                keys.Tick(f);
                if (withInputs)
                    foreach (var s in e.DueAt(f))
                        keys.Press(s.Keys, s.Hold, f);
                m.RunFrame(null);
                m.DrainAudio(sink);
                if ((f + 1) % sampleEvery == 0)
                    samples.Add(Snapshot(m, start, end));
            }
            keys.ReleaseAll();
            return samples;
        }
        try { return (Play(true), Play(false)); }
        finally { m.LoadState(state); m.ReleaseAllKeys(); }
    }

    internal static byte[] Snapshot(IMachine m, int start, int end)
    {
        var b = new byte[end - start + 1];
        for (int i = 0; i < b.Length; i++)
            b[i] = m.Memory.Peek((uint)(start + i));
        return b;
    }

    public static List<LabFinding> Analyze(IMachine m, string machine, LabExperiment e, int sampleEvery = 5, CancellationToken ct = default)
    {
        var (played, idle) = Run(m, machine, e, sampleEvery, ct);
        int start = MemoryLab.RangeFor(machine).start;
        string what = string.IsNullOrWhiteSpace(e.Description) ? "con las entradas del experimento" : $"\"{e.Description}\"";
        var found = e.Kind switch
        {
            "score" => Scores(played, idle, start, what),
            "level" => Counters(played, idle, start, what, "level"),
            "ammo" => Counters(played, idle, start, what, "ammo"),
            "item" => Counters(played, idle, start, what, "item"),
            _ => Counters(played, idle, start, what, "change"),
        };
        return found.OrderByDescending(f => f.Confidence).ThenBy(f => f.Address).ToList();
    }

    private static bool[] ChangedIn(List<byte[]> samples)
    {
        var changed = new bool[samples[0].Length];
        for (int i = 0; i < changed.Length; i++)
            for (int k = 1; k < samples.Count && !changed[i]; k++)
                changed[i] = samples[k][i] != samples[0][i];
        return changed;
    }

    private static bool ValidBcd(byte b) => (b & 0x0F) <= 9 && (b >> 4) <= 9;

    /// <summary>Decoded value of a candidate in one sample, or null when the bytes are not valid for the encoding.</summary>
    private static long? Decode(byte[] sample, int addr, int bytes, string encoding, bool bigEndian)
    {
        long v = 0;
        for (int k = 0; k < bytes; k++)
        {
            // k = 0 is the most significant byte
            int i = bigEndian ? addr + k : addr + bytes - 1 - k;
            byte b = sample[i];
            switch (encoding)
            {
                case "ascii":
                    if (b is < (byte)'0' or > (byte)'9') return null;
                    v = v * 10 + (b - '0');
                    break;
                case "digits":
                    if (b > 9) return null;
                    v = v * 10 + b;
                    break;
                case "bcd":
                    if (!ValidBcd(b)) return null;
                    v = v * 100 + (b >> 4) * 10 + (b & 0x0F);
                    break;
                default:
                    v = v * 256 + b;
                    break;
            }
        }
        return v;
    }

    private static List<long>? Series(List<byte[]> samples, int addr, int bytes, string encoding, bool bigEndian)
    {
        var list = new List<long>(samples.Count);
        foreach (var s in samples)
        {
            if (Decode(s, addr, bytes, encoding, bigEndian) is not long v)
                return null;
            list.Add(v);
        }
        return list;
    }

    private static bool NonDecreasing(List<long> v) => v.Zip(v.Skip(1)).All(p => p.Second >= p.First) && v[^1] > v[0];

    /// <summary>
    /// Numbers that go up with the inputs and stay still without them. For each group of
    /// bytes that changed only in the played run, the encodings that read as a number that
    /// never goes down; the most specific one wins (ASCII, BCD, one digit per byte, binary).
    /// </summary>
    public static List<LabFinding> Scores(List<byte[]> played, List<byte[]> idle, int start, string what)
    {
        int len = played[0].Length;
        var changedP = ChangedIn(played);
        var changedI = ChangedIn(idle);
        bool Seed(int i) => i >= 0 && i < len && changedP[i] && !changedI[i];
        var used = new bool[len];
        var found = new List<LabFinding>();

        LabFinding Make(int addr, int bytes, string encoding, List<long> values, double confidence, string note)
        {
            var steps = values.Zip(values.Skip(1)).Where(p => p.Second != p.First).Select(p => p.Second - p.First).ToList();
            bool round = steps.Count > 0 && steps.All(d => d % 5 == 0);
            if (round) confidence += 0.1;
            if (steps.Count >= 3) confidence += 0.05;
            // one big jump looks more like a copy (the high score at the end of a game) than the score itself
            if (steps.Count == 1) confidence -= 0.1;
            var f = new LabFinding
            {
                Kind = "score", Address = $"{start + addr:X4}", Encoding = encoding, Bytes = bytes,
                Hypothesis = $"Puntaje: número que sube al jugar y no cambia sin tocar nada ({EncodingName(encoding, bytes)}).",
                Evidence =
                {
                    $"en la corrida {what} pasa de {values[0]} a {values[^1]} en {steps.Count} subida(s) " +
                    $"({string.Join(", ", steps.Take(12).Select(d => "+" + d))}{(steps.Count > 12 ? "..." : "")}); sin tocar nada no cambia",
                },
                Confidence = Math.Min(0.85, confidence),
                ExpectedEffect = "Sumarle da puntos; el juego muestra y sigue sumando sobre el valor escrito.",
                HowToVerify = $"Escribir otro puntaje en ${start + addr:X4} ({EncodingName(encoding, bytes)}) y ver que la pantalla lo muestre y que siga sumando desde ahí.",
            };
            if (note.Length > 0)
                f.Evidence.Add(note);
            // one or two steps of one: a counter of objects, enemies or tries, maybe shown on screen
            if (!round && steps.Count < 3 && steps.All(d => d == 1))
            {
                f.Kind = "counter";
                f.Hypothesis = $"Contador que sube al jugar: objetos, enemigos o puntos ({EncodingName(encoding, bytes)}).";
            }
            return f;
        }

        // ASCII digits (and the C64 screen codes for digits, which are the same bytes)
        for (int i = 0; i < len; i++)
        {
            if (!Seed(i) || used[i] || played.Any(s => s[i] is < (byte)'0' or > (byte)'9'))
                continue;
            bool Digit(int j) => j >= 0 && j < len && played.All(s => s[j] is >= (byte)'0' and <= (byte)'9') && idle.All(s => s[j] is >= (byte)'0' and <= (byte)'9');
            int a = i, b = i;
            while (Digit(a - 1)) a--;
            while (Digit(b + 1)) b++;
            if (Enumerable.Range(a, b - a + 1).Any(j => changedI[j]))
                continue;
            int firstChanged = Enumerable.Range(a, b - a + 1).First(j => changedP[j]);
            for (int j = a; j <= b; j++) used[j] = true;
            // scores are usually 5 or 6 digits; the run may also hold the high score
            int width = Math.Min(b - a + 1, Math.Max(b - firstChanged + 2, 6));
            int addr = b - width + 1;
            if (Series(played, addr, width, "ascii", true) is { } v && NonDecreasing(v))
                found.Add(Make(addr, width, "ascii", v, 0.6,
                    b - a + 1 > width ? $"los dígitos siguen de ${start + a:X4} a ${start + b:X4} ({b - a + 1}); se toman los {width} últimos (el ancho se confirma leyendo la pantalla)" : ""));
        }

        // packed BCD and one digit per byte: clusters of consecutive bytes that changed only when playing
        for (int i = 0; i < len; i++)
        {
            if (!Seed(i) || used[i] || played.Any(s => !ValidBcd(s[i])) || idle.Any(s => !ValidBcd(s[i])))
                continue;
            int a = i, b = i;
            while (Seed(b + 1) && !used[b + 1] && played.All(s => ValidBcd(s[b + 1])))
                b++;
            for (int j = a; j <= b; j++) used[j] = true;
            int n = b - a + 1;
            bool digitsOnly = played.All(s => Enumerable.Range(a, n).All(j => s[j] <= 9));
            string enc = digitsOnly && n >= 2 ? "digits" : "bcd";
            // little endian first (the low byte at the lowest address), then big endian
            foreach (bool big in enc == "digits" ? new[] { true, false } : new[] { false, true })
            {
                int addr = a, bytes = n;
                bool Zero(int j) => j >= 0 && j < len && played.All(s => s[j] == 0) && idle.All(s => s[j] == 0);
                if (enc == "bcd" && n == 1)
                {
                    // a one-byte score (0-99) is rare: count the next byte when it is a zero that can carry
                    int higher = big ? a - 1 : a + 1;
                    if (Zero(higher)) { addr = Math.Min(a, higher); bytes = 2; }
                }
                if (enc == "digits")
                {
                    // leading zero digits belong to the number
                    if (big) while (bytes < 8 && Zero(addr - 1)) { addr--; bytes++; }
                    else while (bytes < 8 && Zero(addr + bytes)) bytes++;
                }
                if (Series(played, addr, bytes, enc, big) is { } v && NonDecreasing(v))
                {
                    if (enc == "digits" && !big)
                        break;   // the profiles only read digits most significant first
                    found.Add(Make(addr, bytes, enc, v, enc == "bcd" ? 0.6 : 0.5,
                        enc == "bcd" && bytes > n ? $"se cuenta también ${start + (addr == a ? a + 1 : addr):X4} (en cero) para los dígitos altos" : ""));
                    break;
                }
            }
        }

        // binary: what is left (a counter that only goes up could also be the score)
        for (int i = 0; i < len; i++)
        {
            if (!Seed(i) || used[i])
                continue;
            if (Seed(i + 1) && !used[i + 1] && Series(played, i, 2, "uint16", false) is { } w && NonDecreasing(w) && w.Any(x => x > 255))
            {
                used[i] = used[i + 1] = true;
                found.Add(Make(i, 2, "uint16", w, 0.35, ""));
                continue;
            }
            if (Series(played, i, 1, "uint8", false) is { } v && NonDecreasing(v))
            {
                used[i] = true;
                found.Add(Make(i, 1, "uint8", v, 0.3, "binario de un byte: también puede ser un contador de disparos, de objetos o de enemigos"));
            }
        }
        return found;
    }

    public static string EncodingName(string encoding, int bytes) => encoding switch
    {
        "ascii" => $"{bytes} dígitos ASCII",
        "digits" => $"{bytes} dígitos, uno por byte",
        "bcd" => $"BCD de {bytes} byte(s), {bytes * 2} dígitos",
        "uint16" => "binario de 16 bits",
        _ => "un byte",
    };

    /// <summary>Small counters the inputs change: a level or room that goes up by one, ammunition that goes down, an object picked up.</summary>
    public static List<LabFinding> Counters(List<byte[]> played, List<byte[]> idle, int start, string what, string kind)
    {
        int len = played[0].Length;
        var changedP = ChangedIn(played);
        var changedI = ChangedIn(idle);
        var found = new List<LabFinding>();
        for (int i = 0; i < len; i++)
        {
            if (!changedP[i] || changedI[i])
                continue;
            var v = played.Select(s => (int)s[i]).ToList();
            var steps = v.Zip(v.Skip(1)).Where(p => p.Second != p.First).Select(p => p.Second - p.First).ToList();
            int ups = steps.Count(d => d == 1), downs = steps.Count(d => d == -1);
            double confidence;
            string hypothesis;
            switch (kind)
            {
                case "level" when v[0] <= 99 && ups == steps.Count && ups is >= 1 and <= 3:
                    confidence = ups == 1 ? 0.55 : 0.45;
                    hypothesis = "Nivel, pantalla o habitación: sube de a uno cuando se pasa de nivel.";
                    break;
                case "ammo" when downs >= 2 && downs >= steps.Count - 1:
                    confidence = 0.5 + Math.Min(0.25, downs * 0.03);
                    hypothesis = "Munición o energía que gasta la acción: baja de a uno.";
                    break;
                case "item" when steps.Count is >= 1 and <= 3 && steps.All(d => d > 0):
                    confidence = 0.45;
                    hypothesis = "Objeto o inventario: cambia cuando se junta algo.";
                    break;
                case "change":
                    confidence = 0.3;
                    hypothesis = "Cambia solo con las entradas del experimento.";
                    break;
                default:
                    continue;
            }
            found.Add(new LabFinding
            {
                Kind = kind == "change" ? "change" : kind, Address = $"{start + i:X4}", Hypothesis = hypothesis,
                Evidence = { $"en la corrida {what} pasa de {v[0]} a {v[^1]} ({string.Join(", ", steps.Take(10).Select(d => d > 0 ? "+" + d : d.ToString()))}); sin tocar nada no cambia" },
                Confidence = confidence,
                ExpectedEffect = kind switch { "level" => "Cambiarlo lleva a otro nivel (al empezar el próximo).", "ammo" => "Subirlo da más munición.", _ => "" },
                HowToVerify = $"Escribir otro valor en ${start + i:X4} y comparar la pantalla con la corrida sin tocar.",
            });
        }
        return found.OrderByDescending(f => f.Confidence).Take(kind == "change" ? 20 : 8).ToList();
    }
}
