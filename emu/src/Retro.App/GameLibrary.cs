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
public sealed class GameLibrary
{
    private Task<List<LibraryEntry>>? _task;
    private List<LibraryEntry> _entries = new();

    public IReadOnlyList<LibraryEntry> Entries => _entries;
    public bool Scanning => _task is { IsCompleted: false };
    public string Folder { get; private set; } = "";

    public void Rescan(string folder)
    {
        Folder = folder;
        _task = Task.Run(() => Scan(folder));
    }

    /// <summary>Call every frame: takes the result of a finished scan.</summary>
    public void Poll()
    {
        if (_task is { IsCompleted: true } t)
        {
            _task = null;
            if (t.IsCompletedSuccessfully)
                _entries = t.Result;
        }
    }

    public IEnumerable<LibraryEntry> For(string machineId) => _entries.Where(e => e.Machine.Id == machineId);

    /// <summary>Original files stay playable independently of stored or running fusions.</summary>
    public IEnumerable<LibraryEntry> Originals(string search = "") => _entries
        .Where(e => e.Machine.Status == MachineStatus.Available &&
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
            if (rel.StartsWith("revision/", StringComparison.OrdinalIgnoreCase) || Retro.Compat.CompatDiscovery.IsExcluded(rel)
                || System.IO.Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".md" or ".json" or ".png" or ".pdf" or ".nfo")
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
