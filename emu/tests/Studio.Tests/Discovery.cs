using Retro.Core;
using Retro.Fusion;

namespace Studio.Tests;

/// <summary>Tools used to find sprites and object positions in the real games (run by hand).</summary>
[TestFixture]
[Explicit]
public partial class Discovery
{
    internal static string Out(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "RetroEmu.slnx")))
            {
                Directory.CreateDirectory(Path.Combine(d.FullName, "out"));
                return Path.Combine(d.FullName, "out", name);
            }
        return name;
    }

    internal static string Dump(IMachine m, uint start, int length)
    {
        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < length; r += 16)
        {
            sb.Append($"{start + r:X4}:");
            for (int c = 0; c < 16; c++)
                sb.Append($" {m.Memory.Peek((uint)(start + r + c)):X2}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Test]
    public void InvadersSpriteSheet()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        var m = MachineFactory.Create(found.sources["invaders"], found.romDir);
        // 16-byte column sprites from 1A00 to 1FFF, 8 per row, each cell 20x12
        int count = (0x2000 - 0x1A00) / 16;
        var f = new VideoFrame(8 * 20, (count / 8) * 12);
        for (int i = 0; i < count; i++)
        {
            uint a = (uint)(0x1A00 + i * 16);
            int cx = (i % 8) * 20, cy = (i / 8) * 12;
            for (int c = 0; c < 16; c++)
            {
                byte b = m.Memory.Peek((uint)(a + c));
                for (int bit = 0; bit < 8; bit++)
                    f.Pixels[(cy + 7 - bit) * f.Width + cx + c] = (b & (1 << bit)) != 0 ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFF202040);
            }
        }
        PngWriter.Write(Out("inv_sprites.png"), f, 4);
        for (int row = 0; row < count / 8; row++)
            TestContext.Out.WriteLine($"row {row}: {0x1A00 + row * 128:X4}");
    }

    [Test]
    public void InvadersRam()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        var src = found.sources["invaders"];
        var m = MachineFactory.Create(src, found.romDir);
        MachineFactory.Boot(m, src.Profile);
        for (int t = 0; t < 4; t++)
        {
            for (int i = 0; i < 90; i++)
                m.RunFrame(null);
            TestContext.Out.WriteLine($"--- t={t}\n{Dump(m, 0x2000, 0x80)}");
            PngWriter.Write(Out($"inv_ram_{t}.png"), m.Video, 2);
        }
    }
}

public partial class Discovery
{
    private static void Box(VideoFrame f, int x, int y, int w, int h, int color)
    {
        for (int i = 0; i < w; i++)
        {
            Set(f, x + i, y, color);
            Set(f, x + i, y + h - 1, color);
        }
        for (int j = 0; j < h; j++)
        {
            Set(f, x, y + j, color);
            Set(f, x + w - 1, y + j, color);
        }
    }

    private static void Set(VideoFrame f, int x, int y, int c)
    {
        if (x >= 0 && y >= 0 && x < f.Width && y < f.Height)
            f.Pixels[y * f.Width + x] = c;
    }

