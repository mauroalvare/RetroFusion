using System.Reflection;
using System.Text;

namespace Retro.Asm;

/// <summary>
/// A starting game for one machine: source in assembly, its CPU, how it is packed and the
/// guide to its hardware (what a writer needs: memory, screen, controls, sound, syntax).
/// </summary>
public sealed record GameTemplate(string Machine, string Name, string Cpu, string Source, string Output, string Guide);

/// <summary>
/// The minimal games every new game starts from (Spectrum and C64): they boot, draw,
/// read the controls and make a sound. They live as .s files next to this class.
/// </summary>
public static class Templates
{
    public static IReadOnlyList<GameTemplate> All { get; } = new[]
    {
        new GameTemplate("c64", "Commodore 64", "6502", Read("c64.s"), ".prg", Read("c64.md")),
        new GameTemplate("zx48", "ZX Spectrum 48K", "z80", Read("zx48.s"), ".tap", Read("zx48.md")),
    };

    public static GameTemplate? For(string machine) => All.FirstOrDefault(t => t.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase));

    private static string Read(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream("Retro.Asm.Templates." + name) ?? throw new InvalidOperationException($"falta la plantilla {name}");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>Assembles a source for a machine and packs it so the machine loads it like any game.</summary>
    public static (AsmResult result, byte[]? file) Build(string source, string machine)
    {
        if (machine.Equals("zx48", StringComparison.OrdinalIgnoreCase))
        {
            var r = AsmZ80.Assemble(source);
            return (r, r.Ok ? SpectrumTap.Build(r.Bytes, r.Origin, "juego") : null);
        }
        var c = Asm6502.Assemble(source);
        return (c, c.Ok ? new[] { (byte)c.Origin, (byte)(c.Origin >> 8) }.Concat(c.Bytes).ToArray() : null);
    }
}

/// <summary>
/// A Spectrum tape with a BASIC loader that runs the code by itself:
/// 10 CLEAR origin-1: LOAD "" CODE: RANDOMIZE USR origin.
/// </summary>
public static class SpectrumTap
{
    public static byte[] Build(byte[] code, ushort origin, string name)
    {
        var basic = new List<byte>();
        void Number(int v)
        {
            basic.AddRange(Encoding.ASCII.GetBytes(v.ToString()));
            // the hidden five-byte form of a small integer
            basic.AddRange(new byte[] { 0x0E, 0x00, 0x00, (byte)v, (byte)(v >> 8), 0x00 });
        }
        basic.Add(0xFD);                    // CLEAR
        Number(origin - 1);
        basic.Add((byte)':');
        basic.Add(0xEF);                    // LOAD
        basic.AddRange(new byte[] { (byte)'"', (byte)'"', 0xAF });   // "" CODE
        basic.Add((byte)':');
        basic.Add(0xF9);                    // RANDOMIZE
        basic.Add(0xC0);                    // USR
        Number(origin);
        basic.Add(0x0D);
        var line = new List<byte> { 0x00, 0x0A, (byte)basic.Count, (byte)(basic.Count >> 8) };   // line 10, length
        line.AddRange(basic);

        var tap = new List<byte>();
        tap.AddRange(Block(Header(0, name, line.Count, 10, line.Count)));
        tap.AddRange(Block(Data(line.ToArray())));
        tap.AddRange(Block(Header(3, name, code.Length, origin, 32768)));
        tap.AddRange(Block(Data(code)));
        return tap.ToArray();
    }

    private static byte[] Header(byte type, string name, int length, int param1, int param2)
    {
        var h = new byte[18];
        h[0] = type;
        var n = Encoding.ASCII.GetBytes(name.PadRight(10)[..10]);
        n.CopyTo(h, 1);
        h[11] = (byte)length; h[12] = (byte)(length >> 8);
        h[13] = (byte)param1; h[14] = (byte)(param1 >> 8);
        h[15] = (byte)param2; h[16] = (byte)(param2 >> 8);
        return Flagged(0x00, h[..17]);
    }

    private static byte[] Data(byte[] data) => Flagged(0xFF, data);

    /// <summary>Flag byte, contents and the XOR checksum of both.</summary>
    private static byte[] Flagged(byte flag, byte[] body)
    {
        var b = new byte[body.Length + 2];
        b[0] = flag;
        body.CopyTo(b, 1);
        byte x = flag;
        foreach (byte v in body) x ^= v;
        b[^1] = x;
        return b;
    }

    /// <summary>A .tap block: two bytes of length, then the block.</summary>
    private static byte[] Block(byte[] b) => new[] { (byte)b.Length, (byte)(b.Length >> 8) }.Concat(b).ToArray();
}
