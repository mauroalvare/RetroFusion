namespace Retro.Systems.Pc;

/// <summary>8255 mode 0, with independent input/output directions and bit set/reset for port C.</summary>
public sealed partial class Ppi8255
{
    private byte _a, _b, _c, _control = 0x9B;
    public Func<byte>? InputA { get; set; }
    public Func<byte>? InputB { get; set; }
    public Func<byte>? InputC { get; set; }
    public event Action<byte>? OutputBChanged;
    public byte PortB => (_control & 2) == 0 ? _b : (byte)0;
    public byte Control => _control;
    public void Reset()
    {
        _a = _b = _c = 0; _control = 0x9B; OutputBChanged?.Invoke(PortB);
    }
    public byte Read(int port) => port switch
    {
        0 => (_control & 0x10) != 0 ? InputA?.Invoke() ?? (byte)255 : _a,
        1 => (_control & 2) != 0 ? InputB?.Invoke() ?? (byte)255 : _b,
        2 => ReadC(), 3 => 255,
        _ => throw new ArgumentOutOfRangeException(nameof(port))
    };
    private byte ReadC()
    {
        int pins = InputC?.Invoke() ?? 255;
        int mask = ((_control & 8) != 0 ? 0xF0 : 0) | ((_control & 1) != 0 ? 15 : 0);
        return (byte)((pins & mask) | (_c & ~mask));
    }
    public void Write(int port, byte value)
    {
        switch (port)
        {
            case 0: _a = value; break;
            case 1: _b = value; OutputBChanged?.Invoke(PortB); break;
            case 2: _c = value; break;
            case 3:
                if ((value & 0x80) == 0)
                {
                    int bit = 1 << ((value >> 1) & 7); _c = (byte)((value & 1) == 0 ? _c & ~bit : _c | bit);
                    return;
                }
                if ((value & 0x64) != 0) throw new NotSupportedException("8255: los modos con handshake todavía no están implementados.");
                _control = value; _a = _b = _c = 0; OutputBChanged?.Invoke(PortB);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(port));
        }
    }
}