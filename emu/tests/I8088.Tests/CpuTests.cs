using Retro.Cpu.I8088;

namespace I8088.Tests;

[TestFixture(I8088Model.Intel8088)]
[TestFixture(I8088Model.Intel8086)]
public sealed partial class CpuTests(I8088Model model)
{
    private sealed class Bus : I8088Bus
    {
        public readonly byte[] Memory = new byte[1 << 20];
        public readonly List<(ushort Port, byte Value)> Output = [];
        public readonly List<uint> Reads = [];
        public bool TraceReads;
        public bool TestSignal { get; set; }
        public readonly List<bool> Locks = [];
        public void SetLock(bool locked) => Locks.Add(locked);
        public byte ReadMemory(uint address) { if (TraceReads) Reads.Add(address); return Memory[address]; }
        public void WriteMemory(uint address, byte value) => Memory[address] = value;
        public byte In(ushort port) => port == 0xFFFF ? (byte)0x12 : (byte)0x34;
        public void Out(ushort port, byte value) => Output.Add((port, value));
    }
    private (I8088Cpu Cpu, Bus Bus) Program(params byte[] code)
    {
        var bus = new Bus(); var cpu = new I8088Cpu(bus, model) { CS = 0x1000, IP = 0, SS = 0x5000, SP = 0xFFFE };
        code.CopyTo(bus.Memory, 0x10000); return (cpu, bus);
    }
    private static void Run(I8088Cpu cpu, int limit = 200)
    {
        for (int i = 0; i < limit && !cpu.Halted; i++) cpu.Step();
        Assert.That(cpu.Halted, Is.True, "Program did not reach HLT");
    }

    [Test]
    public void ResetFetchAndTwentyBitAddressWrap()
    {
        var bus = new Bus(); var cpu = new I8088Cpu(bus, model);
        bus.Memory[0xFFFF0] = 0xEA; bus.Memory[0xFFFF1] = 0x10; bus.Memory[0xFFFF2] = 0;
        bus.Memory[0xFFFF3] = 0xFF; bus.Memory[0xFFFF4] = 0xFF;
        bus.Memory[0] = 0xB8; bus.Memory[1] = 0x34; bus.Memory[2] = 0x12; bus.Memory[3] = 0xF4;
        Run(cpu);
        Assert.That(cpu.AX, Is.EqualTo(0x1234));
        Assert.That(cpu.PhysicalPC, Is.EqualTo(4));
        cpu.RequestNmi(); cpu.Reset();
        Assert.That((cpu.CS, cpu.IP, cpu.Flags, cpu.Halted, cpu.Instructions), Is.EqualTo(((ushort)0xFFFF, (ushort)0, (ushort)0xF002, false, 0L)));
    }

    [Test]
    public void ByteRegisterHalvesAndSignExtension()
    {
        var (cpu, _) = Program(0xB8, 0x34, 0x12, 0xB4, 0xAB, 0xB0, 0x80, 0x98, 0x99, 0xF4);
        Run(cpu);
        Assert.That(cpu.AX, Is.EqualTo(0xFF80)); Assert.That(cpu.DX, Is.EqualTo(0xFFFF));
    }

    [TestCase(0, 0x0030, 0x2000)] // BX+SI
    [TestCase(1, 0x0040, 0x2000)] // BX+DI
    [TestCase(2, 0x0120, 0x3000)] // BP+SI
    [TestCase(3, 0x0130, 0x3000)] // BP+DI
    [TestCase(4, 0x0020, 0x2000)]
    [TestCase(5, 0x0030, 0x2000)]
    [TestCase(6, 0x0100, 0x3000)]
    [TestCase(7, 0x0010, 0x2000)]
    public void AddressFormsDefaultSegmentsAndSignedDisplacement(int rm, int address, int segment)
    {
        var (cpu, _) = Program(0x8B, (byte)(0x40 | rm), 0xFE, 0xF4); // MOV AX,[EA-2]
        cpu.BX = 0x10; cpu.SI = 0x20; cpu.DI = 0x30; cpu.BP = 0x100; cpu.DS = 0x2000; cpu.SS = 0x3000;
        cpu.WriteWord((ushort)segment, (ushort)(address - 2), 0xCAFE);
        Run(cpu); Assert.That(cpu.AX, Is.EqualTo(0xCAFE));
    }

