using Retro.Cpu.I8088;

namespace I8088.Tests;

public sealed partial class CpuTests
{
    private static void DivideVector(I8088Cpu cpu, Bus bus)
    {
        cpu.WriteWord(0, 0, 0x20); cpu.WriteWord(0, 2, 0x3000); bus.Memory[0x30020] = 0xCF;
    }

    [TestCase(false)] [TestCase(true)]
    public void ByteMultiplyMatchesEveryOperandPair(bool signed)
    {
        var (cpu, _) = Program(0xF6, signed ? (byte)0xEB : (byte)0xE3);
        for (int a = 0; a <= 255; a++)
        for (int b = 0; b <= 255; b++)
        {
            cpu.IP = 0; cpu.AX = (ushort)(0xA500 | a); cpu.BX = (ushort)b; cpu.DX = 0xCAFE;
            cpu.Flags = 0xED5;
            int expected = signed ? (sbyte)a * (sbyte)b : a * b;
            bool overflow = signed ? expected < -128 || expected > 127 : expected > 255;
            cpu.Step();
            if (cpu.AX != unchecked((ushort)expected) || cpu.DX != 0xCAFE || cpu.Flag(I8088Cpu.Carry) != overflow || cpu.Flag(I8088Cpu.Overflow) != overflow)
                Assert.Fail($"signed={signed}, a={a:X2}, b={b:X2}: AX={cpu.AX:X4}, flags={cpu.Flags:X4}");
            Assert.That(cpu.Flags & 0x600, Is.EqualTo(0x600));
        }
    }

    [TestCase(false)] [TestCase(true)]
    public void WordMultiplyBoundariesAndSelfOperand(bool signed)
    {
        foreach (int a in new[] { 0, 1, 2, 0x7FFF, 0x8000, 0xFFFF })
        foreach (int b in new[] { 0, 1, 2, 0x7FFF, 0x8000, 0xFFFF })
        {
            var (cpu, _) = Program(0xF7, signed ? (byte)0xEB : (byte)0xE3);
            cpu.AX = (ushort)a; cpu.BX = (ushort)b; cpu.DX = 0xA55A;
            long product = signed ? (long)(short)a * (short)b : (long)a * b;
            bool overflow = signed ? product < -32768 || product > 32767 : product > 65535;
            cpu.Step();
            Assert.That(((uint)cpu.DX << 16) | cpu.AX, Is.EqualTo(unchecked((uint)product)));
            Assert.That(cpu.Flag(I8088Cpu.Carry), Is.EqualTo(overflow)); Assert.That(cpu.Flag(I8088Cpu.Overflow), Is.EqualTo(overflow));
        }
        var (self, _) = Program(0xF7, signed ? (byte)0xE8 : (byte)0xE0); self.AX = 0x8000; self.Step();
        Assert.That(((uint)self.DX << 16) | self.AX, Is.EqualTo(0x40000000u));
    }

    [TestCase(0xE4, 0xFE03, 0x02FA)] // MUL AH, accumulator is AL, source read before AX changes
    [TestCase(0xEC, 0xFE03, 0xFFFA)] // IMUL AH
    [TestCase(0xE0, 0xFF10, 0x0100)] // MUL AL
    [TestCase(0xE8, 0xFF80, 0x4000)] // IMUL AL
    public void ByteMultiplyReadsOverlappingRegisterBeforeWriting(int modrm, int ax, int expected)
    {
        var (cpu, _) = Program(0xF6, (byte)modrm); cpu.AX = (ushort)ax; cpu.Step();
        Assert.That(cpu.AX, Is.EqualTo(expected));
    }

    [TestCase(false)] [TestCase(true)]
    public void ByteDivisionCoversEveryDividendAndBoundaryDivisors(bool signed)
    {
        var (cpu, bus) = Program(0xF6, signed ? (byte)0xFB : (byte)0xF3); DivideVector(cpu, bus);
        foreach (int b in new[] { 0, 1, 2, 7, 127, 128, 255 })
        for (int bits = 0; bits <= 65535; bits++)
        {
            cpu.CS = 0x1000; cpu.IP = 0; cpu.SP = 0xFFFE; cpu.AX = (ushort)bits; cpu.BX = (ushort)b; cpu.DX = 0xCAFE; cpu.Flags = 0;
            int dividend = signed ? (short)bits : bits, divisor = signed ? (sbyte)b : b;
            long quotient = divisor == 0 ? 0 : (long)dividend / divisor;
            bool fault = divisor == 0 || quotient < (signed ? -127 : 0) || quotient > (signed ? 127 : 255);
            cpu.Step();
            if (fault)
            {
                if (cpu.CS != 0x3000 || cpu.SP != 0xFFF8) Assert.Fail($"Missing type 0: signed={signed}, AX={bits:X4}, BL={b:X2}");
            }
            else
            {
                int remainder = dividend - (int)quotient * divisor;
                int expected = (remainder & 255) * 256 + ((int)quotient & 255);
                if (cpu.CS != 0x1000 || cpu.AX != expected || cpu.DX != 0xCAFE) Assert.Fail($"signed={signed}, AX={bits:X4}, BL={b:X2}: AX={cpu.AX:X4}, expected={expected:X4}");
            }
        }
    }

