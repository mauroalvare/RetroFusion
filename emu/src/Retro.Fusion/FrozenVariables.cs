using Retro.Core;

namespace Retro.Fusion;

/// <summary>Pins RAM without retaining save states; all access stays on the emulation thread.</summary>
public sealed class FrozenVariables(IMachine machine, GameProfile profile) : IDisposable
{
    private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, (byte Value, int Mask)> _pinned = new();
    private IDisposable? _subscription;
    private bool _restoring;
    public IReadOnlyDictionary<string, int> Values => _values;

    public void Validate(IReadOnlyList<VariableEdit> changes)
    {
        GameModifications.Validate(profile, changes, machine.Memory.AddressBits);
        VariableMemory.ValidateOverlap(profile, _values.Keys.Where(name => changes.All(c => c.Variable != name)).Concat(changes.Select(c => c.Variable)), machine.Id);
        if (changes.Any(c => c.Freeze) && machine is not IWriteWatch)
            throw new NotSupportedException("Esta máquina no permite mantener valores congelados durante la partida.");
    }

    public void Apply(IReadOnlyList<VariableEdit> changes)
    {
        Validate(changes);
        var before = changes.SelectMany(c => VariableMemory.Cells(profile.Variables[c.Variable]))
            .Select(c => c.Address).Distinct().ToDictionary(a => a, machine.Memory.Peek);
        try
        {
            foreach (var change in changes) profile.Variables[change.Variable].Write(machine.Memory, change.Value);
            foreach (var change in changes)
                if (profile.Variables[change.Variable].Read(machine.Memory) != change.Value)
                    throw new InvalidOperationException($"El juego no permite escribir {change.Variable} en esa memoria. No se aplicó la prueba.");
        }
        catch
        {
            foreach (var (address, value) in before) machine.Memory.Poke(address, value);
            throw;
        }
        bool changed = false;
        foreach (var change in changes)
            if (change.Freeze)
            {
                changed |= !_values.TryGetValue(change.Variable, out int previous) || previous != change.Value;
                _values[change.Variable] = change.Value;
            }
            else changed |= _values.Remove(change.Variable);
        if (changed) Refresh();
    }

    internal void RestoreSelection(IReadOnlyDictionary<string, int> values)
    {
        _values.Clear();
        foreach (var pair in values) _values.Add(pair.Key, pair.Value);
        Refresh();
    }

    public void Release(string? name = null)
    {
        if (name == null) _values.Clear();
        else _values.Remove(name);
        Refresh();
    }

    public void Tick()
    {
        foreach (var (name, value) in _values) profile.Variables[name].Write(machine.Memory, value);
    }

    private void Refresh()
    {
        _pinned.Clear();
        var bytes = new EditBytes();
        foreach (var (name, value) in _values)
        {
            var variable = profile.Variables[name]; bytes.Values.Clear(); variable.Write(bytes, value);
            foreach (var cell in VariableMemory.Cells(variable))
            {
                uint address = VariableMemory.Canonical(machine.Id, cell.Address);
                var old = _pinned.GetValueOrDefault(address);
                _pinned[address] = ((byte)((old.Value & ~cell.Mask) | (bytes.Values[cell.Address] & cell.Mask)), old.Mask | cell.Mask);
            }
        }
        if (_pinned.Count > 0) _subscription ??= CpuWriteObservers.Subscribe((IWriteWatch)machine, KeepPinned);
        else { _subscription?.Dispose(); _subscription = null; }
    }

    private void KeepPinned(ushort address, byte value, ushort pc)
    {
        if (_restoring || !_pinned.TryGetValue(VariableMemory.Canonical(machine.Id, address), out var pin)) return;
        byte current = machine.Memory.Peek(address), desired = (byte)((current & ~pin.Mask) | (pin.Value & pin.Mask));
        if (current == desired) return;
        _restoring = true;
        try { machine.Memory.Poke(address, desired); }
        finally { _restoring = false; }
    }

    private sealed class EditBytes : IMemoryView
    {
        public int AddressBits => 32;
        public readonly Dictionary<uint, byte> Values = new();
        public byte Peek(uint address) => Values.GetValueOrDefault(address);
        public void Poke(uint address, byte value) => Values[address] = value;
    }

    public void Dispose() => Release();
}
