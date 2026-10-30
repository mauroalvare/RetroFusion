using System.Globalization;
using System.Text.RegularExpressions;

namespace Retro.Systems.Nes;

public sealed record NestestDifference(int Line, string Expected, string Actual);
public sealed record NestestResult(int States, long LastCycle, NestestDifference? Difference)
{
    public bool Ok => Difference == null;
}

/// <summary>CPU states before instructions in nestest's C000 automation mode. Does not certify PPU/APU or DMA.</summary>
public static partial class NestestVerifier
{
    private sealed record State(ushort Pc, byte A, byte X, byte Y, byte P, byte Sp, long Cycles)
    {
        public override string ToString() => $"PC:{Pc:X4} A:{A:X2} X:{X:X2} Y:{Y:X2} P:{P:X2} SP:{Sp:X2} CYC:{Cycles}";
    }

    [GeneratedRegex(@"^([0-9A-F]{4})\s+.*?A:([0-9A-F]{2})\s+X:([0-9A-F]{2})\s+Y:([0-9A-F]{2})\s+P:([0-9A-F]{2})\s+SP:([0-9A-F]{2})\s+.*?CYC:(\d+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TraceLine();

    private static State Parse(string line, int index)
    {
        if (line.Length > 512) throw new InvalidDataException($"Línea {index}: registro nestest demasiado largo.");
        var match = TraceLine().Match(line);
        if (!match.Success || !long.TryParse(match.Groups[7].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long cycles))
            throw new InvalidDataException($"Línea {index}: se esperaba el formato nestest con PC, A, X, Y, P, SP y CYC.");
        ushort Hex(int group) => ushort.Parse(match.Groups[group].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new(Hex(1), (byte)Hex(2), (byte)Hex(3), (byte)Hex(4), (byte)Hex(5), (byte)Hex(6), cycles);
    }

    public static NestestResult Verify(byte[] rom, IEnumerable<string> lines, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var expected = new List<State>();
        foreach (string line in lines)
        {
            ct.ThrowIfCancellationRequested();
            if (expected.Count == 100000) throw new InvalidDataException("El registro nestest supera 100000 estados.");
            expected.Add(Parse(line, expected.Count + 1));
        }
        if (expected.Count == 0) throw new InvalidDataException("El registro nestest está vacío.");
        var initial = new State(0xC000, 0, 0, 0, 0x24, 0xFD, 7);
        if (expected[0] != initial) throw new InvalidDataException("El registro no empieza en el estado canónico de nestest: " + initial);
        var machine = new Nes(NesCartridge.Load(rom));
        var cpu = machine.Cpu2A03;
        // Explicit diagnostic starting state, independent of the cartridge's interactive reset vector.
        cpu.PC = 0xC000; cpu.A = cpu.X = cpu.Y = 0; cpu.P = 0x24; cpu.S = 0xFD; cpu.CycleCount = 7;
        for (int i = 0; i < expected.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var actual = new State(cpu.PC, cpu.A, cpu.X, cpu.Y, cpu.P, cpu.S, cpu.CycleCount);
            if (actual != expected[i]) return new(i, cpu.CycleCount, new(i + 1, expected[i].ToString(), actual.ToString()));
            // The trace describes the state before this instruction. Do not run unobserved code after the last line.
            if (i + 1 < expected.Count) machine.StepInstruction();
        }
        return new(expected.Count, cpu.CycleCount, null);
    }
}
