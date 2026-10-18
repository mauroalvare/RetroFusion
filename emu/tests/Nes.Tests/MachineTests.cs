using Retro.Systems.Nes;

namespace Nes.Tests;

[TestFixture]
public class MachineTests
{
    /// <summary>NROM-128 image with <paramref name="code"/> at $C000 and the NMI handler at <paramref name="nmiAt"/>.</summary>
    private static Retro.Systems.Nes.Nes Machine(byte[] code, byte[]? nmi = null)
    {
        var data = new byte[16 + NesCartridge.PrgUnit + NesCartridge.ChrUnit];
        "NES\x1A"u8.CopyTo(data);
        data[4] = 1; data[5] = 1;
        var prg = data.AsSpan(16, NesCartridge.PrgUnit);
        code.CopyTo(prg);                                  // $C000 (and $8000)
        nmi ??= new byte[] { 0x40 };                       // RTI
        nmi.CopyTo(prg[0x1000..]);                         // $D000
        prg[0x3FFA] = 0x00; prg[0x3FFB] = 0xD0;            // NMI
        prg[0x3FFC] = 0x00; prg[0x3FFD] = 0xC0;            // reset
        prg[0x3FFE] = 0x00; prg[0x3FFF] = 0xD0;            // IRQ
        return new Retro.Systems.Nes.Nes(NesCartridge.Load(data));
    }

    // LDA #$80 / STA $2000 / JMP *
    private static readonly byte[] EnableNmiAndWait = { 0xA9, 0x80, 0x8D, 0x00, 0x20, 0x4C, 0x05, 0xC0 };
    // INC $00 / RTI
    private static readonly byte[] CountNmi = { 0xE6, 0x00, 0x40 };

    [Test]
    public void Frames_Last29780Cycles_AndRaiseOneNmiEach()
    {
        var nes = Machine(EnableNmiAndWait, CountNmi);
        nes.RunFrame(null);   // the first frame starts at power on
        long start = nes.Cpu2A03.CycleCount;
        for (int i = 0; i < 10; i++)
            nes.RunFrame(null);
        double perFrame = (nes.Cpu2A03.CycleCount - start) / 10.0;
        Assert.That(perFrame, Is.EqualTo(Retro.Systems.Nes.Nes.CyclesPerFrame).Within(3));
        Assert.That(nes.Ram[0], Is.InRange(10, 11), "una NMI por cuadro");
        Assert.That(nes.FrameCount, Is.EqualTo(11));
        Assert.That(nes.FramesPerSecond, Is.EqualTo(60.1).Within(0.05));
    }

    [Test]
    public void Ram_IsMirroredEvery2K_AndPrgRamIsThere()
    {
        var nes = Machine(EnableNmiAndWait);
        nes.Write(0x0812, 0x5A);
        Assert.That(nes.Peek(0x0012), Is.EqualTo(0x5A));
        Assert.That(nes.Peek(0x1812), Is.EqualTo(0x5A));
        nes.Write(0x6123, 0xA5);
        Assert.That(nes.Peek(0x6123), Is.EqualTo(0xA5));
        Assert.That(nes.Peek(0xFFFC), Is.EqualTo(0x00));
        Assert.That(nes.Peek(0xFFFD), Is.EqualTo(0xC0));
    }

    [Test]
    public void Controller_SendsAFirst_ThenOnes()
    {
        var nes = Machine(EnableNmiAndWait);
        nes.SetKey("P1 A", true);
        nes.SetKey("P1 START", true);
        nes.SetKey("P1 RIGHT", true);
        nes.SetKey("P2 B", true);
        nes.Write(0x4016, 1);
        nes.Write(0x4016, 0);
        var bits1 = Enumerable.Range(0, 10).Select(_ => nes.Read(0x4016) & 1).ToArray();
        var bits2 = Enumerable.Range(0, 8).Select(_ => nes.Read(0x4017) & 1).ToArray();
        Assert.That(bits1, Is.EqualTo(new[] { 1, 0, 0, 1, 0, 0, 0, 1, 1, 1 }));
        Assert.That(bits2, Is.EqualTo(new[] { 0, 1, 0, 0, 0, 0, 0, 0 }));

        nes.SetKey("P1 A", false);
        nes.Write(0x4016, 1);   // while the strobe is high, every read returns A
        Assert.That(nes.Read(0x4016) & 1, Is.Zero);
    }

