using Retro.Systems.Pc;

namespace Pc.Tests;

public sealed class PcMachineTests
{
    private static byte[] Firmware()
    {
        byte[] rom = new byte[8192]; new byte[] { 0xEB, 0xFE }.CopyTo(rom, 0);
        new byte[] { 0xEA, 0, 0xE0, 0, 0xF0 }.CopyTo(rom, 0x1FF0); return rom;
    }
    private static byte[] Disk()
    {
        byte[] disk = new byte[737280]; for (int i = 0; i < disk.Length; i++) disk[i] = (byte)(i * 17 + i / 512); return disk;
    }
    private static void Word(PcBus b, ushort port, ushort value) { b.Out(12, 0); b.Out(port, (byte)value); b.Out(port, (byte)(value >> 8)); }

    [Test]
    public void MachineRunsClockedFramesAndRecordsPhysicalProgramAddresses()
    {
        var m = new PcXt(Firmware()) { ExecutedMap = new bool[1 << 20] };
        m.RunFrame(null); Assert.That(m.Cpu8088.Cycles, Is.GreaterThanOrEqualTo(PcXt.CyclesPerFrame));
        Assert.That(m.FrameCount, Is.EqualTo(1)); Assert.That(m.Board.CpuClocks, Is.EqualTo(m.Cpu.Cycles));
        Assert.That(m.ExecutedMap[0xFFFF0], Is.True); Assert.That(m.ExecutedMap[0xFE000], Is.True);
        Assert.That(m.Video.Width, Is.EqualTo(640)); Assert.That(m.Video.Height, Is.EqualTo(400));
        var debug = new Retro.Core.DebugController(); debug.Add(0xFE000);
        Assert.That(m.RunFrame(debug), Is.EqualTo(Retro.Core.RunStopReason.Breakpoint));
        m.ResumeFromBreakpoint(); m.StepInstruction(); Assert.That(m.Cpu.ProgramCounter, Is.EqualTo(0xFE000));
    }
    [Test]
    public void MidSectorStateRestoresDmaPrefetchByteDeadlineAndMemoryExactly()
    {
        var m = new PcXt(Firmware(), Disk()); var b = m.Board;
        b.Out(0x3F2, 0x1C); m.StepInstruction(); b.Out(0x3F5, 8); b.In(0x3F5); b.In(0x3F5);
        foreach (byte v in new byte[] { 3, 0xCF, 2 }) b.Out(0x3F5, v);
        Word(b, 4, 0x2000); Word(b, 5, 511); b.Out(0x81, 0); b.Out(11, 0x46); b.Out(10, 2);
        foreach (byte v in new byte[] { 0x46, 0, 0, 0, 9, 2, 8, 0x2A, 255 }) b.Out(0x3F5, v);
        for (int i = 0; i < 10000 && b.Fdc.BytesTransferred < 40; i++) m.StepInstruction();
        Assert.That(b.Fdc.BytesTransferred, Is.InRange(40, 511)); byte[] state = m.SaveState();
        m.RunFrame(null); m.RunFrame(null); byte[] expected = m.SaveState();
        Assert.That(m.Board.Fdc.BytesTransferred, Is.EqualTo(512));
        m.LoadState(state); m.RunFrame(null); m.RunFrame(null);
        Assert.That(m.SaveState(), Is.EqualTo(expected), "The new runtime must resume all device phases, not only RAM/registers.");
    }
    [Test]
    public void StateRestoresHalfReadPitLatchAndDoesNotInvokeOutputEvents()
    {
        var m = new PcXt(Firmware()); var b = m.Board;
        b.Out(0x43, 0x34); b.Out(0x40, 0x34); b.Out(0x40, 0x12); m.RunFrame(null);
        b.Out(0x43, 0); byte low = b.In(0x40); byte[] state = m.SaveState(); byte high = b.In(0x40);
        m.RunFrame(null); m.LoadState(state); Assert.That(m.Board.In(0x40), Is.EqualTo(high));
        Assert.That((high << 8) | low, Is.GreaterThan(0));
    }
    [Test]
    public void SpeakerAudioUsesTheRestoredRuntimeAndReplaysExactly()
    {
        var m = new PcXt(Firmware()); var b = m.Board;
        b.Out(0x63, 0x99); b.Out(0x43, 0xB6); b.Out(0x42, 0xE8); b.Out(0x42, 3); b.Out(0x61, 3);
        short[] sink = new short[8192]; m.RunFrame(null); m.DrainAudio(sink); byte[] state = m.SaveState();
        m.RunFrame(null); m.RunFrame(null); int count = m.DrainAudio(sink); short[] expected = sink[..count];
        Assert.That(count, Is.InRange(1460, 1490)); Assert.That(expected.Max(), Is.GreaterThan(1000)); Assert.That(expected.Min(), Is.LessThan(-1000));
        m.LoadState(state); m.RunFrame(null); m.RunFrame(null); Assert.That(m.DrainAudio(sink), Is.EqualTo(count));
        Assert.That(sink[..count], Is.EqualTo(expected));
    }
    [Test]
    public void DiskOverlaySurvivesResetAndRestoresWithoutChangingOriginalBytes()
    {
        byte[] disk = Disk(), original = (byte[])disk.Clone(); var m = new PcXt(Firmware(), disk);
        byte[] sector = Enumerable.Repeat((byte)17, 512).ToArray(); m.Board.Fdc.Disk!.WriteSector(0, 0, 1, sector); byte[] state = m.SaveState();
        Array.Fill(sector, (byte)99); m.Board.Fdc.Disk.WriteSector(0, 0, 1, sector); m.LoadState(state);
        m.Reset(false); m.Board.Fdc.Disk!.ReadSector(0, 0, 1, sector); Assert.That(sector, Is.All.EqualTo(17));
        m.Reset(true); m.Board.Fdc.Disk.ReadSector(0, 0, 1, sector); Assert.That(sector, Is.All.EqualTo(17)); Assert.That(disk, Is.EqualTo(original));
    }
    [Test]
    public void CorruptOrDifferentMediaStateCannotMutateTheRunningMachine()
    {
        var m = new PcXt(Firmware(), Disk()); m.Poke(0x40000, 17); byte[] state = m.SaveState();
        byte[] corrupt = (byte[])state.Clone(); corrupt[100] ^= 1;
        Assert.Throws<InvalidDataException>(() => m.LoadState(corrupt)); Assert.That(m.SaveState(), Is.EqualTo(state));
        Assert.Throws<InvalidDataException>(() => m.LoadState(state[..^1])); Assert.That(m.SaveState(), Is.EqualTo(state));
        byte[] other = Disk(); other[0]++; var otherMachine = new PcXt(Firmware(), other);
        Assert.Throws<InvalidDataException>(() => m.LoadState(otherMachine.SaveState())); Assert.That(m.SaveState(), Is.EqualTo(state));
    }
    [Test]
    public void PcKeysUseSetOneMakeBreakAndReleaseHeldKeysAfterStateLoad()
    {
        var m = new PcXt(Firmware()); m.Board.Out(0x63, 0x99); m.Board.Out(0x61, 0x40);
        m.SetKey("A", true); m.RunFrame(null); Assert.That(m.Board.Keyboard.Data, Is.EqualTo(0x1E)); byte[] state = m.SaveState();
        m.Board.Out(0x61, 0xC0); m.Board.Out(0x61, 0x40); m.SetKey("A", false); m.RunFrame(null);
        Assert.That(m.Board.Keyboard.Data, Is.EqualTo(0x9E)); m.LoadState(state);
        m.Board.Out(0x61, 0xC0); m.Board.Out(0x61, 0x40); m.RunFrame(null); Assert.That(m.Board.Keyboard.Data, Is.EqualTo(0x9E));
    }
}