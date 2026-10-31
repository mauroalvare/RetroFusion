using Retro.Cpu.Z80;
namespace Retro.Cpu.Z80.Tests;

public sealed class NmiBusTests
{
    private sealed class Bus : SimpleZ80Bus
    {
        public readonly List<(char Kind, ushort Address)> Accesses = [];
        public int Wait;
        public override int Contention(ushort address, long tstate) => address == 0x4000 ? Wait : 0;
        public override byte ReadMemory(ushort address) { Accesses.Add(('R', address)); return base.ReadMemory(address); }
        public override void WriteMemory(ushort address, byte value) { Accesses.Add(('W', address)); base.WriteMemory(address, value); }
    }
    [TestCase(false, 0)] [TestCase(false, 3)] [TestCase(true, 0)] [TestCase(true, 3)]
    public void NmiPerformsAnIgnoredM1ReadAndHonorsItsWaitBeforeStackingTheReturnAddress(bool halted, int wait)
    {
        var bus = new Bus { Wait = wait }; bus.Memory[0x4000] = 0x37; // SCF must not execute.
        var cpu = new Z80Cpu(bus) { PC = halted ? (ushort)0x3FFF : (ushort)0x4000, SP = 0x9000, F = 0, R = 0xFF, IFF1 = true, IFF2 = true };
        if (halted) { bus.Memory[0x3FFF] = 0x76; cpu.Step(); bus.Accesses.Clear(); cpu.R = 0xFF; }
        cpu.RaiseNmi();
        int clocks = cpu.Step();
        Assert.That(clocks, Is.EqualTo(11 + wait));
        Assert.That(bus.Accesses, Is.EqualTo(new[] { ('R', (ushort)0x4000), ('W', (ushort)0x8FFF), ('W', (ushort)0x8FFE) }));
        Assert.That(cpu.PC, Is.EqualTo(0x66)); Assert.That(cpu.SP, Is.EqualTo(0x8FFE));
        Assert.That((bus.Memory[0x8FFE], bus.Memory[0x8FFF]), Is.EqualTo(((byte)0, (byte)0x40)));
        Assert.That(cpu.R, Is.EqualTo(0x80)); Assert.That(cpu.F, Is.Zero);
        Assert.That(cpu.IFF1, Is.False); Assert.That(cpu.IFF2, Is.True); Assert.That(cpu.Halted, Is.False);
    }
}
