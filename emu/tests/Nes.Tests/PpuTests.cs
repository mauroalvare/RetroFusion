using Retro.Systems.Nes;

namespace Nes.Tests;

[TestFixture]
public class PpuTests
{
    /// <summary>A mapper 0 cartridge with CHR RAM, so the tests can draw their own tiles.</summary>
    private static Ppu NewPpu(byte flags6 = 0)
    {
        var image = new byte[16 + NesCartridge.PrgUnit];
        "NES\x1A"u8.CopyTo(image);
        image[4] = 1; image[6] = flags6;
        return new Ppu(NesCartridge.Load(image));
    }

    private static void SetAddress(Ppu ppu, int address)
    {
        ppu.WriteRegister(0x2006, (byte)(address >> 8));
        ppu.WriteRegister(0x2006, (byte)address);
    }

    private static void Fill(Ppu ppu, int address, params byte[] values)
    {
        SetAddress(ppu, address);
        foreach (var v in values) ppu.WriteRegister(0x2007, v);
    }

    /// <summary>Tile <paramref name="tile"/> of the pattern table at $0000: every pixel uses <paramref name="pixel"/> (0-3).</summary>
    private static void SolidTile(Ppu ppu, int tile, int pixel, int table = 0)
    {
        for (int row = 0; row < 8; row++)
        {
            ppu.Write((ushort)(table + tile * 16 + row), (byte)((pixel & 1) != 0 ? 0xFF : 0));
            ppu.Write((ushort)(table + tile * 16 + row + 8), (byte)((pixel & 2) != 0 ? 0xFF : 0));
        }
    }

    /// <summary>Sets the scroll from nametable 0; needed after $2006 writes, which also change t.</summary>
    private static void Scroll(Ppu ppu, int x, int y)
    {
        ppu.WriteRegister(0x2000, 0);
        ppu.WriteRegister(0x2005, (byte)x);
        ppu.WriteRegister(0x2005, (byte)y);
    }

    private static void RunFrame(Ppu ppu)
    {
        long frame = ppu.FrameNumber;
        while (ppu.FrameNumber == frame) ppu.Step();
    }

    private static int Pixel(Ppu ppu, int x, int y) => ppu.Frame.Pixels[y * Ppu.Width + x] & 0xFFFFFF;

    [Test]
    public void DataPort_ReadsAreBuffered_ExceptPalette()
    {
        var ppu = NewPpu();
        Fill(ppu, 0x2000, 0x11, 0x22);
        SetAddress(ppu, 0x2000);
        ppu.ReadRegister(0x2007);   // stale buffer
        Assert.That(ppu.ReadRegister(0x2007), Is.EqualTo(0x11));
        Assert.That(ppu.ReadRegister(0x2007), Is.EqualTo(0x22));

        Fill(ppu, 0x3F01, 0x2A);
        SetAddress(ppu, 0x3F01);
        Assert.That(ppu.ReadRegister(0x2007) & 0x3F, Is.EqualTo(0x2A), "la paleta se lee sin demora");
    }

    [Test]
    public void Increment32_WalksDownAColumn()
    {
        var ppu = NewPpu();
        ppu.WriteRegister(0x2000, 0x04);
        Fill(ppu, 0x2000, 1, 2);
        Assert.That(ppu.Read(0x2000), Is.EqualTo(1));
        Assert.That(ppu.Read(0x2020), Is.EqualTo(2));
    }

    [Test]
    public void PaletteMirrors_SpriteBackdropEntries()
    {
        var ppu = NewPpu();
        Fill(ppu, 0x3F10, 0x05);
        Assert.That(ppu.Read(0x3F00), Is.EqualTo(0x05));
        Fill(ppu, 0x3F04, 0x06);
        Assert.That(ppu.Read(0x3F14), Is.EqualTo(0x06));
        Assert.That(ppu.Read(0x3F21), Is.EqualTo(ppu.Read(0x3F01)), "$3F20-$3FFF repite $3F00-$3F1F");
    }

    [TestCase((byte)0x00, 0x2400, 0x2000)]   // horizontal: $2400 mirrors $2000
    [TestCase((byte)0x01, 0x2800, 0x2000)]   // vertical: $2800 mirrors $2000
    public void Nametables_FollowTheCartridgeMirroring(byte flags6, int mirror, int original)
    {
        var ppu = NewPpu(flags6);
        Fill(ppu, original + 5, 0x77);
        Assert.That(ppu.Read((ushort)(mirror + 5)), Is.EqualTo(0x77));
        Assert.That(ppu.Read((ushort)(original + 0x1005)), Is.EqualTo(0x77), "$3000-$3EFF repite $2000-$2EFF");
    }

