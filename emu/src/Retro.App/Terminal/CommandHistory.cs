using System.Text.Json;

namespace Retro.App;

/// <summary>
/// Orders already entered. Walking away from the line keeps the unsent draft. Saved locally, one file per
/// account; a damaged file is left in place and the session works from memory.
/// </summary>
public sealed class CommandHistory
{
    public const int MaxEntries = 1000, MaxFileBytes = 256 * 1024, MaxCommandLength = 2048;
    private sealed record Document(int Version, List<string> Commands);
    private readonly List<string> _entries = [];
    private readonly string? _path;
    private int _index;
    private string _draft = "";
    public IReadOnlyList<string> Entries => _entries.AsReadOnly();
    public bool Persist { get; set; } = true;
    public string? Problem { get; private set; }

    public CommandHistory(string? path = null)
    {
        _path = path;
        if (path == null) return;
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException();
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (document is not { Version: 1, Commands: not null }) throw new InvalidDataException();
            _entries.AddRange(document.Commands.Where(command => command.Length is > 0 and <= MaxCommandLength).TakeLast(MaxEntries));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { Problem = "No pude leer el historial de órdenes; esta sesión empieza vacía."; _path = null; }
        _index = _entries.Count;
    }

    public void Add(string command)
    {
        if (command.Length is 0 or > MaxCommandLength) { Rewind(); return; }
        if (_entries.Count == 0 || _entries[^1] != command) _entries.Add(command);
        if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
        Rewind();
    }

    /// <summary>Back to the line being written; used after running or abandoning an edit.</summary>
    public void Rewind() { _index = _entries.Count; _draft = ""; }

    public bool Browsing => _index != _entries.Count;

    public string Move(int direction, string current)
    {
        if (!Browsing) _draft = current;
        _index = Math.Clamp(_index + direction, 0, _entries.Count);
        return Browsing ? _entries[_index] : _draft;
    }

    /// <summary>The newest entry before an index that contains the text; older repeats of a match are skipped by the caller.</summary>
    public int FindBefore(string text, int before)
    {
        for (int i = Math.Min(before, _entries.Count) - 1; i >= 0; i--)
            if (text.Length == 0 || CommandLine.Fold(_entries[i]).Contains(CommandLine.Fold(text))) return i;
        return -1;
    }

    public void Clear() { _entries.Clear(); Rewind(); Save(); }

    /// <summary>Atomic write; a failure only loses the persistence of this session.</summary>
    public void Save()
    {
        if (_path == null || !Persist) return;
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(1, _entries)));
            File.Move(temporary, _path, overwrite: true); Problem = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Problem = "No pude guardar el historial de órdenes; sigue disponible en esta sesión."; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
