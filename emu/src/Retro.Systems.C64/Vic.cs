using Retro.Core;

namespace Retro.Systems.C64;

/// <summary>
/// MOS 6569 VIC-II (PAL): 63 cycles per line, 312 lines. Text and bitmap modes (standard,
/// multicolor and extended color), fine scrolling, borders, 8 sprites with expansion,
/// multicolor, priority and collisions, raster interrupts and bad lines (the 40 cycles a
/// line in which the VIC stops the CPU to read the character pointers). Each line is drawn
/// at its end with the registers as they are then, which covers raster-interrupt effects
/// (splits, colour bars) at line precision.
/// </summary>
public sealed class Vic
{
    public const int CyclesPerLine = 63, Lines = 312;
    public const int Width = 384, Height = 272;
    /// <summary>Raster line drawn at y = 0 (25 text rows start at line 51, so 36 border lines above).</summary>
    public const int FirstVisibleLine = 15;
    /// <summary>VIC X coordinate of the first frame column (the display starts at X 24, after 32 border pixels).</summary>
    private const int FirstX = 24 - 32;

    /// <summary>The "Pepto" palette, measured from real machines.</summary>
    public static readonly int[] Palette =
    {
        0x000000, 0xFFFFFF, 0x68372B, 0x70A4B2, 0x6F3D86, 0x588D43, 0x352879, 0xB8C76F,
        0x6F4F25, 0x433900, 0x9A6759, 0x444444, 0x6C6C6C, 0x9AD284, 0x6C5EB5, 0x959595,
    };
    private static readonly int[] Argb = Palette.Select(c => unchecked((int)0xFF000000) | c).ToArray();

    private readonly byte[] _r = new byte[0x40];
    private readonly byte[] _matrix = new byte[40];
    private readonly byte[] _colors = new byte[40];
    private readonly byte[] _line = new byte[Width];
    private readonly bool[] _fg = new bool[Width];
    private readonly byte[] _spriteAt = new byte[Width];
    private int _irqFlags, _irqMask;
    private byte _spriteSprite, _spriteBg;
    private int _vcBase, _rc;
    private bool _display, _badline, _denLatched, _vBorder = true;

    public int Raster { get; private set; }
    public int Cycle { get; private set; }
    public VideoFrame Frame { get; } = new(Width, Height);
    /// <summary>Set when the last line of a frame has been drawn; the machine clears it.</summary>
    public bool FrameDone { get; set; }

    /// <summary>Reads a byte of the VIC's 16 KB window (the machine adds the bank and the character ROM).</summary>
    public Func<int, byte> Fetch = _ => 0;
    /// <summary>Reads the colour RAM (low nibble).</summary>
    public Func<int, byte> ColorRam = _ => 0;

    public bool Irq => (_irqFlags & _irqMask & 0x0F) != 0;

    /// <summary>The CPU is stopped on read cycles while the VIC reads the character pointers of a bad line.</summary>
    public bool BaLow => _badline && Cycle >= 12 && Cycle <= 54;

    private int RasterCompare => _r[0x12] | ((_r[0x11] & 0x80) << 1);

    public void Reset()
    {
        Array.Clear(_r);
        _irqFlags = _irqMask = 0;
        _spriteSprite = _spriteBg = 0;
        Raster = 0;
        Cycle = 0;
        _vcBase = _rc = 0;
        _display = _badline = _denLatched = false;
        _vBorder = true;
    }

    public void Tick()
    {
        if (++Cycle < CyclesPerLine)
            return;
        EndLine();
        Cycle = 0;
        StartLine();
    }

    private void StartLine()
    {
        if (Raster == 0x30 && (_r[0x11] & 0x10) != 0)
            _denLatched = true;
        _badline = IsBadLine(_r[0x11]);
        if (Raster == RasterCompare)
            _irqFlags |= 0x01;
        if (_badline)
        {
            _display = true;
            _rc = 0;
            FetchMatrix();
        }
    }