    [Test]
    public void InvadersPositions()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        var src = found.sources["invaders"];
        var m = MachineFactory.Create(src, found.romDir);
        MachineFactory.Boot(m, src.Profile);
        for (int i = 0; i < 200; i++)
            m.RunFrame(null);
        var f = m.Video;
        int refY = m.Memory.Peek(0x2009), refX = m.Memory.Peek(0x200A);
        for (int i = 0; i < 55; i++)
        {
            if (m.Memory.Peek((uint)(0x2100 + i)) == 0) continue;
            int row = i / 11, col = i % 11;
            Box(f, refX + col * 16 - 32, 248 - (refY + row * 16), 16, 8, unchecked((int)0xFFFF00FF));
        }
        Box(f, m.Memory.Peek(0x201B) - 32, 248 - m.Memory.Peek(0x201A), 16, 8, unchecked((int)0xFF00FFFF));
        foreach (uint s in new uint[] { 0x2030, 0x2040, 0x2050 })
            if ((m.Memory.Peek(s + 5) & 0x80) != 0)
                Box(f, m.Memory.Peek(s + 0xE) - 32 - 1, 248 - m.Memory.Peek(s + 0xD) - 1, 5, 10, unchecked((int)0xFFFF8000));
        PngWriter.Write(Out("inv_positions.png"), f, 2);
    }

    [Test]
    public void MinerWilly()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        var src = found.sources["manicminer"];
        var m = MachineFactory.Create(src, found.romDir);
        MachineFactory.Boot(m, src.Profile);
        string Vars() => $"8068={m.Memory.Peek(0x8068)} 8069={m.Memory.Peek(0x8069)} 806A={m.Memory.Peek(0x806A):X2} 806B={m.Memory.Peek(0x806B)} 806C={m.Memory.Peek(0x806C):X2} 806D={m.Memory.Peek(0x806D):X2} 806E={m.Memory.Peek(0x806E)} air={m.Memory.Peek(0x80BC)} lives={m.Memory.Peek(0x8457)}";
        void Run(int n, string? key = null)
        {
            if (key != null) m.SetKey(key, true);
            for (int i = 0; i < n; i++) m.RunFrame(null);
            if (key != null) m.SetKey(key, false);
        }
        TestContext.Out.WriteLine("start " + Vars());
        Run(20, "P");
        TestContext.Out.WriteLine("right " + Vars());
        m.SetKey("SPACE", true);
        for (int i = 0; i < 12; i++)
        {
            m.RunFrame(null);
            TestContext.Out.WriteLine($"jump{i} " + Vars());
        }
        m.SetKey("SPACE", false);
        Run(30);
        TestContext.Out.WriteLine("after " + Vars());
        // marker: x = (806C & 31) * 8, y = 8068 / 2, inside the 256x192 paper at (48, 48)
        int attr = m.Memory.Peek(0x806C) | (m.Memory.Peek(0x806D) << 8);
        int x = (attr & 31) * 8, y = m.Memory.Peek(0x8068) / 2;
        var f = m.Video;
        Box(f, 48 + x, 48 + y, 16, 16, unchecked((int)0xFFFF00FF));
        PngWriter.Write(Out("mm_willy.png"), f, 2);
        TestContext.Out.WriteLine("ram 8000-80FF");
        TestContext.Out.WriteLine(Dump(m, 0x8000, 0x100));
        // Willy's sprite frames at 8200 (8 x 32 bytes, 16x16, rows of 2 bytes)
        var sheet = new VideoFrame(8 * 20, 2 * 20);
        for (int fr = 0; fr < 16; fr++)
        {
            uint a = (uint)(0x8200 + fr * 32);
            int cx = (fr % 8) * 20, cy = (fr / 8) * 20;
            for (int r = 0; r < 16; r++)
                for (int b = 0; b < 2; b++)
                {
                    byte v = m.Memory.Peek((uint)(a + r * 2 + b));
                    for (int bit = 0; bit < 8; bit++)
                        Set(sheet, cx + b * 8 + bit, cy + r, (v & (0x80 >> bit)) != 0 ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFF202040));
                }
        }
        PngWriter.Write(Out("mm_sprites.png"), sheet, 4);
    }
}

public partial class Discovery
{
    [Test]
    public void KillAndWaves()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        var mmSrc = found.sources["manicminer"];
        var mm = MachineFactory.Create(mmSrc, found.romDir);
        MachineFactory.Boot(mm, mmSrc.Profile);
        TestContext.Out.WriteLine($"cavern 8407={mm.Memory.Peek(0x8407)} lives={mm.Memory.Peek(0x8457)}");
        mm.Memory.Poke(0x806B, 255);
        for (int i = 0; i < 200; i++)
        {
            mm.RunFrame(null);
            if (i % 20 == 0)
                TestContext.Out.WriteLine($"f{i} 806B={mm.Memory.Peek(0x806B)} lives={mm.Memory.Peek(0x8457)} air={mm.Memory.Peek(0x80BC)} y={mm.Memory.Peek(0x8068)} attr={mm.Memory.Peek(0x806C):X2}");
        }
        PngWriter.Write(Out("mm_after_kill.png"), mm.Video, 1);

        var invSrc = found.sources["invaders"];
        var inv = MachineFactory.Create(invSrc, found.romDir);
        MachineFactory.Boot(inv, invSrc.Profile);
        for (int i = 0; i < 55; i++)
            inv.Memory.Poke((uint)(0x2100 + i), 0);
        inv.Memory.Poke(0x2082, 0);
        for (int i = 0; i < 400; i++)
        {
            inv.RunFrame(null);
            if (i % 50 == 0)
            {
                int alive = 0;
                for (int k = 0; k < 55; k++) alive += inv.Memory.Peek((uint)(0x2100 + k)) != 0 ? 1 : 0;
                TestContext.Out.WriteLine($"inv f{i} alive={alive} num={inv.Memory.Peek(0x2082)} refY={inv.Memory.Peek(0x2009)} refX={inv.Memory.Peek(0x200A)}");
            }
        }
        PngWriter.Write(Out("inv_after_wave.png"), inv.Video, 1);
    }
}

