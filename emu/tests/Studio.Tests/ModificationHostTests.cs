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
}
