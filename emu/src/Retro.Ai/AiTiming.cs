using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Retro.Ai;

public sealed record AiTimeRange(TimeSpan Low, TimeSpan High, int Samples);

/// <summary>Bounded local durations only: hashed operation keys and seconds, no prompts or responses.</summary>
public sealed class AiDurationHistory(string? path = null)
{
    private static readonly object Gate = new();
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "tiempos-ia.json");
    public static string Key(string identity) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    private Dictionary<string, List<double>> Read()
    {
        try
        {
            var file = new FileInfo(_path);
            if (!file.Exists || file.Length > 128 * 1024 || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) return new();
            var data = JsonSerializer.Deserialize<Dictionary<string, List<double>>>(File.ReadAllText(_path));
            if (data == null || data.Count > 128 || data.Any(pair => pair.Key.Length != 64 || pair.Key.Any(c => !char.IsAsciiHexDigit(c)) ||
                pair.Value == null || pair.Value.Count > 20 || pair.Value.Any(n => !double.IsFinite(n) || n < 1 || n > 21600))) return new();
            return data;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public AiTimeRange? Estimate(string key)
    {
        lock (Gate)
        {
            var data = Read();
            if (!data.TryGetValue(key, out var samples) || samples.Count < 3) return null;
            double[] ordered = samples.Order().ToArray(); int trim = (ordered.Length - 1) / 5;
            double low = ordered[trim] * .75, high = ordered[^(trim + 1)] * 1.5;
            return new(TimeSpan.FromSeconds(low), TimeSpan.FromSeconds(Math.Max(low + 1, high)), ordered.Length);
        }
    }
    public void Record(string key, TimeSpan elapsed)
    {
        double seconds = elapsed.TotalSeconds;
        if (key.Length != 64 || key.Any(c => !char.IsAsciiHexDigit(c)) || !double.IsFinite(seconds) || seconds < 1 || seconds > 21600) return;
        lock (Gate)
        {
            string? temporary = null;
            try
            {
                var data = Read();
                if (!data.TryGetValue(key, out var samples))
                {
                    if (data.Count == 128) data.Remove(data.Keys.First());
                    data[key] = samples = new();
                }
                samples.Add(seconds); if (samples.Count > 20) samples.RemoveAt(0);
                string parent = Path.GetDirectoryName(Path.GetFullPath(_path))!; Directory.CreateDirectory(parent);
                temporary = Path.Combine(parent, Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(temporary, JsonSerializer.Serialize(data)); File.Move(temporary, _path, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            finally
            {
                if (temporary != null) try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}

/// <summary>A whole operation, including validation/retries; cancelling and unsuccessful work never train the estimate.</summary>
public sealed class AiTiming
{
    private readonly TimeProvider _clock;
    private readonly AiDurationHistory _history;
    private readonly string _key;
    private readonly long _start;
    private TimeSpan? _stopped;
    public AiTimeRange? Range { get; }
    public TimeSpan Elapsed => _stopped ?? _clock.GetElapsedTime(_start);
    public AiTiming(string identity, AiDurationHistory? history = null, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System; _history = history ?? new(); _key = AiDurationHistory.Key(identity);
        Range = _history.Estimate(_key); _start = _clock.GetTimestamp();
    }
    public void Stop(bool success)
    {
        if (_stopped != null) return;
        _stopped = Elapsed;
        if (success) _history.Record(_key, _stopped.Value);
    }
    public static string Duration(TimeSpan value) => string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalMinutes}:{value.Seconds:00}");
    public string RemainingText
    {
        get
        {
            if (Range == null) return "Todavía no hay tiempos suficientes para estimar.";
            TimeSpan elapsed = Elapsed;
            if (elapsed >= Range.High) return "Superó el tiempo habitual. Podés ver la actividad o cancelar.";
            var low = Range.Low - elapsed; var high = Range.High - elapsed;
            return low <= TimeSpan.Zero ? $"Hasta {Duration(high)} restantes (orientativo)."
                : $"Entre {Duration(low)} y {Duration(high)} restantes (orientativo).";
        }
    }
}