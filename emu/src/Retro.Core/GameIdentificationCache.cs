using System.Text.Json;

namespace Retro.Core;

/// <summary>Disposable lookup data, separate from the user's personal catalog.</summary>
public sealed class GameIdentificationCache
{
    /// <param name="Confirmed">A person chose this match: later automatic passes keep it.</param>
    /// <param name="Rejected">Sources a person rejected; they are not accepted again automatically.</param>
    public sealed record Entry(IdentifiedGame? Game, string Query, DateTimeOffset CheckedAt, bool Ignored = false, bool Confirmed = false, string[]? Rejected = null);
    private sealed record Document(int Version, Dictionary<string, Entry> Games);
    private readonly Dictionary<string, Entry> _games = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly bool _persist;
    private bool _unreadable;
    public string? Problem { get; private set; }
    public int Revision { get; private set; }
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public GameIdentificationCache(string? path = null, bool persist = true)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "identificaciones.json");
        _persist = persist;
        if (!persist) return;
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > 8 * 1024 * 1024) throw new InvalidDataException();
            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path), Json);
            if (doc is not { Version: 1, Games: not null } || doc.Games.Count > 16384 || doc.Games.Any(pair => !LibraryKey.Valid(pair.Key) || !Valid(pair.Value))) throw new InvalidDataException();
            foreach (var pair in doc.Games) _games.Add(pair.Key, Copy(pair.Value));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { _unreadable = true; Problem = "No pude leer las identificaciones. Se conserva el archivo anterior; los datos nuevos quedan en esta sesión."; }
    }

    public Entry? Get(string key) => _games.TryGetValue(key, out var value) ? Copy(value) : null;
    public IdentifiedGame? Detected(string key) => Get(key) is { Ignored: false } entry ? entry.Game : null;
    public bool IsFresh(string key, string query, DateTimeOffset now) => Get(key) is { } entry && (entry.Ignored || entry.Confirmed || entry.Query == query && now >= entry.CheckedAt && now - entry.CheckedAt < TimeSpan.FromDays(entry.Game == null ? 7 : 90));

    public void Put(string key, IdentifiedGame? game, string query, DateTimeOffset now)
    {
        var previous = Get(key);
        // A personal decision outlives what the catalog says later.
        if (previous is { Confirmed: true } || game != null && previous?.Rejected?.Contains(game.Source) == true) return;
        var entry = new Entry(game, query, now, previous?.Ignored ?? false, false, previous?.Rejected);
        if (!LibraryKey.Valid(key) || !Valid(entry) || !_games.ContainsKey(key) && _games.Count >= 16384) return;
        _games[key] = Copy(entry); Revision++; Save();
    }
    public void Ignore(string key, bool ignore)
    {
        if (!_games.TryGetValue(key, out var entry) || entry.Ignored == ignore) return;
        _games[key] = entry with { Ignored = ignore }; Revision++; Save();
    }

    /// <summary>A reviewed candidate. It also lifts a previous omission: choosing an identity is the opposite of rejecting one.</summary>
    public void Confirm(string key, IdentifiedGame game, DateTimeOffset now)
    {
        var entry = new Entry(game, Get(key)?.Query ?? "", now, false, true, Get(key)?.Rejected);
        if (!LibraryKey.Valid(key) || !Valid(entry) || !_games.ContainsKey(key) && _games.Count >= 16384) return;
        _games[key] = Copy(entry); Revision++; Save();
    }

    public void Reject(string key, string source, DateTimeOffset now)
    {
        var entry = Get(key) ?? new(null, "", now);
        string[] rejected = (entry.Rejected ?? []).Append(source).Distinct().TakeLast(16).ToArray();
        var updated = entry with { Rejected = rejected, Game = entry.Game?.Source == source ? null : entry.Game, Confirmed = entry.Confirmed && entry.Game?.Source != source };
        if (!LibraryKey.Valid(key) || !Valid(updated)) return;
        _games[key] = Copy(updated); Revision++; Save();
    }

    private static Entry Copy(Entry entry) => entry with { Game = entry.Game is { } game ? game with { Genres = game.Genres.ToArray() } : null };
    private static bool Valid(Entry? entry) => entry != null && entry.Query is { Length: <= 200 } && (entry.Rejected == null || entry.Rejected.Length <= 16 && entry.Rejected.All(source => source is { Length: > 0 and <= 300 })) && (entry.Game == null ||
        entry.Game is { Title.Length: > 0 and <= 160, Genres.Length: <= 8, Source.Length: > 0 and <= 300, Method.Length: > 0 and <= 160 } game &&
        game.Genres.All(genre => genre is { Length: > 0 and <= 40 }) && (game.ReleaseYear == null || game.ReleaseYear is >= 1900 and <= 2100) &&
        (game.FirstReleaseYear == null || game.FirstReleaseYear is >= 1900 and <= 2100) && (game.Publisher == null || game.Publisher.Length <= 160));

    private void Save()
    {
        if (!_persist || _unreadable) return;
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(1, _games), Json));
            File.Move(temporary, _path, true); Problem = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Problem = "No pude guardar las identificaciones. Los datos quedan en esta sesión."; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
