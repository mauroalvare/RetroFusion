namespace Retro.Cpu.I8088;

/// <summary>8088 byte bus. Physical addresses are already wrapped to 20 bits by the CPU.</summary>
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
    byte ReadMemory(uint address);
    void WriteMemory(uint address, byte value);
    byte In(ushort port);
    void Out(ushort port, byte value);
}
