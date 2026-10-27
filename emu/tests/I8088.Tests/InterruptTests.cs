using Retro.Cpu.I8088;

namespace I8088.Tests;

public sealed partial class CpuTests
{
    private static void Vector(I8088Cpu cpu, Bus bus, byte number, ushort segment, ushort offset = 0x20)
    {
        cpu.WriteWord(0, (ushort)(number * 4), offset);
        cpu.WriteWord(0, (ushort)(number * 4 + 2), segment);
        bus.Memory[I8088Cpu.Physical(segment, offset)] = 0xCF;
    }

    [Test]
    public void TrapSavesTheCompletedInstructionAndDoesNotCountAsAnotherInstruction()
    {
        var (cpu, bus) = Program(0x90, 0x90);
        Vector(cpu, bus, 1, 0x3000);
        cpu.Flags = I8088Cpu.Trap | I8088Cpu.Carry | I8088Cpu.InterruptEnable;
        ushort flags = cpu.Flags, sp = cpu.SP;
        cpu.Step();
        Assert.That((cpu.CS, cpu.IP, cpu.Instructions), Is.EqualTo((0x1000, 1, 1L)));
        cpu.Step();
        Assert.That((cpu.CS, cpu.IP, cpu.Instructions), Is.EqualTo((0x3000, 0x20, 1L)));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(1));
        Assert.That(cpu.ReadWord(cpu.SS, (ushort)(cpu.SP + 2)), Is.EqualTo(0x1000));
        Assert.That(cpu.ReadWord(cpu.SS, (ushort)(cpu.SP + 4)), Is.EqualTo(flags));
        Assert.That(cpu.Flags & (I8088Cpu.Trap | I8088Cpu.InterruptEnable), Is.Zero);
        cpu.Step();
        Assert.That((cpu.CS, cpu.IP, cpu.SP, cpu.Flags), Is.EqualTo((0x1000, 1, (int)sp, (int)flags)));
    }

    // A stack over the vector table shows the order: the vector is read before the frame is pushed.
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void InterruptReadsItsVectorBeforePushingTheReturnFrame(bool divide, bool timed)
    {
        var (cpu, bus) = divide ? Program(0xF6, 0xF3) : Program(0xCD, 0x21); // DIV BL with BL=0, or INT 21h
        byte vector = divide ? (byte)0 : (byte)0x21;
        Vector(cpu, bus, vector, 0x3000, 0x40);
        cpu.SS = 0; cpu.SP = (ushort)(vector * 4 + 4); cpu.BX = 0; cpu.AX = 0x1234; cpu.TimingEnabled = timed;
        ushort flags = cpu.Flags;
        cpu.Step();
        Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x3000, 0x40)));
        Assert.That(cpu.ReadWord(0, (ushort)(vector * 4)), Is.EqualTo(0x1000), "The pushed CS replaced the vector offset.");
        Assert.That(cpu.ReadWord(0, (ushort)(vector * 4 + 2)), Is.EqualTo(flags));
        Assert.That(cpu.ReadWord(0, cpu.SP), Is.EqualTo(2));
    }

    [Test]
    public void PopfEnablingTrapWaitsForTheNextInstruction()
    {
        var (cpu, bus) = Program(0x9D, 0x90, 0x90);
        Vector(cpu, bus, 1, 0x3000);
        cpu.WriteWord(cpu.SS, cpu.SP, I8088Cpu.Trap);
        cpu.Step(); Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x1000, 1)));
        cpu.Step(); Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x1000, 2)));
        cpu.Step(); Assert.That(cpu.CS, Is.EqualTo(0x3000));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(2));
    }

    [Test]
    public void PopfClearingTrapStillCompletesItsAlreadySampledSingleStep()
    {
        var (cpu, bus) = Program(0x9D, 0x90, 0x90);
        Vector(cpu, bus, 1, 0x3000);
        cpu.Flags = I8088Cpu.Trap;
        cpu.WriteWord(cpu.SS, cpu.SP, 0);
        cpu.Step(); Assert.That(cpu.Flag(I8088Cpu.Trap), Is.False);
        cpu.Step(); Assert.That(cpu.CS, Is.EqualTo(0x3000));
        Assert.That(cpu.ReadWord(cpu.SS, (ushort)(cpu.SP + 4)) & I8088Cpu.Trap, Is.Zero);
        cpu.Step(); cpu.Step(); cpu.Step();
        Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x1000, 3)));
    }

    [Test]
    public void IretRestoringTrapDoesNotTrapBeforeTheReturnedInstruction()
    {
        var (cpu, bus) = Program(0xCF);
        Vector(cpu, bus, 1, 0x3000);
        cpu.WriteWord(cpu.SS, cpu.SP, 0x10);
        cpu.WriteWord(cpu.SS, (ushort)(cpu.SP + 2), 0x2000);
        cpu.WriteWord(cpu.SS, (ushort)(cpu.SP + 4), I8088Cpu.Trap);
        bus.Memory[0x20010] = 0x90;
        cpu.Step(); Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x2000, 0x10)));
        cpu.Step(); Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x2000, 0x11)));
        cpu.Step(); Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(0x11));
    }

    [TestCase(0x8E, 0xC0)] [TestCase(0x8E, 0xD0)] [TestCase(0x8E, 0xD8)]
    [TestCase(0x07, -1)] [TestCase(0x17, -1)] [TestCase(0x1F, -1)]
    public void LoadingAnySegmentProtectsTheFollowingInstructionFromTrap(int op, int operand)
    {
        byte[] code = operand < 0 ? [(byte)op, 0x90, 0x90] : [(byte)op, (byte)operand, 0x90, 0x90];
        var (cpu, bus) = Program(code);
        Vector(cpu, bus, 1, 0x3000);
        cpu.AX = 0x2000; cpu.WriteWord(cpu.SS, cpu.SP, 0x2000);
        cpu.Flags = I8088Cpu.Trap;
        cpu.Step(); cpu.Step();
        Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x1000, code.Length - 1)));
        cpu.Step();
        Assert.That(cpu.CS, Is.EqualTo(0x3000));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(code.Length - 1));
    }

    [TestCase(0xC0)] [TestCase(0xD0)] [TestCase(0xD8)]
    public void LoadingAnySegmentAlsoProtectsTheFollowingInstructionFromNmi(int modrm)
    {
        var (cpu, bus) = Program(0x8E, (byte)modrm, 0x90, 0x90);
        cpu.AX = 0x2000; Vector(cpu, bus, 2, 0x4000);
        cpu.Step(); cpu.RequestNmi(); cpu.Step();
        Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x1000, 3)));
        cpu.Step(); Assert.That(cpu.CS, Is.EqualTo(0x4000));
    }

    [TestCase(false)] [TestCase(true)]
    public void ExternalInterruptHasPriorityAndTrapNestsBeforeItsFirstHandlerInstruction(bool nmi)
    {
        var (cpu, bus) = Program(0x90, 0x90);
        Vector(cpu, bus, 1, 0x3000); Vector(cpu, bus, nmi ? (byte)2 : (byte)8, 0x4000);
        cpu.Flags = I8088Cpu.Trap | I8088Cpu.InterruptEnable;
        cpu.Step();
        if (nmi) cpu.RequestNmi(); else cpu.RequestInterrupt(8);
        cpu.Step(); Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x4000, 0x20)));
        cpu.Step(); Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x3000, 0x20)));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(0x20));
        Assert.That(cpu.ReadWord(cpu.SS, (ushort)(cpu.SP + 2)), Is.EqualTo(0x4000));
        Assert.That(cpu.ReadWord(cpu.SS, (ushort)(cpu.SP + 4)) & I8088Cpu.Trap, Is.Zero);
        cpu.Step(); cpu.Step();
        Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x1000, 1)));
        Assert.That(cpu.Flag(I8088Cpu.Trap), Is.True);
    }

    [TestCase(0xCC, -1, 3)] [TestCase(0xCD, 0x7F, 0x7F)]
    [TestCase(0xCE, -1, 4)] [TestCase(0xF6, 0xF3, 0)]
    public void SoftwareInterruptAndDivideErrorEnterBeforeThePendingTrap(int op, int operand, int vector)
    {
        byte[] code = operand < 0 ? [(byte)op] : [(byte)op, (byte)operand];
        var (cpu, bus) = Program(code);
        Vector(cpu, bus, 1, 0x3000); Vector(cpu, bus, (byte)vector, 0x4000);
        cpu.Flags = I8088Cpu.Trap | I8088Cpu.Overflow;
        cpu.Step(); Assert.That(cpu.CS, Is.EqualTo(0x4000));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(code.Length));
        cpu.Step(); Assert.That(cpu.CS, Is.EqualTo(0x3000));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(0x20));
        Assert.That(cpu.ReadWord(cpu.SS, (ushort)(cpu.SP + 2)), Is.EqualTo(0x4000));
    }

    [Test]
    public void RepTrapFramesRestartEachIterationAndFinishAtTheNextInstruction()
    {
        var (cpu, bus) = Program(0xF3, 0xA4, 0x90);
        Vector(cpu, bus, 1, 0x3000);
        cpu.DS = 0x2000; cpu.ES = 0x4000; cpu.CX = 2;
        cpu.WriteWord(cpu.DS, 0, 0x2211); cpu.Flags = I8088Cpu.Trap;
        cpu.Step(); Assert.That((cpu.CX, cpu.IP), Is.EqualTo((1, 0)));
        cpu.Step(); Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.Zero);
        cpu.Step(); cpu.Step();
        Assert.That((cpu.CX, cpu.IP), Is.EqualTo((0, 2)));
        cpu.Step(); Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(2));
        Assert.That(cpu.ReadWord(cpu.ES, 0), Is.EqualTo(0x2211));
    }

    [TestCase(false)] [TestCase(true)]
    public void ATrapAfterHaltWaitsForAnEnabledExternalInterrupt(bool nmi)
    {
        var (cpu, bus) = Program(0xF4);
        Vector(cpu, bus, 1, 0x3000); Vector(cpu, bus, nmi ? (byte)2 : (byte)8, 0x4000);
        cpu.Flags = (ushort)(I8088Cpu.Trap | (nmi ? 0 : I8088Cpu.InterruptEnable));
        cpu.Step();
        Assert.That((cpu.Halted, cpu.TrapPending, cpu.Instructions), Is.EqualTo((true, true, 1L)));
        Assert.That(cpu.Step(), Is.False);
        Assert.That(cpu.Step(), Is.False);
        if (nmi) cpu.RequestNmi(); else cpu.RequestInterrupt(8);
        cpu.Step();
        Assert.That((cpu.Halted, cpu.CS, cpu.IP), Is.EqualTo((false, 0x4000, 0x20)));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(1));
        cpu.Step();
        Assert.That((cpu.CS, cpu.IP), Is.EqualTo((0x3000, 0x20)));
        Assert.That(cpu.ReadWord(cpu.SS, cpu.SP), Is.EqualTo(0x20));
        Assert.That(cpu.Instructions, Is.EqualTo(1));
    }

    [Test]
    public void ResetDiscardsThePendingTrap()
    {
        var (cpu, bus) = Program(0x90);
        Vector(cpu, bus, 1, 0x3000); cpu.Flags = I8088Cpu.Trap;
        cpu.Step(); cpu.Reset(); cpu.CS = 0x1000; cpu.IP = 0;
        cpu.Step(); cpu.Step();
        Assert.That(cpu.CS, Is.EqualTo(0x1000));
    }

    [Test]
    public void UnsupportedOpcodesStillFailInsteadOfBeingConsumedBySingleStepping()
    {
        var (cpu, _) = Program(0x60); cpu.Flags = I8088Cpu.Trap;
        var error = Assert.Throws<NotSupportedException>(() => cpu.Step());
        Assert.That(error!.Message, Does.Contain("60").And.Contain("1000:0000"));
        Assert.That(cpu.Instructions, Is.Zero);
    }
}
