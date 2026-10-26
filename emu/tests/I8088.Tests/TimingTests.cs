using Retro.Cpu.I8088;

namespace I8088.Tests;

public sealed class TimingTests
{
    private sealed class Bus : I8088Bus
    {
        public byte[] Memory = new byte[1 << 20];
        public long Clocks;
        public int HoldUntil;
        public bool HoldRequested => Clocks < HoldUntil;
        public List<(long Time, uint Address)> Reads = [];
        public List<(long Time, ushort Port, byte Value)> Outputs = [];
        public Action? OnClock;
        public void Clock() { Clocks++; OnClock?.Invoke(); }
        public byte ReadMemory(uint address) { Reads.Add((Clocks, address)); return Memory[address]; }
        public void WriteMemory(uint address, byte value) => Memory[address] = value;
        public byte In(ushort port) => 17;
        public void Out(ushort port, byte value) => Outputs.Add((Clocks, port, value));
    }
    private static (I8088Cpu Cpu, Bus Bus) Program(params byte[] code)
    {
        var bus = new Bus(); code.CopyTo(bus.Memory, 0x1000);
        return (new I8088Cpu(bus) { CS = 0x100, TimingEnabled = true, SS = 0, SP = 0xFFF0 }, bus);
    }
    private static byte[] Save(I8088Cpu cpu) { using var s = new MemoryStream(); cpu.Save(new BinaryWriter(s)); return s.ToArray(); }
    private static void Load(I8088Cpu cpu, byte[] state) => cpu.Load(new BinaryReader(new MemoryStream(state)));

    [Test]
    public void FetchUsesFourClockByteTransfersAndPrefetchRunsDuringEuWork()
    {
        var (c, b) = Program(0xB8, 0x34, 0x12, 0x90, 0xF4);
        c.Step(); Assert.That(c.AX, Is.EqualTo(0x1234)); Assert.That(c.Cycles, Is.EqualTo(16));
        Assert.That(b.Reads.Take(4).Select(v => v.Time), Is.EqualTo(new long[] { 3, 7, 11, 15 }));
        c.Step(); Assert.That(c.Cycles, Is.EqualTo(19), "NOP consumes its prefetched byte without another bus stall.");
        Assert.That(b.Clocks, Is.EqualTo(c.Cycles));
    }
    [Test]
    public void WordTransfersUseTwoBusCyclesAndOffsetWrapsWithoutChangingSegment()
    {
        var (c, b) = Program(0xA1, 0xFF, 0xFF, 0xF4); c.DS = 0x200;
        b.Memory[0x11FFF] = 0xCA; b.Memory[0x2000] = 0xFE;
        c.Step(); Assert.That(c.AX, Is.EqualTo(0xFECA));
        var data = b.Reads.Where(v => v.Address is 0x11FFF or 0x2000).ToArray();
        Assert.That(data.Length, Is.EqualTo(2)); Assert.That(data[1].Time - data[0].Time, Is.EqualTo(4));
    }
    [Test]
    public void PrefetchedBytesRemainStaleUntilControlTransferFlushesThem()
    {
        var (c, b) = Program(0xD4, 10, 0xB0, 1, 0xEB, 0, 0xB0, 2, 0xF4); c.AX = 23;
        c.Step(); b.Memory[0x1003] = 99; c.Step(); Assert.That(c.AX & 255, Is.EqualTo(1));
        b.Memory[0x1007] = 33; c.Step(); c.Step(); Assert.That(c.AX & 255, Is.EqualTo(33));
    }
    [Test]
    public void RepReusesDecodedBytesAndAnInterruptRestartsFromPrefix()
    {
        var (c, b) = Program(0xF3, 0xAA, 0xF4); c.CX = 3; c.ES = 0; c.DI = 0x500; c.AX = 0x77;
        c.Step(); c.Step(); c.Step(); Assert.That(b.Memory.AsSpan(0x500, 3).ToArray(), Is.EqualTo(new byte[] { 0x77, 0x77, 0x77 }));
        Assert.That(b.Reads.Count(v => v.Address == 0x1000), Is.EqualTo(1));
        Assert.That(c.IP, Is.EqualTo(2));
        var (other, ob) = Program(0xF3, 0xAA, 0xF4); other.CX = 3; other.Step();
        ob.Memory[8] = 0; ob.Memory[9] = 3; other.RequestNmi(); other.Step();
        Assert.That(other.IP, Is.EqualTo(0x300)); Assert.That(other.ReadWord(other.SS, other.SP), Is.Zero);
    }
    [Test]
    public void HaltKeepsDevicesClockingAndCanWakeOnAnExternalInterrupt()
    {
        var (c, b) = Program(0xFB, 0xF4, 0x90); c.Step(); c.Step(); long haltedAt = c.Cycles;
        b.OnClock = () => { if (b.Clocks == haltedAt + 5) c.RequestInterrupt(0x20); };
        for (int i = 0; i < 5; i++) Assert.That(c.Step(), Is.False);
        Assert.That(c.Instructions, Is.EqualTo(2)); Assert.That(c.Cycles, Is.EqualTo(haltedAt + 5));
        c.Step(); Assert.That(c.Halted, Is.False); Assert.That(c.Cycles, Is.GreaterThan(haltedAt + 5));
    }
    [Test]
    public void DmaHoldDelaysInstructionFetchWithoutIncrementingInstructions()
    {
        var (c, b) = Program(0x90); b.HoldUntil = 12;
        c.Step(); Assert.That(b.Reads[0].Time, Is.EqualTo(15));
        Assert.That(c.Instructions, Is.EqualTo(1)); Assert.That(c.Cycles, Is.EqualTo(19));
    }
    [Test]
    public void StateRestoresClockPartialPrefetchAndRepWithoutChangingTheBus()
    {
        var (c, b) = Program(0xF3, 0xAA, 0x90, 0xF4); c.CX = 4; c.ES = 0; c.DI = 0x500; c.Step();
        byte[] state = Save(c); var secondBus = new Bus { Memory = (byte[])b.Memory.Clone(), Clocks = b.Clocks };
        var second = new I8088Cpu(secondBus); Load(second, state);
        c.Step(); second.Step(); c.Step(); second.Step();
        Assert.That(second.Cycles, Is.EqualTo(c.Cycles)); Assert.That(secondBus.Memory, Is.EqualTo(b.Memory));
        Assert.That(Save(second), Is.EqualTo(Save(c)));
        byte[] before = Save(second); Assert.Catch<IOException>(() => Load(second, state[..^1])); Assert.That(Save(second), Is.EqualTo(before));
    }
}