using System.Text.Json;
using Retro.Ai;
using Retro.Fusion;
using Retro.Systems.Nes;
using Retro.Systems.Spectrum;

namespace Fusion.Tests;

public class VariableEvidenceTests
{
    private static GameProfile Profile(string machine = "zx48") => new() { Id = "own-evidence", Machine = machine };

    [Test]
    public void HistoryAndCodeContextDescribeObservedWritesWithoutChangingTheGame()
    {
        var machine = new Spectrum48(new byte[0x4000]);
        byte[] code = [0x3E, 3, 0x32, 0, 0x80, 0x3C, 0xC3, 2, 0x90];
        for (int i = 0; i < code.Length; i++) machine.Memory.Poke((uint)(0x9000 + i), code[i]);
        machine.Cpu.ProgramCounter = 0x9000; machine.ExecutedMap = new bool[65536];
        using var observation = new ModificationObservation(machine);
        for (int i = 0; i < 18; i++) machine.StepInstruction();
        byte[] before = machine.SaveState();
        var snapshot = observation.Capture("Own", Profile());
        var value = snapshot.Candidates.Single(c => c.Address == 0x8000);
        Assert.That(value.WriteMinimum, Is.EqualTo(3)); Assert.That(value.WriteMaximum, Is.GreaterThan(3));
        Assert.That(value.ValueChanges, Is.GreaterThan(0)); Assert.That(value.RecentValues.Count, Is.LessThanOrEqualTo(6));
        Assert.That(snapshot.Listing, Does.Contain("LD A,$03").And.Contain("LD ($8000),A").And.Contain("INC A"));
        Assert.That(snapshot.Prompt(""), Does.Contain("rango escrito").And.Contain("Intervalo observado").And.Contain("No se registran las acciones"));
        machine.Memory.Poke(0x8000, 100);
        Assert.That(value.Value, Is.Not.EqualTo(100));
        machine.LoadState(before); Assert.That(machine.SaveState(), Is.EqualTo(before));
    }

    [Test]
    public void AQuietRegionIsAdmittedEvenAfterABusyBufferFillsTheObservationBudget()
    {
        var machine = new Spectrum48(new byte[0x4000]);
        using var observation = new ModificationObservation(machine);
        for (ushort address = 0x6000; address < 0x6800; address++) machine.WriteMemory(address, 3);
        machine.WriteMemory(0xC123, 7);
        var snapshot = observation.Capture("Own", Profile());
        Assert.That(observation.WrittenAddresses, Is.EqualTo(2048));
        Assert.That(snapshot.Candidates.Any(c => c.Address == 0xC123 && c.Writes == 1), Is.True);
        Assert.That(snapshot.Candidates.Count, Is.LessThanOrEqualTo(128));
    }

    [Test]
    public void ObservationUnifiesNesRamMirrorsAndCopiesTheElapsedFrameInterval()
    {
        byte[] cart = new byte[16 + 16384];
        cart[0] = 0x4E; cart[1] = 0x45; cart[2] = 0x53; cart[3] = 0x1A; cart[4] = 1;
        cart[16] = 0x4C; cart[17] = 0; cart[18] = 0x80; cart[16 + 0x3FFD] = 0x80;
        var machine = new Nes(NesCartridge.Load(cart));
        using var observation = new ModificationObservation(machine);
        machine.Write(0x0A03, 10); machine.Write(0x0203, 12); machine.RunFrame(null);
        var snapshot = observation.Capture("Own", Profile("nes"));
        var value = snapshot.Candidates.Single(c => c.Address == 0x0203);
        Assert.That(value.Writes, Is.EqualTo(2)); Assert.That(value.RecentValues, Is.EqualTo(new byte[] {10, 12}));
        Assert.That(snapshot.ObservationStartFrame, Is.Zero); Assert.That(snapshot.FramesObserved, Is.EqualTo(1));
    }

    [Test]
    public void TwoNewVariablesCannotAliasTheSameBitsEvenWhenOnlyOneIsUsedInAnIdea()
    {
        var snapshot = new GameAnalysisSnapshot("Own", "zx48", 0, "", [new(0x8000, 3, 0, 1, 0x9003, "")], Profile());
        var response = JsonSerializer.Serialize(new { analysis = "Own", variables = new[] {
            new {name="first",label="One",address="8000",bytes=1,observed=3,reason="Own write"},
            new {name="second",label="Two",address="8000",bytes=1,observed=3,reason="Same byte"}}, ideas=Array.Empty<object>() });
        Assert.Throws<AiException>(() => GameModificationAnalyzer.Parse(response, snapshot));
    }

    [Test]
    public void AdvisorRejectsMirroredChangesBeforeTheyReachTheButton()
    {
        var profile = Profile("nes"); profile.Variables["a"] = new() { Address="0203" };
        profile.Variables["b"] = new() { Address="0A03" };
        string answer = """{"analysis":"Own","ideas":[{"title":"Own","effect":"Own","changes":[{"variable":"a","value":1,"freeze":true},{"variable":"b","value":2,"freeze":true}]}]}""";
        Assert.Throws<AiException>(() => GameModificationAdvisor.Parse(answer, profile));
    }

    [Test]
    public void Uint9EditsAndCpuFreezingPreserveUnrelatedHighBitsAndUndo()
    {
        var machine = new Spectrum48(new byte[0x4000]); var profile = Profile();
        profile.Variables["x"] = new() {Address="8000",Encoding="uint9",HighBitAddress="8003",HighBitMask=4,Max=511};
        machine.Memory.Poke(0x8000, 3); machine.Memory.Poke(0x8003, 0xA2);
        byte[] before = machine.SaveState();
        using var edits = new GameModifications(machine, profile);
        edits.Apply([new("x",300,true)]);
        machine.WriteMemory(0x8000, 12); machine.WriteMemory(0x8003, 0x50);
        Assert.That(profile.Variables["x"].Read(machine.Memory), Is.EqualTo(300));
        Assert.That(machine.Memory.Peek(0x8003), Is.EqualTo(0x54));
        Assert.That(edits.Undo(), Is.True); Assert.That(machine.SaveState(), Is.EqualTo(before));
        Assert.That(GameModificationAnalyzer.OverlapsKnownMemory(profile, 0x8003, 1), Is.True);
    }

    [TestCase("10000", 4)] [TestCase("8003", 3)] [TestCase("8000", 4)]
    public void InvalidSplitBitStorageCannotWriteAPartialValue(string high, int mask)
    {
        var machine = new Spectrum48(new byte[0x4000]); var profile = Profile();
        profile.Variables["x"] = new() {Address="8000",Encoding="uint9",HighBitAddress=high,HighBitMask=mask,Max=511};
        byte[] before = machine.SaveState();
        using var edits = new GameModifications(machine, profile);
        Assert.Throws<InvalidOperationException>(() => edits.Apply([new("x",300,true)]));
        Assert.That(machine.SaveState(), Is.EqualTo(before)); Assert.That(edits.CanUndo, Is.False);
    }
}
