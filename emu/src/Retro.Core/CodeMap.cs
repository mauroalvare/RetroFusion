using System.Text.Json;
using System.Text.Json.Serialization;

namespace Retro.Core;

public enum FlowKind
{
    Normal,
    /// <summary>Subroutine call (continues after it returns).</summary>
    Call,
    /// <summary>Conditional call.</summary>
    CallIf,
    Jump,
    JumpIf,
    Return,
    ReturnIf,
    /// <summary>Jump through a register (JP (HL), PCHL): target unknown statically.</summary>
    JumpIndirect,
    Halt,
}

public readonly record struct FlowInfo(int Length, FlowKind Kind, uint? Target)
{
    /// <summary>Optional sequential address for segmented instruction streams.</summary>
    public uint? Next { get; init; }
}

/// <summary>Control-flow classification of one instruction, per CPU.</summary>
public interface IFlowAnalyzer
{
    int AddressBits => 16;
    FlowInfo Analyze(Func<uint, byte> peek, uint address);
}

public sealed class Routine
{
    public uint Start { get; init; }
    public uint End { get; set; }
    public HashSet<uint> Instructions { get; } = new();
    public HashSet<uint> Callers { get; } = new();
    public HashSet<uint> Calls { get; } = new();
    public bool Executed { get; set; }
}

/// <summary>
/// Splits a program into routines: follows calls and jumps from the entry points
/// (recursive descent) and adds the code the emulator saw executing (so code reached
/// through jump tables is found too). A routine is everything reachable from a CALL
/// target without crossing another routine's start.
/// </summary>
public sealed class CodeMap
{
    private uint _addressMask = 0xFFFF;
    private readonly Dictionary<uint, Routine> _routines = new();
    private readonly Dictionary<uint, FlowInfo> _code = new();

    public IReadOnlyDictionary<uint, Routine> Routines => _routines;
    public IReadOnlyDictionary<uint, FlowInfo> Code => _code;

    public static CodeMap Build(Func<uint, byte> peek, IFlowAnalyzer flow, IEnumerable<uint> entries, bool[]? executed = null, int maxInstructions = 60000)
    {
        if (flow.AddressBits is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(flow));
        uint mask = (uint)((1UL << flow.AddressBits) - 1);
        var map = new CodeMap { _addressMask = mask };
        var starts = new HashSet<uint>(entries.Select(e => e & mask));
        if (executed != null)
        {
            // Executed code whose previous byte is not executed starts a block we did
            // not reach statically: treat it as an entry.
            for (int a = 0; a < executed.Length && (uint)a <= mask; a++)
                if (executed[a] && (a == 0 || !executed[a - 1]))
                    starts.Add((uint)a);
        }

        // 1. find every instruction and every call target
        var callTargets = new HashSet<uint>(entries.Select(e => e & mask));
        var work = new Stack<uint>(starts);
        while (work.Count > 0 && map._code.Count < maxInstructions)
        {
            uint a = work.Pop() & mask;
            while (!map._code.ContainsKey(a) && map._code.Count < maxInstructions)
            {
                var f = flow.Analyze(peek, a);
                map._code[a] = f;
                uint next = (f.Next ?? (a + (uint)f.Length)) & map._addressMask;
                switch (f.Kind)
                {
                    case FlowKind.Call:
                    case FlowKind.CallIf:
                        if (f.Target is uint ct) { callTargets.Add(ct & mask); work.Push(ct & mask); }
                        a = next;
                        continue;
                    case FlowKind.JumpIf:
                        if (f.Target is uint jt) work.Push(jt);
                        a = next;
                        continue;
                    case FlowKind.ReturnIf:
                        a = next;
                        continue;
                    case FlowKind.Jump:
                        if (f.Target is uint t) work.Push(t);
                        break;
                    case FlowKind.Normal:
                        a = next;
                        continue;
                }
                break;   // Return, Jump, JumpIndirect, Halt end the block
            }
        }

        // 2. group instructions into routines starting at call targets / entries
        foreach (var start in callTargets.Where(map._code.ContainsKey))
            map._routines[start] = new Routine { Start = start, End = start };
        foreach (var r in map._routines.Values.ToList())
            map.Group(r, executed);

        // 3. code the emulator saw running that no routine covers (reached through a jump
        // table, the loader or an IM 2 handler) becomes a routine of its own
        if (executed != null)
        {
            var covered = new HashSet<uint>(map._routines.Values.SelectMany(r => r.Instructions));
            foreach (var s in starts.OrderBy(x => x))
            {
                if (covered.Contains(s) || !map._code.ContainsKey(s) || map._routines.ContainsKey(s))
                    continue;
                var r = new Routine { Start = s, End = s };
                map._routines[s] = r;
                map.Group(r, executed);
                covered.UnionWith(r.Instructions);
            }
        }
        return map;
    }

