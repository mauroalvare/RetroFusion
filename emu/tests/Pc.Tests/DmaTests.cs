using Retro.Systems.Pc;

namespace Pc.Tests;

public sealed class DmaTests
{
    private static void Word(Dma8237 d, int port, ushort value) { d.Write(12, 0); d.Write(port, (byte)value); d.Write(port, (byte)(value >> 8)); }
    private static void Clocks(Dma8237 d, int n, bool grant = true) { for (int i = 0; i < n; i++) d.Clock(grant); }
    [Test]
    public void AddressCountAndFlipFlopAreSharedAcrossPortsAndResetDoesNotEraseRegisters()
    {
        var d = new Dma8237(); Word(d, 4, 0x1234); Word(d, 5, 0xABCD);
        d.Write(12, 0); Assert.That(d.Read(4), Is.EqualTo(0x34)); Assert.That(d.Read(5), Is.EqualTo(0xAB));
        d.Write(13, 0); Assert.That(d.Address(2), Is.EqualTo(0x1234)); Assert.That(d.Count(2), Is.EqualTo(0xABCD));
        Assert.That(d.Mask, Is.EqualTo(15)); Assert.That(d.Read(4), Is.EqualTo(0x34));
    }
    [Test]
    public void DeviceWritesRequireGrantFourClocksAndCountPlusOneBytes()
    {
        var d = new Dma8237(); var writes = new List<(int C, ushort A, byte V)>(); byte data = 10;
        d.ReadDevice = _ => data++; d.WriteMemory = (c, a, v) => writes.Add((c, a, v));
        Word(d, 4, 0xFFFE); Word(d, 5, 2); d.Write(11, 0x06); d.Write(10, 2); d.SetRequest(2, true);
        Assert.That(d.HoldRequested, Is.True); Clocks(d, 50, false); Assert.That(writes, Is.Empty);
        Clocks(d, 3); Assert.That(writes, Is.Empty); Clocks(d, 9);
        Assert.That(writes, Is.EqualTo(new[] { (2, (ushort)0xFFFE, (byte)10), (2, (ushort)0xFFFF, (byte)11), (2, (ushort)0, (byte)12) }));
        Assert.That(d.Count(2), Is.EqualTo(0xFFFF)); Assert.That(d.Mask & 4, Is.EqualTo(4));
        Assert.That(d.Read(8) & 4, Is.EqualTo(4)); Assert.That(d.Read(8) & 4, Is.Zero);
    }
    [Test]
    public void AutoInitializeRestoresBaseAndDecrementMovesBackwards()
    {
        var d = new Dma8237(); var addresses = new List<ushort>(); d.ReadMemory = (_, a) => { addresses.Add(a); return 17; }; d.WriteDevice = (_, _) => { };
        Word(d, 2, 0x100); Word(d, 3, 1); d.Write(11, 0x39); d.Write(10, 1); d.SetRequest(1, true); Clocks(d, 16);
        Assert.That(addresses, Is.EqualTo(new ushort[] { 0x100, 0xFF, 0x100, 0xFF })); Assert.That(d.Address(1), Is.EqualTo(0x100)); Assert.That(d.Count(1), Is.EqualTo(1));
    }
    [Test]
    public void SingleModeReleasesForCpuWhileBlockModeFinishesAfterDreqDrops()
    {
        var d = new Dma8237(); Word(d, 0, 0x200); Word(d, 1, 1); d.Write(11, 0x40); d.Write(10, 0); d.SetRequest(0, true);
        Clocks(d, 4); Assert.That(d.Address(0), Is.EqualTo(0x201)); Assert.That(d.HoldRequested, Is.False);
        Clocks(d, 4); Assert.That(d.HoldRequested, Is.True); Clocks(d, 4); Assert.That(d.Count(0), Is.EqualTo(0xFFFF));
        d.Reset(); Word(d, 1, 1); d.Write(11, 0x80); d.Write(10, 0); d.SetRequest(0, true); Clocks(d, 1); d.SetRequest(0, false); Clocks(d, 7);
        Assert.That(d.Count(0), Is.EqualTo(0xFFFF));
    }
    [Test]
    public void VerifyDoesNotTouchMemoryAndPriorityCanRotate()
    {
        var d = new Dma8237(); var order = new List<int>(); d.Acknowledge += order.Add;
        for (int c = 0; c < 3; c++) { Word(d, c * 2 + 1, 0); d.Write(11, (byte)(0x10 | c)); d.SetRequest(c, true); }
        d.Write(8, 0x10); d.Write(14, 0); Clocks(d, 12);
        Assert.That(order, Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(d.Read(8) & 7, Is.EqualTo(7));
    }
    [Test]
    public void BoardPagesWrapAt64kAndLockPreventsDmaTransfer()
    {
        var b = new PcBus(new PcMemory(new byte[8192])); byte next = 31;
        b.Dma.ReadDevice = _ => next++; b.Out(0x81, 3); Word(b.Dma, 4, 0xFFFF); Word(b.Dma, 5, 1);
        b.Dma.Write(11, 6); b.Dma.Write(10, 2); b.Dma.SetRequest(2, true); b.SetBusGranted(true); b.SetLock(true);
        for (int i = 0; i < 20; i++) b.Clock(); Assert.That(b.Memory.Read(0x3FFFF), Is.Zero);
        b.SetLock(false); for (int i = 0; i < 8; i++) b.Clock();
        Assert.That(b.Memory.Read(0x3FFFF), Is.EqualTo(31)); Assert.That(b.Memory.Read(0x30000), Is.EqualTo(32)); Assert.That(b.Memory.Read(0x40000), Is.Zero);
    }
    [TestCase(1)] [TestCase(8)] [TestCase(0x20)] [TestCase(0x40)]
    public void UnsupportedTimingAndMemoryToMemoryModesFailBeforeChangingController(int command)
    {
        var d = new Dma8237(); d.Write(14, 0); d.SetRequest(0, true);
        Assert.Throws<NotSupportedException>(() => d.Write(8, (byte)command)); Assert.That(d.HoldRequested, Is.True);
    }
}