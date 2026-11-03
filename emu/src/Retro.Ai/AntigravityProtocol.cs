using System.Text.Json;

namespace Retro.Ai;

/// <summary>Documented agy stream-json transport: one user message and one terminal result.</summary>
internal static class AntigravityProtocol
{
    public static string Input(string prompt) => JsonSerializer.Serialize(new { @event = "user", message = new { content = prompt } }) + "\n";
    public static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";

    public static JsonElement? Result(string output)
    {
        // Also understand the older single-object envelope, but never mistake arbitrary generated JSON for a confirmed result.
        int start = output.IndexOf('{');
        if (start < 0) return null;
        try
        {
            using var single = JsonDocument.Parse(output[start..]);
            if (single.RootElement.ValueKind == JsonValueKind.Object && Text(single.RootElement, "event").Length == 0
                && Text(single.RootElement, "status").Length > 0) return single.RootElement.Clone();
        }
        catch (JsonException) { }
        JsonElement? result = null;
        foreach (string line in output.Split('\n'))
        {
            if (!line.TrimStart().StartsWith('{')) continue;
            try
            {
                using var json = JsonDocument.Parse(line);
                if (Text(json.RootElement, "event") != "result") continue;
                if (result != null || !json.RootElement.TryGetProperty("result", out var value) || value.ValueKind != JsonValueKind.Object) return null;
                result = value.Clone();
            }
            catch (JsonException) { return null; } // A truncated/invalid event cannot confirm completion.
        }
        return result;
    }

    public static bool PermissionDenied(string output) => output.Contains("headless mode cannot prompt", StringComparison.OrdinalIgnoreCase)
        || output.Contains("permission", StringComparison.OrdinalIgnoreCase)
        && (output.Contains("denied", StringComparison.OrdinalIgnoreCase) || output.Contains("denial", StringComparison.OrdinalIgnoreCase));
}
