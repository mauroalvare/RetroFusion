using Retro.Core;

namespace Retro.Systems.Nes;

/// <summary>
/// The NES picture processor (Ricoh 2C02, NTSC): 341 dots x 262 lines per frame, three dots
/// per CPU cycle. Registers $2000-$2007 with the internal v/t/x/w scroll registers, nametables
/// laid out by <see cref="NesCartridge.NametableFor"/>, 32-byte palette, background with
/// scroll and up to 8 sprites per line (8x8 or 8x16) with sprite 0 hit and overflow.
/// Each visible line is drawn at its dot 1 from the scroll state of that moment, so splits
/// written during horizontal blank work; changes in the middle of a line do not.
/// </summary>
public sealed class Ppu
{
    public const int Width = 256, Height = 240, DotsPerLine = 341, LinesPerFrame = 262;
    public const int VblankLine = 241, PreRenderLine = 261;

    /// <summary>A common 2C02 palette (ARGB without alpha), indexed by the 6-bit color.</summary>
    public static readonly int[] SystemPalette =
    {
        0x666666, 0x002A88, 0x1412A7, 0x3B00A4, 0x5C007E, 0x6E0040, 0x6C0600, 0x561D00,
        0x333500, 0x0B4800, 0x005200, 0x004F08, 0x00404D, 0x000000, 0x000000, 0x000000,
        0xADADAD, 0x155FD9, 0x4240FF, 0x7527FE, 0xA01ACC, 0xB71E7B, 0xB53120, 0x994E00,
        0x6B6D00, 0x388700, 0x0C9300, 0x008F32, 0x007C8D, 0x000000, 0x000000, 0x000000,
        0xFFFEFF, 0x64B0FF, 0x9290FF, 0xC676FF, 0xF36AFF, 0xFE6ECC, 0xFE8170, 0xEA9E22,
        0xBCBE00, 0x88D800, 0x5CE430, 0x45E082, 0x48CDDE, 0x4F4F4F, 0x000000, 0x000000,
        0xFFFEFF, 0xC0DFFF, 0xD3D2FF, 0xE8C8FF, 0xFBC2FF, 0xFEC4EA, 0xFECCC5, 0xF7D8A5,
        0xE4E594, 0xCFEF96, 0xBDF4AB, 0xB3F3CC, 0xB5EBF2, 0xB8B8B8, 0x000000, 0x000000,
    };

    private readonly NesCartridge _cart;
    private readonly byte[] _vram = new byte[0x1000];
    private readonly byte[] _palette = new byte[32];
    private readonly byte[] _lineColor = new byte[Width];   // final 6-bit color per pixel of the line
    private readonly byte[] _bgPixel = new byte[Width];     // background pixel 0-3 (0 = transparent)

    // internal scroll registers (names as in the nesdev wiki)
    private int _v, _t, _x;
    private bool _w;
    private byte _ctrl, _mask, _status, _readBuffer, _openBus;
    private int _sprite0HitDot = -1;

    public Ppu(NesCartridge cart)
    {
        _cart = cart;
        Frame = new VideoFrame(Width, Height);
    }

    public VideoFrame Frame { get; }
    public byte[] Oam { get; } = new byte[256];
    public byte OamAddress { get; set; }
    public int Line { get; private set; }
    public int Dot { get; private set; }
    public long FrameNumber { get; private set; }
    /// <summary>True from the end of the last visible line until the next one starts drawing.</summary>
    public bool FrameReady { get; set; }

    /// <summary>Level of the NMI output (vblank flag AND "NMI on vblank" of $2000); the CPU reacts to its rising edge.</summary>
    public bool Nmi => (_status & 0x80) != 0 && (_ctrl & 0x80) != 0;

    public bool RenderingEnabled => (_mask & 0x18) != 0;
    public int ScrollV => _v;
    public int ScrollT => _t;
    public int FineX => _x;

    public void Reset()
    {
        _ctrl = _mask = _status = _readBuffer = 0;
        _w = false;
        _t = _x = 0;
        Line = Dot = 0;
    }

    // ---------------------------------------------------------------- CPU side

    /// <summary>CPU read of $2000-$3FFF (mirrored every 8 bytes).</summary>
    public byte ReadRegister(ushort address)
    {
        switch (address & 7)
        {
            case 2:
                _openBus = (byte)(_status & 0xE0 | _openBus & 0x1F);
                _status &= 0x7F;
                _w = false;
                return _openBus;
            case 4:
                _openBus = Oam[OamAddress];
                return _openBus;
            case 7:
                int a = _v & 0x3FFF;
                byte result;
                if (a >= 0x3F00)
                {
                    // palette reads are direct; the buffer gets the nametable "underneath"
                    result = (byte)(_palette[PaletteIndex(a)] | _openBus & 0xC0);
                    _readBuffer = Read((ushort)(a - 0x1000));
                }
                else
                {
                    result = _readBuffer;
                    _readBuffer = Read((ushort)a);
                }
                IncrementV();
                _openBus = result;
                return result;
            default:
                return _openBus;
        }
    }

