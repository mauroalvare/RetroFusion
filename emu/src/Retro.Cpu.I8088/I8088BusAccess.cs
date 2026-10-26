namespace Retro.Cpu.I8088;

public enum I8088BusAccessKind { Code, MemoryRead, MemoryWrite, Input, Output }

/// <summary>One byte observed at the data phase. Aligned 8086 word halves share a clock.</summary>
public readonly record struct I8088BusAccess(long Clock, I8088BusAccessKind Kind, uint Address, byte Value);
