using Retro.Asm;
using Retro.Compat;

namespace Compat.Tests;

/// <summary>
/// The trial a game written from scratch goes through: the templates pass it, and each kind
/// of broken game fails the check that tells what to fix.
/// </summary>
[TestFixture]
public class HomebrewTrialTests
{
    private string _firmware = "";

    [OneTimeSetUp]
    public void FindFirmware()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms (48.rom y c64/).");
        _firmware = Path.Combine(dir!.FullName, "roms");
    }

    private static string C64 => Templates.For("c64")!.Source;
    private static string Zx => Templates.For("zx48")!.Source;

    private static string Change(string source, string from, string to)
    {
        Assert.That(source, Does.Contain(from));
        return source.Replace(from, to);
    }

    private static TrialCheck Check(TrialReport r, string name) => r.Checks.Single(c => c.Name == name);

    [Test]
    public void TheTemplates_PassTheTrial()
    {
        foreach (var t in Templates.All)
        {
            var r = HomebrewTrial.Run(t.Source, t.Machine, _firmware);
            Assert.That(r.Ok, Is.True, $"{t.Machine}:\n{r.Summary}");
            Assert.That(r.StartKey, Is.Null, "se juega desde el principio");
            Assert.That(r.Controls!.Name, Is.EqualTo(t.Machine == "c64" ? "joystick en el puerto 2" : "joystick Kempston"));
            Assert.That(Check(r, "sonido").Ok, Is.True, "el disparo del robot suena");
            Assert.That(r.LastPicture, Is.Not.Null);
            Assert.That(r.Summary, Does.Contain("RETROEMU"), "el informe trae el texto de la pantalla");
        }
    }

    /// <summary>The example games (written only from the machine guides) are real games: they pass too.</summary>
    [Test]
    public void TheExampleGames_PassTheTrial()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Retro.Asm", "Examples")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null);
        var examples = Directory.GetFiles(Path.Combine(dir!.FullName, "src", "Retro.Asm", "Examples"), "*.s");
        Assert.That(examples, Has.Length.GreaterThanOrEqualTo(2));
        foreach (var path in examples)
        {
            string machine = Path.GetFileNameWithoutExtension(path).Split('-')[^1];
            var r = HomebrewTrial.Run(File.ReadAllText(path), machine, _firmware);
            Assert.That(r.Ok, Is.True, $"{Path.GetFileName(path)}:\n{r.Summary}");
            Assert.That(r.Checks.All(c => c.Ok), Is.True, $"{Path.GetFileName(path)} también suena:\n{r.Summary}");
        }
    }

    [Test]
    public void AMistake_ComesBackWithTheLineItPointsAt()
    {
        var r = HomebrewTrial.Run(Change(C64, "inc $D020", "inx $D020"), "c64", _firmware);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Checks, Has.Count.EqualTo(1));
        Assert.That(r.Checks[0].Detail, Does.Contain("línea").And.Contain("inx $D020"));
    }

    [Test]
    public void AProgramThatGoesBackToBasic_DoesNotStart()
    {
        var r = HomebrewTrial.Run(Change(C64, "start:  sei", "start:  rts"), "c64", _firmware);
        var started = Check(r, "arranca");
        Assert.That(started.Ok, Is.False);
        Assert.That(started.Detail, Does.Contain("nunca estuvo en tu código"));
    }

    [Test]
    public void AGameThatIgnoresTheControls_FailsTheControlCheck()
    {
        var r = HomebrewTrial.Run(Change(C64, "lda $DC00", "lda #$FF"), "c64", _firmware);
        Assert.That(Check(r, "arranca").Ok && Check(r, "dibuja").Ok, Is.True, r.Summary);
        Assert.That(Check(r, "controles").Ok, Is.False);
        Assert.That(r.Ok, Is.False);
    }

    [Test]
    public void ATitleScreen_IsLeftWithFire()
    {
        string title = Change(C64, "loop:   ; una vuelta por cuadro", """
            @press: lda $DC00           ; título: esperar el disparo
                    and #$10
                    bne @press
            loop:   ; una vuelta por cuadro
            """);
        var r = HomebrewTrial.Run(title, "c64", _firmware);
        Assert.That(r.Ok, Is.True, r.Summary);
        Assert.That(r.StartKey, Is.EqualTo("JOY2 FIRE"));
        Assert.That(Check(r, "controles").Detail, Does.StartWith("después de apretar JOY2 FIRE"));
    }

    [Test]
    public void AGameThatCrashesWhilePlaying_IsCaughtByTheRobot()
    {
        var r = HomebrewTrial.Run(Change(Zx, "@beep:  ld b,40", "@beep:  rst 0"), "zx48", _firmware);
        Assert.That(Check(r, "controles").Ok, Is.True, r.Summary);
        var robot = Check(r, "jugador robot");
        Assert.That(robot.Ok, Is.False);
        Assert.That(robot.Detail, Does.Contain("pantalla de inicio"));
    }
}
