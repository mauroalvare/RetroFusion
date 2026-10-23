using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Retro.Core;
using Retro.Fusion;

namespace Retro.Ai;

public sealed record MemoryEvidence(uint Address, byte Value, byte Initial, int Writes, uint? Writer, string Reference);
public sealed record AnalyzedVariable(string Name, string Label, uint Address, int Bytes, int Observed, string Reason);
public sealed record GameAnalysisResult(string Analysis, IReadOnlyList<AnalyzedVariable> Variables, ModificationAdvice Advice);

/// <summary>Copied request evidence, with no live machine or writable memory.</summary>
public sealed record GameAnalysisSnapshot(string Game, string Machine, long Frame, string Listing,
    IReadOnlyList<MemoryEvidence> Candidates, GameProfile Profile)
{
    public string Prompt(string wish, string previous = "")
    {
        var text = new StringBuilder($"Partida: {Game}. Sistema: {Machine}. Cuadro capturado: {Frame}.\n");
        text.AppendLine("Direcciones disponibles; valores y escrituras observados, no significado confirmado:");
        foreach (var item in Candidates)
            text.AppendLine($"0x{item.Address:X4}: ahora={item.Value}, al abrir={item.Initial}, escrituras={item.Writes}" +
                (item.Writer is uint pc ? $", última instrucción que escribió=0x{pc:X4}" : "") +
                (item.Reference.Length > 0 ? $". {item.Reference}" : ""));
        text.AppendLine("Variables conocidas (conservar nombre, rango y solo lectura):");
        foreach (var (name, variable) in Profile.Variables)
            text.AppendLine($"{name}: 0x{variable.Address}, {variable.Encoding}, rango={variable.Min}..{variable.Max}, " +
                $"{(variable.ReadOnly ? "solo lectura" : "escribible")}. {variable.Description}");
        text.AppendLine("Código capturado (un recorte, no todo el programa):\n" + Listing);
        if (previous.Length > 0) text.AppendLine("Informe anterior, como contexto y no evidencia adicional:\n" + previous[..Math.Min(previous.Length, 3000)]);
        text.AppendLine("Pregunta o pedido del jugador:\n" + (string.IsNullOrWhiteSpace(wish)
            ? "Encontrá modificaciones y reglas que valga la pena investigar con estos datos." : wish));
        return text.ToString();
    }
}

/// <summary>Discovery grounded in writes, memory values and a bounded disassembly. No automatic edits.</summary>
public static class GameModificationAnalyzer
{
    public const string Guide = """
        Analizá la evidencia de una partida de 8 bits y respondé en castellano. El jugador
        quiere investigar sus reglas y probar modificaciones, incluidos efectos inesperados.
        Separá lo observado de las hipótesis. Una escritura o un cambio de byte no demuestra
        su significado ni que modificarlo sea útil. No afirmes haber ejecutado pruebas.
        Respondé analysis, variables nuevas y hasta cuatro ideas de cambios.
        Cada variable nueva debe usar una dirección presente en la evidencia, nombre único
        de identificador, label claro, bytes 1 o 2 (uint16 little endian), observed igual al
        valor capturado y reason que conecte su hipótesis con instrucciones o escrituras reales.
        Para dos bytes, ambas direcciones deben estar en la evidencia. No inventes direcciones,
        código, variables, rangos verificados, significados ni funciones del juego.
        Las nuevas variables son experimentales, no se añaden a un perfil verificado de fusión.
        Podés investigar temporización, movimiento, reglas, interacción entre entidades y
        comportamientos extraños cuando los datos lo permitan. No propongas siempre reservas
        infinitas ni fuerces efectos que no se pueden justificar. Contestá también las preguntas;
        si falta evidencia, explicá qué acción del jugador permitiría obtenerla.
        Las ideas usan nombres ya conocidos o propuestos en variables, value y freeze.
        Respetá los rangos conocidos y solo lectura. Un byte admite 0..255 y una palabra 0..65535.
        No confundas bytes consecutivos con una palabra sin explicarlo como hipótesis.
        Podés devolver variables e ideas vacías. No ejecutes comandos ni pidas archivos.
        El análisis no modifica nada: el jugador elige qué probar y puede restaurar la partida.
        """;

    public static JsonElement Schema()
    {
        var original = GameModificationAdvisor.Schema();
        return JsonSerializer.SerializeToElement(new
        {
            type = "object", additionalProperties = false, required = new[] { "analysis", "variables", "ideas" },
            properties = new
            {
                analysis = new { type = "string" },
                variables = new
                {
                    type = "array", maxItems = 8,
                    items = new
                    {
                        type = "object", additionalProperties = false,
                        required = new[] { "name", "label", "address", "bytes", "observed", "reason" },
                        properties = new
                        {
                            name = new { type = "string" }, label = new { type = "string" },
                            address = new { type = "string" }, bytes = new { type = "integer", minimum = 1, maximum = 2 },
                            observed = new { type = "integer" }, reason = new { type = "string" }
                        }
                    }
                },
                ideas = original.GetProperty("properties").GetProperty("ideas")
            }
        });
    }

