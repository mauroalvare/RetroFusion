using Retro.Fusion;
using Retro.Systems.Spectrum;

namespace Fusion.Tests;

public class ModificationUndoTests
{
    private static GameProfile Profile()=>new(){Variables=new()
    {
        ["a"]=new(){Address="8000"},["b"]=new(){Address="8001"},
        ["word"]=new(){Address="8000",Encoding="uint16",Max=65535},
        ["low"]=new(){Address="8002",Mask=15,Max=15},
        ["high"]=new(){Address="8002",Mask=240,Max=240}
    }};

    [Test]
    public void UndoRestoresEachPreviousStateAndFreezeSelection()
    {
        var machine=new Spectrum48(new byte[0x4000]);var edits=new GameModifications(machine,Profile());
        machine.Video.Pixels[0]=123;
        byte[] initial=machine.SaveState();
        edits.Apply([new("a",9,true)]);
        byte[] first=machine.SaveState();machine.Video.Pixels[0]=456;
        edits.Apply([new("b",7,true)]);
        machine.RunFrame(null);
        Assert.That(edits.Undo(),Is.True);
        Assert.That(machine.SaveState(),Is.EqualTo(first));
        Assert.That(edits.Frozen.Keys,Is.EqualTo(new[]{"a"}));
        Assert.That(machine.Video.Pixels[0],Is.EqualTo(456));
        Assert.That(edits.Undo(),Is.True);Assert.That(machine.SaveState(),Is.EqualTo(initial));
        Assert.That(machine.Video.Pixels[0],Is.EqualTo(123));
        Assert.That((edits.CanUndo,edits.CanRestore),Is.EqualTo((false,false)));
    }

    [Test]
    public void FiveUndoStepsRemainBoundedAndTheFirstReturnPointIsStillAvailable()
    {
        var machine=new Spectrum48(new byte[0x4000]);var edits=new GameModifications(machine,Profile());
        byte[] initial=machine.SaveState();
        for(int i=1;i<=8;i++)edits.Apply([new("a",i,false)]);
        for(int i=7;i>=3;i--)
        { Assert.That(edits.Undo(),Is.True);Assert.That(machine.Memory.Peek(0x8000),Is.EqualTo(i)); }
        Assert.That(edits.Undo(),Is.False);Assert.That(edits.CanRestore,Is.True);
        Assert.That(edits.Restore(),Is.True);Assert.That(machine.SaveState(),Is.EqualTo(initial));
        Assert.That(edits.CanUndo,Is.False);
    }

    [Test]
    public void OverlappingWordsAndActiveFreezesAreRejectedBeforeWriting()
    {
        var machine=new Spectrum48(new byte[0x4000]);var edits=new GameModifications(machine,Profile());
        byte[] before=machine.SaveState();
        Assert.Throws<InvalidOperationException>(()=>edits.Apply([new("word",200,false),new("b",2,false)]));
        Assert.That(machine.SaveState(),Is.EqualTo(before));
        edits.Apply([new("a",7,true)]);before=machine.SaveState();
        Assert.Throws<InvalidOperationException>(()=>edits.Apply([new("word",200,false)]));
        Assert.That(machine.SaveState(),Is.EqualTo(before));
        edits.ReleaseAll();edits.Apply([new("word",200,false)]);
        Assert.That(machine.Memory.Peek(0x8000),Is.EqualTo(200));
    }

    [Test]
    public void DisjointBitsInTheSameByteCanStillBeChangedTogether()
    {
        var machine=new Spectrum48(new byte[0x4000]);var edits=new GameModifications(machine,Profile());
        edits.Apply([new("low",5,true),new("high",160,true)]);
        Assert.That(machine.Memory.Peek(0x8002),Is.EqualTo(165));
        edits.Tick();Assert.That(machine.Memory.Peek(0x8002),Is.EqualTo(165));
    }
}
