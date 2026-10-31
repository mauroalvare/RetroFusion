using Retro.Systems.Arcade;

namespace Arcade.Tests;

public sealed class InterruptStateTests
{
    private static SpaceInvaders Machine()
    {
        var rom = new byte[0x2000]; rom[0] = 0xFB; // EI; NOP
        var machine = new SpaceInvaders(rom); machine.I8080.SP = 0x2400;
        return machine;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SnapshotAfterEiProtectsTheFollowingInstruction(bool newMachine)
    {
        var machine = Machine(); machine.I8080.Step();
        byte[] state = machine.SaveState(); machine.I8080.Step();
        var restored = newMachine ? Machine() : machine;
        restored.LoadState(state);
        Assert.That(restored.I8080.Interrupt(0xCF), Is.Zero);
        Assert.That(restored.I8080.Step(), Is.EqualTo(4));
        Assert.That(restored.I8080.PC, Is.EqualTo(2));
        Assert.That(restored.I8080.Interrupt(0xCF), Is.EqualTo(11));
        Assert.That(restored.I8080.PC, Is.EqualTo(8));
    }

    [Test]
    public void RestoringBeforeEiDiscardsTheDelayOfTheAbandonedExecution()
    {
        var machine = Machine(); machine.I8080.INTE = true;
        byte[] state = machine.SaveState(); machine.I8080.Step();
        machine.LoadState(state);
        Assert.That(machine.I8080.InterruptsAccepted, Is.True);
    }

    [Test]
    public void LegacySnapshotClearsTheEiLatchThatItsFormatDidNotStore()
    {
        var machine = Machine(); machine.I8080.INTE = true;
        byte[] state = machine.SaveState()[..^1];
        using (var stream = new MemoryStream(state))
        {
            new BinaryReader(stream).ReadString();
            new BinaryWriter(stream).Write(1);
        }
        machine.I8080.Step(); machine.LoadState(state);
        Assert.That(machine.I8080.InterruptsAccepted, Is.True);
        Assert.That(machine.SaveState().Length, Is.EqualTo(state.Length + 1));
    }

    [Test]
    public void ResetDiscardsTheEiLatch()
    {
        var machine = Machine(); machine.I8080.Step(); machine.Reset(false);
        Assert.That(machine.I8080.InterruptDelayPending, Is.False);
    }
}
