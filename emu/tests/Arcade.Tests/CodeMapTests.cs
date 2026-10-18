using Retro.Core;
using Retro.Systems.Arcade;

namespace Arcade.Tests;

[TestFixture]
public class CodeMapTests
{
    [Test]
    public void TestProgram_RoutinesAndCalls()
    {
        // 0000 CALL 0010 / CALL 0020 / JMP 0000 ; 0010 CALL 0020 / RET ; 0020 RET
        var rom = new byte[0x2000];
        new byte[] { 0xCD, 0x10, 0x00, 0xCD, 0x20, 0x00, 0xC3, 0x00, 0x00 }.CopyTo(rom, 0);
        new byte[] { 0xCD, 0x20, 0x00, 0xC9 }.CopyTo(rom, 0x10);
        rom[0x20] = 0xC9;
        var m = new SpaceInvaders(rom);
        var map = CodeMap.Build(a => m.Peek(a), m.Flow, new uint[] { 0 });
        Assert.That(map.Routines.Keys, Is.EquivalentTo(new uint[] { 0x0000, 0x0010, 0x0020 }));
        Assert.That(map.Routines[0x0020].Callers, Is.EquivalentTo(new uint[] { 0x0000, 0x0010 }));
        Assert.That(map.Routines[0x0000].Calls, Is.EquivalentTo(new uint[] { 0x0010, 0x0020 }));
        Assert.That(map.Routines[0x0010].Instructions, Is.EquivalentTo(new uint[] { 0x0010, 0x0013 }));
    }

    [Test]
    public void CodeOnlySeenRunning_BecomesARoutine()
    {
        // 0000 JMP 0000 ; 0100 MVI A,1 / CALL 0110 / RET ; 0110 RET
        // Nothing reaches 0100 statically (as with a jump table); the emulator saw it run.
        var rom = new byte[0x2000];
        new byte[] { 0xC3, 0x00, 0x00 }.CopyTo(rom, 0);
        new byte[] { 0x3E, 0x01, 0xCD, 0x10, 0x01, 0xC9 }.CopyTo(rom, 0x100);
        rom[0x110] = 0xC9;
        var m = new SpaceInvaders(rom);
        var executed = new bool[0x10000];
        executed[0x100] = executed[0x102] = executed[0x105] = executed[0x110] = true;
        var map = CodeMap.Build(a => m.Peek(a), m.Flow, new uint[] { 0 }, executed);
        Assert.That(map.Routines.ContainsKey(0x0100), Is.True);
        Assert.That(map.Routines[0x0100].Instructions, Is.EquivalentTo(new uint[] { 0x100, 0x102, 0x105 }));
        Assert.That(map.Routines[0x0110].Callers, Is.EquivalentTo(new uint[] { 0x0100 }));
        Assert.That(map.Routines[0x0100].Executed, Is.True);
    }

    [Test]
    public void PatchCode_ChangesTheProgramInMemory()
    {
        var rom = new byte[0x2000];
        rom[0x10] = 0x3D;   // DCR A
        var m = new SpaceInvaders(rom);
        m.Poke(0x10, 0x00);
        Assert.That(m.Peek(0x10), Is.EqualTo(0x3D), "el depurador no escribe en la ROM");
        m.PatchCode(0x10, 0x00);
        Assert.That(m.Peek(0x10), Is.EqualTo(0x00), "el editor de código sí");
        Assert.That(rom[0x10], Is.EqualTo(0x3D), "el archivo original no se toca");
    }

    [Test]
    [Category("Compat")]
    public void RealGame_IsSplitIntoRoutines()
    {
        SpaceInvaders? m = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && m == null; d = d.Parent)
        {
            string dir = Path.Combine(d.FullName, "ROMs");
            if (Directory.Exists(dir))
                foreach (var zip in Directory.GetFiles(dir, "*.zip"))
                {
                    var files = MediaArchive.Open(zip);
                    if (SpaceInvadersRom.Probe(files) == 100)
                        m = new SpaceInvaders(SpaceInvadersRom.Build(files).rom);
                }
        }
        if (m == null)
            Assert.Ignore("Falta roms/invaders.zip");
        m.ExecutedMap = new bool[0x10000];
        for (int i = 0; i < 600; i++)
            m.RunFrame(null);
        int executed = m.ExecutedMap.Count(x => x);
        var map = CodeMap.Build(a => m.Peek(a), m.Flow, m.CodeEntryPoints, m.ExecutedMap);
        TestContext.Out.WriteLine($"instrucciones ejecutadas: {executed}, instrucciones encontradas: {map.Code.Count}, rutinas: {map.Routines.Count}");
        var top = map.Routines.Values.OrderByDescending(r => r.Callers.Count).Take(5);
        foreach (var r in top)
            TestContext.Out.WriteLine($"  ${r.Start:X4}: {r.Instructions.Count} instrucciones, {r.Callers.Count} llamadores");
        Assert.That(executed, Is.GreaterThan(150), "el modo demostración ejecuta unas 250 direcciones distintas");
        Assert.That(map.Routines.Count, Is.GreaterThan(40));
        Assert.That(map.Code.Keys.Where(a => a < 0x2000).Count(), Is.GreaterThan(1000), "la mayor parte del código está en la ROM");
        Assert.That(map.Routines.ContainsKey(0x0008) && map.Routines.ContainsKey(0x0010), Is.True, "vectores RST 1 y RST 2");
    }
}
