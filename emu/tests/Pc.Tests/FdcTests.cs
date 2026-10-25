using Retro.Systems.Pc;

namespace Pc.Tests;

public sealed class FdcTests
{
    private static byte[] Image()
    {
        byte[] image = new byte[737280]; for (int i = 0; i < 1440; i++) Array.Fill(image, (byte)(i + 1), i * 512, 512); return image;
    }
    private static void Command(PcBus b, params byte[] command) { foreach (byte v in command) b.Out(0x3F5, v); }
    private static byte[] Result(PcBus b) { var data = new List<byte>(); while ((b.In(0x3F4) & 0x40) != 0) data.Add(b.In(0x3F5)); return data.ToArray(); }
    private static void Run(PcBus b, int n) { for (int i = 0; i < n; i++) { b.SetBusGranted(b.HoldRequested); b.Clock(); } }
    private static PcBus Board(bool protect = false)
    {
        var b = new PcBus(new PcMemory(new byte[8192])); b.Fdc.Mount(new FloppyDiskImage(Image(), protect));
        b.Out(0x3F2, 0x1C); Run(b, 16); Command(b, 8); Assert.That(Result(b), Is.EqualTo(new byte[] { 0xC0, 0 }));
        Command(b, 3, 0xCF, 2); return b;
    }
    private static void Dma(PcBus b, int count = 512, bool write = false)
    {
        void Word(int port, ushort v) { b.Out(0x0C, 0); b.Out((ushort)port, (byte)v); b.Out((ushort)port, (byte)(v >> 8)); }
        Word(4, 0x2000); Word(5, (ushort)(count - 1)); b.Out(0x81, 0); b.Out(0x0B, write ? (byte)0x4A : (byte)0x46); b.Out(0x0A, 2);
    }
    [TestCase(1, 9)] [TestCase(9, 8)] [TestCase(9, 9)]
    public void ReadSectorUsesInitialIdAndDmaTcWinsOverEndOfTrack(int sector, int eot)
    {
        var b = Board(); Dma(b); Command(b, 0x46, 0, 0, 0, (byte)sector, 2, (byte)eot, 0x2A, 255);
        Assert.That(b.Fdc.BytesTransferred, Is.Zero); Run(b, 100000);
        Assert.That(b.Fdc.BytesTransferred, Is.EqualTo(512)); Assert.That(b.Memory.Read(0x2000), Is.EqualTo(sector)); Assert.That(b.Memory.Read(0x21FF), Is.EqualTo(sector));
        Assert.That(Result(b).Take(3), Is.EqualTo(new byte[] { 0, 0, 0 })); Assert.That(b.In(0x3F4), Is.EqualTo(0x80));
    }
    [Test]
    public void MultiTrackContinuesAtHeadOneWithoutReadingPastTc()
    {
        var b = Board(); Dma(b, 1024); Command(b, 0xC6, 0, 0, 0, 9, 2, 9, 0x2A, 255); Run(b, 170000);
        Assert.That(b.Fdc.BytesTransferred, Is.EqualTo(1024)); Assert.That(b.Memory.Read(0x2000), Is.EqualTo(9)); Assert.That(b.Memory.Read(0x2200), Is.EqualTo(10));
        byte[] status = Result(b); Assert.That(status[0] & 0xC0, Is.Zero); Assert.That(status[1], Is.Zero);
    }
    [Test]
    public void WriteSectorChangesPrivateOverlayAndNeverTheSourceImage()
    {
        byte[] image = Image(), original = (byte[])image.Clone(); var b = Board(); var disk = new FloppyDiskImage(image); b.Fdc.Mount(disk);
        for (int i = 0; i < 512; i++) b.Memory.Write((uint)(0x2000 + i), (byte)i);
        Dma(b, write: true); Command(b, 0x45, 0, 0, 0, 1, 2, 9, 0x2A, 255); Run(b, 100000);
        Assert.That(Result(b).Take(3), Is.EqualTo(new byte[] { 0, 0, 0 })); Assert.That(disk.ModifiedSectors, Is.EqualTo(1));
        byte[] sector = new byte[512]; disk.ReadSector(0, 0, 1, sector); Assert.That(sector, Is.EqualTo(Enumerable.Range(0, 512).Select(i => (byte)i)));
        Assert.That(image, Is.EqualTo(original));
    }
    [Test]
    public void WriteProtectedDiskReturnsHardwareErrorAndDoesNotRequestDma()
    {
        var b = Board(true); Dma(b, write: true); Command(b, 0x45, 0, 0, 0, 1, 2, 9, 0x2A, 255);
        Assert.That(b.Fdc.DmaRequest, Is.False); byte[] result = Result(b); Assert.That(result[0] & 0xC0, Is.EqualTo(0x40)); Assert.That(result[1], Is.EqualTo(2)); Assert.That(b.Fdc.BytesTransferred, Is.Zero);
    }
    [Test]
    public void UnservicedByteReportsOverrunInsteadOfSilentlyDroppingIt()
    {
        var b = Board(); Command(b, 0x46, 0, 0, 0, 1, 2, 9, 0x2A, 255); Run(b, 6000);
        Assert.That(Result(b)[1], Is.EqualTo(0x10)); Assert.That(b.Fdc.BytesTransferred, Is.Zero); Assert.That(b.Fdc.DmaRequest, Is.False);
    }
    [Test]
    public void NonDmaModeExposesByteDirectionAndRequestInMainStatus()
    {
        var b = Board(); Command(b, 3, 0xCF, 3); Command(b, 0x46, 0, 0, 0, 1, 2, 1, 0x2A, 255);
        Run(b, 4773); Assert.That(b.In(0x3F4) & 0xF0, Is.EqualTo(0xF0)); Assert.That(b.In(0x3F5), Is.EqualTo(1));
        Assert.That(b.Fdc.BytesTransferred, Is.EqualTo(1)); Assert.That(b.Fdc.DmaRequest, Is.False); b.Fdc.TerminalCount();
        Assert.That(Result(b).Take(3), Is.EqualTo(new byte[] { 0, 0, 0 }));
    }
    [Test]
    public void SeekCompletionHasItsOwnSenseStatusAfterResetPollingEnds()
    {
        var b = Board(); Command(b, 15, 0, 3); Assert.That(b.In(0x3F4) & 1, Is.EqualTo(1)); Run(b, 60000); Assert.That(b.In(0x3F4) & 1, Is.EqualTo(1), "Busy remains until Sense Interrupt Status.");
        Command(b, 8); Assert.That(Result(b), Is.EqualTo(new byte[] { 0x20, 3 })); Command(b, 8); Assert.That(Result(b), Is.EqualTo(new byte[] { 0x80 }));
    }
    [Test]
    public void OverlayRestoreValidatesIdentityAndTruncationBeforeReplacingExistingWrites()
    {
        var d = new FloppyDiskImage(Image()); byte[] bytes = Enumerable.Repeat((byte)99, 512).ToArray(); d.WriteSector(0, 0, 1, bytes);
        using var s = new MemoryStream(); d.SaveOverlay(new BinaryWriter(s)); var saved = s.ToArray();
        var restored = new FloppyDiskImage(Image()); restored.LoadOverlay(new BinaryReader(new MemoryStream(saved))); byte[] sector = new byte[512]; restored.ReadSector(0, 0, 1, sector); Assert.That(sector, Is.EqualTo(bytes));
        Assert.Catch<IOException>(() => restored.LoadOverlay(new BinaryReader(new MemoryStream(saved[..^1])))); restored.ReadSector(0, 0, 1, sector); Assert.That(sector, Is.EqualTo(bytes));
        byte[] different = Image(); different[0]++; var wrong = new FloppyDiskImage(different);
        Assert.Throws<InvalidDataException>(() => wrong.LoadOverlay(new BinaryReader(new MemoryStream(saved))));
    }
}