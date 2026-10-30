using Retro.Cpu.I8088;

namespace I8088.Tests;

public sealed class ReadyTimingTests
{
    private sealed class Bus : I8088Bus
    {
        public byte[] Memory = new byte[1 << 20];
        public long Clocks, ReadyAt, HoldUntil;
        public Action? OnClock;
        public List<long> Grants = [];
        public bool Ready => Clocks >= ReadyAt;
        public bool HoldRequested => HoldUntil > 0 && Clocks >= 4 && Clocks < HoldUntil;
        public void Clock() { Clocks++; OnClock?.Invoke(); }
        public void SetBusGranted(bool granted) { if (granted) Grants.Add(Clocks); }
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address, byte value) => Memory[address] = value;
        public byte In(ushort port) => (byte)port;
        public void Out(ushort port, byte value) { }
    }
    private static byte[] Save(I8088Cpu cpu)
    {
        using var stream = new MemoryStream(); cpu.Save(new BinaryWriter(stream)); return stream.ToArray();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WaitClocksDelayTheTransferUntilReadyAndThenFinishT4(bool write)
    {
        var bus = new Bus { ReadyAt = 7 }; bus.Memory[0x20] = 0xA5;
        var cpu = new I8088Cpu(bus) { TimingEnabled = true };
        var accesses = new List<I8088BusAccess>(); cpu.BusAccessed += accesses.Add;
        if (write) cpu.WriteByte(0, 0x20, 0x5A);
        else Assert.That(cpu.ReadByte(0, 0x20), Is.EqualTo(0xA5));
        Assert.That(accesses.Count, Is.EqualTo(1));
        Assert.That(accesses[0].Clock, Is.EqualTo(7));
        Assert.That(accesses[0].Value, Is.EqualTo(write ? 0x5A : 0xA5));
        Assert.That(cpu.Cycles, Is.EqualTo(8));
        Assert.That(bus.Clocks, Is.EqualTo(cpu.Cycles));
    }

    [TestCase(I8088Model.Intel8088, 0x20, 10)]
    [TestCase(I8088Model.Intel8086, 0x20, 6)]
    [TestCase(I8088Model.Intel8086, 0x21, 10)]
    public void WordTransfersKeepTheirWidthAndAlignmentDuringWaits(I8088Model model, int offset, int highClock)
    {
        var bus = new Bus { ReadyAt = 6 };
        var cpu = new I8088Cpu(bus, model) { TimingEnabled = true };
        var accesses = new List<I8088BusAccess>(); cpu.BusAccessed += accesses.Add;
        cpu.WriteWord(0, (ushort)offset, 0x1234);
        Assert.That(accesses.Select(a => a.Clock), Is.EqualTo(new long[] { 6, highClock }));
        Assert.That(bus.Memory.AsSpan(offset, 2).ToArray(), Is.EqualTo(new byte[] { 0x34, 0x12 }));
        Assert.That(cpu.Cycles, Is.EqualTo(highClock + 1));
    }

    [Test]
    public void PortTransfersAlsoWaitForReady()
    {
        var bus = new Bus(); new byte[] { 0xEC, 0xEE }.CopyTo(bus.Memory, 0x1000);
        var cpu = new I8088Cpu(bus) { CS = 0x100, DX = 0x35, TimingEnabled = true };
        cpu.InitializePrefetchQueue(bus.Memory.AsSpan(0x1000, 4));
        var ports = new List<I8088BusAccess>();
        cpu.BusAccessed += a => { if (a.Kind is I8088BusAccessKind.Input or I8088BusAccessKind.Output) ports.Add(a); };
        foreach (int wait in new[] { 7, 9 })
        {
            bus.ReadyAt = cpu.Cycles + wait; cpu.Step();
            Assert.That(ports[^1].Clock, Is.EqualTo(bus.ReadyAt));
        }
        Assert.That(cpu.AX & 255, Is.EqualTo(0x35));
        Assert.That(ports.Select(a => a.Value), Is.EqualTo(new byte[] { 0x35, 0x35 }));
    }

    [Test]
    public void HoldDuringWaitDoesNotGainOwnershipBeforeT4()
    {
        var bus = new Bus { ReadyAt = 7, HoldUntil = 12 };
        var cpu = new I8088Cpu(bus) { TimingEnabled = true };
        cpu.ReadByte(0, 0); Assert.That(bus.Grants, Is.Empty);
        cpu.ReadByte(0, 1);
        Assert.That(bus.Grants, Is.Not.Empty);
        Assert.That(bus.Grants.Min(), Is.EqualTo(8));
        Assert.That(cpu.Cycles, Is.EqualTo(16));
    }

    [Test]
    public void ReadyFallingInT4DoesNotRepeatTheAlreadyLatchedTransfer()
    {
        var bus = new Bus(); var cpu = new I8088Cpu(bus) { TimingEnabled = true };
        var accesses = new List<I8088BusAccess>(); cpu.BusAccessed += accesses.Add;
        bus.OnClock = () => { if (bus.Clocks == 4) bus.ReadyAt = 12; };
        cpu.ReadByte(0, 0); Assert.That(cpu.Cycles, Is.EqualTo(4));
        cpu.ReadByte(0, 1);
        Assert.That(accesses.Select(a => a.Clock), Is.EqualTo(new long[] { 3, 12 }));
        Assert.That(cpu.Cycles, Is.EqualTo(13));
    }

    [Test]
    public void PendingPrefetchWaitSurvivesAStateRoundTripWithoutReadingEarlyOrTwice()
    {
        var bus = new Bus { ReadyAt = 7 }; Array.Fill(bus.Memory, (byte)0x90, 0x1000, 16);
        var cpu = new I8088Cpu(bus) { CS = 0x100, TimingEnabled = true };
        cpu.InitializePrefetchQueue([0x90]); var accesses = new List<I8088BusAccess>(); cpu.BusAccessed += accesses.Add;
        cpu.Step(); Assert.That(cpu.Cycles, Is.EqualTo(3)); Assert.That(accesses, Is.Empty);
        byte[] state = Save(cpu);
        var otherBus = new Bus { Clocks = bus.Clocks, ReadyAt = bus.ReadyAt, Memory = (byte[])bus.Memory.Clone() };
        var other = new I8088Cpu(otherBus); other.Load(new BinaryReader(new MemoryStream(state)));
        var restored = new List<I8088BusAccess>(); other.BusAccessed += restored.Add;
        cpu.Step(); other.Step();
        Assert.That(accesses[0].Clock, Is.EqualTo(7));
        Assert.That(accesses.Count(a => a.Address == 0x1001), Is.EqualTo(1));
        Assert.That(restored, Is.EqualTo(accesses));
        Assert.That(Save(other), Is.EqualTo(Save(cpu)));
    }

    [Test]
    public void FunctionalModeDoesNotWaitForAClockedSignal()
    {
        var bus = new Bus { ReadyAt = long.MaxValue }; var cpu = new I8088Cpu(bus);
        cpu.WriteWord(0, 0x20, 0x1234);
        Assert.That(cpu.ReadWord(0, 0x20), Is.EqualTo(0x1234));
        Assert.That(cpu.Cycles, Is.Zero); Assert.That(bus.Clocks, Is.Zero);
    }
}
