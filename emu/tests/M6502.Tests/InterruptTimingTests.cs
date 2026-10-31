using Retro.Cpu.M6502;

namespace Cpu6502.Tests;

public sealed class InterruptTimingTests
{
    private sealed class Bus : IM6502Bus
    {
        public readonly byte[] Memory = new byte[65536];
        public readonly List<(ushort Address, bool Read)> Accesses = [];
        public Func<int, bool> IrqAtCycle = _ => false;
        public Func<int, bool> NmiAtCycle = _ => false;
        public bool IrqLine => IrqAtCycle(Accesses.Count);
        public bool NmiLine => NmiAtCycle(Accesses.Count);
        public byte Read(ushort address) { Accesses.Add((address, true)); return Memory[address]; }
        public void Write(ushort address, byte value) { Accesses.Add((address, false)); Memory[address] = value; }
    }

    // The Visual6502 branch timing has two polling points: after the opcode fetch,
    // and (only when crossing a page) before the final PCH fixup read.
    // https://www.nesdev.org/wiki/Visual6502wiki/6502_Timing_of_Interrupt_Handling
    [TestCase(false, 1, true)]
    [TestCase(false, 2, false)]
    [TestCase(false, 3, false)]
    [TestCase(true, 1, true)]
    [TestCase(true, 2, false)]
    [TestCase(true, 3, true)]
    [TestCase(true, 4, false)]
    public void TakenBranchPollsIrqAtItsActualPollingPoints(bool crossesPage, int pulseCycle, bool accepted)
    {
        foreach (byte opcode in new byte[] { 0x10, 0x30, 0x50, 0x70, 0x90, 0xB0, 0xD0, 0xF0 })
        {
            var bus = new Bus { IrqAtCycle = cycle => cycle == pulseCycle };
            ushort start = crossesPage ? (ushort)0x20FD : (ushort)0x2000;
            ushort target = crossesPage ? (ushort)0x2101 : (ushort)0x2004;
            bus.Memory[start] = opcode; bus.Memory[start + 1] = 2;
            bus.Memory[target] = 0xEA;
            bus.Memory[0xFFFE] = 0; bus.Memory[0xFFFF] = 0x40;
            byte flag = opcode switch { 0x30 => M6502.FlagN, 0x70 => M6502.FlagV,
                0xB0 => M6502.FlagC, 0xF0 => M6502.FlagZ, _ => 0 };
            var cpu = new M6502(bus) { PC = start, S = 0xFF, P = (byte)(M6502.FlagU | flag) };

            cpu.Step();
            Assert.That(cpu.PC, Is.EqualTo(target), $"branch {opcode:X2}");
            Assert.That(cpu.CycleCount, Is.EqualTo(crossesPage ? 4 : 3));
            int before = bus.Accesses.Count;
            cpu.Step();
            Assert.That(cpu.PC, Is.EqualTo(accepted ? 0x4000 : target + 1), $"IRQ after {opcode:X2}");
            Assert.That(bus.Accesses.Count - before, Is.EqualTo(accepted ? 7 : 2));
            if (accepted)
            {
                Assert.That(bus.Memory[0x01FF], Is.EqualTo(target >> 8));
                Assert.That(bus.Memory[0x01FE], Is.EqualTo(target & 0xFF));
                Assert.That(bus.Memory[0x01FD] & M6502.FlagB, Is.Zero);
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BranchIrqDecisionSurvivesSnapshotAndResetDiscardsIt(bool reset)
    {
        var bus = new Bus { IrqAtCycle = cycle => cycle == 1 };
        bus.Memory[0x2000] = 0xD0; bus.Memory[0x2001] = 2;
        bus.Memory[0x2004] = 0xEA;
        bus.Memory[0xFFFE] = 0; bus.Memory[0xFFFF] = 0x40;
        bus.Memory[0xFFFC] = 0; bus.Memory[0xFFFD] = 0x50;
        bus.Memory[0x5000] = 0xEA;
        var cpu = new M6502(bus) { PC = 0x2000, P = M6502.FlagU };
        cpu.Step();

        using var state = new MemoryStream();
        using var writer = new BinaryWriter(state);
        cpu.Save(writer); writer.Flush(); state.Position = 0;
        var restored = new M6502(bus);
        restored.Load(new BinaryReader(state));
        Assert.That(restored.CycleCount, Is.EqualTo(3));
        if (reset) restored.Reset();
        restored.Step();
        Assert.That(restored.PC, Is.EqualTo(reset ? 0x5001 : 0x4000));
        Assert.That(restored.CycleCount, Is.EqualTo(reset ? 12 : 10));
    }

    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(4, false)]
    public void NmiOnTheFinalInstructionCycleWaitsForAnotherInstruction(int pulseCycle, bool accepted)
    {
        var bus = new Bus { NmiAtCycle = cycle => cycle == pulseCycle };
        // LDA $3000; NOP. NMI vector $6000.
        new byte[] { 0xAD, 0, 0x30, 0xEA }.CopyTo(bus.Memory, 0x2000);
        bus.Memory[0xFFFA] = 0; bus.Memory[0xFFFB] = 0x60;
        var cpu = new M6502(bus) { PC = 0x2000 };
        cpu.Step(); cpu.Step();
        Assert.That(cpu.PC, Is.EqualTo(accepted ? 0x6000 : 0x2004));
        Assert.That(cpu.CycleCount, Is.EqualTo(accepted ? 11 : 6));
        if (!accepted)
        {
            int before = bus.Accesses.Count;
            cpu.Step(); Assert.That(cpu.PC, Is.EqualTo(0x6000));
            Assert.That(cpu.CycleCount, Is.EqualTo(13));
            Assert.That(bus.Accesses.Skip(before), Is.EqualTo(new[]
            {
                ((ushort)0x2004, true), ((ushort)0x2004, true),
                ((ushort)0x01FD, false), ((ushort)0x01FC, false), ((ushort)0x01FB, false),
                ((ushort)0xFFFA, true), ((ushort)0xFFFB, true),
            }));
            Assert.That(bus.Memory[0x01FD], Is.EqualTo(0x20));
            Assert.That(bus.Memory[0x01FC], Is.EqualTo(4));
            Assert.That(bus.Memory[0x01FB] & 0x30, Is.EqualTo(M6502.FlagU));
        }
    }

    [TestCase(false, 1, true)]
    [TestCase(false, 2, false)]
    [TestCase(false, 3, false)]
    [TestCase(true, 1, true)]
    [TestCase(true, 2, true)]
    [TestCase(true, 3, true)]
    [TestCase(true, 4, false)]
    public void TakenBranchUsesItsNmiPollingPoints(bool crossesPage, int pulseCycle, bool accepted)
    {
        var bus = new Bus { NmiAtCycle = cycle => cycle == pulseCycle };
        ushort start = crossesPage ? (ushort)0x20FD : (ushort)0x2000;
        ushort target = crossesPage ? (ushort)0x2101 : (ushort)0x2004;
        bus.Memory[start] = 0xD0; bus.Memory[start + 1] = 2; bus.Memory[target] = 0xEA;
        bus.Memory[0xFFFA] = 0; bus.Memory[0xFFFB] = 0x60;
        var cpu = new M6502(bus) { PC = start, P = M6502.FlagU };
        cpu.Step(); cpu.Step();
        Assert.That(cpu.PC, Is.EqualTo(accepted ? 0x6000 : target + 1));
        if (!accepted) { cpu.Step(); Assert.That(cpu.PC, Is.EqualTo(0x6000)); }
    }

    [TestCase(false, 6)]
    [TestCase(false, 7)]
    [TestCase(true, 8)]
    [TestCase(true, 9)]
    public void LateNmiDuringBrkOrIrqRunsOneHandlerInstructionBeforeNesting(bool irq, int pulseCycle)
    {
        var bus = new Bus { NmiAtCycle = cycle => cycle == pulseCycle, IrqAtCycle = _ => irq };
        bus.Memory[0x2000] = irq ? (byte)0xEA : (byte)0x00;
        bus.Memory[0x4000] = 0xEA;
        bus.Memory[0xFFFE] = 0; bus.Memory[0xFFFF] = 0x40;
        bus.Memory[0xFFFA] = 0; bus.Memory[0xFFFB] = 0x60;
        var cpu = new M6502(bus) { PC = 0x2000, P = M6502.FlagU };
        cpu.Step(); if (irq) cpu.Step();
        Assert.That(cpu.PC, Is.EqualTo(0x4000));
        cpu.Step(); Assert.That(cpu.PC, Is.EqualTo(0x4001));
        cpu.Step(); Assert.That(cpu.PC, Is.EqualTo(0x6000));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SnapshotKeepsLateNmiPendingUntilItsPollingPointAndResetClearsIt(bool reset)
    {
        var bus = new Bus { NmiAtCycle = cycle => cycle == 2 };
        bus.Memory[0x2000] = bus.Memory[0x2001] = bus.Memory[0x5000] = 0xEA;
        bus.Memory[0xFFFA] = 0; bus.Memory[0xFFFB] = 0x60;
        bus.Memory[0xFFFC] = 0; bus.Memory[0xFFFD] = 0x50;
        var cpu = new M6502(bus) { PC = 0x2000 };
        cpu.Step();
        using var state = new MemoryStream(); using var writer = new BinaryWriter(state);
        cpu.Save(writer); writer.Flush(); state.Position = 0;
        var restored = new M6502(bus); restored.Load(new BinaryReader(state));
        if (reset) restored.Reset();
        restored.Step(); Assert.That(restored.PC, Is.EqualTo(reset ? 0x5001 : 0x2002));
        if (!reset) { restored.Step(); Assert.That(restored.PC, Is.EqualTo(0x6000)); }
    }

    [Test]
    public void LegacyCpuStateRetainsItsPendingNmiDecisionWithoutConsumingTheFollowingPayload()
    {
        var bus = new Bus { NmiAtCycle = cycle => cycle == 2 };
        bus.Memory[0x2000] = 0xEA; bus.Memory[0xFFFA] = 0; bus.Memory[0xFFFB] = 0x60;
        var cpu = new M6502(bus) { PC = 0x2000 }; cpu.Step();
        using var current = new MemoryStream(); using var writer = new BinaryWriter(current);
        cpu.Save(writer); writer.Flush();
        byte[] legacy = [..current.ToArray()[..^1], 0xA5];
        using var reader = new BinaryReader(new MemoryStream(legacy));
        var restored = new M6502(bus); restored.Load(reader, hasNmiPoll: false);
        Assert.That(reader.ReadByte(), Is.EqualTo(0xA5));
        restored.Step(); Assert.That(restored.PC, Is.EqualTo(0x6000));
    }
}
