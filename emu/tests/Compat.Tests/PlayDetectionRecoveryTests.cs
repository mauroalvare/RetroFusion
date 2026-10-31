using Retro.Compat;
using Retro.Systems.Spectrum;
using Retro.Systems.Nes;

namespace Compatibility.Tests;

[TestFixture]
public sealed class PlayDetectionRecoveryTests
{
    [Test]
    public void NesActionSamplingRestoresItsStartingImageAsWellAsRam()
    {
        var machine = new Nes(NesCartridge.Load(PreparationNesFixture.Rom()));
        for (int i = 0; i < 100; i++) machine.RunFrame(null);
        machine.SetKey("P1 START", true); for (int i = 0; i < 6; i++) machine.RunFrame(null);
        machine.ReleaseAllKeys(); for (int i = 0; i < 120; i++) machine.RunFrame(null);
        machine.Video.Pixels[17] = unchecked((int)0xFF123456);
        byte[] state = machine.SaveState(); int[] picture = machine.Video.Pixels.ToArray();
        var report = MemoryLab.Analyze(machine, "nes", new(IdleFrames: 60, SampleEvery: 5, HoldShort: 8, HoldLong: 16));
        Assert.That(report.CheckedControls, Does.ContainKey("jump"), "The action probe actually executed");
        Assert.That(machine.SaveState(), Is.EqualTo(state));
        Assert.That(machine.Video.Pixels, Is.EqualTo(picture));
    }

    [TestCase(false)] [TestCase(true)]
    public void DetectionRestoresExactPictureStateAndKeysWhenCompletedOrInterrupted(bool cancel)
    {
        var machine = new Spectrum48(new byte[16384]);
        byte[] code = [0xF3, 0x21, 0x00, 0x80, 0x34, 0x7E, 0x32, 0x00, 0x40, 0xC3, 0x04, 0x90];
        for (int i = 0; i < code.Length; i++) machine.Memory.Poke((uint)(0x9000 + i), code[i]);
        machine.Cpu.ProgramCounter = 0x9000;
        machine.RunFrame(null);
        machine.Video.Pixels[17] = unchecked((int)0xFF123456);
        byte[] state = machine.SaveState(); int[] picture = machine.Video.Pixels.ToArray();
        using var cancellation = new CancellationTokenSource();
        machine.WriteWatch = (a, _, _) => { if (cancel && a == 0x8000) cancellation.Cancel(); };
        if (cancel) Assert.Throws<OperationCanceledException>(() => PlayDetector.Detect(machine, "zx48", ct: cancellation.Token));
        else PlayDetector.Detect(machine, "zx48");
        Assert.That(machine.SaveState(), Is.EqualTo(state));
        Assert.That(machine.Video.Pixels, Is.EqualTo(picture));
        Assert.That(machine.ReadPort(0x7FFE) & 1, Is.EqualTo(1));
        Assert.That(machine.ReadPort(0x001F), Is.Zero);
    }
}
