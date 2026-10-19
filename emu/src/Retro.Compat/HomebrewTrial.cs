using System.Text;
using Retro.Asm;
using Retro.Core;
using Retro.Machines;
using Retro.Systems.Spectrum;

namespace Retro.Compat;

/// <summary>One thing the trial looked at: passed or not, and what it saw. Optional ones only warn.</summary>
public sealed record TrialCheck(string Name, bool Ok, string Detail, bool Required = true);

/// <summary>What happened to a new game in the emulator: every check, the controls found and the screen.</summary>
public sealed class TrialReport
{
    public required string Machine { get; init; }
    public required AsmResult Assembly { get; init; }
    public byte[]? File { get; init; }
    public List<TrialCheck> Checks { get; } = new();
    public ControlSet? Controls { get; set; }
    /// <summary>The key the robot pressed to leave a title screen, if it needed one.</summary>
    public string? StartKey { get; set; }
    public string ScreenText { get; set; } = "";
    public string Sketch { get; set; } = "";
    public VideoFrame? FirstPicture { get; set; }
    public VideoFrame? LastPicture { get; set; }

    public bool Ok => Assembly.Ok && Checks.Count > 0 && Checks.All(c => c.Ok || !c.Required);

    /// <summary>The report as text, for whoever wrote the game (a person or an AI).</summary>
    public string Summary
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var c in Checks)
                sb.AppendLine($"[{(c.Ok ? "bien" : c.Required ? "FALLA" : "aviso")}] {c.Name}: {c.Detail}");
            if (Assembly.Ok && ScreenText.Trim().Length > 0)
            {
                sb.AppendLine("Texto en pantalla al final:");
                sb.AppendLine(string.Join("\n", ScreenText.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0)));
            }
            if (Sketch.Length > 0)
            {
                sb.AppendLine("Dibujo aproximado de la pantalla al final (cada carácter es un pedazo; \" .:+#\" de vacío a lleno):");
                sb.AppendLine(Sketch.TrimEnd());
            }
            return sb.ToString();
        }
    }
}

