using Retro.Cpu.I8088;

namespace I8088.Tests;

// Own programs. Relative data intervals are measured on SingleStepTests/8086 e71c68d.
public sealed class WordIntervalTimingTests
{
    private sealed class Bus : I8088Bus
    {
        public byte[] Memory = new byte[1 << 20];
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address, byte value) => Memory[address] = value;
        public byte In(ushort port) => 0xFF;
        public void Out(ushort port, byte value) { }
    }
    private static (I8088Cpu Cpu, Bus Bus, List<I8088BusAccess> Data) Program(byte[] code, int ip, bool oddStack = false)
    {
        var bus = new Bus(); Array.Fill(bus.Memory, (byte)0x55);
        Array.Fill(bus.Memory, (byte)0x90, 0x1000, 64); code.CopyTo(bus.Memory, 0x1000 + ip);
        var cpu = new I8088Cpu(bus, I8088Model.Intel8086)
        { CS = 0x100, DS = 0x200, ES = 0x300, SS = 0x400, IP = (ushort)ip,
            SP = (ushort)(oddStack ? 0x101 : 0x100), BX = 0x10, BP = 0x10, SI = 0x20, DI = 0x20,
            AX = 0x5555, CX = 4, TimingEnabled = true };
        cpu.InitializePrefetchQueue(bus.Memory.AsSpan(0x1000 + ip, ip == 0 ? 6 : 5));
        var data = new List<I8088BusAccess>();
        cpu.BusAccessed += a => { if (a.Kind != I8088BusAccessKind.Code) data.Add(a); };
        return (cpu, bus, data);
    }
    private static byte[] Code(int opcode, int modRm, bool prefix, params byte[] immediate)
    {
        var code = new List<byte>(); if (prefix) code.Add(0x3E);
        code.Add((byte)opcode); code.Add((byte)modRm);
        if ((modRm & 0xC0) == 0x40) code.Add(0);
        else if ((modRm & 0xC0) == 0x80) { code.Add(0); code.Add(0); }
        else if ((modRm & 0xC7) == 6) { code.Add(0x20); code.Add(0); }
        code.AddRange(immediate); return code.ToArray();
    }
    private static long[] Gaps(List<I8088BusAccess> data) => data.Zip(data.Skip(1), (a, b) => b.Clock - a.Clock).ToArray();
    private static void OddOperand(I8088Cpu cpu, int address)
    {
        switch (address & 7)
        {
            case 0: case 1: case 7: cpu.BX++; break;
            case 2: case 3: cpu.BP++; break;
            case 4: cpu.SI++; break;
            case 5: cpu.DI++; break;
            case 6: if (address != 6) cpu.BP++; break;
        }
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
    public void ImmediateWordReadModifyWriteDependsOnTheInstructionWordBoundary(int group)
    {
        foreach (int ip in new[] { 0, 1 })
        foreach (int address in new[] { 0, 4, 6, 0x40, 0x44, 0x80, 0x84 })
        foreach (bool prefix in new[] { false, true })
        foreach (bool odd in new[] { false, true })
        {
            byte[] code = Code(0x81, address | group << 3, prefix, 1, 0);
            if (odd && address == 6) code[prefix ? 3 : 2]++;
            var (cpu, _, data) = Program(code, ip); if (odd) OddOperand(cpu, address);
            cpu.Step();
            Assert.That(data.Count, Is.EqualTo(4));
            int immediate = ip + code.Length - 2;
            Assert.That(data[2].Clock - data[1].Clock, Is.EqualTo((immediate & 1) == 0 ? 13 : 12),
                $"group={group}, address={address:X2}, ip={ip}, prefix={prefix}, odd={odd}");
            Assert.That(data[2].Address, Is.EqualTo(data[0].Address));
            Assert.That(data[3].Address, Is.EqualTo(data[1].Address));
            Assert.That(data[0].Address % 2, Is.EqualTo(odd ? 1 : 0));
        }
    }
    [Test]
    public void ImmediateCompareDoesNotAddAWriteTransfer()
    {
        var (cpu, _, data) = Program([0x81, 0x3C, 0x55, 0x55], 0); cpu.Step();
        Assert.That(data.Select(a => a.Kind), Is.EqualTo(new[] { I8088BusAccessKind.MemoryRead, I8088BusAccessKind.MemoryRead }));
        Assert.That(cpu.Flag(I8088Cpu.Zero), Is.True);
    }

    [TestCase(0xCC)] [TestCase(0xCD)] [TestCase(0xCE)] [TestCase(0xD4)]
    public void SoftwareInterruptsKeepVectorAndFrameIntervalsOnBothStackAlignments(int opcode)
    {
        foreach (int ip in new[] { 0, 1 })
        foreach (bool oddStack in new[] { false, true })
        {
            byte[] code = opcode == 0xCD ? [0xCD, 0x21] : opcode == 0xD4 ? [0xD4, 0] : [(byte)opcode];
            var (cpu, bus, data) = Program(code, ip, oddStack);
            int vector = opcode == 0xCC ? 3 : opcode == 0xCD ? 0x21 : opcode == 0xCE ? 4 : 0;
            new byte[] { 0x80, 0, 0, 1 }.CopyTo(bus.Memory, vector * 4);
            cpu.Flags = I8088Cpu.Overflow | I8088Cpu.InterruptEnable;
            cpu.Step();
            Assert.That(Gaps(data), Is.EqualTo(new long[] { 0, 7, 0, 8, oddStack ? 4 : 0, 10, oddStack ? 4 : 0, 13, oddStack ? 4 : 0 }));
            Assert.That((cpu.CS, cpu.IP), Is.EqualTo(((ushort)0x100, (ushort)0x80)));
            Assert.That(cpu.Flags & I8088Cpu.InterruptEnable, Is.Zero);
            Assert.That(bus.Memory[I8088Cpu.Physical(cpu.SS, cpu.SP)], Is.EqualTo(ip + code.Length));
        }
    }

    [TestCase(false, false, 0x100, 0, 0, 22)]
    [TestCase(true, false, 0, 1, 0, 22)]
    [TestCase(false, false, 0x100, 0, 1, 22)]
    [TestCase(true, false, 0, 1, 1, 22)]
    [TestCase(false, true, 0x100, 0, 0, 32)]
    [TestCase(false, true, 0xFF00, 0, 0, 36)]
    [TestCase(true, true, 0, 1, 0, 32)]
    [TestCase(false, true, 0x80, 0, 1, 104)]
    [TestCase(false, true, 0xFF80, 0, 0xFF, 107)]
    [TestCase(true, true, 0x8000, 0, 1, 168)]
    public void DivideErrorsKeepTheirOperandToVectorGap(bool word, bool signed, int ax, int dx, int divisor, int gap)
    {
        foreach (int ip in new[] { 0, 1 })
        foreach (bool oddStack in new[] { false, true })
        foreach (bool oddOperand in new[] { false, true })
        {
            var (cpu, bus, data) = Program([(byte)(word ? 0xF7 : 0xF6), (byte)(signed ? 0x3C : 0x34)], ip, oddStack);
            if (oddOperand) cpu.SI++;
            uint operand = I8088Cpu.Physical(cpu.DS, cpu.SI);
            bus.Memory[operand] = (byte)divisor; bus.Memory[operand + 1] = (byte)(divisor >> 8);
            new byte[] { 0x80, 0, 0, 1 }.CopyTo(bus.Memory, 0);
            cpu.AX = (ushort)ax; cpu.DX = (ushort)dx; cpu.Step();
            int lastOperand = word ? 1 : 0;
            Assert.That(data[lastOperand + 1].Clock - data[lastOperand].Clock, Is.EqualTo(gap));
            Assert.That(Gaps(data).Skip(lastOperand + 1), Is.EqualTo(new long[] { 0, 7, 0, 8, oddStack ? 4 : 0, 10, oddStack ? 4 : 0, 13, oddStack ? 4 : 0 }));
            Assert.That((cpu.AX, cpu.DX, cpu.CS, cpu.IP), Is.EqualTo(((ushort)ax, (ushort)dx, (ushort)0x100, (ushort)0x80)));
        }
    }

    [TestCase(2)] [TestCase(3)] [TestCase(5)]
    public void IndirectPointerIntervalsCoverAddressPathsPrefixesAndBothAlignments(int group)
    {
        // Measured near-call gaps, columns: even/odd IP, without/with a segment prefix.
        (int Address, int[] NearGaps)[] paths =
        [
            (0, [16,16,16,16]), (1, [16,16,16,16]), (2, [16,16,16,16]), (3, [16,16,16,16]),
            (4, [16,16,16,16]), (6, [18,18,18,18]),
            (0x40, [16,16,16,16]), (0x41, [16,16,16,16]), (0x44, [16,18,18,16]),
            (0x80, [16,16,16,18]), (0x81, [16,16,16,16]), (0x83, [16,16,16,18]), (0x84, [18,18,18,16])
        ];
        foreach (int ip in new[] { 0, 1 })
        foreach (var (address, nearGaps) in paths)
        foreach (bool prefix in new[] { false, true })
        foreach (bool oddStack in new[] { false, true })
        foreach (bool oddOperand in new[] { false, true })
        {
            byte[] code = Code(0xFF, address | group << 3, prefix);
            if (oddOperand && address == 6) code[prefix ? 3 : 2]++;
            var (cpu, _, data) = Program(code, ip, oddStack);
            if (oddOperand) OddOperand(cpu, address);
            cpu.Step();
            int nearGap = nearGaps[ip * 2 + (prefix ? 1 : 0)];
            Assert.That(data.Count, Is.EqualTo(group == 2 || group == 5 ? 4 : 8));
            int lastRead = group == 2 ? 1 : 3;
            long split = data[1].Address % 2 == 0 ? 4 : 0;
            Assert.That(data[1].Clock - data[0].Clock, Is.EqualTo(split));
            Assert.That(data[0].Address % 2, Is.EqualTo(oddOperand ? 1 : 0));
            if (group == 2) Assert.That(data[2].Clock - data[1].Clock, Is.EqualTo(nearGap));
            else Assert.That(data[2].Clock - data[1].Clock, Is.EqualTo((group == 3 ? 9 : 10) + (nearGap == 18 ? 1 : 0)));
            if (group == 3)
            {
                Assert.That(data[4].Clock - data[3].Clock, Is.EqualTo(10));
                Assert.That(data[6].Clock - data[5].Clock, Is.EqualTo(13));
            }
            Assert.That(cpu.IP, Is.EqualTo(0x5555));
            Assert.That(cpu.CS, Is.EqualTo(group == 2 ? 0x100 : 0x5555));
            if (group != 5) Assert.That(data[lastRead + 1].Kind, Is.EqualTo(I8088BusAccessKind.MemoryWrite));
        }
    }

    [TestCase(false)] [TestCase(true)]
    public void PopCompletesPrefetchBeforeReadingItsStack(bool oddStack)
    {
        foreach (int ip in new[] { 0, 1 })
        foreach (bool prefix in new[] { false, true })
        {
            var (cpu, _, data) = Program(Code(0x8F, 6, prefix), ip, oddStack); cpu.Step();
            Assert.That(data.Count, Is.EqualTo(4));
            Assert.That(data[2].Clock - data[1].Clock, Is.EqualTo(9));
            Assert.That(cpu.SP, Is.EqualTo(oddStack ? 0x103 : 0x102));
        }
    }
}
