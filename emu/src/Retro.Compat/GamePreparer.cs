using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Retro.Core;
using Retro.Fusion;
using Retro.Machines;
using Retro.Systems.Spectrum;

namespace Retro.Compat;

/// <summary>One input of the boot: hold <see cref="Keys"/> (joined with +) for <see cref="Hold"/> frames, then wait <see cref="Wait"/> frames.</summary>
public sealed record PlayAction(string Keys, int Hold, int Wait);

/// <summary>What an advisor knows at one round: the screen, what the keys do and what was done.</summary>
public sealed class PlayObservation
{
    public string Goal { get; init; } = "";
    public string Game { get; init; } = "";
    public string Machine { get; init; } = "";
    public string MachineName { get; init; } = "";
    public string File { get; init; } = "";
    public int Round { get; init; }
    public int FramesSoFar { get; init; }
    public double FramesPerSecond { get; init; }
    public IReadOnlyList<string> Keys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ControlSet> ControlSets { get; init; } = Array.Empty<ControlSet>();
    public string? ScreenText { get; init; }
    public string Sketch { get; init; } = "";
    public bool PictureMoves { get; init; }
    /// <summary>For the usual start keys: whether pressing it changes the picture, and whether to one never seen.</summary>
    public IReadOnlyList<KeyEffect> KeyEffects { get; init; } = Array.Empty<KeyEffect>();
    public IReadOnlyList<string> History { get; init; } = Array.Empty<string>();
    public string PlayCheck { get; init; } = "";
    /// <summary>A copy of the picture, for advisors that can look at images (agents through files).</summary>
    public VideoFrame? Picture { get; init; }

    /// <summary>Everything as text, for an AI.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Juego: {Game} (archivo {File}), máquina {MachineName} a {FramesPerSecond:F0} cuadros por segundo.");
        if (!string.IsNullOrWhiteSpace(Goal)) sb.AppendLine("Objetivo de preparación: " + Goal);
        sb.AppendLine($"Ronda {Round}; van {FramesSoFar} cuadros desde que arrancó el juego.");
        sb.AppendLine("Teclas que existen: " + string.Join(", ", Keys));
        sb.AppendLine("Controles de juego posibles: " + string.Join("; ", ControlSets.Select(c => $"{c.Name}: {c.Left}/{c.Right}/{c.Up}/{c.Down}, disparo {c.Fire}")));
        sb.AppendLine();
        sb.AppendLine("Lo que dicen los controles ahora: " + PlayCheck);
        sb.AppendLine(PictureMoves ? "La pantalla se mueve sola (animación, demo o carga)." : "La pantalla está quieta.");
        if (!string.IsNullOrWhiteSpace(ScreenText))
        {
            sb.AppendLine("Texto que se lee en la pantalla:");
            sb.AppendLine("```");
            sb.AppendLine(ScreenText.TrimEnd());
            sb.AppendLine("```");
        }
        else
            sb.AppendLine("(Esta máquina no tiene un texto legible: guiate por el dibujo.)");
        sb.AppendLine("Dibujo aproximado de la pantalla (cada carácter es una celda; espacio = fondo, # = lleno):");
        sb.AppendLine("```");
        sb.AppendLine(Sketch.TrimEnd());
        sb.AppendLine("```");
        if (KeyEffects.Count > 0)
            sb.AppendLine("Qué pasa si se aprieta cada tecla ahora (6 cuadros y 3 segundos de espera): " +
                          string.Join("; ", KeyEffects.Select(e => $"{e.Key}: {e.Text}")));
        if (History.Count > 0)
        {
            sb.AppendLine("Lo que ya se hizo:");
            foreach (var h in History)
                sb.AppendLine("- " + h);
        }
        return sb.ToString();
    }
}

public sealed record KeyEffect(string Key, bool Changes, bool NewScreen, string Text);

/// <summary>An advisor's answer: the next inputs, or that the game is already being played.</summary>
public sealed class PlayAdvice
{
    public bool Playing { get; set; }
    public string Reason { get; set; } = "";
    public List<PlayAction> Actions { get; set; } = new();
}

/// <summary>A variable the lab verified, as an advisor sees it to say what it is.</summary>
public sealed record VariableCandidate(string Address, string Guess, string Encoding, int Bytes, int Value, bool ReadOnly, string Evidence);

/// <summary>What an advisor says a variable is: a profile name (lives, score, x... or its own), a description, and whether to keep it.</summary>
public sealed record VariableName(string Address, string Name, string Description, bool Keep);

/// <summary>The game in progress and the variables the lab verified, for an advisor that names them.</summary>
public sealed class VariableQuestion
{
    public string Goal { get; init; } = "";
    public string Game { get; init; } = "";
    public string MachineName { get; init; } = "";
    public VideoFrame? Picture { get; init; }
    public IReadOnlyList<VariableCandidate> Candidates { get; init; } = Array.Empty<VariableCandidate>();

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Juego: {Game} ({MachineName}), ya en la partida.");
        if (!string.IsNullOrWhiteSpace(Goal)) sb.AppendLine("Objetivo de preparación: " + Goal);
        sb.AppendLine("El laboratorio reunió estas direcciones y su evidencia. Algunas admiten una prueba de escritura; " +
                      "otras solo se observaron y son de solo lectura. Su nombre provisorio sale de cómo cambian y puede estar mal:");
        foreach (var c in Candidates)
            sb.AppendLine($"- ${c.Address} ({c.Encoding}{(c.Bytes > 1 ? $", {c.Bytes} bytes" : "")}{(c.ReadOnly ? ", solo lectura" : "")}): " +
                          $"provisorio \"{c.Guess}\", vale {c.Value} ahora. {c.Evidence}");
        return sb.ToString();
    }
}

