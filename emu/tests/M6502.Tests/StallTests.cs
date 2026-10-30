using Retro.Cpu.M6502;

namespace Cpu6502.Tests;

public sealed class StallTests
{
    private sealed class Bus : IM6502Bus
    {
        public byte[] Memory = new byte[65536];
        public bool IrqLine { get; set; }
        public bool NmiLine { get; set; }
        public int Accesses;
        public byte Read(ushort address) { Accesses++; return Memory[address]; }
        public void Write(ushort address, byte value) { Accesses++; Memory[address] = value; }
    }
    [Test]
    public void StolenClocksDoNotExecuteInstructionsOrAccessCpuMemory()
    {
        var bus = new Bus(); var cpu = new M6502(bus) { PC = 0x1000, A = 0x42 };
        for (int n = 0; n < 8; n++) cpu.ClockStall();
        Assert.That(cpu.CycleCount, Is.EqualTo(8)); Assert.That(cpu.Instructions, Is.Zero);
        Assert.That(cpu.PC, Is.EqualTo(0x1000)); Assert.That(cpu.A, Is.EqualTo(0x42)); Assert.That(bus.Accesses, Is.Zero);
    }
    [Test]
    public void TransientNmiDuringDmaSurvivesCpuSnapshotAndLaterExecution()
    {
        var bus = new Bus(); bus.Memory[0xFFFA] = 0; bus.Memory[0xFFFB] = 0x20;
        var cpu = new M6502(bus) { PC = 0x1000 };
        bus.NmiLine = true; cpu.ClockStall(); bus.NmiLine = false; cpu.ClockStall();
        using var state = new MemoryStream(); using var writer = new BinaryWriter(state);
        cpu.Save(writer); writer.Flush(); state.Position = 0;
        var clone = new M6502(bus); clone.Load(new BinaryReader(state)); clone.Step();
        Assert.That(clone.PC, Is.EqualTo(0x2000)); Assert.That(clone.CycleCount, Is.EqualTo(9));
        Assert.That(clone.Instructions, Is.Zero);
    }
}
