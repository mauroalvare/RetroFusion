using Retro.Asm;
using Retro.Cpu.Z80;
using Retro.Systems.Spectrum;

namespace Asm.Tests;

[TestFixture]
public class AsmZ80Tests
{
    private const uint At = 0x8000;

    private static string Dis(byte[] bytes) =>
        new Z80Disassembler().Disassemble(a => a - At < bytes.Length ? bytes[a - At] : (byte)0, At).Text;

    /// <summary>Every encoding (plain, CB, ED, DD/FD, DDCB/FDCB), disassembled and assembled again, reads the same.</summary>
    [Test]
    public void EveryEncoding_SurvivesTheRoundTripThroughTheDisassembler()
    {
        var encodings = new List<byte[]>();
        var prefixes = new HashSet<int> { 0xCB, 0xDD, 0xED, 0xFD };
        for (int op = 0; op < 256; op++)
        {
            if (!prefixes.Contains(op)) encodings.Add(new byte[] { (byte)op, 0x34, 0x12 });
            encodings.Add(new byte[] { 0xCB, (byte)op });
            encodings.Add(new byte[] { 0xED, (byte)op, 0x34, 0x12 });
            foreach (byte ix in new byte[] { 0xDD, 0xFD })
            {
                if (!prefixes.Contains(op)) encodings.Add(new byte[] { ix, (byte)op, 0x05, 0x12 });
                encodings.Add(new byte[] { ix, 0xCB, 0xF9, (byte)op });
            }
        }
        int checkedForms = 0;
        foreach (var bytes in encodings)
        {
            string text = Dis(bytes);
            if (text.Contains('*') || text.Contains('/'))
                continue;
            var r = AsmZ80.Assemble($" org ${At:X4}\n {text}\n");
            Assert.That(r.Ok, Is.True, $"{Convert.ToHexString(bytes)} \"{text}\": {string.Join("; ", r.Errors)}");
            Assert.That(Dis(r.Bytes), Is.EqualTo(text), Convert.ToHexString(bytes));
            checkedForms++;
        }
        Assert.That(checkedForms, Is.GreaterThan(1200));
    }

    [Test]
    public void TheUsualForms()
    {
        var r = AsmZ80.Assemble("""
                    org 8000h
            screen  equ $4000
            start:  ld hl,screen
                    ld (hl),$FF
                    ld a,(ix+5)
                    ld (iy-2),a
                    ld (ix),10
                    ex af,af'
                    out ($FE),a
                    in a,(c)
                    rst $38
                    im 1
                    bit 7,(hl)
                    set 0,(ix+1)
            @loop:  djnz @loop
                    jr nz,start
                    jp (hl)
                    call start
                    ld a,'A'
            text:   db "AB", 0
            words:  dw start, $
                    ds 2, $EA
            """);
        Assert.That(r.Ok, Is.True, string.Join("; ", r.Errors));
        Assert.That(r.Origin, Is.EqualTo(0x8000));
        var b = r.Bytes;
        Assert.That(b[..3], Is.EqualTo(new byte[] { 0x21, 0x00, 0x40 }), "LD HL,nn");
        Assert.That(b[3..5], Is.EqualTo(new byte[] { 0x36, 0xFF }), "LD (HL),n");
        Assert.That(b[5..8], Is.EqualTo(new byte[] { 0xDD, 0x7E, 0x05 }), "LD A,(IX+d)");
        Assert.That(b[8..11], Is.EqualTo(new byte[] { 0xFD, 0x77, 0xFE }), "LD (IY-2),A");
        Assert.That(b[11..15], Is.EqualTo(new byte[] { 0xDD, 0x36, 0x00, 0x0A }), "LD (IX),n");
        Assert.That(b[15], Is.EqualTo(0x08), "EX AF,AF'");
        Assert.That(b[16..18], Is.EqualTo(new byte[] { 0xD3, 0xFE }), "OUT (n),A");
        Assert.That(b[18..20], Is.EqualTo(new byte[] { 0xED, 0x78 }), "IN A,(C)");
        Assert.That(b[20], Is.EqualTo(0xFF), "RST $38");
        Assert.That(b[21..23], Is.EqualTo(new byte[] { 0xED, 0x56 }), "IM 1");
        Assert.That(b[23..25], Is.EqualTo(new byte[] { 0xCB, 0x7E }), "BIT 7,(HL)");
        Assert.That(b[25..29], Is.EqualTo(new byte[] { 0xDD, 0xCB, 0x01, 0xC6 }), "SET 0,(IX+1)");
        Assert.That(b[29..31], Is.EqualTo(new byte[] { 0x10, 0xFE }), "DJNZ to itself");
        Assert.That(b[31..33], Is.EqualTo(new byte[] { 0x20, 0xDF }), "JR NZ back to start: $8000 - ($801F + 2)");
        Assert.That(r.Symbols["start@loop"], Is.EqualTo(0x801D));
        int t = r.Symbols["text"] - 0x8000, w = r.Symbols["words"] - 0x8000;
        Assert.That(b[t..(t + 3)], Is.EqualTo(new byte[] { (byte)'A', (byte)'B', 0 }));
        Assert.That(b[w..(w + 4)], Is.EqualTo(new byte[] { 0x00, 0x80, (byte)w, 0x80 }), "DW start, $ ($ is where the line starts)");
    }

    [Test]
    public void MistakesSayWhereAndWhat()
    {
        var r = AsmZ80.Assemble(" ld a,b\n ld b,(de)\n");
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Errors[0].Line, Is.EqualTo(2));
        Assert.That(r.Errors[0].Message, Does.Contain("no es una forma válida"));
        r = AsmZ80.Assemble(" org $8000\n jr far\n ds 200\nfar: ret\n");
        Assert.That(r.Errors[0].Message, Does.Contain("fuera de alcance"));
        r = AsmZ80.Assemble(" ld a,(ix+200)\n");
        Assert.That(r.Errors[0].Message, Does.Contain("desplazamiento"));
    }

    /// <summary>A program written in Z80 assembly prints through the Spectrum ROM (RST $10) on the emulated machine.</summary>
    [Test]
    public void AnAssembledProgram_PrintsOnTheSpectrum()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms.");
        var r = AsmZ80.Assemble("""
                    org $8000
                    ld a,2
                    call $1601      ; open the upper screen channel
                    ld hl,msg
            @next:  ld a,(hl)
                    or a
                    jr z,@done
                    rst $10         ; print the character in A
                    inc hl
                    jr @next
            @done:  jr @done
            msg:    db "HOLA Z80", 0
            """);
        Assert.That(r.Ok, Is.True, string.Join("; ", r.Errors));
        var zx = new Spectrum48(File.ReadAllBytes(Path.Combine(dir!.FullName, "roms", "48.rom")));
        var sink = new short[8192];
        void Run(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                zx.RunFrame(null);
                zx.DrainAudio(sink);
            }
        }
        Run(150);   // to the copyright screen
        for (int i = 0; i < r.Bytes.Length; i++)
            zx.Memory.Poke((uint)(r.Origin + i), r.Bytes[i]);
        zx.Cpu.ProgramCounter = r.Origin;
        Run(20);
        Assert.That(ScreenText.ReadAll(zx), Does.Contain("HOLA Z80"));
    }
}
