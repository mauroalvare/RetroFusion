using Retro.Core;
using Retro.Fusion;
using Retro.Systems.Nes;

namespace Fusion.Tests;

public class ContinuousFreezeTests
{
    private static Nes Machine()
    {
        byte[] image = new byte[16 + 16384 + 8192];
        image[0] = 0x4E; image[1] = 0x45; image[2] = 0x53; image[3] = 0x1A;
        image[4] = 1; image[5] = 1;
        // Our program rewrites the sprite buffer then copies it to the PPU each loop.
        byte[] program = [0xA9, 123, 0x8D, 3, 2, 0xA9, 2, 0x8D, 0x14, 0x40, 0x4C, 0, 0xC0];
        program.CopyTo(image, 16);
        image[16 + 0x3FFC] = 0; image[16 + 0x3FFD] = 0xC0;
        return new Nes(NesCartridge.Load(image));
    }

    private static GameProfile Profile() => new()
    {
        Id = "own-freeze", Machine = "nes",
        Variables = new() { ["x"] = new() { Address = "0203" } }
    };

    [Test]
    public void FrozenSpriteBufferSurvivesCpuWritesBeforeTheSameFramesDma()
    {
        var machine = Machine();
        using var edits = new GameModifications(machine, Profile());
        edits.Apply([new("x", 131, true)]);
        edits.Tick(); machine.RunFrame(null);
        Assert.That(machine.Memory.Peek(0x0203), Is.EqualTo(131));
        machine.Write(0x2003, 3);
        Assert.That(machine.Read(0x2004), Is.EqualTo(131), "DMA must see the pinned byte, not the game's overwritten value.");
    }
    [Test]
    public void FreezeSurvivesMirroredWritesAndRejectsConflictingAliases()
    {
        var machine = Machine(); var profile = Profile();
        profile.Variables["mirror"] = new() { Address = "0A03" };
        using var edits = new GameModifications(machine, profile);
        edits.Apply([new("x", 131, true)]);
        machine.Write(0x0A03, 17);
        Assert.That(machine.Memory.Peek(0x0203), Is.EqualTo(131));
        Assert.Throws<InvalidOperationException>(() => edits.Apply([new("mirror", 12, true)]));
        Assert.That(machine.Memory.Peek(0x0203), Is.EqualTo(131));
    }

    [Test]
    public void MasksPreserveOtherBitsAndUndoRestoresEarlierPins()
    {
        var machine = Machine(); var profile = Profile();
        profile.Variables["x"].Mask = 15; profile.Variables["x"].Max = 15;
        profile.Variables["upper"] = new() { Address = "0203", Mask = 0xF0 };
        using var edits = new GameModifications(machine, profile);
        edits.Apply([new("x", 5, true)]);
        machine.Write(0x0203, 0xA2);
        Assert.That(machine.Memory.Peek(0x0203), Is.EqualTo(0xA5));
        edits.Apply([new("upper", 0xC0, true)]);
        machine.Write(0x0203, 0x17);
        Assert.That(machine.Memory.Peek(0x0203), Is.EqualTo(0xC5));
        edits.Undo(); machine.Write(0x0203, 0x42);
        Assert.That(machine.Memory.Peek(0x0203), Is.EqualTo(0x45));
        edits.Restore(); machine.Write(0x0203, 0x27);
        Assert.That(machine.Memory.Peek(0x0203), Is.EqualTo(0x27));
        Assert.That(machine.WriteWatch, Is.Null);
    }

    [TestCase("uint16", 2, 12345)]
    [TestCase("bcd", 2, 1234)]
    [TestCase("digits", 4, 1234)]
    [TestCase("ascii", 4, 1234)]
    public void EveryEncodedByteIsPinnedDuringCpuWrites(string encoding, int size, int value)
    {
        var machine = Machine(); var profile = Profile();
        profile.Variables["x"] = new() { Address = "0203", Encoding = encoding, Bytes = size, Max = 65535 };
        using var edits = new GameModifications(machine, profile);
        edits.Apply([new("x", value, true)]);
        for (ushort address = 0x0203; address < 0x0203 + size; address++) machine.Write(address, 0);
        Assert.That(profile.Variables["x"].Read(machine.Memory), Is.EqualTo(value));
    }