    [Test]
    public void OamDma_CopiesAPageAndStallsTheCpu()
    {
        var nes = Machine(EnableNmiAndWait);
        for (int i = 0; i < 256; i++)
            nes.Write((ushort)(0x0200 + i), (byte)(i ^ 0x5A));
        nes.Write(0x2003, 0x10);   // DMA starts at the current OAM address
        long before = nes.Cpu2A03.CycleCount;
        nes.Write(0x4014, 0x02);
        long stall = nes.Cpu2A03.CycleCount - before;
        Assert.That(stall, Is.InRange(513, 514));
        Assert.That(nes.Ppu.Oam[0x10], Is.EqualTo(0 ^ 0x5A));
        Assert.That(nes.Ppu.Oam[0x0F], Is.EqualTo(255 ^ 0x5A), "da la vuelta en 256");
    }

    [Test]
    public void SaveState_RestoresTheExactRun()
    {
        var nes = Machine(EnableNmiAndWait, CountNmi);
        for (int i = 0; i < 5; i++)
            nes.RunFrame(null);
        var state = nes.SaveState();
        for (int i = 0; i < 7; i++)
            nes.RunFrame(null);
        long cycles = nes.Cpu2A03.CycleCount;
        byte count = nes.Ram[0];
        nes.LoadState(state);
        for (int i = 0; i < 7; i++)
            nes.RunFrame(null);
        Assert.That(nes.Cpu2A03.CycleCount, Is.EqualTo(cycles));
        Assert.That(nes.Ram[0], Is.EqualTo(count));
    }

    [Test]
    public void TheCpuHasNoDecimalMode()
    {
        // SED / LDA #$09 / CLC / ADC #$01 / STA $00 / JMP *
        var nes = Machine(new byte[] { 0xF8, 0xA9, 0x09, 0x18, 0x69, 0x01, 0x85, 0x00, 0x4C, 0x08, 0xC0 });
        nes.RunFrame(null);
        Assert.That(nes.Ram[0], Is.EqualTo(0x0A), "el 2A03 suma en binario aunque D esté prendido");
    }

    private static readonly string HomebrewDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ROMs", "descargados-autores", "NES");

    /// <summary>
    /// The user's free homebrew games (Mojon Twins) reach a real picture: rendering turned
    /// on and several colors on screen, also after pressing START. (Bootee is left out: it
    /// stars Cheril, whose games AGENTS.md excludes as a precaution.)
    /// </summary>
    [TestCase("mojon-twins--cadaverion.nes")]
    [TestCase("mojon-twins--cave-cnrom.nes")]
    [TestCase("mojon-twins--cheman.nes")]
    [TestCase("mojon-twins--super-uwol.nes")]
    public void RealHomebrew_ReachesAPicture(string file)
    {
        string path = Path.Combine(HomebrewDir, file);
        if (!File.Exists(path))
            Assert.Ignore($"No está {path}");
        var nes = new Retro.Systems.Nes.Nes(NesCartridge.Load(File.ReadAllBytes(path)));
        for (int i = 0; i < 300; i++)
            nes.RunFrame(null);
        int colorsAtTitle = nes.Video.Pixels.Distinct().Count();
        nes.QueueKeys(10, "P1 START");
        for (int i = 0; i < 300; i++)
            nes.RunFrame(null);
        int colorsAfter = nes.Video.Pixels.Distinct().Count();
        TestContext.Out.WriteLine($"{file}: {colorsAtTitle} colores en el título, {colorsAfter} después de START, PC={nes.Cpu2A03.PC:X4}");
        Assert.That(nes.Cpu2A03.Jammed, Is.False);
        Assert.That(Math.Max(colorsAtTitle, colorsAfter), Is.GreaterThanOrEqualTo(3), "hay imagen");
    }
}
