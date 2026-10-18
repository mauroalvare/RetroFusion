using Retro.Core;
using Retro.Fusion;

namespace Fusion.Tests;

/// <summary>The first C64 profile: found by the lab, verified by writing memory, booted like a fusion does.</summary>
[TestFixture]
[Category("Compat")]
public class C64ProfileTests
{
    [Test]
    public void PacManC64_BootsToTheMaze_AndItsLivesAreReadable()
    {
        string? romDir = null;
        GameSource? source = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && source == null; d = d.Parent)
        {
            if (romDir == null && File.Exists(Path.Combine(d.FullName, "roms", "48.rom")))
                romDir = Path.Combine(d.FullName, "roms");
            string zip = Path.Combine(d.FullName, "ROMs", "Pac_Man_C64_ROM_EN.zip");
            if (File.Exists(zip))
            {
                var files = MediaArchive.Open(zip);
                var profile = ProfileLibrary.ForFiles(files);
                Assert.That(profile?.Id, Is.EqualTo("pacman-c64"));
                source = new GameSource(profile!, files, Path.GetDirectoryName(zip));
            }
        }
        if (romDir == null || source == null)
            Assert.Ignore("Hace falta ROMs/Pac_Man_C64_ROM_EN.zip.");
        var m = MachineFactory.Create(source!, romDir!);
        MachineFactory.Boot(m, source!.Profile);
        var c64 = (Retro.Systems.C64.C64)m;
        Assert.That(c64.ScreenText(), Does.Contain("AAAAAAAAAAAAAAAA"), "el laberinto en pantalla");
        Assert.That(source.Profile.Variables["lives"].Read(m.Memory), Is.EqualTo(2));
        Assert.That(source.Profile.Variables["x"].ReadOnly, Is.True);
    }
}
