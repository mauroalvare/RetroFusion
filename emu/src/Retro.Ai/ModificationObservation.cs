using System.Text;
using System.Text.RegularExpressions;
using Retro.Core;
using Retro.Fusion;

namespace Retro.Ai;

/// <summary>Bounded observation on the emulation thread. Disposing restores a previous write observer.</summary>
public sealed class ModificationObservation : IDisposable
{
    private readonly IMachine _machine;
    private readonly byte[] _initial;
    private readonly IWriteWatch? _watch;
    private readonly Action<ushort, byte, ushort>? _previous, _callback;
    private readonly Dictionary<uint, (int Count, uint Pc)> _writes = new();
    public int WrittenAddresses => _writes.Count;

    public ModificationObservation(IMachine machine)
    {
        if (machine.Memory.AddressBits != 16) throw new NotSupportedException("El análisis admite máquinas de 16 bits de dirección.");
        _machine = machine;
        _initial = Image(machine);
        _watch = machine as IWriteWatch;
        if (_watch != null)
        {
            _previous = _watch.WriteWatch;
            _callback = (address, value, pc) =>
            {
                _previous?.Invoke(address, value, pc);
                if (!IsWorkRam(machine.Id, address)) return;
                if (_writes.TryGetValue(address, out var old))
                    _writes[address] = (Math.Min(old.Count + 1, int.MaxValue - 1), pc);
                else if (_writes.Count < 2048) _writes[address] = (1, pc);
            };
            _watch.WriteWatch = _callback;
        }
    }

    public static bool IsWorkRam(string machine, uint address) => machine switch
    {
        "zx48" => address is >= 0x5B00 and <= 0xFFFF,
        "c64" => address is >= 0x0800 and <= 0x9FFF or >= 0xC000 and <= 0xCFFF,
        "nes" => address is <= 0xFF or >= 0x200 and <= 0x7FF,
        "invaders" => address is >= 0x2000 and <= 0x23FF,
        _ => false
    };

    public GameAnalysisSnapshot Capture(string game, GameProfile profile)
    {
        byte[] image = Image(_machine);
        var references = new Dictionary<uint, string>();
        var instructions = new List<uint>();
        foreach (var pc in _writes.Values.OrderByDescending(w => w.Count).Select(w => w.Pc))
            if (!instructions.Contains(pc) && instructions.Count < 48) instructions.Add(pc);
        instructions.Add(_machine.Cpu.ProgramCounter & 0xFFFF);
        if (_machine.ExecutedMap is { } executed)
            for (uint address = 0; address < Math.Min(executed.Length, 65536) && instructions.Count < 160; address++)
                if (executed[address] && IsGameCode(_machine.Id, address) && !instructions.Contains(address)) instructions.Add(address);
        var listing = new StringBuilder();
        foreach (uint pc in instructions.Distinct().Take(160))
        {
            var instruction = _machine.Disassembler.Disassemble(a => image[a & 0xFFFF], pc);
            listing.AppendLine($"0x{pc:X4}: {string.Join(" ", instruction.Bytes.Select(b => b.ToString("X2")))}  {instruction.Text}");
            foreach (Match match in Regex.Matches(instruction.Text, @"\$([0-9a-fA-F]{2,4})\b"))
            {
                uint address = Convert.ToUInt32(match.Groups[1].Value, 16);
                if (IsWorkRam(_machine.Id, address)) references.TryAdd(address, $"Referencia en 0x{pc:X4}: {instruction.Text}");
            }
        }
        var candidates = new SortedDictionary<uint, MemoryEvidence>();
        void Add(uint address, string reference = "")
        {
            if (candidates.Count >= 128 || !IsWorkRam(_machine.Id, address)) return;
            var write = _writes.GetValueOrDefault(address);
            if (candidates.TryGetValue(address, out var old) && old.Reference.Length > 0) reference = old.Reference;
            candidates[address] = new(address, image[address], _initial[address], write.Count,
                write.Count > 0 ? write.Pc : null, reference);
        }
        foreach (var variable in profile.Variables.Values)
            if (uint.TryParse(variable.Address, System.Globalization.NumberStyles.HexNumber, null, out uint address))
            { Add(address, "Variable del perfil conocido."); Add(address + 1); }
        foreach (var (address, _) in _writes.OrderByDescending(w => image[w.Key] != _initial[w.Key]).ThenByDescending(w => w.Value.Count))
        { Add(address, references.GetValueOrDefault(address, "")); Add(address + 1); }
        foreach (var (address, reference) in references) { Add(address, reference); Add(address + 1); }
        return new(game, _machine.Id, _machine.FrameCount, listing.ToString(), candidates.Values.ToArray(),
            GameProfile.Parse(profile.ToJson()));
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

    public void Dispose()
    {
        if (_watch != null && ReferenceEquals(_watch.WriteWatch, _callback)) _watch.WriteWatch = _previous;
    }
}
