using Retro.Compat;
using Retro.Systems.Spectrum;

namespace Compatibility.Tests;

/// <summary>Private lab probes against an original Spectrum program and firmware held in memory.</summary>
[TestFixture]
public sealed class AutonomousProbeTests
{
    private static Spectrum48 Machine(bool counting)
    {
        var machine = new Spectrum48(new byte[16384]);
        // DI; LD HL,8000; INC (HL); LD A,(HL); LD (4000),A; JP 9004.
        byte[] code = counting
            ? [0xF3, 0x21, 0x00, 0x80, 0x34, 0x7E, 0x32, 0x00, 0x40, 0xC3, 0x04, 0x90]
            : [0xF3, 0x76]; // DI; HALT: candidate values are kept without an observable effect.
        for (int i = 0; i < code.Length; i++) machine.Memory.Poke((uint)(0x9000 + i), code[i]);
        machine.Memory.Poke(0x5800, 0x47);
        machine.Memory.Poke(0x8000, 42);
        machine.Cpu.ProgramCounter = 0x9000;
        machine.RunFrame(null);
        // A saved state does not contain the framebuffer. Its precise current image must
        // survive probing, including pixels which have not been redrawn from RAM yet.
        machine.Video.Pixels[17] = unchecked((int)0xFF123456);
        return machine;
    }

    [Test]
    public void ACompletedWriteProbeRestoresTheMachineAndItsExactObservedPicture()
    {
        var machine = Machine(counting: true);
        byte[] before = machine.SaveState();
        int[] picture = machine.Video.Pixels.ToArray();
        int writes = 0;
        Action<ushort, byte, ushort> observer = (address, _, _) => { if (address == 0x8000) writes++; };
        machine.WriteWatch = observer;
        var finding = new LabFinding { Kind = "change", Address = "8000" };

        var result = LabVerifier.Verify(machine, finding, frames: 2);

        Assert.Multiple(() =>
        {
            Assert.That(writes, Is.GreaterThan(0), "The private program must actually execute during the probe");
            Assert.That(result.Frames, Is.EqualTo(2));
            Assert.That(machine.SaveState(), Is.EqualTo(before));
            Assert.That(machine.Video.Pixels, Is.EqualTo(picture));
            Assert.That(machine.WriteWatch, Is.SameAs(observer));
        });
    }