    /// <summary>CPU write of $2000-$3FFF (mirrored every 8 bytes).</summary>
    public void WriteRegister(ushort address, byte value)
    {
        _openBus = value;
        switch (address & 7)
        {
            case 0:
                _ctrl = value;
                _t = _t & ~0x0C00 | (value & 3) << 10;
                break;
            case 1:
                _mask = value;
                break;
            case 3:
                OamAddress = value;
                break;
            case 4:
                Oam[OamAddress++] = value;
                break;
            case 5:
                if (!_w)
                {
                    _t = _t & ~0x001F | value >> 3;
                    _x = value & 7;
                }
                else
                    _t = _t & ~0x73E0 | (value & 7) << 12 | (value >> 3) << 5;
                _w = !_w;
                break;
            case 6:
                if (!_w)
                    _t = _t & 0x00FF | (value & 0x3F) << 8;
                else
                {
                    _t = _t & 0x7F00 | value;
                    _v = _t;
                }
                _w = !_w;
                break;
            case 7:
                Write((ushort)(_v & 0x3FFF), value);
                IncrementV();
                break;
        }
    }

    private void IncrementV() => _v = (_v + ((_ctrl & 0x04) != 0 ? 32 : 1)) & 0x7FFF;

    // ---------------------------------------------------------------- PPU bus

    private static int PaletteIndex(int address)
    {
        int i = address & 0x1F;
        return (i & 0x13) == 0x10 ? i & 0x0F : i;   // $3F10/14/18/1C mirror $3F00/04/08/0C
    }

    private int VramIndex(int address) => _cart.NametableFor((ushort)address) * 0x400 + (address & 0x3FF);

    /// <summary>Read of the PPU address space $0000-$3FFF.</summary>
    public byte Read(ushort address)
    {
        int a = address & 0x3FFF;
        if (a < 0x2000) return _cart.PpuRead((ushort)a);
        if (a < 0x3F00) return _vram[VramIndex(a)];
        return _palette[PaletteIndex(a)];
    }

    /// <summary>Write of the PPU address space $0000-$3FFF.</summary>
    public void Write(ushort address, byte value)
    {
        int a = address & 0x3FFF;
        if (a < 0x2000) _cart.PpuWrite((ushort)a, value);
        else if (a < 0x3F00) _vram[VramIndex(a)] = value;
        else _palette[PaletteIndex(a)] = (byte)(value & 0x3F);
    }

    // ---------------------------------------------------------------- timing

    /// <summary>Advances one dot.</summary>
    public void Step()
    {
        bool rendering = RenderingEnabled;
        if (Line < Height)
        {
            if (Dot == 1)
                DrawLine(Line);
            if (_sprite0HitDot >= 0 && Dot >= _sprite0HitDot)
            {
                _status |= 0x40;
                _sprite0HitDot = -1;
            }
            if (rendering)
            {
                if (Dot == 256) IncrementY();
                else if (Dot == 257) CopyHorizontal();
            }
            if (Line == Height - 1 && Dot == 256)
                FrameReady = true;
        }
        else if (Line == VblankLine && Dot == 1)
            _status |= 0x80;
        else if (Line == PreRenderLine)
        {
            if (Dot == 1)
            {
                _status &= 0x1F;
                _sprite0HitDot = -1;
            }
            if (rendering)
            {
                if (Dot == 256) IncrementY();
                else if (Dot == 257) CopyHorizontal();
                else if (Dot >= 280 && Dot <= 304) _v = _v & ~0x7BE0 | _t & 0x7BE0;
                // odd frames are one dot shorter when rendering is on
                else if (Dot == 339 && (FrameNumber & 1) == 1) Dot++;
            }
        }

        if (++Dot == DotsPerLine)
        {
            Dot = 0;
            if (++Line == LinesPerFrame)
            {
                Line = 0;
                FrameNumber++;
            }
        }
    }

    private void CopyHorizontal() => _v = _v & ~0x041F | _t & 0x041F;

    private void IncrementY()
    {
        if ((_v & 0x7000) != 0x7000)
        {
            _v += 0x1000;
            return;
        }
        _v &= ~0x7000;
        int y = (_v & 0x03E0) >> 5;
        if (y == 29) { y = 0; _v ^= 0x0800; }
        else if (y == 31) y = 0;
        else y++;
        _v = _v & ~0x03E0 | y << 5;
    }

    // ---------------------------------------------------------------- drawing

    private void DrawLine(int line)
    {
        bool showBg = (_mask & 0x08) != 0, showSprites = (_mask & 0x10) != 0;
        byte backdrop = _palette[0];
        Array.Clear(_bgPixel);

        if (showBg)
            DrawBackground();
        for (int px = 0; px < Width; px++)
        {
            int p = _bgPixel[px];
            if (p != 0 && (_mask & 0x02) == 0 && px < 8) { _bgPixel[px] = 0; p = 0; }
            _lineColor[px] = p == 0 ? backdrop : _palette[_bgAttr[px] << 2 | p];
        }

        if (showSprites)
            DrawSprites(line, showBg);

        int greyMask = (_mask & 0x01) != 0 ? 0x30 : 0x3F;
        int row = line * Width;
        var pixels = Frame.Pixels;
        for (int px = 0; px < Width; px++)
            pixels[row + px] = unchecked((int)0xFF000000) | SystemPalette[_lineColor[px] & greyMask];
    }

