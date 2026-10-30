using Retro.Compat;
using Retro.Fusion;
using Retro.Systems.Spectrum;

namespace Compat.Tests;

public sealed class PreparationNeighborhoodTests
{
    [Test]
    public void AChangingNeighborBecomesAHypothesisWithoutLeavingTheMachineAdvanced()
    {
        var machine=new Spectrum48(new byte[16384]);
        byte[] code=[0x21,0x02,0x70,0x35,0xC3,0x03,0x80];
        for(int i=0;i<code.Length;i++) machine.Memory.Poke((uint)(0x8000+i),code[i]);
        machine.Z80.PC=0x8000; machine.Memory.Poke(0x7002,100);
        var profile=new GameProfile {Machine="zx48",Variables=new() { ["timerTens"]=new(){Address="7001"} }};
        byte[] state=machine.SaveState(); var picture=machine.Video.Pixels.ToArray();
        var findings=PreparationNeighborhoods.Observe(machine,profile,frames:5);
        Assert.That(findings.Any(f=>f.Address=="7002" && f.Status=="hypothesis"),Is.True);
        Assert.That(findings.All(f=>f.Address!="7001"),Is.True);
        Assert.That(machine.SaveState(),Is.EqualTo(state)); Assert.That(machine.Video.Pixels,Is.EqualTo(picture));
    }

    [Test]
    public void NoKnownFieldsCannotInventAFieldOrRunTheMachine()
    {
        var machine=new Spectrum48(new byte[16384]); long cycles=machine.Z80.T;
        Assert.That(PreparationNeighborhoods.Observe(machine,new GameProfile()),Is.Empty);
        Assert.That(machine.Z80.T,Is.EqualTo(cycles));
    }
}