    [Test]
    public void ScrollAndAddressWrites_ComposeTheLoopyRegisters()
    {
        var ppu = NewPpu();
        ppu.WriteRegister(0x2000, 0x02);          // nametable 2
        ppu.WriteRegister(0x2005, 0x7D);          // X = 125: coarse 15, fine 5
        ppu.WriteRegister(0x2005, 0x5E);          // Y = 94: coarse 11, fine 6
        Assert.That(ppu.ScrollT, Is.EqualTo(0x6000 | 0x0800 | 11 << 5 | 15));
        Assert.That(ppu.FineX, Is.EqualTo(5));
        ppu.ReadRegister(0x2002);                 // resets the write toggle
        SetAddress(ppu, 0x3F10);
        Assert.That(ppu.ScrollV, Is.EqualTo(0x3F10));
    }

    [Test]
    public void Vblank_RaisesNmi_AndStatusReadClearsIt()
    {
        var ppu = NewPpu();
        ppu.WriteRegister(0x2000, 0x80);
        while (!(ppu.Line == Ppu.VblankLine && ppu.Dot == 2)) ppu.Step();
        Assert.That(ppu.Nmi, Is.True);
        Assert.That(ppu.ReadRegister(0x2002) & 0x80, Is.EqualTo(0x80));
        Assert.That(ppu.Nmi, Is.False);
        Assert.That(ppu.ReadRegister(0x2002) & 0x80, Is.Zero);
    }

    [Test]
    public void Frame_Is341By262Dots()
    {
        var ppu = NewPpu();   // rendering off: no short odd frames
        RunFrame(ppu);
        long dots = 0;
        long frame = ppu.FrameNumber;
        while (ppu.FrameNumber == frame) { ppu.Step(); dots++; }
        Assert.That(dots, Is.EqualTo(341L * 262));
    }

    /// <summary>Background with tile 1 at nametable column 4 of the first row, and the palette 0F/16/27/30.</summary>
    private static Ppu BackgroundScene()
    {
        var ppu = NewPpu(0x01);
        SolidTile(ppu, 1, 3);
        Fill(ppu, 0x3F00, 0x0F, 0x16, 0x27, 0x30);
        Fill(ppu, 0x2004, 1);
        ppu.WriteRegister(0x2001, 0x0A);   // background on, also in the left 8 pixels
        return ppu;
    }

    [Test]
    public void Background_DrawsTilesWithThePalette()
    {
        var ppu = BackgroundScene();
        Scroll(ppu, 0, 0);
        RunFrame(ppu); RunFrame(ppu);
        Assert.That(Pixel(ppu, 32, 0), Is.EqualTo(Ppu.SystemPalette[0x30]));
        Assert.That(Pixel(ppu, 39, 7), Is.EqualTo(Ppu.SystemPalette[0x30]));
        Assert.That(Pixel(ppu, 40, 0), Is.EqualTo(Ppu.SystemPalette[0x0F]), "fuera del tile se ve el color de fondo");
        Assert.That(Pixel(ppu, 32, 8), Is.EqualTo(Ppu.SystemPalette[0x0F]));
    }

    [Test]
    public void Background_FollowsScroll()
    {
        var ppu = BackgroundScene();
        Scroll(ppu, 3, 2);   // 3 to the left, 2 up
        RunFrame(ppu); RunFrame(ppu);
        Assert.That(Pixel(ppu, 29, 0), Is.EqualTo(Ppu.SystemPalette[0x30]));
        Assert.That(Pixel(ppu, 28, 0), Is.EqualTo(Ppu.SystemPalette[0x0F]));
        Assert.That(Pixel(ppu, 29, 5), Is.EqualTo(Ppu.SystemPalette[0x30]));
        Assert.That(Pixel(ppu, 29, 6), Is.EqualTo(Ppu.SystemPalette[0x0F]));
    }

