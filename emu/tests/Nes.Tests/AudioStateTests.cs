using Retro.Systems.Nes;

namespace Nes.Tests;

public sealed class AudioStateTests
{
    private static Retro.Systems.Nes.Nes Machine(bool dmc)
    {
        var rom = new byte[16 + 16384 + 8192]; "NES\x1A"u8.CopyTo(rom); rom[4] = rom[5] = 1;
        new byte[] { 0x78, 0x4C, 1, 0xC0 }.CopyTo(rom, 16); rom[16 + 0x3FFD] = 0xC0;
        Array.Fill(rom, (byte)0x55, 16 + 0x40, 128);
        var m = new Retro.Systems.Nes.Nes(NesCartridge.Load(rom)); m.Write(0x4017, 0x40);
        if (dmc) { m.Write(0x4010, 0x4F); m.Write(0x4011, 64); m.Write(0x4012, 1); m.Write(0x4013, 7); m.Write(0x4015, 0x10); }
        else { m.Write(0x4015, 1); m.Write(0x4000, 0xBF); m.Write(0x4002, 127); m.Write(0x4003, 0); }
        return m;
    }
    private static short[] Frame(Retro.Systems.Nes.Nes m)
    { m.RunFrame(null); var sink = new short[4096]; return sink[..m.DrainAudio(sink)]; }

    [TestCase(false)] [TestCase(true)]
    public void ALoadedStateProducesTheSameFutureSamplesAfterUnrelatedPlayback(bool dmc)
    {
        var m = Machine(dmc); Frame(m);
        // Capture between frame boundaries, with a partial sample in the integrator.
        m.StepInstruction(); byte[] state = m.SaveState();
        var expected = Frame(m); for (int i = 0; i < 4; i++) Frame(m);
        m.LoadState(state); var replay = Frame(m);
        Assert.That(replay, Is.EqualTo(expected), "Mixer integration and DC filter history belong to the saved audio state");
    }

    [Test]
    public void ResetDiscardsSamplesThatWereAlreadyQueuedBeforeReset()
    {
        var m = Machine(true); m.RunFrame(null); m.Reset(true);
        var sink = new short[4096];
        Assert.That(m.DrainAudio(sink), Is.LessThan(10), "Only reset clocks may produce samples; the previous frame must not survive");
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public void InvalidMixerHistoryIsRejected(int field)
    {
        var m = Machine(false); var state = m.SaveState();
        if (field == 1) BitConverter.GetBytes(100).CopyTo(state, state.Length - 12);
        else BitConverter.GetBytes(float.NaN).CopyTo(state, state.Length - 16 + field * 4);
        Assert.Throws<InvalidDataException>(() => m.LoadState(state));
    }

    [TestCase(false)] [TestCase(true)]
    public void LoadingLegacyAudioDoesNotDependOnTheReceivingMachinesPreviousPlayback(bool dmc)
    {
        var source = Machine(dmc); Frame(source); byte[] old = LegacyNesState.FromCurrent(source.SaveState(), 6);
        var clean = Machine(dmc); var busy = Machine(dmc); for (int i = 0; i < 7; i++) Frame(busy);
        clean.LoadState(old); busy.LoadState(old);
        Assert.That(Frame(busy), Is.EqualTo(Frame(clean)), "An old file cannot restore missing filter history, but must start consistently");
    }
}
