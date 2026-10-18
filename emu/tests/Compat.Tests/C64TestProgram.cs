namespace Compatibility.Tests;

/// <summary>A few 6502 instructions with labels and relative branches, assembled at $080D behind a BASIC SYS line.</summary>
internal sealed class C64Asm
{
    public readonly List<byte> Code = new();
    private readonly Dictionary<string, int> _labels = new();
    private readonly List<(int at, string label, bool relative)> _fixups = new();
    private int _unique;
    public int Here => 0x080D + Code.Count;
    public C64Asm B(params byte[] b) { Code.AddRange(b); return this; }
    public C64Asm Abs(byte op, int addr) => B(op, (byte)addr, (byte)(addr >> 8));
    public C64Asm Label(string l) { _labels[l] = Here; return this; }
    public C64Asm Br(byte op, string l) { _fixups.Add((Code.Count + 1, l, true)); return B(op, 0); }
    public C64Asm Jmp(string l) { _fixups.Add((Code.Count + 1, l, false)); return B(0x4C, 0, 0); }
    public string Unique(string name) => $"{name}{_unique++}";

    public byte[] Prg()
    {
        foreach (var (at, label, relative) in _fixups)
        {
            int target = _labels[label];
            if (relative)
            {
                int d = target - (0x080D + at + 1);
                Assert.That(d, Is.InRange(-128, 127), $"salto a {label} fuera de rango");
                Code[at] = (byte)d;
            }
            else
            {
                Code[at] = (byte)target;
                Code[at + 1] = (byte)(target >> 8);
            }
        }
        var prg = new List<byte> { 0x01, 0x08, 0x0B, 0x08, 0x0A, 0x00, 0x9E, 0x32, 0x30, 0x36, 0x31, 0x00, 0x00, 0x00 };
        prg.AddRange(Code);
        return prg.ToArray();
    }
}

/// <summary>
/// The tiny C64 game of the lab tests, optionally behind screens that wait for keys (a
/// cracker's trainer question, "press fire", "type G"), like the real editions.
/// </summary>
internal static class C64TestProgram
{
    public const byte LdaImm = 0xA9, Sta = 0x8D, Lda = 0xAD, Inc = 0xEE, Dec = 0xCE, AndImm = 0x29, CmpImm = 0xC9, Cmp = 0xCD,
        OraImm = 0x09, AdcImm = 0x69, Bne = 0xD0, Beq = 0xF0, Bmi = 0x30, Bcc = 0x90, Bcs = 0xB0, Bpl = 0x10, LdaX = 0xBD, StaX = 0x9D, AdcX = 0x7D, SbcX = 0xFD;

    /// <summary>A screen that waits: shows <paramref name="text"/> and waits for a key of the keyboard matrix (column mask written to $DC00, row bit read from $DC01), or for joystick 2 fire when the mask is 0.</summary>
    public sealed record Gate(string Text, byte Column, byte RowBit);

    public static readonly Gate TrainerQuestion = new("TRAINER? Y/N", 0xEF, 0x80);   // N: column 4, row 7
    public static readonly Gate PressFire = new("PRESS FIRE", 0, 0);
    public static readonly Gate TypeG = new("TYPE G TO START", 0xF7, 0x04);           // G: column 3, row 2

    public static byte[] Game(params Gate[] gates)
    {
        var a = new C64Asm();
        a.B(0x78);                                                           // SEI
        a.B(LdaImm, 0xFF).Abs(Sta, 0xDC02).B(LdaImm, 0x00).Abs(Sta, 0xDC03);  // port A out, port B in
        a.B(LdaImm, 0xFF).Abs(Sta, 0xDC00);
        foreach (var g in gates)
        {
            ClearScreen(a);
            for (int i = 0; i < g.Text.Length; i++)
                a.B(LdaImm, ScreenCode(g.Text[i])).Abs(Sta, 0x0400 + 40 * 10 + i);
            string wait = a.Unique("gate");
            a.Label(wait);
            if (g.Column == 0)
                a.Abs(Lda, 0xDC00).B(AndImm, 0x10).Br(Bne, wait);           // joystick 2 fire
            else
            {
                a.B(LdaImm, g.Column).Abs(Sta, 0xDC00).Abs(Lda, 0xDC01).B(AndImm, g.RowBit).Br(Bne, wait);
                a.B(LdaImm, 0xFF).Abs(Sta, 0xDC00);
                // and released, so the next screen does not see it
                string up = a.Unique("up");
                a.Label(up).B(LdaImm, g.Column).Abs(Sta, 0xDC00).Abs(Lda, 0xDC01).B(AndImm, g.RowBit).Br(Beq, up);
                a.B(LdaImm, 0xFF).Abs(Sta, 0xDC00);
            }
        }
        if (gates.Length > 0)
            ClearScreen(a);
        EmitGame(a);
        return a.Prg();
    }

    private static byte ScreenCode(char c) => c switch
    {
        >= 'A' and <= 'Z' => (byte)(c - 'A' + 1),
        >= '0' and <= '9' => (byte)c,
        ' ' => 32,
        _ => (byte)c,
    };

    private static void ClearScreen(C64Asm a)
    {
        string fill = a.Unique("fill");
        a.B(LdaImm, 0x20, 0xA2, 0x00).Label(fill)
            .Abs(StaX, 0x0400).Abs(StaX, 0x0500).Abs(StaX, 0x0600).Abs(StaX, 0x06E8).B(0xE8).Br(Bne, fill);
    }

