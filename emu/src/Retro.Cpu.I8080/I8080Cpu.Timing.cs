namespace Retro.Cpu.I8080;

public sealed partial class I8080Cpu
{
    /// <summary>Clock memory/I/O transfers within an instruction. Defaults to legacy functional execution.</summary>
    public bool TimingEnabled { get; set; }
    public bool HasCycleTiming => TimingEnabled;
    private long _instructionStart;
    private int _nominalSpent;

    private void BeginTiming() { _instructionStart = Cycles; _nominalSpent = 0; }
    private void Tick(int count)
    {
        for (int n = 0; n < count; n++) { Cycles++; _nominalSpent++; _bus.Clock(); }
    }
    private void TransferClocks()
    {
        Tick(2);
        while (!_bus.Ready) { Cycles++; _bus.Clock(); }
        Tick(1);
    }
    private byte ReadMemory(ushort address)
    {
        if (TimingEnabled) TransferClocks();
        return _bus.ReadMemory(address);
    }
    private void WriteMemory(ushort address, byte value)
    {
        if (TimingEnabled) TransferClocks();
        _bus.WriteMemory(address, value);
    }
    private byte Input(byte port)
    {
        if (TimingEnabled) TransferClocks();
        return _bus.In(port);
    }
    private void Output(byte port, byte value)
    {
        if (TimingEnabled) TransferClocks();
        _bus.Out(port, value);
    }
    private static int FirstCycleClocks(byte opcode)
    {
        // The instruction's extended M1 precedes its operand/stack cycles.
        if (opcode < 0x40 && ((opcode & 7) == 3 || (opcode & 7) is 4 or 5 && ((opcode >> 3) & 7) != 6)) return 5;
        if (opcode is >= 0x40 and < 0x80 && (opcode & 7) != 6 && ((opcode >> 3) & 7) != 6) return 5;
        if ((opcode & 0xC7) is 0xC0 or 0xC4 or 0xC5 or 0xC7 || opcode is 0xE9 or 0xF9) return 5;
        return 4;
    }
    private int FinishTiming(int nominal)
    {
        if (!TimingEnabled) { Cycles += nominal; return nominal; }
        if (_nominalSpent > nominal) throw new InvalidOperationException("Los ciclos de bus exceden el tiempo nominal del 8080.");
        Tick(nominal - _nominalSpent);
        return checked((int)(Cycles - _instructionStart));
    }
}
