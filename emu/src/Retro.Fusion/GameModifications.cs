using Retro.Core;

namespace Retro.Fusion;

public sealed record VariableEdit(string Variable, int Value, bool Freeze);

/// <summary>RAM experiments for one running game. Nothing writes to the original media or profile.</summary>
public sealed class GameModifications(IMachine machine, GameProfile profile) : IDisposable
{
    private sealed record UndoPoint(byte[] State, int[] Picture, Dictionary<string, int> Frozen, Dictionary<string, VariableEdit> Selection);
    private readonly LinkedList<UndoPoint> _undo = new();
    private bool _historyTrimmed;
    private readonly Dictionary<string, VariableEdit> _selection = new(StringComparer.Ordinal);
    public IReadOnlyCollection<VariableEdit> Selection => _selection.Values;
    public bool CanUndo => _undo.Count > 0;
    private byte[]? _returnPoint;
    private int[]? _returnPicture;
    private readonly FrozenVariables _pins = new(machine, profile);
    public bool CanRestore => _returnPoint != null;
    public IReadOnlyDictionary<string, int> Frozen => _pins.Values;

    public static void Validate(GameProfile profile, IReadOnlyList<VariableEdit> changes, int addressBits = 32)
    {
        if (changes.Count is < 1 or > 8) throw new InvalidOperationException("Cada prueba debe cambiar entre una y ocho variables.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (string.IsNullOrWhiteSpace(change.Variable) || !seen.Add(change.Variable))
                throw new InvalidOperationException("Una variable no puede repetirse en la misma prueba.");
            if (!profile.Variables.TryGetValue(change.Variable, out var variable))
                throw new InvalidOperationException($"La variable {change.Variable} no está en el perfil de este juego.");
            if (variable.ReadOnly) throw new InvalidOperationException($"{change.Variable} es de solo lectura.");
            int size = variable.Encoding switch
            {
                "uint8" or "uint9" => 1, "uint16" => 2,
                "bcd" or "digits" or "ascii" when variable.Bytes is >= 1 and <= 9 => variable.Bytes,
                _ => throw new InvalidOperationException($"El formato de {change.Variable} no es válido para modificarlo.")
            };
            long capacity = variable.Encoding switch
            {
                "uint8" => variable.Mask == 0 ? 255 : variable.Mask,
                "uint9" => 511, "uint16" => 65535,
                "bcd" => (long)Math.Min(int.MaxValue, Math.Pow(100, size) - 1),
                _ => (long)Math.Min(int.MaxValue, Math.Pow(10, size) - 1)
            };
            if (variable.Min < 0 || variable.Max < variable.Min || variable.Mask is < 0 or > 255 || variable.Mask != 0 && variable.Encoding != "uint8" ||
                change.Value < variable.Min || change.Value > variable.Max || change.Value > capacity ||
                variable.Encoding == "uint8" && variable.Mask != 0 && (change.Value & ~variable.Mask) != 0)
                throw new InvalidOperationException($"{change.Variable}: usá un valor representable entre {variable.Min} y {Math.Min(variable.Max, capacity)}.");
            _ = VariableMemory.Cells(variable, addressBits);
        }
        VariableMemory.ValidateOverlap(profile, changes.Select(c => c.Variable));
    }

    public void Apply(IReadOnlyList<VariableEdit> changes) => ApplyCore(changes, replace: false);

    /// <summary>Reapplies a saved selection as one reversible edit, with no intermediate partial state.</summary>
    public void ApplySelection(IReadOnlyList<VariableEdit> changes) => ApplyCore(changes, replace: true);

    private void ApplyCore(IReadOnlyList<VariableEdit> changes, bool replace)
    {
        if (!replace) _pins.Validate(changes);
        else
        {
            if (changes.Count is < 1 or > 256) throw new InvalidOperationException("La selección debe contener entre una y 256 variables.");
            foreach (var batch in changes.Chunk(8)) Validate(profile, batch, machine.Memory.AddressBits);
            VariableMemory.ValidateOverlap(profile, changes.Select(c => c.Variable), machine.Id);
            if (changes.Any(c => c.Freeze) && machine is not IWriteWatch)
                throw new NotSupportedException("Esta máquina no permite mantener valores congelados durante la partida.");
        }
        var frozenBefore = new Dictionary<string, int>(Frozen, StringComparer.Ordinal);
        var selectedBefore = new Dictionary<string, VariableEdit>(_selection, StringComparer.Ordinal);
        byte[] before = machine.SaveState();
        int[] picture = (int[])machine.Video.Pixels.Clone();
        try
        {
            if (replace)
            {
                _pins.Release();
                foreach (var batch in changes.Chunk(8)) _pins.Apply(batch);
                _selection.Clear();
            }
            else _pins.Apply(changes);
            // A later edit replaces an older overlapping target, including mirrored RAM.
            var editedCells = changes.SelectMany(c => VariableMemory.Cells(profile.Variables[c.Variable])).ToArray();
            foreach (var name in _selection.Keys.ToArray())
                if (VariableMemory.Cells(profile.Variables[name]).Any(old => editedCells.Any(cell =>
                    VariableMemory.Canonical(machine.Id, old.Address) == VariableMemory.Canonical(machine.Id, cell.Address) && (old.Mask & cell.Mask) != 0)))
                    _selection.Remove(name);
            foreach (var change in changes) _selection[change.Variable] = change;
        }
        catch
        {
            machine.LoadState(before);
            picture.CopyTo(machine.Video.Pixels, 0);
            _pins.RestoreSelection(frozenBefore);
            _selection.Clear();
            foreach (var pair in selectedBefore) _selection.Add(pair.Key, pair.Value);
            throw;
        }
        _undo.AddLast(new UndoPoint(before, picture, frozenBefore, selectedBefore));
        if (_undo.Count > 5) { _undo.RemoveFirst(); _historyTrimmed = true; }
        _returnPoint ??= before;
        _returnPicture ??= picture;
    }

    /// <summary>Called before each emulated frame, including frames run in turbo.</summary>
    public void Tick() => _pins.Tick();
    public void ReleaseAll()
    {
        _pins.Release();
        foreach (var name in _selection.Keys.ToArray()) _selection[name] = _selection[name] with { Freeze = false };
    }
    public void Dispose() => _pins.Dispose();

    /// <summary>Restores the state before the latest edit, including the preceding freeze selection. Keeps five edits.</summary>
    public bool Undo()
    {
        if (_undo.Last is not { } last) return false;
        var point = last.Value;
        machine.LoadState(point.State);
        point.Picture.CopyTo(machine.Video.Pixels, 0);
        _pins.RestoreSelection(point.Frozen);
        _selection.Clear();
        foreach (var pair in point.Selection) _selection.Add(pair.Key, pair.Value);
        _undo.RemoveLast();
        if (_undo.Count == 0 && !_historyTrimmed) { _returnPoint = null; _returnPicture = null; }
        return true;
    }

    /// <summary>Restores the complete machine at the first change, including CPU/video/time, and releases freezes.</summary>
    public bool Restore()
    {
        if (_returnPoint == null) return false;
        machine.LoadState(_returnPoint);
        _returnPicture!.CopyTo(machine.Video.Pixels, 0);
        _returnPoint = null;
        _returnPicture = null;
        _undo.Clear(); _historyTrimmed = false;
        ReleaseAll();
        _selection.Clear();
        return true;
    }
}
