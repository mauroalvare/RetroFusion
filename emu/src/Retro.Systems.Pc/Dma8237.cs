namespace Retro.Systems.Pc;

/// <summary>Four-channel 8237A, normal timing. Clock receives one T-state and an explicit bus grant.</summary>
public sealed partial class Dma8237
{
    private readonly ushort[] _address = new ushort[4], _count = new ushort[4], _baseAddress = new ushort[4], _baseCount = new ushort[4];
    private readonly byte[] _mode = new byte[4];
    private byte _command, _mask = 15, _requests, _softwareRequests, _terminal;
    private bool _highByte;
    private int _active = -1, _phase, _releaseClocks, _priority;
    public Func<int, ushort, byte>? ReadMemory { get; set; }
    public Action<int, ushort, byte>? WriteMemory { get; set; }
    public Func<int, byte>? ReadDevice { get; set; }
    public Action<int, byte>? WriteDevice { get; set; }
    public event Action<int>? Acknowledge;
    public event Action<int>? TerminalCount;
    public bool HoldRequested => _active >= 0 || (_releaseClocks == 0 && Select() >= 0);
    public byte Mask => _mask;
    public ushort Address(int channel) => _address[Validate(channel)];
    public ushort Count(int channel) => _count[Validate(channel)];
    private static int Validate(int channel) => channel is >= 0 and < 4 ? channel : throw new ArgumentOutOfRangeException(nameof(channel));
    public void SetRequest(int channel, bool active)
    {
        int bit = 1 << Validate(channel);
        _requests = (byte)(active ? _requests | bit : _requests & ~bit);
    }
    public void Reset()
    {
        _command = _requests = _softwareRequests = _terminal = 0; _mask = 15; _highByte = false;
        _active = -1; _phase = _releaseClocks = _priority = 0;
        // Reset does not clear address/count or mode registers on the physical part.
    }
    public byte Read(int port)
    {
        if (port is >= 0 and < 8)
        {
            ushort value = (port & 1) == 0 ? _address[port >> 1] : _count[port >> 1];
            byte result = _highByte ? (byte)(value >> 8) : (byte)value; _highByte = !_highByte; return result;
        }
        if (port == 8)
        {
            byte result = (byte)(_terminal | (_requests | _softwareRequests) << 4); _terminal = 0; return result;
        }
        if (port == 15) return _mask;
        return 255;
    }
    public void Write(int port, byte value)
    {
        if (port is >= 0 and < 8)
        {
            int channel = port >> 1; ushort[] current = (port & 1) == 0 ? _address : _count, initial = (port & 1) == 0 ? _baseAddress : _baseCount;
            current[channel] = (ushort)(_highByte ? (current[channel] & 255) | value << 8 : (current[channel] & 0xFF00) | value);
            initial[channel] = current[channel]; _highByte = !_highByte; return;
        }
        int bit = 1 << (value & 3);
        switch (port)
        {
            case 8:
                if ((value & 0xE9) != 0) throw new NotSupportedException("8237: memoria a memoria, polaridad invertida y temporización comprimida/extendida pendientes.");
                _command = value; break;
            case 9: _softwareRequests = (byte)((value & 4) != 0 ? _softwareRequests | bit : _softwareRequests & ~bit); break;
            case 10: _mask = (byte)((value & 4) != 0 ? _mask | bit : _mask & ~bit); break;
            case 11:
                if ((value & 0xC0) == 0xC0 || (value & 12) == 12) throw new NotSupportedException("8237: cascada o tipo de transferencia reservado.");
                _mode[value & 3] = value; break;
            case 12: _highByte = false; break;
            case 13: Reset(); break;
            case 14: _mask = 0; break;
            case 15: _mask = (byte)(value & 15); break;
            default: throw new ArgumentOutOfRangeException(nameof(port));
        }
    }
    private int Select()
    {
        if ((_command & 4) != 0) return -1;
        int pending = (_requests | _softwareRequests) & ~_mask;
        for (int i = 0; i < 4; i++) { int c = (_priority + i) & 3; if ((pending & (1 << c)) != 0) return c; }
        return -1;
    }
    public void Clock(bool granted)
    {
        if (_releaseClocks > 0) { _releaseClocks--; return; }
        if (!granted) return;
        if (_active < 0)
        {
            _active = Select(); if (_active < 0) return;
            _phase = 0; Acknowledge?.Invoke(_active);
        }
        if (++_phase < 4) return;
        int channel = _active, type = (_mode[channel] >> 2) & 3;
        if (type == 1)
        {
            if (ReadDevice == null || WriteMemory == null) throw new InvalidOperationException("8237: faltan conexiones de dispositivo a memoria.");
            WriteMemory(channel, _address[channel], ReadDevice(channel));
        }
        else if (type == 2)
        {
            if (ReadMemory == null || WriteDevice == null) throw new InvalidOperationException("8237: faltan conexiones de memoria a dispositivo.");
            WriteDevice(channel, ReadMemory(channel, _address[channel]));
        }
        _address[channel] = (ushort)(_address[channel] + ((_mode[channel] & 0x20) != 0 ? -1 : 1));
        bool terminal = _count[channel]-- == 0;
        if (terminal) Complete(channel);
        if ((_command & 0x10) != 0) _priority = (channel + 1) & 3;
        int mode = _mode[channel] >> 6;
        bool requested = ((_requests | _softwareRequests) & (1 << channel)) != 0;
        if (terminal || mode == 1 || mode == 0 && !requested)
        {
            _active = -1;
            if (mode == 1) _releaseClocks = 4; // At least one CPU byte bus cycle between single transfers.
        }
        _phase = 0;
    }
    private void Complete(int channel)
    {
        _terminal |= (byte)(1 << channel); _softwareRequests &= (byte)~(1 << channel);
        if ((_mode[channel] & 0x10) != 0) { _address[channel] = _baseAddress[channel]; _count[channel] = _baseCount[channel]; }
        else _mask |= (byte)(1 << channel);
        TerminalCount?.Invoke(channel);
    }
    public void EndProcess(int channel)
    {
        Validate(channel); Complete(channel);
        if (_active == channel) { _active = -1; _phase = 0; }
    }
}