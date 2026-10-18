using Retro.Systems.Nes;

namespace Nes.Tests;

[TestFixture]
public class ApuTests
{
    /// <summary>A NES whose program only waits (JMP *) with interrupts disabled.</summary>
    private static Retro.Systems.Nes.Nes Idle()
    {
        var data = new byte[16 + NesCartridge.PrgUnit + NesCartridge.ChrUnit];
        "NES\x1A"u8.CopyTo(data);
        data[4] = 1; data[5] = 1;
        var prg = data.AsSpan(16, NesCartridge.PrgUnit);
        new byte[] { 0x78, 0x4C, 0x01, 0xC0 }.CopyTo(prg);   // SEI / JMP $C001
        prg[0x3FFC] = 0x00; prg[0x3FFD] = 0xC0;
        // DMC sample at $C040: alternating bits
        for (int i = 0; i < 64; i++) prg[0x40 + i] = 0x55;
        return new Retro.Systems.Nes.Nes(NesCartridge.Load(data));
    }

    private static short[] Run(Retro.Systems.Nes.Nes nes, int frames)
    {
        var all = new List<short>();
        var buf = new short[4096];
        for (int i = 0; i < frames; i++)
        {
            nes.RunFrame(null);
            int n = nes.DrainAudio(buf);
            all.AddRange(buf.Take(n));
        }
        return all.ToArray();
    }

    /// <summary>Frequency from the rising zero crossings (the high-pass centers the wave on zero).</summary>
    private static double Frequency(short[] s)
    {
        int first = -1, last = -1, crossings = 0;
        for (int i = 1; i < s.Length; i++)
            if (s[i - 1] < 0 && s[i] >= 0)
            {
                if (first < 0) first = i;
                else crossings++;
                last = i;
            }
        return crossings * 44100.0 / (last - first);
    }

    [Test]
    public void Status_ReportsLengthCounters_AndTheyRunOut()
    {
        var nes = Idle();
        nes.Write(0x4017, 0x40);          // 4 steps, no IRQ
        nes.Write(0x4015, 0x0F);
        nes.Write(0x4000, 0x10);          // length counter not halted
        nes.Write(0x4003, 0x18);          // length index 3 -> 2 half frames
        nes.Write(0x400F, 0x08);          // noise: index 1 -> 254
        Assert.That(nes.Apu.Status & 0x0F, Is.EqualTo(0x09));
        Run(nes, 3);
        Assert.That(nes.Apu.Status & 0x01, Is.Zero, "el pulso 1 se apaga tras dos medios cuadros");
        Assert.That(nes.Apu.Status & 0x08, Is.EqualTo(0x08), "el ruido sigue");
        nes.Write(0x4015, 0x00);
        Assert.That(nes.Apu.Status & 0x0F, Is.Zero, "deshabilitar borra los contadores");
    }

    [Test]
    public void FrameCounter_RaisesIrqInFourStepMode_AndReadingClearsIt()
    {
        var nes = Idle();
        nes.Write(0x4017, 0x00);
        Run(nes, 2);
        Assert.That(nes.IrqLine, Is.True);
        Assert.That(nes.Read(0x4015) & 0x40, Is.EqualTo(0x40));
        Assert.That(nes.IrqLine, Is.False, "leer $4015 baja la IRQ del contador");

        nes.Write(0x4017, 0x80);          // 5 steps: never an IRQ
        nes.Read(0x4015);
        Run(nes, 3);
        Assert.That(nes.IrqLine, Is.False);
        nes.Write(0x4017, 0x40);          // inhibited
        Run(nes, 3);
        Assert.That(nes.IrqLine, Is.False);
    }

    [Test]
    public void Pulse_Plays440Hz()
    {
        var nes = Idle();
        nes.Write(0x4017, 0x40);
        nes.Write(0x4015, 0x01);
        nes.Write(0x4000, 0xBF);          // duty 50 %, length halted, constant volume 15
        nes.Write(0x4001, 0x00);
        nes.Write(0x4002, 0xFD);          // 1789773 / (16 x 254) = 440.4 Hz
        nes.Write(0x4003, 0x00);
        var s = Run(nes, 60).Skip(4410).ToArray();
        Assert.That(s.Max() - s.Min(), Is.GreaterThan(4000), "se oye");
        Assert.That(Frequency(s), Is.EqualTo(440.4).Within(2));
    }

    [Test]
    public void Triangle_PlaysAnOctaveBelowThePulseWithTheSamePeriod()
    {
        var nes = Idle();
        nes.Write(0x4017, 0x40);
        nes.Write(0x4015, 0x04);
        nes.Write(0x4008, 0xFF);          // control on: linear counter keeps reloading
        nes.Write(0x400A, 0xFD);
        nes.Write(0x400B, 0x00);
        var s = Run(nes, 60).Skip(4410).ToArray();
        Assert.That(s.Max() - s.Min(), Is.GreaterThan(4000));
        Assert.That(Frequency(s), Is.EqualTo(220.2).Within(2));
    }

    [Test]
    public void Silence_WhenNothingPlays_AndNoiseMakesSound()
    {
        var nes = Idle();
        nes.Write(0x4017, 0x40);
        var quiet = Run(nes, 30).Skip(4410).ToArray();
        Assert.That(quiet.Max() - quiet.Min(), Is.LessThan(50));
        nes.Write(0x4015, 0x08);
        nes.Write(0x400C, 0x3F);
        nes.Write(0x400E, 0x04);
        nes.Write(0x400F, 0x00);
        var noise = Run(nes, 30).Skip(4410).ToArray();
        Assert.That(noise.Max() - noise.Min(), Is.GreaterThan(4000));
        Assert.That(noise.Distinct().Count(), Is.GreaterThan(100), "no es un tono fijo");
    }

    [Test]
    public void Dmc_PlaysItsSample_AndRaisesIrqAtTheEnd()
    {
        var nes = Idle();
        nes.Write(0x4017, 0x40);
        nes.Write(0x4010, 0x8F);          // IRQ on, no loop, fastest rate
        nes.Write(0x4011, 0x40);
        nes.Write(0x4012, 0x01);          // $C040
        nes.Write(0x4013, 0x01);          // 17 bytes
        nes.Write(0x4015, 0x10);
        Assert.That(nes.Apu.Status & 0x10, Is.EqualTo(0x10));
        Run(nes, 2);
        Assert.That(nes.Apu.Status & 0x10, Is.Zero, "terminó la muestra");
        Assert.That(nes.Apu.Status & 0x80, Is.EqualTo(0x80));
        Assert.That(nes.IrqLine, Is.True);
        nes.Write(0x4015, 0x00);
        Assert.That(nes.IrqLine, Is.False, "escribir $4015 baja la IRQ del DMC");
    }
}
