using Retro.Asm;
using Retro.Core;
using Retro.Cpu.M6502;
using Retro.Machines;

namespace Asm.Tests;

[TestFixture]
public class Asm6502Tests
{
    private static string Dis(byte[] bytes, uint at) =>
        new M6502Disassembler().Disassemble(a => a - at < bytes.Length ? bytes[a - at] : (byte)0, at).Text;

    /// <summary>
    /// Every opcode, disassembled and assembled again, gives back its instruction: the same
    /// bytes for the official ones, and the official twin (the same instruction) for the
    /// undocumented duplicates such as SBC #$EB or the extra NOPs.
    /// </summary>
    [Test]
    public void EveryOpcode_SurvivesTheRoundTripThroughTheDisassembler()
    {
        const uint at = 0xC000;
        int exact = 0;
        for (int op = 0; op < 256; op++)
        {
            var (name, mode) = M6502Disassembler.Table[op];
            if (name == "JAM")
                continue;
            var bytes = new byte[] { (byte)op, 0x34, 0x12 }[..M6502Disassembler.Length(mode)];
            if (mode == M6502Mode.Rel)
                bytes[1] = 0x10;
            string text = Dis(bytes, at);
            var r = Asm6502.Assemble($".org ${at:X4}\n{text}\n");
            Assert.That(r.Ok, Is.True, $"${op:X2} \"{text}\": {string.Join("; ", r.Errors)}");
            Assert.That(Dis(r.Bytes, at), Is.EqualTo(text), $"${op:X2}");
            if (r.Bytes.SequenceEqual(bytes))
                exact++;
        }
        Assert.That(exact, Is.GreaterThanOrEqualTo(151 + 60), "los 151 oficiales y casi todos los no documentados dan los mismos bytes");
    }

    [Test]
    public void LabelsConstantsAndDirectives()
    {
        var r = Asm6502.Assemble("""
                    .org $C000
            screen = $0400
            start:  ldx #0
            @loop:  lda message,x
                    beq @done
                    sta screen,x
                    inx
                    bne @loop
            @done:  rts
            message: .byte "HI", 0
            table:  .word start, message
                    .byte <table, >table
                    .fill 3, $EA
            other:  lda $10        ; zero page
                    lda a:$10      ; forced absolute
                    lda $0010      ; four digits: absolute
                    jmp (table)
            """);
        Assert.That(r.Ok, Is.True, string.Join("; ", r.Errors));
        Assert.That(r.Origin, Is.EqualTo(0xC000));
        Assert.That(r.Symbols["start"], Is.EqualTo(0xC000));
        Assert.That(r.Symbols["start@loop"], Is.EqualTo(0xC002), "las etiquetas @ son locales a la última global");
        int msg = r.Symbols["message"];
        Assert.That(r.Bytes[msg - 0xC000], Is.EqualTo((byte)'H'));
        int table = r.Symbols["table"] - 0xC000;
        Assert.That(r.Bytes[table..(table + 4)], Is.EqualTo(new byte[] { 0x00, 0xC0, (byte)msg, (byte)(msg >> 8) }));
        Assert.That(r.Bytes[table + 4], Is.EqualTo((byte)(table + 0xC000)));
        int other = r.Symbols["other"] - 0xC000;
        Assert.That(r.Bytes[other..(other + 2)], Is.EqualTo(new byte[] { 0xA5, 0x10 }));
        Assert.That(r.Bytes[(other + 2)..(other + 5)], Is.EqualTo(new byte[] { 0xAD, 0x10, 0x00 }));
        Assert.That(r.Bytes[(other + 5)..(other + 8)], Is.EqualTo(new byte[] { 0xAD, 0x10, 0x00 }));
        Assert.That(r.Listing, Has.Some.Contains("C000"));
    }

    [Test]
    public void ForwardReferencesKeepTheSizeOfThePassOne()
    {
        // "zp" is defined after its use: pass 1 assumes absolute, and pass 2 keeps it so labels do not move
        var r = Asm6502.Assemble(".org $1000\nlda zp\nafter: rts\nzp = $20\n");
        Assert.That(r.Ok, Is.True, string.Join("; ", r.Errors));
        Assert.That(r.Bytes, Is.EqualTo(new byte[] { 0xAD, 0x20, 0x00, 0x60 }));
        Assert.That(r.Symbols["after"], Is.EqualTo(0x1003));
    }

    [Test]
    public void MistakesSayWhereAndWhat()
    {
        var r = Asm6502.Assemble("nop\nfoo #1\nlda nowhere\n");
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Errors.Select(e => e.Line), Is.EqualTo(new[] { 2 }), "la instrucción desconocida corta en la primera pasada");
        Assert.That(r.Errors[0].Message, Does.Contain("FOO"));
        r = Asm6502.Assemble("lda nowhere\n");
        Assert.That(r.Errors[0].ToString(), Does.Contain("línea 1").And.Contain("nowhere"));
        r = Asm6502.Assemble(".org $1000\nbeq far\n.fill 200\nfar: rts\n");
        Assert.That(r.Errors[0].Message, Does.Contain("fuera de alcance"));
    }

    /// <summary>A program written in assembly runs on the emulated C64: prints a word and loops.</summary>
    [Test]
    public void AnAssembledProgram_RunsOnTheC64()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms.");
        var r = Asm6502.Assemble("""
                    .org $0801
                    .word next, 10          ; BASIC: 10 SYS 2061
                    .byte $9E, "2061", 0
            next:   .word 0
            start:  ldx #0
            @copy:  lda text,x
                    beq @loop
                    sta $0400 + 40*5,x
                    inx
                    bne @copy
            @loop:  inc $D020
                    jmp @loop
            text:   .byte 8, 15, 12, 1, 0   ; "HOLA" in screen codes
            """);
        Assert.That(r.Ok, Is.True, string.Join("; ", r.Errors));
        Assert.That(r.Symbols["start"], Is.EqualTo(2061), "el SYS apunta al código");
        var prg = new byte[] { (byte)r.Origin, (byte)(r.Origin >> 8) }.Concat(r.Bytes).ToArray();
        var launch = MachineLauncher.Launch("c64", new[] { new MediaFile("hola.prg", prg, "hola.prg") }, null, Path.Combine(dir!.FullName, "roms"));
        var m = launch.Machine;
        var sink = new short[8192];
        for (int i = 0; i < 120; i++)
        {
            m.RunFrame(null);
            m.DrainAudio(sink);
        }
        string text = ((Retro.Systems.C64.C64)m).ScreenText() ?? "";
        Assert.That(text, Does.Contain("HOLA"));
    }
}
