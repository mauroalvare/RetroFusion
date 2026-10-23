using Retro.Cpu.I8088;
using Retro.Systems.Pc;

namespace Pc.Tests;

public sealed class PcMemoryTests
{
    [TestCase(8192)] [TestCase(16384)] [TestCase(32768)] [TestCase(65536)]
    public void BiosEndsAtOneMegabyteAndCallerCannotChangeIt(int size)
    {
        byte[] bios = new byte[size]; bios[0] = 17; bios[^16] = 0xEA; bios[^1] = 99;
        var memory = new PcMemory(bios); string identity = memory.BiosIdentity;
        bios[0] = 0;
        Assert.That(memory.Read(memory.BiosStart), Is.EqualTo(17));
        Assert.That(memory.Read(0xFFFF0), Is.EqualTo(0xEA));
        Assert.That(memory.Read(0xFFFFF), Is.EqualTo(99));
        memory.Write(memory.BiosStart, 42); memory.ClearRam();
        Assert.That(memory.Read(memory.BiosStart), Is.EqualTo(17));
        Assert.That(memory.BiosIdentity, Is.EqualTo(identity));
    }

    [Test]
    public void InstalledRamDoesNotAliasVideoOrUnpopulatedMemory()
    {
        var memory = new PcMemory(new byte[8192], 256);
        memory.Write(0x3FFFF, 1); memory.Write(0x40000, 2); memory.Write(0xA0000, 3);
        memory.Write(0xB8000, 65); memory.Write(0xBFFFF, 7);
        Assert.That(memory.Read(0x3FFFF), Is.EqualTo(1));
        Assert.That(memory.Read(0x40000), Is.EqualTo(255));
        Assert.That(memory.Read(0xA0000), Is.EqualTo(255));
        Assert.That(memory.Read(0xBC000), Is.EqualTo(65));
        Assert.That(memory.Read(0xBBFFF), Is.EqualTo(7));
        Assert.That(memory.Read(0xB0000), Is.EqualTo(255), "No MDA card is installed.");
        memory.ClearRam(); Assert.That(memory.Ram.ToArray(), Is.All.Zero); Assert.That(memory.VideoRam.ToArray(), Is.All.Zero);
    }

    [Test]
    public void Actual8088ResetVectorRunsOwnBiosAndWritesBdaAndCgaMemory()
    {
        byte[] bios = new byte[8192];
        // Reset far jump to F000:E000; write BDA 0040:0010 and a text cell at B800:0000.
        new byte[] { 0xEA, 0x00, 0xE0, 0x00, 0xF0 }.CopyTo(bios, 0x1FF0);
        new byte[] { 0xB8, 0x40, 0, 0x8E, 0xD8, 0xC6, 0x06, 0x10, 0, 42,
            0xB8, 0, 0xB8, 0x8E, 0xC0, 0x26, 0xC7, 0x06, 0, 0, 65, 0x1F, 0xF4 }.CopyTo(bios, 0);
        var memory = new PcMemory(bios); var cpu = new I8088Cpu(new Bus(memory)); cpu.Reset();
        Assert.That(cpu.PhysicalPC, Is.EqualTo(0xFFFF0));
        for (int n = 0; n < 20 && !cpu.Halted; n++) cpu.Step();
        Assert.That(cpu.Halted, Is.True);
        Assert.That(memory.Read(0x410), Is.EqualTo(42));
        Assert.That(memory.VideoRam[0], Is.EqualTo(65)); Assert.That(memory.VideoRam[1], Is.EqualTo(0x1F));
    }

    [Test]
    public void RejectsInvalidGeometryAndAlreadyUnwrappedAddresses()
    {
        Assert.Throws<ArgumentException>(() => new PcMemory(new byte[4000]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcMemory(new byte[8192], 641));
        var memory = new PcMemory(new byte[8192]);
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.Read(0x100000));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.Write(0x100000, 1));
    }

    private sealed class Bus(PcMemory memory) : I8088Bus
    {
        public byte ReadMemory(uint address) => memory.Read(address);
        public void WriteMemory(uint address, byte value) => memory.Write(address, value);
        public byte In(ushort port) => throw new NotSupportedException();
        public void Out(ushort port, byte value) => throw new NotSupportedException();
    }
}