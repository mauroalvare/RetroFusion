using Retro.Cpu.I8088;

namespace I8088.Tests;

// Own programs; expected data intervals come from the local SingleStepTests/8088 v2 traces.
public sealed class StringTimingTests
{
    private sealed class Bus : I8088Bus
    {
        public byte[] Memory = new byte[1 << 20];
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address, byte value) => Memory[address] = value;
        public byte In(ushort port) => 0xFF;
        public void Out(ushort port, byte value) { }
    }
    private static (I8088Cpu Cpu, Bus Bus, List<I8088BusAccess> Data) Program(byte[] code, bool queued = true)
    {
        var bus = new Bus(); Array.Fill(bus.Memory, (byte)0x90, 0x1000, 64); code.CopyTo(bus.Memory, 0x1000);
        Array.Fill(bus.Memory, (byte)0x55, 0x2000, 64); Array.Fill(bus.Memory, (byte)0x55, 0x3000, 64);
        var cpu = new I8088Cpu(bus) { CS = 0x100, DS = 0x200, ES = 0x300, AX = 0x5555, CX = 5, TimingEnabled = true };
        if (queued) cpu.InitializePrefetchQueue(bus.Memory.AsSpan(0x1000, 4));
        var data = new List<I8088BusAccess>();
        cpu.BusAccessed += a => { if (a.Kind != I8088BusAccessKind.Code) data.Add(a); };
        return (cpu, bus, data);
    }

    [TestCase(0xA4, true, false, 7)]
    [TestCase(0xA5, true, false, 7)]
    [TestCase(0xA4, false, false, 8)]
    [TestCase(0xA5, false, false, 8)]
    [TestCase(0xA4, true, true, 8)]
    [TestCase(0xA5, true, true, 8)]
    [TestCase(0xA6, true, false, 8)]
    [TestCase(0xA7, true, false, 8)]
    [TestCase(0xA6, false, false, 8)]
    [TestCase(0xA7, false, false, 8)]
    public void SourceAndDestinationKeepTheMeasuredGap(int opcode, bool queued, bool segment, int gap)
    {
        var (cpu, _, data) = Program(segment ? [0x3E, (byte)opcode] : [(byte)opcode], queued);
        cpu.Step(); int width = 1 + (opcode & 1);
        Assert.That(data.Count, Is.EqualTo(2 * width));
        Assert.That(data[width].Clock - data[width - 1].Clock, Is.EqualTo(gap));
        if (width == 2)
        {
            Assert.That(data[1].Clock - data[0].Clock, Is.EqualTo(4));
            Assert.That(data[3].Clock - data[2].Clock, Is.EqualTo(4));
        }
    }

    [TestCase(0xA4, 10)]
    [TestCase(0xA5, 10)]
    [TestCase(0xA6, 14)]
    [TestCase(0xA7, 14)]
    [TestCase(0xAA, 10)]
    [TestCase(0xAB, 10)]
    [TestCase(0xAC, 13)]
    [TestCase(0xAD, 13)]
    [TestCase(0xAE, 15)]
    [TestCase(0xAF, 15)]
    public void RepIterationBoundariesDoNotChargeDecodedPrefixesAgain(int opcode, int gap)
    {
        foreach (bool segment in new[] { false, true })
        {
            byte[] code = segment ? [0x3E, 0xF3, (byte)opcode] : [0xF3, (byte)opcode];
            var (cpu, _, data) = Program(code);
            for (int i = 0; i < 5; i++) cpu.Step();
            int stride = (1 + (opcode & 1)) * (opcode is <= 0xA7 ? 2 : 1);
            Assert.That(data.Count, Is.EqualTo(5 * stride));
            for (int i = stride; i < data.Count; i += stride)
                Assert.That(data[i].Clock - data[i - 1].Clock, Is.EqualTo(gap), $"Opcode {opcode:X2}, segment={segment}, iteration={i / stride}");
            Assert.That(cpu.CX, Is.Zero);
            Assert.That(cpu.IP, Is.EqualTo(code.Length));
        }
    }

    [TestCase(0xA4)]
    [TestCase(0xA5)]
    [TestCase(0xA6)]
    [TestCase(0xA7)]
    [TestCase(0xAA)]
    [TestCase(0xAB)]
    [TestCase(0xAC)]
    [TestCase(0xAD)]
    [TestCase(0xAE)]
    [TestCase(0xAF)]
    public void EmptyRepTouchesNoDataAndKeepsRegisters(int opcode)
    {
        var (cpu, _, data) = Program([0xF3, (byte)opcode]); cpu.CX = 0; cpu.SI = 17; cpu.DI = 23;
        cpu.Step();
        Assert.That(data, Is.Empty);
        Assert.That((cpu.AX, cpu.CX, cpu.SI, cpu.DI, cpu.IP), Is.EqualTo(((ushort)0x5555, (ushort)0, (ushort)17, (ushort)23, (ushort)2)));
    }

    [TestCase(0xA4, 18)]
    [TestCase(0xA5, 26)]
    [TestCase(0xA6, 22)]
    [TestCase(0xA7, 30)]
    [TestCase(0xAA, 11)]
    [TestCase(0xAB, 15)]
    [TestCase(0xAC, 12)]
    [TestCase(0xAD, 16)]
    [TestCase(0xAE, 15)]
    [TestCase(0xAF, 19)]
    public void SingleStringKeepsItsNominalEuBudget(int opcode, int clocks)
    {
        var (cpu, _, _) = Program([(byte)opcode]); cpu.Step();
        Assert.That(cpu.Cycles, Is.EqualTo(clocks));
    }

    [TestCase(0xA4)]
    [TestCase(0xA5)]
    [TestCase(0xA6)]
    [TestCase(0xA7)]
    [TestCase(0xAA)]
    [TestCase(0xAB)]
    [TestCase(0xAC)]
    [TestCase(0xAD)]
    [TestCase(0xAE)]
    [TestCase(0xAF)]
    public void TimedRepKeepsFunctionalResultsWithBackwardWrappedOperands(int opcode)
    {
        foreach (var model in new[] { I8088Model.Intel8088, I8088Model.Intel8086 })
        {
            byte[] code = [0x36, 0xF3, (byte)opcode];
            var timed = Program(code, queued: false); var plain = Program(code, queued: false);
            // The shared string path must also preserve the 8086's functional state and split words.
            var first = new I8088Cpu(timed.Bus, model); var second = new I8088Cpu(plain.Bus, model);
            foreach (var cpu in new[] { first, second })
            {
                cpu.CS = 0x100; cpu.DS = 0x200; cpu.ES = 0x300; cpu.SS = 0x400;
                cpu.AX = 0x5555; cpu.CX = 3; cpu.SI = cpu.DI = 0xFFFF; cpu.Flags = I8088Cpu.Direction;
            }
            foreach (var bus in new[] { timed.Bus, plain.Bus })
                foreach (int offset in new[] { 0xFFFF, 0, 0xFFFE, 0xFFFD, 0xFFFC, 0xFFFB })
                    bus.Memory[0x4000 + offset] = bus.Memory[0x3000 + offset] = 0x55;
            first.TimingEnabled = true;
            for (int i = 0; i < 3; i++) { first.Step(); second.Step(); }
            Assert.That((first.AX, first.CX, first.SI, first.DI, first.IP, first.Flags),
                Is.EqualTo((second.AX, second.CX, second.SI, second.DI, second.IP, second.Flags)));
            Assert.That(timed.Bus.Memory, Is.EqualTo(plain.Bus.Memory));
            Assert.That(second.Cycles, Is.Zero);
        }
    }
}
