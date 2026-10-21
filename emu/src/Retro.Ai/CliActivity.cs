using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Retro.Ai;

/// <summary>A bounded, in-memory view of what the CLI has actually emitted.</summary>
public sealed record CliActivity(string Status, bool Running, long StartedAt, long ObservedAt,
    long LastOutputAt, long OutputCharacters, string Output)
{
    public const int MaxOutputLength = 12000;
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(StartedAt, Running ? Stopwatch.GetTimestamp() : ObservedAt);
    public TimeSpan QuietFor => Stopwatch.GetElapsedTime(LastOutputAt, Running ? Stopwatch.GetTimestamp() : ObservedAt);
    internal static string Tail(string text) => text.Length <= MaxOutputLength ? text : text[^MaxOutputLength..];
    internal static string CodexText(string output)
    {
        var text = new StringBuilder();
        foreach (string line in output.Split('\n'))
        {
            if (!line.TrimStart().StartsWith('{')) { text.AppendLine(line); continue; }
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("text", out var message) && message.ValueKind == JsonValueKind.String) text.AppendLine(message.GetString());
                    else if (item.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String) text.AppendLine(command.GetString());
                }
                else if (root.TryGetProperty("message", out var error) && error.ValueKind == JsonValueKind.String) text.AppendLine(error.GetString());
            }
            catch (JsonException) { /* An incomplete event will be shown when its text arrives. */ }
        }
        return text.ToString().Trim();
    }
}
