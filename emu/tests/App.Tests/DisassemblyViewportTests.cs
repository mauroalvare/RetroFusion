using Retro.App;
using Retro.Cpu.M6502;

namespace App.Tests;

public sealed class DisassemblyViewportTests
{
    [Test]
    public void FollowingPcDoesNotDecodeOperandBytesAsPastInstructions()
    {
        var memory = new byte[65536]; var observed = new bool[65536];
        new byte[] { 0xA9, 0x20, 0x8D, 0, 0x20, 0x4C, 5, 0x80 }.CopyTo(memory, 0x8000);
        observed[0x8000] = observed[0x8002] = observed[0x8005] = true;
        var disassembler = new M6502Disassembler();
        Assert.That(DisassemblyViewport.Start(0x8005, 16,
            address => disassembler.Disassemble(a => memory[a], address).Length, observed), Is.EqualTo(0x8000));
    }

    [Test]
    public void NoObservationStartsAtPcInsteadOfInventingABackwardBoundary()
    {
        Assert.That(DisassemblyViewport.Start(0x8010, 16, _ => throw new AssertionException("No hace falta decodificar."), null), Is.EqualTo(0x8010));
    }

    [Test]
    public void ObservedInstructionsMayWrapTheAddressSpace()
    {
        var observed = new bool[65536]; observed[0xFFFE] = true;
        Assert.That(DisassemblyViewport.Start(1, 16, _ => 3, observed), Is.EqualTo(0xFFFE));
    }
}
