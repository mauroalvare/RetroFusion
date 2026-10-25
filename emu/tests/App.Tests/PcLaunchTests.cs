using Retro.App;
using Retro.Core;
using Retro.Compat;
using Retro.Machines;
using Retro.Systems.Pc;

namespace App.Tests;

[TestFixture]
public sealed class PcLaunchTests
{
    private string _folder = "";
    private static byte[] Bios()
    {
        byte[] bios = Enumerable.Repeat((byte)0x90, 8192).ToArray();
        new byte[] { 0xE8, 2, 0, 0xF4, 0x90, 0xC3 }.CopyTo(bios, 8176);
        return bios;
    }
    [SetUp] public void Setup()
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-pc-launch-" + Guid.NewGuid())).FullName;
        Directory.CreateDirectory(Path.Combine(_folder, "pcxt"));
        File.WriteAllBytes(Path.Combine(_folder, "pcxt", "pcxt-bios.rom"), Bios());
    }
    [TearDown] public void Cleanup() => Directory.Delete(_folder, true);
    private static MediaFile Disk(string name = "disco.img") => new(name, new byte[163840], name);
    private EmulatorHost Host()
    {
        var host = new EmulatorHost(); host.StartPc(MachineCatalog.Get("pcxt"), Disk(), _folder); return host;
    }

    [Test]
    public void BootDiskIsRecognizedAndLaunchesWithNestedFirmware()
    {
        var disk = Disk(); disk.Data[510] = 0x55; disk.Data[511] = 0xAA;
        var candidate = MachineCatalog.Identify(disk).First();
        Assert.That((candidate.Machine.Id, candidate.Confidence), Is.EqualTo(("pcxt", 90)));
        Assert.That(MachineCatalog.NotLoadableYet(candidate), Is.Null);
        var launch = MachineLauncher.Launch("pcxt", [disk], null, _folder);
        Assert.That(launch.Machine, Is.TypeOf<PcXt>());
        Assert.That(launch.Machine.Memory.AddressBits, Is.EqualTo(20));
        Assert.That(launch.Machine.Cpu.ProgramCounter, Is.EqualTo(0xFFFF0u));
    }
    [Test]
    public void MissingFirmwareAndStandaloneProgramsHaveActionableErrors()
    {
        var program = new MediaFile("programa.com", [0xCD, 0x20], "programa.com");
        Assert.That(() => MachineLauncher.Launch("pcxt", [program], null, _folder),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("DOS"));
        File.Delete(Path.Combine(_folder, "pcxt", "pcxt-bios.rom"));
        Assert.That(() => MachineLauncher.Launch("pcxt", [Disk()], null, _folder),
            Throws.TypeOf<FileNotFoundException>().With.Message.Contains("firmware/pcxt/pcxt-bios.rom"));
    }
    [Test]
    public void StepOverAndRunToKeepPhysicalAddressesAbove64K()
    {
        var host = Host(); host.Pc!.Cpu8088.SP = 0x8000;
        Assert.That(host.Machine!.ExecutedMap, Has.Length.EqualTo(1 << 20));
        host.StepOver(); host.Update(0.02);
        Assert.That(host.Paused, Is.True);
        Assert.That(host.Machine.Cpu.ProgramCounter, Is.EqualTo(0xFFFF3u));
        Assert.That(host.Machine.ExecutedMap![0xFFFF0], Is.True);
        Assert.That(host.Machine.ExecutedMap[0xFFFF5], Is.True);
        Assert.That(host.Machine.ExecutedMap[0xFFF0], Is.False);
    }
    [Test]
    public void BinaryLoaderUsesTheMachinesAddressWidthAndRejectsTruncation()
    {
        var host = Host(); host.LoadBinary([0xB8, 0x34, 0x12], 0x10000, true); host.StepInto();
        Assert.That(host.Pc!.Cpu8088.AX, Is.EqualTo(0x1234));
        Assert.That(host.Pc.Peek(0), Is.Zero);
        Assert.That(() => host.LoadBinary(new byte[2], 0xFFFFF, false), Throws.TypeOf<InvalidDataException>());
    }
    [Test]
    public void DiskAndFirmwareIdentityScopeSavedStates()
    {
        var first = Host(); first.Pc!.Poke(0x10001, 47); first.QuickSave();
        first.Pc.Poke(0x10001, 6); first.QuickLoad();
        Assert.That(first.Pc.Peek(0x10001), Is.EqualTo(47));
        string key = StateSlotStore.GameKey("pcxt", first.LoadedMediaHashes, first.FirmwareIdentity);
        var other = Disk("otra.img"); other.Data[1024] = 77;
        first.StartPc(MachineCatalog.Get("pcxt"), other, _folder);
        Assert.That(StateSlotStore.GameKey("pcxt", first.LoadedMediaHashes, first.FirmwareIdentity), Is.Not.EqualTo(key));
        Assert.That(first.Machine!.Memory.Peek(0x10001), Is.Zero);
    }
    [Test]
    public async Task PcDiskStaysPlayableButIsNotOfferedTo16BitFusionAnalysis()
    {
        var disk = Disk(); string path = Path.Combine(_folder, disk.Name); File.WriteAllBytes(path, disk.Data);
        Assert.That(GamePreparer.Choose([disk]).machine, Is.Null);
        Assert.That(GameCatalog.Scan(_folder), Is.Empty);
        using var library = new GameLibrary(); library.Rescan(_folder);
        for (int i = 0; i < 200 && library.Scanning; i++) await Task.Delay(10);
        library.Poll();
        Assert.That(library.Originals(machineId: "pcxt").Single().Path, Is.EqualTo(path));
    }

    [TestCase(Raylib_cs.KeyboardKey.F1, "F1")]
    [TestCase(Raylib_cs.KeyboardKey.F5, "F5")]
    [TestCase(Raylib_cs.KeyboardKey.F10, "F10")]
    public void PlainFunctionKeysReachThePc(Raylib_cs.KeyboardKey key, string name)
    {
        var keys = InputMapper.CollectPcKeys(k => k == key, true);
        Assert.That(keys, Is.EquivalentTo(new[] { name }));
        Assert.That(InputMapper.CollectPcKeys(k => k == key, false), Is.Empty);
    }
    [Test]
    public void HostShortcutsDoNotAlsoReachDosButDosControlKeysDo()
    {
        var pressed = new HashSet<Raylib_cs.KeyboardKey> { Raylib_cs.KeyboardKey.LeftControl, Raylib_cs.KeyboardKey.F5 };
        Assert.That(InputMapper.CollectPcKeys(pressed.Contains, true), Is.Empty);
        pressed.Remove(Raylib_cs.KeyboardKey.F5); pressed.Add(Raylib_cs.KeyboardKey.L);
        Assert.That(InputMapper.CollectPcKeys(pressed.Contains, true), Is.EquivalentTo(new[] { "CTRL", "L" }));
        pressed.Add(Raylib_cs.KeyboardKey.LeftShift);
        Assert.That(InputMapper.CollectPcKeys(pressed.Contains, true), Is.Empty);
    }
    [Test]
    public void CodePatchesAbove64KDoNotOverwriteLowMemoryAndAnalysisStopsBeforeCallingAi()
    {
        var host = Host();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        using var window = new CodeWindow(host, new Retro.Ai.AiSettings(), _ => { });
        window.Update();
        var write = typeof(CodeWindow).GetMethod("Write", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.That(write.Invoke(null, [host.Machine!, 0x10003u, new byte[] { 0x47 }]), Is.EqualTo(true));
        Assert.That(host.Machine!.Memory.Peek(0x10003), Is.EqualTo(0x47));
        Assert.That(host.Machine.Memory.Peek(3), Is.Zero);
        var prepare = typeof(CodeWindow).GetMethod("PrepareAnalysis", flags)!;
        var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => prepare.Invoke(window, ["IA simulada"]));
        Assert.That(failure!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NopPatchContinuesExecutionWithoutChangingStackOrAccumulator(bool pc)
    {
        IMachine machine;
        if (pc) machine = Host().Machine!;
        else
        {
            byte[] image = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(image); image[4] = 1;
            machine = new Retro.Systems.Nes.Nes(Retro.Systems.Nes.NesCartridge.Load(image));
        }
        var opcode = (byte)typeof(CodeWindow).GetMethod("NopOpcode", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [machine])!;
        machine.Memory.Poke(0x100, opcode); machine.Cpu.ProgramCounter = 0x100;
        string accumulator = pc ? "AX" : "A"; machine.Cpu.TrySetRegister(accumulator, 55);
        uint stack = machine.Cpu.StackPointer; machine.StepInstruction();
        Assert.That(machine.Cpu.ProgramCounter, Is.EqualTo(0x101u));
        Assert.That(machine.Cpu.StackPointer, Is.EqualTo(stack));
        Assert.That(machine.Cpu.GetRegisters().Single(r => r.Name == accumulator).Value, Is.EqualTo(55));
    }
}