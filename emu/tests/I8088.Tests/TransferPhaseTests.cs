using Retro.Cpu.I8088;

namespace I8088.Tests;

public sealed class TransferPhaseTests
{
    private sealed class Bus : I8088Bus
    {
        public byte[] Memory = new byte[1 << 20];
        public long Clocks;
        public List<(long Time, uint Address, byte Value)> Reads = [], Writes = [];
        public Action? OnClock;
        public void Clock() { Clocks++; OnClock?.Invoke(); }
        public byte ReadMemory(uint address) { byte value = Memory[address]; Reads.Add((Clocks, address, value)); return value; }
        public void WriteMemory(uint address, byte value) { Memory[address] = value; Writes.Add((Clocks, address, value)); }
        public byte In(ushort port) => 255;
        public void Out(ushort port, byte value) { }
    }
    [TestCase(I8088Model.Intel8088)]
    [TestCase(I8088Model.Intel8086)]
    public void DataAccessTakesEffectAtT3AndFinishesT4BeforeReturning(I8088Model model)
    {
        var bus = new Bus();
        var cpu = new I8088Cpu(bus, model) { TimingEnabled = true };
        byte seenAtT4 = 0;
        bus.OnClock = () => { if (bus.Clocks == 4) seenAtT4 = bus.Memory[0x100]; };
        cpu.WriteByte(0, 0x100, 0x5A);
        Assert.That(bus.Writes[0].Time, Is.EqualTo(3), "No-wait 8088 hardware trace: the data strobe is on T3.");
        Assert.That(seenAtT4, Is.EqualTo(0x5A));
        Assert.That(cpu.Cycles, Is.EqualTo(4));
        bus.OnClock = () => { if (bus.Clocks == 8) bus.Memory[0x100] = 0xA5; };
        Assert.That(cpu.ReadByte(0, 0x100), Is.EqualTo(0x5A), "A change on T4 must not replace data already sampled at T3.");
        Assert.That(bus.Reads[0].Time, Is.EqualTo(7));
        Assert.That(cpu.Cycles, Is.EqualTo(8));
    }
    [Test]
    public void PrefetchSamplesAtT3AndStillDeliversACompletedFourClockTransfer()
    {
        var bus = new Bus(); new byte[] { 0xB8, 0x34, 0x12, 0x90, 0xF4 }.CopyTo(bus.Memory, 0x1000);
        var cpu = new I8088Cpu(bus) { CS = 0x100, TimingEnabled = true };
        cpu.Step();
        Assert.That(cpu.AX, Is.EqualTo(0x1234));
        Assert.That(cpu.Cycles, Is.EqualTo(16));
        Assert.That(bus.Reads.Take(4).Select(a => a.Time), Is.EqualTo(new long[] { 3, 7, 11, 15 }));
    }

    [Test]
    public void SavedPrefetchKeepsTheByteSampledAtT3WithoutRepeatingTheReadAtT4()
    {
        var bus = new Bus(); new byte[] { 0xB8, 0x34, 0x12, 0x90, 0x40, 0xF4 }.CopyTo(bus.Memory, 0x1000);
        var cpu = new I8088Cpu(bus) { CS = 0x100, TimingEnabled = true };
        cpu.Step(); cpu.Step();
        Assert.That(cpu.Cycles, Is.EqualTo(19));
        using var saved = new MemoryStream(); cpu.Save(new BinaryWriter(saved));
        bus.Memory[0x1004] = 0x48; // DEC must not replace the already sampled INC.
        var otherBus = new Bus { Memory = (byte[])bus.Memory.Clone(), Clocks = bus.Clocks };
        var other = new I8088Cpu(otherBus);
        other.Load(new BinaryReader(new MemoryStream(saved.ToArray())));
        int reads = bus.Reads.Count;
        cpu.Step(); other.Step();
        Assert.That(cpu.AX, Is.EqualTo(0x1235));
        Assert.That(other.AX, Is.EqualTo(cpu.AX));
        Assert.That(other.Cycles, Is.EqualTo(cpu.Cycles));
        Assert.That(bus.Reads.Skip(reads).Any(a => a.Address == 0x1004), Is.False);
        Assert.That(otherBus.Reads.Any(a => a.Address == 0x1004), Is.False);
    }

    [TestCase(I8088Model.Intel8088)]
    [TestCase(I8088Model.Intel8086)]
    public void Format2PartialPrefetchStillFinishesOneLegacyRead(I8088Model model)
    {
        var bus = new Bus(); bus.Memory[0x1000] = 0x90;
        var cpu = new I8088Cpu(bus, model) { CS = 0x100, TimingEnabled = true };
        cpu.InitializePrefetchQueue(model == I8088Model.Intel8086
            ? new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 } : new byte[] { 0x90, 0x90, 0x90, 0x90 });
        cpu.Step(); if (model == I8088Model.Intel8086) cpu.Step();
        using var stream = new MemoryStream(); cpu.Save(new BinaryWriter(stream));
        byte[] legacy = stream.ToArray()[..^3];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(legacy.AsSpan(11, 4), 2);
        var otherBus = new Bus { Memory = (byte[])bus.Memory.Clone(), Clocks = bus.Clocks };
        var other = new I8088Cpu(otherBus, model);
        other.Load(new BinaryReader(new MemoryStream(legacy)));
        other.Step();
        Assert.That(otherBus.Reads, Is.Not.Empty);
        Assert.That(otherBus.Reads[0].Time, Is.EqualTo(bus.Clocks + 1));
    }

    [TestCase(I8088Model.Intel8088, 4)]
    [TestCase(I8088Model.Intel8086, 6)]
    public void DiagnosticQueueHonorsStaleBytesAndRejectsChangesAfterExecution(I8088Model model, int capacity)
    {
        var bus = new Bus(); bus.Memory[0x1000] = 0x48;
        var cpu = new I8088Cpu(bus, model) { CS = 0x100, AX = 0x100, TimingEnabled = true };
        byte[] queue = Enumerable.Repeat((byte)0x40, capacity).ToArray();
        cpu.InitializePrefetchQueue(queue); cpu.Step();
        Assert.That(cpu.AX, Is.EqualTo(0x101));
        Assert.That(bus.Reads.Any(a => a.Address == 0x1000), Is.False);
        Assert.Throws<InvalidOperationException>(() => cpu.InitializePrefetchQueue(queue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new I8088Cpu(new Bus(), model) { TimingEnabled = true }.InitializePrefetchQueue(new byte[capacity + 1]));
        Assert.Throws<InvalidOperationException>(() => new I8088Cpu(new Bus(), model).InitializePrefetchQueue(queue));
    }

    [Test]
    public void FunctionalModeDoesNotEmitClockedAccessEvents()
    {
        var bus = new Bus(); var cpu = new I8088Cpu(bus); var accesses = new List<I8088BusAccess>();
        cpu.BusAccessed += accesses.Add;
        cpu.WriteWord(0, 0x100, 0xCAFE); cpu.ReadWord(0, 0x100);
        Assert.That(accesses, Is.Empty); Assert.That(cpu.Cycles, Is.Zero);
        cpu.TimingEnabled = true; cpu.ReadByte(0, 0x100);
        Assert.That(accesses, Is.EqualTo(new[] { new I8088BusAccess(3, I8088BusAccessKind.MemoryRead, 0x100, 0xFE) }));
    }
}
