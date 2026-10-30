using Retro.Cpu.Z80;

namespace Retro.Cpu.Z80.Tests;

public sealed class InterruptModeZeroTests
{
    private sealed class Bus : SimpleZ80Bus
    {
        public byte Opcode;
        public int Acknowledges;
        public List<(char Kind, ushort Address, byte Value)> Accesses = [];
        public override byte InterruptDataBus { get { Acknowledges++; return Opcode; } }
        public override byte ReadMemory(ushort address)
        { byte value = base.ReadMemory(address); Accesses.Add(('R', address, value)); return value; }
        public override void WriteMemory(ushort address, byte value)
        { base.WriteMemory(address, value); Accesses.Add(('W', address, value)); }
    }
    private static (Z80Cpu Cpu, Bus Bus) Setup(int opcode, params byte[] following)
    {
        var bus = new Bus { Opcode = (byte)opcode }; following.CopyTo(bus.Memory, 0x2000);
        var cpu = new Z80Cpu(bus) { PC = 0x2000, SP = 0x4000, IM = 0, IntLine = true,
            IFF1 = true, IFF2 = true, WZ = 0xBEEF, R = 0xFF };
        return (cpu, bus);
    }
    [TestCase(0xC7, 0)] [TestCase(0xCF, 8)] [TestCase(0xD7, 16)] [TestCase(0xDF, 24)]
    [TestCase(0xE7, 32)] [TestCase(0xEF, 40)] [TestCase(0xF7, 48)] [TestCase(0xFF, 56)]
    public void DeviceCanSelectAnyRestartVector(int opcode, int vector)
    {
        var (cpu, bus) = Setup(opcode); Assert.That(cpu.Step(), Is.EqualTo(13));
        Assert.That(cpu.PC, Is.EqualTo(vector)); Assert.That(cpu.WZ, Is.EqualTo(vector));
        Assert.That(cpu.SP, Is.EqualTo(0x3FFE)); Assert.That(bus.Memory[0x3FFE], Is.Zero);
        Assert.That(bus.Memory[0x3FFF], Is.EqualTo(0x20));
        Assert.That(bus.Acknowledges, Is.EqualTo(1)); Assert.That(cpu.R, Is.EqualTo(0x80));
        Assert.That(cpu.IFF1 || cpu.IFF2, Is.False);
    }
    [Test]
    public void NopDoesNotReadAnOpcodeFromRamOrPushAFrame()
    {
        var (cpu, bus) = Setup(0); cpu.Q = 0x28;
        Assert.That(cpu.Step(), Is.EqualTo(6)); Assert.That(cpu.PC, Is.EqualTo(0x2000));
        Assert.That(cpu.SP, Is.EqualTo(0x4000)); Assert.That(bus.Accesses, Is.Empty);
        Assert.That(cpu.WZ, Is.EqualTo(0xBEEF)); Assert.That(cpu.Q, Is.Zero);
        Assert.That(bus.Acknowledges, Is.EqualTo(1));
    }
    [Test]
    public void ImmediateOperandStartsAtTheInterruptedPc()
    {
        var (cpu, bus) = Setup(0x3E, 0x42);
        Assert.That(cpu.Step(), Is.EqualTo(9)); Assert.That(cpu.A, Is.EqualTo(0x42));
        Assert.That(cpu.PC, Is.EqualTo(0x2001));
        Assert.That(bus.Accesses, Is.EqualTo(new[] { ('R', (ushort)0x2000, (byte)0x42) }));
    }
    [Test]
    public void InjectedCallPushesTheReturnAfterItsTwoMemoryOperands()
    {
        var (cpu, bus) = Setup(0xCD, 0x34, 0x12);
        Assert.That(cpu.Step(), Is.EqualTo(19)); Assert.That(cpu.PC, Is.EqualTo(0x1234));
        Assert.That(bus.Accesses, Is.EqualTo(new[] { ('R', (ushort)0x2000, (byte)0x34), ('R', (ushort)0x2001, (byte)0x12),
            ('W', (ushort)0x3FFF, (byte)0x20), ('W', (ushort)0x3FFE, (byte)2) }));
    }
    [Test]
    public void InjectedJumpReadsItsTargetWithoutWritingTheStack()
    {
        var (cpu, bus) = Setup(0xC3, 0x78, 0x56);
        Assert.That(cpu.Step(), Is.EqualTo(12)); Assert.That(cpu.PC, Is.EqualTo(0x5678));
        Assert.That(cpu.SP, Is.EqualTo(0x4000)); Assert.That(bus.Accesses.All(a => a.Kind == 'R'), Is.True);
    }
    [Test]
    public void InjectedReturnReadsTheExistingStack()
    {
        var (cpu, bus) = Setup(0xC9); bus.Memory[0x4000] = 0x34; bus.Memory[0x4001] = 0x12;
        Assert.That(cpu.Step(), Is.EqualTo(12)); Assert.That(cpu.PC, Is.EqualTo(0x1234)); Assert.That(cpu.SP, Is.EqualTo(0x4002));
    }
    [Test]
    public void ArithmeticUpdatesFlagsAndQAsAnOrdinaryInstruction()
    {
        var (cpu, _) = Setup(0x80); cpu.A = 0x7F; cpu.B = 1;
        Assert.That(cpu.Step(), Is.EqualTo(6)); Assert.That(cpu.A, Is.EqualTo(0x80));
        Assert.That(cpu.F, Is.EqualTo(Z80Cpu.FS | Z80Cpu.FH | Z80Cpu.FV)); Assert.That(cpu.Q, Is.EqualTo(cpu.F));
    }
    [TestCase(0xCB, new byte[] { 0 }, 10, 1)]
    [TestCase(0xED, new byte[] { 0x44 }, 10, 1)]
    [TestCase(0xDD, new byte[] { 0x21, 0x34, 0x12 }, 16, 3)]
    [TestCase(0xFD, new byte[] { 0x21, 0x34, 0x12 }, 16, 3)]
    public void PrefixTailIsFetchedFromMemoryAndIncrementsRefresh(int prefix, byte[] tail, int clocks, int consumed)
    {
        var (cpu, bus) = Setup(prefix, tail); cpu.A = 1; cpu.B = 0x80;
        Assert.That(cpu.Step(), Is.EqualTo(clocks)); Assert.That(cpu.PC, Is.EqualTo(0x2000 + consumed));
        Assert.That(cpu.R, Is.EqualTo(0x81)); Assert.That(bus.Acknowledges, Is.EqualTo(1));
        if (prefix == 0xCB) { Assert.That(cpu.B, Is.EqualTo(1)); Assert.That(cpu.F & Z80Cpu.FC, Is.EqualTo(Z80Cpu.FC)); }
        if (prefix == 0xED) Assert.That(cpu.A, Is.EqualTo(0xFF));
        if (prefix == 0xDD) Assert.That(cpu.IX, Is.EqualTo(0x1234));
        if (prefix == 0xFD) Assert.That(cpu.IY, Is.EqualTo(0x1234));
    }
    [Test]
    public void InjectedEiStillProtectsTheFollowingMemoryInstruction()
    {
        var (cpu, bus) = Setup(0xFB, 0);
        Assert.That(cpu.Step(), Is.EqualTo(6)); Assert.That(cpu.InterruptsAccepted, Is.False);
        bus.Opcode = 0; Assert.That(cpu.Step(), Is.EqualTo(4)); Assert.That(bus.Acknowledges, Is.EqualTo(1));
        Assert.That(cpu.PC, Is.EqualTo(0x2001)); Assert.That(cpu.Step(), Is.EqualTo(6));
        Assert.That(bus.Acknowledges, Is.EqualTo(2)); Assert.That(cpu.PC, Is.EqualTo(0x2001));
    }
    [Test]
    public void InjectedHaltKeepsWaitingAndNmiReturnsToTheInterruptedPc()
    {
        var (cpu, bus) = Setup(0x76, 0x04); cpu.B = 3;
        Assert.That(cpu.Step(), Is.EqualTo(6)); Assert.That(cpu.Halted, Is.True);
        Assert.That(cpu.Step(), Is.EqualTo(4)); Assert.That(cpu.B, Is.EqualTo(3));
        cpu.RaiseNmi(); Assert.That(cpu.Step(), Is.EqualTo(11));
        Assert.That(cpu.PC, Is.EqualTo(0x66)); Assert.That(cpu.Halted, Is.False);
        Assert.That(bus.Memory[0x3FFE], Is.Zero); Assert.That(bus.Memory[0x3FFF], Is.EqualTo(0x20));
    }
    [Test]
    public void HaltDoesNotExecuteAChangedOpcodeWhileWaiting()
    {
        var (cpu, bus) = Setup(0, 0x76); cpu.IntLine = false; cpu.B = 3;
        Assert.That(cpu.Step(), Is.EqualTo(4)); bus.Memory[0x2000] = 0x04;
        Assert.That(cpu.Step(), Is.EqualTo(4)); Assert.That(cpu.Halted, Is.True);
        Assert.That(cpu.B, Is.EqualTo(3)); Assert.That(cpu.PC, Is.EqualTo(0x2000));
        Assert.That(bus.Accesses[^1].Address, Is.EqualTo(0x2000));
    }
    [Test]
    public void ImOneIgnoresDeviceDataAndRetainsItsExistingFrame()
    {
        var (cpu, bus) = Setup(0); cpu.IM = 1;
        Assert.That(cpu.Step(), Is.EqualTo(13)); Assert.That(cpu.PC, Is.EqualTo(0x38));
        Assert.That(bus.Acknowledges, Is.Zero); Assert.That(cpu.SP, Is.EqualTo(0x3FFE));
    }
    [Test]
    public void ImTwoKeepsItsVectorReadAndAddressWrap()
    {
        var (cpu, bus) = Setup(0xFF); cpu.IM = 2; cpu.I = 0xFF;
        bus.Memory[0xFFFF] = 0x34; bus.Memory[0] = 0x12;
        Assert.That(cpu.Step(), Is.EqualTo(19)); Assert.That(cpu.PC, Is.EqualTo(0x1234));
        Assert.That(bus.Acknowledges, Is.EqualTo(1));
    }
}
