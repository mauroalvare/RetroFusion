using Retro.Systems.C64;

namespace Commodore.Tests;

public sealed class RdyTests
{
    private static C64 Waiting()
    {
        var machine = new C64(MachineTests.Firmware());
        machine.Ram[0x0800] = machine.Ram[0x0801] = 0xEA; machine.Cpu6510.PC = 0x0800;
        machine.Vic.Write(0x11, 0x10);
        for (int n = 0; !machine.Vic.BaLow; n++)
        { Assert.That(n, Is.LessThan(Vic.CyclesPerLine * Vic.Lines)); machine.Vic.Tick(); }
        return machine;
    }
    private static int Position(C64 machine) => machine.Vic.Raster * Vic.CyclesPerLine + machine.Vic.Cycle;
    [Test]
    public void ReadWaitsAdvanceCpuClockTogetherWithVic()
    {
        var machine = Waiting(); int video = Position(machine); long cpu = machine.Cpu6510.CycleCount;
        machine.StepInstruction();
        Assert.That(machine.Cpu6510.CycleCount - cpu, Is.GreaterThan(2));
        Assert.That(machine.Cpu6510.CycleCount - cpu, Is.EqualTo(Position(machine) - video));
        Assert.That(machine.Cpu6510.PC, Is.EqualTo(0x0801));
    }
    [Test]
    public void WriteDoesNotWaitForRdy()
    {
        var machine = Waiting(); int video = Position(machine);
        machine.Write(0x1000, 0x55);
        Assert.That(Position(machine) - video, Is.EqualTo(1)); Assert.That(machine.Ram[0x1000], Is.EqualTo(0x55));
    }
}
