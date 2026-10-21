using System.Text;
using Retro.Core;
using Retro.Fusion;

namespace Retro.Studio;

/// <summary>How one robot player did in the automatic playtest.</summary>
public sealed record RobotResult(
    string Name,
    double PlayedSeconds,
    double? FirstDeathSeconds,
    int Deaths,
    IReadOnlyDictionary<string, int> Causes,
    long Score,
    int MaxLevel,
    bool ReachedGameOver,
    bool? RestartWorked);

public sealed record ValidationReport(
    bool Ok,
    string? Error,
    IReadOnlyList<string> Problems,
    int Ticks,
    string Summary,
    VideoFrame? Picture,
    IReadOnlyList<RobotResult>? Robots = null,
    IReadOnlyList<(string Label, VideoFrame Frame)>? Shots = null,
    IReadOnlyList<string>? Notes = null);

/// <summary>
/// The automatic playtest of a fusion game, run before the player sees it. Three robot
/// players (one that stands still, one that wanders and jumps, one that shoots without
/// stopping) each start the game from the title, play for a while and, if the game ends,
/// try to play again. It checks the structure (title, playing, game over, restart), that
/// points can be scored, how fast the robots die and of what, and that the picture is
/// alive. The report is written for the AI (or agent) that has to fix the game.
/// </summary>
public static class StudioValidator
{
    public const string OwnSceneMessage = "screen.game se retiró: usá screen.base únicamente para el original declarado como base, " +
        "o dibujá una escena con sprites y tiles (screen.sprite, screen.rect); no pegues pantallas completas independientes.";
    public const int DefaultSecondsPerRobot = 30;
    private const int Tps = StudioSession.TicksPerSecond;

    public static ValidationReport Run(StudioGame game, IReadOnlyDictionary<string, GameSource> sources, string romDirectory,
        int secondsPerRobot = DefaultSecondsPerRobot)
    {
        StudioSession session;
        try
        {
            session = StudioSession.Start(game, sources, romDirectory);
        }
        catch (Exception e)
        {
            return new ValidationReport(false, "No se pudieron arrancar los juegos: " + e.Message, Array.Empty<string>(), 0, "", null);
        }
        return Run(session, secondsPerRobot);
    }

    private enum Strategy { Still, Wanderer, Shooter }

