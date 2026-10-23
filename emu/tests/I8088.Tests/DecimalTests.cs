using Retro.Cpu.I8088;

namespace I8088.Tests;

public sealed partial class CpuTests
{
    private static int Packed(int value) => value / 10 * 16 + value % 10;
    private static void CheckDecimalSzp(I8088Cpu cpu, int value)
    {
        Assert.That(cpu.Flag(I8088Cpu.Zero), Is.EqualTo(value == 0));
        Assert.That(cpu.Flag(I8088Cpu.Sign), Is.EqualTo(value >= 128));
        Assert.That(cpu.Flag(I8088Cpu.Parity),
            Is.EqualTo(Convert.ToString(value, 2).Count(c => c == '1') % 2 == 0));
    }

    [TestCase(false)] [TestCase(true)]
    public void PackedBcdArithmeticMatchesDecimalForEveryValidPairAndCarry(bool subtract)
    {
        var (cpu, bus) = Program(subtract ? (byte)0x1C : (byte)0x14, 0, subtract ? (byte)0x2F : (byte)0x27);
        for (int a = 0; a < 100; a++)
        for (int b = 0; b < 100; b++)
        for (int carry = 0; carry < 2; carry++)
        {
            cpu.IP = 0; cpu.AX = (ushort)(0xA500 | Packed(a)); cpu.Flags = (ushort)(0x600 | carry);
            bus.Memory[0x10001] = (byte)Packed(b);
            cpu.Step(); bool overflow = cpu.Flag(I8088Cpu.Overflow);
            cpu.Step();
            int decimalResult = a + (subtract ? -1 : 1) * (b + carry);
            int expected = Packed((decimalResult % 100 + 100) % 100);
            if (cpu.AX != (0xA500 | expected) || cpu.Flag(I8088Cpu.Carry) != (decimalResult < 0 || decimalResult > 99))
                Assert.Fail($"{a} {(subtract ? "-" : "+")} {b}, carry={carry}: AX={cpu.AX:X4}, flags={cpu.Flags:X4}");
            Assert.That(cpu.Flag(I8088Cpu.Auxiliary),
                Is.EqualTo(subtract ? a % 10 < b % 10 + carry : a % 10 + b % 10 + carry >= 10));
            CheckDecimalSzp(cpu, expected);
            Assert.That(cpu.Flag(I8088Cpu.Overflow), Is.EqualTo(overflow));
            Assert.That(cpu.Flags & 0x600, Is.EqualTo(0x600));
        }
    }

    [TestCase(false)] [TestCase(true)]
    public void UnpackedBcdArithmeticAdjustsDecimalDigitAndHighByte(bool subtract)
    {
        var (cpu, bus) = Program(subtract ? (byte)0x1C : (byte)0x14, 0, subtract ? (byte)0x3F : (byte)0x37);
        foreach (int high in new[] { 0, 1, 127, 255 })
        for (int a = 0; a < 10; a++)
        for (int b = 0; b < 10; b++)
        for (int carry = 0; carry < 2; carry++)
        {
            cpu.IP = 0; cpu.AX = (ushort)((high << 8) | a); cpu.Flags = (ushort)(0x600 | carry);
            bus.Memory[0x10001] = (byte)b;
            cpu.Step(); ushort previous = cpu.Flags; cpu.Step();
            int result = a + (subtract ? -1 : 1) * (b + carry);
            bool adjusted = result < 0 || result > 9;
            int expected = (((high + (adjusted ? subtract ? -1 : 1 : 0)) & 255) << 8) | ((result % 10 + 10) % 10);
            Assert.That(cpu.AX, Is.EqualTo(expected));
            Assert.That(cpu.Flag(I8088Cpu.Carry), Is.EqualTo(adjusted));
            Assert.That(cpu.Flag(I8088Cpu.Auxiliary), Is.EqualTo(adjusted));
            Assert.That(cpu.Flags & ~(I8088Cpu.Auxiliary | I8088Cpu.Carry),
                Is.EqualTo(previous & ~(I8088Cpu.Auxiliary | I8088Cpu.Carry)));
        }
    }

    [TestCase(0x37, 0x00FF, 0, 0x0105)]
    [TestCase(0x37, 0xFFFA, 0, 0x0000)]
    [TestCase(0x3F, 0x0100, 0x10, 0x000A)]
    [TestCase(0x3F, 0x0005, 0x10, 0xFF0F)]
    [TestCase(0x37, 0x1232, 1, 0x1202)]
    [TestCase(0x3F, 0x1232, 1, 0x1202)]
    public void AsciiAdjustmentUsesSeparate8088BytesEvenForNonDecimalInputs(int opcode, int ax, int flags, int expected)
    {
        var (cpu, _) = Program((byte)opcode);
        cpu.AX = (ushort)ax; cpu.Flags = (ushort)flags; cpu.Step();
        Assert.That(cpu.AX, Is.EqualTo(expected));
    }

