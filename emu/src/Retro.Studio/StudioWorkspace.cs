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
    public static string Create(StudioGame game, string task, Func<string, GameProfile?> find, string? cliHint)
    {
        string dir = Path.Combine(Root, string.IsNullOrWhiteSpace(game.Id) ? "juego" : game.Id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "GUIA-FUSION.md"), StudioDesigner.Method);
        File.WriteAllText(Path.Combine(dir, "API.md"), StudioDesigner.ApiDoc);
        File.WriteAllText(Path.Combine(dir, "JUEGOS.md"), "# Los juegos de esta fusión\n\n" + StudioDesigner.GamesText(game.Games, find));
        File.WriteAllText(Path.Combine(dir, "ejemplo.js"), StudioDesigner.ExampleScript);
        File.WriteAllText(Path.Combine(dir, "ejemplo-diseño.md"), "# Diseño de Manic Invaders\n\n" + StudioDesigner.ExampleDesign + "\n");
        File.WriteAllText(Path.Combine(dir, GameFile), game.ToJson());
        File.WriteAllText(Path.Combine(dir, ScriptFile), game.Script);
        File.Delete(Path.Combine(dir, ReportFile));
        string test = cliHint != null
            ? $"Probalo vos mismo cuando quieras con:\n\n    {cliHint}\n\n  Imprime el informe de los robots y guarda capturas (prueba_titulo.png, prueba_jugando_8s.png, ...). Miralas."
            : "RetroEmu lo prueba solo cada vez que guardás juego.js (ver informe.md).";
        string tarea = $"""
            # Tarea: {(game.Script.Length == 0 ? "crear" : "mejorar")} un juego fusionado de RetroEmu

            RetroEmu corre juegos de 8 bits emulados de verdad y los FUSIONA con un guion de
            JavaScript que arma UN juego nuevo, en una sola pantalla, con historia y niveles.

            ## Qué hay que hacer

            {task}

            ## Cómo trabajar (en este orden)

            1. Leé `GUIA-FUSION.md` (el método: juego base, héroe, aportes, lo que se descarta,
               reglas, mundo, niveles e historia, legibilidad, estructura, pruebas), `API.md` (lo que
               puede hacer el guion) y `JUEGOS.md` (variables, controles, sprites y ayudas de cada
               juego; si un juego ofrece sus niveles originales, usalos).
            2. Mirá el ejemplo completo: `ejemplo-diseño.md` y `ejemplo.js`.
            3. Escribí el documento de diseño en el campo "design" de `{GameFile}` (una línea por
               parte: juego base, héroe, aportes, se descarta, objetivo, derrota, historia, niveles,
               controles, legibilidad) y completá "title", "approach", "pitch" y "howToPlay". No
               toques "games".
            4. Escribí el guion completo en `{ScriptFile}`.
            5. Probalo: {test}
            6. Corregí hasta que el informe diga LISTO y las capturas se entiendan. Revisá la lista
               de control de la guía y buscá estrategias que rompan el juego.

            Cada vez que guardes `{ScriptFile}`, RetroEmu también lo prueba con tres jugadores robot y
            escribe `{ReportFile}`. Cuando diga "Listo", el jugador ya lo puede jugar.
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
            ? "# Listo\n\nEl juego pasó la prueba automática de jugabilidad. El jugador ya lo puede probar en RetroEmu.\n\n" + report.Summary
            : "# Hay que corregir\n\n" + report.Summary + "\nCorregí juego.js y guardalo de nuevo.\n";
        File.WriteAllText(Path.Combine(dir, ReportFile), text);
    }
}