    public static ValidationReport Run(StudioSession s, int secondsPerRobot = DefaultSecondsPerRobot)
    {
        var problems = new List<string>();
        var notes = new List<string>();
        var robots = new List<RobotResult>();
        var shots = new List<(string, VideoFrame)>();
        int totalTicks = 0;
        bool titleChecked = false, frozen = false, blank = false;
        string? startProblem = null;
        string? error = s.Error;

        foreach (var strategy in new[] { Strategy.Still, Strategy.Wanderer, Strategy.Shooter })
        {
            if (error != null)
                break;
            if (strategy != Strategy.Still)
                s.Restart();
            error = s.Error;
            if (error != null)
                break;
            var rng = new Random(strategy switch { Strategy.Still => 1, Strategy.Wanderer => 2, _ => 3 });
            string name = strategy switch { Strategy.Still => "quieto", Strategy.Wanderer => "explorador", _ => "tirador" };
            int ticks = 0;
            bool Step(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    s.Update(1.0 / Tps);
                    ticks++;
                    // A script cannot hide the violation by catching the JavaScript error.
                    if (s.SceneViolation)
                    {
                        string message = s.SceneProblem ?? OwnSceneMessage;
                        if (!problems.Contains(message)) problems.Add(message);
                        return false;
                    }
                    if (s.Error != null)
                        return false;
                }
                return true;
            }
            void Inputs(bool left = false, bool right = false, bool up = false, bool fire = false, bool jump = false, bool start = false, bool down = false)
            {
                s.SetInput("left", left);
                s.SetInput("right", right);
                s.SetInput("up", up);
                s.SetInput("down", down);
                s.SetInput("fire", fire);
                s.SetInput("jump", jump);
                s.SetInput("start", start);
                s.SetInput("coin", false);
            }

            // 1. the title: nothing pressed for a second and a half
            Inputs();
            if (!Step(Tps * 3 / 2)) { error = s.Error; break; }
            if (!titleChecked)
            {
                titleChecked = true;
                if (!s.ReportsState)
                    problems.Add("El guion no informa en qué parte está: llamá a game.state('title') en la pantalla de título, game.state('playing') al jugar y game.state('gameover') al terminar.");
                else if (s.State != "title" && !(s.Game.BaseSlot != null && s.State == "playing"))
                    problems.Add($"Al arrancar no hay pantalla de título (estado \"{s.State}\"): el juego tiene que empezar mostrando el título, el objetivo y los controles, con los juegos en pausa, hasta que el jugador apriete start, Espacio o disparo.");
                shots.Add(("titulo", Clone(s.Video)));
            }

            // 2. start the game
            bool needsStart = s.State != "playing";
            Inputs(fire: needsStart, jump: needsStart, start: needsStart);
            if (!Step(4)) { error = s.Error; break; }
            Inputs();
            int waited = 0;
            while (s.ReportsState && s.State != "playing" && waited < Tps * 2)
            {
                if (!Step(1)) break;
                waited++;
            }
            if (s.Error != null) { error = s.Error; break; }
            if (s.ReportsState && s.State != "playing" && startProblem == null)
                startProblem = $"Después de apretar start el juego no empezó (estado \"{s.State}\", se esperaba \"playing\").";

            // 3. play
            int deaths0 = s.Deaths.Count;
            long score0 = s.Score;
            int maxLevel = s.Level;
            double? firstDeath = null;
            bool gameOver = false;
            bool? restartWorked = null;
            int playTicks = 0;
            int[]? lastFrame = null;
            int sameFrames = 0, frameChecks = 0;
            int dir = 0, vertical = 0, hold = 0, jumpHold = 0, fireHold = 0;
            while (playTicks < secondsPerRobot * Tps)
            {
                switch (strategy)
                {
                    case Strategy.Still:
                        Inputs();
                        break;
                    case Strategy.Wanderer:
                        if (--hold <= 0) { dir = rng.Next(3) - 1; if (s.Game.BaseSlot != null) vertical = rng.Next(3) - 1; hold = 30 + rng.Next(60); }
                        if (jumpHold > 0) jumpHold--; else if (rng.Next(40) == 0) jumpHold = 6;
                        if (fireHold > 0) fireHold--; else if (rng.Next(25) == 0) fireHold = 3;
                        Inputs(left: dir < 0, right: dir > 0, up: s.Game.BaseSlot != null ? vertical < 0 : jumpHold > 0,
                            down: s.Game.BaseSlot != null && vertical > 0, jump: jumpHold > 0, fire: fireHold > 0);
                        break;
                    default:
                        if (--hold <= 0) { dir = dir > 0 ? -1 : 1; hold = 50 + rng.Next(40); }
                        Inputs(left: dir < 0, right: dir > 0, fire: playTicks % 8 < 3, up: playTicks % 120 < 5);
                        break;
                }
                if (!Step(1)) break;
                playTicks++;
                if (firstDeath == null && s.Deaths.Count > deaths0)
                    firstDeath = playTicks / (double)Tps;
                maxLevel = Math.Max(maxLevel, s.Level);
                if (playTicks % (Tps * 3) == 0 && s.State != "gameover")
                {
                    frameChecks++;
                    if (lastFrame != null && lastFrame.AsSpan().SequenceEqual(s.Video.Pixels))
                        sameFrames++;
                    lastFrame = (int[])s.Video.Pixels.Clone();
                    if (s.Video.Pixels.Distinct().Take(2).Count() < 2)
                        blank = true;
                }
                if (strategy == Strategy.Shooter && (playTicks == Tps * 8 || playTicks == Tps * 20))
                    shots.Add(($"jugando_{playTicks / Tps}s", Clone(s.Video)));
                if (s.ReportsState && s.State == "gameover")
                {
                    gameOver = true;
                    shots.Add(($"fin_{name}", Clone(s.Video)));
                    Inputs();
                    if (!Step(Tps)) break;
                    Inputs(fire: true, jump: true, start: true);
                    if (!Step(4)) break;
                    Inputs();
                    int w = 0;
                    while (s.State == "gameover" && w < Tps * 2)
                    {
                        if (!Step(1)) break;
                        w++;
                    }
                    restartWorked = s.State is "title" or "playing";
                    break;
                }
            }
            if (s.Error != null) { error = s.Error; }
            if (frameChecks >= 3 && sameFrames >= frameChecks - 1)
                frozen = true;
            var causes = s.Deaths.Skip(deaths0).GroupBy(d => d.Cause).ToDictionary(g => g.Key, g => g.Count());
            robots.Add(new RobotResult(name, playTicks / (double)Tps, firstDeath, s.Deaths.Count - deaths0, causes,
                s.Score - score0, maxLevel, gameOver, restartWorked));
            totalTicks += ticks;
            if (error != null)
                break;
        }

