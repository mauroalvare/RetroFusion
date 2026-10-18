using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Retro.Core;
using Retro.Machines;
using Retro.Systems.Spectrum;

namespace Retro.Compat;

/// <summary>The levels a game can reach, in order. Each one assumes the previous ones.</summary>
public static class CompatLevels
{
    public const string Format = "format", Load = "load", Picture = "picture", Title = "title",
        Controls = "controls", Playable = "playable", Fusion = "fusion";

    public static readonly string[] All = { Format, Load, Picture, Title, Controls, Playable, Fusion };

    public static string Spanish(string level) => level switch
    {
        Format => "formato reconocido",
        Load => "medio cargado",
        Picture => "imagen",
        Title => "llega al título",
        Controls => "responde a los controles",
        Playable => "jugabilidad comprobada",
        Fusion => "listo para fusionar",
        _ => level,
    };
}

/// <summary>ok, partial, fail or untested, with what supports it.</summary>
public sealed class LevelResult
{
    public string Status { get; set; } = "untested";
    public string Detail { get; set; } = "";

    public static LevelResult Ok(string d) => new() { Status = "ok", Detail = d };
    public static LevelResult Partial(string d) => new() { Status = "partial", Detail = d };
    public static LevelResult Fail(string d) => new() { Status = "fail", Detail = d };
    public static LevelResult Untested(string d) => new() { Status = "untested", Detail = d };
}

/// <summary>The outcome of one script: evidence for each level.</summary>
public sealed class CompatResult
{
    public string Id { get; set; } = "";
    public string Game { get; set; } = "";
    public string Machine { get; set; } = "";
    public string File { get; set; } = "";
    public string? Member { get; set; }
    public string? Sha256 { get; set; }
    public long Bytes { get; set; }
    public string Edition { get; set; } = "";
    public string Firmware { get; set; } = "";
    public string Model { get; set; } = "";
    public string Controls { get; set; } = "";
    public string Procedure { get; set; } = "";
    public Dictionary<string, LevelResult> Levels { get; set; } = CompatLevels.All.ToDictionary(l => l, _ => LevelResult.Untested(""));
    /// <summary>Highest level with status ok.</summary>
    public string Highest { get; set; } = "";
    /// <summary>Problems of the emulator or of this run.</summary>
    public List<string> Problems { get; set; } = new();
    /// <summary>Things that belong to the game (from the script).</summary>
    public List<string> GameQuirks { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? ScreenHash { get; set; }
    public string? Screenshot { get; set; }
    public int FramesRun { get; set; }
    public string Scope { get; set; } = "";
    public bool Curated { get; set; }
    public string Notes { get; set; } = "";
}

/// <summary>
/// Runs a compatibility script without a window: loads the file, boots the machine, presses
/// the scripted keys, evaluates the checks, probes whether the inputs change anything, and
/// writes a result with evidence. Deterministic: the same script and file give the same
/// result and screen hash.
/// </summary>
public sealed class CompatRunner
{
    private readonly string _gamesRoot, _firmware;
    private readonly string? _shots;

    /// <summary>Frames run for each input of the controls probe before comparing pictures.</summary>
    public int ProbeFrames { get; set; } = 60;
    /// <summary>Frames the input stays pressed (then it is released).</summary>
    public int PressFrames { get; set; } = 8;

    public CompatRunner(string gamesRoot, string firmwareDirectory, string? screenshotsDirectory)
    {
        _gamesRoot = gamesRoot;
        _firmware = firmwareDirectory;
        _shots = screenshotsDirectory;
    }

