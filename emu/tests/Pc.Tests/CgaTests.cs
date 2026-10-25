using Retro.Cpu.I8088;
using Retro.Systems.Pc;

namespace Pc.Tests;

public sealed class CgaTests
{
    private static void Register(Cga6845 c, int index, int value) { c.Write(0x3D4, (byte)index); c.Write(0x3D5, (byte)value); }
    private static Cga6845 Display(PcMemory memory, bool eighty = true, byte[]? font = null)
    {
        var c = new Cga6845(memory, font ?? []);
        int[] registers = eighty ? [113, 80, 90, 10, 31, 6, 25, 28, 2, 7] : [56, 40, 45, 10, 31, 6, 25, 28, 2, 7];
        for (int i = 0; i < registers.Length; i++) Register(c, i, registers[i]);
        Register(c, 10, 0x20); c.Write(0x3D8, eighty ? (byte)9 : (byte)8); return c;
    }
    private static int Color(int i) => unchecked((int)0xFF000000) | Cga6845.Palette[i];

    [Test]
    public void CrtcMirrorsMasksCursorAndProtectsWriteOnlyAndLightPenRegisters()
    {
        var c = Display(new PcMemory(new byte[8192]));
        c.Write(0x3D0, 14); c.Write(0x3D7, 255); Assert.That(c.Read(0x3D1), Is.EqualTo(63));
        Register(c, 0, 255); Assert.That(c.Read(0x3D5), Is.Zero);
        c.Write(0x3D4, 31); c.Write(0x3D5, 255); Assert.That(c.Read(0x3D5), Is.Zero);
        c.Write(0x3DC, 0); Assert.That(c.Status & 2, Is.EqualTo(2)); c.Write(0x3DB, 0); Assert.That(c.Status & 2, Is.Zero);
    }
    [Test]
    public void BlankingAndVsyncDependOnOscillatorNotNumberOfStatusReads()
    {
        var c = Display(new PcMemory(new byte[8192])); byte initial = c.Status;
        for (int i = 0; i < 100; i++) Assert.That(c.Read(0x3DA), Is.EqualTo(initial));
        for (int i = 0; i < 80 * 8; i++) c.ClockMaster(); Assert.That(c.Status & 1, Is.EqualTo(1));
        for (int i = 80 * 8; i < 28 * 8 * 114 * 8; i++) c.ClockMaster(); Assert.That(c.Status & 8, Is.EqualTo(8));
        for (int i = 28 * 8 * 114 * 8; i < 262 * 114 * 8; i++) c.ClockMaster();
        Assert.That(c.FrameCount, Is.EqualTo(1)); Assert.That(c.Status & 8, Is.Zero);
    }
    [TestCase(true)] [TestCase(false)]
    public void TextUsesStartAddressAttributesAndOptionalFullCharacterRom(bool eighty)
    {
        var memory = new PcMemory(new byte[8192]); byte[] font = new byte[2048]; font[0xC4 * 8] = 0x81;
        var c = Display(memory, eighty, font); Register(c, 12, 0); Register(c, 13, 1);
        memory.Write(0xB8002, 0xC4); memory.Write(0xB8003, 0x1E); c.Render();
        int scale = eighty ? 1 : 2;
        Assert.That(c.Frame.Pixels[0], Is.EqualTo(Color(14))); Assert.That(c.Frame.Pixels[scale], Is.EqualTo(Color(1)));
        Assert.That(c.Frame.Pixels[7 * scale], Is.EqualTo(Color(14))); Assert.That(c.Frame.Pixels[640], Is.EqualTo(Color(1)));
        c.Write(0x3D8, 0); c.Render(); Assert.That(c.Frame.Pixels, Is.All.EqualTo(Color(0)));
    }
    [Test]
    public void GraphicsInterleaves8kBanksAndDrawsTwoBitPixelsTwiceHorizontally()
    {
        var memory = new PcMemory(new byte[8192]); var c = Display(memory);
        Register(c, 1, 40); Register(c, 6, 100); Register(c, 9, 1); c.Write(0x3D8, 0x0A); c.Write(0x3D9, 0x30);
        memory.Write(0xB8000, 0x1B); memory.Write(0xBA000, 0xE4); c.Render();
        Assert.That(c.Frame.Pixels.Take(8), Is.EqualTo(new[] { Color(0), Color(0), Color(11), Color(11), Color(13), Color(13), Color(15), Color(15) }));
        Assert.That(c.Frame.Pixels.Skip(640).Take(8), Is.EqualTo(new[] { Color(15), Color(15), Color(13), Color(13), Color(11), Color(11), Color(0), Color(0) }));
        c.Write(0x3D8, 0x1A); c.Write(0x3D9, 15); memory.Write(0xB8000, 0x81); c.Render();
        Assert.That(c.Frame.Pixels.Take(8), Is.EqualTo(new[] { Color(15), Color(0), Color(0), Color(0), Color(0), Color(0), Color(0), Color(15) }));
    }
    [Test]
    public void ClockedCpuWakesFromHaltUsingPitAndPicWhileCgaKeepsRunning()
    {
        var memory = new PcMemory(new byte[8192]); var bus = new PcBus(memory);
        bus.Out(0x20, 0x13); bus.Out(0x21, 8); bus.Out(0x21, 1); bus.Out(0x21, 0xFE);
        bus.Out(0x43, 0x30); bus.Out(0x40, 80); bus.Out(0x40, 0);
        byte[] program = [0xFB, 0xF4, 0xEB, 0xFD], handler = [0xFE, 0x06, 0x00, 0x05, 0xB0, 0x20, 0xE6, 0x20, 0xCF];
        for (int i = 0; i < program.Length; i++) memory.Write((uint)(0x1000 + i), program[i]);
        for (int i = 0; i < handler.Length; i++) memory.Write((uint)(0x300 + i), handler[i]);
        memory.Write(0x20, 0); memory.Write(0x21, 3);
        var cpu = new I8088Cpu(bus, bus.Pic) { CS = 0x100, SS = 0, SP = 0xFFF0, TimingEnabled = true };
        for (int i = 0; i < 2000 && memory.Read(0x500) == 0; i++) cpu.Step();
        Assert.That(memory.Read(0x500), Is.EqualTo(1)); Assert.That(cpu.Cycles, Is.EqualTo(bus.CpuClocks));
        Assert.That(cpu.Instructions, Is.LessThan(20), "HLT idle T-states must not count as instructions.");
        Assert.That(cpu.Cycles, Is.GreaterThanOrEqualTo(320));
    }
}