using System.Text.Json;
using Retro.Compat;

namespace Retro.Ai;

/// <summary>
/// The user's AI as the player who finds the way from the power-on to a game in
/// progress: every round it reads the screen (text and a rough drawing), what each usual
/// key does and what was already tried, and answers the next inputs. The preparer checks
/// every claim with the controls, so a wrong answer costs a round, not a wrong profile.
/// </summary>
public sealed class AiPlayAdvisor : IPlayAdvisor
{
    private readonly IAiProvider _ai;
    private readonly List<AiTurn> _turns = new();

    public AiPlayAdvisor(IAiProvider ai) => _ai = ai;

    public string Name => _ai.Name;

    public static readonly string Guide = """
        Sos un jugador experto en juegos retro (Commodore 64, ZX Spectrum, NES, arcades) y estás
        manejando un emulador. Objetivo: llegar desde el encendido hasta la PARTIDA, es decir el
        momento en que el jugador controla a su personaje (no el título, ni un menú, ni la demo, ni
        las instrucciones, ni la intro del grupo que hizo el crack).

        Cada turno te paso lo que muestra la pantalla (el texto que se lee y un dibujo aproximado),
        qué pasa al apretar las teclas habituales y lo que ya se hizo. Respondé con las próximas
        acciones, en orden. Cada acción aprieta "keys" (nombres EXACTOS de la lista de teclas;
        varias a la vez unidas con +, o vacío para solo esperar) durante "hold" cuadros y después
        espera "wait" cuadros. Hay 50 o 60 cuadros por segundo: una tecla se aprieta 6 a 10
        cuadros y después de cada pantalla conviene esperar 100 a 300.

        Consejos:
        - Ahorrá rondas: cada ronda cuesta tiempo. Cuando sepas lo que viene (una serie de
          preguntas parecidas, un nombre para escribir, los menús que conocés de este juego),
          mandá todas las acciones juntas en la misma respuesta (hasta 12), no una por ronda.
        - Si un texto se escribe solo o hay una animación, probá primero saltearla (ESPACIO,
          RETURN o disparo); si no se puede, esperá de una vez lo que haga falta (600 a 1500
          cuadros), no de a poco.
        - Usá lo que sabés de ese juego y de esa edición (menús, opciones, cómo empezar) para
          anticipar los pasos que siguen hasta la partida.
        - En los menús de trucos de los cracks ("trainer", "infinite lives? Y/N") contestá N:
          los trucos cambian las variables que después se estudian.
        - Si pide un nombre, escribilo con una tecla por acción y terminá con RETURN o ENTER.
        - Si una pantalla dice que se aprieta FIRE, usá el disparo del joystick que corresponda.
        - Si ya es la partida, poné "playing": true y ninguna acción.
        - Respondé SOLO con el JSON pedido.
        """;

