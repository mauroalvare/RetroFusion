using Retro.Cpu.I8088;

namespace I8088.Tests;

public sealed class VariantBusTests
{
    private sealed record Access(long Time, char Kind, uint Address, byte Value);
    private sealed class Bus : I8088Bus
    {
        public byte[] Memory = new byte[1 << 20];
        public List<Access> Accesses = [];
        public List<long> Grants = [];
        public long Clocks, HoldUntil;
        public Action<uint>? AfterWrite;
        public bool HoldRequested => Clocks < HoldUntil;
        public void Clock() => Clocks++;
        public void SetBusGranted(bool granted) { if (granted) Grants.Add(Clocks); }
        public byte ReadMemory(uint address)
        {
            byte value = Memory[address]; Accesses.Add(new(Clocks, 'R', address, value)); return value;
        }
        public void WriteMemory(uint address, byte value)
        {
            Memory[address] = value; Accesses.Add(new(Clocks, 'W', address, value)); AfterWrite?.Invoke(address);
        }
        public byte In(ushort port)
        {
            byte value = (byte)(port ^ 0x5A); Accesses.Add(new(Clocks, 'I', port, value)); return value;
        }
        public void Out(ushort port, byte value) => Accesses.Add(new(Clocks, 'O', port, value));
    }
    private static (I8088Cpu Cpu, Bus Bus) Program(I8088Model model, params byte[] code)
    {
        var bus = new Bus(); code.CopyTo(bus.Memory, 0x1000);
        return (new I8088Cpu(bus, model) { CS = 0x100, SS = 0, SP = 0x800, TimingEnabled = true }, bus);
    }
    private static byte[] Save(I8088Cpu cpu)
    {
        using var stream = new MemoryStream(); cpu.Save(new BinaryWriter(stream)); return stream.ToArray();
    }
    private static void Load(I8088Cpu cpu, byte[] state) => cpu.Load(new BinaryReader(new MemoryStream(state)));

    [TestCase(I8088Model.Intel8088, 0x100, 8)]
    [TestCase(I8088Model.Intel8088, 0x101, 8)]
    [TestCase(I8088Model.Intel8086, 0x100, 4)]
    [TestCase(I8088Model.Intel8086, 0x101, 8)]
    [TestCase(I8088Model.Intel8086, 0xFFFF, 8)]
    public void WordReadAndWriteUseTheModelsAlignedBusCycles(I8088Model model, int offset, int clocks)
    {
        var (cpu, bus) = Program(model);
        cpu.WriteWord(0xFFFF, (ushort)offset, 0xCAFE);
        Assert.That(cpu.Cycles, Is.EqualTo(clocks));
        Assert.That(bus.Accesses.Select(a => a.Address), Is.EqualTo(new[]
        {
            I8088Cpu.Physical(0xFFFF, (ushort)offset), I8088Cpu.Physical(0xFFFF, (ushort)(offset + 1))
        }));
        Assert.That(bus.Accesses.Select(a => a.Value), Is.EqualTo(new byte[] { 0xFE, 0xCA }));
        Assert.That(bus.Accesses[1].Time - bus.Accesses[0].Time, Is.EqualTo(clocks - 4));
        bus.Accesses.Clear();
        Assert.That(cpu.ReadWord(0xFFFF, (ushort)offset), Is.EqualTo(0xCAFE));
        Assert.That(cpu.Cycles, Is.EqualTo(2 * clocks));
        Assert.That(bus.Accesses[1].Time - bus.Accesses[0].Time, Is.EqualTo(clocks - 4));
        Assert.That(cpu.Instructions, Is.Zero);
    }

