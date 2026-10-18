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

public readonly record struct FlowInfo(int Length, FlowKind Kind, uint? Target);

/// <summary>Control-flow classification of one instruction, per CPU.</summary>
public interface IFlowAnalyzer
{
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
    private readonly Dictionary<uint, Routine> _routines = new();
    private readonly Dictionary<uint, FlowInfo> _code = new();

    public IReadOnlyDictionary<uint, Routine> Routines => _routines;
    public IReadOnlyDictionary<uint, FlowInfo> Code => _code;

    public static CodeMap Build(Func<uint, byte> peek, IFlowAnalyzer flow, IEnumerable<uint> entries, bool[]? executed = null, int maxInstructions = 60000)
    {
        var map = new CodeMap();
        var starts = new HashSet<uint>(entries.Select(e => e & 0xFFFF));
        if (executed != null)
        {
            // Executed code whose previous byte is not executed starts a block we did
            // not reach statically: treat it as an entry.
            for (int a = 0; a < executed.Length; a++)
                if (executed[a] && (a == 0 || !executed[a - 1]))
                    starts.Add((uint)a);
        }

        // 1. find every instruction and every call target
        var callTargets = new HashSet<uint>(entries.Select(e => e & 0xFFFF));
        var work = new Stack<uint>(starts);
        while (work.Count > 0 && map._code.Count < maxInstructions)
        {
            uint a = work.Pop() & 0xFFFF;
            while (!map._code.ContainsKey(a) && map._code.Count < maxInstructions)
            {
                var f = flow.Analyze(peek, a);
                map._code[a] = f;
                uint next = (a + (uint)f.Length) & 0xFFFF;
                switch (f.Kind)
                {
                    case FlowKind.Call:
                    case FlowKind.CallIf:
                        if (f.Target is uint ct) { callTargets.Add(ct); work.Push(ct); }
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
                if (executed != null && executed[a])
                    r.Executed = true;
                uint next = (a + (uint)f.Length) & 0xFFFF;
                r.End = Math.Max(r.End, next - 1);
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
        _routines.Values.FirstOrDefault(r => r.Instructions.Contains(address & 0xFFFF));
}

/// <summary>Names and comments the user (or an AI) gave to a program, saved per game.</summary>
public sealed class CodeAnnotations
{
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

    public static CodeAnnotations Load(string gameId)
    {
        string path = Path.Combine(Folder, gameId + ".json");
        try
        {
            if (File.Exists(path))
                return System.Text.Json.JsonSerializer.Deserialize<CodeAnnotations>(File.ReadAllText(path)) ?? new();
        }
        catch (System.Text.Json.JsonException)
        {
        }
        return new CodeAnnotations();
    }

    public void Save(string gameId)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, gameId + ".json"),
            System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
