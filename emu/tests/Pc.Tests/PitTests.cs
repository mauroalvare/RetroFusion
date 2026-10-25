using Retro.Systems.Pc;

namespace Pc.Tests;

public class PitTests
{
    private static Pit8253 Ready(int mode, ushort count, int channel = 0)
    {
        var pit = new Pit8253();
        pit.SetGate(channel, true);
        pit.WriteControl((byte)((channel << 6) | 0x30 | (mode << 1)));
        WriteWord(pit, channel, count);
        pit.Tick(channel); // CR -> counting element, not the first decrement.
        return pit;
    }

    private static void WriteWord(Pit8253 pit, int channel, ushort count)
    { pit.WriteCounter(channel, (byte)count); pit.WriteCounter(channel, (byte)(count >> 8)); }

    private static ushort Count(Pit8253 pit, int channel = 0)
    {
        pit.WriteControl((byte)(channel << 6));
        return (ushort)(pit.ReadCounter(channel) | (pit.ReadCounter(channel) << 8));
    }

    private static void Clocks(Pit8253 pit, int count, int channel = 0)
    { for (int clock = 0; clock < count; clock++) pit.Tick(channel); }

    [Test]
    public void SoftwareMustProgramAndClockTheCounterBeforeReading()
    {
        var pit = new Pit8253();
        pit.Tick(0);
        Assert.That(pit.GetOutput(0), Is.False);
        Assert.Throws<InvalidOperationException>(() => pit.ReadCounter(0));
        Assert.Throws<InvalidOperationException>(() => pit.WriteCounter(0, 4));
        pit.WriteControl(0x30);
        Assert.Throws<InvalidOperationException>(() => pit.ReadCounter(0));
        pit.WriteCounter(0, 4);
        Assert.Throws<InvalidOperationException>(() => pit.ReadCounter(0));
        pit.WriteCounter(0, 0);
        Assert.Throws<InvalidOperationException>(() => pit.ReadCounter(0));
        pit.Tick(0);
        Assert.That(Count(pit), Is.EqualTo(4)); // Loads even while GATE is low.
        Clocks(pit, 10);
        Assert.That(Count(pit), Is.EqualTo(4));
        pit.SetGate(0, true); pit.Tick(0);
        Assert.That(Count(pit), Is.EqualTo(3));
    }

