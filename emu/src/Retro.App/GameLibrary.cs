using Retro.Core;
using Retro.Machines;

namespace Retro.App;

/// <summary>A game in the user's folder and the machine it needs.</summary>
/// <param name="NotYet">Why the machine, already implemented, cannot load this file yet (a later format), or null.</param>
/// <param name="ProfileId">The fusion profile of this copy (verified, or the basic one the fusions give it), or null.</param>
public sealed record LibraryEntry(string Path, string Name, MachineInfo Machine, int Confidence, string Reason, string? NotYet = null, string? ProfileId = null);

/// <summary>
/// Looks through the user's game folder (in the background) and works out which machine
/// each file needs, so the Machine menu can list the games each machine runs and tell the
/// user which machines their games are waiting for.
/// </summary>
public sealed class GameLibrary : IDisposable
{
    private Task<(List<LibraryEntry> Entries, Retro.Compat.OrganizationResult? Organization)>? _task;
    private List<LibraryEntry> _entries = new();
    private FileSystemWatcher? _watcher;
    private long _generation, _scannedGeneration, _changedAt;
    private DateTime _retryAt = DateTime.MaxValue;
    private string? _pendingFolder;
    public bool AutoOrganize { get; init; }
    public Func<string, bool>? InUse { get; init; }
    public int OrganizedCount { get; private set; }
    public int UnclassifiedCount { get; private set; }
    public string? OrganizationError { get; private set; }
    public int Revision { get; private set; }

    public IReadOnlyList<LibraryEntry> Entries => _entries;
    public bool Scanning => _task is { IsCompleted: false };
    public string Folder { get; private set; } = "";

    public void Rescan(string folder) => StartScan(folder, retryOnly: false);

    private void StartScan(string folder, bool retryOnly)
    {
        if (_task != null) { _pendingFolder = folder; return; }
        bool changed = Folder != folder;
        Folder = folder;
        // Only the ROMs library is automatically reorganized, never the fallback user folder.
        bool organize = AutoOrganize && Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)).Equals("ROMs", StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            _watcher?.Dispose(); _watcher = null;
            OrganizedCount = UnclassifiedCount = 0;
            if (organize && Directory.Exists(folder))
            {
                try
                {
                    _watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    void Changed(object sender, FileSystemEventArgs args)
                    {
                        if (Path.GetRelativePath(folder, args.FullPath).Replace('\\', '/').StartsWith(".retroemu/", StringComparison.OrdinalIgnoreCase)) return;
                        Volatile.Write(ref _changedAt, DateTime.UtcNow.Ticks);
                        Interlocked.Increment(ref _generation);
                    }
                    _watcher.Changed += Changed; _watcher.Created += Changed; _watcher.Deleted += Changed; _watcher.Renamed += Changed;
                    _watcher.Error += (_, _) => Interlocked.Increment(ref _generation);
                    _watcher.EnableRaisingEvents = true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _watcher?.Dispose(); _watcher = null; }
            }
        }
        _scannedGeneration = Volatile.Read(ref _generation);
        var previous = _entries;
        _task = Task.Run(() =>
        {
            var result = organize ? Retro.Compat.RomOrganizer.Organize(folder, inUse: InUse) : null;
            // Retrying a locked file does not require reopening every unchanged ROM in the library.
            return (retryOnly && result is { Moved.Count: 0 } ? previous : Scan(folder), result);
        });
    }

    /// <summary>Call every frame: takes the result of a finished scan.</summary>
    public void Poll()
    {
        if (_task is { IsCompleted: true } t)
        {
            _task = null;
            if (t.IsCompletedSuccessfully)
            {
                if (!ReferenceEquals(_entries, t.Result.Entries)) Revision++;
                _entries = t.Result.Entries;
                var result = t.Result.Organization;
                OrganizedCount += result?.Moved.Count ?? 0;
                UnclassifiedCount = result?.Unclassified ?? 0;
                OrganizationError = result?.Error;
                _retryAt = result is { Deferred: > 0 } ? DateTime.UtcNow.AddSeconds(5) : DateTime.MaxValue;
            }
            else { OrganizationError = "No pude actualizar la carpeta de juegos."; _retryAt = DateTime.UtcNow.AddSeconds(30); }
        }
        if (_task == null && _pendingFolder is { } pending) { _pendingFolder = null; Rescan(pending); }
        if (_task == null && AutoOrganize && (DateTime.UtcNow >= _retryAt ||
            Volatile.Read(ref _generation) != _scannedGeneration && DateTime.UtcNow.Ticks - Volatile.Read(ref _changedAt) >= TimeSpan.FromSeconds(3).Ticks))
            StartScan(Folder, retryOnly: Volatile.Read(ref _generation) == _scannedGeneration);
    }

    public void Dispose() { _watcher?.Dispose(); _watcher = null; }

    public IEnumerable<LibraryEntry> For(string machineId) => _entries.Where(e => e.Machine.Id == machineId);

    /// <summary>Original files stay playable independently of stored or running fusions.</summary>
    public IEnumerable<LibraryEntry> Originals(string search = "", string? machineId = null) => _entries
        .Where(e => e.Machine.Status == MachineStatus.Available &&
            (machineId == null || e.Machine.Id == machineId) &&
            (search.Trim().Length == 0 || e.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)))
        .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    private static List<LibraryEntry> Scan(string folder)
    {
        var list = new List<LibraryEntry>();
        if (!Directory.Exists(folder))
            return list;
        // the games the fusions can use, with their profile ids (subfolders included, excluded titles left out)
        var fusable = Retro.Compat.GameCatalog.Scan(folder).ToDictionary(g => g.Path, g => g.Profile.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string rel = System.IO.Path.GetRelativePath(folder, path).Replace('\\', '/');
            if (rel.StartsWith("revision/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith(".retroemu/", StringComparison.OrdinalIgnoreCase) || Retro.Compat.CompatDiscovery.IsExcluded(rel)
                || System.IO.Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".md" or ".json" or ".png" or ".jpg" or ".jpeg" or ".pdf" or ".nfo")
                continue;
            List<MediaFile> files;
            try { files = MediaArchive.Open(path); }
            catch (Exception) { continue; }
            if (files.Count == 0)
                continue;
            string name = System.IO.Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
            if (name.EndsWith(".tzx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tap", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            if (MachineCatalog.IdentifySet(files) is { } board)
            {
                list.Add(new LibraryEntry(path, name, board.Machine, board.Confidence, board.Reason, null, fusable.GetValueOrDefault(path)));
                continue;
            }
            var best = files.SelectMany(MachineCatalog.Identify)
                .OrderByDescending(c => c.Machine.Status == MachineStatus.Available && c.Confidence >= 50 && MachineCatalog.NotLoadableYet(c) == null)
                .ThenByDescending(c => c.Machine.Status == MachineStatus.Available && c.Confidence >= 50)
                .ThenByDescending(c => c.Confidence)
                .FirstOrDefault();
            if (best != null)
                list.Add(new LibraryEntry(path, name, best.Machine, best.Confidence, best.Reason, MachineCatalog.NotLoadableYet(best), fusable.GetValueOrDefault(path)));
        }
        return list;
    }
}
