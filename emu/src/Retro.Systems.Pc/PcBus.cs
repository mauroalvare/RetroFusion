using Retro.Cpu.I8088;

namespace Retro.Systems.Pc;

/// <summary>PC/XT board bus. Unpopulated I/O slots read open bus; device functions reject unsupported modes.</summary>
public sealed class PcBus : I8088Bus
{
    private readonly bool _pc5150;
    private bool _busGranted;
    private readonly byte[] _dmaPages = new byte[4];
    public PcMemory Memory { get; }
    public Pic8259A Pic { get; } = new();
    public Pit8253 Pit { get; } = new();
    public Cga6845 Cga { get; }
    public Ppi8255 Ppi { get; } = new();
    public PcKeyboard Keyboard { get; } = new();
    public Fdc765 Fdc { get; } = new();
    public Dma8237 Dma { get; } = new();
    public bool HoldRequested => Dma.HoldRequested;
    public void SetBusGranted(bool granted) => _busGranted = granted;
    public bool NmiEnabled { get; private set; }
    public bool BusLocked { get; private set; }
    public bool TestSignal { get; set; }
    public long CpuClocks { get; private set; }
    public ReadOnlySpan<byte> DmaPageRegisters => _dmaPages;

    public PcBus(PcMemory memory, bool pc5150 = false)
    {
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _pc5150 = pc5150;
        Cga = new Cga6845(Memory);
        Ppi.InputA = () => _pc5150 && (Ppi.PortB & 0x80) != 0 ? EquipmentSwitches : Keyboard.Data;
        Ppi.InputC = ReadPpiC;
        Ppi.OutputBChanged += value => { Pit.SetGate(2, (value & 1) != 0); Keyboard.SetControl(value); };
        Keyboard.IrqChanged += high => Pic.SetLine(1, high);
        Dma.ReadMemory = (channel, address) => Memory.Read(DmaAddress(channel, address));
        Dma.WriteMemory = (channel, address, value) => Memory.Write(DmaAddress(channel, address), value);
        Dma.ReadDevice = channel => channel == 2 ? Fdc.DmaRead() : throw new NotSupportedException($"PC: falta dispositivo DMA{channel}.");
        Dma.WriteDevice = (channel, value) => { if (channel == 2) Fdc.DmaWrite(value); else if (channel != 0) throw new NotSupportedException($"PC: falta dispositivo DMA{channel}."); };
        Dma.TerminalCount += channel => { if (channel == 2) Fdc.TerminalCount(); };
        Fdc.IrqChanged += high => Pic.SetLine(6, high);
        Fdc.DmaRequestChanged += high => Dma.SetRequest(2, high);
        Dma.Acknowledge += channel => { if (channel == 0) Dma.SetRequest(0, false); };
        Pit.OutputChanged += (channel, high) => { if (channel == 0) Pic.SetLine(0, high); else if (channel == 1 && high) Dma.SetRequest(0, true); };
        Reset(clearMemory: false);
    }

    public void Reset(bool clearMemory)
    {
        CpuClocks = 0; Pit.Reset(); Pic.Reset(); Cga.Reset(); Keyboard.Reset(); Ppi.Reset(); Dma.Reset(); Fdc.Reset(); _busGranted = false;
        Pit.SetGate(0, true); Pit.SetGate(1, true);
        Array.Clear(_dmaPages); NmiEnabled = BusLocked = TestSignal = false;
        if (clearMemory) Memory.ClearRam();
    }

    /// <summary>4.772727 MHz CPU clock: the 8253 receives one pulse for every four T-states.</summary>
    public void Clock()
    {
        if ((++CpuClocks & 3) == 0) TickPit();
        Cga.ClockMaster(); Cga.ClockMaster(); Cga.ClockMaster(); Keyboard.Clock();
        Fdc.Clock(); Dma.Clock(_busGranted && !BusLocked);
    }

    // One floppy drive, no 8087, CGA 80-column display. Switches describe installed RAM.
    private byte EquipmentSwitches => (byte)(0x21 | (_pc5150 ? Math.Min(3, Memory.RamKilobytes / 16 - 1) : Math.Clamp(Memory.RamKilobytes / 64 - 1, 0, 3)) << 2);
    private byte ReadPpiC()
    {
        int switches;
        if (_pc5150)
        {
            int expansion = Math.Max(0, Memory.RamKilobytes - 64) / 32;
            switches = (Ppi.PortB & 4) != 0 ? expansion & 15 : (expansion >> 4) & 1;
        }
        else switches = (Ppi.PortB & 8) == 0 ? EquipmentSwitches & 15 : EquipmentSwitches >> 4;
        return (byte)(switches | (Pit.GetOutput(2) ? 0x20 : 0));
    }

    private uint DmaAddress(int channel, ushort address) =>
        (uint)(((_dmaPages[channel switch { 0 or 1 => 3, 2 => 1, _ => 2 }] & 15) << 16) | address);

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
        >= 0x60 and <= 0x63 => Ppi.Read(port - 0x60),
        <= 0x0F => Dma.Read(port),
        0x3F4 => Fdc.MainStatus, 0x3F5 => Fdc.ReadData(),
        0x3F0 or 0x3F1 or 0x3F2 or 0x3F3 or 0x3F6 or 0x3F7 => 255, // XT controller: no AT extensions.
        0x201 or >= 0x2F8 and <= 0x2FF or >= 0x3F8 and <= 0x3FF or >= 0x278 and <= 0x27F or >= 0x378 and <= 0x37F or >= 0x320 and <= 0x323 => 255, // Optional adapters absent.
        0x213 => 255, // No expansion chassis is installed.
        0x20 => Pic.ReadCommand(), 0x21 => Pic.ReadData(),
        >= 0x40 and <= 0x42 => Pit.ReadCounter(port - 0x40),
        >= 0x80 and <= 0x8F or >= 0xA0 and <= 0xAF => 0xFF, // write-only board latches
        _ => 0xFF
    };

    public void Out(ushort port, byte value)
    {
        switch (port)
        {
            case >= 0x3D0 and <= 0x3DF: Cga.Write(port, value); break;
            case >= 0x3B0 and <= 0x3BF: break;
            case >= 0x60 and <= 0x63: Ppi.Write(port - 0x60, value); break;
            case <= 0x0F: Dma.Write(port, value); break;
            case 0x3F2: Fdc.WriteControl(value); break;
            case 0x3F5: Fdc.WriteData(value); break;
            case 0x3F7: break; // No programmable data rate on the XT adapter.
            case 0x201 or >= 0x2F8 and <= 0x2FF or >= 0x3F8 and <= 0x3FF or >= 0x278 and <= 0x27F or >= 0x378 and <= 0x37F or >= 0x320 and <= 0x323: break;
            case 0x213: break;
            case 0x20: Pic.WriteCommand(value); break;
            case 0x21: Pic.WriteData(value); break;
            case >= 0x40 and <= 0x42: Pit.WriteCounter(port - 0x40, value); break;
            case 0x43: Pit.WriteControl(value); break;
            case >= 0x80 and <= 0x8F: _dmaPages[port & 3] = value; break;
            case >= 0xA0 and <= 0xAF: NmiEnabled = (value & 0x80) != 0; break;
            default: break;
        }
    }

}