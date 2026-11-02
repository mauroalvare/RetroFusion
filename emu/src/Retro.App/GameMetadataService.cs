using System.Text.Json;
using Retro.Core;

namespace Retro.App;

/// <summary>Local identification is immediate. Optional public lookups run serially, with cancellation and bounded time.</summary>
public sealed class GameMetadataService : IDisposable
{
    public GameIdentificationCache Cache { get; }
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Dictionary<string, IdentifiedGame> _local = new(StringComparer.Ordinal);
    private readonly Queue<GameLookup> _queue = new();
    private CancellationTokenSource? _cancel;
    private Task<IdentifiedGame?>? _task;
    private GameLookup? _current;
    private int _libraryRevision = -1;
    private string _folder = "";
    private bool _online, _failed;
    private DateTimeOffset _next;
    public int Revision => _localRevision + Cache.Revision;
    private int _localRevision;
    public string Status { get; private set; } = "Identificación local";
    public string? Problem => Cache.Problem;
    public int Pending => _queue.Count + (_task == null ? 0 : 1);
    /// <summary>Lookups answered in the current pass, so progress is counted rather than guessed.</summary>
    public int Asked { get; private set; }

    public GameMetadataService(GameIdentificationCache? cache = null, HttpClient? http = null)
    { Cache = cache ?? new(); _ownsHttp = http == null; _http = http ?? new HttpClient(); }

    public IdentifiedGame? Get(string key)
    {
        if (Cache.Get(key) is { Ignored: true }) return null;
        var local = _local.GetValueOrDefault(key);
        return local?.Source == "gamelist.xml local" ? local : Cache.Detected(key) ?? local;
    }
    public void Ignore(string key, bool ignore)
    {
        if (Cache.Get(key) == null) Cache.Put(key, null, "local", DateTimeOffset.UtcNow);
        Cache.Ignore(key, ignore);
    }

    public void UpdateLocal(IReadOnlyList<LibraryEntry> games, string folder, int revision)
    {
        if (_libraryRevision == revision && _folder == folder) return;
        Stop(); _queue.Clear(); _local.Clear(); _libraryRevision = revision; _folder = folder; _localRevision++;
        var lists = new Dictionary<string, IReadOnlyDictionary<string, IdentifiedGame>>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            if (!LibraryKey.Valid(game.Identity)) continue;
            IdentifiedGame info = game.CollectionInfo ?? GameIdentification.FromFilename(game.Name);
            // The closest list wins; a root list can describe nested files too.
            foreach (string directory in new[] { Path.GetDirectoryName(game.Path)!, folder }.Distinct())
            {
                string list = Path.Combine(directory, "gamelist.xml");
                if (!lists.TryGetValue(list, out var entries)) lists[list] = entries = GameIdentification.ReadGameList(list, folder);
                if (entries.TryGetValue(Path.GetFullPath(game.Path), out var listed)) { info = listed; break; }
            }
            _local.TryAdd(game.Identity, info);
            if (info.Source == "gamelist.xml local" && !SameFacts(Cache.Get(game.Identity)?.Game, info))
                Cache.Put(game.Identity, info, "collection", DateTimeOffset.UtcNow);
        }
        _online = false; _failed = false;
    }

    private static bool SameFacts(IdentifiedGame? left, IdentifiedGame right) => left != null && left.Title == right.Title &&
        left.ReleaseYear == right.ReleaseYear && left.Publisher == right.Publisher && left.Source == right.Source &&
        left.Method == right.Method && left.FirstReleaseYear == right.FirstReleaseYear && left.Genres.SequenceEqual(right.Genres);

    public void Retry() { _failed = false; _online = false; Status = "Preparando consulta…"; }

    public void Poll(IReadOnlyList<LibraryEntry> games, string folder, int revision, bool online)
    {
        UpdateLocal(games, folder, revision);
        if (!online)
        {
            if (_online || _task != null) { Stop(); _queue.Clear(); }
            _online = false; Status = "Identificación local · consulta en línea desactivada"; return;
        }
        if (_task is { IsCompleted: true } finished)
        {
            _task = null; _cancel?.Dispose(); _cancel = null; Asked++;
            if (finished.IsCompletedSuccessfully && _current is { } completed)
                Cache.Put(completed.Key, finished.Result, Query(completed), DateTimeOffset.UtcNow);
            else
            {
                _ = finished.Exception;
                _failed = true; Status = "Catálogo en línea sin respuesta. Tus juegos siguen disponibles; usá Reintentar en Preferencias.";
            }
            _current = null; _next = DateTimeOffset.UtcNow.AddSeconds(1);
        }
        if (_failed) return;
        if (!_online)
        {
            _online = true; _queue.Clear(); Asked = 0;
            foreach (var game in games.DistinctBy(game => game.Identity))
            {
                if (!LibraryKey.Valid(game.Identity) || !WikidataGameLookup.Supports(game.Machine.Id) || Get(game.Identity)?.Source == "gamelist.xml local") continue;
                var lookup = new GameLookup(game.Identity, game.Name, game.Machine.Id);
                if (!Cache.IsFresh(game.Identity, Query(lookup), DateTimeOffset.UtcNow)) _queue.Enqueue(lookup);
            }
        }
        if (_task == null && _queue.Count > 0 && DateTimeOffset.UtcNow >= _next)
        {
            _current = _queue.Dequeue();
            _cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _task = new WikidataGameLookup(_http).FindAsync(_current.Name, _current.Machine, _cancel.Token);
        }
        Status = Pending > 0 ? $"Completando fichas en segundo plano · {Pending} pendientes" : "Identificación al día · sin coincidencia segura, queda sin clasificar";
    }

    private static string Query(GameLookup game) => game.Machine + ":" + GameIdentification.TitleFromName(game.Name);
    private void Stop()
    {
        _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
        // Observe abandoned failures; an obsolete scan must not publish its results into the new library.
        if (_task is { } pending) _ = pending.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        _task = null; _current = null;
    }
    public void Dispose() { Stop(); if (_ownsHttp) _http.Dispose(); }
}
