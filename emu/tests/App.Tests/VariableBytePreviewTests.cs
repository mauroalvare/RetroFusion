using Retro.App;
using Retro.Core;
using Retro.Fusion;

namespace App.Tests;

public sealed class VariableBytePreviewTests
{
    private sealed class Memory : IMemoryView
    {
        public readonly byte[] Bytes = new byte[65536];
        public int AddressBits => 16;
        public byte Peek(uint address) => Bytes[address];
        public void Poke(uint address, byte value) => throw new AssertionException("A preview must never write the live machine.");
    }

    [TestCase("uint8", 1, 0x11, new byte[] { 0xAA }, new byte[] { 0x11 })]
    [TestCase("uint16", 2, 0xBEEF, new byte[] { 0x34, 0x12 }, new byte[] { 0xEF, 0xBE })]
    [TestCase("bcd", 2, 6789, new byte[] { 0x45, 0x23 }, new byte[] { 0x89, 0x67 })]
    [TestCase("ascii", 3, 907, new byte[] { 0x31, 0x32, 0x33 }, new byte[] { 0x39, 0x30, 0x37 })]
    [TestCase("digits", 3, 907, new byte[] { 1, 2, 3 }, new byte[] { 9, 0, 7 })]
    public void EncodingsShowTheExactProposedBytesAndLeaveOriginalMemoryIntact(string encoding, int bytes, int requested, byte[] before, byte[] after)
    {
        var memory = new Memory(); before.CopyTo(memory.Bytes, 0x2000);
        byte[] original = (byte[])memory.Bytes.Clone();
        var profile = new GameProfile { Machine = "zx48", Variables = new()
        { ["counter"] = new() { Address = "2000", Encoding = encoding, Bytes = bytes, Max = 65535 } } };
        var preview = CodeWindow.PreviewVariable(profile, "counter", requested, memory);
        Assert.That(preview.Select(c => c.Address), Is.EqualTo(Enumerable.Range(0x2000, bytes).Select(a => (uint)a)));
        Assert.That(preview.Select(c => c.Before), Is.EqualTo(before));
        Assert.That(preview.Select(c => c.After), Is.EqualTo(after));
        Assert.That(memory.Bytes, Is.EqualTo(original));
    }

    [Test]
    public void MaskedBytePreservesTheUnrelatedHighBits()
    {
        var memory = new Memory(); memory.Bytes[0x2000] = 0xA5;
        var profile = new GameProfile { Variables = new()
        { ["field"] = new() { Address = "2000", Mask = 0x0F, Max = 15 } } };
        var preview = CodeWindow.PreviewVariable(profile, "field", 9, memory);
        Assert.That(preview, Is.EqualTo(new[] { new CodeWindow.VariableBytePreview(0x2000, 0xA5, 0xA9) }));
        Assert.That(memory.Bytes[0x2000], Is.EqualTo(0xA5));
    }

    [Test]
    public void SplitHighBitShowsBothAddressesAndPreservesTheOtherBits()
    {
        var memory = new Memory(); memory.Bytes[0x2000] = 0x34; memory.Bytes[0x4000] = 0xAA;
        var profile = new GameProfile { Variables = new()
        { ["position"] = new() { Address = "2000", Encoding = "uint9", HighBitAddress = "4000", HighBitMask = 0x40, Max = 511 } } };
        var preview = CodeWindow.PreviewVariable(profile, "position", 0x1BC, memory);
        Assert.That(preview, Is.EqualTo(new[]
        {
            new CodeWindow.VariableBytePreview(0x2000, 0x34, 0xBC),
            new CodeWindow.VariableBytePreview(0x4000, 0xAA, 0xEA),
        }));
        Assert.That((memory.Bytes[0x2000], memory.Bytes[0x4000]), Is.EqualTo((0x34, 0xAA)));
    }

    [TestCase("unknown", "2000", 0, 1, 2)]
    [TestCase("uint16", "FFFF", 0, 2, 2)]
    [TestCase("uint8", "2000", 15, 1, 16)]
    [TestCase("ascii", "2000", 0, 2, 100)]
    [TestCase("digits", "2000", 0, 10, 2)]
    public void InvalidFormatsAddressesAndValuesAreRejectedWithoutWriting(string encoding, string address, int mask, int bytes, int value)
    {
        var memory = new Memory(); Array.Fill(memory.Bytes, (byte)0x5A);
        var profile = new GameProfile { Variables = new()
        { ["invalid"] = new() { Address = address, Encoding = encoding, Mask = mask, Bytes = bytes, Max = 65535 } } };
        Assert.Throws<InvalidOperationException>(() => CodeWindow.PreviewVariable(profile, "invalid", value, memory));
        Assert.That(memory.Bytes.All(b => b == 0x5A), Is.True);
    }

    [Test]
    public void ReadOnlyHypothesisCannotBePreviewedAsAnApplicableEdit()
    {
        var memory = new Memory();
        var profile = new GameProfile { Variables = new()
        { ["hypothesis"] = new() { Address = "2000", ReadOnly = true } } };
        Assert.Throws<InvalidOperationException>(() => CodeWindow.PreviewVariable(profile, "hypothesis", 12, memory));
        Assert.That(memory.Bytes.All(b => b == 0), Is.True);
    }
}
