using Retro.Cpu.I8088;

namespace Retro.Systems.Pc;

/// <summary>
/// One functional 8259A in 8086 mode, single controller, edge-triggered inputs.
/// Acknowledge combines the two INTA pulses; no pin timing, cascade or level mode.
/// </summary>
public sealed class Pic8259A : I8088InterruptSource
{
    private byte _inputs, _requests, _service, _mask = 0xFF;
    private int _initStep, _lowest = 7, _polledLine;
    private bool _autoEoi, _rotateAuto, _specialMask, _readService, _poll;

    public bool BufferedMode { get; private set; }
    public bool BufferedMaster { get; private set; }
    public bool Initialized { get; private set; }
    public byte VectorBase { get; private set; }
    public byte RequestRegister => _requests;
    public byte InServiceRegister => _service;
    public byte Mask => _mask;
    public bool InterruptRequested => Initialized && (_poll ? _polledLine >= 0 : PendingLine() >= 0);

    /// <summary>Deterministic board reset; software must issue ICW1, ICW2 and ICW4.</summary>
    public void Reset()
    {
        _inputs = _requests = _service = 0; _mask = 0xFF;
        _initStep = 0; _lowest = 7; _polledLine = -1;
        _autoEoi = _rotateAuto = _specialMask = _readService = _poll = Initialized = false;
        BufferedMode = BufferedMaster = false; VectorBase = 0;
    }

    /// <summary>Keep an input asserted through acknowledge; electrical short-pulse timing is not modeled.</summary>
    public void SetLine(int line, bool asserted)
    {
        if ((uint)line > 7) throw new ArgumentOutOfRangeException(nameof(line));
        byte bit = (byte)(1 << line);
        if (asserted)
        {
            if ((_inputs & bit) == 0) _requests |= bit;
            _inputs |= bit;
        }
        else _inputs &= (byte)~bit;
    }

    public byte ReadCommand() => Read(data: false);
    public byte ReadData() => Read(data: true);

    private byte Read(bool data)
    {
        if (_poll)
        {
            _poll = false;
            if (_polledLine < 0) return 0;
            int line = _polledLine; Accept(line);
            return (byte)(0x80 | line);
        }
        return data ? _mask : _readService ? _service : _requests;
    }

    public void WriteCommand(byte value)
    {
        if ((value & 0x10) != 0)
        {
            // Reject unsupported configurations before altering the active controller.
            if ((value & 0x0B) != 3)
                throw new NotSupportedException("8259A: se admite un solo controlador, por flancos y con ICW4.");
            _requests = _service = _mask = 0; _lowest = 7;
            _autoEoi = _rotateAuto = _specialMask = _readService = _poll = Initialized = false;
            BufferedMode = BufferedMaster = false; VectorBase = 0; _initStep = 2;
            // Preserve the physical input levels: a held-high edge does not retrigger after ICW1.
            return;
        }
        RequireInitialized();
        if ((value & 8) != 0)
        {
            if ((value & 4) != 0 && _autoEoi)
                throw new NotSupportedException("8259A: poll con AEOI pendiente de contraste; no se modificó el estado.");
            if ((value & 0x40) != 0) _specialMask = (value & 0x20) != 0;
            if ((value & 2) != 0) _readService = (value & 1) != 0;
            if ((value & 4) != 0) { _polledLine = PendingLine(); _poll = true; }
            return;
        }
        int operation = value >> 5, line = value & 7;
        switch (operation)
        {
            case 0: _rotateAuto = false; break;
            case 1: EndOfInterrupt(specific: false, line, rotate: false); break;
            case 2: break;
            case 3: EndOfInterrupt(specific: true, line, rotate: false); break;
            case 4: _rotateAuto = true; break;
            case 5: EndOfInterrupt(specific: false, line, rotate: true); break;
            case 6: _lowest = line; break;
            case 7: EndOfInterrupt(specific: true, line, rotate: true); break;
        }
    }

    public void WriteData(byte value)
    {
        if (_initStep == 2) { VectorBase = (byte)(value & 0xF8); _initStep = 4; return; }
        if (_initStep == 4)
        {
            if ((value & 1) == 0 || (value & 0xF0) != 0)
                throw new NotSupportedException("8259A: se admite modo 8086 sin anidación de cascada.");
            BufferedMode = (value & 8) != 0; BufferedMaster = BufferedMode && (value & 4) != 0;
            _autoEoi = (value & 2) != 0;
            _initStep = 0; Initialized = true; return;
        }
        RequireInitialized();
        _mask = value;
    }

    public byte Acknowledge()
    {
        RequireInitialized();
        int line = PendingLine();
        if (line < 0) return (byte)(VectorBase | 7); // Default IRQ7 never sets ISR7.
        Accept(line);
        return (byte)(VectorBase | line);
    }

    private void Accept(int line)
    {
        byte bit = (byte)(1 << line);
        _requests &= (byte)~bit;
        if (!_autoEoi) _service |= bit;
        else if (_rotateAuto) _lowest = line;
    }

    private int PendingLine()
    {
        int service = First(_specialMask ? (byte)(_service & ~_mask) : _service);
        for (int rank = 0; rank < 8; rank++)
        {
            int line = (_lowest + 1 + rank) & 7;
            if (line == service) break;
            if ((_requests & ~_mask & (1 << line)) != 0) return line;
        }
        return -1;
    }

    private int First(byte bits)
    {
        for (int rank = 0; rank < 8; rank++)
        {
            int line = (_lowest + 1 + rank) & 7;
            if ((bits & (1 << line)) != 0) return line;
        }
        return -1;
    }

    private void EndOfInterrupt(bool specific, int line, bool rotate)
    {
        if (!specific) line = First(_specialMask ? (byte)(_service & ~_mask) : _service);
        if (line < 0) return;
        _service &= (byte)~(1 << line);
        if (rotate) _lowest = line;
    }

    private void RequireInitialized()
    {
        if (!Initialized) throw new InvalidOperationException("8259A: falta completar ICW1, ICW2 e ICW4.");
    }
}
