using System.Text;
using System.Text.Json;
using Retro.Core;
using Retro.Fusion;

namespace Retro.Ai;

public sealed record ModificationIdea(string Title, string Effect, IReadOnlyList<VariableEdit> Changes);
public sealed record ModificationAdvice(string Analysis, IReadOnlyList<ModificationIdea> Ideas);

/// <summary>Ideas about known variables only. The result contains no executable code or raw addresses.</summary>
public static class GameModificationAdvisor
{
    public const string Guide = """
        Analizá las variables conocidas de una partida retro y proponé de una a cuatro pruebas
        divertidas, distintas entre sí y específicas de este juego. Respondé en castellano.
        Usá únicamente los nombres de variables escribibles y sus rangos indicados. No inventes
        direcciones, variables, código ni capacidades. Las variables de solo lectura son contexto.
        Cada cambio indica variable, value y freeze (mantener el valor durante las escrituras de CPU).
        Explicá qué esperás observar y señalá las suposiciones: conocer una dirección no demuestra
        que un efecto funcione. No prometas invulnerabilidad si solo hay vidas de reserva.
        Podés proponer un desafío, una combinación extraña de recursos o explorar una regla;
        no repitas siempre vidas infinitas. Si el pedido no se puede hacer con estas variables,
        explicá la limitación y devolvé ideas vacías. No ejecutes comandos ni pidas archivos.
        El jugador elige qué probar; el programa guarda un punto para volver atrás. Volver restaura
        toda la partida a ese punto. La propuesta por sí sola no modifica nada.
        """;

    public static string Prompt(GameProfile profile, IMemoryView memory, string wish)
    {
        var text = new StringBuilder($"Juego: {profile.Name}. Máquina: {profile.Machine}.\n{profile.Description}\n");
        text.AppendLine("Variables conocidas en este momento (no es un descubrimiento automático de toda la memoria):");
        foreach (var (name, variable) in profile.Variables)
            text.AppendLine($"- {name}: valor={variable.Read(memory)}, rango={variable.Min}..{variable.Max}, " +
                $"{(variable.ReadOnly ? "SOLO LECTURA" : "escribible")}. {variable.Description}");
        text.AppendLine("Pedido del jugador: " + wish);
        return text.ToString();
    }

    public static JsonElement Schema() => JsonSerializer.SerializeToElement(new
    {
        type = "object", additionalProperties = false, required = new[] { "analysis", "ideas" },
        properties = new
        {
            analysis = new { type = "string", minLength = 1, maxLength = 3000 },
            ideas = new
            {
                type = "array", maxItems = 4,
                items = new
                {
                    type = "object", additionalProperties = false, required = new[] { "title", "effect", "changes" },
                    properties = new
                    {
                        title = new { type = "string", minLength = 1, maxLength = 100 }, effect = new { type = "string", minLength = 1, maxLength = 1600 },
                        changes = new
                        {
                            type = "array", minItems = 1, maxItems = 8,
                            items = new
                            {
                                type = "object", additionalProperties = false, required = new[] { "variable", "value", "freeze" },
                                properties = new { variable = new { type = "string", minLength = 1, maxLength = 100 }, value = new { type = "integer" }, freeze = new { type = "boolean" } }
                            }
                        }
                    }
                }
            }
        }
    });

    public static async Task<ModificationAdvice> ProposeAsync(IAiProvider ai, string prompt, GameProfile profile, CancellationToken ct)
    {
        string answer = await ai.CompleteAsync(new AiRequest(Guide, [new AiTurn(true, prompt)], Schema()), ct);
        ct.ThrowIfCancellationRequested();
        return Parse(answer, profile);
    }

    public static ModificationAdvice Parse(string answer, GameProfile profile)
    {
        try
        {
            int start = answer.IndexOf('{'), end = answer.LastIndexOf('}');
            if (start < 0 || end < start) throw new JsonException("Falta el objeto de respuesta.");
            using var document = JsonDocument.Parse(answer[start..(end + 1)]);
            var root = document.RootElement;
            Fields(root, "analysis", "ideas");
            string analysis = Text(root, "analysis", 3000);
            var ideas = root.GetProperty("ideas");
            if (ideas.ValueKind != JsonValueKind.Array || ideas.GetArrayLength() > 4) throw new JsonException("Se esperan hasta cuatro ideas.");
            var result = new List<ModificationIdea>();
            foreach (var idea in ideas.EnumerateArray())
            {
                Fields(idea, "title", "effect", "changes");
                string title = Text(idea, "title", 100), effect = Text(idea, "effect", 1600);
                var changes = new List<VariableEdit>();
                var list = idea.GetProperty("changes");
                if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 8) throw new JsonException("Se esperan de uno a ocho cambios por idea.");
                foreach (var change in list.EnumerateArray())
                {
                    Fields(change, "variable", "value", "freeze");
                    changes.Add(new VariableEdit(Text(change, "variable", 100), change.GetProperty("value").GetInt32(), change.GetProperty("freeze").GetBoolean()));
                }
                GameModifications.Validate(profile, changes);
                result.Add(new ModificationIdea(title, effect, changes));
            }
            return new ModificationAdvice(analysis, result);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new AiException("La propuesta no se puede usar: " + error.Message, error); }
    }

    private static void Fields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("Se esperaba un objeto.");
        var seen = new HashSet<string>();
        foreach (var property in element.EnumerateObject())
            if (!names.Contains(property.Name) || !seen.Add(property.Name)) throw new JsonException("Campo no permitido o repetido: " + property.Name);
        if (seen.Count != names.Length) throw new JsonException("Faltan campos en la propuesta.");
    }

    private static string Text(JsonElement parent, string name, int limit)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > limit)
            throw new JsonException("Texto inválido: " + name);
        return value.GetString()!;
    }
}