    /// <summary>
    /// The game: joystick 2 moves x ($C001, shown as sprite 0; $C005 is a copy rewritten every
    /// frame), fire adds 10 to a BCD score ($C020-$C021) shown as four digits, a timer
    /// ($C000) takes a life ($C002) every 512 frames, and four enemies in 4-byte records at
    /// $C010 bounce at their own speeds.
    /// </summary>
    public static void EmitGame(C64Asm a)
    {
        a.B(LdaImm, 0x40).Abs(Sta, 0xC000);                                  // timer = 64
        a.B(LdaImm, 0x80).Abs(Sta, 0xC001);                                  // x = 128
        a.B(LdaImm, 0x03).Abs(Sta, 0xC002);                                  // lives = 3
        a.B(LdaImm, 0x10).Abs(Sta, 0xC004);                                  // fire released
        a.B(LdaImm, 0x00).Abs(Sta, 0xC020).Abs(Sta, 0xC021);                 // score = 0000 (BCD)
        byte[] xs = { 0x14, 0x3C, 0x64, 0x8C }, speeds = { 1, 2, 1, 3 };
        for (int e = 0; e < 4; e++)
        {
            a.B(LdaImm, xs[e]).Abs(Sta, 0xC010 + e * 4);                     // enemy x
            a.B(LdaImm, 0x01).Abs(Sta, 0xC011 + e * 4);                      // direction
            a.B(LdaImm, 0x07).Abs(Sta, 0xC012 + e * 4);                      // kind
            a.B(LdaImm, speeds[e]).Abs(Sta, 0xC013 + e * 4);                 // speed
        }
        a.B(LdaImm, 0x0D).Abs(Sta, 0x07F8);                                  // sprite 0 data at $0340
        a.B(LdaImm, 0xFF, 0xA2, 0x3E).Label("sprite").Abs(StaX, 0x0340).B(0xCA).Br(Bpl, "sprite");
        a.B(LdaImm, 0x01).Abs(Sta, 0xD015).Abs(Sta, 0xD027);
        a.B(LdaImm, 0x80).Abs(Sta, 0xD001);
        a.Label("loop");
        a.Abs(Lda, 0xD012).B(CmpImm, 0xF8).Br(Bne, "loop");                 // wait for line $F8
        a.Label("pass").Abs(Lda, 0xD012).B(CmpImm, 0xF8).Br(Beq, "pass");   // and for it to pass
        a.Abs(Inc, 0xC003);                                                  // frames++
        a.Abs(Lda, 0xDC00).B(AndImm, 0x04).B(Bne, 0x03).Abs(Dec, 0xC001);    // left: x--
        a.Abs(Lda, 0xDC00).B(AndImm, 0x08).B(Bne, 0x03).Abs(Inc, 0xC001);    // right: x++
        a.Abs(Lda, 0xC001).Abs(Sta, 0xD000).Abs(Sta, 0xC005);                // sprite x and a copy rewritten every frame
        // fire (on press, not while held): score += 10 in BCD
        a.Abs(Lda, 0xDC00).B(AndImm, 0x10).Abs(Cmp, 0xC004).Br(Beq, "nofire").Abs(Sta, 0xC004).B(CmpImm, 0x00).Br(Bne, "nofire");
        a.B(0xF8, 0x18).Abs(Lda, 0xC020).B(AdcImm, 0x10).Abs(Sta, 0xC020).Abs(Lda, 0xC021).B(AdcImm, 0x00).Abs(Sta, 0xC021).B(0xD8);
        a.Label("nofire");
        // the score as four screen-code digits at the top left, the lives next to it
        a.Abs(Lda, 0xC021).B(0x4A, 0x4A, 0x4A, 0x4A).B(OraImm, 0x30).Abs(Sta, 0x0400);
        a.Abs(Lda, 0xC021).B(AndImm, 0x0F).B(OraImm, 0x30).Abs(Sta, 0x0401);
        a.Abs(Lda, 0xC020).B(0x4A, 0x4A, 0x4A, 0x4A).B(OraImm, 0x30).Abs(Sta, 0x0402);
        a.Abs(Lda, 0xC020).B(AndImm, 0x0F).B(OraImm, 0x30).Abs(Sta, 0x0403);
        a.Abs(Lda, 0xC002).B(OraImm, 0x30).Abs(Sta, 0x0410);
        // enemies: x += speed (or -= speed) and turn at $10 and $F0
        a.B(0xA2, 0x00).Label("enemy");
        a.Abs(LdaX, 0xC011).Br(Bmi, "back");
        a.Abs(LdaX, 0xC010).B(0x18).Abs(AdcX, 0xC013).Abs(StaX, 0xC010).B(CmpImm, 0xF0).Br(Bcc, "next");
        a.B(LdaImm, 0xFF).Abs(StaX, 0xC011).Jmp("next");
        a.Label("back").Abs(LdaX, 0xC010).B(0x38).Abs(SbcX, 0xC013).Abs(StaX, 0xC010).B(CmpImm, 0x10).Br(Bcs, "next");
        a.B(LdaImm, 0x01).Abs(StaX, 0xC011);
        a.Label("next").B(0x8A, 0x18).B(AdcImm, 0x04).B(0xAA).B(0xE0, 0x10).Br(Bne, "enemy");
        // every 8 frames the timer goes down; at 0 a life is lost and it refills
        a.Abs(Lda, 0xC003).B(AndImm, 0x07).Br(Beq, "tick").Jmp("loop");
        a.Label("tick").Abs(Dec, 0xC000).Br(Beq, "dead").Jmp("loop");
        a.Label("dead").Abs(Dec, 0xC002).B(LdaImm, 0x40).Abs(Sta, 0xC000).Jmp("loop");
    }
}
