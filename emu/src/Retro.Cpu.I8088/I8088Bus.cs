namespace Retro.Cpu.I8088;

/// <summary>
/// Byte callbacks for the 8088/8086 bus. Both bytes of an aligned 8086 word transfer
/// occur at the same clock, without an intervening HOLD grant. Addresses wrap to 20 bits.
/// </summary>
public interface I8088Bus
{
    /// <summary>External TEST pin. High holds WAIT; inactive without a coprocessor.</summary>
    bool TestSignal => false;
    /// <summary>Logical bus ownership during LOCK or a memory XCHG, not a cycle-accurate signal.</summary>
    void SetLock(bool locked) { }
    /// <summary>One CPU T-state. Only the clocked execution mode calls this.</summary>
    void Clock() { }
    /// <summary>DMA bus request; waits between completed bus cycles, except while LOCK is active.</summary>
    bool HoldRequested => false;
    /// <summary>CPU acknowledges HOLD only between completed bus cycles.</summary>
    void SetBusGranted(bool granted) { }
    byte ReadMemory(uint address);
    void WriteMemory(uint address, byte value);
    byte In(ushort port);
    void Out(ushort port, byte value);
}