        if (error == null)
        {
            if (startProblem != null)
                problems.Add(startProblem);
            if (blank)
                problems.Add("La pantalla quedó de un solo color mientras se jugaba: draw() no dibuja nada visible.");
            if (frozen)
                problems.Add("La imagen no cambia mientras se juega: ¿los juegos quedaron en pausa o draw() dibuja siempre lo mismo?");
            if (!s.ReportsScore && s.Game.BaseSlot == null)
                problems.Add("El guion nunca llamó a game.score(puntos): informá los puntos cada vez que el jugador los gana.");
            var active = robots.Where(r => r.Name != "quieto").ToList();
            if (s.ReportsScore && active.Count > 0 && active.All(r => r.Score == 0) && s.Game.BaseSlot == null)
                problems.Add($"Ningún robot pudo sumar puntos en {secondsPerRobot} segundos (el tirador dispara sin parar y se mueve de lado a lado): revisá que los disparos, los choques o el objetivo funcionen y que no sea imposible.");
            if (s.Game.BaseSlot != null)
            {
                foreach (var donor in s.Game.Games.Where(g => g.Slot != s.Game.BaseSlot && !s.ReferencedSlots.Contains(g.Slot)))
                    problems.Add($"El guion no usa datos, recursos ni controles de games.{donor.Slot}: falta integrar ese original en las reglas del juego base.");
                if (!s.ReportsScore || active.All(r => r.Score == 0))
                    notes.Add("No se observó puntaje en esta prueba. Comprobá los objetivos y el progreso del juego base; no agregues puntos ni un menú artificial para conformar al robot.");
                notes.Add("La prueba técnica no certifica que se hayan conservado los mapas, la estética ni una interacción mecánica real: hay que comparar capturas y jugar la fusión.");
            }
            if (active.Count > 0)
            {
                double avg = active.Average(r => r.FirstDeathSeconds ?? r.PlayedSeconds);
                if (avg < 6)
                    problems.Add($"Demasiado difícil: los robots activos pierden la primera vida a los {avg:F1} segundos en promedio. Empezá con menos enemigos, más lentos, más huecos o cobertura, y medio segundo de invulnerabilidad al aparecer.");
                else if (avg < 10)
                    notes.Add($"Algo difícil al principio: primera vida perdida a los {avg:F1} segundos en promedio.");
            }
            foreach (var r in robots.Where(r => r.ReachedGameOver && r.RestartWorked == false))
                problems.Add($"El robot {r.Name} llegó al fin del juego, apretó start y no volvió a empezar: después de game.state('gameover'), start tiene que reiniciar todo.");
            var allCauses = robots.SelectMany(r => r.Causes).GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.Sum(c => c.Value));
            int deaths = allCauses.Values.Sum();
            if (deaths >= 4 && allCauses.Max(c => c.Value) >= deaths * 0.8)
                notes.Add($"Casi todas las muertes son por \"{allCauses.MaxBy(c => c.Value).Key}\": quizás hay demasiado de eso.");
            if (deaths == 0)
                notes.Add($"Ningún robot perdió una vida en {robots.Count * secondsPerRobot} segundos de juego: si hay peligros, ¿se llama a game.death(causa)? Si no los hay, falta tensión.");
        }

        bool ok = error == null && problems.Count == 0;
        string summary = Summarize(s, error, problems, notes, robots);
        return new ValidationReport(ok, error, problems, totalTicks, summary, Clone(s.Video), robots, shots, notes);
    }

    private static VideoFrame Clone(VideoFrame f)
    {
        var c = new VideoFrame(f.Width, f.Height);
        f.Pixels.CopyTo(c.Pixels, 0);
        return c;
    }

    private static string Summarize(StudioSession s, string? error, List<string> problems, List<string> notes, List<RobotResult> robots)
    {
        var sb = new StringBuilder();
        sb.AppendLine(error != null ? "Falló: " + error
            : problems.Count == 0 ? "Pasó la prueba automática de jugabilidad."
            : "No pasó la prueba automática de jugabilidad.");
        foreach (var p in problems)
            sb.AppendLine("Problema: " + p);
        foreach (var n in notes)
            sb.AppendLine("Nota: " + n);
        foreach (var r in robots)
        {
            string death = r.FirstDeathSeconds is double d ? $"primera vida perdida a los {d:F1} s" : "no perdió vidas";
            string causes = r.Causes.Count > 0 ? " (" + string.Join(", ", r.Causes.Select(c => $"{c.Key} {c.Value}")) + ")" : "";
            string end = r.ReachedGameOver ? (r.RestartWorked == true ? "; llegó al fin y la revancha funcionó" : "; llegó al fin y la revancha NO funcionó") : "";
            sb.AppendLine($"Robot {r.Name}: jugó {r.PlayedSeconds:F0} s, {death}, {r.Deaths} vidas perdidas{causes}, {r.Score} puntos, nivel máximo {r.MaxLevel}{end}.");
        }
        sb.AppendLine($"Pantalla del juego: {s.Video.Width}x{s.Video.Height}.");
        if (s.Hud.Length > 0)
            sb.AppendLine("Último HUD: " + s.Hud);
        if (s.Events.Count > 0)
            sb.AppendLine("Mensajes mostrados: " + string.Join(" | ", s.Events.Select(e => e.Text).Distinct().Take(8)));
        if (s.Log.Count > 0)
            sb.AppendLine("Últimas líneas de log(): " + string.Join(" | ", s.Log.TakeLast(10)));
        return sb.ToString();
    }
}