    [Test]
    public void SegmentOverrideDirectAddressAndWordDisplacement()
    {
        var (cpu, _) = Program(0x26, 0x8B, 0x06, 0x20, 0, 0x89, 0x83, 0x00, 0xFF, 0xF4);
        cpu.ES = 0x4000; cpu.DS = 0x2000; cpu.BP = 0x200; cpu.DI = 0x10;
        cpu.WriteWord(cpu.ES, 0x20, 0xBAAD);
        Run(cpu);
        Assert.That(cpu.AX, Is.EqualTo(0xBAAD)); Assert.That(cpu.ReadWord(cpu.SS, 0x110), Is.EqualTo(0xBAAD));
    }

    [Test]
    public void OffsetsWrapWithinSegmentForDataAndInstructionFetch()
    {
        var (cpu, bus) = Program(); cpu.DS = 0x2000;
        cpu.WriteWord(cpu.DS, 0xFFFF, 0xCAFE);
        Assert.That(bus.Memory[0x2FFFF], Is.EqualTo(0xFE)); Assert.That(bus.Memory[0x20000], Is.EqualTo(0xCA));
        cpu.IP = 0xFFFF; bus.Memory[0x1FFFF] = 0xB8; bus.Memory[0x10000] = 0x34; bus.Memory[0x10001] = 0x12;
        cpu.Step(); Assert.That(cpu.AX, Is.EqualTo(0x1234)); Assert.That(cpu.IP, Is.EqualTo(2));
    }

    [TestCase(0)] [TestCase(2)] [TestCase(3)] [TestCase(5)] [TestCase(7)]
    public void ByteArithmeticMatchesIndependentSignedAndUnsignedOracle(int operation)
    {
        var (cpu, bus) = Program((byte)(operation * 8 + 4), 0);
        for (int a = 0; a < 256; a++)
        for (int b = 0; b < 256; b++)
        for (int incoming = 0; incoming < 2; incoming++)
        {
            cpu.IP = 0; cpu.AX = (ushort)a; cpu.Flags = (ushort)incoming; bus.Memory[0x10001] = (byte)b;
            cpu.Step();
            bool sub = operation is 3 or 5 or 7; int carry = operation is 2 or 3 ? incoming : 0;
            int unsigned = sub ? a - b - carry : a + b + carry;
            int signed = sub ? (sbyte)a - (sbyte)b - carry : (sbyte)a + (sbyte)b + carry;
            int lowNibble = sub ? (a & 15) - (b & 15) - carry : (a & 15) + (b & 15) + carry;
            int expected = unsigned & 255;
            ushort expectedFlags = (ushort)(0xF002 | (unsigned is < 0 or > 255 ? 1 : 0)
                | (lowNibble is < 0 or > 15 ? 16 : 0) | (signed is < -128 or > 127 ? 0x800 : 0)
                | (expected == 0 ? 64 : 0) | (expected >= 128 ? 128 : 0)
                | (System.Numerics.BitOperations.PopCount((uint)expected) % 2 == 0 ? 4 : 0));
            if (cpu.AX != (operation == 7 ? a : expected) || cpu.Flags != expectedFlags)
                Assert.Fail($"op={operation} a={a:X2} b={b:X2} carry={incoming}: AX={cpu.AX:X4}, flags={cpu.Flags:X4}, expected={expected:X2}/{expectedFlags:X4}");
        }
    }

