using Retro.Systems.Nes;

namespace Nes.Tests;

public sealed class DmaTests
{
    private static Retro.Systems.Nes.Nes Machine(byte[] code)
    {
        byte[] data=new byte[16+NesCartridge.PrgUnit+NesCartridge.ChrUnit];"NES\x1A"u8.CopyTo(data);data[4]=data[5]=1;
        var prg=data.AsSpan(16,NesCartridge.PrgUnit);prg.Fill(0xEA);code.CopyTo(prg);prg[0x40]=0xA5;prg[0x1000]=0x40;
        prg[0x3FFA]=0;prg[0x3FFB]=0xD0;prg[0x3FFC]=0;prg[0x3FFD]=0xC0;prg[0x3FFE]=0;prg[0x3FFF]=0xD0;
        var nes=new Retro.Systems.Nes.Nes(NesCartridge.Load(data));
        for(int i=0;i<256;i++)nes.Ram[0x200+i]=(byte)(i^0x5A);
        return nes;
    }
    private static void EnableDmc(Retro.Systems.Nes.Nes nes)
    {
        nes.Apu.Write(0x4010,0x8F);nes.Apu.Write(0x4012,1);nes.Apu.Write(0x4013,0);nes.Apu.Write(0x4015,0x10);
    }
    private static int PpuPosition(Retro.Systems.Nes.Nes nes)=>nes.Ppu.Line*341+nes.Ppu.Dot;
    private static void AssertPpuClocks(Retro.Systems.Nes.Nes nes,int before,long cpuClocks)
    { Assert.That((PpuPosition(nes)-before+341*262)%(341*262),Is.EqualTo(cpuClocks*3)); }
    [TestCase(false,513)] [TestCase(true,514)]
    public void OamStartsOnTheNextReadAndKeepsBothAlignmentLengths(bool shiftParity,int stall)
    {
        byte[] code=shiftParity?[0xA9,2,0x24,0,0x8D,0x14,0x40,0xEA]:[0xA9,2,0x8D,0x14,0x40,0xEA];
        var nes=Machine(code);nes.StepInstruction();if(shiftParity)nes.StepInstruction();
        long before=nes.Cpu2A03.CycleCount;nes.StepInstruction();
        Assert.That(nes.Cpu2A03.CycleCount-before,Is.EqualTo(4));Assert.That(nes.Ppu.Oam.All(b=>b==0),Is.True);
        before=nes.Cpu2A03.CycleCount;int ppu=PpuPosition(nes);nes.StepInstruction();
        Assert.That(nes.Cpu2A03.CycleCount-before,Is.EqualTo(stall+2));AssertPpuClocks(nes,ppu,stall+2);
        Assert.That(nes.Ppu.Oam,Is.EqualTo(Enumerable.Range(0,256).Select(i=>(byte)(i^0x5A)).ToArray()));
    }
    [Test]
    public void RmwWritesQueueOnlyTheLastOamPage()
    {
        var nes=Machine([0x0E,0x14,0x40,0xEA]);var writes=new List<byte>();
        nes.WriteWatch=(a,v,_)=>{if(a==0x4014)writes.Add(v);};long before=nes.Cpu2A03.CycleCount;
        nes.StepInstruction();Assert.That(writes,Is.EqualTo(new byte[]{0x40,0x80}));
        Assert.That(nes.Cpu2A03.CycleCount-before,Is.EqualTo(6));before=nes.Cpu2A03.CycleCount;
        nes.StepInstruction();Assert.That(nes.Cpu2A03.CycleCount-before,Is.EqualTo(515));
        Assert.That(nes.Ppu.Oam.Take(4),Is.EqualTo(new byte[]{0x0E,0x14,0x40,0xEA}));
    }
    [TestCase(0,3)] [TestCase(1,4)]
    public void DmcHaltsReadForThreeOrFourCycles(int phase,int stall)
    {
        var nes=Machine([0xEA]);nes.Cpu2A03.CycleCount=phase;EnableDmc(nes);int ppu=PpuPosition(nes);
        nes.StepInstruction();Assert.That(nes.Cpu2A03.CycleCount-phase,Is.EqualTo(stall+2));
        AssertPpuClocks(nes,ppu,stall+2);Assert.That(nes.Apu.Status&0x10,Is.Zero);Assert.That(nes.Apu.Status&0x80,Is.EqualTo(0x80));
    }
    [Test]
    public void DmcCannotHaltACpuWrite()
    {
        var nes=Machine([0xEA]);EnableDmc(nes);long before=nes.Cpu2A03.CycleCount;
        nes.Write(0x10,0x55);Assert.That(nes.Cpu2A03.CycleCount,Is.EqualTo(before));
        Assert.That(nes.Apu.Status&0x10,Is.EqualTo(0x10));Assert.That(nes.Ram[0x10],Is.EqualTo(0x55));
        nes.StepInstruction();Assert.That(nes.Apu.Status&0x10,Is.Zero);
    }
    [Test]
    public void DisablingDmcBeforeTheReadCancelsItsPendingRequest()
    {
        var nes=Machine([0xEA]);EnableDmc(nes);nes.Apu.Write(0x4015,0);long before=nes.Cpu2A03.CycleCount;
        nes.StepInstruction();Assert.That(nes.Cpu2A03.CycleCount-before,Is.EqualTo(2));Assert.That(nes.Apu.Status&0x90,Is.Zero);
    }
    [Test]
    public void DmcStealsAnOamGetSlotWithoutLosingTheOamByte()
    {
        var nes=Machine([0xA9,2,0x8D,0x14,0x40,0xEA]);nes.StepInstruction();nes.StepInstruction();EnableDmc(nes);
        long before=nes.Cpu2A03.CycleCount;nes.StepInstruction();
        Assert.That(nes.Cpu2A03.CycleCount-before,Is.EqualTo(517));
        Assert.That(nes.Ppu.Oam,Is.EqualTo(Enumerable.Range(0,256).Select(i=>(byte)(i^0x5A)).ToArray()));
        Assert.That(nes.Apu.Status&0x10,Is.Zero);
    }
    [Test]
    public void PendingOamAndDmcRestoreBeforeTheFirstDmaCycle()
    {
        byte[] code=[0xA9,2,0x8D,0x14,0x40,0xEA];var nes=Machine(code);
        nes.StepInstruction();nes.StepInstruction();EnableDmc(nes);byte[] state=nes.SaveState();
        using(var r=new BinaryReader(new MemoryStream(state))){r.ReadString();Assert.That(r.ReadInt32(),Is.EqualTo(5));}
        var clone=Machine(code);clone.LoadState(state);long before=nes.Cpu2A03.CycleCount;
        nes.StepInstruction();clone.StepInstruction();
        Assert.That(nes.Cpu2A03.CycleCount-before,Is.EqualTo(517));Assert.That(clone.SaveState(),Is.EqualTo(nes.SaveState()));
    }
    [Test]
    public void VersionThreeStatesHaveNoPendingOam()
    {
        var nes=Machine([0xEA]);byte[] state=nes.SaveState();
        using(var r=new BinaryReader(new MemoryStream(state)))
        {r.ReadString();int p=(int)r.BaseStream.Position;int version=r.ReadInt32();BitConverter.GetBytes(3).CopyTo(state,p);if(version>=4)state=state[..^(4*(version-3))];}
        nes.LoadState(state);long before=nes.Cpu2A03.CycleCount;nes.StepInstruction();
        Assert.That(nes.Cpu2A03.CycleCount-before,Is.EqualTo(2));
    }
    [TestCase(-2)] [TestCase(256)]
    public void InvalidPendingPageIsRejected(int page)
    {
        var nes=Machine([0xEA]);byte[] state=nes.SaveState();BitConverter.GetBytes(page).CopyTo(state,state.Length-8);
        Assert.Throws<InvalidDataException>(()=>nes.LoadState(state));
    }
    [Test]
    public void NtscNesDmcLosesOnlyOneControllerBitAtTheHaltRead()
    {
        var nes=Machine([0xEA]);nes.SetKey("P1 A",true);nes.SetKey("P1 SELECT",true);
        nes.Write(0x4016,1);nes.Write(0x4016,0);EnableDmc(nes);
        Assert.That(nes.Read(0x4016)&1,Is.Zero,"DMA consumes A, CPU receives B");
        Assert.That(nes.Read(0x4016)&1,Is.EqualTo(1),"the next bit is SELECT");
    }
    [Test]
    public void NmiThatRisesAndFallsDuringOamIsStillSampled()
    {
        var nes=Machine([0xEA]);nes.Write(0x2000,0x80);
        for(int n=0;nes.Ppu.Line!=240||nes.Ppu.Dot!=338;n++)
        {Assert.That(n,Is.LessThan(341*262));nes.Ppu.Step();}
        nes.Write(0x4014,0x20);nes.StepInstruction();Assert.That(nes.Ppu.Nmi,Is.False);
        nes.StepInstruction();Assert.That(nes.Cpu2A03.PC,Is.EqualTo(0xD000));
    }
}
