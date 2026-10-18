using System.Globalization;
using System.Text;
using Retro.Cpu.Z80;

namespace Retro.Cpu.Z80.Tests;

/// <summary>
/// The FUSE emulator's Z80 test suite: for every opcode it checks final registers,
/// memory and the exact T-state of every bus cycle (memory contention points,
/// reads, writes and port accesses).
/// </summary>
[TestFixture]
public class FuseTests
{
    private static readonly string DataDir = Path.Combine(TestPaths.Root, "testdata", "fuse");

    public sealed record FuseCase(string Name, string[] Input, string Expected)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<TestCaseData> Cases()
    {
        var inputs = SplitBlocks(File.ReadAllText(Path.Combine(DataDir, "tests.in")));
        var expected = SplitBlocks(File.ReadAllText(Path.Combine(DataDir, "tests.expected")))
            .ToDictionary(b => b[0].Trim(), b => string.Join("\n", b.Skip(1)));
        foreach (var block in inputs)
        {
            string name = block[0].Trim();
            yield return new TestCaseData(new FuseCase(name, block, expected[name])).SetName("fuse_" + name);
        }
    }

    private static List<string[]> SplitBlocks(string text)
    {
        var blocks = new List<string[]>();
        var current = new List<string>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            if (raw.Trim().Length == 0)
            {
                if (current.Count > 0)
                    blocks.Add(current.ToArray());
                current.Clear();
            }
            else
            {
                current.Add(raw);
            }
        }
        if (current.Count > 0)
            blocks.Add(current.ToArray());
        return blocks;
    }

    private sealed class FuseBus : IZ80Bus
    {
        public readonly byte[] Memory = new byte[0x10000];
        public readonly List<string> Events = new();
        public Z80Cpu Cpu = null!;

        private void Log(long t, string type, ushort addr) =>
            Events.Add($"{t,5} {type} {addr:x4}");

        private void Log(long t, string type, ushort addr, byte value) =>
            Events.Add($"{t,5} {type} {addr:x4} {value:x2}");

        public byte ReadMemory(ushort address)
        {
            byte v = Memory[address];
            Log(Cpu.T, "MR", address, v);
            return v;
        }

        public void WriteMemory(ushort address, byte value)
        {
            Memory[address] = value;
            Log(Cpu.T, "MW", address, value);
        }

        public int Contention(ushort address, long tstate)
        {
            Log(tstate, "MC", address);
            return 0;
        }

        public byte ReadPort(ushort port)
        {
            byte v = (byte)(port >> 8);
            Log(Cpu.T, "PR", port, v);
            return v;
        }

        public void WritePort(ushort port, byte value) => Log(Cpu.T, "PW", port, value);

        public int PortTimingPre(ushort port, long tstate)
        {
            if ((port & 0xC000) == 0x4000)
                Log(tstate, "PC", port);
            return 1;
        }

        public int PortTimingPost(ushort port, long tstate)
        {
            if ((port & 0x0001) != 0)
            {
                if ((port & 0xC000) == 0x4000)
                {
                    Log(tstate, "PC", port);
                    Log(tstate + 1, "PC", port);
                    Log(tstate + 2, "PC", port);
                }
                return 3;
            }
            Log(tstate, "PC", port);
            return 3;
        }

        public byte InterruptDataBus => 0xFF;
    }

    private static ushort Hex16(string s) => ushort.Parse(s, NumberStyles.HexNumber);
    private static byte Hex8(string s) => byte.Parse(s, NumberStyles.HexNumber);

    [TestCaseSource(nameof(Cases))]
    public void Run(FuseCase test)
    {
        var bus = new FuseBus();
        for (int i = 0; i < 0x10000; i += 4)
        {
            bus.Memory[i] = 0xDE;
            bus.Memory[i + 1] = 0xAD;
            bus.Memory[i + 2] = 0xBE;
            bus.Memory[i + 3] = 0xEF;
        }
        var cpu = new Z80Cpu(bus);
        bus.Cpu = cpu;

        var regs = test.Input[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        cpu.AF = Hex16(regs[0]); cpu.BC = Hex16(regs[1]); cpu.DE = Hex16(regs[2]); cpu.HL = Hex16(regs[3]);
        cpu.AF_ = Hex16(regs[4]); cpu.BC_ = Hex16(regs[5]); cpu.DE_ = Hex16(regs[6]); cpu.HL_ = Hex16(regs[7]);
        cpu.IX = Hex16(regs[8]); cpu.IY = Hex16(regs[9]); cpu.SP = Hex16(regs[10]); cpu.PC = Hex16(regs[11]);
        cpu.WZ = Hex16(regs[12]);
        var state = test.Input[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        cpu.I = Hex8(state[0]);
        cpu.R = Hex8(state[1]);
        cpu.IFF1 = state[2] != "0";
        cpu.IFF2 = state[3] != "0";
        cpu.IM = int.Parse(state[4]);
        cpu.Halted = state[5] != "0";
        long end = long.Parse(state[6]);
        cpu.T = 0;
        cpu.Q = 0;

        foreach (var line in test.Input.Skip(3))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] == "-1")
                break;
            ushort addr = Hex16(parts[0]);
            foreach (var b in parts.Skip(1))
            {
                if (b == "-1")
                    break;
                bus.Memory[addr++] = Hex8(b);
            }
        }
        byte[] initial = (byte[])bus.Memory.Clone();

        while (cpu.T < end)
            cpu.Step();

        var sb = new StringBuilder();
        foreach (var e in bus.Events)
            sb.Append(e).Append('\n');
        sb.Append($"{cpu.AF:x4} {cpu.BC:x4} {cpu.DE:x4} {cpu.HL:x4} {cpu.AF_:x4} {cpu.BC_:x4} {cpu.DE_:x4} {cpu.HL_:x4} " +
                  $"{cpu.IX:x4} {cpu.IY:x4} {cpu.SP:x4} {cpu.PC:x4} {cpu.WZ:x4}\n");
        sb.Append($"{cpu.I:x2} {cpu.R:x2} {(cpu.IFF1 ? 1 : 0)} {(cpu.IFF2 ? 1 : 0)} {cpu.IM} {(cpu.Halted ? 1 : 0)} {cpu.T}\n");
        for (int a = 0; a < 0x10000;)
        {
            if (bus.Memory[a] == initial[a]) { a++; continue; }
            sb.Append($"{a:x4} ");
            while (a < 0x10000 && bus.Memory[a] != initial[a])
                sb.Append($"{bus.Memory[a++]:x2} ");
            sb.Append("-1\n");
        }

        string actual = Normalize(sb.ToString());
        string expected = Normalize(test.Expected);
        Assert.That(actual, Is.EqualTo(expected), $"Prueba FUSE {test.Name}");
    }

    private static string Normalize(string s) =>
        string.Join("\n", s.Replace("\r", "").Split('\n')
            .Select(l => string.Join(" ", l.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(l => l.Length > 0));
}
