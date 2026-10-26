using Retro.Cpu.I8088;

namespace I8088.Tests;

public sealed partial class CpuTests
{
    private sealed class Source : I8088InterruptSource
    {
        public bool InterruptRequested { get; set; }
        public byte Vector = 8;
        public int Accepted;
        public byte Acknowledge() { Accepted++; InterruptRequested = false; return Vector; }
    }

    private (I8088Cpu Cpu, Bus Bus, Source Source) Controlled(params byte[] code)
    {
        var source = new Source(); var bus = new Bus();
        var cpu = new I8088Cpu(bus, source, model) { CS = 0x1000, SS = 0x5000, SP = 0xFFFE };
        code.CopyTo(bus.Memory, 0x10000);
        Vector(cpu, bus, 8, 0x3000); Vector(cpu, bus, 0x0B, 0x4000);
        return (cpu, bus, source);
    }

    [Test]
    public void ControllerIsNotAcknowledgedWhileIfIsClear()
    {
        var (cpu, _, source) = Controlled(0x90, 0x90, 0x90);
        source.InterruptRequested = true;
        cpu.Step(); cpu.Step(); cpu.Step();
        Assert.That(source.Accepted, Is.Zero);
        Assert.That(source.InterruptRequested, Is.True);
        Assert.That(cpu.Instructions, Is.EqualTo(3));
    }

    [Test]
    public void StiLetsTheFollowingInstructionFinishBeforeAcknowledgingTheController()
    {
        var (cpu, _, source) = Controlled(0xFB, 0x90, 0x90);
        source.InterruptRequested = true;
        cpu.Step(); cpu.Step();
        Assert.That(source.Accepted, Is.Zero);
        cpu.Step();
        Assert.That(source.Accepted, Is.EqualTo(1));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(2));
        Assert.That(cpu.Instructions, Is.EqualTo(2));
    }

    [Test]
    public void VectorIsResolvedAtAcceptanceInsteadOfWhenTheRequestWasFirstAsserted()
    {
        var (cpu, _, source) = Controlled(0x90);
        source.InterruptRequested = true;
        cpu.Step(); source.Vector = 0x0B; cpu.Flags = I8088Cpu.InterruptEnable;
        cpu.Step();
        Assert.That((source.Accepted, cpu.CS), Is.EqualTo((1, 0x4000)));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(1));
    }

    [Test]
    public void WithdrawnControllerLineDoesNotLeaveAStaleCpuRequest()
    {
        var (cpu, _, source) = Controlled(0x90, 0x90);
        source.InterruptRequested = true; cpu.Step();
        source.InterruptRequested = false; cpu.Flags = I8088Cpu.InterruptEnable;
        cpu.Step();
        Assert.That((source.Accepted, cpu.CS, cpu.IP), Is.EqualTo((0, 0x1000, 2)));
    }

    [TestCase(0xC0)] [TestCase(0xD0)] [TestCase(0xD8)]
    public void SegmentLoadProtectionAlsoAppliesToARealController(int modrm)
    {
        var (cpu, _, source) = Controlled(0x8E, (byte)modrm, 0x90, 0x90);
        cpu.AX = 0x2000; cpu.Flags = I8088Cpu.InterruptEnable;
        cpu.Step(); source.InterruptRequested = true; cpu.Step();
        Assert.That(source.Accepted, Is.Zero);
        cpu.Step();
        Assert.That(source.Accepted, Is.EqualTo(1));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(3));
    }

    [Test]
    public void ControllerRequestWakesHaltWithoutExecutingAnotherOpcode()
    {
        var (cpu, _, source) = Controlled(0xF4, 0x90);
        cpu.Flags = I8088Cpu.InterruptEnable;
        cpu.Step(); Assert.That(cpu.Halted, Is.True);
        Assert.That(cpu.Step(), Is.False);
        source.InterruptRequested = true;
        Assert.That(cpu.Step(), Is.True);
        Assert.That((cpu.Halted, cpu.Instructions, source.Accepted), Is.EqualTo((false, 1L, 1)));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(1));
    }

    [Test]
    public void NmiDoesNotAcknowledgeTheMaskableController()
    {
        var (cpu, bus, source) = Controlled(0x90);
        Vector(cpu, bus, 2, 0x6000); source.InterruptRequested = true;
        cpu.Flags = I8088Cpu.InterruptEnable; cpu.RequestNmi();
        cpu.Step(); Assert.That((cpu.CS, source.Accepted), Is.EqualTo((0x6000, 0)));
        cpu.Step(); cpu.Step();
        Assert.That((cpu.CS, source.Accepted), Is.EqualTo((0x3000, 1)));
    }
}
