namespace Retro.Systems.C64;

/// <summary>
/// MOS 6526 CIA: two 16-bit timers, a time-of-day clock and two 8-bit ports. The C64 has
/// two: CIA 1 reads the keyboard and the joysticks and raises IRQ; CIA 2 selects the VIC-II
/// memory bank and raises NMI.
/// </summary>
public sealed class Cia
{
    public byte Pra, Prb, Ddra, Ddrb;
    private ushort _timerA = 0xFFFF, _timerB = 0xFFFF, _latchA = 0xFFFF, _latchB = 0xFFFF;
    private byte _cra, _crb, _icrMask, _icrFlags, _sdr;
    // time of day (BCD), alarm, latch while the hours are read, stop while the hours are written
    private byte _tod10, _todSec, _todMin, _todHr = 0x01;
    private byte _alarm10, _alarmSec, _alarmMin, _alarmHr;
    private byte[]? _todLatch;
    private bool _todStopped;

    /// <summary>What the outside world drives on port A / B (keyboard, joystick); 0xFF = nothing pulled low.</summary>
    public Func<byte> ExternalA = () => 0xFF, ExternalB = () => 0xFF;

    /// <summary>Interrupt output (IRQ for CIA 1, NMI for CIA 2).</summary>
    public bool Interrupt => (_icrFlags & _icrMask & 0x1F) != 0;

    /// <summary>Value on the port A pins as driven by this chip (outputs; inputs float high).</summary>
    public byte PortAPins => (byte)(Pra | ~Ddra);
    public byte PortBPins => (byte)(Prb | ~Ddrb);

    public void Reset()
    {
        Pra = Prb = Ddra = Ddrb = 0;
        _timerA = _timerB = _latchA = _latchB = 0xFFFF;
        _cra = _crb = _icrMask = _icrFlags = 0;
        _todStopped = false;
        _todLatch = null;
    }

    /// <summary>One clock cycle (phi2).</summary>
    public void Tick()
    {
        bool underA = false;
        if ((_cra & 0x01) != 0 && (_cra & 0x20) == 0)
        {
            if (_timerA == 0)
            {
                underA = true;
                _icrFlags |= 0x01;
                _timerA = _latchA;
                if ((_cra & 0x08) != 0)
                    _cra &= 0xFE;           // one-shot: stop
            }
            else
                _timerA--;
        }
        if ((_crb & 0x01) != 0)
        {
            int mode = (_crb >> 5) & 3;
            bool count = mode == 0 || (mode >= 2 && underA);
            if (count)
            {
                if (_timerB == 0)
                {
                    _icrFlags |= 0x02;
                    _timerB = _latchB;
                    if ((_crb & 0x08) != 0)
                        _crb &= 0xFE;
                }
                else
                    _timerB--;
            }
        }
    }

    /// <summary>Called ten times a second: advances the time-of-day clock.</summary>
    public void TodTick()
    {
        if (_todStopped)
            return;
        _tod10 = (byte)((_tod10 + 1) % 10);
        if (_tod10 == 0)
        {
            _todSec = BcdInc(_todSec, 0x60);
            if (_todSec == 0)
            {
                _todMin = BcdInc(_todMin, 0x60);
                if (_todMin == 0)
                {
                    byte pm = (byte)(_todHr & 0x80);
                    byte h = (byte)(_todHr & 0x1F);
                    h = h == 0x12 ? (byte)0x01 : BcdInc(h, 0x13);
                    if (h == 0x12) pm ^= 0x80;
                    _todHr = (byte)(h | pm);
                }
            }
        }
        if (_tod10 == _alarm10 && _todSec == _alarmSec && _todMin == _alarmMin && _todHr == _alarmHr)
            _icrFlags |= 0x04;
    }

    private static byte BcdInc(byte v, int limit)
    {
        int n = (v >> 4) * 10 + (v & 0xF) + 1;
        int max = (limit >> 4) * 10 + (limit & 0xF);
        if (n >= max) n = 0;
        return (byte)(((n / 10) << 4) | (n % 10));
    }

    public byte Read(int reg)
    {
        switch (reg & 0x0F)
        {
            case 0x0: return (byte)(PortAPins & ExternalA());
            case 0x1: return (byte)(PortBPins & ExternalB());
            case 0x2: return Ddra;
            case 0x3: return Ddrb;
            case 0x4: return (byte)_timerA;
            case 0x5: return (byte)(_timerA >> 8);
            case 0x6: return (byte)_timerB;
            case 0x7: return (byte)(_timerB >> 8);
            case 0x8:
            {
                byte v = _todLatch != null ? _todLatch[0] : _tod10;
                _todLatch = null;
                return v;
            }
            case 0x9: return _todLatch != null ? _todLatch[1] : _todSec;
            case 0xA: return _todLatch != null ? _todLatch[2] : _todMin;
            case 0xB:
                _todLatch = new[] { _tod10, _todSec, _todMin, _todHr };
                return _todHr;
            case 0xC: return _sdr;
            case 0xD:
            {
                byte v = (byte)(_icrFlags | (Interrupt ? 0x80 : 0));
                _icrFlags = 0;
                return v;
            }
            case 0xE: return (byte)(_cra & 0xEF);
            default: return (byte)(_crb & 0xEF);
        }
    }

