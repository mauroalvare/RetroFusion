using Retro.Core;
using Retro.Fusion;

namespace Retro.Compat;

/// <summary>What an automatic experiment did to a hypothesis and what it showed.</summary>
public sealed class LabVerification
{
    /// <summary>verified, refuted or inconclusive.</summary>
    public string Result { get; set; } = "inconclusive";
    public string Experiment { get; set; } = "";
    public List<string> Observations { get; set; } = new();
    public int Frames { get; set; }
    /// <summary>Most pixels that differed from the untouched run at the moments compared.</summary>
    public int PixelsChanged { get; set; }
    public string Date { get; set; } = "";
}

/// <summary>
/// Checks a hypothesis by experiment: from the same state, the game runs once untouched
/// and once with a test value written into the candidate (the same inputs in both). A
/// stored variable keeps the written value (or keeps changing from it as the game plays);
/// one the game recomputes is back to the untouched value at once. Then: does the picture
/// change, does the screen text show the new number, does the game keep counting from it?
/// The machine is left at the starting state; the user's file is never touched.
/// </summary>
public static class LabVerifier
{
    public const int DefaultFrames = 180;

    public static LabVerification Verify(IMachine m, LabFinding f, LabExperiment? replay = null, int frames = 0)
    {
        if (f.Kind == "entity-table" && f.Fields is { Count: > 0 } && frames == 0)
            return VerifyTable(m, f);
        // run past the moment the observation saw it change (a life lost) so the game uses the written value
        int n = frames > 0 ? frames : replay?.Frames ?? Math.Clamp((f.EventFrame ?? 0) + 90, DefaultFrames, 9000);
        uint address = Convert.ToUInt32(f.Address, 16);
        int bytes = f.Kind == "entity-table" ? 1 : f.Bytes;
        var v = new GameVariable { Address = f.Address, Encoding = f.Kind == "entity-table" ? "uint8" : f.Encoding, Bytes = bytes, Min = 0, Max = int.MaxValue };
        byte[] state = m.SaveState();
        int before = v.Read(m.Memory);
        int target = TestValue(f, before);
        var checkpoints = new[] { 2, 10, 30, 60, 120, n }.Where(x => x <= n).Distinct().ToArray();
        var sink = new short[8192];

        (int[] values, Dictionary<int, int[]> pictures, string? text) Play(bool poke)
        {
            m.LoadState(state);
            var keys = new LabKeys(m);
            if (poke)
                v.Write(m.Memory, target);
            var values = new int[n + 1];
            var pictures = new Dictionary<int, int[]>();
            values[0] = v.Read(m.Memory);
            for (int frame = 0; frame < n; frame++)
            {
                keys.Tick(frame);
                if (replay != null)
                    foreach (var s in replay.DueAt(frame))
                        keys.Press(s.Keys, s.Hold, frame);
                m.RunFrame(null);
                m.DrainAudio(sink);
                values[frame + 1] = v.Read(m.Memory);
                if (checkpoints.Contains(frame + 1))
                    pictures[frame + 1] = (int[])m.Video.Pixels.Clone();
            }
            keys.ReleaseAll();
            return (values, pictures, ScreenReader.Text(m));
        }

        var (bv, bp, bt) = Play(false);
        var (pv, pp, pt) = Play(true);
        m.LoadState(state);
        m.ReleaseAllKeys();

        var result = new LabVerification
        {
            Frames = n, Date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"),
            Experiment = $"Escribí {target} en ${f.Address} ({LabExperiments.EncodingName(v.Encoding, bytes)}; antes {before}) y corrí {n} cuadros " +
                         (replay != null ? $"repitiendo las entradas de \"{replay.Description}\" " : "sin tocar nada ") +
                         "contra la misma corrida sin escribir.",
        };
        var obs = result.Observations;
        // when does the written value fall back to the untouched run for good?
        int backAt = n + 1;
        for (int k = n; k >= 0 && pv[k] == bv[k]; k--)
            backAt = k;
        // does the game keep operating on it? same changes as untouched, shifted by what was written
        int offset = target - before, untilDiverge = n + 1, changes = 0;
        for (int k = 1; k <= n; k++)
        {
            if (pv[k] - bv[k] != offset) { untilDiverge = k; break; }
            if (bv[k] != bv[k - 1]) changes++;
        }
        bool keptOperating = changes >= 1;
        // a buffer the game redraws from its real variables gets back within a few frames
        bool overwritten = backAt <= Math.Min(10, n) && !keptOperating;
        if (overwritten)
            obs.Add($"el juego lo vuelve a su valor ({bv[backAt]}) en {backAt} cuadro(s): lo recalcula desde otra variable o es una copia");
        else if (backAt <= n)
            obs.Add($"conserva el valor escrito {backAt} cuadros y después coincide con la corrida sin tocar (el juego lo reinició)");
        else
            obs.Add($"conserva el valor escrito: termina en {pv[n]} (sin tocar, {bv[n]})");
        if (keptOperating)
            obs.Add($"el juego siguió cambiándolo desde el valor escrito igual que sin tocar ({changes} cambio(s) en {Math.Min(untilDiverge, n + 1) - 1} cuadros)");

        // the picture
        int most = 0, at = 0;
        (int x0, int y0, int x1, int y1) box = default;
        int width = m.Video.Width;
        foreach (int k in checkpoints)
        {
            var a = bp[k];
            var b = pp[k];
            int diff = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                {
                    diff++;
                    int x = i % width, y = i / width;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            if (diff > most)
            {
                most = diff;
                at = k;
                box = (minX, minY, maxX, maxY);
            }
        }
        result.PixelsChanged = most;
        if (most > 0)
            obs.Add($"la pantalla cambia: {most} píxeles distintos en el cuadro {at}, en la zona x {box.x0}-{box.x1}, y {box.y0}-{box.y1}");
        else
            obs.Add($"la pantalla queda igual que sin tocar en los cuadros {string.Join(", ", checkpoints)}");

        // the screen text (numbers only: a written score should appear where the old one was)
        bool textShows = false;
        if (bt != null && pt != null && f.Kind is "score" or "lives" or "level" or "ammo" && !overwritten)
        {
            int digits = DigitsOf(v.Encoding, bytes);
            string shown = Pad(pv[n], digits), untouched = Pad(bv[n], digits);
            for (int k = digits; k >= 2 && !textShows; k--)
            {
                string tail = shown[(digits - k)..];
                if (pt.Contains(tail) && !bt.Contains(tail) && Significant(tail))
                {
                    textShows = true;
                    obs.Add($"el texto de la pantalla muestra {tail} donde sin tocar muestra {untouched[(digits - k)..]}");
                    if (f.Kind == "score" && v.Encoding is "ascii" or "digits" && k != bytes)
                    {
                        // the screen shows k digits: the variable is the last k bytes of the run
                        uint newAddress = address + (uint)(bytes - k);
                        obs.Add($"en pantalla se ven {k} dígitos: el puntaje queda en ${newAddress:X4}, {k} bytes (antes ${f.Address}, {bytes})");
                        f.Address = $"{newAddress:X4}";
                        f.Bytes = k;
                    }
                }
            }
        }

        if (overwritten)
        {
            result.Result = "refuted";
            if (f.Kind.StartsWith("position"))
                f.Nature = "derived";
        }
        else if (textShows || keptOperating || most > 0)
            result.Result = "verified";
        else
        {
            result.Result = "inconclusive";
            obs.Add("el valor escrito se mantiene pero no se vio ningún efecto: puede que el juego lo use más adelante (al morir, al pasar de nivel)");
        }
        f.Status = result.Result;
        f.Verification = result;
        if (result.Result == "verified")
        {
            // the stronger the evidence the higher: the text shows it, the game keeps counting from it, the picture changes
            int strength = (textShows ? 3 : 0) + (keptOperating ? 2 : 0) + (most > 0 ? 1 : 0);
            f.Confidence = Math.Min(1.0, 0.85 + 0.02 * strength + 0.02 * Math.Min(1, f.Confidence));
        }
        else if (result.Result == "refuted")
            f.Confidence = Math.Min(f.Confidence, f.Kind.StartsWith("position") ? 0.4 : 0.2);
        return result;
    }

    /// <summary>
    /// A table is verified through its moving fields: the first one the game does not
    /// recompute at once decides (a table whose fields are all recomputed is a copy).
    /// </summary>
    private static LabVerification VerifyTable(IMachine m, LabFinding table)
    {
        int baseAddress = Convert.ToInt32(table.Address, 16);
        LabVerification? last = null;
        foreach (var field in table.Fields!)
        {
            var match = System.Text.RegularExpressions.Regex.Match(field, @"^\+(\d+)");
            if (!match.Success)
                continue;
            var probe = new LabFinding { Kind = "entity-table", Address = $"{baseAddress + int.Parse(match.Groups[1].Value):X4}" };
            last = Verify(m, probe, null, DefaultFrames);
            last.Experiment = $"Campo +{match.Groups[1].Value} del primer registro: " + last.Experiment;
            if (last.Result != "refuted")
                break;
        }
        last ??= new LabVerification { Experiment = "La tabla no tiene campos para probar." };
        table.Status = last.Result;
        table.Verification = last;
        table.Confidence = last.Result == "verified" ? 0.9 : last.Result == "refuted" ? 0.2 : table.Confidence;
        return last;
    }

    /// <summary>A value different enough to notice, valid for the encoding and plausible for the game.</summary>
    public static int TestValue(LabFinding f, int before)
    {
        int max = f.Encoding switch
        {
            "bcd" => Pow10(f.Bytes * 2) - 1,
            "ascii" or "digits" => Pow10(Math.Min(f.Bytes, 9)) - 1,
            "uint16" => 0xFFFF,
            _ => 0xFF,
        };
        int value = f.Kind switch
        {
            "lives" => before <= 7 ? before + 2 : before - 1,
            "timer" => before >= 8 ? before - 4 : before + 4,
            "level" => before + 1,
            "ammo" => before + 5,
            "score" => before + (max >= 9999 ? 1230 : max >= 99 ? 12 : 1),
            "position-x" or "position-y" or "entity-table" => before < 128 ? before + 24 : before - 24,
            _ => before + 1,
        };
        if (value > max)
            value = before > 0 ? before - 1 : 1;
        return value;
    }

    private static int Pow10(int n)
    {
        int p = 1;
        for (int i = 0; i < n && p < int.MaxValue / 10; i++) p *= 10;
        return p;
    }

    private static int DigitsOf(string encoding, int bytes) => encoding switch
    {
        "bcd" => bytes * 2,
        "ascii" or "digits" => bytes,
        "uint16" => 5,
        _ => 3,
    };

    private static string Pad(int value, int digits) => value.ToString().PadLeft(digits, '0');

    /// <summary>Tails like "00" match anything; the text must show at least one non-zero digit.</summary>
    private static bool Significant(string tail) => tail.Any(c => c != '0');

    /// <summary>
    /// Verifies the most likely findings of a report, a few per kind (the lab can find dozens
    /// of weak candidates). Score findings replay the experiment that found them, so the game
    /// adds points in both runs.
    /// </summary>
    public static void VerifyBest(IMachine m, LabReport report, IReadOnlyDictionary<LabFinding, LabExperiment>? replays = null, int perKind = 3)
    {
        // values the game recomputes or rewrites from everywhere are read-only candidates: writing them proves nothing
        foreach (var group in report.Findings.Where(f => f.Nature != "derived").GroupBy(f => f.Kind))
            foreach (var f in group.Where(f => f.Status == "hypothesis").OrderByDescending(f => f.Confidence).Take(perKind))
                Verify(m, f, replays != null && replays.TryGetValue(f, out var e) ? e : null);
        report.Findings = report.Findings
            .OrderBy(f => f.Status switch { "verified" => 0, "hypothesis" => 1, "inconclusive" => 2, _ => 3 })
            .ThenByDescending(f => f.Confidence).ThenBy(f => f.Kind).ThenBy(f => f.Address).ToList();
    }
}
