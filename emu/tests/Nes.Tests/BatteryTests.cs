using Retro.Systems.Nes;
using NesMachine = Retro.Systems.Nes.Nes;

namespace Nes.Tests;

public sealed class BatteryTests
{
    private static byte[] Image(bool battery = true, bool nes2 = false, byte ram = 0x70, byte chrRam = 0)
    {
        var image = new byte[16 + NesCartridge.PrgUnit];
        "NES\x1a"u8.CopyTo(image); image[4] = 1; image[6] = (byte)(battery ? 2 : 0);
        if (nes2) { image[7] = 8; image[10] = ram; image[11] = chrRam; }
        image[16] = 0x4c; image[17] = 0x00; image[18] = 0xc0;
        image[^4] = 0x00; image[^3] = 0xc0;
        return image;
    }

    [TestCase(false), TestCase(true)]
    public void PowerCycleKeepsOnlyBatteryBackedPrgRam(bool battery)
    {
        var nes = new NesMachine(NesCartridge.Load(Image(battery)));
        nes.Write(0x6000, 0x39); nes.Write(0x7fff, 0x72); nes.Write(0x0037, 0x15);
        nes.Reset(false);
        Assert.That(nes.Peek(0x6000), Is.EqualTo(0x39));
        nes.Reset(true);
        Assert.Multiple(() =>
        {
            Assert.That(nes.Peek(0x6000), Is.EqualTo(battery ? 0x39 : 0));
            Assert.That(nes.Peek(0x7fff), Is.EqualTo(battery ? 0x72 : 0));
            Assert.That(nes.Ram[0x37], Is.Zero, "Internal RAM is still volatile.");
        });
    }

    [Test]
    public void BatteryTransferDoesNotRestoreTheCpuOrVolatileMemory()
    {
        var source = new NesMachine(NesCartridge.Load(Image()));
        source.Write(0x6000, 42); source.Write(0x7fff, 74);
        byte[] data = source.SaveBattery(); data[1] = 19;
        Assert.That(source.Peek(0x6001), Is.Zero, "An export must not alias cartridge RAM.");
        var target = new NesMachine(NesCartridge.Load(Image()));
        target.RunFrame(null); target.Ram[37] = 88;
        uint pc = target.Cpu.ProgramCounter; long frames = target.FrameCount;
        target.LoadBattery(data);
        Assert.Multiple(() =>
        {
            Assert.That(target.Peek(0x6000), Is.EqualTo(42)); Assert.That(target.Peek(0x7fff), Is.EqualTo(74));
            Assert.That(target.Ram[37], Is.EqualTo(88)); Assert.That(target.FrameCount, Is.EqualTo(frames));
            Assert.That(target.Cpu.ProgramCounter, Is.EqualTo(pc));
        });
        data[0] = 9; Assert.That(target.Peek(0x6000), Is.EqualTo(42), "An import must copy the bytes.");
    }

    [TestCase(0), TestCase(8191), TestCase(8193)]
    public void InvalidBatterySizeLeavesRamIntact(int size)
    {
        var nes = new NesMachine(NesCartridge.Load(Image())); nes.Write(0x6000, 51);
        Assert.That(() => nes.LoadBattery(new byte[size]), Throws.TypeOf<InvalidDataException>());
        Assert.That(nes.Peek(0x6000), Is.EqualTo(51));
    }

    [TestCase(true, false, 0x70, 0, true)]
    [TestCase(true, true, 0x70, 0x07, true)]
    [TestCase(true, true, 0x70, 0x70, false)]
    [TestCase(true, true, 0x07, 0, false)]
    [TestCase(true, true, 0x60, 0, false)]
    [TestCase(true, true, 0x71, 0, false)]
    [TestCase(false, false, 0x70, 0, false)]
    public void OnlyTheModelledPrgNvramLayoutCanBeExported(bool battery, bool nes2, byte ram, byte chrRam, bool supported)
    {
        var nes = new NesMachine(NesCartridge.Load(Image(battery, nes2, ram, chrRam)));
        Assert.That(nes.Cartridge.SupportsBatterySave, Is.EqualTo(supported));
        if (supported) Assert.That(nes.SaveBattery(), Has.Length.EqualTo(8192));
        else Assert.That(() => nes.SaveBattery(), Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(7)]
    public void BatteryTransferWorksForAllSupportedMappersIncludingWriteProtectedRam(int mapper)
    {
        byte[] data = new byte[16 + 2 * NesCartridge.PrgUnit];
        "NES\x1a"u8.CopyTo(data); data[4] = 2; data[6] = (byte)(mapper << 4 | 2);
        var nes = new NesMachine(NesCartridge.Load(data));
        if (mapper == 4) nes.Cartridge.CpuWrite(0xa001, 0x80);
        nes.Write(0x6000, 37); nes.Write(0x7fff, 81);
        if (mapper == 4) nes.Cartridge.CpuWrite(0xa001, 0x00);
        byte[] battery = nes.SaveBattery();
        var restored = new NesMachine(NesCartridge.Load(data)); restored.LoadBattery(battery);
        if (mapper == 4) restored.Cartridge.CpuWrite(0xa001, 0x80);
        Assert.That(restored.Peek(0x6000), Is.EqualTo(37)); Assert.That(restored.Peek(0x7fff), Is.EqualTo(81));
    }
}