    [TestCase(0x7FFF, 1, 0x8000, 0xF896)]
    [TestCase(0xFFFF, 1, 0, 0xF057)]
    [TestCase(0x8000, 0x8000, 0, 0xF847)]
    public void WordAdditionBoundaryFlags(int a, int b, int result, int flags)
    {
        var (cpu, _) = Program(0x05, (byte)b, (byte)(b >> 8)); cpu.AX = (ushort)a;
        cpu.Step(); Assert.That(cpu.AX, Is.EqualTo(result)); Assert.That(cpu.Flags, Is.EqualTo(flags));
    }

    [Test]
    public void SignExtendedImmediateCompareAndIncPreserveCarry()
    {
        var (cpu, _) = Program(0xB8, 0, 0, 0x83, 0xC0, 0xFF, 0x40, 0xF9, 0x48, 0x3D, 0xFF, 0xFF, 0xF4);
        Run(cpu); Assert.That(cpu.AX, Is.EqualTo(0xFFFF)); Assert.That(cpu.Flag(I8088Cpu.Zero), Is.True);
        var (inc, _) = Program(0x40); inc.AX = 0x7FFF; inc.Flags = I8088Cpu.Carry; inc.Step();
        Assert.That(inc.Flag(I8088Cpu.Carry), Is.True); Assert.That(inc.Flag(I8088Cpu.Overflow), Is.True);
    }

    [Test]
    public void LoopAndNearCallProduceSumThenReturn()
    {
        // CALL sum; store result; HLT; sum: CX=10, BX=AX=0; INC BX; ADD AX,BX; LOOP; RET
        var (cpu, _) = Program(0xE8, 4, 0, 0xA3, 0, 2, 0xF4, 0xB9, 10, 0, 0x31, 0xC0, 0x31, 0xDB, 0x43, 0x01, 0xD8, 0xE2, 0xFB, 0xC3);
        ushort before = cpu.SP; Run(cpu);
        Assert.That(cpu.ReadWord(cpu.DS, 0x200), Is.EqualTo(55)); Assert.That(cpu.CX, Is.Zero); Assert.That(cpu.SP, Is.EqualTo(before));
    }

    [Test]
    public void Original8088PushSpAndPopSpStackWrap()
    {
        var (cpu, _) = Program(0x54, 0x5B, 0xBC, 0, 0, 0x50, 0x5C, 0xF4); cpu.SP = 0x100; cpu.AX = 0x2468;
        Run(cpu); Assert.That(cpu.BX, Is.EqualTo(0xFE)); Assert.That(cpu.SP, Is.EqualTo(0x2468));
        Assert.That(cpu.ReadWord(cpu.SS, 0xFFFE), Is.EqualTo(0x2468));
    }

    [Test]
    public void FarCallAndReturnRestoreCodeSegmentAndStack()
    {
        var (cpu, bus) = Program(0x9A, 0x20, 0, 0, 0x30, 0xF4);
        bus.Memory[0x30020] = 0xB8; bus.Memory[0x30021] = 0xEF; bus.Memory[0x30022] = 0xBE; bus.Memory[0x30023] = 0xCB;
        ushort sp = cpu.SP; Run(cpu);
        Assert.That(cpu.AX, Is.EqualTo(0xBEEF)); Assert.That(cpu.CS, Is.EqualTo(0x1000)); Assert.That(cpu.SP, Is.EqualTo(sp));
    }

