namespace Retro.Core;

public sealed record RoutineAccess(uint Instruction, ushort Address, bool Write, bool Io, int Count);
public sealed record RoutineCandidate(uint Address, int Instructions, int ObservedSteps, string CodeSha256, IReadOnlyList<RoutineAccess> Accesses);
public sealed record RoutineSurvey(int Steps, bool HasIoWatch, bool Truncated, bool ChangingCode, IReadOnlyList<RoutineCandidate> Routines);

/// <summary>Bounded observations on a disposable machine. Advances it; never use the player's running machine.</summary>
public static class RoutineFinder
{
    private sealed class RecordedFlow(Dictionary<uint, FlowInfo> code, HashSet<uint> entries) : IFlowAnalyzer
    {
        public FlowInfo Analyze(Func<uint, byte> peek, uint address)
        {
            if (!code.TryGetValue(address, out var info)) return new(1, FlowKind.Halt, null);
            // Limit the number of routine roots even on adversarial input.
            return info.Kind is FlowKind.Call or FlowKind.CallIf && info.Target is uint target && !entries.Contains(target)
                ? new(info.Length, FlowKind.Normal, null) { Next = info.Next } : info;
        }
    }

    public static RoutineSurvey Sample(IMachine machine, int steps = 12000, CancellationToken ct = default)
    {
        if (machine.Memory.AddressBits != 16) throw new NotSupportedException("La búsqueda de rutinas admite direcciones de 16 bits.");
        if (steps is < 1 or > 50000) throw new ArgumentOutOfRangeException(nameof(steps));
        ct.ThrowIfCancellationRequested();
        var flows = new Dictionary<uint, FlowInfo>();
        var bytes = new Dictionary<uint, byte[]>();
        var visits = new Dictionary<uint, int>();
        var entries = new HashSet<uint> { machine.Cpu.ProgramCounter };
        var accesses = new Dictionary<(uint Pc, ushort Address, bool Write, bool Io), int>();
        bool truncated = false, changed = false;
        uint current = machine.Cpu.ProgramCounter;
        void Record(ushort address, bool write, bool io)
        {
            var key = (current, address, write, io);
            if (accesses.TryGetValue(key, out int count)) accesses[key] = count + 1;
            else if (accesses.Count < 8192) accesses[key] = 1;
            else truncated = true;
        }
        using var writes = machine is IWriteWatch watch ? CpuWriteObservers.Subscribe(watch, (a, _, _) => Record(a, true, false)) : null;
        var io = machine as IIoWatch;
        var previous = io?.IoWatch;
        Action<ushort, byte, bool, ushort> observer = (a, v, w, p) => { previous?.Invoke(a, v, w, p); Record(a, w, true); };
        if (io != null) io.IoWatch = observer;
        try
        {
            for (int i = 0; i < steps; i++)
            {
                if ((i & 127) == 0) ct.ThrowIfCancellationRequested();
                current = machine.Cpu.ProgramCounter & 0xFFFF;
                var flow = machine.Flow.Analyze(machine.Memory.Peek, current);
                byte[] instruction = Enumerable.Range(0, Math.Clamp(flow.Length, 1, 16)).Select(n => machine.Memory.Peek((current + (uint)n) & 0xFFFF)).ToArray();
                if (bytes.TryGetValue(current, out var before) && !before.AsSpan().SequenceEqual(instruction)) changed = true;
                else { bytes[current] = instruction; flows[current] = flow; }
                visits[current] = visits.GetValueOrDefault(current) + 1;
                machine.StepInstruction();
                if (flow.Kind is FlowKind.Call or FlowKind.CallIf && flow.Target == machine.Cpu.ProgramCounter)
                {
                    if (entries.Count < 128) entries.Add(machine.Cpu.ProgramCounter); else truncated = true;
                }
            }
        }
        finally { if (io != null && ReferenceEquals(io.IoWatch, observer)) io.IoWatch = previous; }
        ct.ThrowIfCancellationRequested();
        // A bank switch or self-modification makes a single address map misleading.
        if (changed) return new(steps, io != null, truncated, true, []);
        var map = CodeMap.Build(_ => 0, new RecordedFlow(flows, entries), entries, maxInstructions: flows.Count + 128);
        var grouped = accesses.Select(p => new RoutineAccess(p.Key.Pc, p.Key.Address, p.Key.Write, p.Key.Io, p.Value)).ToLookup(a => a.Instruction);
        var candidates = new List<RoutineCandidate>();
        foreach (var routine in map.Routines.Values)
        {
            var observed = routine.Instructions.Where(bytes.ContainsKey).Order().ToList();
            if (observed.Count == 0) continue;
            var effects = observed.SelectMany(a => grouped[a]).OrderByDescending(a => a.Io).ThenByDescending(a => a.Count).ThenBy(a => a.Instruction).ThenBy(a => a.Address).ToList();
            string identity = RoutineCodeIdentity.Compute(observed.Select(address => (address, bytes[address])));
            candidates.Add(new(routine.Start, observed.Count, observed.Sum(a => visits[a]), identity, effects.Take(16).ToArray()));
            if (effects.Count > 16) truncated = true;
        }
        if (candidates.Count > 8) truncated = true;
        return new(steps, io != null, truncated, false, candidates.OrderByDescending(r => r.Accesses.Any(a => a.Io))
            .ThenByDescending(r => r.Accesses.Sum(a => a.Count)).ThenBy(r => r.Address).Take(8).ToArray());
    }
}
