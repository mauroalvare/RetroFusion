using Retro.Cpu.I8088;
using Retro.Systems.Pc;

namespace Pc.Tests;

public sealed class PcBusTests
{
    private static PcBus Board() => new(new PcMemory(new byte[8192]));

    [Test]
    public void NmiEnableUsesBitSevenAndThePcWriteOnlyAddressAliases()
    {
        var board = Board(); Assert.That(board.NmiEnabled, Is.False);
        board.Out(0xA0, 0x80); Assert.That(board.NmiEnabled, Is.True);
        board.Out(0xAF, 0x7F); Assert.That(board.NmiEnabled, Is.False);
        Assert.That(board.In(0xA0), Is.EqualTo(255));
    }

    [Test]
    public void DmaPagesAreSeparateLatchesWithoutPretendingToImplementTransfers()
    {
        var board = Board(); board.Out(0x81, 3); board.Out(0x83, 7);
        Assert.That(board.DmaPageRegisters.ToArray(), Is.EqualTo(new byte[] { 0, 3, 0, 7 }));
        Assert.Throws<NotSupportedException>(() => board.Out(0x0D, 0));
        Assert.Throws<NotSupportedException>(() => board.In(0x60));
        board.Reset(false); Assert.That(board.DmaPageRegisters.ToArray(), Is.All.Zero);
    }

    [Test]
    public void TimerIrqUsesExternalPulsesWhileCpuInstructionsLeaveCounterUnchanged()
    {
        var board = Board(); board.Out(0x43, 0x34); board.Out(0x40, 4); board.Out(0x40, 0);
        board.Out(0x20, 0x13); board.Out(0x21, 8); board.Out(0x21, 1); board.Out(0x21, 0xFE);
        board.TickPit();
        var cpu = new I8088Cpu(board, board.Pic) { CS = 0, IP = 0 };
        board.WriteMemory(0, 0x90); board.WriteMemory(1, 0x90); cpu.Step(); cpu.Step();
        board.Out(0x43, 0); int count = board.In(0x40) | board.In(0x40) << 8;
        Assert.That(count, Is.EqualTo(4)); Assert.That(board.Pic.InterruptRequested, Is.False);
        for (int n = 0; n < 4; n++) board.TickPit();
        Assert.That(board.Pic.InterruptRequested, Is.True);
        Assert.That(board.Pic.Acknowledge(), Is.EqualTo(8));
        Assert.That(cpu.Instructions, Is.EqualTo(2));
    }

    [Test]
    public void ResetReleasesLockAndResetsDevicesWithoutChangingRom()
    {
        var board = Board(); board.WriteMemory(37, 99); board.SetLock(true); board.Out(0xA0, 0x80);
        board.Reset(false); Assert.That(board.ReadMemory(37), Is.EqualTo(99));
        Assert.That(board.BusLocked, Is.False); Assert.That(board.NmiEnabled, Is.False);
        Assert.That(board.Pic.Mask, Is.EqualTo(255));
        board.Reset(true); Assert.That(board.ReadMemory(37), Is.Zero); Assert.That(board.ReadMemory(0xFFFF0), Is.Zero);
    }
}