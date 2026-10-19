using System.Text.Json;
using System.Text.RegularExpressions;
using Retro.Asm;
using Retro.Compat;
using Retro.Core;

namespace Retro.Ai;

/// <summary>A game written by an AI: what it is, how it is played and its whole source.</summary>
public sealed record GameDraft(string Title, string Pitch, string HowToPlay, string Changes, string Source);

/// <summary>The last draft, its trial in the emulator, how many tries it took and, if it failed, why.</summary>
public sealed record WriterOutcome(GameDraft? Game, TrialReport? Report, int Attempts, string? Problem)
{
    public bool Ok => Game != null && Report?.Ok == true;
}

/// <summary>
/// An AI writes an original 8-bit game in assembly, starting from the machine's template:
/// the program is assembled, loaded and played in the emulator (<see cref="HomebrewTrial"/>)
/// and the AI gets back the assembler errors, what the robot saw and a picture of the
/// screen, until the game passes. Each request is a single turn with the current source, so
/// the conversation never grows.
/// </summary>
public sealed class GameWriter
{
    public int MaxAttempts { get; init; } = 5;

    public static string Guide(GameTemplate t) => $"""
        Sos un programador de juegos de 8 bits. Escribís un juego NUEVO y ORIGINAL para {t.Name} en ensamblador
        {t.Cpu.ToUpperInvariant()}, que corre en una máquina emulada con precisión (CPU, video y sonido reales).

        Reglas:
        - Original: no copies código, gráficos, música, nombres ni personajes de juegos existentes.
        - Un solo archivo de ensamblador, completo. Partí de la plantilla: mantené su arranque (la primera parte, hasta
          `start`) y cambiá todo lo demás.
        - Jugable de verdad: el jugador se mueve con los controles que prueba el robot (abajo), hay un objetivo o un reto
          que crece, puntaje o progreso a la vista, y cuando la partida termina vuelve a empezar (nunca al BASIC).
        - Gráficos claros: el jugador, los enemigos y lo que se junta se distinguen por forma y color.
        - Sonidos cortos para lo importante (juntar, chocar, perder).
        - Que entre en la memoria libre y no use rutinas que la guía no permite.

        Cómo se prueba: ensamblo tu programa, lo cargo como cualquier juego, miro que arranque y dibuje, busco con qué
        controles se mueve algo (también después de apretar el disparo, por si hay una pantalla de título) y un jugador
        robot juega al azar 30 segundos mirando que no se trabe. Si algo falla te mando los errores del ensamblador o lo
        que vi, con una imagen de la pantalla, y corregís.

        {t.Guide}

        Plantilla de partida ({t.Machine}):
        ```asm
        {t.Source.TrimEnd()}
        ```
        """;

