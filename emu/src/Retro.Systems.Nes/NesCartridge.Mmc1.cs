namespace Retro.Systems.Nes;

public sealed partial class NesCartridge
{
    private byte _mmc1Shift = 0x10, _mmc1Control = 0x0C, _mmc1Chr0, _mmc1Chr1, _mmc1Prg;
    private long _mmc1LastWrite = -2;

    private void Mmc1Write(ushort address, byte value, long cycle)
    {
        bool consecutive = cycle >= 0 && cycle == _mmc1LastWrite + 1;
        _mmc1LastWrite = cycle >= 0 ? cycle : -2;
        // D7 reset is accepted even on the second write of a read-modify-write instruction.
        if ((value & 0x80) != 0)
        {
            _mmc1Shift = 0x10;
            _mmc1Control |= 0x0C;
            return;
        }
        if (consecutive) return;
        bool full = (_mmc1Shift & 1) != 0;
        _mmc1Shift = (byte)((_mmc1Shift >> 1) | ((value & 1) << 4));
        if (!full) return;
        // Only the fifth address selects the destination register.
        switch ((address >> 13) & 3)
        {
            case 0: _mmc1Control = _mmc1Shift; UpdateMmc1Mirroring(); break;
            case 1: _mmc1Chr0 = _mmc1Shift; break;
            case 2: _mmc1Chr1 = _mmc1Shift; break;
            case 3: _mmc1Prg = _mmc1Shift; break;
        }
        _mmc1Shift = 0x10;
    }

    private int Mmc1PrgOffset(ushort address)
    {
        // SEROM/SHROM connect CPU A14 directly, without PRG banking.
        if (_prg.Length == 0x8000 && !ChrIsRam) return address - 0x8000;
        int outer = ChrIsRam && _prg.Length > 0x40000 ? _mmc1Chr0 & 0x10 : 0;
        int selected = outer | (_mmc1Prg & 15);
        int bank = ((_mmc1Control >> 2) & 3) switch
        {
            0 or 1 => (selected & ~1) + (address >= 0xC000 ? 1 : 0),
            2 => address < 0xC000 ? outer : selected,
            _ => address < 0xC000 ? selected : outer | (Math.Min(16, _prg.Length / PrgUnit) - 1)
        };
        return ((bank * PrgUnit) + (address & 0x3FFF)) % _prg.Length;
    }

    private int Mmc1ChrOffset(ushort address)
    {
        int bank = (_mmc1Control & 0x10) == 0
            ? (_mmc1Chr0 & ~1) + ((address >> 12) & 1)
            : (address & 0x1000) == 0 ? _mmc1Chr0 : _mmc1Chr1;
        return (bank * 0x1000 + (address & 0x0FFF)) % _chr.Length;
    }

    private void UpdateMmc1Mirroring() => Mirroring = (_mmc1Control & 3) switch
    {
        0 => NesMirroring.SingleLower, 1 => NesMirroring.SingleUpper,
        2 => NesMirroring.Vertical, _ => NesMirroring.Horizontal
    };

    private void ResetMmc1()
    {
        _mmc1Shift = 0x10; _mmc1Control = 0x0C;
        _mmc1Chr0 = _mmc1Chr1 = _mmc1Prg = 0; _mmc1LastWrite = -2;
        UpdateMmc1Mirroring();
    }

    private void SaveMmc1(BinaryWriter w)
    {
        w.Write(_mmc1Shift); w.Write(_mmc1Control); w.Write(_mmc1Chr0);
        w.Write(_mmc1Chr1); w.Write(_mmc1Prg); w.Write(_mmc1LastWrite);
    }

    private void LoadMmc1(BinaryReader r)
    {
        byte shift = r.ReadByte(), control = r.ReadByte(), chr0 = r.ReadByte(), chr1 = r.ReadByte(), prg = r.ReadByte();
        long lastWrite = r.ReadInt64();
        if (shift is < 1 or > 31 || (control | chr0 | chr1 | prg) > 31 || lastWrite < -2)
            throw new InvalidDataException("Los registros MMC1 del estado no son válidos.");
        _mmc1Shift = shift; _mmc1Control = control; _mmc1Chr0 = chr0; _mmc1Chr1 = chr1;
        _mmc1Prg = prg; _mmc1LastWrite = lastWrite;
    }
}