public partial class Discovery
{
    [Test]
    public void ProfileSprites()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        foreach (var id in new[] { "invaders", "manicminer" })
        {
            var src = found.sources[id];
            var m = MachineFactory.Create(src, found.romDir);
            MachineFactory.Boot(m, src.Profile);
            var sprites = src.Profile.Sprites.ToList();
            var sheet = new VideoFrame(sprites.Count * 28, 20);
            for (int i = 0; i < sprites.Count; i++)
            {
                var def = sprites[i].Value;
                var px = def.Decode(m.Memory);
                for (int y = 0; y < def.Height; y++)
                    for (int x = 0; x < def.Width; x++)
                        Set(sheet, i * 28 + x, y + 2, px[y * def.Width + x] ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFF203040));
                TestContext.Out.WriteLine($"{i}: {sprites[i].Key}");
            }
            PngWriter.Write(Out($"sprites_{id}.png"), sheet, 4);
        }
    }
}

public partial class Discovery
{
    [Test]
    public void InvadersShotsAndSaucer()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        var src = found.sources["invaders"];
        foreach (bool cancel in new[] { false, true })
        {
            var m = MachineFactory.Create(src, found.romDir);
            MachineFactory.Boot(m, src.Profile);
            int activations = 0, cancelled = 0, deaths = 0;
            bool[] was = new bool[3];
            byte lastAlive = m.Memory.Peek(0x2015);
            for (int f = 0; f < 3000; f++)
            {
                m.Memory.Poke(0x201B, 0x80);   // cannon parked in the middle
                m.RunFrame(null);
                for (int k = 0; k < 3; k++)
                {
                    uint s = (uint)(0x2030 + k * 16);
                    bool on = (m.Memory.Peek(s + 5) & 0x80) != 0;
                    if (on && !was[k]) activations++;
                    if (cancel && on && m.Memory.Peek(s + 0xD) < 0x40)
                    {
                        m.Memory.Poke(s + 5, 0);
                        cancelled++;
                        on = false;
                    }
                    was[k] = on;
                }
                byte alive = m.Memory.Peek(0x2015);
                if (lastAlive == 0xFF && alive != 0xFF) deaths++;
                lastAlive = alive;
                if (m.Memory.Peek(0x21FF) < 2) m.Memory.Poke(0x21FF, 3);
            }
            TestContext.Out.WriteLine($"cancel={cancel}: bombas lanzadas={activations} canceladas={cancelled} muertes del cañón={deaths} aliens={m.Memory.Peek(0x2082)}");
        }

        // saucer: watch 2080-209F while looking for its pixels in the band under the score
        var mm = MachineFactory.Create(src, found.romDir);
        MachineFactory.Boot(mm, src.Profile);
        for (int f = 0; f < 3600; f++)
        {
            mm.RunFrame(null);
            int lit = 0, minX = 999, maxX = -1;
            for (int y = 32; y < 48; y++)
                for (int x = 0; x < 224; x++)
                    if ((mm.Video.Pixels[y * 224 + x] & 0xFFFFFF) != 0) { lit++; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); }
            if (f % 30 == 0 || (lit > 0 && f % 6 == 0))
                if (lit > 0)
                    TestContext.Out.WriteLine($"f{f} ovni px={lit} x={minX}-{maxX}  " + Dump(mm, 0x2080, 0x20).Replace(Environment.NewLine, " | "));
        }
    }
}

internal static class TestGames
{
    internal static (Dictionary<string, GameSource> sources, string romDir)? Find()
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
            foreach (var file in Directory.GetFiles(games, "*.zip"))
            {
                List<MediaFile> files;
                try { files = MediaArchive.Open(file); }
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
}

public partial class Discovery
{
    [Test]
    public void MinerCaverns()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        var src = found.sources["manicminer"];
        var m = MachineFactory.Create(src, found.romDir);
        MachineFactory.Boot(m, src.Profile);
        string Text(uint a, int n) => new string(Enumerable.Range(0, n).Select(i => (char)m.Memory.Peek((uint)(a + i))).ToArray());
        for (int c = 0; c < 20; c++)
        {
            uint b = (uint)(0xB000 + c * 0x400);
            TestContext.Out.WriteLine($"{c,2} {b:X4} '{Text(b + 512, 32).Trim()}'");
        }
        uint c0 = 0xB000;
        TestContext.Out.WriteLine("tiles cavern0: " + Dump(m, c0 + 544, 72).Replace(Environment.NewLine, " | "));
        TestContext.Out.WriteLine("tiles buffer : " + Dump(m, 0x8020, 72).Replace(Environment.NewLine, " | "));
        TestContext.Out.WriteLine("616..767     : " + Dump(m, c0 + 616, 152).Replace(Environment.NewLine, " | "));
        TestContext.Out.WriteLine("attr rows 0-15 of cavern 0 vs screen attributes:");
        for (int r = 0; r < 16; r++)
        {
            var sb = new System.Text.StringBuilder();
            int same = 0;
            for (int col = 0; col < 32; col++)
            {
                byte a = m.Memory.Peek((uint)(c0 + r * 32 + col));
                sb.Append(a.ToString("X2")).Append(' ');
                if (a == m.Memory.Peek((uint)(0x5800 + r * 32 + col))) same++;
            }
            TestContext.Out.WriteLine($"r{r,2} {sb} igual a la pantalla: {same}/32");
        }
    }
}

