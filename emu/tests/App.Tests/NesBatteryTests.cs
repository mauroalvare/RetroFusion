using Retro.App;
using Retro.Core;
using Retro.Machines;
using Retro.Systems.Nes;

namespace App.Tests;

public sealed class NesBatteryTests
{
    private string _folder = null!;
    private NesBatteryStore _store = null!;
    [SetUp] public void OwnFiles()
    {
        _folder = Path.Combine(Path.GetTempPath(), "retro-battery-" + Guid.NewGuid().ToString("N"));
        _store = new(_folder);
    }
    [TearDown] public void RemoveOwnFiles()
    {
        string full = Path.GetFullPath(_folder);
        Assert.That(full.StartsWith(Path.GetFullPath(Path.GetTempPath()) + "retro-battery-", StringComparison.OrdinalIgnoreCase), Is.True);
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }

    internal static byte[] Image(byte edition = 0, bool battery = true)
    {
        var data = new byte[16 + NesCartridge.PrgUnit];
        "NES\x1a"u8.CopyTo(data); data[4] = 1; data[6] = (byte)(battery ? 2 : 0);
        // Increment SRAM once at boot, then wait: own executable program, no firmware or commercial media.
        new byte[] { 0xee, 0x00, 0x60, 0x4c, 0x03, 0xc0 }.CopyTo(data, 16);
        data[30] = edition; data[^4] = 0x00; data[^3] = 0xc0;
        return data;
    }
    private EmulatorHost Host(byte edition = 0, bool battery = true, string name = "Faro.nes")
    {
        var host = new EmulatorHost(_store);
        host.StartNes(MachineCatalog.Get("nes"), new MediaFile(name, Image(edition, battery), name));
        return host;
    }
    private static byte Value(EmulatorHost host) => host.Nes!.Peek(0x6000);

    [Test]
    public void RealGuestWritesAreSavedWhilePausedAndReloadedByContentAfterMovingTheGame()
    {
        var first = Host(); string path = first.Battery!.Path;
        Assert.That(File.Exists(path), Is.False, "An untouched cartridge must not create a blank save.");
        first.Nes!.RunFrame(null); first.Paused = true;
        first.PollBattery(1); Assert.That(File.Exists(path), Is.False);
        first.PollBattery(1);
        var reopened = Host(name: @"C:\otra carpeta\Copia renombrada.nes");
        Assert.Multiple(() =>
        {
            Assert.That(Value(reopened), Is.EqualTo(1)); Assert.That(reopened.Nes!.FrameCount, Is.Zero);
            Assert.That(reopened.Battery!.Path, Is.EqualTo(path)); Assert.That(first.Battery.Pending, Is.False);
        });
        Assert.That(Value(Host(edition: 1)), Is.Zero, "Another edition must never load this save.");
    }

    [Test]
    public void SwitchingToTheSameCartridgeFlushesBeforeLoadingItsBattery()
    {
        var host = Host(); host.Nes!.Write(0x6000, 37);
        host.StartNes(MachineCatalog.Get("nes"), new MediaFile("renamed.nes", Image(), "renamed.nes"));
        Assert.That(Value(host), Is.EqualTo(37));
        host.Nes!.Write(0x6000, 72);
        host.StartNes(MachineCatalog.Get("nes"), new MediaFile("other.nes", Image(1), "other.nes"));
        Assert.That(Value(host), Is.Zero); Assert.That(Value(Host()), Is.EqualTo(72));
    }

    [Test]
    public void StatesAndPowerCyclesPreserveTheSelectedBatteryProgress()
    {
        var host = Host(); host.Nes!.Write(0x6000, 23); host.QuickSave();
        host.Nes.Write(0x6000, 45); Assert.That(host.TryPreserveBattery(), Is.True);
        host.QuickLoad(); host.Reset(true); Assert.That(Value(host), Is.EqualTo(23));
        Assert.That(host.TryPreserveBattery(), Is.True); Assert.That(Value(Host()), Is.EqualTo(23));
        Assert.That(File.Exists(host.Battery!.Path + ".previous"), Is.True);
        string copy = Path.Combine(_folder, "previous.rfbattery"); File.Copy(host.Battery.Path + ".previous", copy);
        host.RestoreBattery(copy); Assert.That(Value(host), Is.EqualTo(45)); Assert.That(host.Paused, Is.True);
    }

