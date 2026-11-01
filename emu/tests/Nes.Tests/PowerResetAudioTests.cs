using Retro.Systems.Nes;

namespace Nes.Tests;

public sealed class PowerResetAudioTests
{
    private static void Play(Apu apu, int channel)
    {
        apu.Write(0x4017, 0xC0);
        apu.Write(0x4015, 0x0F);
        if (channel < 2)
        {
            ushort first = (ushort)(0x4000 + channel * 4);
            apu.Write(first, 0xBF); apu.Write((ushort)(first + 2), 37); apu.Write((ushort)(first + 3), 0);
        }
        else if (channel == 2)
        {
            apu.Write(0x4008, 0xFF); apu.Write(0x400A, 19); apu.Write(0x400B, 0);
        }
        else
        {
            apu.Write(0x400C, 0x3F); apu.Write(0x400E, 5); apu.Write(0x400F, 0);
        }
    }

    [TestCase(0, 1)] [TestCase(0, 5001)] [TestCase(1, 19999)]
    [TestCase(2, 5001)] [TestCase(3, 19999)]
    public void PowerResetRestartsEveryWaveformFromItsInitialPhase(int channel, int previousClocks)
    {
        var busy = new Apu(); busy.Reset(); Play(busy, channel);
        for (int i = 0; i < previousClocks; i++) busy.Step();
        busy.Reset(); Play(busy, channel);
        var clean = new Apu(); clean.Reset(); Play(clean, channel);
        var actual = new float[36000]; var expected = new float[36000];
        for (int i = 0; i < actual.Length; i++)
        {
            busy.Step(); clean.Step(); actual[i] = busy.Output; expected[i] = clean.Output;
        }
        Assert.That(expected.Distinct().Count(), Is.GreaterThan(1), "The channel must actually be audible");
        Assert.That(actual, Is.EqualTo(expected), "Powering on must not reuse the previous timer, sequencer or envelope");
    }

    [Test]
    public void PowerOnDoesNotClockTheDmcOutputTimerImmediately()
    {
        var apu = new Apu { ExternalDmcDma = true }; apu.Reset();
        apu.Write(0x4011, 64); apu.Write(0x4015, 0x10); apu.CompleteDmcDma(255);
        float initial = apu.Output;
        for (int i = 0; i < 9 * 428 - 1; i++)
        {
            apu.Step();
            Assert.That(apu.Output, Is.EqualTo(initial), "Eight silent bits precede the first buffered output bit");
        }
        apu.Step();
        Assert.That(apu.Output, Is.GreaterThan(initial));
    }

    [TestCase(0)] [TestCase(2)] [TestCase(3)]
    public void TheMachineForwardsPowerResetToItsAudioChannels(int channel)
    {
        Retro.Systems.Nes.Nes Machine()
        {
            var rom = new byte[16 + 16384 + 8192]; "NES\x1A"u8.CopyTo(rom); rom[4] = rom[5] = 1;
            new byte[] { 0x78, 0x4C, 1, 0x80 }.CopyTo(rom, 16); rom[16 + 0x3FFD] = 0x80;
            return new(NesCartridge.Load(rom));
        }
        var busy = Machine(); Play(busy.Apu, channel); busy.RunFrame(null); busy.StepInstruction();
        busy.Reset(true); Play(busy.Apu, channel);
        var clean = Machine(); Play(clean.Apu, channel);
        for (int frame = 0; frame < 2; frame++)
        {
            busy.RunFrame(null); clean.RunFrame(null);
            var actual = new short[4096]; var expected = new short[4096];
            int count = clean.DrainAudio(expected);
            Assert.That(busy.DrainAudio(actual), Is.EqualTo(count));
            Assert.That(actual[..count], Is.EqualTo(expected[..count]));
        }
    }
}
