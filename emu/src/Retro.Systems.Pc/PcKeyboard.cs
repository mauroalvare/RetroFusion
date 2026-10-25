namespace Retro.Systems.Pc;

/// <summary>PC/XT keyboard receive latch, set-1 bytes and clock inhibit/reset. Time uses 4.772727 MHz T-states.</summary>
public sealed class PcKeyboard
{
    private readonly Queue<byte> _queue = new();
    private long _inhibitClocks;
    private int _transferClocks, _batClocks;
    private bool _clockEnabled, _clear, _latched;
    private byte _data;
    public event Action<bool>? IrqChanged;
    public byte Data => _data;
    public void Reset()
    {
        _queue.Clear(); _inhibitClocks = 0; _transferClocks = _batClocks = 0;
        _clockEnabled = _clear = _latched = false; _data = 0; IrqChanged?.Invoke(false);
    }
    public void SetControl(byte portB)
    {
        bool clock = (portB & 0x40) != 0;
        if (clock && !_clockEnabled && _inhibitClocks >= 95_455)
        {
            _queue.Clear(); _batClocks = 2_386_364; _transferClocks = 0;
        }
        if (clock) _inhibitClocks = 0;
        _clockEnabled = clock; _clear = (portB & 0x80) != 0;
        if (_clear) { _data = 0; _latched = false; IrqChanged?.Invoke(false); }
    }
    public void EnqueueScanCode(byte scanCode)
    {
        if (_queue.Count >= 32) return;
        _queue.Enqueue(scanCode);
    }
    public void Clock()
    {
        if (!_clockEnabled) { _inhibitClocks++; return; }
        if (_batClocks > 0 && --_batClocks == 0) _queue.Enqueue(0xAA);
        if (_clear || _latched || _queue.Count == 0) return;
        if (++_transferClocks < 4773) return;
        _transferClocks = 0; _data = _queue.Dequeue(); _latched = true; IrqChanged?.Invoke(true);
    }
}