    /// <summary>Collects the instructions of a routine: everything reachable from its start without crossing another routine's start.</summary>
    private void Group(Routine r, bool[]? executed)
    {
        var map = this;
        {
            var todo = new Stack<uint>();
            todo.Push(r.Start);
            while (todo.Count > 0)
            {
                uint a = todo.Pop();
                if (r.Instructions.Contains(a) || !map._code.TryGetValue(a, out var f))
                    continue;
                if (a != r.Start && map._routines.ContainsKey(a))
                    continue;
                r.Instructions.Add(a);
                if (executed != null && a < executed.Length && executed[a])
                    r.Executed = true;
                uint next = (f.Next ?? (a + (uint)f.Length)) & map._addressMask;
                r.End = Math.Max(r.End, (uint)Math.Min(map._addressMask, (ulong)a + (uint)Math.Max(1, f.Length) - 1));
                switch (f.Kind)
                {
                    case FlowKind.Call:
                    case FlowKind.CallIf:
                        if (f.Target is uint ct && map._routines.TryGetValue(ct, out var callee))
                        {
                            r.Calls.Add(ct);
                            callee.Callers.Add(r.Start);
                        }
                        todo.Push(next);
                        break;
                    case FlowKind.JumpIf:
                        if (f.Target is uint jt) todo.Push(jt);
                        todo.Push(next);
                        break;
                    case FlowKind.Jump:
                        if (f.Target is uint t)
                        {
                            if (map._routines.ContainsKey(t) && t != r.Start)
                                r.Calls.Add(t);   // tail call
                            else
                                todo.Push(t);
                        }
                        break;
                    case FlowKind.Normal:
                    case FlowKind.ReturnIf:
                        todo.Push(next);
                        break;
                }
            }
        }
    }

    /// <summary>The routine that contains <paramref name="address"/>, if any.</summary>
    public Routine? RoutineAt(uint address) =>
        _routines.Values.FirstOrDefault(r => r.Instructions.Contains(address & _addressMask));
}