    /// <summary>Register value without side effects (debugger).</summary>
    public byte Peek(int reg) => (reg & 0x0F) switch
    {
        0x8 => _tod10,
        0xB => _todHr,
        0xD => (byte)(_icrFlags | (Interrupt ? 0x80 : 0)),
        _ => Read(reg),
    };

    public void Write(int reg, byte v)
    {
        switch (reg & 0x0F)
        {
            case 0x0: Pra = v; break;
            case 0x1: Prb = v; break;
            case 0x2: Ddra = v; break;
            case 0x3: Ddrb = v; break;
            case 0x4: _latchA = (ushort)((_latchA & 0xFF00) | v); break;
            case 0x5:
                _latchA = (ushort)((_latchA & 0x00FF) | (v << 8));
                if ((_cra & 0x01) == 0) _timerA = _latchA;
                break;
            case 0x6: _latchB = (ushort)((_latchB & 0xFF00) | v); break;
            case 0x7:
                _latchB = (ushort)((_latchB & 0x00FF) | (v << 8));
                if ((_crb & 0x01) == 0) _timerB = _latchB;
                break;
            case 0x8:
                if ((_crb & 0x80) != 0) _alarm10 = (byte)(v & 0x0F);
                else { _tod10 = (byte)(v & 0x0F); _todStopped = false; }
                break;
            case 0x9:
                if ((_crb & 0x80) != 0) _alarmSec = (byte)(v & 0x7F); else _todSec = (byte)(v & 0x7F);
                break;
            case 0xA:
                if ((_crb & 0x80) != 0) _alarmMin = (byte)(v & 0x7F); else _todMin = (byte)(v & 0x7F);
                break;
            case 0xB:
                if ((_crb & 0x80) != 0) _alarmHr = (byte)(v & 0x9F);
                else { _todHr = (byte)(v & 0x9F); _todStopped = true; }
                break;
            case 0xC: _sdr = v; break;
            case 0xD:
                if ((v & 0x80) != 0) _icrMask |= (byte)(v & 0x1F);
                else _icrMask &= (byte)~(v & 0x1F);
                break;
            case 0xE:
                if ((v & 0x10) != 0) _timerA = _latchA;
                _cra = (byte)(v & 0xEF);
                break;
            default:
                if ((v & 0x10) != 0) _timerB = _latchB;
                _crb = (byte)(v & 0xEF);
                break;
        }
    }

    public void Save(BinaryWriter w)
    {
        w.Write(Pra); w.Write(Prb); w.Write(Ddra); w.Write(Ddrb);
        w.Write(_timerA); w.Write(_timerB); w.Write(_latchA); w.Write(_latchB);
        w.Write(_cra); w.Write(_crb); w.Write(_icrMask); w.Write(_icrFlags); w.Write(_sdr);
        w.Write(_tod10); w.Write(_todSec); w.Write(_todMin); w.Write(_todHr);
        w.Write(_alarm10); w.Write(_alarmSec); w.Write(_alarmMin); w.Write(_alarmHr);
        w.Write(_todStopped);
    }

    public void Load(BinaryReader r)
    {
        Pra = r.ReadByte(); Prb = r.ReadByte(); Ddra = r.ReadByte(); Ddrb = r.ReadByte();
        _timerA = r.ReadUInt16(); _timerB = r.ReadUInt16(); _latchA = r.ReadUInt16(); _latchB = r.ReadUInt16();
        _cra = r.ReadByte(); _crb = r.ReadByte(); _icrMask = r.ReadByte(); _icrFlags = r.ReadByte(); _sdr = r.ReadByte();
        _tod10 = r.ReadByte(); _todSec = r.ReadByte(); _todMin = r.ReadByte(); _todHr = r.ReadByte();
        _alarm10 = r.ReadByte(); _alarmSec = r.ReadByte(); _alarmMin = r.ReadByte(); _alarmHr = r.ReadByte();
        _todStopped = r.ReadBoolean();
        _todLatch = null;
    }

    public string Describe() =>
        $"timer A {_timerA:X4}/{_latchA:X4} {((_cra & 1) != 0 ? "en marcha" : "parado")}, timer B {_timerB:X4}/{_latchB:X4} {((_crb & 1) != 0 ? "en marcha" : "parado")}, " +
        $"interrupciones {_icrFlags:X2} máscara {_icrMask:X2}";
}