public partial class Discovery
{
    [Test]
    public void WillyFrameOffsets()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        var src = found.sources["manicminer"];
        var m = MachineFactory.Create(src, found.romDir);
        MachineFactory.Boot(m, src.Profile);
        foreach (var (name, def) in src.Profile.Sprites.Where(s => s.Key.StartsWith("willy")))
        {
            var px = def.Decode(m.Memory);
            int min = 99, max = -1;
            for (int y = 0; y < def.Height; y++)
                for (int x = 0; x < def.Width; x++)
                    if (px[y * def.Width + x]) { min = Math.Min(min, x); max = Math.Max(max, x); }
            TestContext.Out.WriteLine($"{name,-11} columnas {min}..{max}");
        }
    }
}

public partial class Discovery
{
    /// <summary>Plays Manic Invaders up in the open part of the cavern (where the keys and bombs are) and reports.</summary>
    [Test]
    public void ManicInvadersUpTop()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        var game = Retro.Studio.StudioLibrary.BuiltIn.Single(g => g.Id == "manic-invaders").Copy();
        game.Script = game.Script.Replace("function tick(input) {",
            "function tick(input) {\n  if (mode === 'play' && !this.__placed) { this.__placed = true; willy.x = 120; willy.y = 24; }");
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var s = Retro.Studio.StudioSession.Start(game, found.sources, found.romDir);
            var rng = new Random(seed);
            s.SetInput("start", true); s.Update(1.0 / 60); s.SetInput("start", false);
            int dir = 1, hold = 0;
            double? first = null;
            for (int t = 0; t < 60 * 60 && s.State != "gameover"; t++)
            {
                if (--hold <= 0) { dir = rng.Next(3) - 1; hold = 20 + rng.Next(50); }
                s.SetInput("left", dir < 0);
                s.SetInput("right", dir > 0);
                s.SetInput("fire", t % 10 < 3);
                s.SetInput("jump", rng.Next(60) == 0);
                s.Update(1.0 / 60);
                Assert.That(s.Error, Is.Null);
                if (first == null && s.Deaths.Count > 0) first = t / 60.0;
                if (seed == 1 && t % 600 == 300)
                    PngWriter.Write(Out($"mi_top_{t / 60:D2}s.png"), s.Video, 2);
            }
            TestContext.Out.WriteLine($"semilla {seed}: primera muerte {first?.ToString("F1") ?? "-"} s, muertes {s.Deaths.Count} " +
                $"({string.Join(", ", s.Deaths.GroupBy(d => d.Cause).Select(g => g.Key + " " + g.Count()))}), puntos {s.Score}, estado {s.State}; {s.Hud}");
        }
    }
}

public partial class Discovery
{
    /// <summary>Walks through the 20 caverns (keys and portal forced) and saves each one.</summary>
    [Test]
    public void ManicInvadersAllCaverns()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("sin juegos");
        var game = Retro.Studio.StudioLibrary.BuiltIn.Single(g => g.Id == "manic-invaders").Copy();
        game.Script = game.Script.Replace("function tick(input) {",
            "function tick(input) {\n  if (mode === 'play' && input.tick % 60 === 59) { keys.forEach(function (k) { k.taken = true; }); willy.x = cav.portal.x; willy.y = cav.portal.y; }");
        var s = Retro.Studio.StudioSession.Start(game, found.sources, found.romDir);
        s.SetInput("start", true); s.Update(1.0 / 60); s.SetInput("start", false);
        int lastLevel = 0;
        for (int t = 0; t < 60 * 60 * 3 && s.State != "gameover"; t++)
        {
            s.Update(1.0 / 60);
            Assert.That(s.Error, Is.Null);
            if (s.Level != lastLevel)
            {
                lastLevel = s.Level;
                for (int k = 0; k < 120; k++) s.Update(1.0 / 60);   // past the card
                PngWriter.Write(Out($"mi_caverna_{s.Level:D2}.png"), s.Video, 1);
                TestContext.Out.WriteLine($"caverna {s.Level}: {s.Hud}");
            }
        }
        TestContext.Out.WriteLine($"estado final {s.State}, puntos {s.Score}");
    }
}
