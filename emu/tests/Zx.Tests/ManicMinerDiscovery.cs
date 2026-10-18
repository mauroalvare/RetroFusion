using Retro.Core;
using Retro.Systems.Spectrum;

namespace Retro.Systems.Spectrum.Tests;

/// <summary>Finds Manic Miner's RAM variables with the memory search (explicit, by hand).</summary>
[TestFixture]
[Explicit]
public class ManicMinerDiscovery
{
    internal static Spectrum48? LoadManicMiner()
    {
        string? romDir = null, game = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (romDir == null && File.Exists(Path.Combine(d.FullName, "roms", "48.rom")))
                romDir = Path.Combine(d.FullName, "roms");
            var g = Path.Combine(d.FullName, "ROMs");
            if (game == null && Directory.Exists(g))
                game = Directory.GetFiles(g, "*anic*iner*").FirstOrDefault();
        }
        if (romDir == null || game == null)
            return null;
        var m = new Spectrum48(File.ReadAllBytes(Path.Combine(romDir, "48.rom")));
        var media = MediaArchive.Open(game).First(f => SpectrumMedia.Probe(f) > 0);
        SpectrumMedia.Load(m, media);
        for (int i = 0; i < 150; i++) m.RunFrame(null);
        m.TypeLoadCommand();
        for (int i = 0; i < 20000 && !m.Tape.AtEnd; i++) m.RunFrame(null);
        for (int i = 0; i < 150; i++) m.RunFrame(null);
        m.QueueKeys(40, "ENTER");
        for (int i = 0; i < 100; i++) m.RunFrame(null);
        return m;
    }

    private static void Run(Spectrum48 m, int frames)
    {
        for (int i = 0; i < frames; i++) m.RunFrame(null);
    }

    private static void Hold(Spectrum48 m, string key, int frames)
    {
        m.SetKey(key, true);
        Run(m, frames);
        m.SetKey(key, false);
    }

    private static void Print(string title, MemorySearch s) =>
        TestContext.Out.WriteLine($"{title}: {s.Count} -> " + string.Join(" ", s.Results(30).Select(r => $"{r.Address:X4}={r.Value:X2}")));

    [Test]
    public void DiscoverSlow()
    {
        var m = LoadManicMiner();
        if (m == null)
            Assert.Ignore("Falta Manic Miner o la ROM 48K");
        Run(m, 100);
        var x = new MemorySearch(m);
        x.Start(0x5B00, 0xFFFF);
        for (int i = 0; i < 2; i++)
        {
            Hold(m, "P", 60); Run(m, 4);
            x.Filter(SearchCondition.Increased);
            Hold(m, "O", 40); Run(m, 4);
            x.Filter(SearchCondition.Decreased);
        }
        Print("x de Willy (ventanas largas)", x);

        var air = new MemorySearch(m);
        air.Start(0x5B00, 0xFFFF);
        for (int i = 0; i < 4; i++)
        {
            Run(m, 400);
            air.Filter(SearchCondition.Decreased);
        }
        Print("aire (ventanas de 400 cuadros)", air);

        // Score digits: poke candidates and read the screen
        for (uint a = 0x841E; a <= 0x8430; a++)
        {
            byte old = m.Peek(a);
            m.Poke(a, (byte)'9');
            Run(m, 30);
            string line = ScreenText.Read(m)[19];
            TestContext.Out.WriteLine($"poke {a:X4}='9' -> {line.Trim()}");
            m.Poke(a, old);
            Run(m, 30);
        }
    }

    [Test]
    public void Discover()
    {
        var m = LoadManicMiner();
        if (m == null)
            Assert.Ignore("Falta Manic Miner o la ROM 48K");
        TestContext.Out.WriteLine(string.Join("\n", ScreenText.Read(m).Skip(16)));

        // Willy's position: right increases, left decreases
        var x = new MemorySearch(m);
        x.Start(0x5B00, 0xFFFF);
        for (int i = 0; i < 3; i++)
        {
            Hold(m, "P", 16); Run(m, 2);
            x.Filter(SearchCondition.Increased);
            Hold(m, "O", 16); Run(m, 2);
            x.Filter(SearchCondition.Decreased);
        }
        Print("x de Willy", x);

        // air: goes down while standing still
        var air = new MemorySearch(m);
        air.Start(0x5B00, 0xFFFF);
        for (int i = 0; i < 4; i++)
        {
            Run(m, 120);
            air.Filter(SearchCondition.Decreased);
        }
        Print("aire (baja con el tiempo)", air);

        // score shown as ASCII digits: runs of six '0'
        var runs = new List<uint>();
        for (uint a = 0x5B00; a < 0xFFF0; a++)
        {
            bool zeros = true;
            for (uint k = 0; k < 6; k++)
                zeros &= m.Peek(a + k) == (byte)'0';
            if (zeros) runs.Add(a);
        }
        TestContext.Out.WriteLine("secuencias '000000': " + string.Join(" ", runs.Select(a => a.ToString("X4"))));

        // lives: wait until the air runs out and Willy dies
        var lives = new MemorySearch(m);
        lives.Start(0x5B00, 0xFFFF);
        lives.Filter(SearchCondition.EqualTo, 2);
        int before = lives.Count;
        for (int t = 0; t < 40; t++)
        {
            Run(m, 250);
            var probe = new MemorySearch(m);
            if (lives.Results(4000).Any(r => r.Value == 1))
                break;
        }
        lives.Filter(SearchCondition.EqualTo, 1);
        Print($"vidas (2 -> 1, de {before} candidatos)", lives);
        TestContext.Out.WriteLine(string.Join("\n", ScreenText.Read(m).Skip(16)));
    }
}
