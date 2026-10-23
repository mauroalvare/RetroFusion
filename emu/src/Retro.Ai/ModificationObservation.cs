using System.Text;
using System.Text.RegularExpressions;
using Retro.Core;
using Retro.Fusion;

namespace Retro.Ai;

/// <summary>Bounded CPU write evidence on the emulation thread, independent from edits.</summary>
public sealed class ModificationObservation : IDisposable
{
    private readonly IMachine _machine;
    private readonly byte[] _initial;
    private readonly IDisposable? _subscription;
    private readonly long _startFrame;
    private readonly Dictionary<uint, Writes> _writes = new();
    private readonly Queue<uint>?[] _pages = new Queue<uint>?[256];
    private sealed class Writes
    {
        public int Count, Changes;
        public ushort Pc;
        public byte Minimum = 255, Maximum, Last;
        public readonly List<byte> Recent = new();
        public void Add(byte value, ushort pc)
        {
            if (Count > 0 && value != Last) Changes = Math.Min(Changes + 1, int.MaxValue - 1);
            Count = Math.Min(Count + 1, int.MaxValue - 1); Pc = pc;
            Minimum = Math.Min(Minimum, value); Maximum = Math.Max(Maximum, value);
            if (Recent.Count == 0 || Last != value)
            {
                if (Recent.Count == 6) Recent.RemoveAt(0);
                Recent.Add(value);
            }
            Last = value;
        }
    }
    public int WrittenAddresses => _writes.Count;
    public long FramesObserved => Math.Max(0, _machine.FrameCount - _startFrame);

    public ModificationObservation(IMachine machine)
    {
        if (machine.Memory.AddressBits != 16) throw new NotSupportedException("El análisis admite máquinas de 16 bits de dirección.");
        _machine = machine; _initial = Image(machine); _startFrame = machine.FrameCount;
        if (machine is IWriteWatch watch)
            _subscription = CpuWriteObservers.Subscribe(watch, (address, value, pc) =>
            {
                uint canonical = VariableMemory.Canonical(machine.Id, address);
                if (!IsWorkRam(machine.Id, canonical)) return;
                if (!_writes.TryGetValue(canonical, out var writes))
                {
                    int page = (int)(canonical >> 8);
                    if (_writes.Count >= 2048)
                    {
                        int largest = 0;
                        for (int i = 1; i < _pages.Length; i++)
                            if ((_pages[i]?.Count ?? 0) > (_pages[largest]?.Count ?? 0)) largest = i;
                        if ((_pages[page]?.Count ?? 0) >= _pages[largest]!.Count) return;
                        _writes.Remove(_pages[largest]!.Dequeue());
                    }
                    (_pages[page] ??= new()).Enqueue(canonical);
                    _writes[canonical] = writes = new Writes();
                }
                writes.Add(value, pc);
            });
    }

    public static bool IsWorkRam(string machine, uint address) => machine switch
    {
        "zx48" => address is >= 0x5B00 and <= 0xFFFF,
        "c64" => address is >= 0x0800 and <= 0x9FFF or >= 0xC000 and <= 0xCFFF,
        "nes" => address is <= 0xFF or >= 0x200 and <= 0x7FF,
        "invaders" => address is >= 0x2000 and <= 0x23FF,
        _ => false
    };

    // Round-robin pages keep a busy sprite table from hiding a quieter state variable.
    private static IEnumerable<uint> Diverse(IEnumerable<uint> addresses)
    {
        var pages = addresses.GroupBy(a => a >> 8).Select(g => new Queue<uint>(g)).ToArray();
        while (pages.Any(q => q.Count > 0))
            foreach (var page in pages) if (page.TryDequeue(out uint address)) yield return address;
    }

