using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Retro.Core;

/// <summary>Library identities never depend on the install folder, game name or ZIP member order.</summary>
public static class LibraryKey
{
    public static string Original(string machine, IEnumerable<MediaFile> files)
    {
        var hashes = files.Select(f => Convert.ToHexString(SHA256.HashData(f.Data)))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return Named("original", machine + "\0" + string.Join("", hashes));
    }

    public static string Named(string kind, string identity)
    {
        if (kind is not ("original" or "fusion" or "created")) throw new ArgumentException("Unknown library kind.", nameof(kind));
        return kind + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    internal static bool Valid(string key)
    {
        int split = key.IndexOf(':');
        return split >= 0 && key[..split] is "original" or "fusion" or "created"
            && key.Length - split - 1 == 64 && key[(split + 1)..].All(char.IsAsciiHexDigit);
    }
}

/// <summary>Local favorites and successful launches; contains no paths, game data or connection settings.</summary>
public sealed class LibraryActivity
{
    public const int RecentLimit = 40, FavoriteLimit = 4096;
    private sealed record Entry(bool Favorite, DateTimeOffset? Played);
    private sealed record Document(int Version, Dictionary<string, Entry> Games);
    private readonly Dictionary<string, Entry> _games = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly bool _persist;
    private bool _unreadable;
    public string? Problem { get; private set; }

    public LibraryActivity(string? path = null, TimeProvider? clock = null, bool persist = true)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "biblioteca.json");
        _clock = clock ?? TimeProvider.System;
        _persist = persist;
        if (!_persist) return;
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > 2 * 1024 * 1024) throw new InvalidDataException();
            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path));
            if (doc is not { Version: 1, Games: not null } || doc.Games.Count > FavoriteLimit + RecentLimit ||
                doc.Games.Any(g => !LibraryKey.Valid(g.Key) || g.Value == null) ||
                doc.Games.Count(g => g.Value.Favorite) > FavoriteLimit) throw new InvalidDataException();
            foreach (var pair in doc.Games) _games.Add(pair.Key, pair.Value);
            Trim();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            _unreadable = true;
            Problem = "No pude leer favoritos y recientes. Los cambios se conservarán solo en esta sesión.";
        }
    }

    public bool IsFavorite(string key) => _games.TryGetValue(key, out var entry) && entry.Favorite;
    public DateTimeOffset? LastPlayed(string key) => _games.GetValueOrDefault(key)?.Played;
    public IReadOnlyList<string> Recents => _games.Where(g => g.Value.Played != null)
        .OrderByDescending(g => g.Value.Played).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).ToArray();

    public void SetFavorite(string key, bool favorite)
    {
        if (!LibraryKey.Valid(key)) throw new ArgumentException("Invalid library identity.", nameof(key));
        if (favorite == IsFavorite(key)) return;
        if (favorite && _games.Count(g => g.Value.Favorite) >= FavoriteLimit)
        { Problem = "Se alcanzó el límite de favoritos. Quitá alguno para agregar otro."; return; }
        _games[key] = new(favorite, LastPlayed(key));
        Trim(); Save();
    }

    public void Played(string key)
    {
        if (!LibraryKey.Valid(key)) throw new ArgumentException("Invalid library identity.", nameof(key));
        _games[key] = new(IsFavorite(key), _clock.GetUtcNow());
        Trim(); Save();
    }

    public void ClearRecents()
    {
        foreach (var pair in _games.ToArray()) _games[pair.Key] = pair.Value with { Played = null };
        Trim(); Save();
    }

    private void Trim()
    {
        var recent = Recents.Take(RecentLimit).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in _games.ToArray())
        {
            if (!recent.Contains(pair.Key))
            {
                if (pair.Value.Favorite) _games[pair.Key] = pair.Value with { Played = null };
                else _games.Remove(pair.Key);
            }
            else if (!pair.Value.Favorite && pair.Value.Played == null) _games.Remove(pair.Key);
        }
    }

    private void Save()
    {
        if (!_persist || _unreadable) return; // Preserve damaged or future-format metadata for recovery.
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, new Document(1, _games));
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, overwrite: true);
            Problem = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Problem = "No pude guardar favoritos y recientes. Los cambios se conservarán solo en esta sesión."; }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}