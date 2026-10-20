using Retro.App;
using Retro.Core;
using Retro.Fusion;
using Retro.Machines;

namespace Studio.Tests;

[TestFixture, NonParallelizable]
public sealed class ModificationHostTests
{
    private string _directory = "";
    private string? _oldProfiles;

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "retro-mod-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "48.rom"), new byte[0x4000]);
        _oldProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_directory, "profiles"));
    }

    [TearDown]
    public void Teardown()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _oldProfiles);
        Directory.Delete(_directory, true);
    }

    private EmulatorHost Host()
    {
        var host = new EmulatorHost { TurboWhileLoading = false };
        host.Start(MachineCatalog.Get("zx48"), _directory);
        return host;
    }
    private static MediaFile Tape(byte value = 0) => new("own-test.tap", [3, 0, 0xFF, value, (byte)(0xFF ^ value)], "own-test.tap");

    [Test]
    public void LoadedProfileUsesActualBytesAndReloadOfSameNameInvalidatesRevision()
    {
        var first = Tape(); var second = Tape(1);
        var profile = new GameProfile { Id = "own-hash-test", Name = "Test", Machine = "zx48", Match = new() { Sha256 = [ProfileMatch.HashOf(first)], NamePattern = "own-test" } };
        ProfileLibrary.Save(profile);
        var host = Host(); host.Load(first, false); long revision = host.MediaRevision;
        Assert.That(host.LoadedProfile?.Id, Is.EqualTo(profile.Id));
        host.QuickSave();
        host.Load(second, false);
        Assert.That(host.MediaRevision, Is.GreaterThan(revision)); Assert.That(host.LoadedProfile, Is.Null);
        byte[] state = host.Machine!.SaveState();
        host.QuickLoad(); Assert.That(host.Machine.SaveState(), Is.EqualTo(state), "A different medium must discard an earlier quick save.");
    }

    [Test]
    public void NewlyPreparedProfileCanBeLoadedWithoutChangingTheRunningPartida()
    {
        var tape = Tape(); var host = Host(); host.Load(tape, false);
        Assert.That(host.LoadedProfile, Is.Null);
        byte[] before = host.Machine!.SaveState(); long revision = host.MediaRevision;
        ProfileLibrary.Save(new GameProfile { Id = "new-preparation", Name = "Test", Machine = "zx48", Match = new() { Sha256 = [ProfileMatch.HashOf(tape)] } });
        host.RefreshLoadedProfile();
        Assert.That(host.LoadedProfile?.Id, Is.EqualTo("new-preparation")); Assert.That(host.MediaRevision, Is.GreaterThan(revision));
        Assert.That(host.Machine.SaveState(), Is.EqualTo(before));
        revision = host.MediaRevision; host.RefreshLoadedProfile();
        Assert.That(host.MediaRevision, Is.EqualTo(revision), "Reopening Modificar must not discard an existing experiment when its profile is unchanged.");
    }

    [Test]
    public void FreezeIsReappliedForEveryEmulatedFrameIncludingTurbo()
    {
        var host = Host();
        // A private program increments RAM continuously. The callback must see the
        // result of the preceding frame and restore 9 before each subsequent frame.
        host.LoadBinary([0x21, 0, 0x80, 0x34, 0xC3, 3, 0x90], 0x9000, true);
        var profile = new GameProfile { Variables = new() { ["counter"] = new() { Address = "8000" } } };
        var edits = new GameModifications(host.Machine!, profile); edits.Apply([new("counter", 9, true)]);
        int frames = 0;
        host.BeforeFrame += machine => { edits.Tick(); Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(9)); frames++; };
        host.Continue(); host.Turbo = true;
        for (int i = 0; i < 4 && frames < 2; i++) host.Update(1.0 / 60);
        Assert.That(frames, Is.GreaterThan(1)); Assert.That(host.Machine!.FrameCount, Is.EqualTo(frames));
        int before = frames;
        host.Paused = true; host.StepFrame();
        Assert.That(frames, Is.EqualTo(before + 1)); Assert.That(host.Machine.FrameCount, Is.EqualTo(frames));
    }

    [Test]
    public void RestorationAndResetInvalidateRevisionAndClearQueuedSound()
    {
        var host = Host(); host.Load(Tape(), false);
        host.QuickSave(); long revision = host.MediaRevision;
        host.Audio.Push(new short[] { 11, 22 }); host.QuickLoad();
        Assert.That(host.MediaRevision, Is.GreaterThan(revision)); Assert.That(host.Audio.Count, Is.Zero);
        revision = host.MediaRevision; host.Reset(false); Assert.That(host.MediaRevision, Is.GreaterThan(revision));
    }

    [Test]
    public void DamagedStateFilePreservesTheCurrentCpuMemoryAndPicture()
    {
        var host = Host(); var machine = host.Machine!;
        byte[] old = machine.SaveState();
        string path = Path.Combine(_directory, "damaged.rfstate");
        File.WriteAllBytes(path, old[..^10]);
        machine.Memory.Poke(0x8000, 73); machine.Cpu.ProgramCounter = 0x9123;
        machine.Video.Pixels[0] = 0x123456;
        byte[] before = machine.SaveState(); long revision = host.MediaRevision;
        Assert.Throws<InvalidDataException>(() => host.LoadStateFromFile(path));
        Assert.That(machine.SaveState(), Is.EqualTo(before), "A partial load must be rolled back.");
        Assert.That(machine.Video.Pixels[0], Is.EqualTo(0x123456));
        Assert.That(host.MediaRevision, Is.EqualTo(revision));
    }

    [Test]
    public void QuickSaveRestoresThePausedPictureAlongWithTheMachine()
    {
        var host = Host(); var machine = host.Machine!;
        host.Paused = true; machine.Video.Pixels[0] = 0x123456; machine.Memory.Poke(0x8000, 0);
        host.QuickSave();
        machine.Video.Pixels[0] = 0x654321; machine.Memory.Poke(0x8000, 9);
        host.QuickLoad();
        Assert.That(machine.Video.Pixels[0], Is.EqualTo(0x123456));
        Assert.That(machine.Memory.Peek(0x8000), Is.Zero);
        Assert.That(host.Paused, Is.True);
    }

    [Test, Platform("Win")]
    public void FailedSlotOverwriteKeepsThePreviousFileAndRemovesItsTemporaryFile()
    {
        var host = Host(); string dir = Path.Combine(_directory, "new-slots");
        string path = Path.Combine(dir, "slot.rfstate");
        host.SaveStateToFile(path);
        byte[] saved = File.ReadAllBytes(path);
        host.Machine!.Memory.Poke(0x8000, 9);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Assert.Catch(() => host.SaveStateToFile(path));
            Assert.That(error, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        }
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
        Assert.That(Directory.GetFiles(dir), Has.Length.EqualTo(1));
    }

    private sealed class OwnFusion(IMachine first, IMachine second) : IFusionRun
    {
        public string Title => "Private test";
        public string Description => "";
        public IReadOnlyList<FusionSlot> Slots { get; } = new[]
        {
            new FusionSlot { Id = "a", Profile = new(), Machine = first },
            new FusionSlot { Id = "b", Profile = new(), Machine = second }
        };
        public VideoFrame Video => first.Video;
        public List<FusionEvent> Events { get; } = new();
        public long Tick => 0;
        public void SetInput(string input, bool pressed) { }
        public void Update(double seconds) { }
        public int DrainAudio(short[] destination) => 0;
    }

    [Test]
    public void FusionStateCommandsDoNotRestoreOneOriginalIntoAnother()
    {
        var host = Host(); var first = host.Machine!;
        var second = new Retro.Systems.Spectrum.Spectrum48(new byte[0x4000]);
        host.StartFusion(new OwnFusion(first, second)); host.QuickSave();
        host.FusionFocus = "b"; second.Memory.Poke(0x8000, 73);
        byte[] before = second.SaveState(); host.QuickLoad();
        Assert.That(second.SaveState(), Is.EqualTo(before));
        string path = Path.Combine(_directory, "fusion.rfstate"); host.SaveStateToFile(path);
        Assert.That(File.Exists(path), Is.False, "Saving a focused machine is not a complete fusion save.");
    }

    [Test]
    public void ClearingAudioDoesNotReplaySamplesFromThePreviousGame()
    {
        var audio = new AudioQueue(16); audio.Push(new short[] { 16000 });
        var sample = new short[1]; audio.Pop(sample); Assert.That(sample[0], Is.EqualTo(16000));
        audio.Clear(); var silence = new short[8]; audio.Pop(silence);
        Assert.That(silence, Is.All.Zero);
    }
}
