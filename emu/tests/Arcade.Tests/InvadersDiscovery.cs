using Retro.Core;
using Retro.Systems.Arcade;

namespace Arcade.Tests;

/// <summary>
/// Finds Space Invaders' RAM variables with the memory search, playing the real game.
/// Explicit: run by hand when building the game profile; its findings become the
/// assertions in InvadersProfileTests.
/// </summary>
[TestFixture]
[Explicit]
public class InvadersDiscovery
{
    private static SpaceInvaders? NewGame(int ships = 3)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string dir = Path.Combine(d.FullName, "ROMs");
            if (!Directory.Exists(dir))
                continue;
            foreach (var zip in Directory.GetFiles(dir, "*.zip"))
            {
                var files = MediaArchive.Open(zip);
                if (SpaceInvadersRom.Probe(files) == 100)
                    return new SpaceInvaders(SpaceInvadersRom.Build(files).rom) { Ships = ships };
            }
        }
        return null;
    }

    private static void Run(SpaceInvaders m, int frames)
    {
        for (int i = 0; i < frames; i++)
            m.RunFrame(null);
    }

    private static void Hold(SpaceInvaders m, string key, int frames)
    {
        m.SetKey(key, true);
        Run(m, frames);
        m.SetKey(key, false);
    }

    private static void StartGame(SpaceInvaders m)
    {
        Run(m, 200);
        Hold(m, "COIN", 8);
        Run(m, 60);
        Hold(m, "P1 START", 8);
        Run(m, 250);
    }

    private static void Print(string title, MemorySearch s) =>
        TestContext.Out.WriteLine($"{title}: {s.Count} -> " + string.Join(" ", s.Results(24).Select(r => $"{r.Address:X4}={r.Value:X2}")));

    [Test]
    public void CannonPosition()
    {
        var m = NewGame();
        if (m == null)
            Assert.Ignore("Falta roms/invaders.zip");
        StartGame(m);
        for (int i = 0; i < 12; i++)
        {
            Hold(m, i < 6 ? "P1 RIGHT" : "P1 LEFT", 25);
            TestContext.Out.WriteLine($"{(i < 6 ? "derecha" : "izquierda")}: 201B={m.Peek(0x201B):X2} 2096={m.Peek(0x2096):X2} 20F8={m.Peek(0x20F8):X2} 21FF={m.Peek(0x21FF):X2}");
        }
    }

    [Test]
    public void Discover()
    {
        var m = NewGame();
        if (m == null)
            Assert.Ignore("Falta roms/invaders.zip");
        var s = new MemorySearch(m);

        // credits
        Run(m, 200);
        s.Start(0x2000, 0x23FF);
        Hold(m, "COIN", 8); Run(m, 40);
        s.Filter(SearchCondition.IncreasedBy, 1);
        Hold(m, "COIN", 8); Run(m, 40);
        s.Filter(SearchCondition.IncreasedBy, 1);
        Print("créditos", s);

        // ships: value 3 with Ships=3 and 5 with Ships=5 at the same moment of the game
        var m3 = NewGame(3)!;
        var m5 = NewGame(5)!;
        StartGame(m3);
        StartGame(m5);
        var three = new MemorySearch(m3); three.Start(0x2000, 0x23FF); three.Filter(SearchCondition.EqualTo, 3);
        var five = new MemorySearch(m5); five.Start(0x2000, 0x23FF); five.Filter(SearchCondition.EqualTo, 5);
        var both = three.Results(400).Select(r => r.Address).Intersect(five.Results(400).Select(r => r.Address));
        TestContext.Out.WriteLine("naves (=3 con DIP 3, =5 con DIP 5): " + string.Join(" ", both.Select(a => a.ToString("X4"))));
        var two = new MemorySearch(m3); two.Start(0x2000, 0x23FF); two.Filter(SearchCondition.EqualTo, 2);
        var four = new MemorySearch(m5); four.Start(0x2000, 0x23FF); four.Filter(SearchCondition.EqualTo, 4);
        var reserve = two.Results(400).Select(r => r.Address).Intersect(four.Results(400).Select(r => r.Address));
        TestContext.Out.WriteLine("naves de reserva (=2 / =4): " + string.Join(" ", reserve.Select(a => a.ToString("X4"))));

        // player x
        var game = m3;
        var px = new MemorySearch(game);
        px.Start(0x2000, 0x23FF);
        for (int i = 0; i < 3; i++)
        {
            Hold(game, "P1 RIGHT", 20); Run(game, 2);
            px.Filter(SearchCondition.Increased);
            Hold(game, "P1 LEFT", 20); Run(game, 2);
            px.Filter(SearchCondition.Decreased);
        }
        Print("x del cañón", px);

        // aliens remaining: 55 at the start, one less after each hit
        var al = new MemorySearch(game);
        al.Start(0x2000, 0x23FF);
        al.Filter(SearchCondition.EqualTo, 55);
        Print("invasores (=55)", al);
        // fire until something dies
        for (int shot = 0; shot < 20; shot++)
        {
            Hold(game, "P1 FIRE", 3);
            Run(game, 50);
        }
        al.Filter(SearchCondition.Decreased);
        Print("invasores después de disparar", al);
        TestContext.Out.WriteLine($"20F8..20F9 = {game.Peek(0x20F8):X2} {game.Peek(0x20F9):X2}  21FF = {game.Peek(0x21FF):X2}  2082 = {game.Peek(0x2082):X2}  201B = {game.Peek(0x201B):X2}");
    }
}