    private readonly byte[] _bgAttr = new byte[Width];

    private void DrawBackground()
    {
        int v = _v, fineY = (v >> 12) & 7;
        int table = (_ctrl & 0x10) != 0 ? 0x1000 : 0;
        int px = -_x;
        // 33 tiles cover 256 pixels at any fine X
        for (int tile = 0; tile < 33; tile++)
        {
            int nt = 0x2000 | v & 0x0FFF;
            int at = 0x23C0 | v & 0x0C00 | (v >> 4) & 0x38 | (v >> 2) & 0x07;
            byte index = Read((ushort)nt);
            int shift = (v >> 4) & 4 | v & 2;
            byte attr = (byte)((Read((ushort)at) >> shift) & 3);
            int addr = table + index * 16 + fineY;
            byte lo = Read((ushort)addr), hi = Read((ushort)(addr + 8));
            for (int bit = 7; bit >= 0; bit--, px++)
            {
                if (px < 0 || px >= Width) continue;
                _bgPixel[px] = (byte)((lo >> bit) & 1 | ((hi >> bit) & 1) << 1);
                _bgAttr[px] = attr;
            }
            // coarse X increment, wrapping into the horizontally next nametable
            if ((v & 0x001F) == 31) { v &= ~0x001F; v ^= 0x0400; }
            else v++;
        }
    }

    private readonly int[] _lineSprites = new int[8];

    private void DrawSprites(int line, bool showBg)
    {
        int height = (_ctrl & 0x20) != 0 ? 16 : 8;
        int count = 0;
        for (int i = 0; i < 64; i++)
        {
            int row = line - (Oam[i * 4] + 1);
            if (row < 0 || row >= height) continue;
            if (count == 8)
            {
                _status |= 0x20;
                break;
            }
            _lineSprites[count++] = i;
        }

        bool clipLeft = (_mask & 0x04) == 0;
        // the lowest OAM index is in front: draw from back to front
        for (int s = count - 1; s >= 0; s--)
        {
            int i = _lineSprites[s];
            byte y = Oam[i * 4], tile = Oam[i * 4 + 1], attr = Oam[i * 4 + 2], x = Oam[i * 4 + 3];
            int row = line - (y + 1);
            if ((attr & 0x80) != 0) row = height - 1 - row;
            int addr;
            if (height == 16)
            {
                addr = (tile & 1) * 0x1000 + (tile & 0xFE) * 16;
                if (row >= 8) { addr += 16; row -= 8; }
            }
            else
                addr = ((_ctrl & 0x08) != 0 ? 0x1000 : 0) + tile * 16;
            addr += row;
            byte lo = Read((ushort)addr), hi = Read((ushort)(addr + 8));
            bool behind = (attr & 0x20) != 0;
            int pal = 0x10 | (attr & 3) << 2;
            for (int col = 0; col < 8; col++)
            {
                int px = x + col;
                if (px >= Width) break;
                if (clipLeft && px < 8) continue;
                int bit = (attr & 0x40) != 0 ? col : 7 - col;
                int p = (lo >> bit) & 1 | ((hi >> bit) & 1) << 1;
                if (p == 0) continue;
                bool bgOpaque = _bgPixel[px] != 0;
                if (i == 0 && showBg && bgOpaque && px != 255 && _sprite0HitDot < 0 && (_status & 0x40) == 0)
                    _sprite0HitDot = px + 1;
                if (!behind || !bgOpaque)
                    _lineColor[px] = _palette[pal | p];
                else
                    _lineColor[px] = _palette[_bgAttr[px] << 2 | _bgPixel[px]];
            }
        }
    }
    // ---------------------------------------------------------------- save states

    public void Save(BinaryWriter w)
    {
        w.Write(_vram); w.Write(_palette); w.Write(Oam); w.Write(OamAddress);
        w.Write(_v); w.Write(_t); w.Write(_x); w.Write(_w);
        w.Write(_ctrl); w.Write(_mask); w.Write(_status); w.Write(_readBuffer); w.Write(_openBus);
        w.Write(_sprite0HitDot); w.Write(Line); w.Write(Dot); w.Write(FrameNumber); w.Write(FrameReady);
    }

    public void Load(BinaryReader r)
    {
        r.Read(_vram, 0, _vram.Length); r.Read(_palette, 0, _palette.Length); r.Read(Oam, 0, Oam.Length); OamAddress = r.ReadByte();
        _v = r.ReadInt32(); _t = r.ReadInt32(); _x = r.ReadInt32(); _w = r.ReadBoolean();
        _ctrl = r.ReadByte(); _mask = r.ReadByte(); _status = r.ReadByte(); _readBuffer = r.ReadByte(); _openBus = r.ReadByte();
        _sprite0HitDot = r.ReadInt32(); Line = r.ReadInt32(); Dot = r.ReadInt32(); FrameNumber = r.ReadInt64(); FrameReady = r.ReadBoolean();
    }
}