/// <summary>Advisors that know the game and can say what each verified variable is (an AI, an agent).</summary>
public interface IVariableNamer
{
    Task<List<VariableName>> NameVariablesAsync(VariableQuestion question, CancellationToken ct);
}

/// <summary>Someone who decides what to press to get from the power-on to playing: the built-in key trial or an AI.</summary>
public interface IPlayAdvisor
{
    string Name { get; }
    Task<PlayAdvice> AdviseAsync(PlayObservation observation, CancellationToken ct);
}

/// <summary>
/// The built-in advisor: presses the first usual start key (fire, space, return, F1, N to
/// a trainer's questions...) that takes the game to a screen not seen before; when no key
/// does anything, waits (an intro or a load is going on).
/// </summary>
public sealed class KeyTrialAdvisor : IPlayAdvisor
{
    private string? _last;
    private int _streak;

    public string Name => "prueba de teclas";

    public Task<PlayAdvice> AdviseAsync(PlayObservation o, CancellationToken ct)
    {
        // the same key three times in a row without getting anywhere (pages of a document, a
        // menu that cycles) gives way to the next one
        var usable = o.KeyEffects.Where(e => !(e.Key == _last && _streak >= 3)).ToList();
        var pick = usable.FirstOrDefault(e => e.NewScreen) ?? usable.FirstOrDefault(e => e.Changes);
        _streak = pick != null && pick.Key == _last ? _streak + 1 : 1;
        _last = pick?.Key;
        var advice = pick != null
            ? new PlayAdvice { Reason = $"{pick.Key} {pick.Text}", Actions = { new PlayAction(pick.Key, 6, 150) } }
            : new PlayAdvice { Reason = "ninguna tecla cambia nada: espero", Actions = { new PlayAction("", 0, 300) } };
        return Task.FromResult(advice);
    }
}

public sealed class PrepareOptions
{
    public GameProfile? PreviousProfile { get; set; }
    public PreparationContinuation? Resume { get; set; }
    public IReadOnlyList<PreparationExperimentResult> PreviousExperiments { get; set; } = [];
    public int InvestigationPass { get; set; } = 1;
    public bool ContinueResearch { get; set; }
    public string Goal { get; set; } = "";
    public string FirmwareDirectory { get; set; } = "";
    /// <summary>The user's AI; null = only the built-in key trial.</summary>
    public IPlayAdvisor? Advisor { get; set; }
    /// <summary>Rounds of the key trial before asking the AI (all rounds when there is no AI).</summary>
    public int TrialRounds { get; set; } = 3;
    public int MaxRounds { get; set; } = 24;
    /// <summary>Where the profile is saved; null = the user's profiles folder.</summary>
    public string? ProfilesFolder { get; set; }
    public int LabFrames { get; set; } = 3000;
    public bool Save { get; set; } = true;
}

public sealed class PrepareResult
{
    public PreparationContinuation? Continuation { get; set; }
    public List<PreparationExperimentResult> Experiments { get; set; } = new();
    public string Goal { get; set; } = "";
    public List<string> CheckedData { get; set; } = new();
    public List<string> UnresolvedData { get; set; } = new();
    public bool Ok { get; set; }
    public string? Problem { get; set; }
    public string Game { get; set; } = "";
    public string Machine { get; set; } = "";
    public string File { get; set; } = "";
    public string? Member { get; set; }
    public string Sha256 { get; set; } = "";
    public List<string> Boot { get; set; } = new();
    public ControlSet? Controls { get; set; }
    public int Rounds { get; set; }
    public List<string> Advisors { get; set; } = new();
    public LabReport? Lab { get; set; }
    public GameProfile? Profile { get; set; }
    public string? ProfilePath { get; set; }
    public List<string> Log { get; set; } = new();
}

/// <summary>
/// Prepares any game of the user's folder for the fusions without a profile written by
/// hand: boots it, finds the way from the power-on to playing (the key trial and then the
/// user's AI, checking every time with <see cref="PlayDetector"/>), studies it with the lab
/// (lives, score, time, positions, each verified by experiment) and saves a profile tied to
/// the SHA-256 of the user's copy. The boot is replayed on a new machine before saving:
/// the profile only says what was seen working.
/// </summary>
public static class GamePreparer
{
    /// <summary>Keys tried by the key trial, most usual first.</summary>
    public static readonly Dictionary<string, string[]> StartKeys = new()
    {
        ["c64"] = new[] { "JOY2 FIRE", "SPACE", "RETURN", "F1", "JOY1 FIRE", "N", "RUN/STOP", "Y", "F3", "F5", "F7", "1" },
        ["zx48"] = new[] { "ENTER", "SPACE", "0", "1", "KEMPSTON FIRE", "N", "S", "Y", "5", "M" },
        ["nes"] = new[] { "P1 START", "P1 A", "P1 SELECT", "P1 B" },
        ["invaders"] = new[] { "COIN", "P1 START", "P1 FIRE" },
    };

    /// <summary>Machines supported by the current 16-bit profile and observation tools.</summary>
    public static bool CanPrepare(string machine) => StartKeys.ContainsKey(machine);

