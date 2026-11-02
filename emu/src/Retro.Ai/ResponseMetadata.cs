using System.Text.Json;
using System.Text.RegularExpressions;

namespace Retro.Ai;

/// <summary>Shared metadata rules for generated answers and agent files, independent of provider.</summary>
public static class ResponseMetadata
{
    public static bool TryReadObject(string text, out JsonDocument? document, out string? problem)
    {
        document = null; problem = null;
        var fences = Regex.Matches(text, "```json[ \\t]*\\r?\\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (fences.Count > 1) { problem = "Hay varias fichas JSON. Enviá una sola versión completa."; return false; }
        if (fences.Count == 1) text = fences[0].Groups[1].Value;
        else if (text.Contains("```json", StringComparison.OrdinalIgnoreCase))
        { problem = "El bloque de la ficha JSON está incompleto."; return false; }
        int open = text.IndexOf('{');
        if (text.TrimStart().StartsWith('['))
        { problem = "La ficha debe ser un objeto JSON, no una lista."; return false; }
        if (open < 0)
        {
            if (fences.Count == 1) { problem = "La ficha debe ser un objeto JSON."; return false; }
            return true; // Legacy code-only responses remain supported.
        }
        int close = text.LastIndexOf('}');
        if (close <= open)
        { problem = "La ficha JSON está incompleta. Enviá el objeto completo y el programa."; return false; }
        try { document = JsonDocument.Parse(text[open..(close + 1)]); return true; }
        catch (JsonException error)
        { problem = Retro.Core.UiText.Get("La ficha JSON no es válida: ") + error.Message; return false; }
    }

    public static string? TextProblem(JsonElement root, string name, int maximum, bool allowNull = false)
    {
        if (root.ValueKind != JsonValueKind.Object) return "La ficha debe ser un objeto JSON.";
        if (!root.TryGetProperty(name, out var field)) return null;
        if (root.EnumerateObject().Count(p => p.NameEquals(name)) > 1) return Retro.Core.UiText.Format($"La ficha repite el campo {name}. Enviá un solo valor.");
        if (allowNull && field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String) return Retro.Core.UiText.Format($"El campo {name} debe ser texto.");
        return field.GetString()!.Length > maximum ? Retro.Core.UiText.Format($"El campo {name} supera el límite de {maximum} caracteres.") : null;
    }
}
