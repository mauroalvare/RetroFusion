using Retro.Systems.Nes;

namespace Nes.Tests;

public class BankedCartridgeTests
{
    private static byte[] Image(int mapper, int prg = 8, int chr = 0)
    {
        byte[] data = new byte[16 + prg * 16384 + chr * 8192];
        "NES\x1A"u8.CopyTo(data); data[4] = (byte)prg; data[5] = (byte)chr; data[6] = (byte)(mapper << 4);
        for (int i = 0; i < prg; i++) Array.Fill(data, (byte)i, 16 + i * 16384, 16384);
        return data;
    }
    [TestCase(0)] [TestCase(1)] [TestCase(5)] [TestCase(15)]
    public void UxromSwitchesOnlyLowerHalfAndKeepsResetVectorsVisible(int bank)
    {
        var cart = NesCartridge.Load(Image(2));
        cart.CpuWrite(0x9123, (byte)bank);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(bank % 8));
        Assert.That(cart.CpuRead(0xBFFF), Is.EqualTo(bank % 8));
        Assert.That(cart.CpuRead(0xC000), Is.EqualTo(7));
        Assert.That(cart.CpuRead(0xFFFC), Is.EqualTo(7));
        cart.PpuWrite(0x1FFF, 52); Assert.That(cart.PpuRead(0x1FFF), Is.EqualTo(52));
        cart.PatchPrg(0x8000, 99); Assert.That(cart.CpuRead(0x8000), Is.EqualTo(99));
        Assert.That(cart.CpuRead(0xC000), Is.EqualTo(bank % 8 == 7 ? 99 : 7));
    }
    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(7)]
    public void AxromSwitchesWholePrgAndBothSingleScreenPages(int bank)
    {
        var cart = NesCartridge.Load(Image(7));
        cart.CpuWrite(0xFFFF, (byte)bank);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo((bank % 4) * 2));
        Assert.That(cart.CpuRead(0xC000), Is.EqualTo((bank % 4) * 2 + 1));
        Assert.That(Enumerable.Range(0, 4).Select(t => cart.NametableFor((ushort)(0x2000 + t * 1024))), Is.All.Zero);
        cart.CpuWrite(0x8000, (byte)(bank | 0x10));
        Assert.That(Enumerable.Range(0, 4).Select(t => cart.NametableFor((ushort)(0x2000 + t * 1024))), Is.All.EqualTo(1));
    }
    [TestCase(2)] [TestCase(7)]
    public void StateRestoresBanksAndChrRamAndHardResetRestoresDefaults(int mapper)
    {
        var cart = NesCartridge.Load(Image(mapper)); var machine = new Retro.Systems.Nes.Nes(cart);
        cart.CpuWrite(0x8000, 0x12); cart.PpuWrite(0x123, 73);
        int bank = cart.CpuRead(0x8000), page = cart.NametableFor(0x2000);
        var state = machine.SaveState();
        cart.CpuWrite(0x8000, 0); cart.PpuWrite(0x123, 0);
        machine.LoadState(state);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(bank));
        Assert.That(cart.NametableFor(0x2000), Is.EqualTo(page));
        Assert.That(cart.PpuRead(0x123), Is.EqualTo(73));
        machine.Reset(false);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(bank), "CPU reset does not reset cartridge latches.");
        machine.Reset(true);
        Assert.That(cart.CpuRead(0x8000), Is.Zero);
    }
    [Test]
    public void StateFromAnotherEditionIsRejectedBeforeChangingRam()
    {
        var first = new Retro.Systems.Nes.Nes(NesCartridge.Load(Image(2)));
        byte[] other = Image(2); other[20] ^= 1;
        var second = new Retro.Systems.Nes.Nes(NesCartridge.Load(other));
        second.Ram[0] = 70; var before = second.SaveState();
        Assert.Throws<InvalidDataException>(() => second.LoadState(first.SaveState()));
        Assert.That(second.SaveState(), Is.EqualTo(before));
    }
    [Test]
    public void ExistingVersionTwoStatesRemainReadableForOriginalMappers()
    {
        var machine = new Retro.Systems.Nes.Nes(NesCartridge.Load(Image(0, 1)));
        machine.Ram[17] = 99; byte[] modern = machine.SaveState();
        using var input = new BinaryReader(new MemoryStream(modern));
        string magic = input.ReadString(); _ = input.ReadInt32(); _ = input.ReadString();
        using var old = new MemoryStream(); using (var writer = new BinaryWriter(old, System.Text.Encoding.UTF8, leaveOpen: true))
        { writer.Write(magic); writer.Write(2); writer.Write(modern[(int)input.BaseStream.Position..]); }
        machine.Ram[17] = 0; machine.LoadState(old.ToArray());
        Assert.That(machine.Ram[17], Is.EqualTo(99));
    }
}