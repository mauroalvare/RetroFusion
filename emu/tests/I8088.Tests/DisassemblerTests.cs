using Retro.Core;
using Retro.Cpu.I8088;

namespace I8088.Tests;

[TestFixture]
public sealed class DisassemblerTests
{
    [TestCase(new byte[] { 0xB8, 0x34, 0x12 }, "MOV AX, $1234")]
    [TestCase(new byte[] { 0x26, 0x8B, 0x46, 0xFE }, "MOV AX, word [ES:BP-$2]")]
    [TestCase(new byte[] { 0x83, 0xC0, 0xFF }, "ADD AX, -1")]
    [TestCase(new byte[] { 0xF3, 0xA5 }, "REP MOVSW")]
    [TestCase(new byte[] { 0xC7, 0x06, 0x00, 0x40, 0xCD, 0xAB }, "MOV word [$4000], $ABCD")]
    [TestCase(new byte[] { 0xD9, 0xC0 }, "ESC $08, AX")]
    public void DecodeOperandsAndPrefixes(byte[] code, string expected)
    {
        var instruction = new I8088Disassembler().Disassemble(a => code[a - 0xA0000], 0xA0000);
        Assert.Multiple(() =>
        {
            Assert.That(instruction.Text, Is.EqualTo(expected));
            Assert.That(instruction.Length, Is.EqualTo(code.Length));
            Assert.That(instruction.Bytes, Is.EqualTo(code));
        });
    }

    [Test]
    public void FarCallsKeepTwentyBitTargetsAndReturnAddresses()
    {
        byte[] memory = new byte[1 << 20];
        new byte[] { 0x9A, 0x20, 0, 0, 0xF0, 0xF4 }.CopyTo(memory, 0x10000);
        memory[0xF0020] = 0xCB;
        var map = CodeMap.Build(a => memory[a], new I8088Flow(), [0x10000]);
        Assert.Multiple(() =>
        {
            Assert.That(map.Routines.Keys, Is.EquivalentTo(new uint[] { 0x10000, 0xF0020 }));
            Assert.That(map.Routines[0x10000].Calls, Contains.Item(0xF0020u));
            Assert.That(map.RoutineAt(0x10005)?.Start, Is.EqualTo(0x10000));
            Assert.That(map.RoutineAt(0xF0020)?.Start, Is.EqualTo(0xF0020));
        });
    }

    [Test]
    public void LiveSegmentWrapsBothOperandsAndSequentialFlow()
    {
        byte[] memory = new byte[1 << 20];
        memory[0x1FFFF] = 0xB8; memory[0x10000] = 0x34; memory[0x10001] = 0x12;
        memory[0x10002] = 0xF4;
        var decoder = new I8088Disassembler(() => 0x1000);
        var instruction = decoder.Disassemble(a => memory[a], 0x1FFFF);
        var map = CodeMap.Build(a => memory[a], new I8088Flow(decoder), [0x1FFFF]);
        Assert.Multiple(() =>
        {
            Assert.That(instruction.Text, Is.EqualTo("MOV AX, $1234"));
            Assert.That(map.Code.Keys, Is.EquivalentTo(new uint[] { 0x1FFFF, 0x10002 }));
        });
    }

    [Test]
    public void RelativeBranchWrapsPhysicalMemoryAndSegmentOffset()
    {
        var linear = new I8088Flow().Analyze(a => a == 0xFFFFFu ? (byte)0xEB : (byte)0xFE, 0xFFFFF);
        var segmented = new I8088Flow(new(() => 0x1000)).Analyze(a => a == 0x1FFFE ? (byte)0xEB : (byte)0x02, 0x1FFFE);
        Assert.That(linear.Target, Is.EqualTo(0xFFFFF));
        Assert.That(segmented.Target, Is.EqualTo(0x10002));
    }

    [Test]
    public void InterruptVectorsAndIndirectJumpsDoNotInventTargets()
    {
        byte[] memory = new byte[1 << 20];
        new byte[] { 0xCD, 0x10, 0xFF, 0x27 }.CopyTo(memory, 0x20000);
        new byte[] { 0x00, 0x01, 0x00, 0xF0 }.CopyTo(memory, 0x40);
        var flow = new I8088Flow();
        Assert.That(flow.Analyze(a => memory[a], 0x20000).Target, Is.EqualTo(0xF0100));
        Assert.That(flow.Analyze(a => memory[a], 0x20002).Kind, Is.EqualTo(FlowKind.JumpIndirect));
        Assert.That(flow.Analyze(a => memory[a], 0x20002).Target, Is.Null);
    }

    [Test]
    public void PrefixOnlyMemoryHasBoundedDecodeAndMap()
    {
        var map = CodeMap.Build(_ => 0xF3, new I8088Flow(), [0xFFFFF]);
        Assert.That(map.Code.Count, Is.EqualTo(1));
        Assert.That(map.Routines[0xFFFFF].End, Is.EqualTo(0xFFFFF));
    }
}