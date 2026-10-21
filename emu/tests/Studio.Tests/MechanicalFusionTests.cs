using System.Text.Json;
using Retro.Fusion;
using Retro.Studio;

namespace Studio.Tests;

/// <summary>Hand-written integration fixtures, not bundled games or generator templates.</summary>
[TestFixture, Category("Compat")]
public class MechanicalFusionTests
{
    private static (Dictionary<string, GameSource> sources, string romDir) Games(string donor)
    {
        var found = TestGames.Find() ?? throw new IgnoreException("Hacen falta los originales locales y firmware.");
        if (!found.sources.ContainsKey(donor)) Assert.Ignore("Falta el original " + donor);
        return found;
    }

    private static StudioSession Session(string id, string a, string b)
    {
        var found = Games(b);
        return StudioSession.Start(new StudioGame { Id = id, BaseSlot = "a",
            Games = [new() { Slot = "a", Profile = a }, new() { Slot = "b", Profile = b }],
            Script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", id + ".js")) }, found.sources, found.romDir);
    }

    private static StudioSession Play(string id, string a, string b, bool active)
    {
        var s = Session(id, a, b);
        for (int n = 0; n < 900; n++)
        {
            s.SetInput("fire", active && n % 24 < 12);
            s.SetInput("right", active && (id == "miner-pac" ? n < 350 : id == "invaders-miner" ? n % 240 < 120 : n % 200 < 70));
            s.SetInput("left", active && id == "invaders-miner" && n % 240 >= 120);
            s.SetInput("jump", active && id == "miner-invaders" && n % 90 < 12);
            s.Update(1.0 / 60);
            if (s.Error != null) Assert.Fail(s.Error);
        }
        // Compare with the original at that very frame, not a static title screenshot.
        var baseMachine = s.Slots[0].Machine;
        int ox = baseMachine is Retro.Systems.Spectrum.Spectrum48 ? Retro.Systems.Spectrum.Spectrum48.BorderSide : 0;
        int oy = baseMachine is Retro.Systems.Spectrum.Spectrum48 ? Retro.Systems.Spectrum.Spectrum48.BorderTop : 0;
        int same = 0;
        for (int y = 0; y < s.Video.Height; y++)
            for (int x = 0; x < s.Video.Width; x++)
                if (s.Video.Pixels[y * s.Video.Width + x] == baseMachine.Video.Pixels[(y + oy) * baseMachine.Video.Width + x + ox]) same++;
        Assert.That(same, Is.GreaterThan(s.Video.Pixels.Length * 0.9), "El injerto no debe sustituir el mundo nativo.");
        return s;
    }

    [Test]
    public void NativeCave_ShotsChangeTheOriginalFleetAirAndScore()
    {
        var still = Play("miner-invaders", "manicminer", "invaders", false);
        var active = Play("miner-invaders", "manicminer", "invaders", true);
        Assert.Multiple(() =>
        {
            Assert.That(still.Score, Is.Zero);
            Assert.That(active.Score, Is.GreaterThan(0));
            Assert.That(active.Log, Has.Some.StartsWith("impacto "));
            Assert.That(active.Slots[1].Machine.Memory.Peek(0x2082), Is.LessThan(22));
            Assert.That(active.Slots[0].Profile.Variables["score"].Read(active.Slots[0].Machine.Memory), Is.GreaterThan(0));
        });
        using var hit = JsonDocument.Parse(active.Log.First(l => l.StartsWith("impacto "))[8..]);
        Assert.That(hit.RootElement.GetProperty("airAfter").GetInt32(), Is.GreaterThan(hit.RootElement.GetProperty("airBefore").GetInt32()));
    }

    [Test]
    public void ReversingTheBase_UsesNativeCannonShotsAndDeathWhenAirRunsOut()
    {
        var still = Play("invaders-miner", "invaders", "manicminer", false);
        var active = Play("invaders-miner", "invaders", "manicminer", true);
        Assert.Multiple(() =>
        {
            Assert.That(still.Log, Does.Contain("aire agotado: explosión nativa"));
            Assert.That(still.Slots[0].Machine.Memory.Peek(0x21FF), Is.LessThan(2));
            Assert.That(active.Log, Has.Some.StartsWith("aire por disparo "));
            Assert.That(active.Slots[0].Machine.Memory.Peek(0x2082), Is.LessThan(55));
            Assert.That(active.Deaths.Count, Is.LessThan(still.Deaths.Count));
            Assert.That(active.Video.Width, Is.EqualTo(224));
            Assert.That(active.Video.Height, Is.EqualTo(256));
        });
        active.Restart();
        Assert.That(active.Slots[0].Machine.Memory.Peek(0x2082), Is.EqualTo(55));
        Assert.That(active.Slots[0].Machine.Memory.Peek(0x21FF), Is.EqualTo(2));
    }

    [Test]
    public void DifferentDonor_ChangesDecisionsToChaseFleeAndEatWithinNativeCave()
    {
        var still = Play("miner-pac", "manicminer", "pacman-c64", false);
        var active = Play("miner-pac", "manicminer", "pacman-c64", true);
        Assert.Multiple(() =>
        {
            Assert.That(still.Log, Does.Contain("fantasma toca Willy"));
            Assert.That(still.Score, Is.Zero);
            Assert.That(active.Log, Does.Contain("píldora: perseguir cambia a huir"));
            Assert.That(active.Log, Has.Some.StartsWith("fantasma comido "));
            Assert.That(active.Score, Is.EqualTo(200));
            Assert.That(active.Log, Has.None.StartsWith("impacto "));
        });
        using var eaten = JsonDocument.Parse(active.Log.First(l => l.StartsWith("fantasma comido "))[16..]);
        Assert.That(eaten.RootElement.GetProperty("after").GetInt32(), Is.GreaterThan(eaten.RootElement.GetProperty("before").GetInt32()));
    }

    [Test]
    public void PacManActors_MatchRealPixelsInTheVerifiedEdition()
    {
        var found = Games("pacman-c64");
        const string script = "function init(){ games.a.pause();games.a.ghosts().forEach(function(g){log(JSON.stringify(g));});game.state('playing'); }";
        var s = StudioSession.Start(new StudioGame { BaseSlot = "a", Games = [new() { Slot = "a", Profile = "pacman-c64" }], Script = script }, found.sources, found.romDir);
        Assert.That(s.Error, Is.Null);
        Assert.That(s.Log, Has.Count.EqualTo(4));
        int[] colors = [0x68372B, 0x9A6759, 0x588D43, 0x6F4F25];
        for (int n = 0; n < 4; n++)
        {
            using var pose = JsonDocument.Parse(s.Log[n]);
            int x = pose.RootElement.GetProperty("x").GetInt32() + 16, y = pose.RootElement.GetProperty("y").GetInt32() + 1;
            int count = 0;
            for (int yy = y; yy < y + 11; yy++)
                for (int xx = x; xx < x + 14; xx++)
                    if ((s.Video.Pixels[yy * 320 + xx] & 0xFFFFFF) == colors[n]) count++;
            Assert.That(count, Is.EqualTo(122), "Cuerpo y color originales del fantasma " + n);
        }
    }
}
