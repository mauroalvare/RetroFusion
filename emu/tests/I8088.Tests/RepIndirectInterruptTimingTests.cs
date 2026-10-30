using Retro.Cpu.I8088;

namespace I8088.Tests;

// Own programs; expected adjacent data intervals were checked on SingleStepTests/8088 v2.
public sealed class RepIndirectInterruptTimingTests
{
    private sealed class Bus : I8088Bus
    {
        public byte[] Memory = new byte[1 << 20];
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address, byte value) => Memory[address] = value;
        public byte In(ushort port) => 0xFF;
        public void Out(ushort port, byte value) { }
    }
    private static (I8088Cpu Cpu, Bus Bus, List<I8088BusAccess> Data) Program(byte[] code,
        bool queued = true, I8088Model model = I8088Model.Intel8088)
    {
        var bus = new Bus(); Array.Fill(bus.Memory, (byte)0x90, 0x1000, 64); code.CopyTo(bus.Memory, 0x1000);
        Array.Fill(bus.Memory, (byte)0x55, 0x2000, 128); Array.Fill(bus.Memory, (byte)0x55, 0x3000, 128);
        new byte[] { 0x80, 0, 0, 1 }.CopyTo(bus.Memory, 0);
        var cpu = new I8088Cpu(bus, model) { CS = 0x100, DS = 0x200, ES = 0x300, SS = 0x400,
            SP = 0x100, AX = 0x5555, CX = 4, BX = 0x10, SI = 0x20, DI = 0x40, TimingEnabled = true };
        if (queued) cpu.InitializePrefetchQueue(bus.Memory.AsSpan(0x1000, model == I8088Model.Intel8086 ? 6 : 4));
        var data = new List<I8088BusAccess>();
        cpu.BusAccessed += a => { if (a.Kind != I8088BusAccessKind.Code) data.Add(a); };
        return (cpu, bus, data);
    }
    private static byte[] Save(I8088Cpu cpu)
    {
        using var stream = new MemoryStream(); cpu.Save(new BinaryWriter(stream)); return stream.ToArray();
    }

    [TestCase(0xA4, 0xF2)]
    [TestCase(0xA5, 0xF2)]
    [TestCase(0xA4, 0xF3)]
    [TestCase(0xA5, 0xF3)]
    public void RepMovsKeepsItsFirstAndSubsequentReadWriteGaps(int opcode, int prefix)
    {
        foreach (bool queued in new[] { false, true })
        foreach (bool segment in new[] { false, true })
        {
            byte[] code = segment ? [0x3E, (byte)prefix, (byte)opcode] : [(byte)prefix, (byte)opcode];
            var (cpu, _, data) = Program(code, queued); int width = 1 + (opcode & 1);
            for (int i = 0; i < 4; i++) cpu.Step();
            Assert.That(data.Count, Is.EqualTo(8 * width));
            for (int i = 0; i < 4; i++)
            {
                int offset = i * 2 * width;
                Assert.That(data[offset + width].Clock - data[offset + width - 1].Clock,
                    Is.EqualTo(i == 0 && !queued ? 8 : 7), $"Iteration {i}, queued={queued}, segment={segment}");
                if (i > 0) Assert.That(data[offset].Clock - data[offset - 1].Clock, Is.EqualTo(10));
            }
        }
    }

    [TestCase(0xAE, 0xF2)]
    [TestCase(0xAF, 0xF2)]
    [TestCase(0xAE, 0xF3)]
    [TestCase(0xAF, 0xF3)]
    public void RepScasKeepsTheFirstBoundaryWithAnInitiallyEmptyQueue(int opcode, int prefix)
    {
        foreach (bool queued in new[] { false, true })
        foreach (bool segment in new[] { false, true })
        {
            var (cpu, _, data) = Program(segment ? [0x3E, (byte)prefix, (byte)opcode] : [(byte)prefix, (byte)opcode], queued);
            if (prefix == 0xF2) cpu.AX = 0xAAAA;
            int width = 1 + (opcode & 1);
            for (int i = 0; i < 4; i++) cpu.Step();
            Assert.That(data.Count, Is.EqualTo(4 * width));
            for (int i = width; i < data.Count; i += width)
                Assert.That(data[i].Clock - data[i - 1].Clock, Is.EqualTo(15));
        }
    }

    [TestCase(0xA4)]
    [TestCase(0xA5)]
    [TestCase(0xAE)]
    [TestCase(0xAF)]
    public void RestoringDuringRepPreservesThePendingQueueAndEveryFollowingAccess(int opcode)
    {
        var first = Program([0xF3, (byte)opcode], queued: false); first.Cpu.Step();
        byte[] state = Save(first.Cpu);
        var bus = new Bus { Memory = (byte[])first.Bus.Memory.Clone() }; var second = new I8088Cpu(bus);
        second.Load(new BinaryReader(new MemoryStream(state))); var accesses = new List<I8088BusAccess>();
        second.BusAccessed += a => { if (a.Kind != I8088BusAccessKind.Code) accesses.Add(a); };
        first.Data.Clear();
        for (int i = 0; i < 3; i++) { first.Cpu.Step(); second.Step(); }
        Assert.That(accesses, Is.EqualTo(first.Data));
        Assert.That(bus.Memory, Is.EqualTo(first.Bus.Memory));
        Assert.That(Save(second), Is.EqualTo(Save(first.Cpu)));
    }

    [TestCase(2, 0, true, new int[] { 4, 16, 4 })]
    [TestCase(2, 4, true, new int[] { 4, 18, 4 })]
    [TestCase(2, 4, false, new int[] { 4, 18, 4 })]
    [TestCase(3, 0, true, new int[] { 4, 9, 4, 10, 4, 13, 4 })]
    [TestCase(3, 4, true, new int[] { 4, 10, 4, 10, 4, 13, 4 })]
    [TestCase(3, 4, false, new int[] { 4, 10, 4, 13, 4, 13, 4 })]
    [TestCase(5, 0, true, new int[] { 4, 10, 4 })]
    [TestCase(5, 4, true, new int[] { 4, 11, 4 })]
    [TestCase(5, 4, false, new int[] { 4, 11, 4 })]
    public void IndirectWordsKeepTheirQueueDependentIntervals(int group, int address, bool queued, int[] gaps)
    {
        var (cpu, _, data) = Program([0xFF, (byte)((group << 3) | address)], queued); cpu.Step();
        Assert.That(data.Zip(data.Skip(1), (a, b) => b.Clock - a.Clock), Is.EqualTo(gaps));
        Assert.That((cpu.IP, cpu.CS), Is.EqualTo(((ushort)0x5555, group == 2 ? (ushort)0x100 : (ushort)0x5555)));
        Assert.That(cpu.SP, Is.EqualTo(group == 2 ? 0xFE : group == 3 ? 0xFC : 0x100));
    }

    [TestCase(0xCC, true, 7)]
    [TestCase(0xCC, false, 8)]
    [TestCase(0xCD, true, 8)]
    [TestCase(0xCD, false, 8)]
    [TestCase(0xCE, true, 7)]
    [TestCase(0xCE, false, 8)]
    public void SoftwareInterruptsSeparateTheVectorAndFrameWords(int opcode, bool queued, int vectorGap)
    {
        var (cpu, bus, data) = Program(opcode == 0xCD ? [0xCD, 0x21] : [(byte)opcode], queued);
        int vector = opcode == 0xCD ? 0x21 : opcode == 0xCC ? 3 : 4;
        new byte[] { 0x80, 0, 0, 1 }.CopyTo(bus.Memory, vector * 4);
        cpu.Flags = I8088Cpu.Overflow | I8088Cpu.InterruptEnable | I8088Cpu.Trap; ushort flags = cpu.Flags;
        cpu.Step();
        Assert.That(data.Zip(data.Skip(1), (a, b) => b.Clock - a.Clock),
            Is.EqualTo(new int[] { 4, vectorGap, 4, 8, 4, 10, 4, 13, 4 }));
        Assert.That(bus.Memory.AsSpan(0x40FA, 6).ToArray(),
            Is.EqualTo(new byte[] { (byte)(opcode == 0xCD ? 2 : 1), 0, 0, 1, (byte)flags, (byte)(flags >> 8) }));
        Assert.That(cpu.Flags & (I8088Cpu.Trap | I8088Cpu.InterruptEnable), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AamZeroCompletesItsSetupBeforeEnteringTheInterruptFrame(bool queued)
    {
        var (cpu, bus, data) = Program([0xD4, 0], queued); cpu.Step();
        Assert.That(data.Zip(data.Skip(1), (a, b) => b.Clock - a.Clock),
            Is.EqualTo(new int[] { 4, 7, 4, 8, 4, 10, 4, 13, 4 }));
        Assert.That((cpu.AX, cpu.CS, cpu.IP), Is.EqualTo(((ushort)0x5555, (ushort)0x100, (ushort)0x80)));
        Assert.That(bus.Memory.AsSpan(0x40FA, 2).ToArray(), Is.EqualTo(new byte[] { 2, 0 }));
    }

    [TestCase(I8088Model.Intel8088)]
    [TestCase(I8088Model.Intel8086)]
    public void IndirectFarCallReadsTheWholePointerBeforeOverlappingItsStack(I8088Model model)
    {
        foreach (bool timed in new[] { false, true })
        {
            var (cpu, bus, _) = Program([0xFF, 0x1C], model: model);
            if (!timed) cpu.TimingEnabled = false;
            cpu.DS = cpu.SS; cpu.SI = 0xFFFF; cpu.SP = 1;
            bus.Memory[0x13FFF] = 0x80; bus.Memory[0x4000] = 0;
            bus.Memory[0x4001] = 0; bus.Memory[0x4002] = 1;
            bus.Memory[0x1080] = 0xCB; ushort flags = cpu.Flags;
            cpu.Step();
            Assert.That((cpu.CS, cpu.IP, cpu.SP), Is.EqualTo(((ushort)0x100, (ushort)0x80, (ushort)0xFFFD)));
            Assert.That(bus.Memory.AsSpan(0x13FFD, 3).ToArray(), Is.EqualTo(new byte[] { 2, 0, 0 }));
            Assert.That(bus.Memory[0x4000], Is.EqualTo(1));
            cpu.Step();
            Assert.That((cpu.CS, cpu.IP, cpu.SP, cpu.Flags), Is.EqualTo(((ushort)0x100, (ushort)2, (ushort)1, flags)));
        }
    }

    [TestCase(false, false, 0x1234, 0, 0, 21)]
    [TestCase(false, false, 0x200, 0, 2, 21)]
    [TestCase(true, false, 0, 1, 1, 21)]
    [TestCase(false, true, 0x1234, 0, 0, 31)]
    [TestCase(false, true, 0xF234, 0, 0, 35)]
    [TestCase(false, true, 0x200, 0, 2, 31)]
    [TestCase(false, true, 0xFE00, 0, 2, 35)]
    [TestCase(false, true, 0x200, 0, 0xFE, 30)]
    [TestCase(false, true, 0xFE00, 0, 0xFE, 34)]
    [TestCase(false, true, 0x100, 0, 2, 103)]
    [TestCase(false, true, 0xFF00, 0, 2, 107)]
    [TestCase(false, true, 0x1FE, 0, 2, 112)]
    [TestCase(true, true, 0, 1, 2, 167)]
    [TestCase(true, true, 0, 0xFFFF, 2, 171)]
    public void DivideErrorsPlaceEarlyOrCompletedQuotientWorkBeforeTheVector(bool word, bool signed, int ax, int dx, int divisor, int gap)
    {
        foreach (bool queued in new[] { false, true })
        {
            var (cpu, bus, data) = Program([(byte)(word ? 0xF7 : 0xF6), (byte)(signed ? 0x3C : 0x34)], queued);
            cpu.AX = (ushort)ax; cpu.DX = (ushort)dx;
            bus.Memory[0x2020] = (byte)divisor; bus.Memory[0x2021] = (byte)(divisor >> 8);
            cpu.Step(); int width = word ? 2 : 1;
            Assert.That(data[width].Clock - data[width - 1].Clock, Is.EqualTo(gap));
            Assert.That(data.Skip(width).Zip(data.Skip(width + 1), (a, b) => b.Clock - a.Clock),
                Is.EqualTo(new int[] { 4, 7, 4, 8, 4, 10, 4, 13, 4 }));
            Assert.That((cpu.AX, cpu.DX, cpu.CS, cpu.IP), Is.EqualTo(((ushort)ax, (ushort)dx, (ushort)0x100, (ushort)0x80)));
            Assert.That(bus.Memory.AsSpan(0x40FA, 2).ToArray(), Is.EqualTo(new byte[] { 2, 0 }));
        }
    }
}
