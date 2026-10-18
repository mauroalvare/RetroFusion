using Retro.Core;
using Retro.Fusion;

namespace Fusion.Tests;

/// <summary>
/// The built-in fusion with the real games (skipped if they are not in ROMs/):
/// both machines boot, run together, and the cross-game rules act on real events.
/// </summary>
[TestFixture]
[Category("Compat")]
public class FusionPlayTests
{
    internal static (Dictionary<string, GameSource> sources, string romDir)? FindGames()
    {
        string? romDir = null;
        var sources = new Dictionary<string, GameSource>();
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (romDir == null && File.Exists(Path.Combine(d.FullName, "roms", "48.rom")))
                romDir = Path.Combine(d.FullName, "roms");
            string games = Path.Combine(d.FullName, "ROMs");
            if (!Directory.Exists(games))
                continue;
            foreach (var zip in Directory.GetFiles(games, "*.zip"))
            {
                List<MediaFile> files;
                try { files = MediaArchive.Open(zip); }
                catch (InvalidDataException) { continue; }
                var p = ProfileLibrary.ForFiles(files);
                if (p != null && !sources.ContainsKey(p.Id))
                    sources[p.Id] = new GameSource(p, files, games);
            }
        }
        if (romDir == null || !sources.ContainsKey("invaders") || !sources.ContainsKey("manicminer"))
            return null;
        return (sources, romDir);
    }

    [Test]
    public void InvadersMiner_RulesReactToRealEvents()
    {
        var found = FindGames();
        if (found == null)
            Assert.Ignore("Hacen falta roms/invaders.zip y Manic Miner en ROMs/.");
        var recipe = RecipeLibrary.BuiltIn.Single(r => r.Id == "invaders-miner");
        var session = FusionSession.Start(recipe, found.Value.sources, found.Value.romDir);

        var inv = session.Slot("a");
        var mm = session.Slot("b");
        Assert.That(inv.Profile.Variables["aliens"].Read(inv.Machine.Memory), Is.EqualTo(55), "Space Invaders arrancó la partida");
        int air0 = mm.Profile.Variables["air"].Read(mm.Machine.Memory);
        Assert.That(air0, Is.InRange(50, 63), "Manic Miner está jugando con el aire casi lleno");

        // Shoot for a while: fire only reaches the Invaders slot.
        int airIncreases = 0, kills = 0;
        int lastAliens = 55, lastAir = air0;
        for (int tick = 0; tick < 60 * 30; tick++)
        {
            session.SetInput("fire", tick % 40 < 3);
            session.Update(1.0 / 60);
            int aliens = inv.Profile.Variables["aliens"].Read(inv.Machine.Memory);
            int air = mm.Profile.Variables["air"].Read(mm.Machine.Memory);
            if (aliens < lastAliens) kills += lastAliens - aliens;
            if (air > lastAir) airIncreases++;
            lastAliens = aliens;
            lastAir = air;
        }
        TestContext.Out.WriteLine($"invasores abatidos: {kills}, subidas de aire: {airIncreases}, eventos: {session.Events.Count}");
        foreach (var e in session.Events.Take(10))
            TestContext.Out.WriteLine($"  tick {e.Tick}: {e.Text}");
        Assert.That(kills, Is.GreaterThan(0), "no se abatió ningún invasor disparando 30 segundos");
        Assert.That(airIncreases, Is.GreaterThan(0), "la regla no le dio aire a Willy");
        Assert.That(session.Events.Any(e => e.Text.Contains("Invasor abatido")), Is.True);

        // Shared controls: holding right moves both the cannon and Willy.
        int cannon0 = inv.Profile.Variables["x"].Read(inv.Machine.Memory);
        int willy0 = mm.Profile.Variables["x"].Read(mm.Machine.Memory);
        session.SetInput("fire", false);
        session.SetInput("right", true);
        for (int i = 0; i < 90; i++)
            session.Update(1.0 / 60);
        session.SetInput("right", false);
        Assert.That(inv.Profile.Variables["x"].Read(inv.Machine.Memory), Is.GreaterThan(cannon0), "el cañón no se movió");
        Assert.That(mm.Profile.Variables["x"].Read(mm.Machine.Memory), Is.Not.EqualTo(willy0), "Willy no se movió");
        int lit = session.Video.Pixels.Count(p => (p & 0xFFFFFF) != 0);
        Assert.That(lit, Is.GreaterThan(1000), "la imagen combinada está vacía");
    }
}