    public GameAnalysisSnapshot Capture(string game, GameProfile profile)
    {
        byte[] image = Image(_machine);
        var references = new Dictionary<uint, string>();
        var instructions = new List<uint>();
        void Instruction(uint pc) { if (instructions.Count < 160 && !instructions.Contains(pc)) instructions.Add(pc); }
        var ranked = _writes.Keys.OrderByDescending(a => image[a] != _initial[a])
            .ThenByDescending(a => _writes[a].Changes > 0).ThenBy(a => _writes[a].Count).ToArray();
        foreach (uint address in Diverse(ranked).Take(24))
        {
            uint pc = _writes[address].Pc;
            if (_machine.ExecutedMap is { } map)
                for (uint start = pc > 16 ? pc - 16 : 0; start < pc; start++)
                    if (map[start] && start + _machine.Disassembler.Disassemble(a => image[a & 0xFFFF], start).Length <= pc) Instruction(start);
            for (int n = 0; n < 5; n++)
            {
                Instruction(pc);
                pc = (pc + (uint)Math.Max(1, _machine.Disassembler.Disassemble(a => image[a & 0xFFFF], pc).Length)) & 0xFFFF;
            }
        }
        Instruction(_machine.Cpu.ProgramCounter & 0xFFFF);
        if (_machine.ExecutedMap is { } executed)
            for (uint pc = 0; pc < Math.Min(executed.Length, 65536) && instructions.Count < 160; pc++)
                if (executed[pc] && IsGameCode(_machine.Id, pc)) Instruction(pc);
        var listing = new StringBuilder();
        foreach (uint pc in instructions)
        {
            var instruction = _machine.Disassembler.Disassemble(a => image[a & 0xFFFF], pc);
            string execution = _machine.ExecutedMap is { } map && map[pc] ? "" : " [contexto; ejecución no registrada]";
            listing.AppendLine($"0x{pc:X4}: {string.Join(" ", instruction.Bytes.Select(b => b.ToString("X2")))}  {instruction.Text}{execution}");
            foreach (Match match in Regex.Matches(instruction.Text, @"\$([0-9a-fA-F]{2,4})\b"))
            {
                uint address = VariableMemory.Canonical(_machine.Id, Convert.ToUInt32(match.Groups[1].Value, 16));
                if (IsWorkRam(_machine.Id, address)) references.TryAdd(address, $"Referencia en 0x{pc:X4}: {instruction.Text}");
            }
        }
        var candidates = new SortedDictionary<uint, MemoryEvidence>();
        void Add(uint raw, string reference = "")
        {
            uint address = VariableMemory.Canonical(_machine.Id, raw);
            if (!IsWorkRam(_machine.Id, address) || candidates.Count >= 128 && !candidates.ContainsKey(address)) return;
            _writes.TryGetValue(address, out var write);
            if (candidates.TryGetValue(address, out var old) && old.Reference.Length > 0) reference = old.Reference;
            candidates[address] = new(address, image[address], _initial[address], write?.Count ?? 0, write?.Pc, reference)
            {
                WriteMinimum = write?.Minimum, WriteMaximum = write?.Maximum, ValueChanges = write?.Changes ?? 0,
                RecentValues = write?.Recent.ToArray() ?? []
            };
        }
        foreach (var variable in profile.Variables.Values)
            foreach (var cell in VariableMemory.Cells(variable)) Add(cell.Address, "Variable del perfil de sesión; su descripción indica si es una hipótesis.");
        foreach (var (address, reference) in references.Take(16)) Add(address, reference);
        foreach (uint address in Diverse(ranked))
        {
            Add(address, references.GetValueOrDefault(address, ""));
            Add(address + 1, references.GetValueOrDefault(address + 1, ""));
        }
        foreach (var (address, reference) in references) { Add(address, reference); Add(address + 1); }
        return new(game, _machine.Id, _machine.FrameCount, listing.ToString(), candidates.Values.ToArray(), GameProfile.Parse(profile.ToJson()))
        { ObservationStartFrame = _startFrame };
    }

    private static bool IsGameCode(string machine, uint address) => machine switch
    {
        "zx48" => address >= 0x4000, "c64" => address is >= 0x800 and < 0xA000 or >= 0xC000 and < 0xD000,
        "invaders" => address < 0x2000, _ => true
    };

    private static byte[] Image(IMachine machine)
    {
        var image = new byte[65536];
        for (uint address = 0; address < image.Length; address++) image[address] = machine.Memory.Peek(address);
        return image;
    }

    public void Dispose() => _subscription?.Dispose();
}
