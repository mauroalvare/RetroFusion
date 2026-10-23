using Retro.Cpu.I8088;
using Retro.Systems.Pc;

namespace Pc.Tests;

public class TimerIntegrationTests
{
    private sealed class Board : I8088Bus, I8088InterruptSource
    {
        public readonly byte[] Memory = new byte[1 << 20];
        public readonly Pic8259A Pic = new();
        public readonly Pit8253 Pit = new();
        public readonly List<byte> Vectors = [];
        public Board()
        {
            Pit.OutputChanged += (channel, high) => { if (channel == 0) Pic.SetLine(0, high); };
            Pit.SetGate(0, true);
        }
        public bool InterruptRequested => Pic.InterruptRequested;
        public byte Acknowledge() { byte vector=Pic.Acknowledge(); Vectors.Add(vector); return vector; }
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address, byte value) => Memory[address]=value;
        public byte In(ushort port) => port switch
        {
            0x20=>Pic.ReadCommand(),0x21=>Pic.ReadData(),
            >=0x40 and <=0x42=>Pit.ReadCounter(port-0x40),_=>throw new InvalidOperationException()
        };
        public void Out(ushort port, byte value)
        {
            if(port==0x20)Pic.WriteCommand(value);
            else if(port==0x21)Pic.WriteData(value);
            else if(port is >=0x40 and <=0x42)Pit.WriteCounter(port-0x40,value);
            else if(port==0x43)Pit.WriteControl(value);
            else throw new InvalidOperationException();
        }
    }

    private static (I8088Cpu Cpu,Board Board) Guest(int mode, byte mask=0xFE)
    {
        var board=new Board();
        var cpu=new I8088Cpu(board,board){CS=0x1000,DS=0x1000,ES=0x1000,SS=0x1000,SP=0xFFFE,IP=0x100};
        byte[] program=[
            0xB0,(byte)(0x30|(mode<<1)),0xE6,0x43, 0xB0,4,0xE6,0x40, 0xB0,0,0xE6,0x40,
            0xB0,0x13,0xE6,0x20, 0xB0,8,0xE6,0x21, 0xB0,1,0xE6,0x21,0xB0,mask,0xE6,0x21,
            0xFB,0x90,0xF4, 0x90,0xF4, 0xFA,0xF4]; // Two enabled waits, then CLI; HLT.
        program.CopyTo(board.Memory,0x10100);
        cpu.WriteWord(0,32,0x200);cpu.WriteWord(0,34,0x1000);
        byte[] handler=[0x50,0xFE,0x06,0,3,0xB0,0x20,0xE6,0x20,0x58,0xCF];
        handler.CopyTo(board.Memory,0x10200);
        UntilHalt(cpu);
        return(cpu,board);
    }

    private static void UntilHalt(I8088Cpu cpu)
    {
        for(int step=0;step<100&&!cpu.Halted;step++)cpu.Step();
        Assert.That(cpu.Halted,Is.True,"Guest failed to reach HLT within the budget.");
    }

    private static void Pulses(Board board,int count,int channel=0)
    { for(int pulse=0;pulse<count;pulse++)board.Pit.Tick(channel); }

    [TestCase(2)] [TestCase(3)]
    public void GuestProgramsPortsAndReturnsFromRepeatedTimerInterrupts(int mode)
    {
        var(cpu,board)=Guest(mode);
        Assert.That(board.Pic.RequestRegister,Is.Zero,"PIT programming preceded PIC initialization.");
        Assert.That(cpu.Step(),Is.False,"CPU execution alone must not generate a timer pulse.");
        long instructions=cpu.Instructions;
        Pulses(board,5); // One load plus four input pulses: first periodic rising edge.
        Assert.That((cpu.Halted,cpu.Instructions,board.InterruptRequested),Is.EqualTo((true,instructions,true)));
        cpu.Step();UntilHalt(cpu);
        Assert.That((board.Memory[0x10300],cpu.SP,board.Pic.InServiceRegister),Is.EqualTo((1,0xFFFE,0)));
        Pulses(board,4);
        cpu.Step();UntilHalt(cpu);
        Assert.That(board.Vectors,Is.EqualTo(new byte[]{8,8}));
        Assert.That((board.Memory[0x10300],cpu.SP,cpu.Flag(I8088Cpu.InterruptEnable)),Is.EqualTo((2,0xFFFE,false)));
        Assert.That((board.Pic.RequestRegister,board.Pic.InServiceRegister),Is.EqualTo((0,0)));
    }

    [Test]
    public void TimerKeepsAMaskedRequestWithoutWakingTheCpuUntilUnmasked()
    {
        var(cpu,board)=Guest(2,0xFF);
        Pulses(board,5);
        Assert.That((board.Pic.RequestRegister,board.InterruptRequested),Is.EqualTo((1,false)));
        Assert.That(cpu.Step(),Is.False);
        Assert.That((cpu.Halted,board.Memory[0x10300]),Is.EqualTo((true,0)));
        board.Out(0x21,0xFE);cpu.Step();UntilHalt(cpu);
        Assert.That(board.Vectors,Is.EqualTo(new byte[]{8}));
        Assert.That((board.Memory[0x10300],cpu.SP),Is.EqualTo((1,0xFFFE)));
    }

    [Test]
    public void ModeZeroRaisesOnlyOneEdgeEvenAsItsCounterKeepsRunning()
    {
        var(cpu,board)=Guest(0);
        Pulses(board,4);
        Assert.That(board.Pic.RequestRegister,Is.Zero); // Load plus three decrements.
        board.Pit.Tick(0);cpu.Step();UntilHalt(cpu);
        Assert.That((board.Memory[0x10300],board.Pit.GetOutput(0)),Is.EqualTo((1,true)));
        Pulses(board,65537);
        Assert.That((board.Pic.RequestRegister,cpu.Step(),cpu.Halted),Is.EqualTo((0,false,true)));
        Assert.That(board.Vectors,Is.EqualTo(new byte[]{8}));
    }

    [Test]
    public void ProgrammingAndGateTransitionsReachThePicWithoutAClockPulse()
    {
        var board=new Board();
        board.Out(0x20,0x13);board.Out(0x21,8);board.Out(0x21,1);
        board.Out(0x43,0x34);
        Assert.That(board.Pic.RequestRegister,Is.EqualTo(1));
        board.Acknowledge();board.Out(0x20,0x20);
        Assert.That(board.Pic.RequestRegister,Is.Zero);
        board.Out(0x40,4);board.Out(0x40,0);Pulses(board,4);
        Assert.That(board.Pit.GetOutput(0),Is.False);
        board.Pit.SetGate(0,false);
        Assert.That(board.Pic.RequestRegister,Is.EqualTo(1));
        Assert.That(board.Acknowledge(),Is.EqualTo(8));
    }

    [Test]
    public void SeveralTimerEdgesCoalesceAndWaitForTheEoiOfAnActiveIrq()
    {
        var(_,board)=Guest(2);
        Pulses(board,5);board.Acknowledge();
        Pulses(board,12); // Three more edges; IRR has one bit, not an interrupt queue.
        Assert.That((board.Pic.InServiceRegister,board.Pic.RequestRegister,board.InterruptRequested),Is.EqualTo((1,1,false)));
        board.Out(0x20,0x20);
        Assert.That(board.InterruptRequested,Is.True);
        Assert.That(board.Acknowledge(),Is.EqualTo(8));
        board.Out(0x20,0x20);
        Assert.That((board.Pic.RequestRegister,board.InterruptRequested),Is.EqualTo((0,false)));
    }

    [Test]
    public void OtherChannelsAreAccessibleByPortsWithoutDrivingIrqZero()
    {
        var(cpu,board)=Guest(2);
        for(int channel=1;channel<3;channel++)
        {
            board.Pit.SetGate(channel,true);
            board.Out(0x43,(byte)((channel<<6)|0x34));
            board.Out((ushort)(0x40+channel),4);board.Out((ushort)(0x40+channel),0);
            Pulses(board,13,channel);
            board.Out(0x43,(byte)(channel<<6));
            Assert.That(board.In((ushort)(0x40+channel))|(board.In((ushort)(0x40+channel))<<8),Is.EqualTo(4));
        }
        Assert.That((board.Pic.RequestRegister,cpu.Step(),cpu.Halted),Is.EqualTo((0,false,true)));
        Assert.That(board.Vectors,Is.Empty);
    }
}