    [Test]
    public void NonBatteryAndUnconfiguredHostsNeverTouchPersistence()
    {
        var host = Host(battery: false); host.Nes!.RunFrame(null); host.PollBattery(5);
        Assert.That(host.Battery, Is.Null); Assert.That(Directory.Exists(_folder), Is.False);
        var lab = new EmulatorHost(); lab.StartNes(MachineCatalog.Get("nes"), new MediaFile("lab.nes", Image(), "lab.nes"));
        lab.Nes!.RunFrame(null); lab.PollBattery(5); lab.EnsureBatteryPreserved();
        Assert.That(lab.Battery, Is.Null); Assert.That(Directory.Exists(_folder), Is.False);
    }

    [Test]
    public void TwoInstancesCannotSilentlyOverwriteOneAnothersProgress()
    {
        var first = Host(); var second = Host();
        first.Nes!.Write(0x6000, 21); second.Nes!.Write(0x6000, 38);
        Assert.That(first.TryPreserveBattery(), Is.True);
        byte[] expected = File.ReadAllBytes(first.Battery!.Path);
        Assert.That(second.TryPreserveBattery(), Is.False);
        Assert.That(second.BatteryProblem, Does.Contain("Otra instancia"));
        Assert.That(File.ReadAllBytes(first.Battery.Path), Is.EqualTo(expected));
        Assert.That(Value(second), Is.EqualTo(38));
        var original = second.Machine;
        Assert.That(() => second.StartNes(MachineCatalog.Get("nes"), new MediaFile("other.nes", Image(1), "other.nes")), Throws.TypeOf<IOException>());
        Assert.That(second.Machine, Is.SameAs(original), "Changing games must not discard an unsaved session.");
        string exported = second.Battery!.Export(Path.Combine(_folder, "second.rfbattery"));
        Assert.That(second.TryPreserveBattery(), Is.True, "An explicit export preserves the exact current RAM.");
        second.Nes.Write(0x6000, 39); Assert.That(second.TryPreserveBattery(), Is.False, "A stale export cannot excuse new unsaved writes.");
        second.RestoreBattery(exported);
        Assert.That(Value(Host()), Is.EqualTo(38));
        Assert.That(File.ReadAllBytes(second.Battery.PreservedPath!), Is.EqualTo(expected));
        Assert.That(File.Exists(second.Battery.ExportedPath), Is.True, "RAM from before recovery is preserved too.");
    }

    [TestCase("truncated"), TestCase("checksum"), TestCase("version"), TestCase("identity"), TestCase("oversized")]
    public void InvalidExistingFilesAreNeverOverwrittenOrPartiallyLoaded(string damage)
    {
        var writer = Host(); writer.Nes!.Write(0x6000, 44); writer.EnsureBatteryPreserved();
        string path = writer.Battery!.Path;
        byte[] bad = File.ReadAllBytes(path);
        if (damage == "truncated") bad = bad[..70];
        else if (damage == "checksum") bad[^1] ^= 1;
        else if (damage == "version") bad[14] = 99;
        else if (damage == "identity") bad[18] ^= 1;
        else bad = new byte[65537];
        File.WriteAllBytes(path, bad);
        var host = Host(); Assert.That(host.BatteryProblem, Is.Not.Null);
        Assert.That(Value(host), Is.Zero, "A failed load must not partially change cartridge RAM.");
        host.Nes!.Write(0x6000, 61); host.PollBattery(5);
        Assert.That(host.TryPreserveBattery(), Is.False); Assert.That(File.ReadAllBytes(path), Is.EqualTo(bad));
        string recovery = host.Battery!.Export(Path.Combine(_folder, "safe-copy.rfbattery"));
        if (damage == "oversized") Assert.That(() => host.RestoreBattery(recovery), Throws.TypeOf<InvalidDataException>());
        else
        {
            host.RestoreBattery(recovery);
            Assert.That(File.ReadAllBytes(host.Battery.PreservedPath!), Is.EqualTo(bad));
            Assert.That(Value(Host()), Is.EqualTo(61)); Assert.That(host.BatteryProblem, Is.Null);
        }
    }

