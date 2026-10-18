using System.Text;
using System.Text.Json;
using Retro.Cpu.M6502;

namespace Cpu6502.Tests;

/// <summary>Flat 64 KB memory that records every bus cycle.</summary>
internal sealed class TestBus : IM6502Bus
{
    public readonly byte[] Memory = new byte[0x10000];
    public readonly List<(ushort address, byte value, bool read)> Log = new();
    public bool Logging;
    public bool IrqLine { get; set; }
    public bool NmiLine { get; set; }

    public byte Read(ushort address)
    {
        byte v = Memory[address];
        if (Logging) Log.Add((address, v, true));
        return v;
    }

    public void Write(ushort address, byte value)
    {
        Memory[address] = value;
        if (Logging) Log.Add((address, value, false));
    }
}

[TestFixture]
public class ConformanceTests
{
    private static string Data(string sub)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "testdata", "6502")))
            dir = dir.Parent;
        if (dir == null)
            Assert.Ignore("Faltan las pruebas en testdata/6502.");
        return Path.Combine(dir!.FullName, "testdata", "6502", sub);
    }

    /// <summary>
    /// Tom Harte's SingleStepTests (NMOS 6502, the first 1000 cases of each of the 256
    /// opcodes): registers, memory and every bus cycle (address, value, read or write).
    /// </summary>
    [Test]
    public void Harte_EveryOpcode_EveryCycle()
    {
        string folder = Data("harte");
        var failures = new Dictionary<string, (int count, string first)>();
        int total = 0;
        foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            foreach (var test in doc.RootElement.EnumerateArray())
            {
                total++;
                string? error = RunCase(test);
                if (error == null)
                    continue;
                string op = Path.GetFileNameWithoutExtension(file);
                failures[op] = failures.TryGetValue(op, out var f) ? (f.count + 1, f.first) : (1, test.GetProperty("name").GetString() + ": " + error);
            }
        }
        TestContext.Out.WriteLine($"{total} casos, {failures.Values.Sum(f => f.count)} con error en {failures.Count} opcodes");
        foreach (var (op, (count, first)) in failures.OrderBy(f => f.Key))
            TestContext.Out.WriteLine($"  {op}: {count} errores. Primero: {first}");
        Assert.That(failures, Is.Empty);
    }

    private static readonly TestBus Bus = new();

    private static string? RunCase(JsonElement test)
    {
        var bus = Bus;
        var initial = test.GetProperty("initial");
        var final = test.GetProperty("final");
        foreach (var cell in initial.GetProperty("ram").EnumerateArray())
            bus.Memory[cell[0].GetInt32()] = (byte)cell[1].GetInt32();
        var cpu = new M6502(bus)
        {
            PC = (ushort)initial.GetProperty("pc").GetInt32(),
            S = (byte)initial.GetProperty("s").GetInt32(),
            A = (byte)initial.GetProperty("a").GetInt32(),
            X = (byte)initial.GetProperty("x").GetInt32(),
            Y = (byte)initial.GetProperty("y").GetInt32(),
            P = (byte)initial.GetProperty("p").GetInt32(),
        };
        bus.Log.Clear();
        bus.Logging = true;
        cpu.Step();
        bus.Logging = false;

        var sb = new StringBuilder();
        void Check(string name, int got, string key)
        {
            int want = final.GetProperty(key).GetInt32();
            if (got != want)
                sb.Append($"{name}={got:X2} (esperado {want:X2}) ");
        }
        Check("PC", cpu.PC, "pc");
        Check("S", cpu.S, "s");
        Check("A", cpu.A, "a");
        Check("X", cpu.X, "x");
        Check("Y", cpu.Y, "y");
        Check("P", cpu.P, "p");
        foreach (var cell in final.GetProperty("ram").EnumerateArray())
        {
            int a = cell[0].GetInt32(), v = cell[1].GetInt32();
            if (bus.Memory[a] != v)
                sb.Append($"[{a:X4}]={bus.Memory[a]:X2} (esperado {v:X2}) ");
        }
        var cycles = test.GetProperty("cycles");
        int n = cycles.GetArrayLength();
        if (n != bus.Log.Count)
            sb.Append($"ciclos={bus.Log.Count} (esperado {n}) ");
        else
        {
            int i = 0;
            foreach (var c in cycles.EnumerateArray())
            {
                var (address, value, read) = bus.Log[i++];
                bool wantRead = c[2].GetString() == "read";
                if (address != c[0].GetInt32() || value != c[1].GetInt32() || read != wantRead)
                {
                    sb.Append($"ciclo {i}: {(read ? "lee" : "escribe")} {address:X4}={value:X2}, esperado {(wantRead ? "lee" : "escribe")} {c[0].GetInt32():X4}={c[1].GetInt32():X2}");
                    break;
                }
            }
        }
        // leave memory clean for the next case
        foreach (var cell in initial.GetProperty("ram").EnumerateArray())
            bus.Memory[cell[0].GetInt32()] = 0;
        foreach (var (address, _, _) in bus.Log)
            bus.Memory[address] = 0;
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>Klaus Dormann's 6502 functional test: every instruction and addressing mode, decimal mode included. Ends in a JMP * at $3469 when everything passes.</summary>
    [Test]
    public void Dormann_FunctionalTest()
    {
        var bus = new TestBus();
        File.ReadAllBytes(Data(Path.Combine("dormann", "6502_functional_test.bin"))).CopyTo(bus.Memory, 0);
        var cpu = new M6502(bus) { PC = 0x0400 };
        ushort last = 0xFFFF;
        long steps = 0;
        while (steps++ < 200_000_000)
        {
            ushort before = cpu.PC;
            cpu.Step();
            if (cpu.PC == before)
                break;   // JMP * : success or a failure trap
            last = before;
        }
        TestContext.Out.WriteLine($"terminó en ${cpu.PC:X4} tras {cpu.Instructions} instrucciones y {cpu.CycleCount} ciclos (prueba número {bus.Memory[0x0200]:X2})");
        Assert.That(cpu.PC, Is.EqualTo(0x3469), $"falló la prueba {bus.Memory[0x0200]:X2} (trampa en ${cpu.PC:X4}, antes ${last:X4})");
    }

    [Test]
    public void Interrupts_IrqIsMaskedByI_AndNmiIsEdgeTriggered()
    {
        var bus = new TestBus();
        // $0200: NOP NOP NOP ... ; IRQ handler at $0300, NMI handler at $0400
        for (int i = 0; i < 16; i++) bus.Memory[0x0200 + i] = 0xEA;
        bus.Memory[0x0300] = 0xEA;
        bus.Memory[0x0400] = 0xEA;
        bus.Memory[0xFFFE] = 0x00; bus.Memory[0xFFFF] = 0x03;
        bus.Memory[0xFFFA] = 0x00; bus.Memory[0xFFFB] = 0x04;
        var cpu = new M6502(bus) { PC = 0x0200, P = 0x24, S = 0xFF };
        bus.IrqLine = true;
        cpu.Step(); cpu.Step();
        Assert.That(cpu.PC, Is.EqualTo(0x0202), "con I puesto, la IRQ se ignora");
        cpu.P = 0x20;
        cpu.Step();   // the line is sampled during this instruction
        cpu.Step();   // and taken at the next boundary
        Assert.That(cpu.PC, Is.EqualTo(0x0300), "sin I, la IRQ entra al terminar la instrucción siguiente");
        Assert.That(bus.Memory[0x01FD] & 0x30, Is.EqualTo(0x20), "la IRQ guarda P con B en 0 y el bit 5 en 1");
        Assert.That(cpu.P & M6502.FlagI, Is.Not.Zero);
        bus.IrqLine = false;
        bus.NmiLine = true;
        cpu.Step(); cpu.Step();
        Assert.That(cpu.PC, Is.EqualTo(0x0400), "la NMI entra aunque I esté puesto");
        cpu.PC = 0x0200;
        cpu.Step(); cpu.Step();
        Assert.That(cpu.PC, Is.EqualTo(0x0202), "con la línea NMI todavía activa no vuelve a entrar: es por flanco");
    }

    [TestCase(true, 0x10, 0x09)]
    [TestCase(false, 0x0A, 0x0F)]
    public void Decimal_The2A03KeepsTheDFlagButAddsInBinary(bool decimalEnabled, int sum, int difference)
    {
        var bus = new TestBus();
        // SED; CLC; LDA #$09; ADC #$01; STA $10; SEC; LDA #$10; SBC #$01; STA $11
        byte[] program = { 0xF8, 0x18, 0xA9, 0x09, 0x69, 0x01, 0x85, 0x10, 0x38, 0xA9, 0x10, 0xE9, 0x01, 0x85, 0x11 };
        program.CopyTo(bus.Memory, 0x0200);
        var cpu = new M6502(bus) { PC = 0x0200, DecimalEnabled = decimalEnabled };
        for (int i = 0; i < 9; i++) cpu.Step();
        Assert.That(bus.Memory[0x10], Is.EqualTo(sum), "ADC con D puesto");
        Assert.That(bus.Memory[0x11], Is.EqualTo(difference), "SBC con D puesto");
        Assert.That(cpu.P & M6502.FlagD, Is.Not.Zero, "el flag D se guarda igual en los dos");
    }
}

