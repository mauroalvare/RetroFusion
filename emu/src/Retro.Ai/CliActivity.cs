using System.Diagnostics;

namespace Retro.Ai;

/// <summary>A bounded, in-memory view of what the CLI has actually emitted.</summary>
public sealed record CliActivity(string Status, bool Running, long StartedAt, long ObservedAt,
    long LastOutputAt, long OutputCharacters, string Output)
{
    public const int MaxOutputLength = 12000;
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(StartedAt, Running ? Stopwatch.GetTimestamp() : ObservedAt);
    public TimeSpan QuietFor => Stopwatch.GetElapsedTime(LastOutputAt, Running ? Stopwatch.GetTimestamp() : ObservedAt);
    internal static string Tail(string text) => text.Length <= MaxOutputLength ? text : text[^MaxOutputLength..];
}