    [Test]
    public void Background_ScrollsIntoTheNextNametable()
    {
        var ppu = BackgroundScene();       // vertical mirroring: $2400 is a different table
        Fill(ppu, 0x2400, 1);
        // scroll the whole screen by 256 - 16: column 0 of $2400 appears at x = 16
        Scroll(ppu, 240, 0);
        RunFrame(ppu); RunFrame(ppu);
        Assert.That(Pixel(ppu, 16, 0), Is.EqualTo(Ppu.SystemPalette[0x30]));
        Assert.That(Pixel(ppu, 15, 0), Is.EqualTo(Ppu.SystemPalette[0x0F]));
    }

    [Test]
    public void Sprites_DrawOneLineBelowTheirY_WithFlipAndPriority()
    {
        var ppu = BackgroundScene();
        // sprite tile 2: only its left column is set (pixel value 1)
        for (int row = 0; row < 8; row++) ppu.Write((ushort)(2 * 16 + row), 0x80);
        Fill(ppu, 0x3F11, 0x21);
        ppu.Oam[4] = 49; ppu.Oam[5] = 2; ppu.Oam[6] = 0x00; ppu.Oam[7] = 100;   // sprite 1
        ppu.Oam[8] = 49; ppu.Oam[9] = 2; ppu.Oam[10] = 0x40; ppu.Oam[11] = 120; // sprite 2 flipped
        ppu.Oam[0] = 0xF0;                                                       // sprite 0 hidden
        ppu.WriteRegister(0x2001, 0x1E);
        Scroll(ppu, 0, 0);
        RunFrame(ppu); RunFrame(ppu);
        Assert.That(Pixel(ppu, 100, 50), Is.EqualTo(Ppu.SystemPalette[0x21]));
        Assert.That(Pixel(ppu, 100, 49), Is.EqualTo(Ppu.SystemPalette[0x0F]), "aparece una línea abajo de su Y");
        Assert.That(Pixel(ppu, 127, 50), Is.EqualTo(Ppu.SystemPalette[0x21]), "espejado horizontal");
        Assert.That(Pixel(ppu, 120, 50), Is.EqualTo(Ppu.SystemPalette[0x0F]));
    }

    [Test]
    public void Sprite0Hit_IsSetWhereSpriteMeetsBackground()
    {
        var ppu = BackgroundScene();
        SolidTile(ppu, 2, 1);
        ppu.Oam[0] = 3; ppu.Oam[1] = 2; ppu.Oam[2] = 0; ppu.Oam[3] = 36;   // overlaps tile 1 at (36, 4)
        ppu.WriteRegister(0x2001, 0x1E);
        Scroll(ppu, 0, 0);
        RunFrame(ppu);
        while (ppu.Line != 4 || ppu.Dot != 30) ppu.Step();
        Assert.That(ppu.ReadRegister(0x2002) & 0x40, Is.Zero, "todavía no llegó al punto");
        while (ppu.Dot != 40) ppu.Step();
        Assert.That(ppu.ReadRegister(0x2002) & 0x40, Is.EqualTo(0x40));
        while (ppu.Line != Ppu.PreRenderLine || ppu.Dot != 2) ppu.Step();
        Assert.That(ppu.ReadRegister(0x2002) & 0x40, Is.Zero, "la línea previa lo borra");
    }

    [Test]
    public void NinthSpriteOnALine_SetsOverflow_AndIsNotDrawn()
    {
        var ppu = BackgroundScene();
        SolidTile(ppu, 2, 1);
        Fill(ppu, 0x3F11, 0x21);
        for (int i = 0; i < 64; i++) ppu.Oam[i * 4] = 0xF0;
        for (int i = 0; i < 9; i++)
        {
            ppu.Oam[i * 4] = 99; ppu.Oam[i * 4 + 1] = 2; ppu.Oam[i * 4 + 3] = (byte)(64 + i * 16);
        }
        ppu.WriteRegister(0x2001, 0x1E);
        Scroll(ppu, 0, 0);
        RunFrame(ppu);
        while (ppu.Line != 101) ppu.Step();
        Assert.That(ppu.ReadRegister(0x2002) & 0x20, Is.EqualTo(0x20));
        Assert.That(Pixel(ppu, 64 + 7 * 16, 100), Is.EqualTo(Ppu.SystemPalette[0x21]));
        Assert.That(Pixel(ppu, 64 + 8 * 16, 100), Is.EqualTo(Ppu.SystemPalette[0x0F]));
    }
}
