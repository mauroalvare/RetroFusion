namespace Retro.Systems.Pc;

/// <summary>PC/XT keyboard receive latch, set-1 bytes and clock inhibit/reset. Time uses 4.772727 MHz T-states.</summary>
public sealed partial class PcKeyboard
{
    private readonly Queue<byte> _queue = new();
    private long _inhibitClocks;
    private int _transferClocks, _batClocks;
    private bool _clockEnabled, _clear, _latched;
    private byte _data;
    private int _repeatKey = -1, _repeatTimer;
    public event Action<bool>? IrqChanged;
    public byte Data => _data;
    public void Reset()
    {
        StopRepeat(); _queue.Clear(); _inhibitClocks = 0; _transferClocks = _batClocks = 0;
        _clockEnabled = _clear = _latched = false; _data = 0; IrqChanged?.Invoke(false);
    }
    public void SetControl(byte portB)
    {
        bool clock = (portB & 0x40) != 0;
        if (clock && !_clockEnabled && _inhibitClocks >= 95_455)
        {
            StopRepeat(); _queue.Clear(); _batClocks = 2_386_364; _transferClocks = 0;
        }
        if (clock) _inhibitClocks = 0;
        _clockEnabled = clock; _clear = (portB & 0x80) != 0;
        if (_clear) { _data = 0; _latched = false; IrqChanged?.Invoke(false); }
    }
    public void StopRepeat() { _repeatKey = -1; _repeatTimer = 0; }
    public void SendKey(byte scan, bool pressed)
    {
        EnqueueScanCode(pressed ? scan : (byte)(scan | 0x80));
        if (pressed && scan is not (0x1D or 0x2A or 0x36 or 0x38 or 0x3A or 0x45 or 0x46))
        { _repeatKey = scan; _repeatTimer = 2_386_364; }
        else if (!pressed && _repeatKey == scan) StopRepeat();
    }
    public void EnqueueScanCode(byte scanCode)
    {
        if (_queue.Count >= 32) return;
        _queue.Enqueue(scanCode);
    }
    public void Clock()
    {
        if (!_clockEnabled) { _inhibitClocks++; return; }
        if (_repeatKey >= 0 && --_repeatTimer <= 0) { EnqueueScanCode((byte)_repeatKey); _repeatTimer = 159_091; }
        if (_batClocks > 0 && --_batClocks == 0) _queue.Enqueue(0xAA);
        if (_clear || _latched || _queue.Count == 0) return;
        if (++_transferClocks < 4773) return;
        _transferClocks = 0; _data = _queue.Dequeue(); _latched = true; IrqChanged?.Invoke(true);
    }
}