    [TestCase(0x100)]
    [TestCase(0x101)]
    public void HoldCannotSplitAn8086WordTransfer(int offset)
    {
        var (cpu, bus) = Program(I8088Model.Intel8086);
        bus.AfterWrite = address => { if (address == offset) bus.HoldUntil = 12; };
        cpu.WriteWord(0, (ushort)offset, 0x1234);
        Assert.That(bus.Grants, Is.Empty);
        Assert.That(bus.Accesses[1].Time, Is.EqualTo(offset == 0x100 ? 3 : 7));
        cpu.ReadByte(0, 0x200);
        Assert.That(bus.Grants, Is.Not.Empty);
        Assert.That(bus.Accesses[^1].Time, Is.EqualTo(15));
    }

    [TestCase(I8088Model.Intel8088, 0x80, 4)]
    [TestCase(I8088Model.Intel8088, 0x81, 4)]
    [TestCase(I8088Model.Intel8086, 0x80, 0)]
    [TestCase(I8088Model.Intel8086, 0x81, 4)]
    [TestCase(I8088Model.Intel8086, 0xFFFF, 4)]
    public void WordIoRespectsAlignmentAndWrapsAtTheLastPort(I8088Model model, int port, int gap)
    {
        var (cpu, bus) = Program(model, 0xED, 0xEF); cpu.DX = (ushort)port;
        cpu.Step(); cpu.Step();
        var input = bus.Accesses.Where(a => a.Kind == 'I').ToArray();
        var output = bus.Accesses.Where(a => a.Kind == 'O').ToArray();
        byte low = (byte)(port ^ 0x5A), high = (byte)((ushort)(port + 1) ^ 0x5A);
        Assert.That(cpu.AX, Is.EqualTo(low | high << 8));
        foreach (var pair in new[] { input, output })
        {
            Assert.That(pair.Select(a => a.Address), Is.EqualTo(new uint[] { (ushort)port, (ushort)(port + 1) }));
            Assert.That(pair.Select(a => a.Value), Is.EqualTo(new[] { low, high }));
            Assert.That(pair[1].Time - pair[0].Time, Is.EqualTo(gap));
        }
    }

    [TestCase(I8088Model.Intel8088)]
    [TestCase(I8088Model.Intel8086)]
    public void ByteIoPreservesAhAndUsesOnlyTheSelectedPort(I8088Model model)
    {
        var (cpu, bus) = Program(model, 0xE4, 0x81, 0xE6, 0x81); cpu.AX = 0xAB00;
        cpu.Step(); cpu.Step();
        Assert.That(cpu.AX, Is.EqualTo(0xABDB));
        Assert.That(bus.Accesses.Where(a => a.Kind is 'I' or 'O').Select(a => a.Address), Is.EqualTo(new uint[] { 0x81, 0x81 }));
    }

    [TestCase(I8088Model.Intel8088, 6, 99)]
    [TestCase(I8088Model.Intel8086, 8, 33)]
    public void SixByteQueueKeepsItsLastPrefetchedOperandStale(I8088Model model, int reads, int last)
    {
        var (cpu, bus) = Program(model, 0xD4, 10, 0xB0, 11, 0xB3, 22, 0xB1, 33, 0xF4);
        cpu.AX = 23; cpu.Step();
        Assert.That(bus.Accesses.Count, Is.EqualTo(reads));
        bus.Memory[0x1007] = 99;
        cpu.Step(); cpu.Step(); cpu.Step();
        Assert.That(cpu.AX & 255, Is.EqualTo(11));
        Assert.That(cpu.BX & 255, Is.EqualTo(22));
        Assert.That(cpu.CX & 255, Is.EqualTo(last));
    }

    [Test]
    public void FiveQueuedBytesDoNotStartAnotherWordPrefetch()
    {
        var (cpu, bus) = Program(I8088Model.Intel8086, 0xD4, 10, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xF4);
        cpu.Step(); int full = bus.Accesses.Count;
        cpu.Step(); Assert.That(bus.Accesses.Count, Is.EqualTo(full));
        cpu.Step(); Assert.That(bus.Accesses.Count, Is.EqualTo(full + 2), "The data is sampled at T3 while the transfer awaits T4.");
        cpu.Step(); Assert.That(bus.Accesses.Count, Is.EqualTo(full + 2));
        Assert.That(bus.Accesses[^1].Time, Is.EqualTo(bus.Accesses[^2].Time));
    }

