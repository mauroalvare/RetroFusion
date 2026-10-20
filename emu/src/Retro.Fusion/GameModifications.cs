using System.Globalization;
using Retro.Core;

namespace Retro.Fusion;

public sealed record VariableEdit(string Variable, int Value, bool Freeze);

/// <summary>RAM experiments for one running game. Nothing writes to the original media or profile.</summary>
public sealed class GameModifications(IMachine machine, GameProfile profile)
{
    private byte[]? _returnPoint;
    private readonly Dictionary<string, int> _frozen = new(StringComparer.Ordinal);
    public bool CanRestore => _returnPoint != null;
    public IReadOnlyDictionary<string, int> Frozen => _frozen;

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
                "uint8" => 1, "uint16" => 2,
                "bcd" or "digits" or "ascii" when variable.Bytes is >= 1 and <= 9 => variable.Bytes,
                _ => throw new InvalidOperationException($"El formato de {change.Variable} no es válido para modificarlo.")
            };
            long capacity = variable.Encoding switch
            {
                "uint8" => variable.Mask == 0 ? 255 : variable.Mask,
                "uint16" => 65535,
                "bcd" => (long)Math.Min(int.MaxValue, Math.Pow(100, size) - 1),
                _ => (long)Math.Min(int.MaxValue, Math.Pow(10, size) - 1)
            };
            if (variable.Min < 0 || variable.Max < variable.Min || variable.Mask is < 0 or > 255 || variable.Mask != 0 && variable.Encoding != "uint8" ||
                change.Value < variable.Min || change.Value > variable.Max || change.Value > capacity ||
                variable.Encoding == "uint8" && variable.Mask != 0 && (change.Value & ~variable.Mask) != 0)
                throw new InvalidOperationException($"{change.Variable}: usá un valor representable entre {variable.Min} y {Math.Min(variable.Max, capacity)}.");
            if (addressBits is < 1 or > 32 || !uint.TryParse(variable.Address, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address) ||
                (ulong)address + (uint)size > (1UL << addressBits))
                throw new InvalidOperationException($"La dirección de {change.Variable} queda fuera de esta máquina.");
        }
    }

    public void Apply(IReadOnlyList<VariableEdit> changes)
    {
        Validate(profile, changes, machine.Memory.AddressBits);
        byte[] before = machine.SaveState();
        try
        {
            foreach (var change in changes) profile.Variables[change.Variable].Write(machine.Memory, change.Value);
            foreach (var change in changes)
                if (profile.Variables[change.Variable].Read(machine.Memory) != change.Value)
                    throw new InvalidOperationException($"El juego no permite escribir {change.Variable} en esa memoria. No se aplicó la prueba.");
        }
        catch
        {
            machine.LoadState(before);
            throw;
        }
        _returnPoint ??= before;
        foreach (var change in changes)
            if (change.Freeze) _frozen[change.Variable] = change.Value;
            else _frozen.Remove(change.Variable);
    }

    /// <summary>Called before each emulated frame, including frames run in turbo.</summary>
    public void Tick()
    {
        foreach (var (name, value) in _frozen) profile.Variables[name].Write(machine.Memory, value);
    }

    public void ReleaseAll() => _frozen.Clear();

    /// <summary>Restores the complete machine at the first change, including CPU/video/time, and releases freezes.</summary>
    public bool Restore()
    {
        if (_returnPoint == null) return false;
        machine.LoadState(_returnPoint);
        _returnPoint = null;
        _frozen.Clear();
        return true;
    }
}
