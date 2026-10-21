using Retro.Fusion;

namespace Retro.Studio;

/// <summary>
/// A folder for coding agents (Antigravity, Claude Code, Codex, Gemini CLI): the task, the
/// API, the games and the current script as files. The agent edits juego.js; the emulator
/// notices, tries it and writes informe.md (errors or "ok") so the agent can iterate.
/// </summary>
public static class StudioWorkspace
{
    public const string ScriptFile = "juego.js";
    public const string GameFile = "juego.json";
    public const string ReportFile = "informe.md";

    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RetroEmu", "agente-juegos");

    /// <summary>Creates the folder for a new game (from a concept or the player's idea) or a new version of one.</summary>
    public static string Create(StudioGame game, string task, Func<string, GameProfile?> find, string? cliHint, string? folder = null, OriginalReferences? references = null)
    {
        string dir = folder ?? Path.Combine(Root, string.IsNullOrWhiteSpace(game.Id) ? "juego" : game.Id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Retro.Ai.GameDesignGuide.FileName), Retro.Ai.GameDesignGuide.Text);
        File.WriteAllText(Path.Combine(dir, "CONTRATO.md"), StudioDesigner.CoreGuide);
        File.WriteAllText(Path.Combine(dir, "GUIA-FUSION.md"), StudioDesigner.Method);
        File.WriteAllText(Path.Combine(dir, "API.md"), StudioDesigner.ApiDoc);
        File.WriteAllText(Path.Combine(dir, "JUEGOS.md"), "# Los juegos de esta fusión\n\n" + StudioDesigner.GamesText(game.Games, find));
        File.WriteAllText(Path.Combine(dir, GameFile), game.ToJson());
        File.WriteAllText(Path.Combine(dir, ScriptFile), game.Script);
        if (references != null)
        {
            references.WriteTo(dir);
            File.WriteAllText(Path.Combine(dir, "ORIGINALES.md"), references.Describe(game.BaseSlot, true));
        }
        File.Delete(Path.Combine(dir, ReportFile));
        string test = cliHint != null
            ? $"Probalo vos mismo cuando quieras con:\n\n    {cliHint}\n\n  Imprime el informe de los robots y guarda capturas (prueba_titulo.png, prueba_jugando_8s.png, ...). Miralas."
            : "RetroEmu lo prueba solo cada vez que guardás juego.js (ver informe.md).";
        string tarea = $"""
            # Tarea: {(game.Script.Length == 0 ? "crear" : "mejorar")} un juego {(game.IsInspired ? "nuevo con originales" : "fusionado")} de RetroEmu

            RetroEmu corre juegos de 8 bits emulados de verdad y los FUSIONA con un guion de
            JavaScript que conecta sus reglas en una partida compartida. Conservá el mundo
            y la presentación del original base en una fusión. La creación con originales
            tiene escena y reglas propias, sin una base obligatoria.

            {StudioDesigner.DirectionText(game.Games, find, FusionDirection.From(game))}

            ## Qué hay que hacer

            {task}

            ## Cómo trabajar (en este orden)

            1. Leé `CONTRATO.md`, `API.md` (capacidades del guion) y `JUEGOS.md` (datos de los originales).
               `{Retro.Ai.GameDesignGuide.FileName}` y `GUIA-FUSION.md` amplían criterios cuando necesites profundizar;
               no hace falta releerlos en cada corrección.
               {(references != null ? "Revisá ORIGINALES.md y las capturas original-*.png si podés ver imágenes. Compará identidad, mapas e interfaz; no deduzcas direcciones de memoria de una captura." : "")}
            2. Diseñá una regla inseparable: quitar cualquier original debe cambiar una decisión
               central. Compará las variantes anteriores incluidas en la tarea. No partas de un
               guion de ejemplo para recolorearlo ni inventes datos ausentes de los perfiles.
            3. Escribí en "design" de `{GameFile}` la unión mecánica, regla única, diferencia,
               aportes, descartes, objetivo, final, progresión, controles, presentación y legibilidad.
               Completá "title" (hasta 60 caracteres), "approach" (hasta 180), "pitch" (una oración,
               hasta 240), "howToPlay" (objetivo y teclas, hasta 320) y "changes" (hasta 400).
               El diseño detallado no va en la ayuda del jugador. No toques "games", "baseSlot" ni "playerRequest".
               Si falta un dato indispensable, escribí qué falta en informe.md y detené la implementación.
            4. Escribí el guion completo en `{ScriptFile}`.
            5. Probalo: {test}
            6. Corregí los fallos conservando el pedido y lo que funciona. Revisá la lista de
               control y las capturas; probá también inicio, progreso, final, reinicio y estrategias
               que anulen el reto. Un informe LISTO no demuestra originalidad ni diversión.

            Cada vez que guardes `{ScriptFile}`, RetroEmu también lo prueba con tres jugadores robot y
            escribe `{ReportFile}`. Distinguí qué comprobó el robot y qué falta jugar u observar.
            Textos para el jugador en castellano.
            """;
        File.WriteAllText(Path.Combine(dir, "TAREA.md"), tarea);
        string pointer = "Leé TAREA.md y seguí sus instrucciones.\n";
        File.WriteAllText(Path.Combine(dir, "AGENTS.md"), pointer);
        File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), pointer);
        File.WriteAllText(Path.Combine(dir, "GEMINI.md"), pointer);
        return dir;
    }

    /// <summary>The game in the folder (juego.json + juego.js) and when the script was last written.</summary>
    public static (StudioGame game, DateTime written)? Read(string dir, StudioGame template)
    {
        string scriptPath = Path.Combine(dir, ScriptFile);
        if (!File.Exists(scriptPath))
            return null;
        var game = template.Copy();
        try
        {
            string gamePath = Path.Combine(dir, GameFile);
            if (File.Exists(gamePath))
            {
                var fromFile = StudioGame.Parse(File.ReadAllText(gamePath));
                game.Title = fromFile.Title;
                game.Approach = fromFile.Approach;
                game.Pitch = fromFile.Pitch;
                game.HowToPlay = fromFile.HowToPlay;
                game.Design = fromFile.Design;
                game.Changes = fromFile.Changes;
            }
        }
        catch (Exception)
        {
            // a half-written juego.json: keep the template's texts
        }
        game.Script = File.ReadAllText(scriptPath);
        return (game, File.GetLastWriteTimeUtc(scriptPath));
    }

    public static void WriteReport(string dir, ValidationReport report)
    {
        string text = report.Ok
            ? "# Listo\n\nPasó la prueba automática. Falta comprobar manualmente la originalidad, las decisiones, el equilibrio y la calidad visual.\n\n" + report.Summary
            : "# Hay que corregir\n\n" + report.Summary + "\nCorregí juego.js y guardalo de nuevo.\n";
        File.WriteAllText(Path.Combine(dir, ReportFile), text);
    }
}