    private bool IsBadLine(byte ctrl1) => Raster >= 0x30 && Raster <= 0xF7 && _denLatched && (Raster & 7) == (ctrl1 & 7);

    private void FetchMatrix()
    {
        int vm = (_r[0x18] & 0xF0) << 6;
        for (int i = 0; i < 40; i++)
        {
            int vc = (_vcBase + i) & 0x3FF;
            _matrix[i] = Fetch(vm | vc);
            _colors[i] = (byte)(ColorRam(vc) & 0x0F);
        }
    }

    private void EndLine()
    {
        RenderLine();
        int vcEnd = _display ? (_vcBase + 40) & 0x3FF : _vcBase;
        if (_rc == 7)
        {
            _vcBase = vcEnd;
            _display = false;
        }
        if (_display)
            _rc = (_rc + 1) & 7;
        if (++Raster == Lines)
        {
            Raster = 0;
            _vcBase = 0;
            _denLatched = false;
            FrameDone = true;
        }
    }

    // ------------------------------------------------------------------ drawing
    private void RenderLine()
    {
        bool rsel = (_r[0x11] & 0x08) != 0, den = (_r[0x11] & 0x10) != 0;
        int top = rsel ? 51 : 55, bottom = rsel ? 251 : 247;
        if (Raster == bottom) _vBorder = true;
        if (Raster == top && den) _vBorder = false;

        int y = Raster - FirstVisibleLine;
        if (y < 0 || y >= Height)
            return;
        byte border = (byte)(_r[0x20] & 15);
        var px = Frame.Pixels;
        int row = y * Width;
        if (_vBorder)
        {
            DrawSprites(onlyCollisions: true);
            Array.Fill(px, Argb[border], row, Width);
            return;
        }
        DrawGraphics();
        DrawSprites(onlyCollisions: false);
        bool csel = (_r[0x16] & 0x08) != 0;
        int left = (csel ? 24 : 31) - FirstX, right = (csel ? 344 : 335) - FirstX;
        for (int x = 0; x < Width; x++)
            px[row + x] = Argb[x < left || x >= right ? border : _line[x]];
    }

    private void DrawGraphics()
    {
        byte bg0 = (byte)(_r[0x21] & 15);
        Array.Fill(_line, bg0);
        Array.Clear(_fg);
        bool ecm = (_r[0x11] & 0x40) != 0, bmm = (_r[0x11] & 0x20) != 0, mcm = (_r[0x16] & 0x10) != 0;
        bool invalid = ecm && (bmm || mcm);
        int xscroll = _r[0x16] & 7;
        int cb = (_r[0x18] & 0x0E) << 10;
        int start = 24 - FirstX + xscroll;
        for (int col = 0; col < 40; col++)
        {
            int ox = start + col * 8;
            if (!_display)
            {
                // idle state: the VIC reads $3FFF and shows its bits in black
                byte idle = Fetch(ecm ? 0x39FF : 0x3FFF);
                for (int b = 0; b < 8; b++)
                    if ((idle & (0x80 >> b)) != 0) Put(ox + b, 0, true);
                continue;
            }
            byte ch = _matrix[col], color = _colors[col];
            byte data = bmm
                ? Fetch((cb & 0x2000) | (((_vcBase + col) & 0x3FF) << 3) | _rc)
                : Fetch(cb | ((ecm ? ch & 0x3F : ch) << 3) | _rc);
            // multicolor text: only characters whose colour has bit 3 set use 2-bit pixels
            bool multi = mcm && (invalid || bmm || (color & 8) != 0);
            if (multi)
            {
                for (int b = 0; b < 8; b += 2)
                {
                    int pair = (data >> (6 - b)) & 3;
                    byte c = pair switch
                    {
                        0 => bg0,
                        1 => bmm ? (byte)(ch >> 4) : (byte)(_r[0x22] & 15),
                        2 => bmm ? (byte)(ch & 15) : (byte)(_r[0x23] & 15),
                        _ => bmm ? color : (byte)(color & 7),
                    };
                    bool fg = pair >= 2;
                    Put(ox + b, invalid ? (byte)0 : c, fg);
                    Put(ox + b + 1, invalid ? (byte)0 : c, fg);
                }
            }
            else
            {
                byte fgc = bmm ? (byte)(ch >> 4) : mcm ? (byte)(color & 7) : color;
                byte bgc = bmm ? (byte)(ch & 15) : ecm ? (byte)(_r[0x21 + (ch >> 6)] & 15) : bg0;
                for (int b = 0; b < 8; b++)
                {
                    bool on = (data & (0x80 >> b)) != 0;
                    Put(ox + b, invalid ? (byte)0 : on ? fgc : bgc, on);
                }
            }
        }
    }

