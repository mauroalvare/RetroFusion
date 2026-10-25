namespace Retro.Systems.Nes;

public sealed partial class NesCartridge
{
    private readonly byte[] _mmc3Banks = new byte[8];
    private byte _mmc3Select, _mmc3Ram, _mmc3Latch, _mmc3Counter;
    private bool _mmc3Reload, _mmc3Enabled, _mmc3Irq, _mmc3A12;
    private long _mmc3Dots, _mmc3LowSince = -10;

    private int Mmc3PrgOffset(ushort address)
    {
        int slot = (address >> 13) & 3, count = _prg.Length / 0x2000;
        if ((_mmc3Select & 0x40) != 0 && slot is 0 or 2) slot ^= 2;
        int bank = slot switch { 0 => _mmc3Banks[6] & 63, 1 => _mmc3Banks[7] & 63, 2 => count - 2, _ => count - 1 };
        return bank % count * 0x2000 + (address & 0x1FFF);
    }

    private int Mmc3ChrOffset(ushort address)
    {
        int slot = (address >> 10) & 7;
        if ((_mmc3Select & 0x80) != 0) slot ^= 4;
        int bank = slot < 4 ? (_mmc3Banks[slot >> 1] & 0xFE) | (slot & 1) : _mmc3Banks[slot - 2];
        return bank % (_chr.Length / 0x400) * 0x400 + (address & 0x3FF);
    }

    private void Mmc3Write(ushort address, byte value)
    {
        switch (address & 0xE001)
        {
            case 0x8000: _mmc3Select = value; break;
            case 0x8001: _mmc3Banks[_mmc3Select & 7] = value; break;
            case 0xA000:
                if (_initialMirroring != NesMirroring.FourScreen)
                    Mirroring = (value & 1) == 0 ? NesMirroring.Vertical : NesMirroring.Horizontal;
                break;
            case 0xA001: _mmc3Ram = value; break;
            case 0xC000: _mmc3Latch = value; break;
            case 0xC001: _mmc3Counter = 0; _mmc3Reload = true; break;
            case 0xE000: _mmc3Enabled = _mmc3Irq = false; break;
            case 0xE001: _mmc3Enabled = true; break;
        }
    }

    /// <summary>One PPU dot, including blanking. Debugger/pixel reads must not call this.</summary>
    public void ClockPpuDot() { if (Mapper == 4) _mmc3Dots++; }

    /// <summary>Physical PPU address changes, independent of scanline renderer reads.</summary>
    public void ObservePpuAddress(ushort address)
    {
        if (Mapper != 4) return;
        bool high = (address & 0x1000) != 0;
        if (high == _mmc3A12) return;
        _mmc3A12 = high;
        if (!high) { _mmc3LowSince = _mmc3Dots; return; }
        // Conservative dot-domain approximation of three M2 falling edges.
        // Nine-dot inter-tile gaps must not create a second scanline clock.
        if (_mmc3Dots - _mmc3LowSince < 10) return;
        // MMC3B/C: zero reload can assert IRQ, and acknowledge does not stop the counter.
        _mmc3Counter = _mmc3Counter == 0 || _mmc3Reload ? _mmc3Latch : (byte)(_mmc3Counter - 1);
        _mmc3Reload = false;
        if (_mmc3Counter == 0 && _mmc3Enabled) _mmc3Irq = true;
    }

    private void ResetMmc3()
    {
        new byte[] { 0, 2, 4, 5, 6, 7, 0, 1 }.CopyTo(_mmc3Banks, 0);
        _mmc3Select = _mmc3Ram = _mmc3Latch = _mmc3Counter = 0;
        _mmc3Reload = _mmc3Enabled = _mmc3Irq = _mmc3A12 = false;
        _mmc3Dots = 0; _mmc3LowSince = -10;
    }

    private void SaveMmc3(BinaryWriter w)
    {
        w.Write(_mmc3Banks); w.Write(_mmc3Select); w.Write(_mmc3Ram); w.Write((int)Mirroring);
        w.Write(_mmc3Latch); w.Write(_mmc3Counter); w.Write(_mmc3Reload); w.Write(_mmc3Enabled); w.Write(_mmc3Irq);
        w.Write(_mmc3A12); w.Write(_mmc3Dots); w.Write(_mmc3LowSince);
    }

    private void LoadMmc3(BinaryReader r)
    {
        byte[] banks = r.ReadBytes(8);
        if (banks.Length != 8) throw new EndOfStreamException();
        byte select = r.ReadByte(), ram = r.ReadByte(); var mirroring = (NesMirroring)r.ReadInt32();
        byte latch = r.ReadByte(), counter = r.ReadByte();
        bool reload = r.ReadBoolean(), enabled = r.ReadBoolean(), irq = r.ReadBoolean(), a12 = r.ReadBoolean();
        long dots = r.ReadInt64(), low = r.ReadInt64();
        if (dots < 0 || low < -10 || low > dots || irq && !enabled ||
            (_initialMirroring == NesMirroring.FourScreen ? mirroring != NesMirroring.FourScreen : mirroring is not (NesMirroring.Vertical or NesMirroring.Horizontal)))
            throw new InvalidDataException("Los registros MMC3 del estado no son vÃ¡lidos.");
        banks.CopyTo(_mmc3Banks, 0); _mmc3Select = select; _mmc3Ram = ram; Mirroring = mirroring;
        _mmc3Latch = latch; _mmc3Counter = counter; _mmc3Reload = reload; _mmc3Enabled = enabled; _mmc3Irq = irq;
        _mmc3A12 = a12; _mmc3Dots = dots; _mmc3LowSince = low;
    }
}