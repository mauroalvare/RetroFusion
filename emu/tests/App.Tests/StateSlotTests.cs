using Retro.App;
using Retro.Core;
using Retro.Machines;

namespace App.Tests;

[TestFixture, NonParallelizable]
public sealed class StateSlotTests
{
    private string _directory = "";
    private string? _profiles;
    private StateSlotStore Store => new(Path.Combine(_directory, "states"));

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "retro-slot-" + Guid.NewGuid().ToString("N"));
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
    private static byte[] Image(byte edition)
    {
        byte[] data = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(data); data[4] = 1;
        Array.Fill(data, (byte)0xEA, 16, 16384); data[20] = edition;
        data[^4] = 0; data[^3] = 0x80;
        return data;
    }
    private static void Start(EmulatorHost host, string name, byte edition) =>
        host.StartNes(MachineCatalog.Get("nes"), new MediaFile(name, Image(edition), name));
    private static string Key(EmulatorHost host) => StateSlotStore.GameKey(host.Machine!.Id, host.LoadedMediaHashes, host.FirmwareIdentity);

    [Test]
    public void SameMachineKeepsIndependentSlotsAndRenamingTheGameRecoversThem()
    {
        var host = new EmulatorHost(); var store = Store;
        Start(host, "first.nes", 1); string first = Key(host);
        host.Nes!.Ram[37] = 17; host.SaveStateToFile(store.PathFor(first, 1));
        Start(host, "other.nes", 2); string second = Key(host);
        host.Nes!.Ram[37] = 29; host.SaveStateToFile(store.PathFor(second, 1));
        Assert.That(second, Is.Not.EqualTo(first));
        host.Nes.Ram[37] = 0; host.LoadStateFromFile(store.PathFor(second, 1));
        Assert.That(host.Nes.Ram[37], Is.EqualTo(29));
        Start(host, "renamed.nes", 1);
        Assert.That(Key(host), Is.EqualTo(first));
        host.LoadStateFromFile(store.PathFor(Key(host), 1));
        Assert.That(host.Nes!.Ram[37], Is.EqualTo(17));
    }

    [Test]
    public void MachineWideLegacyStatesRemainReadableAndAreNeverOverwrittenByNewSlots()
    {
        var host = new EmulatorHost(); var store = Store; Start(host, "own.nes", 1);
        host.Nes!.Ram[0] = 41; string legacy = store.LegacyPath("nes", 2); host.SaveStateToFile(legacy);
        byte[] previous = File.ReadAllBytes(legacy);
        host.Nes.Ram[0] = 99; host.SaveStateToFile(store.PathFor(Key(host), 2));
        Assert.That(File.ReadAllBytes(legacy), Is.EqualTo(previous));
        Assert.That(store.ListLegacy("nes").Single(s => s.Number == 2).SavedAt, Is.Not.Null);
        host.LoadStateFromFile(legacy); Assert.That(host.Nes.Ram[0], Is.EqualTo(41));
    }

    [Test]
    public void AnotherEditionOrFirmwareDoesNotReuseTheSlotDirectory()
    {
        string[] hashes = [new('A', 64)];
        string first = StateSlotStore.GameKey("zx48", hashes, "firmware1");
        string second = StateSlotStore.GameKey("zx48", hashes, "firmware2");
        string noMedia = StateSlotStore.GameKey("zx48", [], "firmware1");
        Assert.That(new[] { first, second, noMedia }.Distinct().Count(), Is.EqualTo(3));
        var store = Store;
        Directory.CreateDirectory(Path.GetDirectoryName(store.PathFor(first, 3))!);
        File.WriteAllBytes(store.PathFor(first, 3), [1]);
        Assert.That(store.List(second).Select(s => s.SavedAt), Is.All.Null);
        Assert.That(store.List(noMedia).Select(s => s.SavedAt), Is.All.Null);
    }

    [Test]
    public void ListingDatesDoesNotCreateEmptySlotsOrFolders()
    {
        var store = Store; string key = new('A', 64);
        Assert.That(store.List(key).Select(s => s.SavedAt), Is.All.Null);
        Assert.That(Directory.Exists(Path.Combine(_directory, "states")), Is.False);
        Directory.CreateDirectory(Path.GetDirectoryName(store.PathFor(key, 5))!);
        File.WriteAllBytes(store.PathFor(key, 5), [1]);
        var date = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(store.PathFor(key, 5), date);
        var slot = store.List(key).Single(s => s.Number == 5);
        Assert.That(slot.SavedAt!.Value.ToUniversalTime(), Is.EqualTo(date));
        Assert.That(Directory.GetFiles(Path.Combine(_directory, "states"), "*", SearchOption.AllDirectories).Length, Is.EqualTo(1));
    }

    [Test]
    public void InvalidIdentitiesAndSlotNumbersCannotEscapeTheStore()
    {
        var store = Store;
        Assert.Throws<ArgumentException>(() => store.PathFor("../../outside", 1));
        Assert.Throws<ArgumentException>(() => store.LegacyPath("../outside", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.PathFor(new('A', 64), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.LegacyPath("nes", 6));
        Assert.That(Directory.Exists(Path.Combine(_directory, "states")), Is.False);
    }
}