    [Test]
    public void FailedWritesCanBeRetriedWithoutLosingRamOrThePreviousFile()
    {
        var host = Host(); host.Nes!.Write(0x6000, 14); host.EnsureBatteryPreserved();
        byte[] previous = File.ReadAllBytes(host.Battery!.Path);
        using (var busy = new FileStream(host.Battery.Path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            host.Nes.Write(0x6000, 68); host.PollBattery(5);
            Assert.That(host.BatteryAtRisk, Is.True); Assert.That(host.TryPreserveBattery(), Is.False);
            Assert.That(File.ReadAllBytes(host.Battery.Path), Is.EqualTo(previous));
        }
        Assert.That(host.TryPreserveBattery(), Is.True); Assert.That(host.BatteryProblem, Is.Null);
        Assert.That(Value(Host()), Is.EqualTo(68)); Assert.That(host.BatteryAtRisk, Is.False);
        Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
    }

    [Test]
    public void ExportsNeverReplaceAnExistingFileAndWrongGameRecoveryLeavesEverythingIntact()
    {
        var host = Host(); host.Nes!.Write(0x6000, 37); host.EnsureBatteryPreserved();
        string copy = host.Battery!.Export(Path.Combine(_folder, "copy.rfbattery")); byte[] expected = File.ReadAllBytes(copy);
        host.Nes.Write(0x6000, 51);
        Assert.That(() => host.Battery.Export(copy), Throws.TypeOf<IOException>()); Assert.That(File.ReadAllBytes(copy), Is.EqualTo(expected));
        var other = Host(edition: 1); other.Nes!.Write(0x6000, 88); other.EnsureBatteryPreserved();
        string otherCopy = other.Battery!.Export(Path.Combine(_folder, "other.rfbattery"));
        Assert.That(() => host.RestoreBattery(otherCopy), Throws.TypeOf<InvalidDataException>());
        Assert.That(Value(host), Is.EqualTo(51)); Assert.That(Value(Host()), Is.EqualTo(37));
        Assert.That(host.Battery.ExportedPath, Is.EqualTo(copy));
    }

    [Test]
    public void UnsupportedNvramLayoutsWarnInsteadOfPretendingToSaveThem()
    {
        byte[] image = Image(); image[7] = 8; image[10] = 0x60;
        var host = new EmulatorHost(_store); host.StartNes(MachineCatalog.Get("nes"), new MediaFile("small.nes", image, "small.nes"));
        Assert.That(host.Battery, Is.Null); Assert.That(host.BatteryProblem, Does.Contain("todavía no"));
        Assert.That(Directory.Exists(_folder), Is.False);
    }

    [Test]
    public void AnExportOnlyAllowsLeavingWhileTheActualFileStillMatchesCurrentRam()
    {
        var first = Host(); var second = Host();
        first.Nes!.Write(0x6000, 14); first.EnsureBatteryPreserved();
        second.Nes!.Write(0x6000, 38);
        string copy = second.Battery!.Export(Path.Combine(_folder, "copy.rfbattery"));
        Assert.That(second.TryPreserveBattery(), Is.True);
        byte[] expected = File.ReadAllBytes(copy);
        File.WriteAllBytes(copy, expected[..40]); Assert.That(second.TryPreserveBattery(), Is.False);
        File.WriteAllBytes(copy, expected); Assert.That(second.TryPreserveBattery(), Is.True);
        File.Delete(copy); Assert.That(second.TryPreserveBattery(), Is.False);
    }

    [Test]
    public void AFailurePreservingThePreviousSaveDoesNotReplaceThePrimary()
    {
        var host = Host(); host.Nes!.Write(0x6000, 17); host.EnsureBatteryPreserved();
        string path = host.Battery!.Path; byte[] expected = File.ReadAllBytes(path);
        Directory.CreateDirectory(path + ".previous");
        host.Nes.Write(0x6000, 89);
        Assert.That(host.TryPreserveBattery(), Is.False); Assert.That(File.ReadAllBytes(path), Is.EqualTo(expected));
        Assert.That(Value(host), Is.EqualTo(89)); Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
        Directory.Delete(path + ".previous");
        Assert.That(host.TryPreserveBattery(), Is.True); Assert.That(Value(Host()), Is.EqualTo(89));
    }

    [Test]
    public void AnUpdateCannotStartItsHelperUntilTheBatteryIsPreserved()
    {
        var host = Host(); host.Nes!.Write(0x6000, 27);
        string path = host.Battery!.Path; Directory.CreateDirectory(_folder);
        int installed = 0;
        // Transport and installation are never called: the only installer is this counter.
        using var updates = new UpdateWindow(["--no-update"], root: _folder, settings: Path.Combine(_folder, "settings"),
            install: _ => installed++, beforeInstall: host.EnsureBatteryPreserved);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(UpdateWindow).GetField("_ready", flags)!.SetValue(updates, new Retro.Update.UpdatePlan { ToVersion = "0.3.8" });
        var install = typeof(UpdateWindow).GetMethod("Install", flags)!;
        using (var busy = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            install.Invoke(updates, null);
            Assert.That(installed, Is.Zero); Assert.That(updates.RestartRequested, Is.False);
            Assert.That(Value(host), Is.EqualTo(27));
        }
        install.Invoke(updates, null);
        Assert.That(installed, Is.EqualTo(1)); Assert.That(updates.RestartRequested, Is.True);
        Assert.That(Value(Host()), Is.EqualTo(27));
    }
}
