using Retro.Systems.Nes;

namespace Nes.Tests;

[TestFixture]
public class CartridgeTests
{
    /// <summary>Builds an iNES image whose every PRG/CHR byte tells which 1 KB block it is in.</summary>
    private static byte[] Image(int prgUnits, int chrUnits, byte flags6 = 0, byte flags7 = 0, bool trainer = false, string? tail = null)
    {
        if (trainer) flags6 |= 0x04;
        var header = new byte[16];
        "NES\x1A"u8.CopyTo(header);
        header[4] = (byte)prgUnits; header[5] = (byte)chrUnits; header[6] = flags6; header[7] = flags7;
        if (tail != null)
            System.Text.Encoding.ASCII.GetBytes(tail).CopyTo(header, 7);
        var data = new List<byte>(header);
        if (trainer) data.AddRange(new byte[NesCartridge.TrainerSize]);
        for (int i = 0; i < prgUnits * NesCartridge.PrgUnit; i++) data.Add((byte)(0x80 | i >> 10));
        for (int i = 0; i < chrUnits * NesCartridge.ChrUnit; i++) data.Add((byte)(i >> 10));
        return data.ToArray();
    }

    [Test]
    public void Nrom128_MirrorsItsPrgAtC000()
    {
        var cart = NesCartridge.Load(Image(1, 1));
        Assert.That(cart.Mapper, Is.Zero);
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(0x80));
        Assert.That(cart.CpuRead(0xC000), Is.EqualTo(0x80), "16 KB aparecen dos veces");
        Assert.That(cart.CpuRead(0xFFFF), Is.EqualTo(0x80 | 15));
        Assert.That(cart.Mirroring, Is.EqualTo(NesMirroring.Horizontal));
    }

    [Test]
    public void Nrom256_HasTwoDifferentHalves_AndVerticalMirroring()
    {
        var cart = NesCartridge.Load(Image(2, 1, flags6: 0x01));
        Assert.That(cart.CpuRead(0xC000), Is.EqualTo(0x80 | 16));
        Assert.That(cart.Mirroring, Is.EqualTo(NesMirroring.Vertical));
        Assert.That(cart.NametableFor(0x2000), Is.Zero);
        Assert.That(cart.NametableFor(0x2400), Is.EqualTo(1));
        Assert.That(cart.NametableFor(0x2800), Is.Zero);
        cart.CpuWrite(0x8000, 1);
        Assert.That(cart.ChrBank, Is.Zero, "NROM no cambia de banco");
    }

    [Test]
    public void HorizontalMirroring_PairsTheTopAndBottomTables()
    {
        var cart = NesCartridge.Load(Image(1, 1));
        Assert.That(new[] { 0x2000, 0x2400, 0x2800, 0x2C00 }.Select(a => cart.NametableFor((ushort)a)), Is.EqualTo(new[] { 0, 0, 1, 1 }));
    }

    [Test]
    public void Cnrom_WritesPickTheChrBank()
    {
        var cart = NesCartridge.Load(Image(2, 4, flags6: 0x30));
        Assert.That(cart.Mapper, Is.EqualTo(3));
        Assert.That(cart.PpuRead(0x0000), Is.Zero);
        cart.CpuWrite(0x8000, 2);
        Assert.That(cart.PpuRead(0x0000), Is.EqualTo(16), "banco 2 = tercer bloque de 8 KB");
        cart.CpuWrite(0xFFFF, 5);
        Assert.That(cart.ChrBank, Is.EqualTo(1), "el número de banco se limita a los bancos que hay");
        cart.PpuWrite(0x0000, 0xAA);
        Assert.That(cart.PpuRead(0x0000), Is.EqualTo(8), "la CHR ROM no se escribe");
    }

    [Test]
    public void NoChr_MeansEightKilobytesOfChrRam()
    {
        var cart = NesCartridge.Load(Image(1, 0));
        Assert.That(cart.ChrIsRam, Is.True);
        cart.PpuWrite(0x1234, 0x5A);
        Assert.That(cart.PpuRead(0x1234), Is.EqualTo(0x5A));
    }

    [Test]
    public void Trainer_IsSkipped()
    {
        var cart = NesCartridge.Load(Image(1, 1, trainer: true));
        Assert.That(cart.CpuRead(0x8000), Is.EqualTo(0x80));
    }

    [Test]
    public void DiskDudeHeaders_IgnoreTheJunkMapperNibble()
    {
        var cart = NesCartridge.Load(Image(1, 1, tail: "DiskDude!"));
        Assert.That(cart.Mapper, Is.Zero);
    }

    [Test]
    public void BadImages_SayWhy()
    {
        Assert.That(() => NesCartridge.Load(new byte[40]), Throws.TypeOf<InvalidDataException>().With.Message.Contains("firma"));
        Assert.That(() => NesCartridge.Load(Image(1, 1, flags6: 0x10)), Throws.TypeOf<InvalidDataException>().With.Message.Contains("Mapper 1"));
        var cut = Image(2, 1)[..20000];
        Assert.That(() => NesCartridge.Load(cut), Throws.TypeOf<InvalidDataException>().With.Message.Contains("cortada"));
    }

    private static readonly string HomebrewDir = Path.Combine(Retro.Core.PlayerFolders.Find("ROMs",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ROMs"))!, "descargados-autores", "NES");

    [TestCase("mojon-twins--bootee.nes", 0)]
    [TestCase("mojon-twins--cadaverion.nes", 0)]
    [TestCase("mojon-twins--cave-cnrom.nes", 3)]
    [TestCase("mojon-twins--cheman.nes", 0)]
    [TestCase("mojon-twins--super-uwol.nes", 0)]
    public void Homebrew_LoadsWithItsMapper_AndItsResetVectorPointsIntoPrg(string file, int mapper)
    {
        string path = Path.Combine(HomebrewDir, file);
        if (!File.Exists(path))
            Assert.Ignore($"Falta {path}.");
        var cart = NesCartridge.Load(File.ReadAllBytes(path));
        TestContext.Out.WriteLine(cart.Describe());
        Assert.That(cart.Mapper, Is.EqualTo(mapper));
        int reset = cart.CpuRead(0xFFFC) | cart.CpuRead(0xFFFD) << 8;
        Assert.That(reset, Is.GreaterThanOrEqualTo(0x8000), "el vector de reset apunta a la PRG ROM");
    }
}
