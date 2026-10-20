namespace Retro.Cpu.I8088;

/// <summary>8088 byte bus. Physical addresses are already wrapped to 20 bits by the CPU.</summary>
public interface I8088Bus
{
    byte ReadMemory(uint address);
    void WriteMemory(uint address, byte value);
    byte In(ushort port);
    void Out(ushort port, byte value);
}