    public static async Task<GameAnalysisResult> AnalyzeAsync(IAiProvider provider, GameAnalysisSnapshot snapshot,
        string wish, string previous = "", CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string answer = await provider.CompleteAsync(new(Guide, [new(true, snapshot.Prompt(wish, previous))], Schema()), ct);
        ct.ThrowIfCancellationRequested();
        return Parse(answer, snapshot);
    }

    public static GameAnalysisResult Parse(string answer, GameAnalysisSnapshot snapshot)
    {
        try
        {
            int start = answer.IndexOf('{'), end = answer.LastIndexOf('}');
            if (start < 0 || end < start) throw new JsonException("Falta el objeto de respuesta.");
            using var document = JsonDocument.Parse(answer[start..(end + 1)]);
            var root = document.RootElement;
            Fields(root, "analysis", "variables", "ideas");
            string analysis = Text(root, "analysis", 3000);
            var variables = root.GetProperty("variables");
            if (variables.ValueKind != JsonValueKind.Array || variables.GetArrayLength() > 8)
                throw new JsonException("Se esperan hasta ocho variables nuevas.");
            var profile = GameProfile.Parse(snapshot.Profile.ToJson());
            var known = snapshot.Candidates.ToDictionary(c => c.Address);
            var names = new HashSet<string>(profile.Variables.Keys, StringComparer.Ordinal);
            var result = new List<AnalyzedVariable>();
            foreach (var variable in variables.EnumerateArray())
            {
                Fields(variable, "name", "label", "address", "bytes", "observed", "reason");
                string name = Text(variable, "name", 32), addressText = Text(variable, "address", 4);
                if (!Regex.IsMatch(name, "^[a-zA-Z_][a-zA-Z0-9_]*$") || !names.Add(name))
                    throw new JsonException("El nombre de una variable es inválido o ya existe.");
                if (addressText.Length != 4 || !uint.TryParse(addressText, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out uint address))
                    throw new JsonException("La dirección debe tener cuatro dígitos hexadecimales.");
                int bytes = variable.GetProperty("bytes").GetInt32(), observed = variable.GetProperty("observed").GetInt32();
                if (bytes is < 1 or > 2 || !ModificationObservation.IsWorkRam(snapshot.Machine, address) ||
                    bytes == 2 && !ModificationObservation.IsWorkRam(snapshot.Machine, address + 1) || !known.TryGetValue(address, out var first) ||
                    bytes == 2 && !known.ContainsKey(address + 1))
                    throw new JsonException("La variable usa memoria que no está en la evidencia.");
                if (OverlapsKnownMemory(snapshot.Profile, address, bytes))
                    throw new JsonException("La dirección ya pertenece a una variable conocida: usá su nombre y sus restricciones.");
                int actual = first.Value | (bytes == 2 ? known[address + 1].Value << 8 : 0);
                if (observed != actual) throw new JsonException("El valor observado no coincide con la captura.");
                var found = new AnalyzedVariable(name, Text(variable, "label", 80), address, bytes, observed, Text(variable, "reason", 1000));
                result.Add(found);
                profile.Variables[name] = Definition(found);
            }
            var ideas = GameModificationAdvisor.Parse(JsonSerializer.Serialize(new { analysis, ideas = root.GetProperty("ideas") }), profile);
            return new(analysis, result, ideas);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { throw new AiException("El análisis no se puede usar: " + error.Message, error); }
    }

    public static bool OverlapsKnownMemory(GameProfile profile, uint address, int bytes)
    {
        foreach (var existing in profile.Variables.Values)
        {
            if (!uint.TryParse(existing.Address, System.Globalization.NumberStyles.HexNumber, null, out uint known)) continue;
            int size = existing.Encoding == "uint16" ? 2 : existing.Encoding is "ascii" or "bcd" or "digits" ? existing.Bytes : 1;
            if (address < (ulong)known + (uint)Math.Max(1, size) && known < (ulong)address + (uint)bytes) return true;
            if (existing.Encoding == "uint9" && uint.TryParse(existing.HighBitAddress,
                System.Globalization.NumberStyles.HexNumber, null, out uint high) && high >= address && high < (ulong)address + (uint)bytes) return true;
        }
        return false;
    }

    public static GameVariable Definition(AnalyzedVariable variable) => new()
    {
        Address = variable.Address.ToString("X4"), Encoding = variable.Bytes == 2 ? "uint16" : "uint8",
        Bytes = variable.Bytes, Min = 0, Max = variable.Bytes == 2 ? 65535 : 255,
        Description = "Hipótesis del análisis: " + variable.Reason
    };

    private static void Fields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("Se esperaba un objeto.");
        var seen = new HashSet<string>();
        foreach (var property in element.EnumerateObject())
            if (!names.Contains(property.Name) || !seen.Add(property.Name)) throw new JsonException("Campo no permitido o repetido: " + property.Name);
        if (seen.Count != names.Length) throw new JsonException("Faltan campos en el análisis.");
    }

    private static string Text(JsonElement parent, string name, int limit)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > limit)
            throw new JsonException("Texto inválido: " + name);
        return value.GetString()!;
    }
}
