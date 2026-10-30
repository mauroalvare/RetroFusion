using System.Security.Cryptography;
using Retro.Systems.Nes;

namespace Nes.Tests;

public sealed class NestestTests
{
    private static byte[] OwnRom()
    {
        var rom = new byte[16 + 16384 + 8192];
        "NES\x1A"u8.CopyTo(rom); rom[4] = rom[5] = 1;
        // LDA #$80; TAX; INX; canonical automation entry in a mirrored NROM bank.
        new byte[] { 0xA9, 0x80, 0xAA, 0xE8 }.CopyTo(rom, 16);
        rom[16 + 0x3FFC] = 0; rom[16 + 0x3FFD] = 0xC0;
        return rom;
    }

    private static readonly string[] OwnTrace = [
        "C000  A9 80 LDA #$80 A:00 X:00 Y:00 P:24 SP:FD PPU:0,21 CYC:7",
        "C002  AA TAX         A:80 X:00 Y:00 P:A4 SP:FD PPU:0,27 CYC:9",
        "C003  E8 INX         A:80 X:80 Y:00 P:A4 SP:FD PPU:0,33 CYC:11"
    ];

    [Test]
    public void OwnTraceChecksInstructionStatesAndAbsoluteCpuCycles()
    {
        var result = NestestVerifier.Verify(OwnRom(), OwnTrace);
        Assert.That(result.Ok, Is.True); Assert.That(result.States, Is.EqualTo(3)); Assert.That(result.LastCycle, Is.EqualTo(11));
    }

    [TestCase("P:A4", "P:A5")] [TestCase("CYC:11", "CYC:12")] [TestCase("X:80", "X:81")]
    public void AChangedExpectedFlagRegisterOrCycleIsARealFailure(string before, string after)
    {
        var lines = OwnTrace.ToArray(); lines[2] = lines[2].Replace(before, after);
        var result = NestestVerifier.Verify(OwnRom(), lines);
        Assert.That(result.Ok, Is.False); Assert.That(result.Difference!.Line, Is.EqualTo(3)); Assert.That(result.States, Is.EqualTo(2));
    }

    [Test]
    public void MalformedOrEmptyLogsCannotSilentlyPass()
    {
        Assert.Throws<InvalidDataException>(() => NestestVerifier.Verify(OwnRom(), []));
        Assert.Throws<InvalidDataException>(() => NestestVerifier.Verify(OwnRom(), [OwnTrace[0], "sin registros"]));
        Assert.Throws<InvalidDataException>(() => NestestVerifier.Verify(OwnRom(), [OwnTrace[0].Replace("CYC:7", "CYC:8")]));
    }

    [Test]
    public void AuthorizedExternalNestestMatchesEveryState()
    {
        string? directory = Environment.GetEnvironmentVariable("RETRO_NESTEST");
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); directory == null && d != null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, "out", "mantenimiento", "nestest");
            if (File.Exists(Path.Combine(candidate, "nestest.nes"))) directory = candidate;
        }
        if (directory == null) Assert.Ignore("nestest es un diagnóstico externo opcional; no se descarga durante las pruebas.");
        byte[] rom = File.ReadAllBytes(Path.Combine(directory!, "nestest.nes"));
        byte[] log = File.ReadAllBytes(Path.Combine(directory!, "nestest.log"));
        Assert.That(Convert.ToHexString(SHA256.HashData(rom)).ToLowerInvariant(), Is.EqualTo("f67d55fd6b3cf0bad1cc85f1df0d739c65b53e79cecb7fea8f77ec0eadab0004"));
        Assert.That(Convert.ToHexString(SHA256.HashData(log)).ToLowerInvariant(), Is.EqualTo("442c4dd5539c7e88b3fd73c7b732a7eadbd22b47c2cd9e58397ef147f64f6f8f"));
        var result = NestestVerifier.Verify(rom, File.ReadLines(Path.Combine(directory!, "nestest.log")));
        Assert.That(result.Difference, Is.Null, result.Difference?.ToString());
        Assert.That(result.States, Is.EqualTo(8991)); Assert.That(result.LastCycle, Is.EqualTo(26554));
    }
}
