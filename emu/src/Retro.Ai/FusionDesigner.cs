using System.Text;
using System.Text.Json;
using Retro.Fusion;

namespace Retro.Ai;

public sealed record DesignResult(FusionRecipe? Recipe, IReadOnlyList<string> Errors, int Attempts, string LastAnswer);

/// <summary>
/// Asks an AI to design a fusion recipe for games that have no registered one.
/// The AI gets the verified profiles of the games (RAM variables and controls), the
/// rule language and the JSON schema; its answer is validated by the same validator
/// the engine uses and, if it has mistakes, they are sent back for a corrected recipe.
/// </summary>
public sealed class FusionDesigner
{
    public const int MaxAttempts = 3;

    public static readonly string Guide = """
        Diseñás FUSIONES jugables entre videojuegos retro. Los juegos corren AL MISMO TIEMPO con
        emulación real de sus CPUs; vos no programás los juegos, los conectás:

        1. Vista ("layout"): "side" (uno al lado del otro), "overlay" (uno encima del otro, el de
           arriba semitransparente; "base" es el que va abajo y "opacity" la transparencia, 0.3-0.8)
           o "pip" (el "base" grande y los demás chicos en una esquina).
        2. Controles ("controls"): cada entrada del jugador (left, right, up, down, fire, jump,
           start, coin) aprieta una lista de controles de los juegos, escritos "slot.control"
           (los controles de cada juego están en su perfil). Pueden ser compartidos (un mismo
           botón mueve a los dos protagonistas) o repartidos.
        3. Reglas ("rules"): conectan los juegos leyendo y escribiendo variables de su RAM.
           - "when": "slot.variable CONDICION", con CONDICION = increases | decreases | changes |
             equals N | above N | below N. Se dispara una vez cuando el valor cambia así.
           - o "every": N  (cada N ticks; hay 60 ticks por segundo; mínimo 10).
           - "do": lista de acciones:
               "slot.variable = N" | "slot.variable += N" | "slot.variable -= N" |
               "slot.variable = otro.variable" | "press slot.control N" (aprieta N cuadros) |
               "pause slot N" (congela ese juego N ticks) | "swap" (cambia cuál juego va abajo) |
               "message texto".
           - "message": texto corto que ve el jugador cuando se dispara (opcional).
           - "cooldown": ticks mínimos entre dos disparos de la regla (opcional).
           Los valores escritos se recortan al mínimo/máximo de cada variable. Las variables
           "readOnly" solo se pueden leer.

        Reglas de diseño:
        - Usá SOLO los slots (a, b, c), variables y controles que aparecen en los perfiles.
        - Tiene que ser jugable: ninguna regla puede matar al jugador sin aviso ni en bucle;
          usá cooldown en reglas que podrían dispararse seguido; equilibrá premios y castigos.
        - Buscá interacciones con sentido entre los mundos de los juegos (lo que pasa en uno
          afecta al otro de una forma que se entienda) y que obliguen a prestar atención a ambos.
        - "name" con gancho y "description" en castellano que explique cómo se juega.
        - Respondé SOLO con el JSON de la receta.
        """;