    public static readonly Dictionary<string, string[]> DefaultProbeInputs = new()
    {
        // the usual game keys and the menu keys titles wait for (start, keyboard/joystick choice, define keys)
        ["zx48"] = new[] { "KEMPSTON LEFT", "KEMPSTON RIGHT", "KEMPSTON UP", "KEMPSTON FIRE", "O", "P", "Q", "A", "M", "Z", "X", "N", "S", "K", "Y",
                           "SPACE", "ENTER", "CAPS SHIFT", "SYMBOL SHIFT", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" },
        ["c64"] = new[] { "JOY2 LEFT", "JOY2 RIGHT", "JOY2 UP", "JOY2 DOWN", "JOY2 FIRE", "JOY1 RIGHT", "JOY1 FIRE", "SPACE", "F1", "RETURN" },
        ["invaders"] = new[] { "COIN", "P1 START", "P1 LEFT", "P1 RIGHT", "P1 FIRE" },
    };

    /// <summary>
    /// Boots the script's game and plays its steps, leaving the machine at the end of the
    /// script (where the lab and agents start their experiments). Returns null and the
    /// reason when it cannot get there.
    /// </summary>
    public (IMachine? machine, string? sha256, string? problem) Start(CompatScript s)
    {
        string path = Path.Combine(_gamesRoot, s.File);
        if (!System.IO.File.Exists(path))
            return (null, null, $"no está {s.File} en {_gamesRoot}");
        var files = MediaArchive.Open(path);
        MediaFile? media = s.Machine == "invaders" ? null
            : files.FirstOrDefault(f => s.Member != null && (f.Name.Equals(s.Member, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f.Name).Equals(s.Member, StringComparison.OrdinalIgnoreCase)));
        LaunchResult launch;
        try { launch = MachineLauncher.Launch(s.Machine, files, media, _firmware, Path.GetDirectoryName(path)); }
        catch (Exception e) when (e is NotSupportedException or InvalidDataException or FileNotFoundException) { return (null, null, e.Message); }
        var m = launch.Machine;
        if (launch.IsTape && s.Boot != "none" && m is Spectrum48 zx)
            MachineLauncher.LoadTape(zx, s.BootFrames);
        var sink = new short[8192];
        for (int f = 0; f < s.Frames; f++)
        {
            foreach (var st in s.Steps.Where(st => st.Frame == f))
                m.QueueKeys(st.Hold, st.Keys.Replace('_', ' ').Split('+'));
            m.RunFrame(null);
            m.DrainAudio(sink);
        }
        return (m, Convert.ToHexString(SHA256.HashData(launch.Media.Data)).ToLowerInvariant(), null);
    }

    public CompatResult Run(CompatScript s)
    {
        var r = new CompatResult
        {
            Id = s.Id, Game = s.Game, Machine = s.Machine, File = s.File, Edition = s.Edition, Controls = s.Controls,
            GameQuirks = s.GameQuirks.ToList(), Curated = s.Curated, Notes = s.Notes,
        };
        var sw = Stopwatch.StartNew();
        try
        {
            RunInto(s, r);
        }
        catch (Exception e)
        {
            r.Problems.Add($"error inesperado del emulador: {e.GetType().Name}: {e.Message}");
        }
        r.Highest = CompatLevels.All.LastOrDefault(l => r.Levels[l].Status == "ok") ?? "";
        return r;
    }

    private void RunInto(CompatScript s, CompatResult r)
    {
        string path = Path.Combine(_gamesRoot, s.File);
        if (!System.IO.File.Exists(path))
        {
            r.Levels[CompatLevels.Format] = LevelResult.Fail($"no está el archivo {s.File} en {_gamesRoot}");
            return;
        }
        var files = MediaArchive.Open(path);
        // ---------------------------------------------------------------- format
        MediaFile? media = null;
        string formatReason;
        if (s.Machine == "invaders")
        {
            var set = MachineCatalog.IdentifySet(files);
            formatReason = set != null ? set.Reason : "";
            media = files.FirstOrDefault();
            if (set == null)
            {
                r.Levels[CompatLevels.Format] = LevelResult.Fail("el zip no tiene los chips de Space Invaders");
                return;
            }
        }
        else
        {
            var candidates = files
                .Where(f => s.Member == null || f.Name.Equals(s.Member, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f.Name).Equals(s.Member, StringComparison.OrdinalIgnoreCase))
                .SelectMany(MachineCatalog.Identify)
                .Where(c => c.Machine.Id == s.Machine || (s.Machine == "zx128" && c.Machine.Id == "zx48"))
                .OrderByDescending(c => c.Confidence).ThenBy(c => MediaArchive.DumpScore(c.File.Name))
                .ToList();
            if (candidates.Count == 0)
            {
                r.Levels[CompatLevels.Format] = LevelResult.Fail(
                    s.Member != null && !files.Any(f => f.Name.EndsWith(s.Member, StringComparison.OrdinalIgnoreCase))
                        ? $"no está {s.Member} dentro de {s.File}"
                        : $"ningún archivo de {s.File} tiene un formato de {s.Machine}");
                return;
            }
            media = candidates[0].File;
            formatReason = candidates[0].Reason;
        }
        r.Member = media!.Name;
        r.Bytes = media.Data.Length;
        r.Sha256 = Convert.ToHexString(SHA256.HashData(media.Data)).ToLowerInvariant();
        if (s.Sha256 != null && !s.Sha256.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase))
            r.Warnings.Add("el archivo no es la edición para la que se escribió el guion (otro SHA-256): los pasos y comprobaciones pueden no valer");
        r.Levels[CompatLevels.Format] = LevelResult.Ok($"{formatReason} ({media.Name}, {media.Data.Length} bytes)");

        // ---------------------------------------------------------------- load
        if (!MachineLauncher.IsImplemented(s.Machine))
        {
            r.Levels[CompatLevels.Load] = LevelResult.Untested($"la máquina {s.Machine} todavía no está implementada");
            r.Problems.Add($"limitación del emulador: falta la máquina {s.Machine}");
            return;
        }
        LaunchResult launch;
        try
        {
            launch = MachineLauncher.Launch(s.Machine, files, s.Machine == "invaders" ? null : media, _firmware, Path.GetDirectoryName(path));
        }
        catch (Exception e) when (e is NotSupportedException or InvalidDataException or FileNotFoundException)
        {
            r.Levels[CompatLevels.Load] = LevelResult.Fail(e.Message);
            r.Problems.Add("limitación del emulador: " + e.Message);
            return;
        }
        var m = launch.Machine;
        r.Firmware = MachineLauncher.FirmwareOf(m);
        r.Model = MachineLauncher.ModelOf(m);
        var procedure = new List<string> { launch.Description };
        procedure.AddRange(launch.Warnings.Select(w => "aviso: " + w));
        if (launch.IsTape && s.Boot != "none" && m is Spectrum48 zx)
        {
            int frames = MachineLauncher.LoadTape(zx, s.BootFrames);
            if (!zx.Tape.AtEnd && zx.Tape.IsPlaying && s.LoadEndsByTime)
                procedure.Add($"LOAD \"\" y {frames} cuadros de cinta (multicarga: el guion da por cargada la primera parte en el bloque {zx.Tape.CurrentBlock})");
            else if (!zx.Tape.AtEnd && zx.Tape.IsPlaying)
            {
                r.Levels[CompatLevels.Load] = LevelResult.Fail($"la cinta no terminó de cargar en {frames} cuadros (bloque {zx.Tape.CurrentBlock})");
                r.Problems.Add("la carga desde cinta no terminó");
                Capture(m, s, r);
                Finish(m, s, r, procedure, 0);
                return;
            }
            else
                procedure.Add(zx.Tape.AtEnd
                    ? $"LOAD \"\" y cinta completa por la ROM en {frames} cuadros ({frames / m.FramesPerSecond:F0} s)"
                    : $"LOAD \"\" en {frames} cuadros ({frames / m.FramesPerSecond:F0} s) hasta que la cinta se detuvo sola en el bloque {zx.Tape.CurrentBlock} (multicarga: el resto se carga cuando el juego lo pide)");
        }
        r.Levels[CompatLevels.Load] = LevelResult.Ok(string.Join("; ", procedure));

        // ---------------------------------------------------------------- scripted run
        foreach (var st in s.Steps)
            procedure.Add($"cuadro {st.Frame}: {st.Keys} durante {st.Hold}");
        var wantedPictures = s.Checks.Where(c => c.Type == "changed").Select(c => c.Since).ToHashSet();
        var pictures = new Dictionary<int, string>();
        var checkResults = new List<(CompatCheck check, bool ok, string detail)>();
        var sink = new short[8192];
        for (int f = 0; f < s.Frames; f++)
        {
            foreach (var st in s.Steps.Where(st => st.Frame == f))
                m.QueueKeys(st.Hold, st.Keys.Replace('_', ' ').Split('+'));
            m.RunFrame(null);
            m.DrainAudio(sink);
            int now = f + 1;
            if (wantedPictures.Contains(now))
                pictures[now] = ScreenReader.Hash(m.Video);
            foreach (var c in s.Checks.Where(c => c.Frame == now))
                checkResults.Add(Evaluate(m, c, pictures));
        }
        foreach (var c in s.Checks.Where(c => c.Frame == 0 || c.Frame > s.Frames))
            checkResults.Add(Evaluate(m, c, pictures));
        r.FramesRun = s.Frames;

        Capture(m, s, r);

        // ---------------------------------------------------------------- picture
        string? text = ScreenReader.Text(m);
        var stats = ScreenReader.Stats(m.Video);
        string? failure = ScreenReader.FailureSign(m, text);
        if (failure != null)
        {
            r.Levels[CompatLevels.Picture] = LevelResult.Fail(failure);
            r.Problems.Add(failure);
        }
        else if (stats.DistinctColors < 2 || stats.DominantShare > 0.998)
            r.Levels[CompatLevels.Picture] = LevelResult.Fail($"pantalla casi vacía al final ({stats.DistinctColors} colores, {stats.DominantShare:P0} de un solo color)");
        else
            r.Levels[CompatLevels.Picture] = LevelResult.Ok($"{stats.DistinctColors} colores al final; ningún signo de fallo (BASIC, error, pantalla vacía)");

        // ---------------------------------------------------------------- scripted levels
        foreach (string level in new[] { CompatLevels.Title, CompatLevels.Controls, CompatLevels.Playable })
        {
            var mine = checkResults.Where(x => x.check.Level == level).ToList();
            if (mine.Count == 0)
                continue;
            string detail = string.Join("; ", mine.Select(x => (x.ok ? "sí: " : "no: ") + x.detail));
            r.Levels[level] = mine.All(x => x.ok) ? LevelResult.Ok(detail) : LevelResult.Fail(detail);
        }
        if (r.Levels[CompatLevels.Title].Status == "untested")
            r.Levels[CompatLevels.Title] = LevelResult.Untested("sin comprobación de título en el guion (mirar la captura)");

        // ---------------------------------------------------------------- controls probe
        if (r.Levels[CompatLevels.Picture].Status == "ok")
        {
            var probe = ProbeControls(m, s);
            var scripted = r.Levels[CompatLevels.Controls];
            if (scripted.Status == "untested")
                r.Levels[CompatLevels.Controls] = probe;
            else if (scripted.Status == "ok")
                scripted.Detail += " | sonda: " + probe.Detail;
        }
        if (r.Levels[CompatLevels.Playable].Status == "untested")
            r.Levels[CompatLevels.Playable] = LevelResult.Untested("sin comprobaciones de partida en el guion");

        // ---------------------------------------------------------------- fusion
        var profile = Retro.Fusion.ProfileLibrary.ForFiles(files);
        r.Levels[CompatLevels.Fusion] = profile == null
            ? LevelResult.Untested("no hay perfil de fusión para este juego")
            : LevelResult.Partial($"perfil \"{profile.Id}\" asociado por nombre de archivo; sus direcciones no están verificadas contra este hash (T-053)");
        Finish(m, s, r, procedure, s.Frames);
    }