/// <summary>
/// Tries a game written from scratch the way a player would: assembles it, loads it like any
/// game of its machine, and checks that it starts, draws, answers to the controls (from a
/// title screen too, if fire or a start key leaves it) and survives a robot playing it at
/// random. What it reports is what an AI needs to correct its program.
/// </summary>
public static class HomebrewTrial
{
    public static TrialReport Run(string source, string machine, string firmwareDirectory, int robotFrames = 1500, int seed = 1)
    {
        var template = Templates.For(machine) ?? throw new ArgumentException($"no hay plantilla para {machine}");
        var (asm, file) = Templates.Build(source, template.Machine);
        var report = new TrialReport { Machine = template.Machine, Assembly = asm, File = file };
        if (file == null)
        {
            report.Checks.Add(new TrialCheck("ensambla", false, ErrorsText(asm, source)));
            return report;
        }
        report.Checks.Add(new TrialCheck("ensambla", true, $"{asm.Bytes.Length} bytes desde ${asm.Origin:X4}"));

        IMachine m;
        try
        {
            string name = "juego" + template.Output;
            m = MachineLauncher.Launch(template.Machine, new[] { new MediaFile(name, file, name) }, null, firmwareDirectory).Machine;
            if (m is Spectrum48 zx)
                MachineLauncher.LoadTape(zx);
            else
                Frames(m, 150, null);       // the C64 loads the .prg and types RUN
        }
        catch (Exception ex)
        {
            report.Checks.Add(new TrialCheck("carga", false, ex.Message));
            return report;
        }

        bool heard = false;
        uint start = asm.Origin, end = (uint)(asm.Origin + asm.Bytes.Length);
        int inside = 0;
        uint lastPc = 0;
        Frames(m, 100, () =>
        {
            lastPc = m.Cpu.ProgramCounter;
            if (lastPc >= start && lastPc < end)
                inside++;
        }, s => heard |= s);
        string? failure = ScreenReader.FailureSign(m, ScreenReader.Text(m));
        bool readyLeft = failure != null && failure.Contains("READY.") && inside >= 50;
        if (inside == 0)
            report.Checks.Add(new TrialCheck("arranca", false,
                $"en 100 cuadros la CPU nunca estuvo en tu código (${start:X4}-${end - 1:X4}); está en ${lastPc:X4}" + (failure != null ? $" y {failure}" : "")));
        else if (failure != null && !readyLeft)
            report.Checks.Add(new TrialCheck("arranca", false, failure));
        else
            report.Checks.Add(new TrialCheck("arranca", true, $"la CPU está en tu código en {inside} de 100 cuadros"));
        if (readyLeft)
            report.Checks.Add(new TrialCheck("pantalla limpia", false, "todavía se ve READY. del BASIC: borrá la pantalla al empezar", Required: false));
        if (!report.Checks[^1].Ok && report.Checks[^1].Required)
            return Finish(report, m);
        report.FirstPicture = Copy(m.Video);

        var (distinct, drawn) = Picture(m);
        report.Checks.Add(distinct >= 2 && drawn >= 16
            ? new TrialCheck("dibuja", true, $"{distinct} colores en la imagen; {drawn} puntos no son fondo")
            : new TrialCheck("dibuja", false, distinct < 2 ? "la imagen es de un solo color: no se dibujó nada" : $"solo {drawn} puntos no son fondo: lo dibujado casi no se ve"));

        // the controls, from where the game is now or after a start key
        var play = PlayDetector.Detect(m, report.Machine);
        if (!play.Playing)
        {
            byte[] before = m.SaveState();
            foreach (string key in StartKeys(report.Machine))
            {
                m.LoadState(before);
                m.SetKey(key, true);
                Frames(m, 5, null, s => heard |= s);
                m.SetKey(key, false);
                Frames(m, 60, null, s => heard |= s);
                play = PlayDetector.Detect(m, report.Machine);
                if (play.Playing)
                {
                    report.StartKey = key;
                    break;
                }
            }
            if (!play.Playing)
                m.LoadState(before);
        }
        report.Controls = play.Controls;
        report.Checks.Add(play.Playing
            ? new TrialCheck("controles", true, (report.StartKey != null ? $"después de apretar {report.StartKey}, " : "") + play.Evidence)
            : new TrialCheck("controles", false,
                "ningún control mueve algo de forma sostenida (probé " + string.Join(", ", PlayDetector.SetsFor(report.Machine).Select(s => s.Name)) +
                ", también después de apretar " + string.Join(" o ", StartKeys(report.Machine)) + "): el jugador tiene que moverse con alguno"));

        if (play.Playing && play.Controls != null)
            report.Checks.Add(Robot(m, play.Controls, robotFrames, seed, start, end, s => heard |= s));
        report.Checks.Add(new TrialCheck("sonido", heard, heard ? "se oyó sonido" : "no sonó nada en toda la prueba", Required: false));
        return Finish(report, m);
    }

    /// <summary>Keys that usually leave a title screen: the fire of every control set, and the machine's own.</summary>
    public static IEnumerable<string> StartKeys(string machine) =>
        PlayDetector.SetsFor(machine).Select(s => s.Fire).Concat(machine == "zx48" ? new[] { "ENTER" } : new[] { "SPACE" }).Distinct();

