using Retro.Core;
using Retro.Ai;
using Retro.Systems.Spectrum;

namespace Studio.Tests;

public sealed class RoutineFinderTests
{
    private static Spectrum48 OwnProgram()
    {
        var machine = new Spectrum48(new byte[16384]);
        byte[] main = [0xCD, 0x10, 0x80, 0xC3, 0x00, 0x80];
        byte[] routine = [0x3E, 0x10, 0x32, 0x00, 0x40, 0xD3, 0xFE, 0xDB, 0xFE, 0xC9];
        for (int i = 0; i < main.Length; i++) machine.Memory.Poke((uint)(0x8000 + i), main[i]);
        for (int i = 0; i < routine.Length; i++) machine.Memory.Poke((uint)(0x8010 + i), routine[i]);
        machine.Z80.PC = 0x8000; machine.Z80.SP = 0xFF00;
        return machine;
    }

    [Test]
    public void ACalledRoutineHasObservedScreenWritesAndBothIoDirections()
    {
        var machine = OwnProgram();
        var report = RoutineFinder.Sample(machine, 70);
        var routine = report.Routines.Single(r => r.Address == 0x8010);
        Assert.Multiple(() =>
        {
            Assert.That(report.Steps, Is.EqualTo(70));
            Assert.That(report.HasIoWatch, Is.True);
            Assert.That(report.ChangingCode, Is.False);
            Assert.That(routine.CodeSha256, Has.Length.EqualTo(64));
            Assert.That(routine.Accesses.Any(a => a.Instruction == 0x8012 && a.Address == 0x4000 && a.Write && !a.Io && a.Count == 10), Is.True);
            Assert.That(routine.Accesses.Any(a => a.Instruction == 0x8015 && a.Address == 0x10FE && a.Write && a.Io), Is.True);
            Assert.That(routine.Accesses.Any(a => a.Instruction == 0x8017 && a.Address == 0x10FE && !a.Write && a.Io), Is.True);
        });
    }

    [Test]
    public void SamplingPreservesOtherObserversAndDoesNotInstallAnExecutionMap()
    {
        var machine = OwnProgram(); int writes = 0, ports = 0;
        Action<ushort, byte, ushort> write = (_, _, _) => writes++;
        Action<ushort, byte, bool, ushort> io = (_, _, _, _) => ports++;
        machine.WriteWatch = write; machine.IoWatch = io;
        RoutineFinder.Sample(machine, 7);
        Assert.That(writes, Is.GreaterThan(0)); Assert.That(ports, Is.EqualTo(2));
        Assert.That(machine.WriteWatch, Is.SameAs(write)); Assert.That(machine.IoWatch, Is.SameAs(io));
        Assert.That(machine.ExecutedMap, Is.Null);
    }

    [Test]
    public void CancellationDuringAProbeRestoresBothObservers()
    {
        var machine = OwnProgram(); using var cancellation = new CancellationTokenSource();
        Action<ushort, byte, ushort> write = (_, _, _) => cancellation.Cancel();
        Action<ushort, byte, bool, ushort> io = (_, _, _, _) => { };
        machine.WriteWatch = write; machine.IoWatch = io;
        Assert.Throws<OperationCanceledException>(() => RoutineFinder.Sample(machine, 1000, cancellation.Token));
        Assert.That(machine.WriteWatch, Is.SameAs(write)); Assert.That(machine.IoWatch, Is.SameAs(io));
    }

    [Test]
    public void SelfModifiedInstructionsDoNotProduceAFalseStableRoutine()
    {
        var machine = OwnProgram();
        byte[] code = [0x3E, 0x00, 0x32, 0x00, 0x80, 0xC3, 0x00, 0x80];
        for (int i = 0; i < code.Length; i++) machine.Memory.Poke((uint)(0x8000 + i), code[i]);
        var report = RoutineFinder.Sample(machine, 20);
        Assert.That(report.ChangingCode, Is.True);
        Assert.That(report.Routines, Is.Empty);
    }

    [TestCase(0)] [TestCase(50001)]
    public void InvalidBudgetsDoNotAdvanceTheMachine(int steps)
    {
        var machine = OwnProgram(); long cycles = machine.Z80.T;
        Assert.Throws<ArgumentOutOfRangeException>(() => RoutineFinder.Sample(machine, steps));
        Assert.That(machine.Z80.T, Is.EqualTo(cycles));
    }

    [Test]
    public void ACodeCitationDistinguishesEditionFirmwareAndMutableMemory()
    {
        var machine = OwnProgram(); var routine = new Routine { Start = 0x8010, End = 0x8019, Executed = true };
        routine.Instructions.UnionWith([0x8010, 0x8012, 0x8015, 0x8017, 0x8019]);
        string edition = new('A', 64);
        string before = CodeExplainer.Reference(machine, routine, [edition], "firmware propio", "Original propio");
        machine.Memory.Poke(0x8011, 0x20);
        string after = CodeExplainer.Reference(machine, routine, [edition], "firmware propio", "Original propio");
        Assert.That(after, Is.Not.EqualTo(before));
        foreach (string citation in new[] { before, after })
            Assert.That(citation, Does.Contain("Original propio").And.Contain(edition).And.Contain("firmware propio").And.Contain("no todos sus caminos").And.Contain("no es el hash del archivo"));
    }

    [Test]
    public void MissingEditionIsExplicitInsteadOfUsingAnUnrelatedHash()
    {
        string citation = CodeExplainer.Reference(OwnProgram(), new Routine { Start = 0x8010 }, [], "sin identificar");
        Assert.That(citation, Does.Contain("Edición sin identificar").And.Contain("Sin ejecución observada"));
    }

    [Test]
    public void AProbeAndTheCodeViewerAgreeOnTheSameCodeSnapshot()
    {
        var machine = OwnProgram(); var observed = RoutineFinder.Sample(machine, 70).Routines.Single(r => r.Address == 0x8010);
        var routine = new Routine { Start = 0x8010, End = 0x8019 };
        routine.Instructions.UnionWith([0x8019, 0x8017, 0x8015, 0x8012, 0x8010]);
        Assert.That(CodeExplainer.Reference(machine, routine, [], ""), Does.Contain(observed.CodeSha256));
    }
}
