using Retro.Core;
using Retro.Cpu.I8088;

namespace I8088.Tests;

public sealed partial class CpuTests
{
    private static byte[] Snapshot(I8088Cpu cpu)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        cpu.Save(writer); return stream.ToArray();
    }
    private static void Restore(I8088Cpu cpu, byte[] state) => cpu.Load(new BinaryReader(new MemoryStream(state)));

    [Test]
    public void PhysicalDebuggerAddressesPreserveUsableSegmentsAndWrapAtTwentyBits()
    {
        var (cpu, _) = Program(0x90); cpu.CS = 0xFFFF; cpu.IP = 0;
        cpu.ProgramCounter = 5;
        Assert.That(cpu.CS, Is.EqualTo(0xFFFF)); Assert.That(cpu.IP, Is.EqualTo(0x15));
        cpu.ProgramCounter = 0x20000;
        Assert.That(cpu.PhysicalPC, Is.EqualTo(0x20000));
        Assert.That(cpu.TrySetRegister("ax", 0xCAFE), Is.True);
        Assert.That(cpu.AX, Is.EqualTo(0xCAFE));
        Assert.That(cpu.TrySetRegister("AX", 0x10000), Is.False);
        Assert.That(cpu.TrySetRegister("PC", 0x100000), Is.False);
        Assert.That(cpu.TrySetRegister("missing", 1), Is.False);
        Assert.That(cpu.TrySetFlag("CF", true), Is.True); Assert.That(cpu.Flag(I8088Cpu.Carry), Is.True);
        Assert.That(cpu.TrySetFlag("missing", true), Is.False);
        Assert.That(cpu.GetRegisters().Single(r => r.Name == "PC").Bits, Is.EqualTo(20));
        Assert.That(((ICpuDebugView)cpu).HasCycleTiming, Is.False);
        Assert.That(((ICpuDebugView)cpu).Cycles, Is.Zero, "Instruction counts must never be presented as a clock.");
    }

    [Test]
    public void CpuStateResumesAnInterruptShadowAndPendingIrqExactly()
    {
        var (cpu, bus) = Program(0xFB, 0x90, 0xF4);
        cpu.WriteWord(0, 0x80, 0x100); cpu.WriteWord(0, 0x82, 0x3000);
        cpu.RequestInterrupt(0x20); cpu.Step();
        byte[] state = Snapshot(cpu); var otherBus = new Bus(); bus.Memory.CopyTo(otherBus.Memory, 0);
        var other = new I8088Cpu(otherBus); Restore(other, state);
        cpu.Step(); other.Step();
        Assert.That(cpu.CS, Is.EqualTo(0x1000), "The saved STI shadow must protect NOP.");
        Assert.That(Snapshot(other), Is.EqualTo(Snapshot(cpu)));
        cpu.Step(); other.Step();
        Assert.That(other.CS, Is.EqualTo(0x3000)); Assert.That(other.IP, Is.EqualTo(0x100));
        Assert.That(Snapshot(other), Is.EqualTo(Snapshot(cpu)));
        Assert.That(otherBus.Memory, Is.EqualTo(bus.Memory));
    }

    [Test]
    public void CpuStatePreservesHaltAndQueuedNmiAndRejectsTruncationWithoutMutation()
    {
        var (cpu, bus) = Program(0xF4); cpu.Step(); cpu.RequestNmi();
        byte[] state = Snapshot(cpu); var other = new I8088Cpu(bus); Restore(other, state);
        Assert.That(other.Halted, Is.True);
        Assert.That(other.GetInterruptState().Any(s => s.Contains("NMI=pendiente")), Is.True);
        Assert.That(other.Step(), Is.True); Assert.That(other.Halted, Is.False);
        var before = Snapshot(other);
        foreach (int length in new[] { 0, 3, state.Length - 1 })
        {
            Assert.Catch<IOException>(() => Restore(other, state[..length]));
            Assert.That(Snapshot(other), Is.EqualTo(before));
        }
    }

    [Test]
    public void WaitHoldsAtTestHighWithoutRetiringATrapAndResumesWhenTestFalls()
    {
        var (cpu, bus) = Program(0x9B, 0x90); bus.TestSignal = true; cpu.Flags = I8088Cpu.Trap;
        cpu.Step(); Assert.That(cpu.IP, Is.Zero); Assert.That(cpu.TrapPending, Is.False);
        byte[] state = Snapshot(cpu); Restore(cpu, state);
        bus.TestSignal = false; cpu.Step();
        Assert.That(cpu.IP, Is.EqualTo(1)); Assert.That(cpu.TrapPending, Is.True);
    }

    [Test]
    public void WaitCanBeInterruptedAndTheReturnPointsToTheWaitingInstruction()
    {
        var (cpu, bus) = Program(0x9B); bus.TestSignal = true; cpu.Flags = I8088Cpu.InterruptEnable;
        cpu.Step(); cpu.WriteWord(0, 0x80, 0x100); cpu.WriteWord(0, 0x82, 0x3000);
        cpu.RequestInterrupt(0x20); cpu.Step();
        Assert.That(cpu.CS, Is.EqualTo(0x3000)); Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.Zero);
    }

    [Test]
    public void EscWithoutCoprocessorReadsMemoryOperandButDoesNotInventAnFpuResult()
    {
        var (cpu, bus) = Program(0x26, 0xD9, 0x06, 0xFF, 0xFF); cpu.ES = 0x4000;
        bus.TraceReads = true; cpu.AX = 0x1234; cpu.Step();
        Assert.That(bus.Reads.TakeLast(2), Is.EqualTo(new uint[] { 0x4FFFF, 0x40000 }));
        Assert.That(cpu.AX, Is.EqualTo(0x1234)); Assert.That(cpu.IP, Is.EqualTo(5));
    }

    [TestCase(0xF0)] [TestCase(0xF1)]
    public void LockIsReleasedEvenWhenTheInstructionFails(int prefix)
    {
        var (cpu, bus) = Program((byte)prefix, 0x60);
        Assert.Throws<NotSupportedException>(() => cpu.Step());
        Assert.That(bus.Locks, Is.EqualTo(new[] { true, false }));
    }
    [Test]
    public void MemoryXchgLocksTheBusWithoutAnExplicitPrefix()
    {
        var (cpu, bus) = Program(0x87, 0x06, 0x10, 0); cpu.DS = 0x4000; cpu.AX = 0x1234;
        cpu.WriteWord(cpu.DS, 0x10, 0x5678); cpu.Step();
        Assert.That(cpu.AX, Is.EqualTo(0x5678)); Assert.That(cpu.ReadWord(cpu.DS, 0x10), Is.EqualTo(0x1234));
        Assert.That(bus.Locks, Is.EqualTo(new[] { true, false }));
    }
}