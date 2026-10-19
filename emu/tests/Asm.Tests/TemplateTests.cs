using Retro.Asm;
using Retro.Core;
using Retro.Machines;
using Retro.Systems.C64;
using Retro.Systems.Spectrum;

namespace Asm.Tests;

/// <summary>
/// The templates new games start from: each one assembles, loads like any game of its
/// machine, draws its title and answers to the controls.
/// </summary>
[TestFixture]
public class TemplateTests
{
    private static string Roms()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "roms", "48.rom")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Falta emu/roms.");
        return Path.Combine(dir!.FullName, "roms");
    }

    private static void Run(IMachine m, int frames)
    {
        var sink = new short[8192];
        for (int i = 0; i < frames; i++)
        {
            m.RunFrame(null);
            m.DrainAudio(sink);
        }
    }

    private static int Hold(IMachine m, string key, int frames, Func<int> read)
    {
        int before = read();
        m.SetKey(key, true);
        Run(m, frames);
        m.SetKey(key, false);
        Run(m, 4);
        return read() - before;
    }

    [Test]
    public void EveryTemplate_Assembles()
    {
        Assert.That(Templates.All.Select(t => t.Machine), Is.EquivalentTo(new[] { "c64", "zx48" }));
        foreach (var t in Templates.All)
        {
            var (r, file) = Templates.Build(t.Source, t.Machine);
            Assert.That(r.Ok, Is.True, $"{t.Machine}: {string.Join("; ", r.Errors)}");
            Assert.That(file, Is.Not.Null.And.Not.Empty);
            Assert.That(r.Symbols.Keys, Does.Contain("px").And.Contain("py"), "las variables del jugador tienen nombre");
        }
        Assert.That(Templates.For("ZX48"), Is.Not.Null);
        Assert.That(Templates.For("amiga"), Is.Null);
    }

    [Test]
    public void TheC64Template_DrawsItsTitle_AndAnswersTheJoystick()
    {
        var t = Templates.For("c64")!;
        var (r, prg) = Templates.Build(t.Source, t.Machine);
        var m = (C64)MachineLauncher.Launch("c64", new[] { new MediaFile("juego.prg", prg!, "juego.prg") }, null, Roms()).Machine;
        Run(m, 150);
        Assert.That(m.ScreenText(), Does.Contain("RETROEMU"));
        int px = r.Symbols["px"], py = r.Symbols["py"];
        Assert.That(m.Memory.Peek((uint)px), Is.EqualTo(160), "el sprite arranca en su lugar");
        Assert.That(Hold(m, "JOY2 RIGHT", 30, () => m.Memory.Peek((uint)px)), Is.InRange(25, 35), "un punto por cuadro a la derecha");
        Assert.That(Hold(m, "JOY2 UP", 20, () => m.Memory.Peek((uint)py)), Is.InRange(-25, -15), "y hacia arriba");
        Assert.That(m.Memory.Peek(0xD000), Is.EqualTo(m.Memory.Peek((uint)px)), "el sprite sigue a la variable");
        int border = m.Memory.Peek(0xD020) & 0x0F;
        Hold(m, "JOY2 FIRE", 3, () => 0);
        Assert.That(m.Memory.Peek(0xD020) & 0x0F, Is.Not.EqualTo(border), "el disparo cambia el borde");
    }

    [Test]
    public void TheSpectrumTemplate_LoadsFromItsTape_AndAnswersTheKeysAndTheKempston()
    {
        var t = Templates.For("zx48")!;
        var (r, tap) = Templates.Build(t.Source, t.Machine);
        var launch = MachineLauncher.Launch("zx48", new[] { new MediaFile("juego.tap", tap!, "juego.tap") }, null, Roms());
        Assert.That(launch.IsTape, Is.True);
        var zx = (Spectrum48)launch.Machine;
        MachineLauncher.LoadTape(zx);
        Run(zx, 100);
        string text = ScreenText.ReadAll(zx);
        Assert.That(text, Does.Contain("RETROEMU"), "el cargador BASIC arrancó el código solo");
        Assert.That(text, Does.Not.Contain("Bytes"), "y la pantalla de la carga se borró");
        int px = r.Symbols["px"], py = r.Symbols["py"];
        Assert.That(zx.Memory.Peek((uint)px), Is.EqualTo(15));
        Assert.That(zx.Memory.Peek(0x5800 + 12 * 32 + 15), Is.EqualTo(0x50), "el bloque está dibujado en los atributos");
        Assert.That(Hold(zx, "P", 40, () => zx.Memory.Peek((uint)px)), Is.InRange(8, 12), "P: una celda cada 4 cuadros a la derecha");
        Assert.That(Hold(zx, "KEMPSTON LEFT", 20, () => zx.Memory.Peek((uint)px)), Is.InRange(-6, -4), "y el Kempston también");
        Assert.That(Hold(zx, "Q", 20, () => zx.Memory.Peek((uint)py)), Is.InRange(-6, -4), "Q sube");
        int x = zx.Memory.Peek((uint)px), y = zx.Memory.Peek((uint)py);
        Assert.That(zx.Memory.Peek((uint)(0x5800 + y * 32 + x)), Is.EqualTo(0x50), "el bloque se dibuja donde está");
        Assert.That(zx.Memory.Peek(0x5800 + 12 * 32 + 15), Is.EqualTo(0x38), "y se borra donde estaba");
    }

    /// <summary>The tape is what the ROM expects: header and data blocks with their checksums.</summary>
    [Test]
    public void TheSpectrumTape_HasAnAutostartLoaderAndTheCode()
    {
        var code = new byte[] { 0xC9 };
        var tap = SpectrumTap.Build(code, 0x8000, "prueba");
        var blocks = new List<byte[]>();
        for (int i = 0; i < tap.Length;)
        {
            int len = tap[i] | tap[i + 1] << 8;
            blocks.Add(tap[(i + 2)..(i + 2 + len)]);
            i += 2 + len;
        }
        Assert.That(blocks, Has.Count.EqualTo(4));
        foreach (var b in blocks)
            Assert.That(b.Aggregate((byte)0, (x, v) => (byte)(x ^ v)), Is.EqualTo(0), "el XOR de cada bloque da cero");
        Assert.That(blocks[0][1], Is.EqualTo(0), "programa BASIC");
        Assert.That(System.Text.Encoding.ASCII.GetString(blocks[0][2..12]), Is.EqualTo("prueba    "));
        Assert.That(blocks[0][14] | blocks[0][15] << 8, Is.EqualTo(10), "arranca solo en la línea 10");
        Assert.That(blocks[2][1], Is.EqualTo(3), "bloque de código");
        Assert.That(blocks[2][14] | blocks[2][15] << 8, Is.EqualTo(0x8000), "se carga en el origen");
        Assert.That(blocks[3][1..^1], Is.EqualTo(code));
    }
}
