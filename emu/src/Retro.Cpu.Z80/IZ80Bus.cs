namespace Retro.Cpu.Z80;

/// <summary>
/// Everything a Z80 sees outside itself. The CPU drives the T-state clock and asks
/// the bus how many wait states each cycle costs, so machines with contended memory
/// (ZX Spectrum, Amstrad CPC) get exact timing.
/// </summary>
public interface IZ80Bus
{
    byte ReadMemory(ushort address);
    void WriteMemory(ushort address, byte value);

    /// <summary>
    /// Extra T-states inserted when <paramref name="address"/> is on the address bus at
    /// <paramref name="tstate"/> (memory contention). 0 on machines without contention.
    /// </summary>
    int Contention(ushort address, long tstate);

    byte ReadPort(ushort port);
    void WritePort(ushort port, byte value);

    /// <summary>T-states for the part of an I/O cycle before the data transfer (normally 1).</summary>
    int PortTimingPre(ushort port, long tstate);

    /// <summary>T-states for the part of an I/O cycle after the data transfer (normally 3).</summary>
    int PortTimingPost(ushort port, long tstate);

    /// <summary>Byte on the data bus during interrupt acknowledge (IM 2 vector low byte).</summary>
    byte InterruptDataBus { get; }
}

/// <summary>Flat 64 KB RAM, no contention, standard I/O timing. Useful for tests and CP/M.</summary>
public class SimpleZ80Bus : IZ80Bus
{
    public readonly byte[] Memory = new byte[0x10000];

    public virtual byte ReadMemory(ushort address) => Memory[address];
    public virtual void WriteMemory(ushort address, byte value) => Memory[address] = value;
    public virtual int Contention(ushort address, long tstate) => 0;
    public virtual byte ReadPort(ushort port) => 0xFF;
    public virtual void WritePort(ushort port, byte value) { }
    public virtual int PortTimingPre(ushort port, long tstate) => 1;
    public virtual int PortTimingPost(ushort port, long tstate) => 3;
    public virtual byte InterruptDataBus => 0xFF;
}
