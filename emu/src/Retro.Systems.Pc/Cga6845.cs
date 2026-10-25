using Retro.Core;

namespace Retro.Systems.Pc;

/// <summary>CGA RGBI output and MC6845 registers. ClockMaster receives the 14.31818 MHz oscillator.</summary>
public sealed partial class Cga6845
{
    public static readonly int[] Palette =
    [0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xAA5500, 0xAAAAAA,
     0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF];
    private static readonly byte[] Masks = [255, 255, 255, 255, 127, 31, 127, 127, 3, 31, 127, 31, 63, 255, 63, 255];
    private readonly byte[] _registers = new byte[18];
    private readonly PcMemory _memory;
    private readonly byte[] _font;
    private int _index, _oscillator, _character, _raster, _row, _adjust, _vsyncRemaining;
    private bool _inAdjust, _lightPen;
    public Cga6845(PcMemory memory, ReadOnlySpan<byte> font = default)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        if (font.Length is not (0 or 1024 or 2048)) throw new ArgumentException("La fuente CGA debe ocupar 1024 o 2048 bytes.", nameof(font));
        _font = font.ToArray();
    }
    public VideoFrame Frame { get; } = new(640, 200);
    public byte Mode { get; private set; }
    public byte Color { get; private set; }
    public long FrameCount { get; private set; }
    public bool FrameReady { get; set; }
    public bool DisplayEnabled => !_inAdjust && _character < _registers[1] && _row < _registers[6];
    public byte Status => (byte)((DisplayEnabled ? 0 : 1) | (_lightPen ? 2 : 0) | (_vsyncRemaining > 0 ? 8 : 0));

    public void Reset()
    {
        Array.Clear(_registers); Mode = Color = 0; _index = _oscillator = _character = _raster = _row = _adjust = _vsyncRemaining = 0;
        _inAdjust = _lightPen = FrameReady = false; FrameCount = 0;
        Array.Fill(Frame.Pixels, unchecked((int)0xFF000000));
    }
    public byte Read(ushort port)
    {
        if (port is >= 0x3D0 and <= 0x3D7)
            return (port & 1) == 0 ? (byte)0xFF : _index is >= 12 and <= 17 ? _registers[_index] : (byte)0;
        return port == 0x3DA ? Status : (byte)0xFF;
    }
    public void Write(ushort port, byte value)
    {
        if (port is >= 0x3D0 and <= 0x3D7)
        {
            if ((port & 1) == 0) _index = value & 31;
            else if (_index < 16) _registers[_index] = (byte)(value & Masks[_index]);
            return;
        }
        switch (port)
        {
            case 0x3D8: Mode = value; break;
            case 0x3D9: Color = (byte)(value & 63); break;
            case 0x3DB: _lightPen = false; break;
            case 0x3DC:
                if (!_lightPen)
                {
                    int address = (StartAddress + _row * _registers[1] + _character) & 0x3FFF;
                    _registers[16] = (byte)(address >> 8); _registers[17] = (byte)address; _lightPen = true;
                }
                break;
        }
    }
    private int StartAddress => (_registers[12] << 8) | _registers[13];
    public void ClockMaster()
    {
        if (++_oscillator < ((Mode & 1) != 0 ? 8 : 16)) return;
        _oscillator = 0;
        if (++_character <= _registers[0]) return;
        _character = 0;
        if (_vsyncRemaining > 0) _vsyncRemaining--;
        if (_inAdjust)
        {
            if (++_adjust >= _registers[5]) EndFrame();
        }
        else if (++_raster > _registers[9])
        {
            _raster = 0;
            if (++_row > _registers[4])
            {
                if (_registers[5] == 0) EndFrame();
                else { _inAdjust = true; _adjust = 0; }
            }
        }
        if (!_inAdjust && _row == _registers[7] && _raster == 0) _vsyncRemaining = 16;
    }
    private void EndFrame()
    {
        _row = _raster = _adjust = 0; _inAdjust = false;
        if (_registers[1] == 0 || _registers[6] == 0) return;
        FrameCount++; Render(); FrameReady = true;
    }
    private static int Rgbi(int index) => unchecked((int)0xFF000000) | Palette[index & 15];
    public void Render()
    {
        int[] pixels = Frame.Pixels;
        Array.Fill(pixels, Rgbi(Color & 15));
        if ((Mode & 8) == 0) { Array.Fill(pixels, Rgbi(0)); return; }
        if ((Mode & 2) != 0) DrawGraphics(pixels);
        else DrawText(pixels);
    }
    private void DrawGraphics(int[] pixels)
    {
        bool highResolution = (Mode & 16) != 0;
        ReadOnlySpan<byte> ram = _memory.VideoRam;
        int columns = Math.Min(80, _registers[1] * 2), height = Math.Min(200, _registers[6] * (_registers[9] + 1));
        for (int y = 0; y < height; y++)
        for (int column = 0; column < columns; column++)
        {
            int value = ram[((StartAddress * 2) + ((y & 1) << 13) + (y >> 1) * columns + column) & 0x3FFF];
            for (int x = 0; x < 8; x++)
            {
                int color;
                if (highResolution) color = ((value >> (7 - x)) & 1) == 0 ? 0 : Color & 15;
                else
                {
                    int pair = (value >> (6 - (x & ~1))) & 3;
                    color = pair == 0 ? Color & 15 : (pair * 2) + ((Color & 0x20) == 0 ? 0 : 1);
                    if ((Mode & 4) != 0 && pair != 0) color = pair switch { 1 => 3, 2 => 4, _ => 7 };
                    if (pair != 0 && (Color & 0x10) != 0) color |= 8;
                }
                pixels[y * 640 + column * 8 + x] = Rgbi(color);
            }
        }
    }
    private void DrawText(int[] pixels)
    {
        bool wide = (Mode & 1) != 0, blink = (Mode & 0x20) != 0;
        int scale = wide ? 1 : 2, height = _registers[9] + 1, columns = Math.Min(wide ? 80 : 40, (int)_registers[1]);
        int cursor = (_registers[14] << 8) | _registers[15], cursorMode = (_registers[10] >> 5) & 3;
        bool cursorOn = cursorMode != 1 && (cursorMode == 0 || ((FrameCount >> (cursorMode == 2 ? 4 : 5)) & 1) == 0);
        ReadOnlySpan<byte> ram = _memory.VideoRam;
        for (int y = 0; y < 200 && y < _registers[6] * height; y++)
        for (int column = 0; column < columns; column++)
        {
            int cell = (StartAddress + y / height * _registers[1] + column) & 0x3FFF;
            int character = ram[(cell * 2) & 0x3FFF], attribute = ram[(cell * 2 + 1) & 0x3FFF], row = y % height;
            int glyphAddress = character * 8 + row;
            int bits = row >= 8 ? 0 : _font.Length == 0 ? (character < 128 ? _memory.Read((uint)(0xFFA6E + glyphAddress)) : 0) :
                glyphAddress < _font.Length ? _font[glyphAddress] : 0;
            if (blink && (attribute & 0x80) != 0 && (FrameCount & 16) != 0) bits = 0;
            if (cell == cursor && cursorOn && row >= (_registers[10] & 31) && row <= (_registers[11] & 31)) bits = 255;
            int background = (attribute >> 4) & (blink ? 7 : 15);
            for (int x = 0; x < 8 * scale; x++)
                pixels[y * 640 + column * 8 * scale + x] = Rgbi((bits & (0x80 >> (x / scale))) != 0 ? attribute & 15 : background);
        }
    }
}