[TestFixture]
public class DisassemblerTests
{
    [Test]
    public void Lengths_MatchWhatTheCpuConsumes()
    {
        // every opcode in the Harte tests: the final PC of a non-jump instruction is PC + length
        var dis = new M6502Disassembler();
        var mem = new byte[0x10000];
        var samples = new Dictionary<int, string>();
        for (int op = 0; op < 256; op++)
        {
            mem[0x1000] = (byte)op;
            mem[0x1001] = 0x34;
            mem[0x1002] = 0x12;
            var ins = dis.Disassemble(a => mem[a], 0x1000);
            samples[op] = ins.Text;
        }
        Assert.That(samples[0xA9], Is.EqualTo("LDA #$34"));
        Assert.That(samples[0xBD], Is.EqualTo("LDA $1234,X"));
        Assert.That(samples[0xB1], Is.EqualTo("LDA ($34),Y"));
        Assert.That(samples[0x6C], Is.EqualTo("JMP ($1234)"));
        Assert.That(samples[0xD0], Is.EqualTo("BNE $1036"));
        Assert.That(samples[0xA7], Is.EqualTo("LAX $34"));
        Assert.That(samples[0x0A], Is.EqualTo("ASL A"));
        Assert.That(samples[0x02], Is.EqualTo("JAM"));
        Assert.That(samples[0x9E], Is.EqualTo("SHX $1234,Y"));
        Assert.That(samples.Values.Count(v => v.Length == 0), Is.Zero);
    }

    [Test]
    public void Lengths_AgreeWithTheCore()
    {
        var bus = new TestBus();
        for (int op = 0; op < 256; op++)
        {
            var (name, mode) = M6502Disassembler.Table[op];
            if (name is "JSR" or "JMP" or "RTS" or "RTI" or "BRK" or "JAM" || mode == M6502Mode.Rel)
                continue;
            Array.Clear(bus.Memory);
            bus.Memory[0x1000] = (byte)op;
            var cpu = new M6502(bus) { PC = 0x1000, S = 0xFF, P = 0x20 };
            cpu.Step();
            Assert.That(cpu.PC - 0x1000, Is.EqualTo(M6502Disassembler.Length(mode)), $"{op:X2} {name}");
        }
    }
}
