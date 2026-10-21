using Retro.Cpu.I8088;

namespace I8088.Tests;

public sealed partial class CpuTests
{
    // Closed-form arithmetic and bit-ring rotation, independent of the CPU's bit steps.
    private static (int Value, bool Carry) ShiftResult(int op, int value, int count, bool carry, int bits)
    {
        int mask = (1 << bits) - 1;
        if (count == 0) return (value, carry);
        if (op < 4)
        {
            int ringBits = bits + (op >= 2 ? 1 : 0), rotation = count % ringBits;
            ulong ringMask = (1UL << ringBits) - 1;
            ulong ring = (uint)value | (op >= 2 && carry ? 1UL << bits : 0);
            ulong result = (op & 1) == 0
                ? ((ring << rotation) | (ring >> (ringBits - rotation))) & ringMask
                : ((ring >> rotation) | (ring << (ringBits - rotation))) & ringMask;
            return ((int)result & mask, op >= 2 ? (result & (1UL << bits)) != 0
                : op == 0 ? (result & 1) != 0 : (result & (1UL << (bits - 1))) != 0);
        }
        if (op == 4) return (count >= bits ? 0 : (value * (1 << count)) & mask,
            count <= bits && (value & (1 << (bits - count))) != 0);
        if (op == 5) return (count >= bits ? 0 : value / (1 << count),
            count <= bits && (value & (1 << (count - 1))) != 0);
        int signed = value >= 1 << (bits - 1) ? value - (1 << bits) : value;
        return (count >= bits ? signed < 0 ? mask : 0 : (int)Math.Floor(signed / Math.Pow(2, count)) & mask,
            count >= bits ? signed < 0 : (value & (1 << (count - 1))) != 0);
    }

    [TestCase(0, false)] [TestCase(1, false)] [TestCase(2, false)] [TestCase(3, false)]
    [TestCase(4, false)] [TestCase(5, false)] [TestCase(7, false)]
    [TestCase(0, true)] [TestCase(1, true)] [TestCase(2, true)] [TestCase(3, true)]
    [TestCase(4, true)] [TestCase(5, true)] [TestCase(7, true)]
    public void ShiftsAndRotatesMatchArithmeticOracleIncludingUnmaskedCl(int op, bool word)
    {
        var (cpu, _) = Program(word ? (byte)0xD3 : (byte)0xD2, (byte)(0xC0 | op << 3));
        int bits = word ? 16 : 8;
        int[] values = word ? [0, 1, 2, 3, 0x7F, 0x80, 0xFF, 0x100, 0x1234, 0x4000, 0x7FFF, 0x8000, 0x8001, 0xA55A, 0xFFFF]
            : Enumerable.Range(0, 256).ToArray();
        int[] counts = [0, 1, 2, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 64, 127, 128, 255];
        foreach (int value in values)
        foreach (int count in counts)
        for (int incoming = 0; incoming < 2; incoming++)
        {
            cpu.IP = 0; cpu.AX = (ushort)(word ? value : value | 0xA500); cpu.CX = (ushort)(0x5A00 | count);
            cpu.Flags = (ushort)(I8088Cpu.InterruptEnable | I8088Cpu.Direction | I8088Cpu.Auxiliary
                | I8088Cpu.Sign | I8088Cpu.Parity | I8088Cpu.Zero | (incoming == 1 ? I8088Cpu.Carry | I8088Cpu.Overflow : 0));
            ushort before = cpu.Flags;
            var (result, carry) = ShiftResult(op, value, count, incoming == 1, bits);
            cpu.Step();
            int expectedAx = word ? result : result | 0xA500;
            Assert.That(cpu.AX, Is.EqualTo(expectedAx), $"op={op}, bits={bits}, value={value:X4}, count={count}, carry={incoming}");
            if (count == 0) { Assert.That(cpu.Flags, Is.EqualTo(before)); continue; }
            Assert.That(cpu.Flag(I8088Cpu.Carry), Is.EqualTo(carry));
            const ushort control = I8088Cpu.InterruptEnable | I8088Cpu.Direction;
            Assert.That(cpu.Flags & control, Is.EqualTo(before & control));
            if (op < 4)
            {
                const ushort untouched = I8088Cpu.Sign | I8088Cpu.Parity | I8088Cpu.Zero | I8088Cpu.Auxiliary;
                Assert.That(cpu.Flags & untouched, Is.EqualTo(before & untouched));
            }
            else
            {
                Assert.That(cpu.Flag(I8088Cpu.Sign), Is.EqualTo(result >= 1 << (bits - 1)));
                Assert.That(cpu.Flag(I8088Cpu.Zero), Is.EqualTo(result == 0));
                Assert.That(cpu.Flag(I8088Cpu.Parity), Is.EqualTo(System.Numerics.BitOperations.PopCount((uint)(result & 255)) % 2 == 0));
            }
            if (count == 1)
            {
                bool msb = result >= 1 << (bits - 1);
                bool overflow = op switch { 0 or 2 or 4 => msb != carry,
                    1 or 3 => msb != ((result & (1 << (bits - 2))) != 0),
                    5 => value >= 1 << (bits - 1), _ => false };
                Assert.That(cpu.Flag(I8088Cpu.Overflow), Is.EqualTo(overflow));
            }
        }
    }

    [TestCase(0xD0, 0x9103)] [TestCase(0xD1, 0x2303)]
    [TestCase(0xD2, 0x9106)] [TestCase(0xD3, 0x4606)]
    public void OneBitAndClEncodingsSelectTheirOwnCount(int opcode, int expected)
    {
        var (cpu, _) = Program((byte)opcode, 0xC0); cpu.AX = 0x9181; cpu.CX = 2;
        cpu.Step(); Assert.That(cpu.AX, Is.EqualTo(expected));
    }

    [Test]
    public void ClAndCxAsDestinationsDoNotChangeTheCountDuringTheOperation()
    {
        var (cpu, _) = Program(0xD2, 0xC1, 0xD3, 0xC1);
        cpu.CX = 0xAA09; cpu.Step(); Assert.That(cpu.CX, Is.EqualTo(0xAA12));
        cpu.CX = 0x0909; cpu.Step(); Assert.That(cpu.CX, Is.EqualTo(0x1212));
    }

    [Test]
    public void HighByteAndOverriddenMemoryDestinationUseNormalOperandDecoding()
    {
        var (cpu, bus) = Program(0xD0, 0xFC, 0x26, 0xD1, 0x6E, 0); // SAR AH,1; SHR word ES:[BP],1
        cpu.AX = 0x8123; cpu.BP = 0xFFFF; cpu.ES = 0x4000; cpu.DS = 0x2000;
        cpu.WriteWord(cpu.ES, cpu.BP, 0x8003); cpu.WriteWord(cpu.DS, cpu.BP, 0xBEEF);
        cpu.Step(); Assert.That(cpu.AX, Is.EqualTo(0xC023));
        Assert.That(cpu.Flag(I8088Cpu.Carry), Is.True); Assert.That(cpu.Flag(I8088Cpu.Overflow), Is.False);
        cpu.Step(); Assert.That(cpu.ReadWord(cpu.ES, 0xFFFF), Is.EqualTo(0x4001));
        Assert.That(bus.Memory[0x4FFFF], Is.EqualTo(1)); Assert.That(bus.Memory[0x40000], Is.EqualTo(0x40));
        Assert.That(cpu.ReadWord(cpu.DS, 0xFFFF), Is.EqualTo(0xBEEF));
        Assert.That(cpu.Flag(I8088Cpu.Carry), Is.True); Assert.That(cpu.Flag(I8088Cpu.Overflow), Is.True);
    }
}
