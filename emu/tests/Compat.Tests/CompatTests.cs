using Retro.Compat;
using Retro.Systems.Spectrum;

namespace Compatibility.Tests;

/// <summary>The compatibility runner with small programs written for the test (no games needed).</summary>
[TestFixture]
public class CompatTests
{
    private string _games = "", _firmware = "";

    private static string Firmware()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms (48.rom y c64/).");
        return Path.Combine(dir!.FullName, "roms");
    }

    [SetUp]
    public void SetUp()
    {
        _firmware = Firmware();
        _games = Path.Combine(Path.GetTempPath(), "retro-compat-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_games);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_games, true);

    /// <summary>A C64 .prg: 10 SYS 2061, then clear the screen and loop (reading the joystick or not).</summary>
    private static byte[] Program(bool readsJoystick)
    {
        var code = new List<byte>
        {
            0xA2, 0x00, 0xA9, 0x08,                    // LDX #0 / LDA #8: fill the screen with H
            0x9D, 0x00, 0x04, 0x9D, 0x00, 0x05,        // STA $0400,X / STA $0500,X
            0x9D, 0x00, 0x06, 0x9D, 0xE8, 0x06,        // STA $0600,X / STA $06E8,X
            0xE8, 0xD0, 0xF1,                          // INX / BNE (back to the first STA)
            0xA9, 0x08, 0x8D, 0x00, 0x04,              // LDA #8 (H) / STA $0400
        };
        ushort loop = (ushort)(0x080D + code.Count);
        if (readsJoystick)
            code.AddRange(new byte[] { 0xAD, 0x00, 0xDC, 0x29, 0x1F, 0x8D, 0x20, 0xD0, 0x8D, 0x01, 0x04 });  // LDA $DC00 / AND #$1F / STA $D020 / STA $0401
        code.AddRange(new byte[] { 0x4C, (byte)loop, (byte)(loop >> 8) });
        var prg = new List<byte> { 0x01, 0x08, 0x0B, 0x08, 0x0A, 0x00, 0x9E, 0x32, 0x30, 0x36, 0x31, 0x00, 0x00, 0x00 };
        prg.AddRange(code);
        return prg.ToArray();
    }

    private CompatScript Script(string file, params CompatCheck[] checks) => new()
    {
        Id = "prueba", Game = "Prueba", Machine = "c64", File = file, Frames = 100, Curated = true,
        Checks = checks.ToList(), ProbeInputs = new() { "JOY2 RIGHT", "JOY2 FIRE" },
    };

    [Test]
    public void ProgramThatReadsTheJoystick_RespondsToControls()
    {
        File.WriteAllBytes(Path.Combine(_games, "joy.prg"), Program(readsJoystick: true));
        var r = new CompatRunner(_games, _firmware, null).Run(Script("joy.prg",
            new CompatCheck { Level = "title", Frame = 50, Type = "text", Value = "H" }));
        Assert.That(r.Levels["format"].Status, Is.EqualTo("ok"), r.Levels["format"].Detail);
        Assert.That(r.Levels["load"].Status, Is.EqualTo("ok"), r.Levels["load"].Detail);
        Assert.That(r.Levels["picture"].Status, Is.EqualTo("ok"), r.Levels["picture"].Detail);
        Assert.That(r.Levels["title"].Status, Is.EqualTo("ok"), r.Levels["title"].Detail);
        Assert.That(r.Levels["controls"].Status, Is.EqualTo("ok"), r.Levels["controls"].Detail);
        Assert.That(r.Levels["controls"].Detail, Does.Contain("JOY2 RIGHT"));
        Assert.That(r.Sha256, Has.Length.EqualTo(64));
        Assert.That(r.Highest, Is.EqualTo("controls"));
    }

    [Test]
    public void ProgramThatIgnoresInput_DoesNotRespond()
    {
        File.WriteAllBytes(Path.Combine(_games, "quieto.prg"), Program(readsJoystick: false));
        var r = new CompatRunner(_games, _firmware, null).Run(Script("quieto.prg"));
        Assert.That(r.Levels["picture"].Status, Is.EqualTo("ok"), r.Levels["picture"].Detail);
        Assert.That(r.Levels["controls"].Status, Is.EqualTo("fail"), r.Levels["controls"].Detail);
        Assert.That(r.Levels["title"].Status, Is.EqualTo("untested"));
    }

    [Test]
    public void SameScriptTwice_GivesTheSamePicture()
    {
        File.WriteAllBytes(Path.Combine(_games, "joy.prg"), Program(readsJoystick: true));
        var a = new CompatRunner(_games, _firmware, null).Run(Script("joy.prg"));
        var b = new CompatRunner(_games, _firmware, null).Run(Script("joy.prg"));
        Assert.That(a.ScreenHash, Is.Not.Null);
        Assert.That(b.ScreenHash, Is.EqualTo(a.ScreenHash));
    }

    [Test]
    public void FailedChecks_AndMissingFiles_AreReported()
    {
        File.WriteAllBytes(Path.Combine(_games, "joy.prg"), Program(readsJoystick: true));
        var r = new CompatRunner(_games, _firmware, null).Run(Script("joy.prg",
            new CompatCheck { Level = "playable", Type = "memory", Address = "0400", Op = "eq", Value = "$99" }));
        Assert.That(r.Levels["playable"].Status, Is.EqualTo("fail"));
        Assert.That(r.Levels["playable"].Detail, Does.Contain("$0400 = 8,"));
        var missing = new CompatRunner(_games, _firmware, null).Run(Script("no-existe.prg"));
        Assert.That(missing.Levels["format"].Status, Is.EqualTo("fail"));
        Assert.That(missing.Highest, Is.EqualTo(""));
    }

    [Test]
    public void BasicPrompt_IsASignOfFailure()
    {
        // a program that returns at once: the machine is back at READY.
        File.WriteAllBytes(Path.Combine(_games, "rts.prg"), new byte[] { 0x01, 0x08, 0x0B, 0x08, 0x0A, 0x00, 0x9E, 0x32, 0x30, 0x36, 0x31, 0x00, 0x00, 0x00, 0x60 });
        var r = new CompatRunner(_games, _firmware, null).Run(Script("rts.prg"));
        Assert.That(r.Levels["load"].Status, Is.EqualTo("ok"));
        Assert.That(r.Levels["picture"].Status, Is.EqualTo("fail"));
        Assert.That(r.Levels["picture"].Detail, Does.Contain("READY."));
    }

    [Test]
    public void FutureMachineAndFutureFormat_StopAtTheRightLevel()
    {
        File.WriteAllBytes(Path.Combine(_games, "juego.nes"), new byte[] { 0x4E, 0x45, 0x53, 0x1A, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        var nes = new CompatRunner(_games, _firmware, null).Run(new CompatScript { Id = "n", Machine = "nes", File = "juego.nes" });
        Assert.That(nes.Levels["format"].Status, Is.EqualTo("ok"));
        Assert.That(nes.Levels["load"].Status, Is.EqualTo("untested"));
        Assert.That(nes.Problems, Has.Some.Contains("falta la máquina nes"));
        File.WriteAllBytes(Path.Combine(_games, "disco.d64"), new byte[174848]);
        var d64 = new CompatRunner(_games, _firmware, null).Run(new CompatScript { Id = "d", Machine = "c64", File = "disco.d64" });
        Assert.That(d64.Levels["format"].Status, Is.EqualTo("ok"));
        Assert.That(d64.Levels["load"].Status, Is.EqualTo("fail"));
        Assert.That(d64.Levels["load"].Detail, Does.Contain(".d64"));
    }

    [Test]
    public void SpectrumText_ReadsTheRomFont()
    {
        var zx = new Spectrum48(File.ReadAllBytes(Path.Combine(_firmware, "48.rom")));
        for (int i = 0; i < 150; i++)
            zx.RunFrame(null);
        string text = ScreenReader.Text(zx)!;
        Assert.That(text, Does.Contain("© 1982 Sinclair Research Ltd"));
        Assert.That(ScreenReader.FailureSign(zx, text), Does.Contain("pantalla de inicio"));
    }

    [Test]
    public void Discovery_SkipsExcludedTitlesAndKeepsCuratedScripts()
    {
        File.WriteAllBytes(Path.Combine(_games, "Mi_Juego.prg"), Program(true));
        File.WriteAllBytes(Path.Combine(_games, "Fundamentally_Loathsome.prg"), Program(true));
        File.WriteAllText(Path.Combine(_games, "leeme.txt"), "texto");
        string scripts = Path.Combine(_games, "guiones");
        var created = CompatDiscovery.Discover(_games, scripts);
        Assert.That(created.Select(s => s.File), Is.EquivalentTo(new[] { "Mi_Juego.prg" }));
        Assert.That(created[0].Machine, Is.EqualTo("c64"));
        var again = CompatDiscovery.Discover(_games, scripts);
        Assert.That(again, Is.Empty, "no repite ni pisa guiones existentes");
        Assert.That(CompatDiscovery.IsExcluded("descargados/Mojon-Twins--Cheril-Perils.tzx"), Is.True);
    }

    [TestCase("Mojon-Twins--Sir-Ababol--C64", "Sir Ababol")]
    [TestCase("Pac_Man_ZX-Spectrum_ROM_EN", "Pac Man")]
    [TestCase("boulder-dash", "Boulder Dash")]
    [TestCase("mojon-twins--cave-cnrom", "Cave Cnrom")]
    [TestCase("Fase-Bonus-The-Game--The-Mojon-Twins", "Fase Bonus The Game")]
    [TestCase("Spy_vs_Spy_AppleII_EN (1)", "Spy vs Spy")]
    public void GameNames_ComeOutReadable(string file, string expected) =>
        Assert.That(CompatDiscovery.GameName(file), Is.EqualTo(expected));

    [Test]
    public void Report_HasOneSectionPerMachineAndRoundTrips()
    {
        File.WriteAllBytes(Path.Combine(_games, "joy.prg"), Program(true));
        var report = new CompatReport { Generated = "hoy", Commit = "abc", GamesFolder = _games };
        report.Merge(new[] { new CompatRunner(_games, _firmware, null).Run(Script("joy.prg")) });
        string md = report.ToMarkdown();
        Assert.That(md, Does.Contain("## Commodore 64"));
        Assert.That(md, Does.Contain("| Prueba |"));
        string json = Path.Combine(_games, "r.json");
        report.SaveJson(json);
        var back = CompatReport.LoadJson(json);
        Assert.That(back.Results.Single().Levels["controls"].Status, Is.EqualTo("ok"));
    }
}