    /// <summary>Hash and screenshot of the picture at the end of the scripted run.</summary>
    private void Capture(IMachine m, CompatScript s, CompatResult r)
    {
        r.ScreenHash = ScreenReader.Hash(m.Video);
        if (_shots != null)
        {
            Directory.CreateDirectory(_shots);
            string file = Path.Combine(_shots, s.Id + ".png");
            PngWriter.Write(file, m.Video, 1);
            r.Screenshot = file.Replace('\\', '/');
        }
    }

    private void Finish(IMachine m, CompatScript s, CompatResult r, List<string> procedure, int frames)
    {
        r.Procedure = string.Join("; ", procedure);
        double seconds = frames / m.FramesPerSecond;
        r.Scope = $"{frames} cuadros ({seconds:F0} s de máquina) después del arranque, con {s.Steps.Count} pulsaciones guionadas y {s.Checks.Count} comprobaciones" +
                  (r.Levels[CompatLevels.Controls].Status != "untested" ? $"; sonda de controles de {ProbeFrames} cuadros por entrada" : "") +
                  ". No es una partida completa.";
    }

    private static (CompatCheck, bool, string) Evaluate(IMachine m, CompatCheck c, Dictionary<int, string> pictures)
    {
        string label = c.Description ?? $"{c.Type} {c.Value ?? c.Address}";
        switch (c.Type)
        {
            case "text":
            case "notText":
            {
                string? text = ScreenReader.Text(m)?.Replace('@', ' ');
                if (text == null)
                    return (c, false, $"{label}: esta máquina no tiene texto legible");
                bool found = Normalize(text).Contains(Normalize(c.Value ?? ""), StringComparison.OrdinalIgnoreCase);
                bool ok = c.Type == "text" ? found : !found;
                return (c, ok, $"{label} (\"{c.Value}\" {(found ? "está" : "no está")} en pantalla en el cuadro {c.Frame})");
            }
            case "memory":
            {
                uint addr = Convert.ToUInt32((c.Address ?? "0").TrimStart('$'), 16);
                int actual = m.Memory.Peek(addr);
                int expected = ParseNumber(c.Value ?? "0");
                bool ok = c.Op switch
                {
                    "ne" => actual != expected, "gt" => actual > expected, "lt" => actual < expected,
                    "ge" => actual >= expected, "le" => actual <= expected, _ => actual == expected,
                };
                return (c, ok, $"{label} (${addr:X4} = {actual}, se esperaba {c.Op} {expected})");
            }
            case "colors":
            {
                var st = ScreenReader.Stats(m.Video);
                return (c, st.DistinctColors >= c.Min, $"{label} ({st.DistinctColors} colores, mínimo {c.Min})");
            }
            case "changed":
            {
                bool ok = pictures.TryGetValue(c.Since, out var before) && before != ScreenReader.Hash(m.Video);
                return (c, ok, $"{label} (la imagen {(ok ? "cambió" : "no cambió")} desde el cuadro {c.Since})");
            }
            default:
                return (c, false, $"tipo de comprobación desconocido: {c.Type}");
        }
    }