    [Test]
    public void SoftwareInterruptFrameAndIretRestoreFlags()
    {
        var (cpu, bus) = Program(0xCD, 0x21, 0xF4); cpu.Flags = I8088Cpu.InterruptEnable | I8088Cpu.Direction | I8088Cpu.Carry;
        cpu.WriteWord(0, 0x84, 0x10); cpu.WriteWord(0, 0x86, 0x3000); bus.Memory[0x30010] = 0xCF;
        ushort flags = cpu.Flags, sp = cpu.SP; cpu.Step();
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(2));
        Assert.That(cpu.ReadWord(cpu.SS, (ushort)(cpu.SP + 2)), Is.EqualTo(0x1000));
        Assert.That(cpu.ReadWord(cpu.SS, (ushort)(cpu.SP + 4)), Is.EqualTo(flags));
        Assert.That(cpu.Flag(I8088Cpu.InterruptEnable), Is.False);
        cpu.Step(); Assert.That(cpu.Flags, Is.EqualTo(flags)); Assert.That(cpu.SP, Is.EqualTo(sp)); Run(cpu);
    }

    [Test]
    public void PendingIrqWaitsThroughStiNextInstructionAndWakesHalt()
    {
        var (cpu, bus) = Program(0xFB, 0xF4, 0x90); cpu.WriteWord(0, 0x20, 0); cpu.WriteWord(0, 0x22, 0x3000); bus.Memory[0x30000] = 0xCF;
        cpu.RequestInterrupt(8); cpu.Step(); Assert.That(cpu.IP, Is.EqualTo(1));
        cpu.Step(); Assert.That(cpu.Halted, Is.True); Assert.That(cpu.IP, Is.EqualTo(2));
        cpu.Step(); Assert.That(cpu.Halted, Is.False); Assert.That(cpu.CS, Is.EqualTo(0x3000));
        cpu.Step(); Assert.That(cpu.CS, Is.EqualTo(0x1000)); Assert.That(cpu.IP, Is.EqualTo(2));
    }

    [Test]
    public void MovSsProtectsFollowingStackPointerChangeEvenFromNmi()
    {
        var (cpu, _) = Program(0x8E, 0xD0, 0xBC, 0x00, 0x80, 0x90); cpu.AX = 0x2000;
        cpu.WriteWord(0, 8, 0x20); cpu.WriteWord(0, 10, 0x3000);
        cpu.Step(); cpu.RequestNmi(); cpu.Step(); Assert.That(cpu.SP, Is.EqualTo(0x8000)); Assert.That(cpu.CS, Is.EqualTo(0x1000));
        cpu.Step(); Assert.That(cpu.CS, Is.EqualTo(0x3000)); Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(5));
    }

    [Test]
    public void RepMovswCanBeInterruptedAndResumedAtItsPrefix()
    {
        var (cpu, bus) = Program(0xF3, 0xA5, 0xF4); cpu.DS = 0x2000; cpu.ES = 0x3000; cpu.SI = 0x100; cpu.DI = 0x200; cpu.CX = 3;
        cpu.WriteWord(cpu.DS, 0x100, 0x1234); cpu.WriteWord(cpu.DS, 0x102, 0x5678); cpu.WriteWord(cpu.DS, 0x104, 0x9ABC);
        cpu.WriteWord(0, 8, 0); cpu.WriteWord(0, 10, 0x4000); bus.Memory[0x40000] = 0xCF;
        cpu.Step(); Assert.That(cpu.IP, Is.Zero); Assert.That(cpu.CX, Is.EqualTo(2));
        cpu.RequestNmi(); cpu.Step(); Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.Zero); cpu.Step(); Run(cpu);
        Assert.That(cpu.ReadWord(cpu.ES, 0x200), Is.EqualTo(0x1234)); Assert.That(cpu.ReadWord(cpu.ES, 0x204), Is.EqualTo(0x9ABC));
        Assert.That(cpu.SI, Is.EqualTo(0x106)); Assert.That(cpu.DI, Is.EqualTo(0x206)); Assert.That(cpu.CX, Is.Zero);
    }

    [Test]
    public void RepeatZeroSkipsMemoryAndRepeCmpsStopsAtFirstDifference()
    {
        var (cpu, bus) = Program(0xF3, 0xA6, 0xF4); cpu.CX = 0; cpu.DS = 0x2000; cpu.ES = 0x3000;
        bus.TraceReads = true; cpu.Step(); Assert.That(bus.Reads, Is.EqualTo(new uint[] { 0x10000, 0x10001 }));
        cpu.IP = 0; cpu.CX = 4; cpu.WriteWord(cpu.DS, 0, 0x2211); cpu.WriteWord(cpu.ES, 0, 0x3311);
        Run(cpu); Assert.That(cpu.CX, Is.EqualTo(2)); Assert.That(cpu.SI, Is.EqualTo(2)); Assert.That(cpu.Flag(I8088Cpu.Zero), Is.False);
    }

    [Test]
    public void BackwardStringUsesOverriddenSourceAndFixedEsDestination()
    {
        var (cpu, _) = Program(0x36, 0xF3, 0xA4, 0xF4); cpu.SS = 0x2000; cpu.DS = 0x4000; cpu.ES = 0x3000;
        cpu.SI = 2; cpu.DI = 4; cpu.CX = 3; cpu.Flags = I8088Cpu.Direction;
        cpu.WriteByte(cpu.SS, 2, 11); cpu.WriteByte(cpu.SS, 1, 22); cpu.WriteByte(cpu.SS, 0, 33);
        Run(cpu); Assert.That(cpu.ReadByte(cpu.ES, 2), Is.EqualTo(33)); Assert.That(cpu.ReadByte(cpu.ES, 4), Is.EqualTo(11));
        Assert.That(cpu.SI, Is.EqualTo(0xFFFF)); Assert.That(cpu.DI, Is.EqualTo(1));
    }

    [Test]
    public void WordIoUsesTwoByteTransactionsAndWrapsPorts()
    {
        var (cpu, bus) = Program(0xBA, 0xFF, 0xFF, 0xED, 0xEF, 0xF4);
        Run(cpu); Assert.That(cpu.AX, Is.EqualTo(0x3412));
        Assert.That(bus.Output, Is.EqualTo(new (ushort, byte)[] { (0xFFFF, 0x12), (0, 0x34) }));
    }

    [Test]
    public void ConditionalBranchesAgreeWithSignedAndUnsignedComparisons()
    {
        var (cpu, bus) = Program(0x3C, 0, 0x70, 2); // CMP AL,imm; Jcc +2
        int[] values = [0, 1, 2, 0x7F, 0x80, 0x81, 0xFE, 0xFF];
        foreach (int a in values)
        foreach (int b in values)
        {
            int result = (a - b) & 255;
            bool parity = System.Numerics.BitOperations.PopCount((uint)result) % 2 == 0;
            bool overflow = (sbyte)a - (sbyte)b is < -128 or > 127;
            bool[] expected = [overflow, !overflow, a < b, a >= b, a == b, a != b, a <= b, a > b,
                result >= 128, result < 128, parity, !parity, (sbyte)a < (sbyte)b, (sbyte)a >= (sbyte)b, (sbyte)a <= (sbyte)b, (sbyte)a > (sbyte)b];
            for (int condition = 0; condition < 16; condition++)
            {
                cpu.IP = 0; cpu.AX = (ushort)a; bus.Memory[0x10001] = (byte)b; bus.Memory[0x10002] = (byte)(0x70 + condition);
                cpu.Step(); cpu.Step();
                Assert.That(cpu.IP, Is.EqualTo(expected[condition] ? 6 : 4), $"a={a}, b={b}, condition={condition}");
            }
        }
    }

    [TestCase(0x0C, 0xA5, 0x3C, 0xBD)] // OR AL
    [TestCase(0x24, 0xA5, 0x3C, 0x24)] // AND AL
    [TestCase(0x34, 0xA5, 0x3C, 0x99)] // XOR AL
    public void LogicClearsCarryAndOverflowAndSetsResultFlags(int opcode, int a, int b, int result)
    {
        var (cpu, _) = Program((byte)opcode, (byte)b); cpu.AX = (ushort)a; cpu.Flags = I8088Cpu.Carry | I8088Cpu.Overflow;
        cpu.Step(); Assert.That(cpu.AX, Is.EqualTo(result));
        Assert.That(cpu.Flag(I8088Cpu.Carry), Is.False); Assert.That(cpu.Flag(I8088Cpu.Overflow), Is.False);
        Assert.That(cpu.Flag(I8088Cpu.Sign), Is.EqualTo(result >= 128));
    }

    [Test]
    public void IndirectFarCallReadsPointerBeforePushingReturnAddress()
    {
        var (cpu, bus) = Program(0xFF, 0x1E, 0, 2, 0xF4); // CALL FAR [0200]
        cpu.WriteWord(cpu.DS, 0x200, 0x20); cpu.WriteWord(cpu.DS, 0x202, 0x3000);
        bus.Memory[0x30020] = 0xCB;
        cpu.Step(); Assert.That(cpu.CS, Is.EqualTo(0x3000)); Assert.That(cpu.IP, Is.EqualTo(0x20));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(4));
        cpu.Step(); Assert.That(cpu.CS, Is.EqualTo(0x1000)); Run(cpu);
    }

    [Test]
    public void LeaDoesNotReadMemoryAndLesLoadsBothHalvesOfPointer()
    {
        var (cpu, bus) = Program(0x8D, 0x42, 0xF0, 0xC4, 0x1E, 0x40, 0, 0xF4);
        cpu.BP = 0x100; cpu.SI = 0x20; cpu.WriteWord(cpu.DS, 0x40, 0xCAFE); cpu.WriteWord(cpu.DS, 0x42, 0x3000);
        bus.TraceReads = true; cpu.Step();
        Assert.That(cpu.AX, Is.EqualTo(0x110)); Assert.That(bus.Reads, Has.Count.EqualTo(3));
        Run(cpu); Assert.That(cpu.BX, Is.EqualTo(0xCAFE)); Assert.That(cpu.ES, Is.EqualTo(0x3000));
    }

    [Test]
    public void RepneScasAndLodsStosUseAccumulatorAndDirection()
    {
        var (cpu, _) = Program(0xF2, 0xAE, 0xAC, 0xAA, 0xF4); cpu.DS = 0x2000; cpu.ES = 0x3000;
        cpu.AX = 22; cpu.CX = 4; cpu.SI = 0x100; cpu.DI = 0x200;
        cpu.WriteWord(cpu.ES, 0x200, 0x160B); cpu.WriteByte(cpu.DS, 0x100, 0xA5);
        Run(cpu); Assert.That(cpu.CX, Is.EqualTo(2)); Assert.That(cpu.AX, Is.EqualTo(0xA5));
        Assert.That(cpu.ReadByte(cpu.ES, 0x202), Is.EqualTo(0xA5)); Assert.That(cpu.DI, Is.EqualTo(0x203));
    }

    [Test]
    public void FlagTransfersPreserveControlFlagsAndReservedBits()
    {
        var (cpu, _) = Program(0x9E, 0x9F, 0x9C, 0xF8, 0x9D, 0xF4);
        cpu.AX = 0xFF42; cpu.Flags = I8088Cpu.InterruptEnable | I8088Cpu.Direction | I8088Cpu.Overflow;
        Run(cpu); Assert.That(cpu.AX, Is.EqualTo(0xD742)); Assert.That(cpu.Flags, Is.EqualTo(0xFED7));
    }

    [TestCase(0xC0, 0xE0)] [TestCase(0xC1, 0xE0)] [TestCase(0xD1, 0xF0)]
    public void UnsupportedInstructionFailsWithLocationInsteadOfRunningAsNop(int opcode, int operand)
    {
        var (cpu, _) = Program((byte)opcode, (byte)operand);
        var error = Assert.Throws<NotSupportedException>(() => cpu.Step());
        Assert.That(error!.Message, Does.Contain($"{opcode:X2}").And.Contain("1000:0000")); Assert.That(cpu.Instructions, Is.Zero);
    }
}