    public static JsonElement Schema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "title", "pitch", "howToPlay", "changes", "source" },
        properties = new
        {
            title = new { type = "string" },
            pitch = new { type = "string" },
            howToPlay = new { type = "string" },
            changes = new { type = "string" },
            source = new { type = "string" },
        },
    });

    private const string AnswerShape =
        "Devolvé title (el nombre del juego), pitch (de qué se trata, en una oración), howToPlay (controles y objetivo), changes (qué " +
        "hiciste o corregiste, en una o dos oraciones) y source (el programa COMPLETO en ensamblador, no solo los cambios).";

    public static string FirstPrompt(GameTemplate t, string wish, Random rng) =>
        (wish.Trim().Length > 0
            ? $"Pedido del jugador: {wish.Trim()}\nEl pedido manda; si deja algo libre, sorprendé (semilla por si sirve: {CreativeSeeds.DrawOriginal(rng)})."
            : "El jugador no pidió nada: sorprendelo con un juego que no se parezca al último que hiciste. Partí de esta semilla, sorteada esta " +
              $"vez (si una parte no encaja con la máquina, cambiala por algo igual de inesperado): {CreativeSeeds.DrawOriginal(rng)}.") +
        $"\nEscribí el juego para {t.Name}. " + AnswerShape;

    public static string FixPrompt(GameDraft current, TrialReport report) => $"""
        Este es tu juego "{current.Title}" como está ahora:
        ```asm
        {current.Source.TrimEnd()}
        ```
        Lo probé en el emulador y no pasó:
        {report.Summary.TrimEnd()}
        {(report.LastPicture != null ? "Te mando una imagen de la pantalla al final de la prueba." : "")}
        Corregí lo que falla sin perder lo que anda. {AnswerShape}
        """;

    public async Task<WriterOutcome> WriteAsync(IAiProvider ai, string machine, string wish, string firmwareDirectory, CancellationToken ct,
        Action<string>? progress = null, Random? rng = null)
    {
        var t = Templates.For(machine) ?? throw new ArgumentException($"no hay plantilla para {machine}");
        string prompt = FirstPrompt(t, wish, rng ?? Random.Shared);
        GameDraft? draft = null;
        TrialReport? report = null;
        string? picture = null;
        string? problem = null;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            progress?.Invoke(attempt == 1 ? $"{ai.Name} está escribiendo el juego..." : $"{ai.Name} está corrigiendo (intento {attempt} de {MaxAttempts})...");
            string answer;
            try
            {
                answer = await ai.CompleteAsync(new AiRequest(Guide(t), new[] { new AiTurn(true, prompt) }, Schema(), picture != null ? new[] { picture } : null), ct);
            }
            finally
            {
                if (picture != null)
                    try { File.Delete(picture); } catch (IOException) { }
                picture = null;
            }
            var next = ParseDraft(answer, out problem);
            if (next == null)
            {
                prompt = $"No pude leer tu respuesta: {problem}. " + (draft != null ? FixPrompt(draft, report!) : FirstPrompt(t, wish, rng ?? Random.Shared));
                continue;
            }
            draft = next;
            progress?.Invoke($"Probando \"{draft.Title}\" en el emulador...");
            report = await Task.Run(() => HomebrewTrial.Run(draft.Source, t.Machine, firmwareDirectory), ct);
            if (report.Ok)
                return new WriterOutcome(draft, report, attempt, null);
            problem = string.Join("; ", report.Checks.Where(c => !c.Ok && c.Required).Select(c => $"{c.Name}: {c.Detail.Split('\n')[0]}"));
            progress?.Invoke("No pasó: " + problem);
            if (report.LastPicture != null)
            {
                picture = Path.Combine(Path.GetTempPath(), $"retroemu-juego-{Guid.NewGuid():N}.png");
                PngWriter.Write(picture, report.LastPicture, 2);
            }
            prompt = FixPrompt(draft, report);
        }
        if (picture != null)
            try { File.Delete(picture); } catch (IOException) { }
        return new WriterOutcome(draft, report, MaxAttempts, problem ?? "no pasó la prueba");
    }

    /// <summary>Reads an answer: the JSON of the schema, or a JSON with the texts plus a ```asm block with the source.</summary>
    public static GameDraft? ParseDraft(string answer, out string? error)
    {
        error = null;
        string? source = null;
        var fence = Regex.Match(answer, "```(?:asm|s|z80|6502|assembly)?[ \\t]*\\r?\\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        string title = "", pitch = "", how = "", changes = "";
        int open = answer.IndexOf('{'), close = answer.LastIndexOf('}');
        if (open >= 0 && close > open)
        {
            try
            {
                using var doc = JsonDocument.Parse(answer[open..(close + 1)]);
                var e = doc.RootElement;
                title = Str(e, "title");
                pitch = Str(e, "pitch");
                how = Str(e, "howToPlay");
                changes = Str(e, "changes");
                string s = Str(e, "source");
                if (s.Trim().Length > 0)
                    source = s;
            }
            catch (JsonException ex)
            {
                error = "el JSON no es válido (" + ex.Message + ")";
            }
        }
        if (source == null && fence.Success)
            source = fence.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(source))
        {
            error ??= "falta el programa (source)";
            return null;
        }
        error = null;
        return new GameDraft(title.Trim().Length > 0 ? title.Trim() : "Juego sin nombre", pitch, how, changes, source.Replace("\r\n", "\n"));
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