    /// <summary>
    /// A robot that plays at random with the controls found (a direction, sometimes with fire,
    /// held a while) and watches that the game neither crashes nor freezes.
    /// </summary>
    private static TrialCheck Robot(IMachine m, ControlSet set, int frames, int seed, uint start, uint end, Action<bool> sound)
    {
        var rng = new Random(seed);
        string[] directions = { set.Left, set.Right, set.Up, set.Down };
        var held = new List<string>();
        int nextChange = 0, samples = 0, changed = 0, inside = 0;
        string lastHash = ScreenReader.Hash(m.Video);
        for (int f = 0; f < frames; f++)
        {
            if (f == nextChange)
            {
                foreach (var k in held)
                    m.SetKey(k, false);
                held.Clear();
                if (rng.Next(5) > 0)
                    held.Add(directions[rng.Next(directions.Length)]);
                if (rng.Next(3) == 0)
                    held.Add(set.Fire);
                foreach (var k in held.Distinct())
                    m.SetKey(k, true);
                nextChange = f + 8 + rng.Next(32);
            }
            Frames(m, 1, null, sound);
            uint pc = m.Cpu.ProgramCounter;
            if (pc >= start && pc < end)
                inside++;
            if (f % 50 == 49)
            {
                samples++;
                string hash = ScreenReader.Hash(m.Video);
                if (hash != lastHash)
                    changed++;
                lastHash = hash;
                string? failure = ScreenReader.FailureSign(m, ScreenReader.Text(m));
                if (failure != null)
                {
                    m.ReleaseAllKeys();
                    return new TrialCheck("jugador robot", false, $"jugando al azar, en el cuadro {f + 1} {failure}");
                }
            }
        }
        m.ReleaseAllKeys();
        double seconds = frames / m.FramesPerSecond;
        if (changed == 0)
            return new TrialCheck("jugador robot", false, $"jugó {seconds:F0} s al azar y la pantalla no cambió nunca: el juego se colgó o no dibuja lo que pasa");
        if (inside == 0)
            return new TrialCheck("jugador robot", false, $"jugó {seconds:F0} s y la CPU dejó tu código (está en ${m.Cpu.ProgramCounter:X4})");
        return new TrialCheck("jugador robot", true, $"jugó {seconds:F0} s al azar sin que se trabe; la pantalla cambió en {changed} de {samples} miradas");
    }

    private static TrialReport Finish(TrialReport report, IMachine m)
    {
        report.LastPicture = Copy(m.Video);
        report.ScreenText = ScreenReader.Text(m) ?? "";
        report.Sketch = ScreenReader.Sketch(m);
        return report;
    }

    /// <summary>Distinct colours of the game's own picture (no border) and how many points are not the commonest one.</summary>
    private static (int distinct, int drawn) Picture(IMachine m)
    {
        var (x0, y0, w, h) = ScreenReader.PictureArea(m);
        var counts = new Dictionary<int, int>();
        for (int y = y0; y < Math.Min(y0 + h, m.Video.Height); y++)
            for (int x = x0; x < Math.Min(x0 + w, m.Video.Width); x++)
            {
                int p = m.Video.Pixels[y * m.Video.Width + x];
                counts[p] = counts.GetValueOrDefault(p) + 1;
            }
        return (counts.Count, counts.Count == 0 ? 0 : counts.Values.Sum() - counts.Values.Max());
    }

    private static VideoFrame Copy(VideoFrame v)
    {
        var copy = new VideoFrame(v.Width, v.Height);
        Array.Copy(v.Pixels, copy.Pixels, v.Pixels.Length);
        return copy;
    }

    /// <summary>Runs frames, calling <paramref name="each"/> after every one and telling <paramref name="sound"/> whether it sounded.</summary>
    private static void Frames(IMachine m, int frames, Action? each, Action<bool>? sound = null)
    {
        var sink = new short[8192];
        for (int i = 0; i < frames; i++)
        {
            m.RunFrame(null);
            int n = m.DrainAudio(sink);
            if (sound != null && n > 0)
            {
                short min = short.MaxValue, max = short.MinValue;
                for (int j = 0; j < n; j++)
                {
                    if (sink[j] < min) min = sink[j];
                    if (sink[j] > max) max = sink[j];
                }
                sound(max - min > 1500);
            }
            each?.Invoke();
        }
    }

    /// <summary>The assembler's errors with the line they point at, so the writer sees what to fix.</summary>
    public static string ErrorsText(AsmResult r, string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder($"{r.Errors.Count} error{(r.Errors.Count == 1 ? "" : "es")} del ensamblador:");
        foreach (var e in r.Errors.Take(15))
        {
            sb.Append($"\n  línea {e.Line}: {e.Message}");
            if (e.Line >= 1 && e.Line <= lines.Length)
                sb.Append($"\n      {lines[e.Line - 1].Trim()}");
        }
        if (r.Errors.Count > 15)
            sb.Append($"\n  y {r.Errors.Count - 15} más");
        return sb.ToString();
    }
}
