using Retro.Core;

namespace Retro.App;

/// <summary>A game in the user's folder and the machine it needs.</summary>
public sealed record LibraryEntry(string Path, string Name, MachineInfo Machine, int Confidence, string Reason);

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

    private static List<LibraryEntry> Scan(string folder)
    {
        var list = new List<LibraryEntry>();
        if (!Directory.Exists(folder))
            return list;
        foreach (var path in Directory.GetFiles(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
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
                list.Add(new LibraryEntry(path, name, board.Machine, board.Confidence, board.Reason));
                continue;
            }
            var best = files.SelectMany(MachineCatalog.Identify)
                .OrderByDescending(c => c.Machine.Status == MachineStatus.Available && c.Confidence >= 50)
                .ThenByDescending(c => c.Confidence)
                .FirstOrDefault();
            if (best != null)
                list.Add(new LibraryEntry(path, name, best.Machine, best.Confidence, best.Reason));
        }
        return list;
    }
}
