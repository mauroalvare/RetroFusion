using System.Diagnostics;
using System.Text;
using Retro.Cpu.I8080;

namespace I8080.Tests;

/// <summary>
/// Classic 8080 diagnostics run under a minimal CP/M: TST8080 (Microcosm), 8080PRE and
/// 8080EXM (Bartholomew/Cringle exerciser with CRCs from a real 8080) and CPUTEST
/// (SuperSoft Diagnostics II). Besides the printed result, the total number of
/// instructions and clock states must match the published values exactly, which checks
/// the cycle count of every instruction executed.
/// </summary>
[TestFixture]
public class CpmConformanceTests
{
    private static string DataDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "testdata", "8080")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "testdata", "8080");
        }
    }

    private sealed class CpmBus : I8080Bus
    {
        public readonly byte[] Memory = new byte[0x10000];
        public readonly StringBuilder Output = new();
        public bool Finished;
        public I8080Cpu Cpu = null!;

        public byte ReadMemory(ushort address) => Memory[address];
        public void WriteMemory(ushort address, byte value) => Memory[address] = value;
        public byte In(byte port) => 0x00;

        public void Out(byte port, byte value)
        {
            if (port == 0)
                Finished = true;
            else if (port == 1)
            {
                // BDOS: C=2 print the character in E, C=9 print the string at DE up to '$'.
                if (Cpu.C == 2)
                    Output.Append((char)Cpu.E);
                else if (Cpu.C == 9)
                    for (ushort a = Cpu.DE; Memory[a] != '$'; a++)
                        Output.Append((char)Memory[a]);
            }
        }
    }

    private static (string output, long instructions, long cycles) Run(string file)
    {
        var bus = new CpmBus();
        string path = Path.Combine(DataDir, file);
        if (!File.Exists(path))
            Assert.Ignore($"Falta {file} en testdata/8080 (no se distribuye con el código; se puede agregar a mano).");
        File.ReadAllBytes(path).CopyTo(bus.Memory, 0x100);
        bus.Memory[0x0000] = 0xD3;   // OUT 0,A : warm boot = end of test
        bus.Memory[0x0001] = 0x00;
        bus.Memory[0x0005] = 0xD3;   // OUT 1,A : BDOS call
        bus.Memory[0x0006] = 0x01;
        bus.Memory[0x0007] = 0xC9;   // RET
        var cpu = new I8080Cpu(bus) { PC = 0x0100 };
        bus.Cpu = cpu;
        while (!bus.Finished)
            cpu.Step();
        return (bus.Output.ToString(), cpu.Instructions, cpu.Cycles);
    }

    [Test]
    public void Tst8080()
    {
        var (output, instructions, cycles) = Run("TST8080.COM");
        TestContext.Out.WriteLine(output);
        Assert.That(output, Does.Contain("CPU IS OPERATIONAL"));
        Assert.That(instructions, Is.EqualTo(651));
        Assert.That(cycles, Is.EqualTo(4924));
    }

    [Test]
    public void Preliminary8080()
    {
        var (output, instructions, cycles) = Run("8080PRE.COM");
        TestContext.Out.WriteLine(output);
        Assert.That(output, Does.Contain("8080 Preliminary tests complete"));
        Assert.That(instructions, Is.EqualTo(1061));
        Assert.That(cycles, Is.EqualTo(7817));
    }

    [Test]
    public void SuperSoftCpuTest()
    {
        var (output, instructions, cycles) = Run("CPUTEST.COM");
        TestContext.Out.WriteLine(output);
        Assert.That(output, Does.Contain("CPU IS 8080/8085"));
        Assert.That(output, Does.Contain("CPU TESTS OK"));
        Assert.That(instructions, Is.EqualTo(33971311));
        Assert.That(cycles, Is.EqualTo(255653383));
    }

    [Test]
    [Category("Long")]
    public void Exerciser8080Exm()
    {
        var sw = Stopwatch.StartNew();
        var (output, instructions, cycles) = Run("8080EXM.COM");
        TestContext.Out.WriteLine(output);
        TestContext.Out.WriteLine($"{instructions:N0} instrucciones, {cycles:N0} ciclos en {sw.Elapsed.TotalSeconds:F1} s");
        Assert.That(output, Does.Contain("Tests complete"));
        Assert.That(output, Does.Not.Contain("ERROR"));
        Assert.That(instructions, Is.EqualTo(2919050698));
        Assert.That(cycles, Is.EqualTo(23803381171));
    }
}
