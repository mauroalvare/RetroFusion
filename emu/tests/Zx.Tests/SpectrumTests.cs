using Retro.Core;
using Retro.Systems.Spectrum;
using Retro.Systems.Spectrum.Tape;

namespace Retro.Systems.Spectrum.Tests;

[TestFixture]
public class SpectrumTests
{
    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "roms")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("roms/");
        }
    }

    private static Spectrum48 NewMachine() => new(File.ReadAllBytes(Path.Combine(Root, "roms", "48.rom")));

    private static void Run(Spectrum48 m, int frames)
    {
        for (int i = 0; i < frames; i++)
            m.RunFrame(null);
    }

    private static void RunUntilIdle(Spectrum48 m, int maxFrames = 2000)
    {
        for (int i = 0; i < maxFrames && m.TypingInProgress; i++)
            m.RunFrame(null);
        Run(m, 25);
    }

    [Test]
    public void Boot_ShowsSinclairCopyright()
    {
        var m = NewMachine();
        Run(m, 150);
        string[] text = ScreenText.Read(m);
        Assert.That(text[23], Does.Contain("© 1982 Sinclair Research Ltd"), ScreenText.ReadAll(m));
    }

    [Test]
    public void Frame_Is69888TStatesAnd50Hz()
    {
        var m = NewMachine();
        Run(m, 10);
        Assert.That(m.Z80.T, Is.GreaterThanOrEqualTo(10L * 69888).And.LessThan(10L * 69888 + 30));
        Assert.That(m.FramesPerSecond, Is.EqualTo(50.08).Within(0.01));
    }

    [Test]
    public void Keyboard_BasicCommandIsExecuted()
    {
        var m = NewMachine();
        Run(m, 150);
        // K mode: P = PRINT. Then 2, SYMBOL SHIFT+K = '+', 2, ENTER.
        foreach (var keys in new[] { new[] { "P" }, new[] { "2" }, new[] { "SYMBOL SHIFT", "K" }, new[] { "2" }, new[] { "ENTER" } })
        {
            m.QueueKeys(4, keys);
            m.QueueKeys(4);
        }
        RunUntilIdle(m);
        string[] text = ScreenText.Read(m);
        Assert.That(text[0].TrimEnd(), Is.EqualTo("4"), ScreenText.ReadAll(m));
        Assert.That(text[23], Does.Contain("0 OK"), ScreenText.ReadAll(m));
    }

    /// <summary>A real tape: header + data blocks for 10 PRINT "RETRO OK" (auto-run).</summary>
    internal static byte[] BuildBasicTap()
    {
        var line = new List<byte> { 0xF5, (byte)'"' };       // PRINT "
        line.AddRange("RETRO OK"u8.ToArray());
        line.Add((byte)'"');
        line.Add(0x0D);
        var program = new List<byte> { 0x00, 0x0A, (byte)line.Count, 0x00 };
        program.AddRange(line);

        var header = new List<byte> { 0x00, 0x00 };          // flag, type Program
        header.AddRange("TEST      "u8.ToArray());
        header.AddRange(new[] { (byte)program.Count, (byte)0, (byte)10, (byte)0, (byte)program.Count, (byte)0 });
        var data = new List<byte> { 0xFF };
        data.AddRange(program);

        var tap = new List<byte>();
        foreach (var block in new[] { header, data })
        {
            byte checksum = 0;
            foreach (var b in block)
                checksum ^= b;
            block.Add(checksum);
            tap.Add((byte)block.Count);
            tap.Add((byte)(block.Count >> 8));
            tap.AddRange(block);
        }
        return tap.ToArray();
    }

    [Test]
    public void Tape_RomLoaderReadsRealSignal()
    {
        var m = NewMachine();
        m.InsertTape(TapLoader.Load(BuildBasicTap(), "test.tap"));
        Run(m, 150);
        m.TypeLoadCommand();
        for (int i = 0; i < 3000 && !(m.Tape.AtEnd && !m.TypingInProgress); i++)
            m.RunFrame(null);
        Run(m, 50);
        string all = ScreenText.ReadAll(m);
        Assert.That(m.Tape.AtEnd, Is.True, "la cinta no terminó de reproducirse");
        Assert.That(all, Does.Contain("RETRO OK"), all);
        Assert.That(all, Does.Contain("0 OK, 10:1"), all);
    }

    [Test]
    public void Tap_RejectsC64RawTape()
    {
        var c64 = new byte[64];
        "C64-TAPE-RAW"u8.ToArray().CopyTo(c64, 0);
        Assert.That(TapLoader.Probe(c64), Is.False);
        Assert.That(SpectrumMedia.Probe(new MediaFile("juego.tap", c64, "test")), Is.EqualTo(0));
    }

    [Test]
    public void Contention_MatchesUlaPattern()
    {
        var m = NewMachine();
        // first contended T-state of the frame: 14335, pattern 6,5,4,3,2,1,0,0
        int[] expected = { 6, 5, 4, 3, 2, 1, 0, 0, 6 };
        for (int i = 0; i < expected.Length; i++)
            Assert.That(m.Contention(0x4000, 14335 + i), Is.EqualTo(expected[i]), $"T={14335 + i}");
        Assert.That(m.Contention(0x4000, 14334), Is.EqualTo(0));
        Assert.That(m.Contention(0x4000, 14335 + 128), Is.EqualTo(0), "borde derecho sin contención");
        Assert.That(m.Contention(0x4000, 14335 + 224), Is.EqualTo(6), "línea siguiente");
        Assert.That(m.Contention(0x8000, 14335), Is.EqualTo(0), "RAM alta sin contención");
        Assert.That(m.Contention(0x4000, 14335 + 192 * 224), Is.EqualTo(0), "borde inferior");
    }

    [Test]
    public void Contention_SlowsCodeInLowRam()
    {
        long Measure(ushort address)
        {
            var m = NewMachine();
            for (int i = 0; i < 200; i++)
                m.Poke((uint)(address + i), 0x00);   // NOPs
            m.Poke((uint)(address + 200), 0x76);     // HALT
            var cpu = m.Z80;
            cpu.IFF1 = cpu.IFF2 = false;
            // run until the paper area starts, then time 200 NOPs
            while (m.FrameTState < 20000)
                m.StepInstruction();
            cpu.PC = address;
            long start = cpu.T;
            for (int i = 0; i < 200; i++)
                m.StepInstruction();
            return cpu.T - start;
        }

        Assert.That(Measure(0x8000), Is.EqualTo(800));
        Assert.That(Measure(0x6000), Is.GreaterThan(800));
    }

    [Test]
    public void Interrupts_HappenOncePerFrame()
    {
        var m = NewMachine();
        Run(m, 100);
        // The ROM interrupt handler increments FRAMES (23672) once per interrupt.
        int Frames() => m.Peek(23672) | (m.Peek(23673) << 8) | (m.Peek(23674) << 16);
        int before = Frames();
        Run(m, 50);
        Assert.That(Frames() - before, Is.EqualTo(50));
    }

    [Test]
    public void FloatingBus_ReturnsFFInBorder()
    {
        var m = NewMachine();
        Run(m, 1);
        Assert.That(m.FrameTState, Is.LessThan(14000));
        Assert.That(m.ReadPort(0x40FF), Is.EqualTo(0xFF));
    }

    [Test]
    public void SaveState_RestoresExactly()
    {
        var m = NewMachine();
        m.InsertTape(TapLoader.Load(BuildBasicTap(), "test.tap"));
        Run(m, 150);
        m.TypeLoadCommand();
        Run(m, 200);
        byte[] state = m.SaveState();
        Run(m, 300);
        int[] after = (int[])m.Video.Pixels.Clone();
        long t = m.Z80.T;

        var m2 = NewMachine();
        m2.InsertTape(TapLoader.Load(BuildBasicTap(), "test.tap"));
        m2.LoadState(state);
        Run(m2, 300);
        Assert.That(m2.Video.Pixels, Is.EqualTo(after));
        Assert.That(m2.Z80.PC, Is.EqualTo(m.Z80.PC));
        Assert.That(m2.Z80.T % 69888, Is.EqualTo(t % 69888));
    }
}
