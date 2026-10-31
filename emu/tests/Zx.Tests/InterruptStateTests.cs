using Retro.Systems.Spectrum;

namespace Retro.Systems.Spectrum.Tests;

public sealed class InterruptStateTests
{
    private static Spectrum48 Machine()
    {
        var rom = new byte[0x4000]; rom[0] = 0xFB; // EI; NOP
        var machine = new Spectrum48(rom); machine.Z80.SP = 0x9000;
        return machine;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SnapshotAfterEiProtectsTheFollowingInstruction(bool newMachine)
    {
        var machine = Machine(); machine.Z80.Step();
        byte[] state = machine.SaveState();
        machine.Z80.Step();
        var restored = newMachine ? Machine() : machine;
        restored.LoadState(state);
        restored.Z80.IntLine = true;
        Assert.That(restored.Z80.Step(), Is.EqualTo(4));
        Assert.That(restored.Z80.PC, Is.EqualTo(2));
        Assert.That(restored.Z80.Step(), Is.EqualTo(13));
        Assert.That(restored.Z80.PC, Is.EqualTo(0x38));
    }

    [Test]
    public void RestoringBeforeEiDiscardsTheDelayOfTheAbandonedExecution()
    {
        var machine = Machine(); machine.Z80.IFF1 = machine.Z80.IFF2 = true;
        byte[] state = machine.SaveState(); machine.Z80.Step();
        machine.LoadState(state);
        Assert.That(machine.Z80.InterruptsAccepted, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SnapshotRestoresItsOwnPendingNmi(bool pending)
    {
        var machine = Machine();
        if (pending) machine.Z80.RaiseNmi();
        byte[] state = machine.SaveState();
        machine.Z80.Step(); machine.Z80.RaiseNmi();
        var restored = pending ? Machine() : machine;
        restored.LoadState(state); restored.Z80.Step();
        Assert.That(restored.Z80.PC, Is.EqualTo(pending ? 0x66 : 1));
    }

    [Test]
    public void LegacySnapshotClearsLatchesThatItsFormatDidNotStore()
    {
        var machine = Machine(); machine.Z80.IFF1 = machine.Z80.IFF2 = true;
        byte[] state = machine.SaveState()[..^2];
        using (var stream = new MemoryStream(state))
        {
            new BinaryReader(stream).ReadString();
            new BinaryWriter(stream).Write(1);
        }
        machine.Z80.Step(); machine.Z80.RaiseNmi();
        machine.LoadState(state);
        Assert.That(machine.Z80.InterruptsAccepted, Is.True);
        Assert.That(machine.Z80.NmiPending, Is.False);
        Assert.That(machine.SaveState().Length, Is.EqualTo(state.Length + 2));
    }

    [Test]
    public void ResetDiscardsEiAndNmiLatches()
    {
        var machine = Machine(); machine.Z80.Step(); machine.Z80.RaiseNmi();
        machine.Reset(false);
        Assert.That(machine.Z80.InterruptDelayPending || machine.Z80.NmiPending, Is.False);
    }
}
