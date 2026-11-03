using System.Reflection;
using Retro.Ai;
using Retro.App;
using Retro.Core;
using Retro.Machines;

namespace App.Tests;

[NonParallelizable]
public sealed class CodePatchTransactionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _scope = "";
    private string? _previousProfiles;
    [SetUp]
    public void IsolateProfiles()
    {
        _scope = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-patch-scope-" + Guid.NewGuid().ToString("N"))).FullName;
        _previousProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_scope, "profiles"));
    }
    [TearDown]
    public void RestoreProfiles()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _previousProfiles);
        Assert.That(Path.GetDirectoryName(_scope), Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
        Assert.That(Path.GetFileName(_scope), Does.StartWith("retro-patch-scope-")); Directory.Delete(_scope, true);
    }
    private static EmulatorHost Host()
    {
        byte[] rom = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(rom); rom[4] = 1;
        Array.Fill(rom, (byte)0xEA, 16, 16384); rom[^3] = 0x80;
        var host = new EmulatorHost(); host.StartNes(MachineCatalog.Get("nes"), new MediaFile("propio.nes", rom, "propio.nes"));
        return host;
    }
    private static bool Write(IMachine machine, uint address, byte[] bytes) =>
        (bool)typeof(CodeWindow).GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [machine, address, bytes])!;

    [TestCase("8000", "AA|BB CC"), TestCase("8000", "AA BB|CC"), TestCase("8000", "GG|CC")]
    [TestCase("8000", "AA"), TestCase("8000", "|"), TestCase("XXXX", "AA|BB"), TestCase("FFFF", "AA BB|CC DD")]
    public void DamagedOrIncompleteBackupsCannotBeAppliedOrRemovedAsIfTheyWereComplete(string address, string saved)
    {
        Assert.That(CodeWindow.TryReadPatch(address, saved, 16, out _, out _, out _), Is.False);
    }

    [Test]
    public void ValidPatchRecordsPreserveBytesAndAddressesAbove64K()
    {
        Assert.That(CodeWindow.TryReadPatch("10003", "EA 20|00 FF", 20, out uint address, out byte[] original, out byte[] replacement), Is.True);
        Assert.That(address, Is.EqualTo(0x10003));
        Assert.That(original, Is.EqualTo(new byte[] { 0xEA, 0x20 }));
        Assert.That(replacement, Is.EqualTo(new byte[] { 0, 0xFF }));
    }

    [TestCase(0x1FFFu), TestCase(0xFFFFu)]
    public void ARejectedPatchDoesNotLeaveAPartialWriteOrWrapTheAddressSpace(uint address)
    {
        var host = Host(); var machine = host.Machine!; byte[] state = machine.SaveState();
        byte before = machine.Memory.Peek(address);
        Assert.That(Write(machine, address, [0x12, 0x34]), Is.False);
        Assert.That(machine.Memory.Peek(address), Is.EqualTo(before));
        Assert.That(machine.SaveState(), Is.EqualTo(state));
    }

    [TestCase(false, false), TestCase(false, true), TestCase(true, false)]
    public void EditingAPatchKeepsAllOriginalBytesAndRejectsOverlappingRecords(bool overlap, bool shorten)
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-patch-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var host = Host(); var machine = host.Machine!;
            using var window = new CodeWindow(host, new AiSettings { Provider = "manual", ProvidersDisabled = true }, _ => { }, history: new ModificationHistory(persist: false), annotationsDirectory: Path.Combine(folder, "notes"));
            var notes = CodeAnnotations.Load("propio", folder);
            typeof(CodeWindow).GetField("_notes", Private)!.SetValue(window, notes);
            typeof(CodeWindow).GetField("_gameId", Private)!.SetValue(window, "propio");
            void Patch(uint address, string hex) => typeof(CodeWindow).GetMethod("Patch", Private)!.Invoke(window, [machine, address, hex]);
            Patch(0x8000, overlap || shorten ? "AA BB" : "AA");
            if (overlap)
            {
                Patch(0x8001, "CC");
                Assert.That(notes.Patches, Has.Count.EqualTo(1));
                Assert.That(machine.Memory.Peek(0x8001), Is.EqualTo(0xBB));
            }
            else
            {
                Patch(0x8000, shorten ? "CC" : "BB CC");
                Assert.That(machine.Memory.Peek(0x8001), Is.EqualTo(shorten ? 0xEA : 0xCC));
                byte[] original = notes.Patches["8000"].Split('|')[0].Split(' ').Select(b => Convert.ToByte(b, 16)).ToArray();
                Assert.That(Write(machine, 0x8000, original), Is.True);
                Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(0xEA));
                Assert.That(machine.Memory.Peek(0x8001), Is.EqualTo(0xEA), "Undo restores the additional byte too.");
            }
        }
        finally
        {
            Assert.That(Path.GetDirectoryName(folder), Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
            Assert.That(Path.GetFileName(folder), Does.StartWith("retro-patch-")); Directory.Delete(folder, true);
        }
    }
}