    [TestCase(false, false)] [TestCase(false, true)]
    [TestCase(true, false)] [TestCase(true, true)]
    public void SamplingRestoresTheExactPictureAndObserversOnCompletionOrCancellation(bool memoryLab, bool cancel)
    {
        var machine = Machine(counting: true);
        byte[] state = machine.SaveState(); int[] picture = machine.Video.Pixels.ToArray();
        using var cancellation = new CancellationTokenSource();
        Action<ushort, byte, ushort> observer = (address, _, _) => { if (cancel && address == 0x8000) cancellation.Cancel(); };
        machine.WriteWatch = observer;
        void Sample()
        {
            if (memoryLab) MemoryLab.Analyze(machine, "zx48", new(IdleFrames: 10, SampleEvery: 1, HoldShort: 2, HoldLong: 4), ct: cancellation.Token);
            else LabExperiments.Run(machine, "zx48", new() { Frames = 4, Steps = [new() { Frame = 0, Keys = "SPACE", Hold = 3 }] }, sampleEvery: 1, ct: cancellation.Token);
        }
        if (cancel) Assert.Throws<OperationCanceledException>(Sample); else Sample();
        Assert.That(machine.SaveState(), Is.EqualTo(state));
        Assert.That(machine.Video.Pixels, Is.EqualTo(picture), "Restoring RAM alone does not restore pixels which had not yet been redrawn");
        Assert.That(machine.WriteWatch, Is.SameAs(observer));
        Assert.That(machine.ReadPort(0x7FFE) & 1, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancellationDuringEitherComparisonRunRestoresStatePictureAndReleasesReplayKeys(bool modifiedRun)
    {
        var machine = Machine(counting: true);
        byte[] before = machine.SaveState();
        int[] picture = machine.Video.Pixels.ToArray();
        int baselineWrites = 0;
        machine.WriteWatch = (address, _, _) => { if (address == 0x8000) baselineWrites++; };
        machine.RunFrame(null);
        machine.RunFrame(null);
        machine.LoadState(before);
        picture.CopyTo(machine.Video.Pixels, 0);
        Assert.That(baselineWrites, Is.GreaterThan(0));

        using var cancellation = new CancellationTokenSource();
        int writes = 0;
        int cancelAt = modifiedRun ? baselineWrites + 1 : 1;
        Action<ushort, byte, ushort> observer = (address, _, _) =>
        {
            if (address == 0x8000 && ++writes == cancelAt) cancellation.Cancel();
        };
        machine.WriteWatch = observer;
        var replay = new LabExperiment { Kind = "change", Description = "Mantener entradas durante el contraste", Frames = 2,
            Steps = [new() { Frame = 0, Keys = "SPACE+KEMPSTON FIRE", Hold = 2 }] };
        var finding = new LabFinding { Kind = "change", Address = "8000" };

        Assert.Throws<OperationCanceledException>(() => LabVerifier.Verify(machine, finding, replay, frames: 2, ct: cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(writes, Is.GreaterThanOrEqualTo(cancelAt), "Cancellation must happen during actual emulation");
            Assert.That(machine.SaveState(), Is.EqualTo(before));
            Assert.That(machine.Video.Pixels, Is.EqualTo(picture));
            Assert.That(machine.ReadPort(0x7FFE) & 1, Is.EqualTo(1), "SPACE must not remain held");
            Assert.That(machine.ReadPort(0x001F), Is.Zero, "The replay must release the Kempston fire input");
            Assert.That(machine.WriteWatch, Is.SameAs(observer));
            Assert.That(finding.Status, Is.EqualTo("hypothesis"), "An interrupted probe must not claim a verification");
            Assert.That(finding.Verification, Is.Null);
        });
    }

    [TestCase("uint8", 42, 43, "2A", "2B")]
    [TestCase("uint16", 4660, 4661, "3412", "3512")]
    [TestCase("bcd", 1234, 1235, "3412", "3512")]
    [TestCase("ascii", 123, 124, "313233", "313234")]
    [TestCase("digits", 123, 124, "010203", "010204")]
    public void PreviewReportsTheOriginalAndWrittenValuesAndPhysicalBytesWithoutCertifyingMeaning(
        string encoding, int originalValue, int writtenValue, string originalHex, string writtenHex)
    {
        var machine = Machine(counting: false);
        byte[] bytes = Convert.FromHexString(originalHex);
        for (int i = 0; i < bytes.Length; i++) machine.Memory.Poke((uint)(0x8000 + i), bytes[i]);
        byte[] state = machine.SaveState();
        int[] picture = machine.Video.Pixels.ToArray();
        var finding = new LabFinding { Kind = "change", Address = "8000", Encoding = encoding, Bytes = bytes.Length,
            Hypothesis = "El propósito todavía no se comprobó" };

        var result = LabVerifier.Verify(machine, finding, frames: 2);

        Assert.Multiple(() =>
        {
            Assert.That(result.BeforeValue, Is.EqualTo(originalValue));
            Assert.That(result.TestValue, Is.EqualTo(writtenValue));
            Assert.That(result.BeforeHex, Is.EqualTo(originalHex));
            Assert.That(result.TestHex, Is.EqualTo(writtenHex));
            Assert.That(result.Result, Is.EqualTo("inconclusive"), "Keeping a written value does not prove its meaning or an effect");
            Assert.That(result.PixelsChanged, Is.Zero);
            Assert.That(finding.Hypothesis, Is.EqualTo("El propósito todavía no se comprobó"));
            Assert.That(machine.SaveState(), Is.EqualTo(state));
            Assert.That(machine.Video.Pixels, Is.EqualTo(picture));
        });
    }
}