    [Test]
    public void AamIgnoresOldHighByteAndConvertsEveryByteToBaseTen()
    {
        var (cpu, _) = Program(0xD4, 10);
        foreach (int high in new[] { 0, 1, 255 })
        for (int low = 0; low < 256; low++)
        {
            cpu.IP = 0; cpu.AX = (ushort)((high << 8) | low); cpu.Flags = 0xE11;
            cpu.Step();
            Assert.That(cpu.AX, Is.EqualTo((low / 10) * 256 + low % 10));
            CheckDecimalSzp(cpu, low % 10);
            Assert.That(cpu.Flags & 0xE11, Is.EqualTo(0xE11));
        }
    }

    [Test]
    public void AadConvertsEveryWordToByteAndClearsHighByte()
    {
        var (cpu, _) = Program(0xD5, 10);
        for (int ax = 0; ax < 65536; ax++)
        {
            cpu.IP = 0; cpu.AX = (ushort)ax; cpu.Flags = 0xE11;
            cpu.Step();
            int expected = ((ax >> 8) * 10 + (ax & 255)) % 256;
            Assert.That(cpu.AX, Is.EqualTo(expected));
            CheckDecimalSzp(cpu, expected);
            Assert.That(cpu.Flags & 0xE11, Is.EqualTo(0xE11));
        }
    }

    [Test]
    public void DecimalOperationsComposeWithMultiplyAndDivide()
    {
        var (cpu, _) = Program(0xB0, 9, 0xB3, 9, 0xF6, 0xE3, 0xD4, 10, // MUL, AAM => 0801
            0xD5, 10, 0xF6, 0xF3, 0xF4); // AAD, DIV => 0009
        Run(cpu); Assert.That(cpu.AX, Is.EqualTo(9));
    }

    [TestCase(0xD4)] [TestCase(0xD5)]
    public void DecimalImmediateFetchWrapsAtEndOfCodeSegment(int opcode)
    {
        var (cpu, bus) = Program(10, 0xF4);
        bus.Memory[0x1FFFF] = (byte)opcode; cpu.IP = 0xFFFF; cpu.AX = 0x0105;
        cpu.Step();
        Assert.That(cpu.IP, Is.EqualTo(1));
        Assert.That(cpu.AX, Is.EqualTo(opcode == 0xD4 ? 5 : 15));
    }

    [TestCase(0xD4, 16, 0x0F0E)] [TestCase(0xD5, 0, 0x00FE)] [TestCase(0xD5, 16, 0x009E)]
    public void HardwareVerifiedRadicesExtendBeyondDecimal(int opcode, int radix, int expected)
    {
        var (cpu, _) = Program((byte)opcode, (byte)radix); cpu.AX = 0xCAFE;
        cpu.Step(); Assert.That(cpu.AX, Is.EqualTo(expected));
    }

    [Test]
    public void AamZeroEntersTheDivideVectorAndReturnsPastTheImmediate()
    {
        var (cpu, bus) = Program(0xD4, 0); DivideVector(cpu, bus); cpu.AX = 0xCAFE;
        cpu.Step(); Assert.That(cpu.CS, Is.EqualTo(0x3000));
        Assert.That(cpu.Flags & (I8088Cpu.Sign | I8088Cpu.Zero | I8088Cpu.Parity), Is.EqualTo(I8088Cpu.Zero | I8088Cpu.Parity));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(2));
        cpu.Step(); Assert.That(cpu.IP, Is.EqualTo(2));
    }

    [TestCase(0x27, 0x369E, 0x10, 0x36A4, false)]
    [TestCase(0x27, 0x009A, 0, 0x0000, true)]
    [TestCase(0x2F, 0xEA01, 0x10, 0xEAFB, false)]
    public void DecimalAdjustMatchesNmosHardwareOutsideValidBcd(int opcode, int initial, int flags, int expected, bool carry)
    {
        var (cpu, _) = Program((byte)opcode); cpu.AX = (ushort)initial; cpu.Flags = (ushort)flags;
        cpu.Step(); Assert.That(cpu.AX, Is.EqualTo(expected));
        Assert.That(cpu.Flag(I8088Cpu.Carry), Is.EqualTo(carry));
    }
    [TestCase(0x8C, 0xF8)] [TestCase(0x8E, 0xE0)] [TestCase(0x8E, 0xF8)]
    public void SegmentMovesIgnoreTheHighRegisterSelectorBitOn8088(int opcode, int modrm)
    {
        var (cpu, _) = Program((byte)opcode, (byte)modrm); cpu.AX = 0x1234; cpu.DS = 0x5678;
        cpu.Step();
        if (opcode == 0x8C) Assert.That(cpu.AX, Is.EqualTo(0x5678));
        else if (modrm == 0xE0) Assert.That(cpu.ES, Is.EqualTo(0x1234));
        else Assert.That(cpu.DS, Is.EqualTo(0x1234));
    }

}