    private const int EffectFrames = 180;

    /// <summary>The machine and file of a set of files (the best implemented one), or the reason there is none.</summary>
    public static (string? machine, MediaFile? media, string reason) Choose(IReadOnlyList<MediaFile> files)
    {
        if (MachineCatalog.IdentifySet(files) is { } board)
            return (board.Machine.Id, files[0], board.Reason);
        var best = files
            .SelectMany(MachineCatalog.Identify)
            .Where(c => c.Confidence >= 50 && CanPrepare(c.Machine.Id) && MachineCatalog.NotLoadableYet(c) == null)
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => MediaArchive.DumpScore(c.File.Name))
            .FirstOrDefault();
        return best == null ? (null, null, "ninguna máquina disponible reconoce este archivo") : (best.Machine.Id, best.File, best.Reason);
    }

    /// <summary>A readable title from the file name ("Spy_vs_Spy_C64_EN.zip" → "Spy vs Spy").</summary>
    public static string GameNameOf(string path, string? machine)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith(".tzx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tap", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return machine == "invaders" ? "Space Invaders" : CompatDiscovery.GameName(name);
    }

    public static string Sha256Of(MediaFile f) => Convert.ToHexString(SHA256.HashData(f.Data)).ToLowerInvariant();

    public static async Task<PrepareResult> PrepareAsync(string path, PrepareOptions o, Action<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string goal = (o.Goal ?? "").Trim();
        var r = new PrepareResult { Goal = goal.Length > 4000 ? goal[..4000] : goal };
        void Say(string text)
        {
            r.Log.Add(text);
            progress?.Invoke(text);
        }
        var files = MediaArchive.Open(path);
        var (machine, media, reason) = Choose(files);
        r.Game = GameNameOf(path, machine);
        if (machine == null || media == null)
        {
            r.Problem = reason;
            return r;
        }
        r.Machine = machine;
        r.File = Path.GetFileName(path);
        r.Member = files.Count > 1 ? media.Name : null;
        r.Sha256 = Sha256Of(media);
        var previous = o.PreviousProfile != null ? GameProfile.Parse(o.PreviousProfile.ToJson()) : ProfileLibrary.ForFiles(files);
        if (previous?.Machine != machine || previous.Match.Sha256.Contains(r.Sha256, StringComparer.OrdinalIgnoreCase) != true) previous = null;
        if (o.Resume is { } requested && (requested.Machine != machine ||
            !requested.Sha256.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("La continuación de investigación corresponde a otra edición.");
        string profileFolder = o.ProfilesFolder ?? ProfileLibrary.UserFolder;
        string expectedProfilePath = Path.Combine(profileFolder, (previous?.Id ?? $"{Slug(r.Game)}-{r.Sha256[..8]}") + ".json");
        string? ProfileStamp() => File.Exists(expectedProfilePath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expectedProfilePath))) : null;
        string? originalProfileStamp = o.Save ? ProfileStamp() : null;
        bool recordingBoot = o.Resume == null;
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        LaunchResult Launch() => MachineLauncher.Launch(machine, files, machine == "invaders" ? null : media, o.FirmwareDirectory, folder);
        var launch = Launch();
        var m = launch.Machine;
        // probes run on a twin, so the real machine only sees the boot (and replays the same)
        var twin = Launch().Machine;
        string machineName = MachineCatalog.Get(machine).Name;
        Say($"{r.Game}: {machineName}, {launch.Media.Name} ({launch.Description}).");
        if (o.Resume == null && launch.IsTape && m is Spectrum48 zx)
        {
            Say("Cargando la cinta como lo haría una persona (LOAD \"\" y la cinta sonando)...");
            MachineLauncher.LoadTape(zx);
            r.Boot.Add("load-tape");
        }
        var sink = new short[8192];
        int frames = 0;
        void Wait(int n)
        {
            for (int i = 0; i < n; i++)
            {
                ct.ThrowIfCancellationRequested();
                m.RunFrame(null);
                m.DrainAudio(sink);
            }
            frames += n;
            if (!recordingBoot) return;
            if (r.Boot.Count > 0 && r.Boot[^1].StartsWith("wait "))
                r.Boot[^1] = $"wait {int.Parse(r.Boot[^1][5..]) + n}";
            else
                r.Boot.Add($"wait {n}");
        }
        void Press(string keys, int hold)
        {
            var list = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var k in list) m.SetKey(k, true);
            for (int i = 0; i < hold; i++)
            {
                ct.ThrowIfCancellationRequested();
                m.RunFrame(null);
                m.DrainAudio(sink);
            }
            foreach (var k in list) m.SetKey(k, false);
            frames += hold;
            if (recordingBoot) r.Boot.Add($"press {string.Join('+', list)} {hold}");
        }
        PlayCheck CheckTwin()
        {
            twin.LoadState(m.SaveState());
            return PlayDetector.Detect(twin, machine);
        }

        var validKeys = m.Keys.Select(k => k.Name).Concat(PlayDetector.SetsFor(machine).SelectMany(s => s.Keys)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trial = new KeyTrialAdvisor();
        var seen = new List<string>();
        var history = new List<string>();
        if (o.Resume is { } resume)
        {
            m.LoadState(resume.State); m.ReleaseAllKeys();
            r.Boot = resume.Boot.ToList(); frames = resume.Frames;
            Say($"Retomando la partida observada para la investigación {o.InvestigationPass}.");
        }
        else Wait(100);
        seen.Add(Signature(m));
        PlayCheck check = CheckTwin();
        bool accepted = false;
        PlayAdvice? lastAdvice = null;
        for (int round = 1; round <= o.MaxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();
            bool aiDriving = o.Advisor != null && round > o.TrialRounds;
            // the controls move something: that is the game, unless an AI is driving and has not
            // said so (a shop or menu cursor also moves with the directions): then it is asked
            if (check.Playing && (!aiDriving || lastAdvice?.Playing == true))
            {
                accepted = true;
                break;
            }
            r.Rounds = round;
            var advisor = aiDriving ? o.Advisor! : trial;
            if (!r.Advisors.Contains(advisor.Name))
                r.Advisors.Add(advisor.Name);
            var observation = Observe(m, twin, machine, machineName, r.Game, launch.Media.Name, round, frames, seen, history,
                check.Playing
                    ? check with { Evidence = check.Evidence + ". Si esto ya es la partida, respondé playing=true y ninguna acción; si es un menú, una tienda o una demo, seguí." }
                    : check, r.Goal, ct);
            Say($"Ronda {round}: {advisor.Name} mira la pantalla...");
            PlayAdvice advice;
            try
            {
                advice = await advisor.AdviseAsync(observation, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException && advisor != trial)
            {
                Say($"La IA falló ({e.Message}); sigo con la prueba de teclas.");
                history.Add($"Ronda {round}: la IA no respondió ({e.Message}).");
                r.Problem = null;
                o.Advisor = null;
                advisor = trial;
                advice = await trial.AdviseAsync(observation, ct);
            }
            lastAdvice = advice;
            if (check.Playing && advice.Playing)
            {
                history.Add($"Ronda {round} ({advisor.Name}): confirma que es la partida. Motivo: {advice.Reason}.");
                Say(history[^1]);
                accepted = true;
                break;
            }
            var done = new List<string>();
            foreach (var a in advice.Actions.Take(16))
            {
                var keys = a.Keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var wrong = keys.Where(k => !validKeys.Contains(k)).ToList();
                if (wrong.Count > 0)
                {
                    done.Add($"(tecla inexistente: {string.Join(", ", wrong)})");
                    continue;
                }
                if (keys.Length > 0)
                {
                    Press(string.Join('+', keys), Math.Clamp(a.Hold, 1, 120));
                    done.Add($"{string.Join('+', keys)} {Math.Clamp(a.Hold, 1, 120)} cuadros");
                }
                if (a.Wait > 0)
                {
                    Wait(Math.Clamp(a.Wait, 1, 3000));
                    done.Add($"esperar {Math.Clamp(a.Wait, 1, 3000)}");
                }
            }
            if (done.Count == 0)
            {
                Wait(120);
                done.Add("esperar 120 (no hubo acciones válidas)");
            }
            seen.Add(Signature(m));
            check = CheckTwin();
            string after = check.Playing ? "los controles mueven algo: " + check.Evidence : "todavía no se juega";
            history.Add($"Ronda {round} ({advisor.Name}): {string.Join(", ", done)}. Motivo: {advice.Reason}. Resultado: {after}." +
                        (advice.Playing && !check.Playing ? " (Se dijo que era la partida, pero los controles no mueven nada.)" : ""));
            Say(history[^1]);
        }
        if (!accepted && check.Playing && (o.Advisor == null || lastAdvice?.Playing == true))
            accepted = true;
        if (!accepted)
        {
            r.Problem = $"No llegué a la partida en {o.MaxRounds} rondas. " + (o.Advisor == null && r.Advisors.Count == 1
                ? "Con una IA configurada (Claude, ChatGPT o Gemini por suscripción) se puede intentar de nuevo."
                : "No se pudo confirmar el arranque con esta preparación. Revisá la actividad o intentá otra estrategia.");
            return r;
        }
        // A future directional test can cross the last frames of a level-intro screen.
        // Wait for the entity to exist now, and record the wait in the replayable boot.
        if (machine == "nes" && m is Retro.Systems.Nes.Nes nes &&
            !Enumerable.Range(0, 64).Any(i => nes.Ppu.Oam[i * 4] < 239))
        {
            byte[] beforeTransition = m.SaveState();
            var beforeBoot = r.Boot.ToList();
            int beforeFrames = frames;
            Say("Esperando que termine la transición de la partida...");
            for (int attempts = 0; attempts < 4 && !Enumerable.Range(0, 64).Any(i => nes.Ppu.Oam[i * 4] < 239); attempts++)
                Wait(30);
            var afterTransition = CheckTwin();
            if (afterTransition.Playing) check = afterTransition;
            else
            {
                m.LoadState(beforeTransition); r.Boot = beforeBoot; frames = beforeFrames;
                Say("No se confirmó una transición jugable; se conserva el arranque anterior.");
            }
        }
        r.Controls = check.Controls;
        Say($"En la partida después de {frames} cuadros: {check.Evidence}.");

        // ---------------------------------------------------------------- the lab
        Say("Laboratorio: observando la partida y buscando vidas, tiempo, posiciones y entidades...");
        var c = check.Controls!;
        var lab = MemoryLab.Analyze(m, machine, new MemoryLab.Options(IdleFrames: o.LabFrames), (c.Left, c.Right, c.Up, c.Down), ct);
        lab.Game = r.Game;
        lab.Script = "preparación automática";
        lab.Sha256 = r.Sha256;
        if (machine == "nes" && r.Boot.Any(step => step.StartsWith("press P1 START ", StringComparison.Ordinal)))
            lab.CheckedControls["start"] = "P1 START";
        ct.ThrowIfCancellationRequested();
        var play = PlayExperiment(c, machine);
        Say("Laboratorio: jugando al azar (siempre el mismo azar) para encontrar el puntaje...");
        var (played, idle) = LabExperiments.Run(m, machine, play, ct: ct);
        int start = MemoryLab.RangeFor(machine).start;
        var scores = LabExperiments.Scores(played, idle, start, $"\"{play.Description}\"");
        lab.Findings.AddRange(scores);
        // somebody playing dies more often than nobody touching: lives seen only then count too
        var livesInPlay = MemoryLab.LivesIn(played, start, 5, "jugando al azar")
            .Where(l => lab.Findings.All(f => f.Kind != "lives" || f.Address != l.Address)).ToList();
        lab.Findings.AddRange(livesInPlay);
        lab.Method += $" Experimento de puntaje: {play.Description}, {play.Frames} cuadros contra los mismos sin tocar nada.";
        Say("Laboratorio: verificando cada hipótesis con un experimento...");
        LabVerifier.VerifyBest(m, lab, scores.Concat(livesInPlay).ToDictionary(f => f, _ => play), ct: ct);
        ct.ThrowIfCancellationRequested();
        LabExperiment? advance = null;
        if (r.Goal.Length > 0 && o.Advisor is IPreparationExperimentAdvisor planner)
        {
            Say("Preparación dirigida: buscando acciones para observar los datos pedidos...");
            try
            {
                var experiments = await planner.PlanExperimentsAsync(new PreparationExperimentQuestion
                {
                    Goal = r.Goal, Game = r.Game, Machine = machine, MachineName = machineName,
                    Picture = Copy(m.Video), Controls = c, Keys = validKeys.ToList(), Candidates = Candidates(lab, m),
                    Pass = o.InvestigationPass, PreviousExperiments = o.PreviousExperiments.TakeLast(18).ToArray(),
                    KnownVariables = previous?.Variables.Keys.ToArray() ?? [],
                }, ct);
                foreach (var experiment in (experiments ?? new()).Take(PreparationExperiments.MaxExperiments))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!PreparationExperiments.TryValidate(experiment, validKeys, out string problem))
                    {
                        Say("Experimento descartado: " + problem); r.UnresolvedData.Add(problem);
                        r.Experiments.Add(new("invalid", experiment.Description ?? "", "Descartado: " + problem, true)); continue;
                    }
                    string signature = PreparationResearch.Signature(experiment, m.SaveState());
                    if (o.PreviousExperiments.Concat(r.Experiments).Any(e => e.Signature == signature))
                    {
                        r.Experiments.Add(new(signature, experiment.Description ?? "", "Ya se probó esta secuencia desde este mismo estado.", true));
                        continue;
                    }
                    Say("Probando: " + experiment.Description);
                    var proposed = LabExperiments.Analyze(m, machine, experiment, ct: ct).Where(f =>
                    {
                        uint address = Convert.ToUInt32(f.Address, 16);
                        var range = MemoryLab.RangeFor(machine);
                        return address >= range.start && address + (uint)Math.Max(1, f.Bytes) - 1 <= range.end;
                    }).OrderByDescending(f => f.Confidence).Take(3).ToList();
                    foreach (var finding in proposed)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (lab.Findings.Any(f => f.Address == finding.Address && f.Kind == finding.Kind && f.Encoding == finding.Encoding && f.Bytes == finding.Bytes && f.Status == "verified")) continue;
                        LabVerifier.Verify(m, finding, experiment, frames: experiment.Frames, ct: ct);
                        lab.Findings.Add(finding);
                    }
                    ct.ThrowIfCancellationRequested();
                    string outcome = $"Experimento repetido: {experiment.Description}; {proposed.Count(f => f.Status == "verified")} candidatos con efecto observado. " +
                        string.Join(" ", proposed.Select(f => $"0x{f.Address}: {f.Status}; {string.Join("; ", f.Verification?.Observations.Take(2) ?? [])}"));
                    r.CheckedData.Add(outcome);
                    r.Experiments.Add(new(signature, experiment.Description ?? "", outcome));
                    advance = experiment;
                }
                r.Advisors.Add(planner is IPlayAdvisor playingPlanner ? playingPlanner.Name + " (experimentos)" : "experimentos dirigidos");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                string problem = "No se completaron los experimentos dirigidos: " + error.Message;
                Say(problem); r.UnresolvedData.Add(problem);
            }
        }
        r.Lab = lab;

        // ---------------------------------------------------------------- what each variable is
        var candidates = Candidates(lab, m);
        IReadOnlyList<VariableName>? names = null;
        if (o.Advisor is IVariableNamer namer && candidates.Count > 0)
        {
            Say($"{o.Advisor.Name} mira la partida y nombra los {candidates.Count} datos observados...");
            try
            {
                names = await namer.NameVariablesAsync(new VariableQuestion
                {
                    Goal = r.Goal, Game = r.Game, MachineName = machineName, Picture = Copy(m.Video), Candidates = candidates,
                }, ct);
                r.Advisors.Add($"{o.Advisor.Name} (nombres)");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Say($"No pudo nombrarlas ({e.Message}); quedan los nombres del laboratorio.");
            }
        }

        // ---------------------------------------------------------------- the profile
        var profile = BuildProfile(r, machineName, lab, c, names, sourcePath: path);
        PreservePrevious(profile, previous);
        r.Profile = profile;
        Say("Comprobando el perfil: arranco una máquina nueva con el arranque guardado...");
        var replay = MachineFactory.Create(new GameSource(profile, files, folder), o.FirmwareDirectory);
        MachineFactory.Boot(replay, profile);
        var again = PlayDetector.Detect(replay, machine, c);
        if (!again.Playing)
        {
            r.Problem = "El arranque guardado no llega a la partida en una máquina nueva (el emulador no se comportó igual dos veces).";
            Say(r.Problem);
            return r;
        }
        profile.Verified += " Comprobado: una máquina nueva con este arranque llega a la partida (" + again.Evidence + ").";
        r.CheckedData.Add("El arranque guardado se reprodujo en una máquina nueva y respondió a los controles.");
        foreach (var control in lab.CheckedControls)
            r.CheckedData.Add($"Control observado: {control.Key} usa {control.Value}.");
        r.CheckedData.Add($"{profile.Variables.Count(v => !v.Value.ReadOnly)} variables disponibles con evidencia experimental o perfil previo; {profile.Variables.Count(v => v.Value.ReadOnly)} de solo lectura.");
        if (profile.Variables.Count == 0)
            r.UnresolvedData.Add("El arranque quedó preparado, pero no se identificaron variables reutilizables.");
        if (r.Goal.Length > 0)
            r.UnresolvedData.Add("Revisar qué requisitos del objetivo quedaron cubiertos por los datos observados: " + r.Goal);
        r.UnresolvedData.Add("La preparación no acredita por sí sola hitboxes, solidez, cámara, daño ni progreso; requieren evidencia específica.");
        lab.Notes.AddRange(r.CheckedData);
        lab.Notes.AddRange(r.UnresolvedData);
        profile.Verified += " Alcance de esta preparación: arranque/controles y variables descritas; no acredita automáticamente todos los requisitos de la fusión.";
        ct.ThrowIfCancellationRequested();
        if (o.ContinueResearch && advance != null)
        {
            Say("Conservando el avance de la partida para investigar los próximos eventos...");
            PreparationResearch.Advance(m, advance, ct);
            frames += advance.Frames;
        }
        r.Continuation = new(machine, r.Sha256, m.SaveState(), r.Boot.ToArray(), frames);
        if (o.Save)
        {
            if (!Path.GetFullPath(Path.Combine(profileFolder, profile.Id + ".json")).Equals(Path.GetFullPath(expectedProfilePath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                ProfileStamp() != originalProfileStamp)
                throw new InvalidDataException("El perfil cambió durante la investigación. No se sobrescribieron los cambios.");
            string dir = profileFolder;
            r.ProfilePath = ProfileLibrary.SaveTo(profile, dir);
            lab.Save(Path.Combine(dir, "lab", profile.Id + ".json"));
            Say($"Perfil guardado en {r.ProfilePath}.");
        }
        r.Ok = true;
        return r;
    }

    private static void PreservePrevious(GameProfile profile, GameProfile? previous)
    {
        if (previous == null) return;
        profile.Id = previous.Id; profile.Name = previous.Name;
        foreach (var item in previous.Variables)
        {
            if (profile.Variables.TryGetValue(item.Key, out var fresh))
            {
                bool sameField = fresh.Address == item.Value.Address && fresh.Encoding == item.Value.Encoding && fresh.Bytes == item.Value.Bytes &&
                    fresh.Mask == item.Value.Mask && fresh.HighBitAddress == item.Value.HighBitAddress && fresh.HighBitMask == item.Value.HighBitMask;
                if (sameField && item.Value.ReadOnly && !fresh.ReadOnly) continue;
                // Keep conflicting observations accessible without silently redirecting an existing helper's variable.
                if (!sameField && !fresh.ReadOnly)
                    profile.Variables.TryAdd(item.Key + "Observed" + fresh.Address + fresh.Encoding + fresh.Bytes, fresh);
            }
            profile.Variables[item.Key] = item.Value;
        }
        foreach (var item in previous.Controls) profile.Controls.TryAdd(item.Key, item.Value);
        profile.Sprites = new(previous.Sprites);
        profile.Helpers = previous.Helpers.ToList();
        profile.HelpersDoc = previous.HelpersDoc.ToList();
        profile.Description = previous.Description.Split("\nPreparación adicional:", 2)[0] + "\nPreparación adicional: " + profile.Description;
        profile.Verified = (previous.Verified.Length > 4000 ? previous.Verified[..4000] : previous.Verified) + "\n" + profile.Verified;
    }

    /// <summary>What a screen shows, coarse enough that an animation or a scroller does not make it another screen.</summary>
    public static string Signature(IMachine m) => ScreenReader.Text(m) is { Length: > 0 } text && text.Any(char.IsLetterOrDigit)
        ? text : ScreenReader.Sketch(m, 32, 12);

    /// <summary>Share of equal characters among the places where either screen shows something (1 = the same screen).</summary>
    public static double Similarity(string a, string b)
    {
        int shown = 0, same = 0;
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            char x = i < a.Length ? a[i] : ' ', y = i < b.Length ? b[i] : ' ';
            if (char.IsWhiteSpace(x) && char.IsWhiteSpace(y))
                continue;
            shown++;
            if (x == y) same++;
        }
        return shown == 0 ? 1 : (double)same / shown;
    }

    /// <summary>The screen and what each usual key does now, measured on the twin.</summary>
    private static PlayObservation Observe(IMachine m, IMachine twin, string machine, string machineName, string game, string file, int round,
        int frames, List<string> seen, List<string> history, PlayCheck check, string goal = "", CancellationToken ct = default)
    {
        byte[] state = m.SaveState();
        var sink = new short[8192];
        (string hash, string signature) After(string? key, int n)
        {
            twin.LoadState(state);
            twin.ReleaseAllKeys();
            if (key != null) twin.SetKey(key, true);
            for (int i = 0; i < n; i++)
            {
                if (i == 6 && key != null) twin.SetKey(key, false);
                ct.ThrowIfCancellationRequested();
                twin.RunFrame(null);
                twin.DrainAudio(sink);
            }
            return (ScreenReader.Hash(twin.Video), Signature(twin));
        }
        bool moves = After(null, 30).hash != ScreenReader.Hash(m.Video);
        var idle = After(null, EffectFrames);
        var effects = new List<KeyEffect>();
        foreach (var key in StartKeys.GetValueOrDefault(machine) ?? Array.Empty<string>())
        {
            var (h, signature) = After(key, EffectFrames);
            // same moment, same machine: any difference with the idle run is the key's doing
            bool changes = h != idle.hash;
            bool fresh = changes && Similarity(signature, idle.signature) < 0.9 && seen.All(x => Similarity(signature, x) < 0.9);
            effects.Add(new KeyEffect(key, changes, fresh, !changes ? "no cambia nada" : fresh ? "lleva a una pantalla nueva" : "cambia la pantalla (parecida a una ya vista)"));
        }
        return new PlayObservation
        {
            Goal = goal, Game = game, Machine = machine, MachineName = machineName, File = file, Round = round, FramesSoFar = frames,
            FramesPerSecond = m.FramesPerSecond,
            Keys = m.Keys.Select(k => k.Name).ToList(), ControlSets = PlayDetector.SetsFor(machine),
            ScreenText = ScreenReader.Text(m), Sketch = ScreenReader.Sketch(m), PictureMoves = moves,
            KeyEffects = effects, History = history.ToList(), PlayCheck = check.Evidence, Picture = Copy(m.Video),
        };
    }

    private static VideoFrame Copy(VideoFrame v)
    {
        var copy = new VideoFrame(v.Width, v.Height);
        v.Pixels.CopyTo(copy.Pixels, 0);
        return copy;
    }

    /// <summary>Always the same "random" play: directions and fire, to make the score go up.</summary>
    public static LabExperiment PlayExperiment(ControlSet c, string machine)
    {
        var e = new LabExperiment { Kind = "score", Description = $"jugar al azar con {c.Name} y disparar seguido", Frames = 2400 };
        var rng = new Random(1983);
        string[] moves = { c.Left, c.Right, c.Up, c.Down };
        for (int f = 0; f < e.Frames; f += 20)
        {
            e.Steps.Add(new CompatStep { Frame = f, Keys = c.Fire, Hold = 4 });
            e.Steps.Add(new CompatStep { Frame = f + 5, Keys = moves[rng.Next(moves.Length)], Hold = 10 + rng.Next(10) });
        }
        return e;
    }

    public static string Slug(string s) => Regex.Replace(Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-"), "-+", "-").Trim('-');

    /// <summary>The variables worth naming: the verified ones and the best read-only positions.</summary>
    public static List<VariableCandidate> Candidates(LabReport lab, IMachine m)
    {
        var picked = lab.Findings.Where(f => f.Status == "verified" && f.Kind != "entity-table")
            .Concat(new[] { "position-x", "position-y" }
                .Select(k => lab.Findings.Where(f => f.Kind == k && f.Status != "verified" && f.Confidence >= 0.4).MaxBy(f => f.Confidence))
                .Where(f => f != null && !lab.Findings.Any(v => v.Kind == f.Kind && v.Status == "verified"))!)
            .OrderByDescending(f => f!.Confidence).Take(12).ToList();
        return picked.Select(f => new VariableCandidate(f!.Address, f.Kind, f.Encoding, f.Bytes,
            new GameVariable { Address = f.Address, Encoding = f.Encoding, Bytes = f.Bytes, Max = int.MaxValue }.Read(m.Memory),
            f.Status != "verified", string.Join(" ", f.Evidence.Take(2)) + (f.Verification != null ? " Verificación: " + string.Join("; ", f.Verification.Observations.Take(2)) + "." : "")))
            .ToList();
    }

    /// <summary>A name for the fusion scripts: letters and digits, starting with a letter (g.get('name')).</summary>
    private static string CleanName(string name)
    {
        var clean = new string(name.Where(char.IsLetterOrDigit).ToArray());
        return clean.Length == 0 || !char.IsLetter(clean[0]) ? "" : char.ToLowerInvariant(clean[0]) + clean[1..];
    }

    /// <summary>
    /// A profile with the verified variables only (positions the game recomputes are
    /// read-only). With names from an advisor, those decide what each variable is called
    /// and which ones stay; without them, the lab's kinds do.
    /// </summary>
    public static GameProfile BuildProfile(PrepareResult r, string machineName, LabReport lab, ControlSet c, IReadOnlyList<VariableName>? names = null, string? sourcePath = null)
    {
        var p = new GameProfile
        {
            Id = $"{Slug(r.Game)}-{r.Sha256[..8]}",
            Name = $"{r.Game} ({machineName})",
            Machine = r.Machine,
            Match = new ProfileMatch { Sha256 = { r.Sha256 }, Member = r.Member },
            Boot = r.Boot.ToList(),
        };
        LabFinding? Best(string kind) => lab.Findings.Where(f => f.Kind == kind && f.Status == "verified").MaxBy(f => f.Confidence);
        string Why(LabFinding f) => f.Verification == null ? "" : " Verificada: " + string.Join("; ", f.Verification.Observations.Take(3)) + ".";
        int MaxFor(LabFinding f) => f.Encoding switch
        {
            "bcd" => (int)Math.Min(int.MaxValue, Math.Pow(10, f.Bytes * 2) - 1),
            "ascii" or "digits" => (int)Math.Min(int.MaxValue, Math.Pow(10, Math.Min(9, f.Bytes)) - 1),
            "uint16" => 0xFFFF,
            _ => 0xFF,
        };
        void Add(string name, LabFinding? f, string what, int? max = null)
        {
            if (f == null)
                return;
            p.Variables[name] = new GameVariable
            {
                Address = f.Address, Encoding = f.Encoding, Bytes = f.Bytes, Min = 0, Max = max ?? MaxFor(f),
                Description = "Hipótesis por variación: " + what + Why(f),
            };
        }
        var named = names?.Where(n => n.Keep && CleanName(n.Name).Length > 0).ToList();
        if (named != null)
        {
            foreach (var n in named)
            {
                var f = lab.Findings.Where(x => x.Address.Equals(n.Address.TrimStart('$'), StringComparison.OrdinalIgnoreCase) && x.Kind != "entity-table")
                    .OrderByDescending(x => x.Status == "verified").ThenByDescending(x => x.Confidence).FirstOrDefault();
                string name = CleanName(n.Name);
                if (f == null || p.Variables.ContainsKey(name))
                    continue;
                bool readOnly = f.Status != "verified";
                p.Variables[name] = new GameVariable
                {
                    Address = f.Address, Encoding = f.Encoding, Bytes = f.Bytes, Min = 0, Max = name == "lives" ? 9 : MaxFor(f), ReadOnly = readOnly,
                    Description = "Nombre propuesto por la IA: " + n.Description + (readOnly ? " (Solo lectura: el juego la recalcula.)" : Why(f)),
                };
            }
        }
        else
            AddByKind();
        void AddByKind()
        {
        Add("lives", Best("lives"), "Vidas (o intentos) que quedan.", 9);
        if (Best("score") is { } score)
            Add("score", score, "Puntaje.");
        else
            Add("counter", Best("counter"), "Contador que sube al jugar (objetos, enemigos o puntos).");
        Add("timer", Best("timer"), "Tiempo, aire o energía que se consume solo.");
        foreach (var (kind, name, what) in new[] { ("position-x", "x", "Posición horizontal del jugador"), ("position-y", "y", "Posición vertical del jugador") })
        {
            var stored = Best(kind);
            if (stored != null)
            {
                Add(name, stored, what + ".");
                continue;
            }
            // a position the game recomputes can still be read by the fusion rules
            var read = lab.Findings.Where(f => f.Kind == kind && f.Status != "verified" && f.Confidence >= 0.4).MaxBy(f => f.Confidence);
            if (read != null)
                p.Variables[name] = new GameVariable
                {
                    Address = read.Address, Min = 0, Max = 255, ReadOnly = true,
                    Description = "Hipótesis por variación: " + what + " (solo lectura: el juego la recalcula, escribirla no mueve al personaje). " + read.Evidence.FirstOrDefault(),
                };
        }
        }
        p.Controls["left"] = c.Left;
        p.Controls["right"] = c.Right;
        if (c.Up != c.Fire) p.Controls["up"] = c.Up;
        if (c.Down != c.Fire) p.Controls["down"] = c.Down;
        p.Controls["fire"] = c.Fire;
        foreach (var control in lab.CheckedControls) p.Controls[control.Key] = control.Value;
        var tables = lab.Findings.Where(f => f.Kind == "entity-table" && f.Status == "verified").ToList();
        p.Description = $"{r.Game} para {machineName} (copia {r.File}{(r.Member != null ? " > " + r.Member : "")}, SHA-256 {r.Sha256}). " +
                        $"Perfil preparado automáticamente por RetroEmu: se juega con {c.Name}." +
                        (tables.Count > 0 ? " Entidades en memoria: " + string.Join("; ", tables.Select(t => $"${t.Address}, {t.Count} registros de {t.Stride} bytes")) + "." : "");
        p.Verified = $"Preparado el {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. Arranque descubierto por {string.Join(" y ", r.Advisors.DefaultIfEmpty("el arranque solo"))} " +
                     $"en {r.Rounds} ronda(s), comprobado con los controles. Variables halladas por el laboratorio y verificadas escribiendo valores " +
                     $"y comparando con la misma corrida sin tocar ({p.Variables.Count(v => !v.Value.ReadOnly)} verificadas, {p.Variables.Count(v => v.Value.ReadOnly)} de solo lectura)" +
                     (named is { Count: > 0 } ? "; los nombres los puso la IA mirando la partida." : "; los nombres salen de cómo cambian (pueden no ser exactos).");
        if (sourcePath != null) RomIdentities.ApplyForFile(sourcePath, p);
        return p;
    }
}