/// <summary>Names and comments the user (or an AI) gave to a program, saved per game.</summary>
public sealed class CodeAnnotations
{
    private string? _folder;
    private string? _loadedPath;
    private byte[]? _expected;
    private byte[]? _exported;
    private bool _unreadable;
    [JsonIgnore] public string? Problem { get; private set; }
    [JsonIgnore] public bool CanRecover => _unreadable;
    [JsonIgnore] public string? RecoveryPath { get; private set; }
    [JsonIgnore] public bool HasUnsavedChanges { get; private set; }
    [JsonIgnore] public string? ExportedPath { get; private set; }
    [JsonIgnore] public bool HasVerifiedExport
    {
        get
        {
            if (_exported == null || ExportedPath == null || !Valid()) return false;
            try
            {
                byte[] current = JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true });
                byte[]? file = ReadFile(ExportedPath);
                return current.AsSpan().SequenceEqual(_exported) && file != null && file.AsSpan().SequenceEqual(_exported);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { return false; }
        }
    }
    public int Format { get; set; } = 1;
    public string GameIdentity { get; set; } = "";
    public string LegacySourceHash { get; set; } = "";
    [JsonIgnore] public string SourceFingerprint => _expected == null ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_expected));
    public Dictionary<string, string> Labels { get; set; } = new();
    public Dictionary<string, string> Comments { get; set; } = new();
    public Dictionary<string, string> Summaries { get; set; } = new();
    public Dictionary<string, string> Pseudocode { get; set; } = new();
    /// <summary>Code changes: address -> "original bytes|new bytes" in hex, to undo or apply again.</summary>
    public Dictionary<string, string> Patches { get; set; } = new();

    public static string Key(uint address) => address.ToString("X4");

    public string? Label(uint a) => Labels.TryGetValue(Key(a), out var l) ? l : null;
    public string? Comment(uint a) => Comments.TryGetValue(Key(a), out var c) ? c : null;

    private static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "anotaciones");

    private bool Valid() => Format == 1 && GameIdentity != null && LegacySourceHash != null &&
        (LegacySourceHash.Length == 0 || LegacySourceHash.Length == 64 && LegacySourceHash.All(char.IsAsciiHexDigit)) && new[] { Labels, Comments, Summaries, Pseudocode, Patches }
        .All(values => values != null && values.All(pair => pair.Value != null));

    private static string PathFor(string gameId, string folder)
    {
        if (string.IsNullOrWhiteSpace(gameId) || gameId is "." or ".." || gameId.Length > 180 ||
            gameId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || gameId.Contains('/') || gameId.Contains('\\'))
            throw new ArgumentException("La identidad de las anotaciones no es válida.");
        return Path.GetFullPath(Path.Combine(folder, gameId + ".json"));
    }

    public static CodeAnnotations Load(string gameId) => Load(gameId, Folder);

    public static CodeAnnotations Load(string gameId, string folder) => Load(gameId, folder, requireIdentity: false);

    public static CodeAnnotations Load(string gameId, string folder, bool requireIdentity)
    {
        string? path = null;
        byte[]? data = null;
        try
        {
            path = PathFor(gameId, folder);
            data = ReadFile(path);
            if (data != null)
            {
                using var reader = new StreamReader(new MemoryStream(data), detectEncodingFromByteOrderMarks: true);
                var notes = JsonSerializer.Deserialize<CodeAnnotations>(reader.ReadToEnd()) ?? throw new InvalidDataException();
                if (!notes.Valid() || (notes.GameIdentity.Length > 0 ? notes.GameIdentity != gameId : requireIdentity)) throw new InvalidDataException();
                notes._folder = Path.GetDirectoryName(path)!; notes._loadedPath = path; notes._expected = data; return notes;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            return new CodeAnnotations { _folder = path == null ? folder : Path.GetDirectoryName(path)!, _loadedPath = path, _expected = data, _unreadable = true,
                Problem = "No pude leer las anotaciones. Se conservó el archivo; los cambios quedan solo en esta sesión." };
        }
        return new CodeAnnotations { _folder = path == null ? folder : Path.GetDirectoryName(path)!, _loadedPath = path };
    }

    public void Save(string gameId) => TrySave(gameId, _folder ?? Folder);
    public void Save(string gameId, string folder) => TrySave(gameId, folder);

    private bool TrySave(string gameId, string folder)
    {
        HasUnsavedChanges = true;
        if (!Valid()) { Problem = "Las anotaciones contienen valores inválidos."; return false; }
        if (GameIdentity.Length > 0 && GameIdentity != gameId) { Problem = "Las anotaciones pertenecen a otra edición. Se conservó el archivo."; return false; }
        if (_unreadable) return false;
        try
        {
            string path = PathFor(gameId, folder);
            Directory.CreateDirectory(folder);
            using var gate = Lock(path);
            byte[]? current = ReadFile(path);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            byte[]? expected = string.Equals(path, _loadedPath, comparison) ? _expected : null;
            if (!(current == null ? expected == null : expected != null && current.AsSpan().SequenceEqual(expected)))
            {
                _folder = Path.GetDirectoryName(path)!; _loadedPath = path; _unreadable = true;
                Problem = "Otra instancia cambió las anotaciones. Los cambios siguen en esta sesión; recuperá el archivo para conservar ambas copias.";
                return false;
            }
            return WriteFile(gameId, folder, path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { Problem = "No pude guardar las anotaciones. Los cambios quedan en esta sesión."; return false; }
    }

    private bool WriteFile(string gameId, string folder, string path)
    {
        string? temporary = null;
        try
        {
            GameIdentity = gameId;
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, this, new JsonSerializerOptions { WriteIndented = true }); file.Flush(flushToDisk: true);
            }
            byte[] written = ReadFile(temporary)!;
            File.Move(temporary, path, overwrite: true);
            _folder = Path.GetDirectoryName(path)!; _loadedPath = path; _expected = written; _unreadable = false; HasUnsavedChanges = false; Problem = null;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { Problem = "No pude guardar las anotaciones. Los cambios quedan en esta sesión."; return false; }
        finally
        {
            try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Explicit user recovery: preserve the old file before publishing the session's notes.</summary>
    public bool Recover(string gameId)
    {
        string folder = _folder ?? Folder;
        if (!_unreadable) return TrySave(gameId, folder);
        if (!Valid()) { Problem = "Las anotaciones contienen valores inválidos."; return false; }
        if (GameIdentity.Length > 0 && GameIdentity != gameId) { Problem = "Las anotaciones pertenecen a otra edición. Se conservó el archivo."; return false; }
        try
        {
            string path = PathFor(gameId, folder);
            Directory.CreateDirectory(folder);
            using var gate = Lock(path);
            byte[] original = ReadFile(path) ?? throw new FileNotFoundException();
            string backup = path + ".respaldo-" + Guid.NewGuid().ToString("N");
            using (var preserved = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { preserved.Write(original); preserved.Flush(flushToDisk: true); }
            RecoveryPath = backup;
            // The same lock covers preservation and replacement; the writer being recovered is explicit.
            return WriteFile(gameId, folder, path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { Problem = "No pude conservar el archivo anterior. Las anotaciones siguen solo en esta sesión."; return false; }
    }

    /// <summary>A verified recovery copy of current session notes; never overwrites the requested destination.</summary>
    public string Export(string gameId, string path)
    {
        if (!Valid() || GameIdentity.Length > 0 && GameIdentity != gameId) throw new InvalidDataException("Las anotaciones pertenecen a otra edición. Se conservó el archivo.");
        GameIdentity = gameId;
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true });
        if (data.Length > 4 * 1024 * 1024) throw new InvalidDataException("Las anotaciones superan el máximo de 4 MB.");
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try { file.Write(data); file.Flush(flushToDisk: true); }
        catch
        {
            file.Dispose();
            try { File.Delete(path); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
        _exported = data; ExportedPath = path;
        return path;
    }

    private static FileStream Lock(string path)
    {
        RejectLink(path + ".lock");
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException();
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static byte[]? ReadFile(string path)
    {
        try
        {
            RejectLink(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException();
            byte[] data = new byte[(int)file.Length]; file.ReadExactly(data); return data;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
