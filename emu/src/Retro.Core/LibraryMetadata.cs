using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Retro.Core;

public sealed record LibraryGameInfo(IReadOnlyList<string> Genres, int? ReleaseYear, DateTimeOffset? AddedAt);

/// <summary>Personal catalogue data, bound to the same content identities as favorites. Never edits game files.</summary>
public sealed class LibraryMetadata
{
    public const int GameLimit = 16384, GenreLimit = 8;
    private sealed record Entry(string[] Genres, int? ReleaseYear, DateTimeOffset AddedAt);
    private sealed record Document(int Version, Dictionary<string, Entry> Games);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly Dictionary<string, Entry> _games = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly bool _persist;
    private readonly TimeProvider _clock;
    private bool _unreadable;
    public int Revision { get; private set; }
    public string? Problem { get; private set; }
    public string? RecoveryPath { get; private set; }
    public bool CanRecover => _persist && _unreadable;

    public LibraryMetadata(string? path = null, bool persist = true, TimeProvider? clock = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "catalogo.json");
        _persist = persist; _clock = clock ?? TimeProvider.System;
        if (!persist) return;
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > 4 * 1024 * 1024) throw new InvalidDataException();
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path), Json);
            if (document is not { Version: 1, Games: not null } || document.Games.Count > GameLimit || document.Games.Any(pair =>
                !ValidKey(pair.Key) || pair.Value == null || !Valid(pair.Value.Genres, pair.Value.ReleaseYear) || pair.Value.AddedAt < DateTimeOffset.UnixEpoch))
                throw new InvalidDataException();
            foreach (var pair in document.Games) _games.Add(pair.Key, pair.Value);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            _unreadable = true;
            Problem = "No pude leer las fichas de la biblioteca. Se conservó el archivo; los cambios quedan solo en esta sesión.";
        }
    }

    public static string Normalize(string value)
    {
        try { return string.Concat(value.Trim().Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)).ToUpperInvariant(); }
        catch (ArgumentException) { return value.Trim().ToUpperInvariant(); }
    }

    private static bool ValidKey(string key) => !string.IsNullOrEmpty(key) && LibraryKey.Valid(key);
    private static bool Valid(string[]? genres, int? year) => genres != null && genres.Length <= GenreLimit &&
        (year == null || year is >= 1900 and <= 2100) && genres.All(genre => !string.IsNullOrWhiteSpace(genre) && genre.Length <= 40 && !genre.Any(char.IsControl));

    public LibraryGameInfo Get(string key) => _games.TryGetValue(key, out var entry)
        ? new(Array.AsReadOnly(entry.Genres.ToArray()), entry.ReleaseYear, entry.AddedAt)
        : new(Array.Empty<string>(), null, null);

    /// <summary>The first time this catalogue sees an identity; not a release date or a filesystem timestamp.</summary>
    public void Observe(IEnumerable<string> keys)
    {
        string[] added = keys.Distinct(StringComparer.Ordinal).Where(key => !_games.ContainsKey(key)).ToArray();
        if (added.Any(key => !ValidKey(key))) throw new ArgumentException("Invalid catalogue identity.", nameof(keys));
        if (added.Length == 0) return;
        if (_games.Count + added.Length > GameLimit)
        { Problem = "Se alcanzó el límite de fichas de la biblioteca. Los juegos siguen disponibles."; return; }
        var now = _clock.GetUtcNow();
        foreach (string key in added) _games.Add(key, new([], null, now));
        Revision++; Save();
    }

    public bool Update(string key, IEnumerable<string> genres, int? releaseYear)
    {
        if (!ValidKey(key)) { Problem = "La identidad del juego no es válida."; return false; }
        string[] values = genres.Select(genre => genre?.Trim() ?? "").ToArray();
        if (!Valid(values, releaseYear))
        { Problem = "Usá hasta ocho géneros de 40 caracteres y un año entre 1900 y 2100, o dejá el año sin completar."; return false; }
        values = values.DistinctBy(Normalize).ToArray();
        if (!_games.ContainsKey(key) && _games.Count >= GameLimit)
        { Problem = "Se alcanzó el límite de fichas de la biblioteca."; return false; }
        var previous = _games.GetValueOrDefault(key);
        if (previous != null && previous.ReleaseYear == releaseYear && previous.Genres.SequenceEqual(values))
        { if (Problem != null && !_unreadable) Save(); return true; }
        _games[key] = new(values, releaseYear, previous?.AddedAt ?? _clock.GetUtcNow());
        Revision++; Save();
        return true;
    }

    private bool Save()
    {
        if (!_persist) { Problem = null; return true; }
        if (_unreadable) return false;
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, new Document(1, _games), Json); file.Flush(flushToDisk: true); }
            if (new FileInfo(temporary).Length > 4 * 1024 * 1024) throw new InvalidDataException();
            File.Move(temporary, _path, overwrite: true); Problem = null; return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { Problem = "No pude guardar las fichas de la biblioteca. Los cambios quedan en esta sesión."; return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public bool Recover()
    {
        if (!CanRecover) return Save();
        string backup = _path + ".respaldo-" + Guid.NewGuid().ToString("N");
        try { File.Copy(_path, backup, overwrite: false); RecoveryPath = backup; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Problem = "No pude conservar las fichas anteriores. Los cambios siguen solo en esta sesión."; return false; }
        _unreadable = false;
        if (Save()) return true;
        _unreadable = true; return false;
    }
}
