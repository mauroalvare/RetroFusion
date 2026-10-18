using Retro.Core;
using Retro.Systems.Arcade;

namespace Arcade.Tests;

/// <summary>
/// The Space Invaders board tested with a small hand-assembled 8080 program (the game
/// ROM is not part of the repository): interrupt timing, barrel shifter, input ports,
/// video rotation with the colour overlay, sound triggers and save states.
/// </summary>
[TestFixture]
public class SpaceInvadersBoardTests
{
    /// <summary>
    /// 0000  JMP $0040
    /// 0008  JMP $0080   (RST 1 vector, like the real game)
    /// 0010  JMP $0090   (RST 2 vector)
    /// 0040  LXI SP,$2400 / EI / JMP $0044 (idle loop)
    /// 0080  PUSH PSW / LDA $2000 / INR A / STA $2000 / POP PSW / EI / RET
    /// 0090  PUSH PSW / LDA $2001 / INR A / STA $2001 / POP PSW / EI / RET
    /// </summary>
    private static byte[] TestRom()
    {
        var rom = new byte[0x2000];
        new byte[] { 0xC3, 0x40, 0x00 }.CopyTo(rom, 0x0000);
        new byte[] { 0xC3, 0x80, 0x00 }.CopyTo(rom, 0x0008);
        new byte[] { 0xC3, 0x90, 0x00 }.CopyTo(rom, 0x0010);
        new byte[] { 0x31, 0x00, 0x24, 0xFB, 0xC3, 0x44, 0x00 }.CopyTo(rom, 0x0040);
        new byte[] { 0xF5, 0x3A, 0x00, 0x20, 0x3C, 0x32, 0x00, 0x20, 0xF1, 0xFB, 0xC9 }.CopyTo(rom, 0x0080);
        new byte[] { 0xF5, 0x3A, 0x01, 0x20, 0x3C, 0x32, 0x01, 0x20, 0xF1, 0xFB, 0xC9 }.CopyTo(rom, 0x0090);
        return rom;
    }

    [Test]
    public void Timing_Is59_54HzWith128CyclesPerLine()
    {
        var m = new SpaceInvaders(TestRom());
        Assert.That(SpaceInvaders.CyclesPerFrame, Is.EqualTo(33536));
        Assert.That(m.FramesPerSecond, Is.EqualTo(59.54).Within(0.01));
    }

    [Test]
    public void Interrupts_TwoPerFrameAtLines96And224()
    {
        var m = new SpaceInvaders(TestRom());
        for (int i = 0; i < 60; i++)
            m.RunFrame(null);
        Assert.That(m.Peek(0x2000), Is.EqualTo(60), "RST 1 por cuadro");
        Assert.That(m.Peek(0x2001), Is.EqualTo(60), "RST 2 por cuadro");

        // Stop exactly where each handler starts and check the beam position.
        var debug = new DebugController();
        debug.Add(0x0008);
        Assert.That(m.RunFrame(debug), Is.EqualTo(RunStopReason.Breakpoint));
        long c1 = m.FrameCycle;
        Assert.That(c1, Is.InRange(96 * 128 + 11, 96 * 128 + 11 + 10), "RST 1 en la línea 96 (+11 ciclos del RST)");
        debug.Clear();
        debug.Add(0x0010);
        m.ResumeFromBreakpoint();
        Assert.That(m.RunFrame(debug), Is.EqualTo(RunStopReason.Breakpoint));
        long c2 = m.FrameCycle;
        Assert.That(c2, Is.InRange(224 * 128 + 11, 224 * 128 + 11 + 10), "RST 2 en la línea 224");
    }

    [Test]
    public void Interrupt_WaitsWhileDisabled()
    {
        var rom = TestRom();
        rom[0x0043] = 0x00;                               // no EI in the main program
        var m = new SpaceInvaders(rom);
        for (int i = 0; i < 5; i++)
            m.RunFrame(null);
        Assert.That(m.Peek(0x2000), Is.EqualTo(0));
        Assert.That(m.Peek(0x2001), Is.EqualTo(0));
    }

    [Test]
    public void Shifter_ReturnsWindowOfLast16BitsWritten()
    {
        var m = new SpaceInvaders(TestRom());
        m.Out(4, 0xAB);
        m.Out(4, 0xCD);                                   // shift register = $CDAB
        for (int offset = 0; offset < 8; offset++)
        {
            m.Out(2, (byte)offset);
            Assert.That(m.In(3), Is.EqualTo((byte)(0xCDAB >> (8 - offset))), $"desplazamiento {offset}");
        }
        m.Out(4, 0x12);                                   // $12CD
        m.Out(2, 0);
        Assert.That(m.In(3), Is.EqualTo(0x12));
    }