    private static string Normalize(string s) => string.Join(' ', s.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));

    private static int ParseNumber(string v) => v.StartsWith('$')
        ? Convert.ToInt32(v[1..], 16)
        : int.Parse(v, CultureInfo.InvariantCulture);

    /// <summary>
    /// From a saved state, holds each candidate input and compares the picture with the one
    /// of the same frames without input. Emulation is deterministic, so a different picture
    /// means the game reacted to that input.
    /// </summary>
    private LevelResult ProbeControls(IMachine m, CompatScript s)
    {
        string[] inputs = s.ProbeInputs.Count > 0 ? s.ProbeInputs.ToArray()
            : DefaultProbeInputs.TryGetValue(s.Machine, out var d) ? d : Array.Empty<string>();
        if (inputs.Length == 0)
            return LevelResult.Untested("no hay entradas para probar en esta máquina");
        byte[] state;
        try { state = m.SaveState(); }
        catch (Exception e) { return LevelResult.Untested("no se pudo guardar el estado para la sonda: " + e.Message); }
        var sink = new short[8192];
        // a press of PressFrames and then released (menus often wait for press and release);
        // the picture is compared while pressed and at the end
        string RunWith(string? input)
        {
            m.LoadState(state);
            string pressed = "";
            for (int i = 0; i < ProbeFrames; i++)
            {
                if (input != null && (i == 0 || i == PressFrames))
                    foreach (var k in input.Split('+'))
                        m.SetKey(k, i == 0);
                m.RunFrame(null);
                m.DrainAudio(sink);
                if (i == PressFrames - 1)
                    pressed = ScreenReader.Hash(m.Video);
            }
            return pressed + ScreenReader.Hash(m.Video);
        }
        string baseline = RunWith(null);
        string again = RunWith(null);
        if (again != baseline)
        {
            m.LoadState(state);
            return LevelResult.Untested("la sonda no es fiable: la misma corrida sin entradas dio imágenes distintas");
        }
        var effective = inputs.Where(i => RunWith(i) != baseline).ToList();
        m.LoadState(state);
        return effective.Count > 0
            ? LevelResult.Ok($"cambian la imagen: {string.Join(", ", effective)} (de {inputs.Length} probadas)")
            : LevelResult.Fail($"ninguna de {inputs.Length} entradas (apretadas {PressFrames} cuadros) cambió la imagen en {ProbeFrames} cuadros ({string.Join(", ", inputs)})");
    }
}