    [TestCase(0x100, 0xFFFF)]
    [TestCase(0xFFFF, 0xF)]
    public void OddInstructionFetchStartsWithOneByteThenAlignedWordsAndWraps(int segment, int offset)
    {
        var (cpu, bus) = Program(I8088Model.Intel8086); cpu.CS = (ushort)segment; cpu.IP = (ushort)offset;
        byte[] code = [0xB8, 0x34, 0x12];
        for (int i = 0; i < code.Length; i++) bus.Memory[I8088Cpu.Physical((ushort)segment, (ushort)(offset + i))] = code[i];
        cpu.Step();
        Assert.That(cpu.AX, Is.EqualTo(0x1234));
        Assert.That(cpu.IP, Is.EqualTo((offset + 3) & 65535));
        Assert.That(bus.Accesses.Take(3).Select(a => a.Address), Is.EqualTo(Enumerable.Range(0, 3)
            .Select(i => I8088Cpu.Physical((ushort)segment, (ushort)(offset + i)))));
        Assert.That(bus.Accesses[0].Time, Is.EqualTo(3));
        Assert.That(bus.Accesses[1].Time, Is.EqualTo(7));
        Assert.That(bus.Accesses[2].Time, Is.EqualTo(7));
    }

    [Test]
    public void BranchFlushesTheQueueAndFetchesAnOddTargetBeforeAlignedWords()
    {
        var (cpu, bus) = Program(I8088Model.Intel8086, 0xEB, 1, 0x90, 0xB8, 0x34, 0x12, 0xF4);
        cpu.Step();
        var target = bus.Accesses.Where(a => a.Address >= 0x1003).Take(3).ToArray();
        Assert.That(target.Select(a => a.Address), Is.EqualTo(new uint[] { 0x1003, 0x1004, 0x1005 }));
        Assert.That(target[1].Time - target[0].Time, Is.EqualTo(4));
        Assert.That(target[2].Time, Is.EqualTo(target[1].Time));
        cpu.Step(); Assert.That(cpu.AX, Is.EqualTo(0x1234));
    }

    [TestCase(0)]
    [TestCase(2)]
    public void StateRestoresTheSixByteQueueIncludingAPartialPrefetch(int nops)
    {
        var code = new byte[30]; Array.Fill(code, (byte)0x90); code[0] = 0xD4; code[1] = 10;
        var (cpu, bus) = Program(I8088Model.Intel8086, code);
        cpu.Step(); for (int i = 0; i < nops; i++) cpu.Step();
        byte[] state = Save(cpu);
        var secondBus = new Bus { Memory = (byte[])bus.Memory.Clone(), Clocks = bus.Clocks };
        var second = new I8088Cpu(secondBus, I8088Model.Intel8086); Load(second, state);
        bus.Accesses.Clear();
        for (int i = 0; i < 8; i++) { cpu.Step(); second.Step(); }
        Assert.That(Save(second), Is.EqualTo(Save(cpu)));
        Assert.That(secondBus.Accesses, Is.EqualTo(bus.Accesses));
        byte[] before = Save(second);
        Assert.Catch<IOException>(() => Load(second, state[..^1]));
        Assert.That(Save(second), Is.EqualTo(before));
    }

    [TestCase(I8088Model.Intel8088, I8088Model.Intel8086)]
    [TestCase(I8088Model.Intel8086, I8088Model.Intel8088)]
    public void StateFromAnotherModelIsRejectedWithoutMutation(I8088Model source, I8088Model target)
    {
        var (cpu, _) = Program(source, 0x90); cpu.Step();
        var (other, _) = Program(target, 0x90); byte[] before = Save(other);
        Assert.Throws<InvalidDataException>(() => Load(other, Save(cpu)));
        Assert.That(Save(other), Is.EqualTo(before));
    }

