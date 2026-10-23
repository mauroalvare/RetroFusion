using Retro.Cpu.I8088;
using Retro.Systems.Pc;

namespace Pc.Tests;

public class IntegrationTests
{
    private sealed class Board : I8088Bus, I8088InterruptSource
    {
        public readonly byte[] Memory = new byte[1 << 20];
        public readonly Pic8259A Pic = new();
        public readonly List<byte> Vectors = [];
        public bool InterruptRequested => Pic.InterruptRequested;
        public byte Acknowledge() { byte vector=Pic.Acknowledge();Vectors.Add(vector);return vector; }
        public byte ReadMemory(uint address) => Memory[address];
        public void WriteMemory(uint address,byte value) => Memory[address]=value;
        public byte In(ushort port) => port switch {0x20=>Pic.ReadCommand(),0x21=>Pic.ReadData(),_=>throw new InvalidOperationException()};
        public void Out(ushort port,byte value)
        { if(port==0x20)Pic.WriteCommand(value);else if(port==0x21)Pic.WriteData(value);else throw new InvalidOperationException(); }
    }

    private static (I8088Cpu Cpu, Board Board) Program(params byte[] code)
    {
        var board=new Board();
        var cpu=new I8088Cpu(board,board){CS=0x1000,DS=0x1000,ES=0x1000,SS=0x1000,SP=0xFFFE,IP=0x100};
        code.CopyTo(board.Memory,0x10100);
        for(int line=0;line<8;line++)
        {
            ushort entry=(ushort)(0x200+line*16),counter=(ushort)(0x300+line);
            cpu.WriteWord(0,(ushort)((8+line)*4),entry);cpu.WriteWord(0,(ushort)((8+line)*4+2),0x1000);
            byte[] handler=[0xFE,0x06,(byte)counter,(byte)(counter>>8),0xB0,0x20,0xE6,0x20,0xCF];
            handler.CopyTo(board.Memory,0x10000+entry);
        }
        return(cpu,board);
    }
    private static void Initialize(Board board)
    { board.Out(0x20,0x13);board.Out(0x21,8);board.Out(0x21,1); }
    private static void UntilHalt(I8088Cpu cpu)
    {
        for(int step=0;step<200&&!cpu.Halted;step++)cpu.Step();
        Assert.That(cpu.Halted,Is.True);
    }

    [Test]
    public void GuestInitializesThePicThroughIoAndServicesMaskedThenUnmaskedRequests()
    {
        var(cpu,board)=Program(
            0xB0,0x13,0xE6,0x20,0xB0,8,0xE6,0x21,0xB0,1,0xE6,0x21,
            0xB0,0xFC,0xE6,0x21,0xFB,0x90,0xF4,
            0xB0,0xDC,0xE6,0x21,0xFA,0xF4);
        UntilHalt(cpu);
        board.Pic.SetLine(5,true);board.Pic.SetLine(1,true);board.Pic.SetLine(0,true);
        cpu.Step();UntilHalt(cpu);
        Assert.That(board.Vectors,Is.EqualTo(new byte[]{8,9,13}));
        Assert.That((board.Memory[0x10300],board.Memory[0x10301],board.Memory[0x10305]),Is.EqualTo((1,1,1)));
        Assert.That((board.Pic.RequestRegister,board.Pic.InServiceRegister,board.Pic.Mask),Is.EqualTo((0,0,0xDC)));
        Assert.That((cpu.SP,cpu.CS,cpu.IP,cpu.Flag(I8088Cpu.InterruptEnable)),Is.EqualTo((0xFFFE,0x1000,0x119,false)));
    }

    [Test]
    public void ANewHigherPriorityRequestWinsWhenTheCpuFinallyEnablesInterrupts()
    {
        var(cpu,board)=Program(0xFA,0x90,0xFB,0x90,0xF4);
        Initialize(board);board.Pic.SetLine(5,true);
        cpu.Step();board.Pic.SetLine(1,true);cpu.Step();cpu.Step();cpu.Step();
        Assert.That(board.Vectors,Is.Empty);
        cpu.Step();UntilHalt(cpu);
        Assert.That(board.Vectors,Is.EqualTo(new byte[]{9,13}));
        Assert.That((board.Memory[0x10301],board.Memory[0x10305],cpu.SP),Is.EqualTo((1,1,0xFFFE)));
    }

    [Test]
    public void AStoredMaskedRequestDoesNotWakeHaltUntilUnmasked()
    {
        var(cpu,board)=Program(0xF4);
        Initialize(board);board.Pic.WriteData(8);cpu.Flags=I8088Cpu.InterruptEnable;
        cpu.Step();board.Pic.SetLine(3,true);
        Assert.That(cpu.Step(),Is.False);
        Assert.That((cpu.Halted,board.Pic.RequestRegister,board.Vectors.Count),Is.EqualTo((true,8,0)));
        board.Pic.WriteData(0);
        Assert.That(cpu.Step(),Is.True);
        Assert.That((cpu.Halted,cpu.Instructions),Is.EqualTo((false,1L)));
        Assert.That(board.Vectors,Is.EqualTo(new byte[]{11}));
    }

    [Test]
    public void NestedHigherPriorityIrqUsesItsOwnEoiAndPreservesTheOuterFrame()
    {
        var(cpu,board)=Program(0x90,0xF4);
        Initialize(board);cpu.Flags=I8088Cpu.InterruptEnable;board.Pic.SetLine(4,true);
        byte[] outer=[0xFB,0x90,0xFE,0x06,0x04,0x03,0xB0,0x20,0xE6,0x20,0xCF];
        outer.CopyTo(board.Memory,0x10240);
        cpu.Step();cpu.Step();board.Pic.SetLine(0,true);cpu.Step();
        Assert.That(board.Vectors,Is.EqualTo(new byte[]{12}));
        cpu.Step();Assert.That(board.Pic.InServiceRegister,Is.EqualTo(0x11));
        for(int i=0;i<4;i++)cpu.Step();
        Assert.That((board.Pic.InServiceRegister,cpu.IP),Is.EqualTo((16,0x242)));
        UntilHalt(cpu);
        Assert.That(board.Vectors,Is.EqualTo(new byte[]{12,8}));
        Assert.That((board.Memory[0x10300],board.Memory[0x10304],cpu.SP,board.Pic.InServiceRegister),Is.EqualTo((1,1,0xFFFE,0)));
    }

    [Test]
    public void PendingTrapEntersAfterPicAcceptanceButBeforeItsGuestHandler()
    {
        var(cpu,board)=Program(0x90,0x90);
        Initialize(board);
        cpu.WriteWord(0,4,0x400);cpu.WriteWord(0,6,0x1000);board.Memory[0x10400]=0xCF;
        cpu.Flags=I8088Cpu.Trap|I8088Cpu.InterruptEnable;
        cpu.Step();board.Pic.SetLine(0,true);
        cpu.Step();Assert.That(cpu.IP,Is.EqualTo(0x200));
        cpu.Step();Assert.That(cpu.IP,Is.EqualTo(0x400));
        Assert.That((board.Memory[0x10300],board.Pic.InServiceRegister),Is.EqualTo((0,1)));
        Assert.That(cpu.ReadWord(cpu.SS,cpu.SP),Is.EqualTo(0x200));
        cpu.Step();
        for(int i=0;i<4;i++)cpu.Step();
        Assert.That((cpu.IP,cpu.SP,board.Memory[0x10300],board.Pic.InServiceRegister),Is.EqualTo((0x101,0xFFFE,1,0)));
        Assert.That(cpu.Flag(I8088Cpu.Trap),Is.True);
    }
}
