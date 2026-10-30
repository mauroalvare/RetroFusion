using Retro.App;
using Retro.Core;
using Retro.Machines;

namespace App.Tests;

[TestFixture, NonParallelizable]
public sealed class TimelineTests
{
    private string _directory = "";
    private string? _profiles;
    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "retro-timeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _profiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_directory, "profiles"));
    }
    [TearDown]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _profiles);
        Assert.That(Path.GetDirectoryName(Path.GetFullPath(_directory)), Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
        Directory.Delete(_directory, recursive: true);
    }

    private static byte[] Cartridge(byte edition = 0)
    {
        byte[] image = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(image); image[4] = 1;
        // Own NROM: latch A, retain it in RAM and color the backdrop; count loop iterations.
        byte[] code = [0x78, 0xA9, 1, 0x8D, 0x16, 0x40, 0xA9, 0, 0x8D, 0x16, 0x40,
            0xAD, 0x16, 0x40, 0x29, 1, 0x85, 0, 0x0A, 0x0A, 0x0A, 0x0A, 0x85, 2,
            0xA9, 0x3F, 0x8D, 6, 0x20, 0xA9, 0, 0x8D, 6, 0x20,
            0xA5, 2, 0x8D, 7, 0x20, 0xE6, 1, 0x4C, 1, 0x80];
        code.CopyTo(image, 16); image[16 + 0x100] = edition;
        image[^6] = image[^4] = image[^2] = 0;
        image[^5] = image[^3] = image[^1] = 0x80;
        return image;
    }
    private static EmulatorHost Start(byte edition = 0)
    {
        var host = new EmulatorHost();
        host.StartNes(MachineCatalog.Get("nes"), new MediaFile("own.nes", Cartridge(edition), "own.nes"));
        return host;
    }
    private static void Frames(EmulatorHost host, int count)
    {
        for (int i = 0; i < count; i++) host.StepFrame();
    }

    [Test]
    public void RewindBufferEvictsByBytesAndCountAndDoesNotAcceptOversizedStates()
    {
        var bytes = new RewindBuffer(capacity: 16);
        for (int frame = 0; frame < 5; frame++) Assert.That(bytes.Add(new(frame, new byte[4], new int[1])), Is.True);
        Assert.That(bytes.Count, Is.EqualTo(2)); Assert.That(bytes.Bytes, Is.EqualTo(16));
        Assert.That(bytes.Find(0, 5)!.Frame, Is.EqualTo(3));
        Assert.That(bytes.Add(new(6, new byte[17], [])), Is.False);
        Assert.That(bytes.Bytes, Is.EqualTo(16));
        var count = new RewindBuffer(capacity: 1024, maxPoints: 2);
        for (int frame = 0; frame < 5; frame++) count.Add(new(frame, [], []));
        Assert.That(count.Count, Is.EqualTo(2)); Assert.That(count.Find(0, 5)!.Frame, Is.EqualTo(3));
    }

    [Test]
    public void RewindSelectsAnEarlierCheckpointAndTrimsTheAbandonedFuture()
    {
        var buffer = new RewindBuffer();
        foreach (int frame in new[] { 0, 30, 60 }) buffer.Add(new(frame, [], []));
        Assert.That(buffer.Find(30, 60)!.Frame, Is.EqualTo(30));
        Assert.That(buffer.Find(100, 30)!.Frame, Is.Zero);
        Assert.That(buffer.Find(0, 0), Is.Null);
        buffer.TrimAfter(30); Assert.That(buffer.Count, Is.EqualTo(2));
        Assert.That(buffer.LastFrame, Is.EqualTo(30));
        Assert.That(buffer.Add(new(29, [], [])), Is.False);
        Assert.That(buffer.Add(new(31, [], [])), Is.True);
    }

    [Test]
    public void RewindRestoresMemoryVideoAndFrameAndThenBranchesFromThatPoint()
    {
        var host = Start(); Assert.That(host.RewindEnabled, Is.False); host.SetRewindEnabled(true);
        Frames(host, 30); byte[] memory = host.Nes!.Ram.ToArray(); int[] pixels = host.Machine!.Video.Pixels.ToArray();
        host.SetInput("P1 A", true); Frames(host, 30);
        Assert.That(host.Nes.Ram[0], Is.EqualTo(1));
        Assert.That(host.Machine.Video.Pixels, Is.Not.EqualTo(pixels));
        Assert.That(host.Rewind(0.5), Is.True);
        Assert.That(host.Machine.FrameCount, Is.EqualTo(30));
        Assert.That(host.Nes.Ram, Is.EqualTo(memory)); Assert.That(host.Machine.Video.Pixels, Is.EqualTo(pixels));
        Assert.That(host.Paused, Is.True); Assert.That(host.RewindPoints, Is.EqualTo(2));
        Frames(host, 10); Assert.That(host.Machine.FrameCount, Is.EqualTo(40));
        Assert.That(host.Rewind(0.01), Is.True); Assert.That(host.Machine.FrameCount, Is.EqualTo(30));
        host.ClearRewind(); Assert.That(host.RewindBytes, Is.Zero); Assert.That(host.Rewind(), Is.False);
    }

    [Test]
    public void RecordedInputsReplayEveryScreenAndTheFinalStateExactlyWhileIgnoringLiveInput()
    {
        var host = Start(); Assert.That(host.StartInputRecording(), Is.True);
        for (int frame = 0; frame < 12; frame++)
        {
            host.SetInput("P1 A", frame is >= 2 and < 7); host.StepFrame();
        }
        host.StopInputRecording(); Assert.That(host.RecordedFrames, Is.EqualTo(12));
        byte[] state = host.Machine!.SaveState(); int[] pixels = host.Machine.Video.Pixels.ToArray();
        Assert.That(host.StartInputReplay(), Is.True);
        for (int frame = 0; frame < 12; frame++)
        {
            host.SetInput("P1 A", true); host.StepFrame();
        }
        Assert.That(host.IsReplayingInputs, Is.False); Assert.That(host.Paused, Is.True);
        Assert.That(host.Status, Does.Contain("verificada: 12"));
        Assert.That(host.Machine.Video.Pixels, Is.EqualTo(pixels));
        Assert.That(host.Machine.SaveState(), Is.EqualTo(state));
    }

    [Test]
    public void ReplayStopsOnTheFirstDivergentScreenInsteadOfClaimingSuccess()
    {
        var host = Start(); host.StartInputRecording(); Frames(host, 3); host.StopInputRecording();
        host.StartInputReplay(); host.BeforeFrame += m => m.SetKey("P1 A", true);
        host.StepFrame();
        Assert.That(host.IsReplayingInputs, Is.False); Assert.That(host.Paused, Is.True);
        Assert.That(host.Status, Does.Contain("difiere en el cuadro 1"));
        Assert.That(host.RecordedFrames, Is.EqualTo(3));
    }

    [Test]
    public void LoadingAStateCancelsActiveCaptureAndClearsRewindButKeepsACompletedMovie()
    {
        var host = Start(); host.StartInputRecording(); Frames(host, 3); host.StopInputRecording();
        host.QuickSave(); host.SetRewindEnabled(true); Frames(host, 30);
        Assert.That(host.RewindPoints, Is.GreaterThan(0)); host.QuickLoad();
        Assert.That(host.RewindPoints, Is.Zero); Assert.That(host.CanReplayInputs, Is.True);
        host.StartInputReplay(); host.QuickLoad(); Assert.That(host.IsReplayingInputs, Is.False);
        host.StartInputRecording(); host.QuickLoad(); Assert.That(host.IsRecordingInputs, Is.False);
    }

    [Test]
    public void NewMediaDoesNotReuseThePreviousEditionsMovieOrHeldKeys()
    {
        var host = Start(); host.StartInputRecording(); Frames(host, 2); host.StopInputRecording();
        Assert.That(host.CanReplayInputs, Is.True); host.SetInput("P1 A", true);
        host.StartNes(MachineCatalog.Get("nes"), new MediaFile("own.nes", Cartridge(1), "own.nes"));
        Assert.That(host.CanReplayInputs, Is.False); Assert.That(host.RecordedFrames, Is.Zero);
        host.SetInput("P1 A", true); Frames(host, 1); Assert.That(host.Nes!.Ram[0], Is.EqualTo(1));
    }

    [Test]
    public void KeyboardSinkCapturesBothEdgesAndForgetsPreviousKeysAfterRestoration()
    {
        var host = Start(); var mapper = new InputMapper { KeySink = host.SetInput };
        host.InputReset += mapper.ForgetKeys;
        host.StartInputRecording(); mapper.Apply(host.Machine!, ["P1 A"]); host.StepFrame();
        mapper.Apply(host.Machine!, []); host.StepFrame(); host.StopInputRecording();
        host.StartInputReplay(); Frames(host, 2); Assert.That(host.Status, Does.Contain("verificada: 2"));
        mapper.Apply(host.Machine!, ["P1 A"]); host.StepFrame(); Assert.That(host.Nes!.Ram[0], Is.EqualTo(1));
    }

    [Test]
    public void NoMachineAndDebuggerSteppingCannotLeaveAnActiveMovie()
    {
        var host = new EmulatorHost(); Assert.That(host.StartInputRecording(), Is.False); Assert.That(host.Rewind(), Is.False);
        host = Start(); host.Debug.Toggle(0x8000); Assert.That(host.StartInputRecording(), Is.False);
        host.Debug.Clear(); host.StartInputRecording(); host.StepInto(); Assert.That(host.IsRecordingInputs, Is.False);
        host.StartInputRecording(); Frames(host, 2); host.StopInputRecording(); host.StartInputReplay();
        host.StepInto(); Assert.That(host.IsReplayingInputs, Is.False);
    }

    [Test]
    public void RecordingStopsAtItsFrameBudgetWithoutLosingTheCompletedMovie()
    {
        var host = Start(); host.StartInputRecording(seconds: 1);
        Frames(host, 65);
        Assert.That(host.IsRecordingInputs, Is.False);
        Assert.That(host.RecordedFrames, Is.EqualTo((int)Math.Ceiling(host.Machine!.FramesPerSecond)));
        Assert.That(host.Status, Does.Contain("límite")); Assert.That(host.CanReplayInputs, Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => host.StartInputRecording(seconds: 301));
    }

    [Test]
    public void ExcessPendingInputStopsCaptureWithoutInvalidatingTheMappersIteration()
    {
        var host = Start(); var mapper = new InputMapper { KeySink = host.SetInput };
        host.InputReset += mapper.ForgetKeys; host.StartInputRecording(); host.StepFrame();
        for (int i = 0; i < 1100; i++) mapper.Apply(host.Machine!, (i & 1) == 0 ? ["P1 A"] : []);
        Assert.That(host.IsRecordingInputs, Is.False);
        Assert.That(host.RecordedFrames, Is.EqualTo(1)); Assert.That(host.CanReplayInputs, Is.True);
    }

    [Test]
    public void ResetCancelsRecordingAndDropsTheRewindHistory()
    {
        var host = Start(); host.SetRewindEnabled(true); Frames(host, 31);
        Assert.That(host.RewindPoints, Is.GreaterThan(1)); host.StartInputRecording(); host.StepFrame();
        host.Reset(false);
        Assert.That(host.IsRecordingInputs, Is.False); Assert.That(host.IsReplayingInputs, Is.False);
        Assert.That(host.RewindPoints, Is.Zero); Assert.That(host.RewindBytes, Is.Zero);
    }

    [Test]
    public void ScriptedTypingDoesNotLeakIntoARecordingOrItsReplay()
    {
        var host = Start(); host.Machine!.QueueKeys(30, "P1 A"); host.Machine.QueueKeys(30, "P1 B");
        host.StartInputRecording(); Frames(host, 4); host.StopInputRecording();
        Assert.That(host.Nes!.Ram[0], Is.Zero);
        host.Machine.QueueKeys(30, "P1 A"); host.StartInputReplay(); Frames(host, 4);
        Assert.That(host.Status, Does.Contain("verificada: 4")); Assert.That(host.Nes.Ram[0], Is.Zero);
    }

    [Test]
    public void CancellingAScriptReleasesItsHeldKeysAndAllowsANewSequenceImmediately()
    {
        var sequence = new KeySequencer(); var events = new List<(string, bool)>();
        void Key(string key, bool pressed) => events.Add((key, pressed));
        sequence.Enqueue(30, ["A"]); sequence.Enqueue(30, ["B"]); sequence.Tick(0, Key);
        sequence.Clear(Key); Assert.That(sequence.Busy, Is.False); sequence.Tick(1, Key);
        sequence.Enqueue(2, ["C"]); sequence.Tick(2, Key);
        Assert.That(events, Is.EqualTo(new[] { ("A", true), ("A", false), ("C", true) }));
    }

    [Test]
    public void SpectrumCancelsBothHeldAndPendingMacroKeys()
    {
        var zx = new Retro.Systems.Spectrum.Spectrum48(new byte[16384]);
        zx.QueueKeys(30, "A"); zx.QueueKeys(30, "B"); zx.RunFrame(null);
        Assert.That(zx.TypingInProgress, Is.True); Assert.That(zx.ReadPort(0xFDFE) & 1, Is.Zero);
        zx.CancelQueuedKeys(); Assert.That(zx.TypingInProgress, Is.False);
        Assert.That(zx.ReadPort(0xFDFE) & 1, Is.EqualTo(1));
    }
}