    [TestCase(false, 0xFFFFFFFFL, 0xFFFF, 0, 0, true)]
    [TestCase(false, 0xFFFFFFFEL, 0xFFFF, 0, 0, true)]
    [TestCase(false, 0xFFFE0001L, 0xFFFF, 0xFFFF, 0, false)]
    [TestCase(false, 0x12345678L, 0x4321, 17772, 8076, false)]
    [TestCase(true, -101L, 7, -14, -3, false)]
    [TestCase(true, 101L, -7, -14, 3, false)]
    [TestCase(true, -101L, -7, 14, -3, false)]
    [TestCase(true, -32767L, 1, -32767, 0, false)]
    [TestCase(true, -32768L, 1, 0, 0, true)]
    [TestCase(true, -2147483648L, -1, 0, 0, true)]
    [TestCase(true, 32768L, 1, 0, 0, true)]
    [TestCase(false, 123L, 0, 0, 0, true)]
    public void WordDivisionChecksWideDividendAndSigned8088Limits(bool signed, long dividend, int divisor, int quotient, int remainder, bool fault)
    {
        var (cpu, bus) = Program(0xF7, signed ? (byte)0xFB : (byte)0xF3); DivideVector(cpu, bus);
        uint bits = unchecked((uint)dividend); cpu.AX = (ushort)bits; cpu.DX = (ushort)(bits >> 16); cpu.BX = unchecked((ushort)divisor);
        cpu.Step();
        Assert.That(cpu.CS, Is.EqualTo(fault ? 0x3000 : 0x1000));
        if (!fault) { Assert.That(cpu.AX, Is.EqualTo(unchecked((ushort)quotient))); Assert.That(cpu.DX, Is.EqualTo(unchecked((ushort)remainder))); }
    }

    [TestCase(0xF4, 0x030A, 0)] // DIV AH: 778 / 3 = 259, overflow
    [TestCase(0xF0, 0x0010, 0x0001)] // DIV AL
    [TestCase(0xFC, 0xFE00, 0x0000)] // IDIV AH: -512 / -2 = 256, overflow
    public void DivisionWithAccumulatorSourceDoesNotReadAfterWriting(int modrm, int initial, int expected)
    {
        var (cpu, bus) = Program(0xF6, (byte)modrm); DivideVector(cpu, bus); cpu.AX = (ushort)initial; cpu.Step();
        if (modrm == 0xF0) Assert.That(cpu.AX, Is.EqualTo(expected)); else Assert.That(cpu.CS, Is.EqualTo(0x3000));
    }

    [TestCase(false)] [TestCase(true)]
    public void DivideErrorConsumesOperandAndIretReturnsToFollowingInstruction(bool signed)
    {
        var (cpu, bus) = Program(0x26, 0xF7, signed ? (byte)0x3E : (byte)0x36, 0xFF, 0xFF, 0xF4);
        DivideVector(cpu, bus); cpu.ES = 0x4000; cpu.AX = 0x1234; cpu.DX = 0x5678; cpu.SP = 1; cpu.Flags = 0xED5;
        cpu.WriteWord(cpu.ES, 0xFFFF, 0); ushort flags = cpu.Flags;
        bus.TraceReads = true; cpu.Step();
        Assert.That(bus.Reads.Take(7), Is.EqualTo(new uint[] { 0x10000, 0x10001, 0x10002, 0x10003, 0x10004, 0x4FFFF, 0x40000 }));
        Assert.That(cpu.SP, Is.EqualTo(0xFFFB));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(5));
        Assert.That(cpu.ReadWord(cpu.SS, 0xFFFD), Is.EqualTo(0x1000)); Assert.That(cpu.ReadWord(cpu.SS, 0xFFFF), Is.EqualTo(flags));
        Assert.That(cpu.Flag(I8088Cpu.InterruptEnable), Is.False);
        cpu.Step(); Assert.That((cpu.CS, cpu.IP, cpu.SP, cpu.Flags), Is.EqualTo(((ushort)0x1000, (ushort)5, (ushort)1, flags)));
        Run(cpu); Assert.That(cpu.Instructions, Is.EqualTo(3));
    }

    [Test]
    public void SuccessfulWordMemoryMultiplyUsesSegmentOverrideAndDoesNotWriteSource()
    {
        var (cpu, bus) = Program(0x26, 0xF7, 0x26, 0xFF, 0xFF); cpu.ES = 0x4000; cpu.AX = 0xFFFF;
        cpu.WriteWord(cpu.ES, 0xFFFF, 2); cpu.Step();
        Assert.That(cpu.DX, Is.EqualTo(1)); Assert.That(cpu.AX, Is.EqualTo(0xFFFE));
        Assert.That(bus.Memory[0x4FFFF], Is.EqualTo(2)); Assert.That(bus.Memory[0x40000], Is.Zero);
    }
    [TestCase(0xF3, 0xF6, 0xE3)]
    public void UndocumentedRepeatMultiplyDivideIsExplicitlyRejected(int prefix, int opcode, int modrm)
    {
        var (cpu, _) = Program((byte)prefix, (byte)opcode, (byte)modrm);
        Assert.Throws<NotSupportedException>(() => cpu.Step());
    }
    [TestCase(0xF2, false)] [TestCase(0xF3, false)] [TestCase(0xF2, true)] [TestCase(0xF3, true)]
    public void RepOnIdivNegatesOnlyTheQuotient(int prefix, bool word)
    {
        var (cpu, _) = Program((byte)prefix, word ? (byte)0xF7 : (byte)0xF6, 0xFB);
        cpu.AX = 17; cpu.DX = 0; cpu.BX = 5; cpu.Step();
        if (word) { Assert.That(cpu.AX, Is.EqualTo(0xFFFD)); Assert.That(cpu.DX, Is.EqualTo(2)); }
        else Assert.That(cpu.AX, Is.EqualTo(0x02FD));
    }

}
