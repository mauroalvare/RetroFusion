using Retro.App;
using Retro.Core;
using Retro.Cpu.Z80;
using Retro.Cpu.I8080;
using Retro.Cpu.I8088;
using Retro.Cpu.M6502;
using Retro.Machines;

namespace App.Tests;

public sealed class CpuQualityTests
{
    private sealed class Bus : SimpleZ80Bus, I8080Bus, IM6502Bus, I8088Bus
    {
        public int Accesses;
        public override byte ReadMemory(ushort address) { Accesses++; return base.ReadMemory(address); }
        public override void WriteMemory(ushort address, byte value) { Accesses++; base.WriteMemory(address, value); }
        public byte In(byte port) => 255; public void Out(byte port, byte value) { }
        public byte Read(ushort address) => ReadMemory(address);
        public void Write(ushort address, byte value) => WriteMemory(address, value);
        public bool IrqLine => false; public bool NmiLine => false;
        public byte ReadMemory(uint address) => ReadMemory((ushort)address);
        public void WriteMemory(uint address, byte value) => WriteMemory((ushort)address, value);
        public byte In(ushort port) => 255; public void Out(ushort port, byte value) { }
    }
    private static ICpuDebugView Cpu(string model, Bus bus) => model switch {
        "Z80" => new Z80Cpu(bus), "8080" => new I8080Cpu(bus),
        "8088" => new I8088Cpu(bus), "8086" => new I8088Cpu(bus, I8088Model.Intel8086),
        _ => new M6502(bus) { Name = model, DecimalEnabled = model != "2A03" }
    };
    [TestCase("Z80")] [TestCase("8080")] [TestCase("6502")] [TestCase("6510")]
    [TestCase("2A03")] [TestCase("8088")] [TestCase("8086")]
    public void EveryExposedRegisterRejectsOverflowWithoutMutationOrBusAccess(string model)
    {
        var bus = new Bus(); var cpu = Cpu(model, bus);
        foreach (var register in cpu.GetRegisters())
        {
            uint maximum = (1u << register.Bits) - 1;
            Assert.That(cpu.TrySetRegister(register.Name, maximum), Is.True, register.Name);
            var before = cpu.GetRegisters().ToArray();
            Assert.That(cpu.TrySetRegister(register.Name, maximum + 1), Is.False, register.Name);
            Assert.That(cpu.GetRegisters(), Is.EqualTo(before), register.Name);
        }
        Assert.That(cpu.TrySetRegister("unknown", 1), Is.False);
        Assert.That(cpu.TrySetRegister("pc", 0x1234), Is.True);
        Assert.That(cpu.ProgramCounter, Is.EqualTo(0x1234));
        uint bound = model is "8088" or "8086" ? 0xFFFFFu : 0xFFFFu;
        cpu.ProgramCounter = bound;
        Assert.Throws<ArgumentOutOfRangeException>(() => cpu.ProgramCounter = bound + 1);
        Assert.That(cpu.ProgramCounter, Is.EqualTo(bound));
        Assert.That(bus.Accesses, Is.Zero);
    }
    [Test]
    public void WiderAudioMarginActuallyBuildsMoreReserveDuringEmulation()
    {
        int Reserve(int frames)
        {
            var rom = new byte[16 + 16384 + 8192]; new byte[] { 78, 69, 83, 26, 1, 1 }.CopyTo(rom, 0);
            new byte[] { 0x4C, 0, 0x80 }.CopyTo(rom, 16); rom[16 + 0x3FFD] = 0x80;
            var host = new EmulatorHost { AudioBufferFrames = frames };
            host.StartNes(MachineCatalog.Get("nes"), new MediaFile("own-loop.nes", rom, "own-loop.nes"));
            var sink = new short[735];
            for (int i = 0; i < 180; i++) { host.Update(1d / 60); host.Audio.Pop(sink); }
            return host.Audio.Count;
        }
        Assert.That(Reserve(4), Is.GreaterThan(Reserve(2) + 500));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmulatorHost { AudioBufferFrames = 0 });
    }

    [Test]
    public void NesStepOverRunsTheSubroutineAndStopsAtItsReturnAddress()
    {
        var rom = new byte[16 + 16384 + 8192]; new byte[] { 78, 69, 83, 26, 1, 1 }.CopyTo(rom, 0);
        new byte[] { 0x20, 8, 0x80, 0x4C, 3, 0x80, 0xEA, 0xEA, 0xE6, 0, 0x60 }.CopyTo(rom, 16);
        rom[16 + 0x3FFC] = 0; rom[16 + 0x3FFD] = 0x80;
        var host = new EmulatorHost(); host.StartNes(MachineCatalog.Get("nes"), new MediaFile("own.nes", rom, "own.nes"));
        host.Paused = true; Assert.That(host.Machine!.Cpu.CpuName, Does.Contain("2A03"));
        host.StepOver(); Assert.That(host.Paused, Is.False);
        host.Update(0.05);
        Assert.That(host.Paused, Is.True);
        Assert.That(host.Machine.Cpu.ProgramCounter, Is.EqualTo(0x8003));
        Assert.That(host.Machine.Memory.Peek(0), Is.EqualTo(1));
    }
}
