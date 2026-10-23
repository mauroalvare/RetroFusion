using System.Globalization;

namespace Retro.Fusion;

public readonly record struct VariableCell(uint Address, int Mask);

/// <summary>Storage shared by the analyzer, reversible experiments and studio scripts.</summary>
public static class VariableMemory
{
    public static uint Canonical(string machine, uint address) => machine switch
    {
        "nes" when address < 0x2000 => address & 0x7FF,
        "invaders" => address & 0x3FFF,
        _ => address
    };

    public static IReadOnlyList<VariableCell> Cells(GameVariable variable, int addressBits = 32)
    {
        uint Address(string? text)
        {
            if (addressBits is < 1 or > 32 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address) ||
                (ulong)address >= (1UL << addressBits))
                throw new InvalidOperationException("La dirección de la variable queda fuera de esta máquina.");
            return address;
        }
        uint start = Address(variable.Address);
        if (variable.Encoding == "uint9")
        {
            if (variable.Mask != 0 || variable.HighBitMask is < 1 or > 128 || (variable.HighBitMask & (variable.HighBitMask - 1)) != 0)
                throw new InvalidOperationException("uint9 necesita una máscara de un bit para el bit alto.");
            return [new(start, 255), new(Address(variable.HighBitAddress), variable.HighBitMask)];
        }
        int size = variable.Encoding switch
        {
            "uint8" => 1, "uint16" => 2,
            "bcd" or "digits" or "ascii" when variable.Bytes is >= 1 and <= 9 => variable.Bytes,
            _ => throw new InvalidOperationException("El formato de la variable no es válido para modificarlo.")
        };
        if ((ulong)start + (uint)size > (1UL << addressBits))
            throw new InvalidOperationException("La variable termina fuera de esta máquina.");
        return Enumerable.Range(0, size).Select(offset => new VariableCell(start + (uint)offset, variable.Mask == 0 ? 255 : variable.Mask)).ToArray();
    }

    public static void ValidateOverlap(GameProfile profile, IEnumerable<string> names, string? machine = null)
    {
        var occupied = new Dictionary<uint, int>();
        foreach (string name in names)
            foreach (var cell in Cells(profile.Variables[name]))
            {
                uint address = Canonical(machine ?? profile.Machine, cell.Address);
                if ((occupied.GetValueOrDefault(address) & cell.Mask) != 0)
                    throw new InvalidOperationException("Dos cambios usan los mismos bits de memoria. Liberá el congelado anterior o probalos por separado.");
                occupied[address] = occupied.GetValueOrDefault(address) | cell.Mask;
            }
    }
}
