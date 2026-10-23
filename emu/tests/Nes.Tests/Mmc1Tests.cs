using Retro.Systems.Nes;

namespace Nes.Tests;

public sealed class Mmc1Tests
{
    private static byte[] Image(int prg = 8, int chr = 4)
    {
        byte[] data = new byte[16 + prg * 16384 + chr * 8192];
        "NES\x1A"u8.CopyTo(data); data[4] = (byte)prg; data[5] = (byte)chr; data[6] = 0x10;
        for (int i = 0; i < prg; i++) Array.Fill(data, (byte)i, 16 + i * 16384, 16384);
        for (int i = 0; i < chr * 2; i++) Array.Fill(data, (byte)i, 16 + prg * 16384 + i * 4096, 4096);
        return data;
    }
    private static void Serial(NesCartridge cart, ushort address, int value)
    {
        for (int i = 0; i < 5; i++) cart.CpuWrite(address, (byte)(value >> i & 1));
    }

    [Test]
    public void FifthWriteSelectsRegisterAndEarlierAddressesDoNotMatter()
    {
        var cart = NesCartridge.Load(Image());
        cart.CpuWrite(0x8000, 1); cart.CpuWrite(0xA000, 1); cart.CpuWrite(0xC000, 0); cart.CpuWrite(0x8000, 0);
        Assert.That(cart.CpuRead(0x8000), Is.Zero);
        cart.CpuWrite(0xFFFF, 0);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(3));
        Assert.That(cart.CpuRead(0xFFFC), Is.EqualTo(7));
    }

    [TestCase(0, 2, 3)] [TestCase(4, 2, 3)] [TestCase(8, 0, 3)] [TestCase(12, 3, 7)]
    public void PrgModesSelect32kOrEitherFixed16kWindow(int control, int lower, int upper)
    {
        var cart = NesCartridge.Load(Image()); Serial(cart, 0xE000, 3); Serial(cart, 0x8000, control);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(lower)); Assert.That(cart.CpuRead(0xFFFF), Is.EqualTo(upper));
        cart.PatchPrg(0x8000, 98); Assert.That(cart.CpuRead(0x8000), Is.EqualTo(98));
    }

    [TestCase(0, 0, 0, 0, 0)] [TestCase(1, 1, 1, 1, 1)]
    [TestCase(2, 0, 1, 0, 1)] [TestCase(3, 0, 0, 1, 1)]
    public void ControlSelectsAllNametableLayouts(int control, int a, int b, int c, int d)
    {
        var cart = NesCartridge.Load(Image()); Serial(cart, 0x8000, control);
        Assert.That(new ushort[] { 0x2000, 0x2400, 0x2800, 0x2C00 }.Select(cart.NametableFor), Is.EqualTo(new[] { a, b, c, d }));
    }

    [Test]
    public void ChrBanksSupport8kPairsAndIndependent4kWindows()
    {
        var cart = NesCartridge.Load(Image()); Serial(cart, 0xA000, 3); Serial(cart, 0xC000, 5);
        Assert.That(cart.PpuRead(0), Is.EqualTo(2)); Assert.That(cart.PpuRead(0x1000), Is.EqualTo(3));
        Serial(cart, 0x8000, 0x1C);
        Assert.That(cart.PpuRead(0), Is.EqualTo(3)); Assert.That(cart.PpuRead(0x1000), Is.EqualTo(5));
        cart.PpuWrite(0, 99); Assert.That(cart.PpuRead(0), Is.EqualTo(3), "CHR ROM remains read-only.");
    }

    [Test]
    public void ChrRamWritesFollowTheSelected4kBank()
    {
        var cart = NesCartridge.Load(Image(chr: 0));
        cart.PpuWrite(0, 17); cart.PpuWrite(0x1000, 29);
        Serial(cart, 0x8000, 0x1C); Serial(cart, 0xA000, 1); Serial(cart, 0xC000, 0);
        Assert.That(cart.PpuRead(0), Is.EqualTo(29)); Assert.That(cart.PpuRead(0x1000), Is.EqualTo(17));
        cart.PpuWrite(0x1000, 41); Serial(cart, 0xA000, 0);
        Assert.That(cart.PpuRead(0), Is.EqualTo(41));
    }

    [Test]
    public void SuromOuterBankAlsoMovesTheFixedLastBank()
    {
        var cart = NesCartridge.Load(Image(32, 0));
        Assert.That(cart.CpuRead(0xC000), Is.EqualTo(15));
        Serial(cart, 0xA000, 0x10); Serial(cart, 0xE000, 3);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(19)); Assert.That(cart.CpuRead(0xC000), Is.EqualTo(31));
    }

    [Test]
    public void SeromKeeps32kPrgFixedDespiteBankRegisters()
    {
        var cart = NesCartridge.Load(Image(2, 4)); Serial(cart, 0xE000, 9); Serial(cart, 0x8000, 0);
        Assert.That(cart.CpuRead(0x8000), Is.Zero); Assert.That(cart.CpuRead(0xC000), Is.EqualTo(1));
    }

    [Test]
    public void ConsecutiveDataWritesAreIgnoredButResetIsAlwaysAccepted()
    {
        var cart = NesCartridge.Load(Image());
        cart.CpuWrite(0xE000, 1, 10); cart.CpuWrite(0xE000, 0, 11); cart.CpuWrite(0xE000, 0, 12);
        for (int i = 0; i < 4; i++) cart.CpuWrite(0xE000, 0, 14 + i * 2);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(1));
        cart.CpuWrite(0x8000, 0, 30); cart.CpuWrite(0x8000, 0x80, 31);
        for (int i = 0; i < 4; i++) cart.CpuWrite(0xE000, 0, 34 + i * 2);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(1), "Reset discarded the unfinished transfer.");
        cart.CpuWrite(0xE000, 0, 42); Assert.That(cart.CpuRead(0x8000), Is.Zero);
    }

    [Test]
    public void Actual6502RmwInstructionsWriteOnlyOneSerialBitEach()
    {
        byte[] data = Image(); int start = 16 + 7 * 16384;
        for (int i = 0; i < 5; i++) new byte[] { 0xEE, 0x00, 0xE1 }.CopyTo(data, start + i * 3);
        data[start + 0x2100] = 0; data[start + 0x3FFC] = 0; data[start + 0x3FFD] = 0xC0;
        var machine = new Retro.Systems.Nes.Nes(NesCartridge.Load(data)); Serial(machine.Cartridge, 0xE000, 3);
        for (int i = 0; i < 5; i++) machine.Cpu2A03.Step();
        Assert.That(machine.Cpu2A03.CycleCount, Is.EqualTo(37));
        Assert.That(machine.Cartridge.CpuRead(0x8000), Is.Zero);
    }

    [Test]
    public void DisabledWramReturnsOpenBusAndIgnoresCpuWrites()
    {
        var machine = new Retro.Systems.Nes.Nes(NesCartridge.Load(Image()));
        machine.Write(0x6000, 72); Serial(machine.Cartridge, 0xE000, 0x10);
        machine.Write(0x6000, 13); machine.Read(0x8000);
        Assert.That(machine.Read(0x6000), Is.Zero);
        Serial(machine.Cartridge, 0xE000, 0); Assert.That(machine.Read(0x6000), Is.EqualTo(72));
    }

    [Test]
    public void StateRestoresAnUnfinishedSerialTransferAndChrRam()
    {
        var machine = new Retro.Systems.Nes.Nes(NesCartridge.Load(Image(chr: 0))); var cart = machine.Cartridge;
        cart.PpuWrite(0x123, 81);
        cart.CpuWrite(0xE000, 1, 100); cart.CpuWrite(0xE000, 0, 102); cart.CpuWrite(0xE000, 1, 104);
        byte[] state = machine.SaveState();
        Serial(cart, 0xE000, 0); cart.PpuWrite(0x123, 0); machine.LoadState(state);
        cart.CpuWrite(0xE000, 0, 106); cart.CpuWrite(0xE000, 0, 108);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(5)); Assert.That(cart.PpuRead(0x123), Is.EqualTo(81));
        machine.Reset(false); Assert.That(cart.CpuRead(0x8000), Is.EqualTo(5));
        machine.Reset(true); Assert.That(cart.CpuRead(0x8000), Is.Zero);
    }

    [Test]
    public void HeaderValidationDoesNotCopyTheRom()
    {
        byte[] image = Image(32, 0); NesCartridge.ValidateImage(image);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) NesCartridge.ValidateImage(image);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(32768));
        image[8] = 4;
        Assert.Throws<InvalidDataException>(() => NesCartridge.ValidateImage(image));
        Assert.Throws<InvalidDataException>(() => NesCartridge.Load(image));
    }
}