using Retro.Systems.C64;

namespace Commodore.Tests;

public sealed class LegacyStateTests
{
    [Test]
    public void VersionOneKeepsCpuRamAndPeripheralPayloadsAligned()
    {
        var firmware = new C64Firmware(new byte[8192], new byte[8192], new byte[4096], "own fixture");
        var machine = new C64(firmware);
        machine.Ram[0x2000] = 0xEA; machine.Cpu6510.PC = 0x2000;
        machine.Ram[17] = 99; machine.Write(0xD800, 7); machine.Cia1.Write(2, 0xA5);
        byte[] modern = machine.SaveState();
        using var reader = new BinaryReader(new MemoryStream(modern));
        reader.ReadString(); int versionAt = (int)reader.BaseStream.Position;
        Assert.That(reader.ReadInt32(), Is.EqualTo(2));
        int nmiPollAt = (int)reader.BaseStream.Position + 29;
        byte[] legacy = [..modern[..nmiPollAt], ..modern[(nmiPollAt + 1)..]];
        BitConverter.GetBytes(1).CopyTo(legacy, versionAt);
        var restored = new C64(firmware); restored.LoadState(legacy);
        Assert.That(restored.SaveState(), Is.EqualTo(modern));
        machine.StepInstruction(); restored.StepInstruction();
        Assert.That(restored.SaveState(), Is.EqualTo(machine.SaveState()));
    }
}