    private void Put(int x, byte color, bool fg)
    {
        if ((uint)x >= Width)
            return;
        _line[x] = color;
        _fg[x] = fg;
    }

    private void DrawSprites(bool onlyCollisions)
    {
        byte enabled = _r[0x15];
        if (enabled == 0)
            return;
        Array.Clear(_spriteAt);
        int vm = (_r[0x18] & 0xF0) << 6;
        byte oldSs = _spriteSprite, oldSb = _spriteBg;
        for (int s = 7; s >= 0; s--)
        {
            int bit = 1 << s;
            if ((enabled & bit) == 0)
                continue;
            bool yexp = (_r[0x17] & bit) != 0;
            int rel = Raster - (_r[1 + 2 * s] + 1);
            if (rel < 0 || rel >= (yexp ? 42 : 21))
                continue;
            int spriteLine = yexp ? rel >> 1 : rel;
            int ptr = Fetch(vm | 0x3F8 | s);
            int addr = (ptr << 6) + spriteLine * 3;
            int bits = (Fetch(addr & 0x3FFF) << 16) | (Fetch((addr + 1) & 0x3FFF) << 8) | Fetch((addr + 2) & 0x3FFF);
            if (bits == 0)
                continue;
            int x = _r[2 * s] | (((_r[0x10] >> s) & 1) << 8);
            bool xexp = (_r[0x1D] & bit) != 0, mc = (_r[0x1C] & bit) != 0, behind = (_r[0x1B] & bit) != 0;
            int w = xexp ? 2 : 1;
            int ox0 = x - FirstX;
            for (int i = 0; i < 24; i++)
            {
                byte color;
                bool set;
                if (mc)
                {
                    int pair = (bits >> (22 - (i & ~1))) & 3;
                    set = pair != 0;
                    color = pair switch { 1 => (byte)(_r[0x25] & 15), 2 => (byte)(_r[0x27 + s] & 15), _ => (byte)(_r[0x26] & 15) };
                }
                else
                {
                    set = ((bits >> (23 - i)) & 1) != 0;
                    color = (byte)(_r[0x27 + s] & 15);
                }
                if (!set)
                    continue;
                for (int d = 0; d < w; d++)
                {
                    int ox = ox0 + i * w + d;
                    if ((uint)ox >= Width)
                        continue;
                    if (_spriteAt[ox] != 0)
                        _spriteSprite |= (byte)(_spriteAt[ox] | bit);
                    _spriteAt[ox] |= (byte)bit;
                    if (onlyCollisions)
                        continue;
                    if (_fg[ox])
                        _spriteBg |= (byte)bit;
                    if (!(behind && _fg[ox]))
                        _line[ox] = color;
                }
            }
        }
        if (oldSs == 0 && _spriteSprite != 0) _irqFlags |= 0x04;
        if (oldSb == 0 && _spriteBg != 0) _irqFlags |= 0x02;
    }

    // ------------------------------------------------------------------ registers
    public byte Read(int reg)
    {
        reg &= 0x3F;
        switch (reg)
        {
            case 0x1E: { byte v = _spriteSprite; _spriteSprite = 0; return v; }
            case 0x1F: { byte v = _spriteBg; _spriteBg = 0; return v; }
            default: return Peek(reg);
        }
    }