    public static JsonElement Schema(IEnumerable<string> profileIds)
    {
        var strArray = new { type = "array", items = new { type = "string" } };
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "name", "description", "games", "layout", "controls", "rules" },
            properties = new
            {
                id = new { type = "string" },
                name = new { type = "string" },
                description = new { type = "string" },
                author = new { type = "string" },
                games = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "slot", "profile" },
                        properties = new
                        {
                            slot = new { type = "string", @enum = new[] { "a", "b", "c" } },
                            profile = new { type = "string", @enum = profileIds.ToArray() },
                        },
                    },
                },
                layout = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "mode", "base", "opacity" },
                    properties = new
                    {
                        mode = new { type = "string", @enum = new[] { "side", "overlay", "pip" } },
                        @base = new { type = "string", @enum = new[] { "a", "b", "c" } },
                        opacity = new { type = "number" },
                    },
                },
                controls = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "left", "right" },
                    properties = new
                    {
                        left = strArray, right = strArray, up = strArray, down = strArray,
                        fire = strArray, jump = strArray, start = strArray, coin = strArray,
                    },
                },
                rules = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "do" },
                        properties = new
                        {
                            when = new { type = "string" },
                            every = new { type = "integer" },
                            @do = strArray,
                            message = new { type = "string" },
                            cooldown = new { type = "integer" },
                        },
                    },
                },
            },
        };
        return JsonSerializer.SerializeToElement(schema);
    }

    /// <summary>What the AI needs to know about the games (verified profiles).</summary>
    public static string ProfilesText(IReadOnlyList<GameProfile> games)
    {
        var sb = new StringBuilder();
        foreach (var p in games)
        {
            sb.AppendLine($"Perfil \"{p.Id}\": {p.Name}");
            sb.AppendLine($"  {p.Description}");
            sb.AppendLine("  Variables:");
            foreach (var (name, v) in p.Variables)
                sb.AppendLine($"    {name} ({v.Min}..{v.Max}{(v.ReadOnly ? ", readOnly" : "")}): {v.Description}");
            sb.AppendLine("  Controles: " + string.Join(", ", p.Controls.Keys));
        }
        return sb.ToString();
    }

    public static string UserPrompt(IReadOnlyList<GameProfile> games, string instructions)
    {
        var slots = games.Select((g, i) => $"slot {(char)('a' + i)} = \"{g.Id}\"");
        return $"""
            Juegos a fusionar ({string.Join(", ", slots)}):

            {ProfilesText(games)}
            Pedido del jugador: {(string.IsNullOrWhiteSpace(instructions) ? "ninguno en particular: sorprendeme con una mezcla divertida y jugable." : instructions)}
            """;
    }

    /// <summary>Everything in one text, for pasting into any chat (ChatGPT, Gemini, Claude.ai...).</summary>
    public static string ManualPrompt(IReadOnlyList<GameProfile> games, string instructions) =>
        Guide + "\n\n" + UserPrompt(games, instructions) +
        "\n\nEsquema JSON de la respuesta:\n" + JsonSerializer.Serialize(Schema(games.Select(g => g.Id)), new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Parses and validates an answer from any AI.</summary>
    public static (FusionRecipe? recipe, List<string> errors) Check(string answer, IReadOnlyList<GameProfile> games, Func<string, GameProfile?> findProfile)
    {
        FusionRecipe recipe;
        try
        {
            recipe = FusionRecipe.Parse(answer);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            return (null, new List<string> { "La respuesta no es un JSON de receta válido: " + e.Message });
        }
        var errors = recipe.Validate(findProfile);
        var expected = games.Select(g => g.Id).OrderBy(x => x).ToList();
        if (!recipe.Games.Select(g => g.Profile).OrderBy(x => x).SequenceEqual(expected))
            errors.Add("La receta debe usar exactamente estos juegos: " + string.Join(", ", expected) + ".");
        return (errors.Count == 0 ? recipe : null, errors);
    }

    public async Task<DesignResult> DesignAsync(IAiProvider ai, IReadOnlyList<GameProfile> games, string instructions,
        Func<string, GameProfile?> findProfile, CancellationToken ct, Action<string>? progress = null)
    {
        var turns = new List<AiTurn> { new(true, UserPrompt(games, instructions)) };
        var schema = Schema(games.Select(g => g.Id));
        string answer = "";
        List<string> errors = new();
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            progress?.Invoke(attempt == 1 ? $"Pidiendo la receta a {ai.Name}..." : $"Pidiendo correcciones (intento {attempt})...");
            answer = await ai.CompleteAsync(new AiRequest(Guide, turns, schema), ct);
            (var recipe, errors) = Check(answer, games, findProfile);
            if (recipe != null)
            {
                if (string.IsNullOrWhiteSpace(recipe.Author))
                    recipe.Author = ai.Name;
                return new DesignResult(recipe, Array.Empty<string>(), attempt, answer);
            }
            turns.Add(new AiTurn(false, answer));
            turns.Add(new AiTurn(true, "La receta tiene estos problemas:\n- " + string.Join("\n- ", errors) +
                                       "\nDevolvé la receta completa corregida."));
        }
        return new DesignResult(null, errors, MaxAttempts, answer);
    }
}
