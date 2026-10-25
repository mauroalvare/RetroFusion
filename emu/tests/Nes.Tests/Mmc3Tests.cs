using Retro.Systems.Nes;

namespace Nes.Tests;

public sealed class Mmc3Tests
{
    private static byte[] Image(int chr = 4, byte flags = 0x40)
    {
        byte[] data = new byte[16 + 8 * 16384 + chr * 8192];
        "NES\x1A"u8.CopyTo(data); data[4] = 8; data[5] = (byte)chr; data[6] = flags;
        for (int i = 0; i < 16; i++) Array.Fill(data, (byte)i, 16 + i * 8192, 8192);
        for (int i = 0; i < chr * 8; i++) Array.Fill(data, (byte)i, 16 + 8 * 16384 + i * 1024, 1024);
        return data;
    }
    private static void Bank(NesCartridge c, byte register, byte value) { c.CpuWrite(0x8000, register); c.CpuWrite(0x8001, value); }
    private static void Edge(NesCartridge c, int low = 10)
    {
        c.ObservePpuAddress(0); for (int i = 0; i < low; i++) c.ClockPpuDot(); c.ObservePpuAddress(0x1000);
    }
    private static void Irq(NesCartridge c, byte latch) { c.CpuWrite(0xC000, latch); c.CpuWrite(0xC001, 0); c.CpuWrite(0xE001, 0); }