    [TestCase(true)] [TestCase(false)]
    public void ClosingObservationAndFreezingInEitherOrderKeepsIndependentSubscribers(bool observeFirst)
    {
        var machine = Machine(); int priorWrites = 0;
        Action<ushort, byte, ushort> prior = (_, _, _) => priorWrites++;
        machine.WriteWatch = prior;
        using var edits = new GameModifications(machine, Profile());
        Retro.Ai.ModificationObservation? observation = observeFirst ? new(machine) : null;
        edits.Apply([new("x", 131, true)]);
        observation ??= new(machine);
        machine.Write(0x0203, 1);
        Assert.That(observation.WrittenAddresses, Is.EqualTo(1));
        observation.Dispose();
        machine.Write(0x0203, 2);
        Assert.That(observation.WrittenAddresses, Is.EqualTo(1), "A disposed observer must not be retained in the freeze callback.");
        Assert.That(machine.Memory.Peek(0x0203), Is.EqualTo(131));
        using (var reopened = new Retro.Ai.ModificationObservation(machine))
        {
            edits.ReleaseAll(); machine.Write(0x0203, 3);
            Assert.That(reopened.WrittenAddresses, Is.EqualTo(1));
            Assert.That(machine.Memory.Peek(0x0203), Is.EqualTo(3));
        }
        Assert.That(machine.WriteWatch, Is.SameAs(prior));
        Assert.That(priorWrites, Is.EqualTo(3));
    }

    [Test]
    public void DisposingAnOldSubscriptionNeverRemovesANewExternalWatch()
    {
        var machine = Machine();
        using var old = CpuWriteObservers.Subscribe(machine, (_, _, _) => { });
        int calls = 0;
        Action<ushort, byte, ushort> replacement = (_, _, _) => calls++;
        machine.WriteWatch = replacement;
        using (var current = CpuWriteObservers.Subscribe(machine, (_, _, _) => calls++))
        {
            old.Dispose(); machine.Write(0x0203, 1);
            Assert.That(calls, Is.EqualTo(2));
        }
        Assert.That(machine.WriteWatch, Is.SameAs(replacement));
    }

    [Test]
    public void DisposeReleasesPinsWithoutRestoringOrChangingTheCurrentGame()
    {
        var machine = Machine(); var edits = new GameModifications(machine, Profile());
        edits.Apply([new("x", 131, true)]); long frame = machine.FrameCount;
        edits.Dispose(); machine.Write(0x0203, 23);
        Assert.That(machine.Memory.Peek(0x0203), Is.EqualTo(23));
        Assert.That(machine.FrameCount, Is.EqualTo(frame)); Assert.That(machine.WriteWatch, Is.Null);
    }

    [TestCase("zx48")] [TestCase("c64")] [TestCase("invaders")]
    public void OtherMachinesExposeThePinnedValueImmediatelyAfterACpuWrite(string system)
    {
        IMachine machine = system switch
        {
            "zx48" => new Retro.Systems.Spectrum.Spectrum48(new byte[0x4000]),
            "c64" => new Retro.Systems.C64.C64(new(new byte[8192], new byte[8192], new byte[4096], "Own firmware")),
            _ => new Retro.Systems.Arcade.SpaceInvaders(new byte[0x2000])
        };
        ushort address = system == "invaders" ? (ushort)0x2003 : (ushort)0x8003;
        var profile = new GameProfile { Variables = new() { ["counter"] = new() { Address = address.ToString("X4") } } };
        using var edits = new GameModifications(machine, profile);
        edits.Apply([new("counter", 11, true)]);
        void Write(byte value)
        {
            if (machine is Retro.Systems.Spectrum.Spectrum48 zx) zx.WriteMemory(address, value);
            else if (machine is Retro.Systems.C64.C64 c64) c64.Write(address, value);
            else ((Retro.Systems.Arcade.SpaceInvaders)machine).WriteMemory(address, value);
        }
        Write(23); Assert.That(machine.Memory.Peek(address), Is.EqualTo(11));
        edits.ReleaseAll(); Write(29); Assert.That(machine.Memory.Peek(address), Is.EqualTo(29));
    }

}