    [Test]
    public void InputPorts_ReflectControlsAndDipSwitches()
    {
        var m = new SpaceInvaders(TestRom());
        Assert.That(m.In(1), Is.EqualTo(0x08), "bit 3 siempre en 1");
        m.SetKey("COIN", true);
        m.SetKey("P1 START", true);
        m.SetKey("P1 FIRE", true);
        m.SetKey("P1 LEFT", true);
        Assert.That(m.In(1), Is.EqualTo(0x01 | 0x04 | 0x08 | 0x10 | 0x20));
        m.ReleaseAllKeys();
        m.Ships = 5;
        m.BonusAt1000 = true;
        m.ShowCoinInfo = false;
        m.SetKey("P2 RIGHT", true);
        Assert.That(m.In(2), Is.EqualTo(0x02 | 0x08 | 0x40 | 0x80));
    }

    [Test]
    public void Video_IsRotatedWithColourOverlay()
    {
        var m = new SpaceInvaders(TestRom());
        // hardware line 10: pixel x=0 (bit 0 of byte 0) -> screen bottom row
        m.Poke(0x2400 + 10 * 32 + 0, 0x01);
        // hardware line 100: pixel x=215 (byte 26, bit 7) -> screen row 40 (red band)
        m.Poke(0x2400 + 100 * 32 + 26, 0x80);
        // hardware line 120: pixel x=55 (byte 6, bit 7) -> screen row 200 (green band)
        m.Poke(0x2400 + 120 * 32 + 6, 0x80);
        m.RunFrame(null);
        int[] px = m.Video.Pixels;
        int W = SpaceInvaders.ScreenWidth;
        Assert.That(px[255 * W + 10], Is.EqualTo(unchecked((int)0xFFF4F4F4)), "fila inferior, columna 10: blanco");
        Assert.That(px[40 * W + 100], Is.EqualTo(unchecked((int)0xFFFF3030)), "franja roja del OVNI");
        Assert.That(px[200 * W + 120], Is.EqualTo(unchecked((int)0xFF30FF50)), "franja verde de las bases");
        Assert.That(px[200 * W + 121], Is.EqualTo(unchecked((int)0xFF000000)), "píxel apagado: negro");
    }

    [Test]
    public void Sound_TriggersOnRisingEdgesOnlyWithAmplifier()
    {
        var m = new SpaceInvaders(TestRom());
        var buf = new short[4096];
        m.RunFrame(null);
        m.DrainAudio(buf);

        m.Out(3, 0x02);                                   // shot, amplifier off
        m.RunFrame(null);
        int n = m.DrainAudio(buf);
        Assert.That(Rms(buf, n), Is.LessThan(1), "sin amplificador no suena");

        m.Out(3, 0x20);                                   // amplifier on, shot bit low
        m.Out(3, 0x22);                                   // rising edge of the shot bit
        m.RunFrame(null);
        n = m.DrainAudio(buf);
        Assert.That(n, Is.InRange(735, 746), "muestras por cuadro a 44,1 kHz");
        Assert.That(Rms(buf, n), Is.GreaterThan(1000), "el disparo suena");

        m.Out(5, 0x01);                                   // fleet note 1
        m.RunFrame(null);
        n = m.DrainAudio(buf);
        Assert.That(Rms(buf, n), Is.GreaterThan(1000), "la nota de la flota suena");
    }

    private static double Rms(short[] s, int n) => n == 0 ? 0 : Math.Sqrt(s.Take(n).Average(x => (double)x * x));

    [Test]
    public void SaveState_RestoresExactly()
    {
        var m = new SpaceInvaders(TestRom());
        for (int i = 0; i < 10; i++) m.RunFrame(null);
        m.Poke(0x2400 + 5 * 32, 0xFF);
        byte[] state = m.SaveState();
        for (int i = 0; i < 20; i++) m.RunFrame(null);
        int[] after = (int[])m.Video.Pixels.Clone();

        var m2 = new SpaceInvaders(TestRom());
        m2.LoadState(state);
        for (int i = 0; i < 20; i++) m2.RunFrame(null);
        Assert.That(m2.Video.Pixels, Is.EqualTo(after));
        Assert.That(m2.Peek(0x2000), Is.EqualTo(m.Peek(0x2000)));
    }

    [Test]
    public void Rom_ProbesMameChipNames()
    {
        var chips = new[] { "invaders.h", "invaders.g", "invaders.f", "invaders.e" }
            .Select(n => new MediaFile(n, new byte[0x800], "test")).ToList();
        Assert.That(SpaceInvadersRom.Probe(chips), Is.EqualTo(100));
        var (rom, description) = SpaceInvadersRom.Build(chips);
        Assert.That(rom.Length, Is.EqualTo(0x2000));
        Assert.That(description, Does.Contain("no coincide"), "ceros no pasan la verificación de CRC");
        Assert.That(SpaceInvadersRom.Probe(new[] { new MediaFile("Space Invaders (1980)(Atari)(US).xex", new byte[4184], "test") }), Is.EqualTo(0),
            "el Space Invaders de Atari 8-bit no es la placa arcade");
    }
}