    public byte Peek(int reg)
    {
        reg &= 0x3F;
        return reg switch
        {
            0x11 => (byte)((_r[0x11] & 0x7F) | ((Raster & 0x100) >> 1)),
            0x12 => (byte)Raster,
            0x13 or 0x14 => 0,
            0x16 => (byte)(_r[0x16] | 0xC0),
            0x18 => (byte)(_r[0x18] | 0x01),
            0x19 => (byte)(_irqFlags | 0x70 | (Irq ? 0x80 : 0)),
            0x1A => (byte)(_irqMask | 0xF0),
            0x1E => _spriteSprite,
            0x1F => _spriteBg,
            >= 0x20 and <= 0x2E => (byte)(_r[reg] | 0xF0),
            >= 0x2F => 0xFF,
            _ => _r[reg],
        };
    }

    public void Write(int reg, byte v)
    {
        reg &= 0x3F;
        switch (reg)
        {
            case 0x11:
                _r[0x11] = v;
                if (Raster == 0x30 && (v & 0x10) != 0)
                    _denLatched = true;
                bool bad = IsBadLine(v);
                if (bad && !_badline && Cycle < 54)
                {
                    _display = true;
                    if (Cycle <= 14) _rc = 0;
                    FetchMatrix();
                }
                _badline = bad && Cycle < 54;
                if (Raster == RasterCompare) _irqFlags |= 0x01;
                break;
            case 0x12:
                _r[0x12] = v;
                if (Raster == RasterCompare) _irqFlags |= 0x01;
                break;
            case 0x19:
                _irqFlags &= ~v & 0x0F;
                break;
            case 0x1A:
                _irqMask = v & 0x0F;
                break;
            case 0x1E:
            case 0x1F:
                break;
            default:
                if (reg < 0x2F) _r[reg] = v;
                break;
        }
    }

    public void Save(BinaryWriter w)
    {
        w.Write(_r);
        w.Write(_matrix);
        w.Write(_colors);
        w.Write(Raster); w.Write(Cycle);
        w.Write(_irqFlags); w.Write(_irqMask); w.Write(_spriteSprite); w.Write(_spriteBg);
        w.Write(_vcBase); w.Write(_rc);
        w.Write(_display); w.Write(_badline); w.Write(_denLatched); w.Write(_vBorder);
    }

    public void Load(BinaryReader r)
    {
        r.Read(_r, 0, _r.Length);
        r.Read(_matrix, 0, 40);
        r.Read(_colors, 0, 40);
        Raster = r.ReadInt32(); Cycle = r.ReadInt32();
        _irqFlags = r.ReadInt32(); _irqMask = r.ReadInt32(); _spriteSprite = r.ReadByte(); _spriteBg = r.ReadByte();
        _vcBase = r.ReadInt32(); _rc = r.ReadInt32();
        _display = r.ReadBoolean(); _badline = r.ReadBoolean(); _denLatched = r.ReadBoolean(); _vBorder = r.ReadBoolean();
    }

    public string Describe()
    {
        bool ecm = (_r[0x11] & 0x40) != 0, bmm = (_r[0x11] & 0x20) != 0, mcm = (_r[0x16] & 0x10) != 0;
        string mode = (ecm, bmm, mcm) switch
        {
            (false, false, false) => "texto",
            (false, false, true) => "texto multicolor",
            (true, false, false) => "texto color extendido",
            (false, true, false) => "bitmap",
            (false, true, true) => "bitmap multicolor",
            _ => "modo inválido (negro)",
        };
        return $"línea {Raster}, ciclo {Cycle}{(_badline ? " (bad line)" : "")}, modo {mode}, sprites {_r[0x15]:X2}, IRQ {_irqFlags:X}/{_irqMask:X}";
    }
}
