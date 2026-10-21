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
        File.WriteAllText(Path.Combine(dir, GameDesignGuide.FileName), GameDesignGuide.Text);
        File.WriteAllText(Path.Combine(dir, "CONTRATO.md"), GameDesignGuide.Compact);
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

            Escribí un juego NUEVO y ORIGINAL en ensamblador {t.Cpu.ToUpperInvariant()} para {t.Name}.
            Las instrucciones son independientes del agente o editor que uses.

            ## Qué juego

            {GameWriter.Brief(t, wish, rng)}

            ## Cómo trabajar

            1. Leé `CONTRATO.md` y `GUIA.md`
               (memoria, pantalla, controles, sonido y sintaxis del ensamblador).
               `{GameDesignGuide.FileName}` amplía los criterios de diseño si necesitás profundizar.
            2. Escribí una ficha breve de diseño en comentarios de `{SourceFile}` y luego el juego completo.
               `plantilla.s` aporta el arranque hasta `start` y demostraciones técnicas: no es el juego a recolorear.
               Implementá estados, reglas y recursos propios. No agregues dependencias.
            3. Completá `{InfoFile}`: "title" (hasta 60 caracteres), "pitch" (una oración, hasta 240)
               y "howToPlay" (objetivo y teclas, hasta 320). La explicación de diseño no va en la ayuda del jugador.
            4. {test}
            5. La prueba ensambla, carga, comprueba dibujo y movimiento sostenido con los controles documentados
               y ejecuta un robot aleatorio unos 30 segundos. Si aparece un fallo, corregí su causa conservando el pedido.
            6. Un informe "Listo" solo aprueba esa prueba técnica. Revisá también objetivo, decisiones, legibilidad,
               progreso, final y reinicio según el contrato de diseño. Si no pudiste probarlos, indicá qué falta.

            RetroEmu vuelve a probar `{SourceFile}` al guardarlo y actualiza `{ReportFile}`.
            Si no recibiste rutinas de ROMs analizadas, no afirmes haberlas estudiado ni copies código de los originales.

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
            ? "# Listo\n\nPasó la prueba técnica automática. Falta comprobar manualmente las decisiones, el objetivo, el equilibrio y la calidad visual.\n\n" + report.Summary
            : "# Hay que corregir\n\n" + report.Summary + $"\nCorregí {SourceFile} y guardalo de nuevo.\n";
        File.WriteAllText(Path.Combine(dir, ReportFile), text);
        if (report.LastPicture != null)
            PngWriter.Write(Path.Combine(dir, PictureFile), report.LastPicture, 2);
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
