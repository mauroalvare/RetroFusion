using Retro.Systems.Nes;

namespace Nes.Tests;

public sealed class DmcBufferTests
{
    // Isolated APU reader tests. They do not assert the machine's unresolved DMA-abort timing.
    private static Apu Ready()
    {
        var a = new Apu { ExternalDmcDma = true };
        a.Write(0x4010, 0x8F); a.Write(0x4011, 64); a.Write(0x4012, 1); a.Write(0x4013, 0);
        a.Write(0x4015, 0x10); return a;
    }

    [Test]
    public void StoppingTheReaderDoesNotDiscardTheBufferedSampleOrSilenceItsOutput()
    {
        var a = Ready(); a.CompleteDmcDma(255); a.Write(0x4015, 0);
        Assert.That(a.DmcDmaAddress, Is.Null); float before = a.Output;
        for (int i = 0; i < 16 * 54; i++) a.Step();
        Assert.That(a.Output, Is.GreaterThan(before), "The buffered eight one bits still reach the DAC");
        Assert.That(a.DmcDmaAddress, Is.Null);
    }

    [Test]
    public void RestartWaitsForTheOldBufferThenUsesTheNewSampleAddress()
    {
        var a = Ready(); a.CompleteDmcDma(255); a.Write(0x4015, 0); a.Write(0x4012, 2); a.Write(0x4015, 0x10);
        Assert.That(a.DmcDmaAddress, Is.Null, "Restart must not overwrite a buffered byte");
        for (int i = 0; i < 8 * 54 && a.DmcDmaAddress == null; i++) a.Step();
        Assert.That(a.DmcDmaAddress, Is.EqualTo(0xC080));
        a.CompleteDmcDma(0);
        Assert.That(a.Status & 0x90, Is.EqualTo(0x80), "The reader finishes before its audio bits finish playing");
    }

    [Test]
    public void ResetClearsTheBufferedByteAndOutputShiftUnit()
    {
        var a = Ready(); a.CompleteDmcDma(255); for (int i = 0; i < 9 * 54; i++) a.Step();
        a.Reset(); var reference = new Apu(); reference.Reset();
        for (int i = 0; i < 16 * 428; i++) { a.Step(); reference.Step(); Assert.That(a.Output, Is.EqualTo(reference.Output), "An old sample must not resume after reset"); }
    }

    [TestCase(false)] [TestCase(true)]
    public void ACompletedReadersBufferAndShiftResumeExactlyAfterSaving(bool stopReader)
    {
        var a = Ready(); a.CompleteDmcDma(0xF3); for (int i = 0; i < 9 * 54; i++) a.Step();
        if (stopReader) a.Write(0x4015, 0);
        using var data = new MemoryStream(); using (var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, true)) a.Save(writer);
        var b = Ready(); data.Position = 0; using (var reader = new BinaryReader(data, System.Text.Encoding.UTF8, true)) b.Load(reader);
        for (int i = 0; i < 9 * 54; i++) { a.Step(); b.Step(); Assert.That((b.Output, b.Status, b.DmcDmaAddress), Is.EqualTo((a.Output, a.Status, a.DmcDmaAddress))); }
    }
}
