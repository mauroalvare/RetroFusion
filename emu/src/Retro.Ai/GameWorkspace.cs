using System.Text.Json;
using Retro.Asm;
using Retro.Compat;
using Retro.Core;

namespace Retro.Ai;

/// <summary>
/// A folder for coding agents (Claude Code, Codex, Antigravity, Gemini CLI) to write an 8-bit
/// game: the task, the machine guide and juego.s (the template to start from). The agent edits
/// juego.s; the emulator (or the agent itself with retro-cli probar) tries it and writes
/// informe.md and captura.png until it passes.
/// </summary>
public static class GameWorkspace
{
    public const string SourceFile = "juego.s";
    public const string InfoFile = "juego.json";
    public const string ReportFile = "informe.md";
    public const string PictureFile = "captura.png";

    public static string Root { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RetroEmu", "agente-creador");

    /// <summary>Creates a new task folder. <paramref name="cliHint"/> is how the agent can try the game itself, if it can run commands.</summary>
    public static string Create(string machine, string wish, Random rng, string? cliHint, string? folder = null)
    {
        var t = Templates.For(machine) ?? throw new ArgumentException($"no hay plantilla para {machine}");
        string dir = folder ?? Path.Combine(Root, $"{t.Machine}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "GUIA.md"), t.Guide);
        File.WriteAllText(Path.Combine(dir, "plantilla.s"), t.Source);
        File.WriteAllText(Path.Combine(dir, SourceFile), t.Source);
        File.WriteAllText(Path.Combine(dir, InfoFile), JsonSerializer.Serialize(new { title = "", pitch = "", howToPlay = "" },
            new JsonSerializerOptions { WriteIndented = true }));
        File.Delete(Path.Combine(dir, ReportFile));
        string test = cliHint != null
            ? $"Probalo vos mismo cuando quieras con:\n\n    {cliHint}\n\n   Imprime el informe y deja {PictureFile.Replace("captura", "juego")} y el programa al lado. Mirá la imagen."
            : $"RetroEmu lo prueba solo cada vez que guardás `{SourceFile}` y escribe `{ReportFile}` y `{PictureFile}`. Mirá la imagen.";
        string tarea = $"""
            # Tarea: escribir un juego original de 8 bits para {t.Name}

            RetroEmu emula el {t.Name} con precisión (CPU {t.Cpu.ToUpperInvariant()}, video y sonido). Hay que escribir
            un juego NUEVO en ensamblador que corra en esa máquina como cualquier juego de la época.

            ## Qué juego

            {GameWriter.Request(wish, rng)}

            ## Reglas

            - Original: no copies código, gráficos, música, nombres ni personajes de juegos existentes.
            - Un solo archivo, `{SourceFile}`. Ya tiene la plantilla: mantené su arranque (hasta `start`) y cambiá lo demás.
            - Jugable de verdad: el jugador se mueve con los controles que prueba el robot (los dice la guía), hay un
              objetivo o un reto que crece, puntaje o progreso a la vista, y al terminar la partida vuelve a empezar.
            - Gráficos claros (forma y color distinguen al jugador, los enemigos y lo que se junta) y sonidos cortos.

            ## Cómo trabajar

            1. Leé `GUIA.md`: memoria libre, pantalla, controles, sonido y la sintaxis del ensamblador de RetroEmu.
            2. Escribí el juego completo en `{SourceFile}` (la plantilla original queda en `plantilla.s`).
            3. Completá `{InfoFile}`: "title" (el nombre), "pitch" (de qué se trata, una oración) y "howToPlay".
            4. {test}
            5. Corregí hasta que `{ReportFile}` diga "Listo". La prueba ensambla, carga el juego, mira que arranque y
               dibuje, busca con qué controles se mueve el jugador (también después del disparo, por si hay título) y un
               robot juega al azar 30 segundos mirando que no se trabe.

            Textos para el jugador en castellano.
            """;
        File.WriteAllText(Path.Combine(dir, "TAREA.md"), tarea);
        string pointer = "Leé TAREA.md y seguí sus instrucciones.\n";
        foreach (var name in new[] { "AGENTS.md", "CLAUDE.md", "GEMINI.md" })
            File.WriteAllText(Path.Combine(dir, name), pointer);
        return dir;
    }

    /// <summary>The game in the folder (juego.s and the texts of juego.json) and when juego.s was last written.</summary>
    public static (GameDraft draft, DateTime written)? Read(string dir)
    {
        string path = Path.Combine(dir, SourceFile);
        if (!File.Exists(path))
            return null;
        string title = "", pitch = "", how = "";
        try
        {
            string info = Path.Combine(dir, InfoFile);
            if (File.Exists(info))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(info));
                var e = doc.RootElement;
                title = Str(e, "title");
                pitch = Str(e, "pitch");
                how = Str(e, "howToPlay");
            }
        }
        catch (JsonException)
        {
            // a half-written juego.json: the game still has its source
        }
        var draft = new GameDraft(title.Trim().Length > 0 ? title.Trim() : "Juego del agente", pitch, how, "", File.ReadAllText(path));
        return (draft, File.GetLastWriteTimeUtc(path));
    }

    public static void WriteReport(string dir, TrialReport report)
    {
        string text = report.Ok
            ? "# Listo\n\nEl juego pasó la prueba. El jugador ya lo puede jugar en RetroEmu.\n\n" + report.Summary
            : "# Hay que corregir\n\n" + report.Summary + $"\nCorregí {SourceFile} y guardalo de nuevo.\n";
        File.WriteAllText(Path.Combine(dir, ReportFile), text);
        if (report.LastPicture != null)
            PngWriter.Write(Path.Combine(dir, PictureFile), report.LastPicture, 2);
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
