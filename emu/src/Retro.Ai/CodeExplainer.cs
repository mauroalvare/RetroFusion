using System.Text;
using System.Text.Json;
using Retro.Core;
using Retro.Fusion;

namespace Retro.Ai;

public sealed record LineComment(string Address, string Text);

public sealed record RoutineExplanation(string Name, string Summary, string Pseudocode, List<LineComment> Comments);

/// <summary>
/// Asks an AI to read one routine of a game: give it a name, explain what it does,
/// write readable pseudocode and comment the important lines. The AI gets the
/// disassembly, the routines it calls (with the names already given) and the game's
/// known RAM variables, so the explanation is grounded in the real code.
/// </summary>
public static class CodeExplainer
{
    public const string Guide = """
        Sos un experto en ingeniería inversa de videojuegos retro. Te paso una rutina
        desensamblada de un juego real. Respondé en castellano con:
        - name: un nombre corto en inglés estilo identificador (ej. MovePlayerLeft, DrawAliens).
        - summary: qué hace la rutina y para qué la usa el juego, en 1 a 3 oraciones.
        - pseudocode: la rutina traducida a pseudocódigo tipo C, legible, usando los nombres de
          variables conocidas cuando la rutina las toca.
        - comments: comentarios para las líneas importantes (dirección física en hexadecimal, con todos sus dígitos
          y texto corto).
        No inventes lo que no está en el código: si algo es una suposición, decilo.
        """;

    public static JsonElement Schema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "name", "summary", "pseudocode", "comments" },
        properties = new
        {
            name = new { type = "string" },
            summary = new { type = "string" },
            pseudocode = new { type = "string" },
            comments = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "address", "text" },
                    properties = new { address = new { type = "string" }, text = new { type = "string" } },
                },
            },
        },
    });

    /// <summary>The routine as text: address, bytes, instruction, known names.</summary>
    public static string Listing(IMachine m, Routine r, CodeAnnotations notes)
    {
        var sb = new StringBuilder();
        foreach (var a in r.Instructions.OrderBy(x => x))
        {
            var ins = m.Disassembler.Disassemble(x => m.Memory.Peek(x), a);
            string label = notes.Label(a) ?? m.SymbolFor(a) ?? (a == r.Start ? $"sub_{a:X4}" : "");
            if (label.Length > 0)
                sb.AppendLine(label + ":");
            sb.AppendLine($"  {a:X4}  {string.Join(" ", ins.Bytes.Select(b => b.ToString("X2"))),-12} {ins.Text}");
        }
        return sb.ToString();
    }

    public static string Prompt(IMachine m, Routine r, CodeAnnotations notes, GameProfile? profile, CodeMap map)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Máquina: {m.DisplayName} ({m.Cpu.CpuName}). {m.MemoryMapDescription}.");
        if (profile != null)
        {
            sb.AppendLine($"Juego: {profile.Name}. {profile.Description}");
            sb.AppendLine("Variables de RAM conocidas:");
            foreach (var (name, v) in profile.Variables)
                sb.AppendLine($"  ${v.Address} {name}: {v.Description}");
        }
        if (r.Calls.Count > 0)
        {
            sb.AppendLine("Rutinas que llama:");
            foreach (var c in r.Calls)
                sb.AppendLine($"  ${c:X4} {notes.Label(c) ?? m.SymbolFor(c) ?? "sin nombre"}{(notes.Summaries.TryGetValue(CodeAnnotations.Key(c), out var s) ? " - " + s : "")}");
        }
        sb.AppendLine($"La llaman {r.Callers.Count} rutinas.{(r.Executed ? " Se ejecutó durante la partida." : "")}");
        sb.AppendLine();
        sb.AppendLine("Rutina:");
        sb.Append(Listing(m, r, notes));
        return sb.ToString();
    }

    public static async Task<RoutineExplanation> ExplainAsync(IAiProvider ai, string prompt, CancellationToken ct)
    {
        string answer = await ai.CompleteAsync(new AiRequest(Guide, new[] { new AiTurn(true, prompt) }, Schema()), ct);
        return Parse(answer);
    }

    /// <summary>Reads the JSON answer (tolerates prose or code fences around it).</summary>
    public static RoutineExplanation Parse(string answer)
    {
        int s = answer.IndexOf('{'), e = answer.LastIndexOf('}');
        if (s < 0 || e <= s)
            throw new AiException("La respuesta no tiene un JSON.");
        try
        {
            var x = JsonSerializer.Deserialize<RoutineExplanation>(answer[s..(e + 1)], GameProfile.Json)
                    ?? throw new AiException("Respuesta vacía.");
            return x with { Name = x.Name ?? "", Summary = x.Summary ?? "", Pseudocode = x.Pseudocode ?? "", Comments = x.Comments ?? new() };
        }
        catch (JsonException ex)
        {
            throw new AiException("El JSON de la respuesta no es válido: " + ex.Message);
        }
    }

    /// <summary>The same request as text, for pasting into any chat (ChatGPT, Gemini, Claude.ai...).</summary>
    public static string ManualPrompt(string prompt) =>
        Guide + "\n" + prompt + """

        Respondé SOLO con un JSON con esta forma (sin texto antes ni después):
        {"name": "NombreDeLaRutina", "summary": "...", "pseudocode": "...", "comments": [{"address": "1A2B", "text": "..."}]}
        """;

    /// <summary>Stores the explanation as the routine's name, summary, pseudocode and line comments.</summary>
    public static void Apply(RoutineExplanation x, Routine r, CodeAnnotations notes)
    {
        string key = CodeAnnotations.Key(r.Start);
        if (!string.IsNullOrWhiteSpace(x.Name))
            notes.Labels[key] = x.Name;
        notes.Summaries[key] = x.Summary;
        notes.Pseudocode[key] = x.Pseudocode;
        foreach (var c in x.Comments ?? new())
        {
            if (uint.TryParse(c.Address.TrimStart('$'), System.Globalization.NumberStyles.HexNumber, null, out uint a) && r.Instructions.Contains(a))
                notes.Comments[CodeAnnotations.Key(a)] = c.Text;
        }
    }
}