    [Test]
    public void DefaultModelRemains8088AndInvalidModelsAreRejected()
    {
        Assert.That(new I8088Cpu(new Bus()).Model, Is.EqualTo(I8088Model.Intel8088));
        Assert.That(new I8088Cpu(new Bus(), I8088Model.Intel8086).CpuName, Is.EqualTo("Intel 8086"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new I8088Cpu(new Bus(), (I8088Model)123));
    }

    [TestCase(I8088Model.Intel8088)]
    [TestCase(I8088Model.Intel8086)]
    public void ClockedRepRestoresItsPrefixAfterInterruptAndResumesFromSavedState(I8088Model model)
    {
        var (cpu, bus) = Program(model, 0xF3, 0xA5, 0xF4);
        cpu.DS = 0x200; cpu.ES = 0x300; cpu.SI = 0x101; cpu.DI = 0x202; cpu.CX = 3;
        bus.Memory[8] = 0; bus.Memory[9] = 4; bus.Memory[10] = 0; bus.Memory[11] = 0;
        bus.Memory[0x400] = 0xCF;
        byte[] source = [0x34, 0x12, 0x78, 0x56, 0xBC, 0x9A]; source.CopyTo(bus.Memory, 0x2101);
        cpu.Step();
        Assert.That(cpu.IP, Is.Zero); Assert.That(cpu.CX, Is.EqualTo(2));
        byte[] state = Save(cpu);
        var secondBus = new Bus { Memory = (byte[])bus.Memory.Clone(), Clocks = bus.Clocks };
        var second = new I8088Cpu(secondBus, model); Load(second, state);
        foreach (var current in new[] { cpu, second })
        {
            current.RequestNmi(); current.Step();
            Assert.That(current.IP, Is.EqualTo(0x400));
            current.Step();
            Assert.That(current.IP, Is.Zero); Assert.That(current.SP, Is.EqualTo(0x800));
            current.Step(); current.Step(); current.Step();
            Assert.That(current.Halted, Is.True);
            Assert.That((current.CX, current.SI, current.DI), Is.EqualTo((0, 0x107, 0x208)));
            Assert.That(current.Instructions, Is.EqualTo(5));
        }
        Assert.That(bus.Memory.AsSpan(0x3202, 6).ToArray(), Is.EqualTo(source));
        Assert.That(secondBus.Memory, Is.EqualTo(bus.Memory));
        Assert.That(Save(second), Is.EqualTo(Save(cpu)));
    }

    [Test]
    public void Legacy8088StateStillLoadsAndCannotBeMistakenFor8086()
    {
        // Format 1 fixture predates clocks/prefetch and ends immediately after WAIT.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("RETRO-8088"); writer.Write(1);
            foreach (ushort value in new ushort[] { 0x1234, 0, 0, 0, 0x800, 0, 0, 0 }) writer.Write(value);
            foreach (ushort value in new ushort[] { 0, 0x100, 0, 0 }) writer.Write(value);
            writer.Write((ushort)0); writer.Write((ushort)0xF002); writer.Write(false); writer.Write(7L);
            for (int i = 0; i < 6; i++) writer.Write(false);
        }
        var (cpu, _) = Program(I8088Model.Intel8088, 0x90); Load(cpu, stream.ToArray());
        Assert.That(cpu.AX, Is.EqualTo(0x1234)); Assert.That(cpu.TimingEnabled, Is.False);
        Assert.That(cpu.Instructions, Is.EqualTo(7)); cpu.Step(); Assert.That(cpu.IP, Is.EqualTo(1));
        var (wide, _) = Program(I8088Model.Intel8086, 0x90); byte[] before = Save(wide);
        Assert.Throws<InvalidDataException>(() => Load(wide, stream.ToArray()));
        Assert.That(Save(wide), Is.EqualTo(before));
    }
}
