using Retro.Compat;

namespace Compatibility.Tests;

/// <summary>
/// The lab's experiments and automatic verification against a tiny C64 game whose
/// variables are known: a BCD score that fire adds to and the screen shows, a player
/// position (shown as a sprite) with a copy the game rewrites every frame, lives, a timer
/// and four enemies in a table of 4-byte records.
/// </summary>
[TestFixture]
public class LabV2Tests
{
    /// <summary>A few 6502 instructions with labels and relative branches.</summary>
    private sealed class Asm
    {
        public readonly List<byte> Code = new();
        private readonly Dictionary<string, int> _labels = new();
        private readonly List<(int at, string label, bool relative)> _fixups = new();
        public int Here => 0x080D + Code.Count;
        public Asm B(params byte[] b) { Code.AddRange(b); return this; }
        public Asm Abs(byte op, int addr) => B(op, (byte)addr, (byte)(addr >> 8));
        public Asm Label(string l) { _labels[l] = Here; return this; }
        public Asm Br(byte op, string l) { _fixups.Add((Code.Count + 1, l, true)); return B(op, 0); }
        public Asm Jmp(string l) { _fixups.Add((Code.Count + 1, l, false)); return B(0x4C, 0, 0); }
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

    private const byte LdaImm = 0xA9, Sta = 0x8D, Lda = 0xAD, Inc = 0xEE, Dec = 0xCE, AndImm = 0x29, CmpImm = 0xC9, Cmp = 0xCD,
        OraImm = 0x09, AdcImm = 0x69, Bne = 0xD0, Beq = 0xF0, Bmi = 0x30, Bcc = 0x90, Bcs = 0xB0, Bpl = 0x10, LdaX = 0xBD, StaX = 0x9D, AdcX = 0x7D, SbcX = 0xFD;

    private static byte[] Game()
    {
        var a = new Asm();
        a.B(0x78);                                                           // SEI
        a.B(LdaImm, 0xFF).Abs(Sta, 0xDC00);
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
        a.B(LdaImm, 0xFF, 0xA2, 0x3E).Label("fill").Abs(StaX, 0x0340).B(0xCA).Br(Bpl, "fill");
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
        return a.Prg();
    }

    private string _games = "";
    private Retro.Core.IMachine _m = null!;

    [SetUp]
    public void SetUp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms.");
        _games = Path.Combine(Path.GetTempPath(), "retro-lab2-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_games);
        File.WriteAllBytes(Path.Combine(_games, "juego.prg"), Game());
        var runner = new CompatRunner(_games, Path.Combine(dir!.FullName, "roms"), null);
        var (m, _, problem) = runner.Start(new CompatScript { Id = "j", Machine = "c64", File = "juego.prg", Frames = 20 });
        Assert.That(m, Is.Not.Null, problem);
        _m = m!;
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_games, true);

    private static readonly LabExperiment Shooting = new()
    {
        Kind = "score", Description = "disparar cada 20 cuadros", Frames = 400, RepeatEvery = 20,
        Steps = { new CompatStep { Frame = 0, Keys = "JOY2 FIRE", Hold = 4 } },
    };

    [Test]
    public void ScoreExperiment_FindsTheBcdScore_AndVerifiesItOnScreen()
    {
        var found = LabExperiments.Analyze(_m, "c64", Shooting);
        var score = found.FirstOrDefault(f => f.Address == "C020")
            ?? throw new AssertionException("no encontró el puntaje: " + string.Join("; ", found.Select(f => $"{f.Address} {f.Encoding}")));
        Assert.That((score.Encoding, score.Bytes), Is.EqualTo(("bcd", 2)));
        Assert.That(score.Evidence[0], Does.Contain("de 0 a 200"));

        var v = LabVerifier.Verify(_m, score, Shooting);
        Assert.That(v.Result, Is.EqualTo("verified"), string.Join("; ", v.Observations));
        Assert.That(v.Observations, Has.Some.Contains("el texto de la pantalla muestra"));
        Assert.That(v.Observations, Has.Some.Contains("siguió cambiándolo"));

        // the digits on the screen also go up, but the game redraws them from the real score
        var shown = found.FirstOrDefault(f => f.Address == "0400" && f.Encoding == "ascii");
        Assert.That(shown, Is.Not.Null, "los dígitos de la pantalla también son candidatos");
        Assert.That(LabVerifier.Verify(_m, shown!, Shooting).Result, Is.EqualTo("refuted"));
    }

    [Test]
    public void Verification_TellsTheStoredPositionFromTheCopyTheGameRewrites()
    {
        var report = MemoryLab.Analyze(_m, "c64", new MemoryLab.Options(IdleFrames: 1300));
        var x = report.Findings.First(f => f.Kind == "position-x" && f.Address == "C001");
        Assert.That(LabVerifier.Verify(_m, x).Result, Is.EqualTo("verified"), string.Join("; ", x.Verification!.Observations));
        Assert.That(x.Verification!.PixelsChanged, Is.GreaterThan(0), "el sprite se mueve");

        var copy = new LabFinding { Kind = "position-x", Address = "C005" };
        Assert.That(LabVerifier.Verify(_m, copy).Result, Is.EqualTo("refuted"));
        Assert.That(copy.Nature, Is.EqualTo("derived"));

        var lives = report.Findings.First(f => f.Kind == "lives" && f.Address == "C002");
        Assert.That(LabVerifier.Verify(_m, lives).Result, Is.EqualTo("verified"), string.Join("; ", lives.Verification!.Observations));
        Assert.That(lives.Verification!.Observations, Has.Some.Contains("siguió cambiándolo"), "el juego descuenta desde el valor escrito");
    }

    [Test]
    public void EntityTable_IsToldApartFromTheLooseVariable()
    {
        var report = MemoryLab.Analyze(_m, "c64", new MemoryLab.Options(IdleFrames: 1300));
        var table = report.Findings.FirstOrDefault(f => f.Kind == "entity-table" && f.Address == "C010")
            ?? throw new AssertionException("no encontró la tabla: " + string.Join("; ", report.Findings.Where(f => f.Kind == "entity-table").Select(f => $"{f.Address} x{f.Count}/{f.Stride}")));
        Assert.That((table.Stride, table.Count), Is.EqualTo((4, 4)));
        Assert.That(report.Findings.Count(f => f.Kind == "entity-table"), Is.EqualTo(1), "una sola tabla");
        var x = report.Findings.First(f => f.Kind == "position-x" && f.Address == "C001");
        Assert.That(x.Evidence, Has.Some.Contains("variable suelta"));

        LabVerifier.VerifyBest(_m, report);
        Assert.That(table.Status, Is.EqualTo("verified"), string.Join("; ", table.Verification!.Observations));
    }
}