    [TestCase(0, new ushort[] {4,3,2,1,0,65535,65534}, new bool[] {false,false,false,false,true,true,true})]
    [TestCase(2, new ushort[] {4,3,2,1,4,3,2,1,4}, new bool[] {true,true,true,false,true,true,true,false,true})]
    [TestCase(3, new ushort[] {4,2,4,2,4,2,4}, new bool[] {true,true,false,false,true,true,false})]
    public void IntelCountAndOutputTimelinesIncludeLoadingAndTerminalCount(int mode, ushort[] counts, bool[] outputs)
    {
        var pit = Ready(mode, 4);
        for (int pulse = 0; pulse < counts.Length; pulse++)
        {
            Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((counts[pulse], outputs[pulse])), $"pulse={pulse}");
            pit.Tick(0);
        }
    }

    [Test]
    public void ModeThreeOddCountUsesOriginal8253OneAndThreeDecrements()
    {
        var pit = Ready(3, 5);
        ushort[] counts = [5,4,2,5,2,5,4,2,5,2,5];
        bool[] outputs = [true,true,true,false,false,true,true,true,false,false,true];
        for (int pulse = 0; pulse < counts.Length; pulse++)
        {
            Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((counts[pulse], outputs[pulse])), $"pulse={pulse}");
            pit.Tick(0);
        }
    }

    [TestCase(2)] [TestCase(3)]
    public void PeriodicOutputsFollowDivisionAndDutyCycleForManyDivisors(int mode)
    {
        // Independent arithmetic oracle for the observable waveform, not a second state machine.
        int[] divisors = Enumerable.Range(2, 126).Concat(new[] {255,256,257,32767,32768,65535,65536}).ToArray();
        foreach (int divisor in divisors)
        {
            if (mode == 3 && divisor == 3) continue; // Original-silicon corner is explicitly unsupported.
            var pit = Ready(mode, unchecked((ushort)divisor));
            for (int pulse = 0; pulse <= 3 * divisor; pulse++)
            {
                int phase = pulse % divisor;
                bool expected = mode == 2 ? phase < divisor - 1 : phase < (divisor + 1) / 2;
                if (pit.GetOutput(0) != expected) Assert.Fail($"mode={mode}, N={divisor}, pulse={pulse}");
                pit.Tick(0);
            }
        }
    }

    [Test]
    public void ZeroInModeZeroMeans65536AndKeepsCountingAfterTerminal()
    {
        var pit = Ready(0, 0);
        Clocks(pit, 65535);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((1, false)));
        pit.Tick(0);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((0, true)));
        pit.Tick(0);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((65535, true)));
        Clocks(pit, 65536);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((65535, true)));
    }

    [Test]
    public void ModeZeroGatePausesAndResumesWithoutReloading()
    {
        var pit = Ready(0, 5);
        pit.Tick(0); pit.SetGate(0, false);
        Clocks(pit, 12);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((4, false)));
        pit.SetGate(0, true); pit.Tick(0);
        Assert.That(Count(pit), Is.EqualTo(3));
        Clocks(pit, 3); pit.SetGate(0, false);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((0, true)));
    }

    [Test]
    public void ModeZeroFirstWriteStopsAnOldCountAndSecondWriteSchedulesALoad()
    {
        var pit = Ready(0, 2);
        Clocks(pit, 2);
        Assert.That(pit.GetOutput(0), Is.True);
        pit.WriteCounter(0, 5);
        Assert.That(pit.GetOutput(0), Is.False);
        Clocks(pit, 20);
        Assert.Throws<InvalidOperationException>(() => pit.ReadCounter(0));
        pit.WriteCounter(0, 0); pit.Tick(0);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((5, false)));
        Clocks(pit, 4);
        Assert.That(pit.GetOutput(0), Is.False);
        pit.Tick(0); Assert.That(pit.GetOutput(0), Is.True);
    }

    [TestCase(2)] [TestCase(3)]
    public void PeriodicGateForcesHighImmediatelyThenRestartsOnTheNextPulse(int mode)
    {
        var pit = Ready(mode, 4);
        Clocks(pit, mode == 2 ? 3 : 2);
        Assert.That(pit.GetOutput(0), Is.False);
        pit.SetGate(0, false);
        Assert.That(pit.GetOutput(0), Is.True);
        ushort paused = Count(pit);
        Clocks(pit, 15);
        Assert.That(Count(pit), Is.EqualTo(paused));
        pit.SetGate(0, true);
        Assert.That(Count(pit), Is.EqualTo(paused));
        pit.Tick(0);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((4, true)));
        pit.SetGate(0, true); pit.Tick(0); // A high level is not another rising edge.
        Assert.That(Count(pit), Is.EqualTo(mode == 2 ? 3 : 2));
    }

    [Test]
    public void AShortGatePulseStillRequestsARestartWithoutClocksDuringThePulse()
    {
        var pit = Ready(2, 6);
        Clocks(pit, 2);
        pit.SetGate(0, false); pit.SetGate(0, true);
        pit.Tick(0);
        Assert.That(Count(pit), Is.EqualTo(6));
    }

    [Test]
    public void ModeTwoNewDivisorWaitsForTheEndOfTheCurrentPeriod()
    {
        var pit = Ready(2, 4);
        Clocks(pit, 2); WriteWord(pit, 0, 6);
        Assert.That(Count(pit), Is.EqualTo(2));
        pit.Tick(0);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((1, false)));
        pit.Tick(0);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((6, true)));
    }

    [Test]
    public void ModeThreeNewDivisorStartsAtTheNextOutputTransition()
    {
        var pit = Ready(3, 6);
        pit.Tick(0); WriteWord(pit, 0, 5);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((4, true)));
        pit.Tick(0); Assert.That(Count(pit), Is.EqualTo(2));
        pit.Tick(0);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((5, false)));
        pit.Tick(0); Assert.That(Count(pit), Is.EqualTo(2));
        pit.Tick(0);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((5, true)));
    }

    [TestCase(2)] [TestCase(3)]
    public void PartialPeriodicWriteDoesNotReplaceTheOldReloadValue(int mode)
    {
        var pit = Ready(mode, 4);
        pit.WriteCounter(0, 6);
        Clocks(pit, 4);
        Assert.That(pit.GetOutput(0), Is.True);
        pit.WriteCounter(0, 0);
        Assert.That(Count(pit), Is.EqualTo(4));
        Clocks(pit, mode == 2 ? 4 : 2);
        Assert.That(Count(pit), Is.EqualTo(6));
    }

    [TestCase(2)] [TestCase(3)]
    public void GateRetriggerUsesTheMostRecentlyCompletedDivisor(int mode)
    {
        var pit = Ready(mode, 8);
        pit.Tick(0); WriteWord(pit, 0, 10);
        pit.SetGate(0, false); pit.SetGate(0, true); pit.Tick(0);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((10, true)));
    }

    [Test]
    public void LatchKeepsBothBytesCoherentWhileTheCounterContinues()
    {
        var pit = Ready(0, 0x0100);
        pit.WriteControl(0);
        Assert.That(pit.ReadCounter(0), Is.Zero);
        Clocks(pit, 2); // The live high byte becomes zero.
        Assert.That(pit.ReadCounter(0), Is.EqualTo(1));
        Assert.That(Count(pit), Is.EqualTo(0x00FE));
    }

    [Test]
    public void DirectReadsUseTheLiveCounterAndRequireStoppingClocksForACoherentWord()
    {
        var pit = Ready(0, 0x0100);
        Assert.That(pit.ReadCounter(0), Is.Zero);
        pit.Tick(0);
        Assert.That(pit.ReadCounter(0), Is.Zero); // Not an automatic word latch as on another chip.
        Assert.That(Count(pit), Is.EqualTo(0x00FF));
    }

    [Test]
    public void LatchIgnoresModeAndBcdBitsAndCannotBeOverwrittenBeforeReading()
    {
        var pit = Ready(0, 7);
        pit.WriteControl(0x0F); // Access=0; other low bits are don't-care.
        Clocks(pit, 2);
        Assert.Throws<InvalidOperationException>(() => pit.WriteControl(0));
        Assert.That(pit.ReadCounter(0) | (pit.ReadCounter(0) << 8), Is.EqualTo(7));
        Assert.That(Count(pit), Is.EqualTo(5));
        Assert.That(pit.GetOutput(0), Is.False);
    }

    [Test]
    public void AnIncompleteWordReadCannotBeInterleavedWithAWritingOrAnotherLatch()
    {
        var pit = Ready(0, 0x0102);
        pit.ReadCounter(0);
        Assert.Throws<InvalidOperationException>(() => pit.WriteCounter(0, 9));
        Assert.Throws<InvalidOperationException>(() => pit.WriteControl(0));
        Assert.That(pit.ReadCounter(0), Is.EqualTo(1));
        WriteWord(pit, 0, 9); pit.Tick(0);
        Assert.That(Count(pit), Is.EqualTo(9));
    }

    [TestCase(1)] [TestCase(2)]
    public void SingleByteAccessClearsTheOtherHalfAndConsumesTheLatchInOneRead(int access)
    {
        var pit = new Pit8253(); pit.SetGate(0, true);
        pit.WriteControl((byte)(access << 4)); pit.WriteCounter(0, 0xAB); pit.Tick(0);
        pit.WriteControl(0);
        Assert.That(pit.ReadCounter(0), Is.EqualTo(0xAB));
        pit.Tick(0); pit.WriteControl(0);
        Assert.That(pit.ReadCounter(0), Is.EqualTo(0xAA));
        // A second latch was accepted: no hidden unread byte remains.
    }

    [Test]
    public void NewModeAbandonsOldLatchesPartialWritesAndPendingLoads()
    {
        var pit = Ready(0, 9);
        pit.WriteControl(0); pit.ReadCounter(0);
        pit.WriteControl(0x34); WriteWord(pit, 0, 8);
        pit.WriteControl(0x36); pit.WriteCounter(0, 5);
        pit.WriteControl(0x30); WriteWord(pit, 0, 4); pit.Tick(0);
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((4, false)));
    }

    [TestCase(6,2)] [TestCase(7,3)]
    public void ModeAliasesUseTheSameWaveform(int encoded, int mode)
    {
        var pit = new Pit8253(); pit.SetGate(0, true);
        pit.WriteControl((byte)(0x30 | (encoded << 1))); WriteWord(pit, 0, 4); pit.Tick(0);
        Clocks(pit, mode == 2 ? 3 : 2);
        Assert.That(pit.GetOutput(0), Is.False);
    }

    [TestCase(0x32)] [TestCase(0x38)] [TestCase(0x3A)] // Modes 1/4/5.
    [TestCase(0x31)] [TestCase(0x35)] [TestCase(0x37)] // BCD.
    [TestCase(0xC0)] [TestCase(0xF0)] // Illegal selection / 8254 read-back.
    public void UnsupportedControlWordsLeaveTheRunningCounterIntact(int control)
    {
        var pit = Ready(0, 4);
        Assert.Throws<NotSupportedException>(() => pit.WriteControl((byte)control));
        Assert.That((Count(pit), pit.GetOutput(0)), Is.EqualTo((4, false)));
        Clocks(pit, 4); Assert.That(pit.GetOutput(0), Is.True);
    }

    [TestCase(3,3)]
    public void UnsupportedDivisorsDoNotReplaceTheOldRunningPeriod(int mode, int invalid)
    {
        var pit = Ready(mode, 8);
        pit.WriteCounter(0, (byte)invalid);
        Assert.Throws<NotSupportedException>(() => pit.WriteCounter(0, 0));
        Clocks(pit, 8);
        Assert.That(pit.GetOutput(0), Is.True);
        // Retry the final byte with a supported value: the low byte was not discarded by rejection.
        pit.WriteCounter(0, 1);
        pit.SetGate(0, false); pit.SetGate(0, true); pit.Tick(0);
        Assert.That(Count(pit), Is.EqualTo(0x100 | invalid));
    }

    [Test]
    public void ChannelsHaveSeparateClocksGatesAndByteSequences()
    {
        var pit = new Pit8253();
        for (int channel = 0; channel < 3; channel++)
        {
            pit.SetGate(channel, true);
            pit.WriteControl((byte)((channel << 6) | 0x30));
            pit.WriteCounter(channel, (byte)(4 + channel));
        }
        for (int channel = 2; channel >= 0; channel--) { pit.WriteCounter(channel, 0); pit.Tick(channel); }
        pit.Tick(0); Clocks(pit, 2, 1);
        Assert.That(new[] {Count(pit),Count(pit,1),Count(pit,2)}, Is.EqualTo(new ushort[] {3,3,6}));
        pit.SetGate(1, false); Clocks(pit, 6, 1); Clocks(pit, 6, 2);
        Assert.That(new[] {Count(pit),Count(pit,1),Count(pit,2)}, Is.EqualTo(new ushort[] {3,3,0}));
        Assert.That(new[] {pit.GetOutput(0),pit.GetOutput(1),pit.GetOutput(2)}, Is.EqualTo(new[] {false,false,true}));
    }

    [Test]
    public void OutputEventsIncludeProgrammingGateAndResetButNotUnchangedLevels()
    {
        var pit = new Pit8253(); var changes = new List<(int,bool)>();
        pit.OutputChanged += (channel, high) => changes.Add((channel, high));
        pit.SetGate(2, true); pit.WriteControl(0xB4); WriteWord(pit, 2, 2);
        pit.Tick(2); pit.Tick(2); pit.SetGate(2, false); pit.SetGate(2, false);
        pit.Reset(); pit.Reset();
        Assert.That(changes, Is.EqualTo(new[] {(2,true),(2,false),(2,true),(2,false)}));
        pit.Tick(2);
        Assert.Throws<InvalidOperationException>(() => pit.ReadCounter(2));
        pit.WriteControl(0x90); pit.WriteCounter(2, 1); pit.Tick(2); pit.Tick(2);
        Assert.That(pit.GetOutput(2), Is.False); // Reset also cleared GATE.
    }

    [TestCase(-1)] [TestCase(3)]
    public void InvalidChannelsCannotAddressAnotherCounter(int channel)
    {
        var pit = Ready(0, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => pit.Tick(channel));
        Assert.Throws<ArgumentOutOfRangeException>(() => pit.SetGate(channel, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => pit.GetOutput(channel));
        Assert.Throws<ArgumentOutOfRangeException>(() => pit.ReadCounter(channel));
        Assert.Throws<ArgumentOutOfRangeException>(() => pit.WriteCounter(channel, 0));
        Assert.That(Count(pit), Is.EqualTo(4));
    }
    [TestCase(2, 2)] [TestCase(3, 0)]
    public void CountOneUsesTheEffectiveReloadObservedOnHardware(int mode, int effective)
    {
        var pit = Ready(mode, 1); Assert.That(Count(pit), Is.EqualTo(effective));
        if (mode == 2)
        { pit.Tick(0); Assert.That(pit.GetOutput(0), Is.False); pit.Tick(0); Assert.That(pit.GetOutput(0), Is.True); }
        else
        { Clocks(pit, 32767); Assert.That(pit.GetOutput(0), Is.True); pit.Tick(0); Assert.That(pit.GetOutput(0), Is.False); }
    }

}
