namespace Retro.Cpu.I8088;

/// <summary>A live INTR line; the vector is resolved only when the CPU accepts it.</summary>
public interface I8088InterruptSource
{
    bool InterruptRequested { get; }
    /// <summary>One functional acknowledge sequence, not timed INTA pin pulses.</summary>
    byte Acknowledge();
}
