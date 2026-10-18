using System.Diagnostics;
using System.Text;
using Retro.Cpu.Z80;

namespace Retro.Cpu.Z80.Tests;

/// <summary>
/// ZEXDOC / ZEXALL (Frank Cringle): run every instruction group over thousands of
/// operand combinations and compare a CRC of the results with values recorded on a
/// real Z80. ZEXDOC checks documented flags only, ZEXALL also flags 3 and 5.
/// They are CP/M programs, so a minimal BDOS (console output) is provided.
/// </summary>
[TestFixture]
[Category("Long")]
public class ZexTests
{
    [TestCase("zexdoc.com")]
    [TestCase("zexall.com")]
    public void Exerciser(string file)
    {
        var bus = new SimpleZ80Bus();
        byte[] program = File.ReadAllBytes(Path.Combine(TestPaths.Root, "testdata", "zex", file));
        program.CopyTo(bus.Memory, 0x100);
        bus.Memory[0x0000] = 0x76;   // warm boot: HALT
        bus.Memory[0x0005] = 0xC9;   // BDOS entry: RET (the call is trapped below)
        bus.Memory[0x0006] = 0x00;   // top of TPA, used by the program to set SP
        bus.Memory[0x0007] = 0xF0;

        var cpu = new Z80Cpu(bus) { PC = 0x0100, SP = 0xF000 };
        var output = new StringBuilder();
        var sw = Stopwatch.StartNew();
        long instructions = 0;
        while (true)
        {
            if (cpu.PC == 0x0005)
            {
                if (cpu.C == 2)
                    output.Append((char)cpu.E);
                else if (cpu.C == 9)
                    for (ushort a = cpu.DE; bus.Memory[a] != '$'; a++)
                        output.Append((char)bus.Memory[a]);
            }
            if (cpu.PC == 0x0000)
                break;
            cpu.Step();
            instructions++;
        }
        sw.Stop();
        string text = output.ToString();
        TestContext.Out.WriteLine(text);
        TestContext.Out.WriteLine($"{instructions:N0} instrucciones, {cpu.T:N0} T-states en {sw.Elapsed.TotalSeconds:F1} s " +
                                  $"({cpu.T / sw.Elapsed.TotalSeconds / 1e6:F0} MHz Z80 equivalentes)");
        Assert.That(text, Does.Contain("Tests complete"));
        Assert.That(text, Does.Not.Contain("ERROR"));
    }
}
