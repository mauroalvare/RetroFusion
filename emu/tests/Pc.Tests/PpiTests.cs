using Retro.Systems.Pc;

namespace Pc.Tests;

public sealed class PpiTests
{
    [Test]
    public void ModeZeroDirectionsAndBitSetResetKeepIndependentPortLatches()
    {
        var p = new Ppi8255 { InputA = () => 0xA5, InputB = () => 0x5A, InputC = () => 0xC3 };
        Assert.That(p.Read(0), Is.EqualTo(0xA5)); Assert.That(p.Read(1), Is.EqualTo(0x5A));
        p.Write(3, 0x88); p.Write(0, 17); p.Write(1, 29); p.Write(2, 0x0A);
        Assert.That(p.Read(0), Is.EqualTo(17)); Assert.That(p.Read(1), Is.EqualTo(29)); Assert.That(p.Read(2), Is.EqualTo(0xCA));
        p.Write(3, 1); Assert.That(p.Read(2), Is.EqualTo(0xCB)); Assert.That(p.Control, Is.EqualTo(0x88));
        p.Write(3, 2); Assert.That(p.Read(2), Is.EqualTo(0xC9));
        p.Write(3, 0x99); Assert.That(p.Read(1), Is.Zero); Assert.That(p.Read(3), Is.EqualTo(255));
        Assert.Throws<NotSupportedException>(() => p.Write(3, 0xB9)); Assert.That(p.Control, Is.EqualTo(0x99));
    }
    [TestCase(false, 0x0D, 0x02)] [TestCase(true, 2, 1)]
    public void BoardSwitchSelectionReflectsCgaAndInstalled640kRam(bool pc, int first, int second)
    {
        var b = new PcBus(new PcMemory(new byte[8192]), pc5150: pc); b.Out(0x63, 0x99);
        b.Out(0x61, pc ? (byte)0x84 : (byte)0);
        Assert.That(b.In(0x62) & 15, Is.EqualTo(first));
        b.Out(0x61, pc ? (byte)0x80 : (byte)8);
        Assert.That(b.In(0x62) & 15, Is.EqualTo(second));
        if (pc) Assert.That(b.In(0x60), Is.EqualTo(0x2D));
    }
    [Test]
    public void SpeakerGateAndTimerOutputUsePpiPinsRatherThanReadSideEffects()
    {
        var b = new PcBus(new PcMemory(new byte[8192])); b.Out(0x63, 0x99);
        b.Out(0x43, 0x90); b.Out(0x42, 2); b.TickPit();
        for (int i = 0; i < 8; i++) b.TickPit(); Assert.That(b.In(0x62) & 0x20, Is.Zero);
        b.Out(0x61, 1); b.TickPit(); b.TickPit(); Assert.That(b.In(0x62) & 0x20, Is.EqualTo(0x20));
    }
    [Test]
    public void KeyboardSerialDelayLatchesUntilPortBClearAndMakesAnIrqEdgePerByte()
    {
        var k = new PcKeyboard(); var edges = new List<bool>(); k.IrqChanged += edges.Add;
        k.SetControl(0x40); k.EnqueueScanCode(0x1E); k.EnqueueScanCode(0x9E);
        for (int i = 0; i < 4772; i++) k.Clock(); Assert.That(k.Data, Is.Zero);
        k.Clock(); Assert.That(k.Data, Is.EqualTo(0x1E));
        for (int i = 0; i < 5000; i++) k.Clock(); Assert.That(k.Data, Is.EqualTo(0x1E));
        k.SetControl(0xC0); Assert.That(k.Data, Is.Zero); k.SetControl(0x40);
        for (int i = 0; i < 4773; i++) k.Clock(); Assert.That(k.Data, Is.EqualTo(0x9E));
        Assert.That(edges, Is.EqualTo(new[] { true, false, true }));
    }
    [Test]
    public void LongClockInhibitTriggersKeyboardSelfTestRatherThanInventingABiosResponse()
    {
        var k = new PcKeyboard(); k.SetControl(0x80);
        for (int i = 0; i < 95_455; i++) k.Clock(); k.SetControl(0x40);
        for (int i = 0; i < 2_386_364 + 4772; i++) k.Clock();
        Assert.That(k.Data, Is.EqualTo(0xAA));
        k.Reset(); Assert.That(k.Data, Is.Zero);
    }
}