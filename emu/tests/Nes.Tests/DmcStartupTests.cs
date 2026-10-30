using System.Buffers.Binary;
using Retro.Systems.Nes;
using NesMachine = Retro.Systems.Nes.Nes;

namespace Nes.Tests;

public sealed class DmcStartupTests
{
    private static NesMachine OwnMachine(bool oddWrite)
    {
        var rom = new byte[16 + 16384 + 8192]; "NES\x1A"u8.CopyTo(rom); rom[4] = rom[5] = 1;
        byte[] program = oddWrite ? [0x24, 0, 0x8D, 0x15, 0x40, 0xEA, 0xEA] : [0x8D, 0x15, 0x40, 0xEA, 0xEA];
        program.CopyTo(rom, 16); rom[16 + 0x3FFD] = 0x80;
        var machine = new NesMachine(NesCartridge.Load(rom));
        machine.Apu.Write(0x4010, 0x80); machine.Apu.Write(0x4012, 0); machine.Apu.Write(0x4013, 0);
        machine.Cpu2A03.A = 0x10;
        if (oddWrite) machine.StepInstruction();
        return machine;
    }

    [TestCase(false, 5, 2)] [TestCase(true, 2, 5)]
    public void ActivationWaitsForItsLoadDmaPhase(bool oddWrite, int firstNop, int secondNop)
    {
        var machine = OwnMachine(oddWrite); long before = machine.Cpu2A03.CycleCount;
        machine.StepInstruction();
        Assert.That(machine.Cpu2A03.CycleCount - before, Is.EqualTo(4));
        Assert.That(machine.Apu.Status & 0x90, Is.EqualTo(0x10), "La escritura todavía no leyó el sample.");
        before = machine.Cpu2A03.CycleCount; machine.StepInstruction();
        Assert.That(machine.Cpu2A03.CycleCount - before, Is.EqualTo(firstNop));
        before = machine.Cpu2A03.CycleCount; machine.StepInstruction();
        Assert.That(machine.Cpu2A03.CycleCount - before, Is.EqualTo(secondNop));
        Assert.That(machine.Apu.Status & 0x90, Is.EqualTo(0x80));
    }

    [TestCase(false)] [TestCase(true)]
    public void SaveDuringActivationResumesTheSameBusPhase(bool oddWrite)
    {
        var machine = OwnMachine(oddWrite); machine.StepInstruction(); byte[] state = machine.SaveState();
        var restored = OwnMachine(oddWrite); restored.LoadState(state);
        for (int i = 0; i < 2; i++) { machine.StepInstruction(); restored.StepInstruction(); }
        Assert.That(restored.SaveState(), Is.EqualTo(machine.SaveState()));
    }

    private static void Version(byte[] state, int version)
    {
        using var stream = new MemoryStream(state); using var reader = new BinaryReader(stream); reader.ReadString();
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan((int)stream.Position, 4), version);
    }

    [Test]
    public void PreviousVersionFourLoadsWithItsImmediateRequestSemantics()
    {
        var machine = OwnMachine(false); machine.StepInstruction();
        byte[] old = machine.SaveState()[..^4]; Version(old, 4);
        var restored = OwnMachine(false); restored.LoadState(old); long before = restored.Cpu2A03.CycleCount;
        restored.StepInstruction(); Assert.That(restored.Cpu2A03.CycleCount - before, Is.EqualTo(6));
    }

    [TestCase(-1)] [TestCase(4)]
    public void InvalidActivationDelayIsRejected(int delay)
    {
        var machine = OwnMachine(false); machine.StepInstruction(); byte[] state = machine.SaveState();
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(^4), delay);
        Assert.Throws<InvalidDataException>(() => machine.LoadState(state));
    }
}
