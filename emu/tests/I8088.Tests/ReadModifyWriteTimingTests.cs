using Retro.Cpu.I8088;

namespace I8088.Tests;

/// <summary>
/// Own programs that pin the EU/BIU rules measured on SingleStepTests/8088 v2 traces.
/// Data clocks are compared relative to the first data access, like cpu8088 verificar-bus.
/// </summary>
public sealed class ReadModifyWriteTimingTests
{
    private sealed class Bus : I8088Bus
    {
        public byte[] Memory = new byte[1 << 20];
        public int Writes;
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address, byte value) { Memory[address] = value; Writes++; }
        public byte In(ushort port) => 0xFF;
        public void Out(ushort port, byte value) { }
    }
    private static (I8088Cpu Cpu, Bus Bus, List<I8088BusAccess> Accesses) Program(byte[] code, bool queued)
    {
        var bus = new Bus(); Array.Fill(bus.Memory, (byte)0x90, 0x1000, 64); code.CopyTo(bus.Memory, 0x1000);
        var cpu = new I8088Cpu(bus) { CS = 0x100, DS = 0x200, BX = 0x10, SI = 0x20, TimingEnabled = true };
        if (queued) cpu.InitializePrefetchQueue(bus.Memory.AsSpan(0x1000, 4));
        var accesses = new List<I8088BusAccess>(); cpu.BusAccessed += accesses.Add;
        return (cpu, bus, accesses);
    }
    private static long[] DataClocks(List<I8088BusAccess> accesses)
    {
        var data = accesses.Where(a => a.Kind != I8088BusAccessKind.Code).ToArray();
        return data.Select(a => a.Clock - data[0].Clock).ToArray();
    }

    // Read T3 to write T3 with the instruction already queued: the EU work between the transfers.
    [TestCase(new byte[] { 0x00, 0x00 }, new long[] { 0, 11 }, TestName = "ADD m8,r8")]
    [TestCase(new byte[] { 0x01, 0x00 }, new long[] { 0, 4, 15, 19 }, TestName = "ADD m16,r16")]
    [TestCase(new byte[] { 0x86, 0x00 }, new long[] { 0, 12 }, TestName = "XCHG m8,r8")]
    [TestCase(new byte[] { 0x80, 0x00, 0x05 }, new long[] { 0, 12 }, TestName = "ADD m8,imm8")]
    [TestCase(new byte[] { 0x81, 0x00, 0x34, 0x12 }, new long[] { 0, 4, 16, 20 }, TestName = "ADD m16,imm16")]
    [TestCase(new byte[] { 0xD0, 0x00 }, new long[] { 0, 10 }, TestName = "ROL m8,1")]
    [TestCase(new byte[] { 0xF6, 0x10 }, new long[] { 0, 10 }, TestName = "NOT m8")]
    [TestCase(new byte[] { 0xFE, 0x00 }, new long[] { 0, 10 }, TestName = "INC m8")]
    public void ReadModifyWriteKeepsTheMeasuredWorkBetweenTransfers(byte[] code, long[] expected)
    {
        var (cpu, bus, accesses) = Program(code, queued: true); bus.Memory[0x2030] = 0x41; bus.Memory[0x2031] = 0x82;
        cpu.Step();
        Assert.That(DataClocks(accesses), Is.EqualTo(expected));
        Assert.That(cpu.IP, Is.EqualTo(code.Length));
    }

    [TestCase(0, 15)]
    [TestCase(2, 23)]
    public void ShiftByClWritesBackEvenWithAZeroCount(int count, long gap)
    {
        var (cpu, bus, accesses) = Program([0xD2, 0x00], queued: true); bus.Memory[0x2030] = 0x81;
        cpu.CX = (ushort)(0x1200 | count); cpu.Flags = I8088Cpu.Carry;
        cpu.Step();
        var data = accesses.Where(a => a.Kind != I8088BusAccessKind.Code).ToArray();
        Assert.That(data.Select(a => a.Kind), Is.EqualTo(new[] { I8088BusAccessKind.MemoryRead, I8088BusAccessKind.MemoryWrite }));
        Assert.That(data[1].Value, Is.EqualTo(count == 0 ? 0x81 : 0x06));
        Assert.That(data[1].Clock - data[0].Clock, Is.EqualTo(gap));
        if (count == 0) Assert.That(cpu.Flag(I8088Cpu.Carry), Is.True, "A zero count keeps the flags.");
    }

    [Test]
    public void FunctionalModeAlsoWritesBackAZeroCountShift()
    {
        var bus = new Bus(); new byte[] { 0xD2, 0x00 }.CopyTo(bus.Memory, 0x1000); bus.Memory[0x2030] = 0x81;
        var cpu = new I8088Cpu(bus) { CS = 0x100, DS = 0x200, BX = 0x10, SI = 0x20, CX = 0 };
        cpu.Step();
        Assert.That(bus.Writes, Is.EqualTo(1), "Memory-mapped devices see the same write as on the bus.");
        Assert.That(bus.Memory[0x2030], Is.EqualTo(0x81));
        Assert.That(cpu.Cycles, Is.Zero);
    }

    [Test]
    public void BiuStartsNoPrefetchInTheTwoClocksBeforeAnOperandRequest()
    {
        // MOV AL,[SI]: one prefetch fits in the address work; the next would start two clocks before T1.
        var (cpu, _, accesses) = Program([0x8A, 0x04], queued: true);
        cpu.Step();
        int read = accesses.FindIndex(a => a.Kind == I8088BusAccessKind.MemoryRead);
        Assert.That(accesses.Take(read).Count(a => a.Kind == I8088BusAccessKind.Code), Is.EqualTo(1));
    }

    [Test]
    public void SegmentPrefixTakesTwoClocksBeforeTheOpcode()
    {
        // With DS: decoded first, a second prefetch completes before the same operand request.
        var (cpu, _, accesses) = Program([0x3E, 0x8A, 0x04], queued: true);
        cpu.Step();
        int read = accesses.FindIndex(a => a.Kind == I8088BusAccessKind.MemoryRead);
        Assert.That(accesses.Take(read).Count(a => a.Kind == I8088BusAccessKind.Code), Is.EqualTo(2));
        Assert.That(accesses[read].Address, Is.EqualTo(0x2020));
    }

    [Test]
    public void LateHighDisplacementByteLeavesFourClocksBeforeTheOperand()
    {
        // MOV AL,[SI+1234h] from an empty queue: the EU waits for the high byte, reads it on the
        // clock after its T4, then four clocks precede T1; the operand is sampled at T3.
        byte[] code = [0x8A, 0x84, 0x34, 0x12];
        var (cpu, _, accesses) = Program(code, queued: false);
        cpu.Step();
        var high = accesses.Single(a => a.Kind == I8088BusAccessKind.Code && a.Address == 0x1003);
        var read = accesses.Single(a => a.Kind == I8088BusAccessKind.MemoryRead);
        Assert.That(read.Address, Is.EqualTo(0x3254));
        Assert.That(read.Clock - (high.Clock + 1), Is.EqualTo(9), "Prefetch data is sampled at T3 and queued at T4.");
    }

    [Test]
    public void TimedReadModifyWriteKeepsTheFunctionalResult()
    {
        byte[] code = [0x80, 0x00, 0x7F, 0x86, 0x00, 0xD2, 0x00, 0xFE, 0x00, 0xF4];
        var timed = Program(code, queued: false); var plain = Program(code, queued: false);
        plain.Cpu.TimingEnabled = false;
        foreach (var (cpu, bus, _) in new[] { timed, plain })
        {
            bus.Memory[0x2030] = 0x81; cpu.AX = 0x33; cpu.CX = 3;
            while (!cpu.Halted) cpu.Step();
        }
        Assert.That(timed.Bus.Memory[0x2030], Is.EqualTo(plain.Bus.Memory[0x2030]));
        Assert.That(timed.Cpu.AX, Is.EqualTo(plain.Cpu.AX));
        Assert.That(timed.Cpu.Flags, Is.EqualTo(plain.Cpu.Flags));
    }
}
