using Retro.Cpu.I8088;

namespace Retro.Systems.Pc;

/// <summary>Partial PC/XT board bus. Missing devices fail explicitly; clocks are supplied externally.</summary>
public sealed class PcBus : I8088Bus
{
    private readonly byte[] _dmaPages = new byte[4];
    public PcMemory Memory { get; }
    public Pic8259A Pic { get; } = new();
    public Pit8253 Pit { get; } = new();
    public Cga6845 Cga { get; }
    public bool NmiEnabled { get; private set; }
    public bool BusLocked { get; private set; }
    public bool TestSignal { get; set; }
    public long CpuClocks { get; private set; }
    public ReadOnlySpan<byte> DmaPageRegisters => _dmaPages;

    public PcBus(PcMemory memory)
    {
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        Cga = new Cga6845(Memory);
        Pit.OutputChanged += (channel, high) => { if (channel == 0) Pic.SetLine(0, high); };
        Reset(clearMemory: false);
    }

    public void Reset(bool clearMemory)
    {
        CpuClocks = 0; Pit.Reset(); Pic.Reset(); Cga.Reset();
        Pit.SetGate(0, true); Pit.SetGate(1, true);
        Array.Clear(_dmaPages); NmiEnabled = BusLocked = TestSignal = false;
        if (clearMemory) Memory.ClearRam();
    }

    /// <summary>4.772727 MHz CPU clock: the 8253 receives one pulse for every four T-states.</summary>
    public void Clock()
    {
        if ((++CpuClocks & 3) == 0) TickPit();
        Cga.ClockMaster(); Cga.ClockMaster(); Cga.ClockMaster();
    }

    public byte ReadMemory(uint address) => Memory.Read(address);
    public void WriteMemory(uint address, byte value) => Memory.Write(address, value);
    public void SetLock(bool locked) => BusLocked = locked;

    /// <summary>One complete external clock pulse on each PIT input. CPU steps never call this implicitly.</summary>
    public void TickPit()
    {
        for (int channel = 0; channel < 3; channel++) Pit.Tick(channel);
    }

    public byte In(ushort port) => port switch
    {
        >= 0x3D0 and <= 0x3DF => Cga.Read(port),
        >= 0x3B0 and <= 0x3BF => 0xFF, // No MDA is installed alongside the CGA.
        0x20 => Pic.ReadCommand(), 0x21 => Pic.ReadData(),
        >= 0x40 and <= 0x42 => Pit.ReadCounter(port - 0x40),
        >= 0x80 and <= 0x83 or >= 0xA0 and <= 0xAF => 0xFF, // write-only board latches
        _ => throw Missing(port)
    };

    public void Out(ushort port, byte value)
    {
        switch (port)
        {
            case >= 0x3D0 and <= 0x3DF: Cga.Write(port, value); break;
            case >= 0x3B0 and <= 0x3BF: break;
            case 0x20: Pic.WriteCommand(value); break;
            case 0x21: Pic.WriteData(value); break;
            case >= 0x40 and <= 0x42: Pit.WriteCounter(port - 0x40, value); break;
            case 0x43: Pit.WriteControl(value); break;
            case >= 0x80 and <= 0x83: _dmaPages[port - 0x80] = value; break;
            case >= 0xA0 and <= 0xAF: NmiEnabled = (value & 0x80) != 0; break;
            default: throw Missing(port);
        }
    }

    private static NotSupportedException Missing(ushort port) =>
        new($"PC/XT: falta implementar el dispositivo del puerto {port:X4}.");
}