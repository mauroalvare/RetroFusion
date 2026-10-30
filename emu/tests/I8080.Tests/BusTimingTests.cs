using Retro.Cpu.I8080;

namespace I8080.Tests;

public sealed class BusTimingTests
{
    private sealed class Bus : I8080Bus
    {
        public byte[] Memory = new byte[65536];
        public int Clocks, ReleaseAt, WriteReleaseAt;
        public List<(char Kind, int Clock, ushort Address, byte Value)> Accesses = [];
        public int Reads;
        public void Clock() => Clocks++;
        public bool Ready => Clocks >= ReleaseAt && (Reads < 2 || Clocks >= WriteReleaseAt);
        public void InterruptAcknowledge(byte opcode) => Accesses.Add(('A', Clocks, 0, opcode));
        public byte ReadMemory(ushort address) { Reads++; byte v=Memory[address];Accesses.Add(('R',Clocks,address,v));return v; }
        public void WriteMemory(ushort address,byte value) { Memory[address]=value;Accesses.Add(('W',Clocks,address,value)); }
        public byte In(byte port) { Accesses.Add(('I',Clocks,port,0x42));return 0x42; }
        public void Out(byte port,byte value) => Accesses.Add(('O',Clocks,port,value));
    }
    private static (I8080Cpu Cpu, Bus Bus) Program(byte[] bytes, bool timed=true)
    {
        var bus=new Bus();bytes.CopyTo(bus.Memory,0x100);bus.Memory[0x4000]=0x34;bus.Memory[0x4001]=0x12;
        var cpu=new I8080Cpu(bus){PC=0x100,SP=0x4000,HL=0x2000,BC=0x3000,A=0x5A};
        typeof(I8080Cpu).GetProperty("TimingEnabled")?.SetValue(cpu,timed);
        return(cpu,bus);
    }
    [TestCase(new byte[]{0x21,0x34,0x12},10,"RRR",new int[]{3,7,10})]
    [TestCase(new byte[]{0x36,0x55},10,"RRW",new int[]{3,7,10})]
    [TestCase(new byte[]{0x22,0,0x30},16,"RRRWW",new int[]{3,7,10,13,16})]
    [TestCase(new byte[]{0x2A,0,0x30},16,"RRRRR",new int[]{3,7,10,13,16})]
    [TestCase(new byte[]{0x77},7,"RW",new int[]{3,7})]
    [TestCase(new byte[]{0x34},10,"RRW",new int[]{3,7,10})]
    [TestCase(new byte[]{0xC5},11,"RWW",new int[]{3,8,11})]
    [TestCase(new byte[]{0xC1},10,"RRR",new int[]{3,7,10})]
    [TestCase(new byte[]{0xCD,0x34,0x12},17,"RRRWW",new int[]{3,8,11,14,17})]
    [TestCase(new byte[]{0xC9},10,"RRR",new int[]{3,7,10})]
    [TestCase(new byte[]{0xD3,0x10},10,"RRO",new int[]{3,7,10})]
    [TestCase(new byte[]{0xDB,0x10},10,"RRI",new int[]{3,7,10})]
    [TestCase(new byte[]{0xE3},18,"RRRWW",new int[]{3,7,10,13,16})]
    [TestCase(new byte[]{0x09},10,"R",new int[]{3})]
    [TestCase(new byte[]{0x00},4,"R",new int[]{3})]
    [TestCase(new byte[]{0x78},5,"R",new int[]{3})]
    public void AccessesOccurWithinTheirMachineCycles(byte[] code,int total,string kinds,int[] clocks)
    {
        var(cpu,bus)=Program(code);Assert.That(cpu.Step(),Is.EqualTo(total));
        Assert.That(bus.Clocks,Is.EqualTo(total));Assert.That(cpu.Cycles,Is.EqualTo(total));
        Assert.That(string.Concat(bus.Accesses.Select(a=>a.Kind)),Is.EqualTo(kinds));
        Assert.That(bus.Accesses.Select(a=>a.Clock),Is.EqualTo(clocks));
    }
    [TestCase(true,11,new int[]{3,8,11})]
    [TestCase(false,17,new int[]{3,8,11,14,17})]
    public void ConditionalCallKeepsFiveStatesInM1(bool zero,int total,int[] clocks)
    {
        var(cpu,bus)=Program([0xC4,0x34,0x12]);cpu.Z=zero;
        Assert.That(cpu.Step(),Is.EqualTo(total));Assert.That(bus.Accesses.Select(a=>a.Clock),Is.EqualTo(clocks));
        Assert.That(bus.Clocks,Is.EqualTo(total));
    }
    [TestCase(true,5,new int[]{3})]
    [TestCase(false,11,new int[]{3,8,11})]
    public void ConditionalReturnIncludesItsDecisionBeforeStackReads(bool zero,int total,int[] clocks)
    {
        var(cpu,bus)=Program([0xC0]);cpu.Z=zero;
        Assert.That(cpu.Step(),Is.EqualTo(total));Assert.That(bus.Accesses.Select(a=>a.Clock),Is.EqualTo(clocks));
        Assert.That(bus.Clocks,Is.EqualTo(total));
    }
    [Test]
    public void ReadyWaitsPrecedeTheOnlyReadTransfer()
    {
        var(cpu,bus)=Program([0]);bus.ReleaseAt=6;
        Assert.That(cpu.Step(),Is.EqualTo(8));Assert.That(cpu.Cycles,Is.EqualTo(8));
        Assert.That(bus.Accesses.Single().Clock,Is.EqualTo(7));Assert.That(bus.Reads,Is.EqualTo(1));
    }
    [Test]
    public void ReadyAlsoDelaysWritesWithoutRepeatingThem()
    {
        var(cpu,bus)=Program([0x36,0x55]);bus.WriteReleaseAt=12;
        Assert.That(cpu.Step(),Is.EqualTo(13));Assert.That(bus.Accesses.Count(a=>a.Kind=='W'),Is.EqualTo(1));
        Assert.That(bus.Accesses[^1].Clock,Is.EqualTo(13));Assert.That(bus.Memory[0x2000],Is.EqualTo(0x55));
    }
    [Test]
    public void InterruptAcknowledgesWithoutFetchingOrAdvancingPc()
    {
        var(cpu,bus)=Program([0]);cpu.INTE=true;
        Assert.That(cpu.Interrupt(0xCF),Is.EqualTo(11));
        Assert.That(string.Concat(bus.Accesses.Select(a=>a.Kind)),Is.EqualTo("AWW"));
        Assert.That(bus.Accesses.Select(a=>a.Clock),Is.EqualTo(new[]{3,8,11}));
        Assert.That(cpu.PC,Is.EqualTo(8));Assert.That(bus.Memory[0x3FFE],Is.Zero);Assert.That(bus.Memory[0x3FFF],Is.EqualTo(1));
    }
    [Test]
    public void FunctionalModeKeepsItsLegacyTotalsAndDoesNotClockTheBus()
    {
        var(cpu,bus)=Program([0x21,0x34,0x12],false);
        Assert.That(cpu.Step(),Is.EqualTo(10));Assert.That(cpu.HL,Is.EqualTo(0x1234));
        Assert.That(cpu.Cycles,Is.EqualTo(10));Assert.That(bus.Clocks,Is.Zero);
    }
    [Test]
    public void TimedAndFunctionalPathsAgreeForAllOpcodesAndBothBranchOutcomes()
    {
        for(int opcode=0;opcode<256;opcode++)
        foreach(byte flags in new byte[]{0x02,0xD7})
        {
            byte[] code=[(byte)opcode,0,0x30];var(fast,fb)=Program(code,false);var(timed,tb)=Program(code);
            fast.F=timed.F=flags;int expected=fast.Step();int actual=timed.Step();
            Assert.That(actual,Is.EqualTo(expected),$"{opcode:X2}/{flags:X2}");
            Assert.That(tb.Clocks,Is.EqualTo(expected),$"{opcode:X2}/{flags:X2}");
            Assert.That((timed.A,timed.BC,timed.DE,timed.HL,timed.SP,timed.PC,timed.F,timed.Halted,timed.INTE),
                Is.EqualTo((fast.A,fast.BC,fast.DE,fast.HL,fast.SP,fast.PC,fast.F,fast.Halted,fast.INTE)));
            Assert.That(tb.Memory,Is.EqualTo(fb.Memory));
        }
    }
}