    [Test]
    public void PrgSwitches8kSlotsAndKeepsVectorsFixedInBothModes()
    {
        var c = NesCartridge.Load(Image()); Bank(c, 6, 3); Bank(c, 7, 5);
        Assert.That(new ushort[] { 0x8000, 0xA000, 0xC000, 0xFFFC }.Select(c.CpuRead), Is.EqualTo(new[] { 3, 5, 14, 15 }));
        c.CpuWrite(0x9FFE, 0x46);
        Assert.That(new ushort[] { 0x8000, 0xBFFF, 0xC000, 0xFFFF }.Select(c.CpuRead), Is.EqualTo(new[] { 14, 5, 3, 15 }));
        c.PatchPrg(0xC000, 98); c.CpuWrite(0x8000, 6); Assert.That(c.CpuRead(0x8000), Is.EqualTo(98));
        Bank(c, 6, 0xFF); Assert.That(c.CpuRead(0x8000), Is.EqualTo(15), "PRG D6/7 are disconnected.");
    }
    [Test]
    public void ChrPairsIgnoreLowBitAndInversionMovesTheWhole4kHalf()
    {
        var c = NesCartridge.Load(Image());
        for (byte i = 0; i < 6; i++) Bank(c, i, (byte)(i + 9));
        Assert.That(Enumerable.Range(0, 8).Select(i => c.PpuRead((ushort)(i * 1024))), Is.EqualTo(new[] { 8, 9, 10, 11, 11, 12, 13, 14 }));
        c.CpuWrite(0x8000, 0x80);
        Assert.That(Enumerable.Range(0, 8).Select(i => c.PpuRead((ushort)(i * 1024))), Is.EqualTo(new[] { 11, 12, 13, 14, 8, 9, 10, 11 }));
        c.PpuWrite(0, 99); Assert.That(c.PpuRead(0), Is.EqualTo(11));
    }
    [Test]
    public void ChrRamFollows1kBanksAndMirroringRespectsFourScreenWiring()
    {
        var c = NesCartridge.Load(Image(0)); Bank(c, 2, 7); c.PpuWrite(0x1000, 37);
        Bank(c, 3, 7); Assert.That(c.PpuRead(0x1400), Is.EqualTo(37));
        c.CpuWrite(0xA000, 0); Assert.That(c.NametableFor(0x2400), Is.EqualTo(1));
        c.CpuWrite(0xBFFE, 1); Assert.That(c.NametableFor(0x2400), Is.Zero);
        var four = NesCartridge.Load(Image(flags: 0x48)); four.CpuWrite(0xA000, 1);
        Assert.That(Enumerable.Range(0, 4).Select(i => four.NametableFor((ushort)(0x2000 + i * 1024))), Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }
    [Test]
    public void WorkRamEnableAndWriteProtectionReachTheActualCpuBus()
    {
        var m = new Retro.Systems.Nes.Nes(NesCartridge.Load(Image()));
        m.Cartridge.CpuWrite(0xA001, 0x80); m.Write(0x6123, 17); Assert.That(m.Read(0x6123), Is.EqualTo(17));
        m.Cartridge.CpuWrite(0xA001, 0xC0); m.Write(0x6123, 98); Assert.That(m.Read(0x6123), Is.EqualTo(17));
        m.Cartridge.CpuWrite(0xA001, 0); m.Write(0x6123, 65); Assert.That(m.Read(0x6123), Is.EqualTo(65), "Disabled RAM returns open bus.");
        m.Cartridge.CpuWrite(0xA001, 0x80); Assert.That(m.Read(0x6123), Is.EqualTo(17));
    }
    [Test]
    public void ShortA12PulsesDoNotClockAndHighLevelDoesNotRepeat()
    {
        var c = NesCartridge.Load(Image()); Irq(c, 1); Edge(c);
        for (int i = 0; i < 200; i++) { c.ClockPpuDot(); c.ObservePpuAddress(0x1FFF); }
        Assert.That(c.Irq, Is.False);
        Edge(c, 9); Assert.That(c.Irq, Is.False); Edge(c, 10); Assert.That(c.Irq, Is.True);
        c.CpuWrite(0xE000, 0); Assert.That(c.Irq, Is.False);
    }
    [Test]
    public void ReloadIsDeferredAndZeroLatchUsesMmc3BcBehavior()
    {
        var c = NesCartridge.Load(Image()); Irq(c, 4); Edge(c); Edge(c);
        c.CpuWrite(0xC000, 1); Assert.That(c.Irq, Is.False);
        c.CpuWrite(0xC001, 0); Edge(c); Assert.That(c.Irq, Is.False); Edge(c); Assert.That(c.Irq, Is.True);
        c.CpuWrite(0xE000, 0); c.CpuWrite(0xC000, 0); c.CpuWrite(0xE001, 0);
        Edge(c); Assert.That(c.Irq, Is.True);
    }
    [TestCase(0x08, 261)] [TestCase(0x10, 325)]
    public void PpuDrivesOneFilteredIrqClockPerRenderedLine(int control, int edgeDot)
    {
        var c = NesCartridge.Load(Image()); var p = new Ppu(c);
        p.WriteRegister(0x2000, (byte)control); p.WriteRegister(0x2001, 0x18);
        while (p.Line < 1) p.Step();
        Irq(c, 2);
        while (p.Line < 3 || p.Dot <= edgeDot) { p.Step(); if (p.Line < 3) Assert.That(c.Irq, Is.False); }
        Assert.That(c.Irq, Is.True);
    }
    [Test]
    public void DebuggerReadsAndDisabledRenderingDoNotGenerateIrqsButCpuAddressWritesDo()
    {
        var c = NesCartridge.Load(Image()); var p = new Ppu(c); Irq(c, 0);
        for (int i = 0; i < 1000; i++) { p.Read(0); p.Read(0x1000); p.Step(); }
        Assert.That(c.Irq, Is.False);
        p.WriteRegister(0x2006, 0x10); p.WriteRegister(0x2006, 0);
        Assert.That(c.Irq, Is.True);
    }
    [Test]
    public void SavedStatePreservesPendingEdgeCounterProtectionAndBanks()
    {
        var m = new Retro.Systems.Nes.Nes(NesCartridge.Load(Image(0))); var c = m.Cartridge;
        Bank(c, 6, 5); c.CpuWrite(0xA000, 0); c.CpuWrite(0xA001, 0xC0); Irq(c, 1); Edge(c);
        c.ObservePpuAddress(0); for (int i = 0; i < 9; i++) c.ClockPpuDot();
        byte[] state = m.SaveState(); c.ClockPpuDot(); c.ObservePpuAddress(0x1000); Assert.That(m.IrqLine, Is.True);
        c.CpuWrite(0x8000, 0); c.CpuWrite(0xA000, 1); m.LoadState(state);
        Assert.That(c.CpuRead(0x8000), Is.EqualTo(5)); Assert.That(c.Mirroring, Is.EqualTo(NesMirroring.Vertical));
        Assert.That(c.PrgRamWritable, Is.False); Assert.That(c.Irq, Is.False);
        c.ClockPpuDot(); c.ObservePpuAddress(0x1000); Assert.That(m.IrqLine, Is.True);
        m.Reset(true); Assert.That(c.Irq, Is.False); Assert.That(c.CpuRead(0x8000), Is.Zero);
    }
    [Test]
    public void UnsupportedMmc6AndExpandedRamAreRejectedRatherThanMisEmulated()
    {
        byte[] image = Image(); image[7] = 8; image[8] = 0x10;
        Assert.That(() => NesCartridge.Load(image), Throws.TypeOf<InvalidDataException>().With.Message.Contains("MMC6"));
        image[8] = 0; image[10] = 8;
        Assert.That(() => NesCartridge.Load(image), Throws.TypeOf<InvalidDataException>());
    }
}