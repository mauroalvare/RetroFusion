using Retro.Cpu.I8088;

namespace I8088.Tests;

// Own programs; the relative data clocks are measured on SingleStepTests/8088 v2.
public sealed class StackControlTimingTests
{
    private sealed class Bus : I8088Bus
    {
        public byte[] Memory = new byte[1 << 20];
        public Action? OnClock;
        public long Clocks, HoldUntil;
        public List<long> Grants = [];
        public bool HoldRequested => Clocks < HoldUntil;
        public void SetBusGranted(bool granted) { if (granted) Grants.Add(Clocks); }
        public void Clock() { Clocks++; OnClock?.Invoke(); }
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address, byte value) => Memory[address] = value;
        public byte In(ushort port) => 0xFF;
        public void Out(ushort port, byte value) { }
    }
    private static (I8088Cpu Cpu, Bus Bus, List<I8088BusAccess> Data) Program(byte[] code, bool queued = true,
        I8088Model model = I8088Model.Intel8088)
    {
        var bus = new Bus(); Array.Fill(bus.Memory, (byte)0x90, 0x1000, 64); code.CopyTo(bus.Memory, 0x1000);
        new byte[] { 0x34, 0x12, 0x00, 0x01, 0x43, 0xF2 }.CopyTo(bus.Memory, 0x3100);
        var cpu = new I8088Cpu(bus, model) { CS = 0x100, DS = 0x200, SS = 0x300, SP = 0x100,
            BX = 0x10, BP = 0x30, SI = 0x20, DI = 0x40, TimingEnabled = true };
        if (queued) cpu.InitializePrefetchQueue(bus.Memory.AsSpan(0x1000, 4));
        var data = new List<I8088BusAccess>();
        cpu.BusAccessed += a => { if (a.Kind != I8088BusAccessKind.Code) data.Add(a); };
        return (cpu, bus, data);
    }

    [TestCase(new byte[] { 0x8F, 0x04 }, true, 13)]
    [TestCase(new byte[] { 0x8F, 0x04 }, false, 14)]
    [TestCase(new byte[] { 0x8F, 0x44, 0xC7 }, true, 13)]
    [TestCase(new byte[] { 0x8F, 0x44, 0xC7 }, false, 14)]
    [TestCase(new byte[] { 0x8F, 0x81, 0x00, 0x00 }, true, 13)]
    [TestCase(new byte[] { 0x8F, 0x81, 0x00, 0x00 }, false, 14)]
    [TestCase(new byte[] { 0x8F, 0x82, 0x00, 0x00 }, true, 13)]
    [TestCase(new byte[] { 0x8F, 0x82, 0x00, 0x00 }, false, 14)]
    [TestCase(new byte[] { 0x36, 0x8F, 0x04 }, true, 14)]
    [TestCase(new byte[] { 0x36, 0x8F, 0x04 }, false, 14)]
    public void PopMemoryCoordinatesItsReadAndDestinationRequests(byte[] code, bool queued, int write)
    {
        var (cpu, _, data) = Program(code, queued); cpu.Step();
        Assert.That(data.Select(a => a.Clock - data[0].Clock), Is.EqualTo(new long[] { 0, 4, write, write + 4 }));
        Assert.That(data.Select(a => a.Kind), Is.EqualTo(new[] { I8088BusAccessKind.MemoryRead, I8088BusAccessKind.MemoryRead,
            I8088BusAccessKind.MemoryWrite, I8088BusAccessKind.MemoryWrite }));
        Assert.That(data.Select(a => a.Value), Is.EqualTo(new byte[] { 0x34, 0x12, 0x34, 0x12 }));
        Assert.That(cpu.SP, Is.EqualTo(0x102));
    }

    [TestCase(new byte[] { 0x9A, 0x10, 0, 0, 2 }, new long[] { 0, 4, 17, 21 })]
    [TestCase(new byte[] { 0xCA, 0, 0 }, new long[] { 0, 4, 13, 17 })]
    [TestCase(new byte[] { 0xCB }, new long[] { 0, 4, 13, 17 })]
    [TestCase(new byte[] { 0xCF }, new long[] { 0, 4, 13, 17, 25, 29 })]
    public void FarControlTransfersSeparateTheirStackWords(byte[] code, long[] expected)
    {
        foreach (bool queued in new[] { true, false })
        {
            var (cpu, _, data) = Program(code, queued); cpu.Step();
            Assert.That(data.Select(a => a.Clock - data[0].Clock), Is.EqualTo(expected));
        }
    }

    [TestCase(new byte[] { 0x8F, 0x04 }, 30)]
    [TestCase(new byte[] { 0x9A, 0x10, 0, 0, 2 }, 40)] // One immediate byte must be fetched beyond the seeded queue.
    [TestCase(new byte[] { 0xCA, 0, 0 }, 25)]
    [TestCase(new byte[] { 0xCB }, 26)]
    [TestCase(new byte[] { 0xCF }, 36)]
    public void PlacingTheWorkKeepsTheNominalBudget(byte[] code, int clocks)
    {
        var (cpu, _, _) = Program(code); cpu.Step();
        Assert.That(cpu.Cycles, Is.EqualTo(clocks));
    }

    [TestCase(I8088Model.Intel8088)]
    [TestCase(I8088Model.Intel8086)]
    public void FarCallAndReturnKeepTheOriginalFrame(I8088Model model)
    {
        foreach (bool timed in new[] { true, false })
        {
            var (cpu, bus, _) = Program([0x9A, 0x10, 0, 0, 2], model: model);
            if (!timed) cpu.TimingEnabled = false;
            cpu.Flags = I8088Cpu.Carry | I8088Cpu.Zero; ushort flags = cpu.Flags;
            bus.Memory[0x2010] = 0xCB; cpu.Step();
            Assert.That((cpu.CS, cpu.IP, cpu.SP), Is.EqualTo(((ushort)0x200, (ushort)0x10, (ushort)0xFC)));
            Assert.That(bus.Memory.AsSpan(0x30FC, 4).ToArray(), Is.EqualTo(new byte[] { 5, 0, 0, 1 }));
            cpu.Step();
            Assert.That((cpu.CS, cpu.IP, cpu.SP), Is.EqualTo(((ushort)0x100, (ushort)5, (ushort)0x100)));
            Assert.That(cpu.Flags, Is.EqualTo(flags));
        }
    }

    [TestCase(I8088Model.Intel8088)]
    [TestCase(I8088Model.Intel8086)]
    public void IretRestoresTheFrameAndFlags(I8088Model model)
    {
        var (cpu, _, _) = Program([0xCF], model: model); cpu.Step();
        Assert.That((cpu.CS, cpu.IP, cpu.SP), Is.EqualTo(((ushort)0x100, (ushort)0x1234, (ushort)0x106)));
        Assert.That(cpu.Flag(I8088Cpu.Carry) && cpu.Flag(I8088Cpu.Zero) && cpu.Flag(I8088Cpu.InterruptEnable), Is.True);
    }

    [TestCase(I8088Model.Intel8088)]
    [TestCase(I8088Model.Intel8086)]
    public void NmiDuringPopWaitsForTheDestinationWrite(I8088Model model)
    {
        var (cpu, bus, data) = Program([0x8F, 0x04], queued: false, model: model);
        new byte[] { 0x80, 0, 0, 1 }.CopyTo(bus.Memory, 8);
        cpu.BusAccessed += a =>
        {
            if (a.Kind == I8088BusAccessKind.MemoryRead && a.Address == 0x3101)
                bus.OnClock = () => { bus.OnClock = null; cpu.RequestNmi(); };
        };
        cpu.Step();
        Assert.That(cpu.IP, Is.EqualTo(2));
        Assert.That(data.Count(a => a.Kind == I8088BusAccessKind.MemoryWrite), Is.EqualTo(2));
        Assert.That(bus.Memory.AsSpan(0x2020, 2).ToArray(), Is.EqualTo(new byte[] { 0x34, 0x12 }));
        cpu.Step();
        Assert.That((cpu.CS, cpu.IP, cpu.SP), Is.EqualTo(((ushort)0x100, (ushort)0x80, (ushort)0xFC)));
        Assert.That(cpu.Instructions, Is.EqualTo(1));
        Assert.That(bus.Memory[0x30FC], Is.EqualTo(2));
    }

    [Test]
    public void HoldDuringPopPrefetchFinishesT4BeforeGrantingTheBus()
    {
        var (cpu, bus, data) = Program([0x8F, 0x04], queued: false);
        bool stackRead = false; long prefetch = 0;
        cpu.BusAccessed += a =>
        {
            if (a.Kind == I8088BusAccessKind.MemoryRead && a.Address == 0x3101) stackRead = true;
            if (stackRead && prefetch == 0 && a.Kind == I8088BusAccessKind.Code)
            { prefetch = a.Clock; bus.HoldUntil = a.Clock + 10; }
        };
        cpu.Step();
        Assert.That(prefetch, Is.GreaterThan(0));
        Assert.That(bus.Grants, Is.Not.Empty);
        Assert.That(bus.Grants.All(t => t >= prefetch + 1), Is.True, "T4 finishes before HOLD is granted.");
        Assert.That(data.Where(a => a.Kind == I8088BusAccessKind.MemoryWrite).All(a => a.Clock >= bus.HoldUntil), Is.True);
        Assert.That(bus.Memory.AsSpan(0x2020, 2).ToArray(), Is.EqualTo(new byte[] { 0x34, 0x12 }));
        Assert.That(cpu.Cycles, Is.EqualTo(bus.Clocks));
    }
}
