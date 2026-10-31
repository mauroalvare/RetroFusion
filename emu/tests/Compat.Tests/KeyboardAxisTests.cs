using Retro.Compat;
using Retro.Fusion;
using Retro.Systems.Spectrum;

namespace Compatibility.Tests;

public sealed class KeyboardAxisTests
{
    [Test]
    public void AnUnusualKeyboardAxisIsMeasuredWithoutInventingAFireButton()
    {
        var rom = new byte[16384]; rom[0x38] = 0xFB; rom[0x39] = 0xC9;
        var m = new Spectrum48(rom);
        // One horizontal step per interrupt, using only Z/X. Draw the actual coordinate.
        byte[] code = [0xF3,0x21,0x00,0xC0,0x36,0x80,0xFB,0x76,0x01,0xFE,0xFE,0xED,0x78,
            0xCB,0x4F,0x20,1,0x35,0xCB,0x57,0x20,1,0x34,0x7E,0x32,0x00,0x40,0xC3,7,0x90];
        for (int i = 0; i < code.Length; i++) m.Memory.Poke((uint)(0x9000 + i), code[i]);
        m.Memory.Poke(0x5800, 0x47); m.Cpu.ProgramCounter = 0x9000; m.RunFrame(null);
        var check = PlayDetector.Detect(m, "zx48");
        Assert.That(check.Playing, Is.True, check.Evidence);
        Assert.That((check.Controls!.Left, check.Controls.Right, check.Controls.Fire), Is.EqualTo(("Z", "X", "")));
    }

    [Test]
    public void AMeasuredAxisDoesNotAdvertiseOtherActionsInItsPreparedProfile()
    {
        var c = new ControlSet("Eje Z/X", "Z", "X", "", "", "");
        var profile = GamePreparer.BuildProfile(new PrepareResult { Game = "Propio", Machine = "zx48", File = "propio.z80", Sha256 = new string('a',64) },
            "Spectrum", new LabReport(), c);
        Assert.That(profile.Controls.Keys, Is.EquivalentTo(new[] { "left", "right" }));
        Assert.That(c.Keys, Is.EquivalentTo(new[] { "Z", "X" }));
    }
}
