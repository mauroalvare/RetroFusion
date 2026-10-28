using Retro.Cpu.I8088;

namespace I8088.Tests;

// Own programs with relative intervals measured on SingleStepTests/8088 v2.
public sealed class PointerStackTimingTests
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
        new byte[] { 0x34, 0x12, 0x78, 0x56 }.CopyTo(bus.Memory, 0x2020);
        new byte[] { 0xCD, 0xAB }.CopyTo(bus.Memory, 0x3100);
        var cpu = new I8088Cpu(bus, model) { CS = 0x100, DS = 0x200, SS = 0x300, SI = 0x20, SP = 0x100, TimingEnabled = true };
        if (queued) cpu.InitializePrefetchQueue(bus.Memory.AsSpan(0x1000, 4));
        var data = new List<I8088BusAccess>();
        cpu.BusAccessed += a => { if (a.Kind != I8088BusAccessKind.Code) data.Add(a); };
        return (cpu, bus, data);
    }

    [TestCase(0xC4, 0x04, true, 14)]
    [TestCase(0xC5, 0x04, true, 14)]
    [TestCase(0xFF, 0x34, true, 15)]
    [TestCase(0xC4, 0x04, false, 14)]
    [TestCase(0xC5, 0x04, false, 14)]
    [TestCase(0xFF, 0x34, false, 16)]
    public void WordPairsKeepTheirMeasuredSpacing(int opcode, int modrm, bool queued, int secondPair)
    {
        var (cpu, _, data) = Program([(byte)opcode, (byte)modrm], queued);
        cpu.Step();
        Assert.That(data.Select(a => a.Clock - data[0].Clock), Is.EqualTo(new long[] { 0, 4, secondPair, secondPair + 4 }));
        Assert.That(data.Select(a => a.Kind), Is.EqualTo(new[] { I8088BusAccessKind.MemoryRead, I8088BusAccessKind.MemoryRead,
            opcode is 0xC4 or 0xC5 ? I8088BusAccessKind.MemoryRead : I8088BusAccessKind.MemoryWrite,
            opcode is 0xC4 or 0xC5 ? I8088BusAccessKind.MemoryRead : I8088BusAccessKind.MemoryWrite }));
    }

    [TestCase(0xC4, 0x04, 29)]
    [TestCase(0xC5, 0x04, 29)]
    [TestCase(0xFF, 0x34, 29)]
    public void MovingEuWorkKeepsTheNominalInstructionBudget(int opcode, int modrm, int clocks)
    {
        var (cpu, _, _) = Program([(byte)opcode, (byte)modrm]); cpu.Step();
        Assert.That(cpu.Cycles, Is.EqualTo(clocks));
    }

    [TestCase(0xC4)]
    [TestCase(0xC5)]
    public void FarPointerWrapsBeforeReplacingItsSourceSegmentOrOffset(int opcode)
    {
        foreach (var model in new[] { I8088Model.Intel8088, I8088Model.Intel8086 })
        {
            // LES/LDS SI,[SI] must finish reading the original DS:SI pointer first.
            var (cpu, bus, data) = Program([(byte)opcode, 0x34], model: model); cpu.SI = 0xFFFF;
            bus.Memory[0x11FFF] = 0x34; bus.Memory[0x2000] = 0x12;
            bus.Memory[0x2001] = 0x78; bus.Memory[0x2002] = 0x56;
            cpu.Step();
            Assert.That(data.Select(a => a.Address), Is.EqualTo(new uint[] { 0x11FFF, 0x2000, 0x2001, 0x2002 }));
            Assert.That(cpu.SI, Is.EqualTo(0x1234));
            Assert.That(opcode == 0xC4 ? cpu.ES : cpu.DS, Is.EqualTo(0x5678));
        }
    }

    [TestCase(0x8F, 0x04)]
    [TestCase(0xFF, 0x34)]
    public void MemoryStackTransfersKeepValuesWhenSourceAndStackAlias(int opcode, int modrm)
    {
        foreach (var model in new[] { I8088Model.Intel8088, I8088Model.Intel8086 })
        {
            var timed = Program([(byte)opcode, (byte)modrm], model: model);
            var plain = Program([(byte)opcode, (byte)modrm], model: model); plain.Cpu.TimingEnabled = false;
            foreach (var (cpu, bus, _) in new[] { timed, plain })
            {
                cpu.DS = cpu.SS; cpu.SI = 0xFFFF; cpu.SP = opcode == 0x8F ? (ushort)0xFFFF : (ushort)1;
                bus.Memory[0x12FFF] = 0xA5; bus.Memory[0x3000] = 0x5A; cpu.Step();
            }
            Assert.That(timed.Cpu.SP, Is.EqualTo(plain.Cpu.SP));
            Assert.That(timed.Cpu.SP, Is.EqualTo(opcode == 0x8F ? 1 : 0xFFFF));
            Assert.That(timed.Bus.Memory[0x12FFF], Is.EqualTo(0xA5));
            Assert.That(timed.Bus.Memory[0x3000], Is.EqualTo(0x5A));
            Assert.That(timed.Bus.Memory, Is.EqualTo(plain.Bus.Memory));
        }
    }

    [TestCase(0x8F, 0xC4, 12, 0xABCD)]
    [TestCase(0xFF, 0xF4, 15, 0x00FE)]
    public void RegisterSpFormsKeepTheirTimingAndOriginalStackSemantics(int opcode, int modrm, int clocks, int sp)
    {
        var (cpu, bus, data) = Program([(byte)opcode, (byte)modrm]); cpu.Step();
        Assert.That(cpu.SP, Is.EqualTo(sp));
        Assert.That(cpu.Cycles, Is.EqualTo(clocks));
        Assert.That(data.Count, Is.EqualTo(2));
        if (opcode == 0xFF)
        {
            Assert.That(bus.Memory[0x30FE], Is.EqualTo(0xFE));
            Assert.That(bus.Memory[0x30FF], Is.Zero);
        }
    }
}