    public static JsonElement Schema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "playing", "reason", "actions" },
        properties = new
        {
            playing = new { type = "boolean" },
            reason = new { type = "string" },
            actions = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "keys", "hold", "wait" },
                    properties = new
                    {
                        keys = new { type = "string" },
                        hold = new { type = "integer" },
                        wait = new { type = "integer" },
                    },
                },
            },
        },
    });

    public async Task<PlayAdvice> AdviseAsync(PlayObservation observation, CancellationToken ct)
    {
        _turns.Add(new AiTurn(true, observation.Describe()));
        string? picture = null;
        if (observation.Picture != null)
        {
            // the screen as it is: the text reader and the sketch miss custom fonts and graphics
            string dir = Path.Combine(Path.GetTempPath(), "RetroEmu-ia", "pantallas");
            Directory.CreateDirectory(dir);
            picture = Path.Combine(dir, $"ronda-{observation.Round:00}-{Guid.NewGuid():N}.png");
            Retro.Core.PngWriter.Write(picture, observation.Picture, 2);
        }
        string answer;
        try
        {
            answer = await _ai.CompleteAsync(new AiRequest(Guide, _turns.ToList(), Schema(), picture != null ? new[] { picture } : null), ct);
        }
        finally
        {
            if (picture != null)
                try { File.Delete(picture); } catch (IOException) { }
        }
        _turns.Add(new AiTurn(false, answer));
        // only the last few rounds travel: the history in each observation already says what was done
        while (_turns.Count > 6)
            _turns.RemoveAt(0);
        return Parse(answer);
    }

    /// <summary>Reads an answer (a JSON object, alone or inside prose or a ```json block).</summary>
    public static PlayAdvice Parse(string answer)
    {
        int start = answer.IndexOf('{'), end = answer.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new AiException("La IA no respondió un objeto JSON.");
        using var doc = JsonDocument.Parse(answer[start..(end + 1)]);
        var root = doc.RootElement;
        var advice = new PlayAdvice
        {
            Playing = root.TryGetProperty("playing", out var p) && p.ValueKind == JsonValueKind.True,
            Reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "",
        };
        if (root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            foreach (var a in actions.EnumerateArray())
            {
                string keys = a.TryGetProperty("keys", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : "";
                int hold = a.TryGetProperty("hold", out var h) && h.TryGetInt32(out int hv) ? hv : 8;
                int wait = a.TryGetProperty("wait", out var w) && w.TryGetInt32(out int wv) ? wv : 150;
                advice.Actions.Add(new PlayAction(keys.Trim(), hold, wait));
            }
        return advice;
    }
}

/// <summary>
/// An agent (Claude Code, Codex, Antigravity, a person) answers through files: each round
/// is written as ronda-NN.md in the folder, with the instructions in LEEME.md, and the
/// preparer waits for respuesta-NN.json. For AIs that are not called directly.
/// </summary>
public sealed class FolderPlayAdvisor : IPlayAdvisor
{
    private readonly string _folder;
    private readonly TimeSpan _timeout;

    public FolderPlayAdvisor(string folder, TimeSpan? timeout = null)
    {
        _folder = folder;
        _timeout = timeout ?? TimeSpan.FromMinutes(30);
    }

    public string Name => "un agente (carpeta " + _folder + ")";

    public async Task<PlayAdvice> AdviseAsync(PlayObservation observation, CancellationToken ct)
    {
        Directory.CreateDirectory(_folder);
        string readme = Path.Combine(_folder, "LEEME.md");
        if (!File.Exists(readme))
            File.WriteAllText(readme, "# Preparar un juego para las fusiones\n\n" + AiPlayAdvisor.Guide +
                "\n\nCada ronda está en ronda-NN.md. Escribí la respuesta en respuesta-NN.json (mismo número) con este esquema JSON:\n\n```json\n" +
                JsonSerializer.Serialize(AiPlayAdvisor.Schema(), new JsonSerializerOptions { WriteIndented = true }) + "\n```\n");
        string answerPath = Path.Combine(_folder, $"respuesta-{observation.Round:00}.json");
        if (File.Exists(answerPath))
            File.Delete(answerPath);
        string text = observation.Describe();
        if (observation.Picture != null)
        {
            // agents that can look at images read the screen better than the sketch
            string png = Path.Combine(_folder, $"ronda-{observation.Round:00}.png");
            Retro.Core.PngWriter.Write(png, observation.Picture, 2);
            text += $"\nCaptura de la pantalla: {Path.GetFileName(png)} (en esta carpeta).\n";
        }
        File.WriteAllText(Path.Combine(_folder, $"ronda-{observation.Round:00}.md"), text);
        var until = DateTime.UtcNow + _timeout;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(1000, ct);
            if (!File.Exists(answerPath))
                continue;
            try
            {
                return AiPlayAdvisor.Parse(File.ReadAllText(answerPath));
            }
            catch (Exception e) when (e is JsonException or IOException or AiException)
            {
                // still being written
            }
        }
        throw new AiException($"Nadie respondió {Path.GetFileName(answerPath)} en {_timeout.TotalMinutes:F0} minutos.");
    }
}
