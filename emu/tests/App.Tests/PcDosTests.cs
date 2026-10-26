using System.IO.Compression;
using System.Text;
using Retro.App;
using Retro.Core;
using Retro.Machines;
using Retro.Systems.Pc;

namespace App.Tests;

[TestFixture]
public sealed class PcDosTests
{
    private string _folder = "";
    [SetUp] public void Setup() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-dos-" + Guid.NewGuid())).FullName;
    [TearDown] public void Cleanup() => Directory.Delete(_folder, true);
    private static MediaFile Program(string source = "own.com") => new("OWN.COM", [0xCD, 0x20], source);
    private static void Run(PcXt pc, int frames)
    {
        short[] sound = new short[8192];
        for (int i = 0; i < frames; i++) { pc.RunFrame(null); pc.DrainAudio(sound); }
    }

    [Test]
    public void DiskIdentityIsDeterministicAndIncludesAllData()
    {
        var program = Program();
        var data = new MediaFile("DATA.INI", [17], "DATA.INI");
        byte[] first = PcDos.Prepare([program, data], program);
        Assert.That(PcDos.Prepare([data, program], program), Is.EqualTo(first));
        Assert.That(PcDos.Prepare([program, data with { Data = [99] }], program), Is.Not.EqualTo(first));
        Assert.That(program.Data, Is.EqualTo(new byte[] { 0xCD, 0x20 }));
    }

    [Test]
    public void OversizedGamesFailWithoutChangingTheOriginals()
    {
        var program = Program(); var data = new MediaFile("DATA.BIN", new byte[720 * 1024], "DATA.BIN");
        Assert.That(() => PcDos.Prepare([program, data], program), Throws.TypeOf<InvalidDataException>().With.Message.Contains("720 KB"));
        Assert.That(program.Data, Is.EqualTo(new byte[] { 0xCD, 0x20 }));
    }

    [Test]
    public void UnsafeAndAmbiguousDosPathsAreRejected()
    {
        var program = Program("game.zip!OWN.COM");
        Assert.That(() => PcDos.Prepare([program, new("EVIL.DAT", [1], "game.zip!../EVIL.DAT")], program),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("ruta"));
        Assert.That(() => PcDos.Prepare([program, new("DATA.DAT", [1], "game.zip!DATA.DAT"), new("data.dat", [2], "game.zip!data.dat")], program),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("mismo nombre"));
    }

    [Test]
    public void ZipAuxiliaryDataIsOptionalForOtherMachinesButAvailableForDos()
    {
        string path = Path.Combine(_folder, "own.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var one = zip.CreateEntry("game.com").Open()) one.Write([0xCD, 0x20]);
            using (var two = zip.CreateEntry("DATA.INI").Open()) two.Write([73]);
            zip.CreateEntry("EMPTY.DAT");
        }
        Assert.That(MediaArchive.Open(path), Has.Count.EqualTo(1));
        Assert.That(MediaArchive.Open(path, keepAuxiliary: true), Has.Count.EqualTo(3));
    }

    // Reads DATA.INI with native DOS INT 21h, records its byte at 0050:0010, then waits
    // for a key via DOS and records that key. No emulation shortcuts or mocks.
    private static byte[] ReadingProgram()
    {
        var code = new List<byte>();
        void E(params byte[] bytes) => code.AddRange(bytes);
        E(0xBA, 0, 0, 0xB8, 0, 0x3D, 0xCD, 0x21, 0x89, 0xC3, 0xBA, 0, 0, 0xB9, 1, 0, 0xB4, 0x3F, 0xCD, 0x21);
        E(0xB8, 0x50, 0, 0x8E, 0xC0, 0xA0, 0, 0, 0x26, 0xA2, 0x10, 0);
        E(0xB4, 8, 0xCD, 0x21, 0x26, 0xA2, 0x11, 0, 0xEB, 0xFE);
        int name = 0x100 + code.Count; code[1] = (byte)name; code[2] = (byte)(name >> 8);
        code.AddRange(Encoding.ASCII.GetBytes("DATA.INI\0"));
        int buffer = 0x100 + code.Count;
        code[11] = code[26] = (byte)buffer; code[12] = code[27] = (byte)(buffer >> 8); code.Add(0);
        return code.ToArray();
    }

    [Test]
    public void RealDosReadsCompanionFromZipAndHandlesKeyboardWithoutExternalFirmware()
    {
        string path = Path.Combine(_folder, "own.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var exe = zip.CreateEntry("A long archive folder/OWN.COM").Open()) exe.Write(ReadingProgram());
            using (var data = zip.CreateEntry("A long archive folder/DATA.INI").Open()) data.Write([73]);
            zip.CreateEntry("A long archive folder/Metadata from launcher.marker");
            zip.CreateEntry("A long archive folder\\");
        }
        byte[] original = File.ReadAllBytes(path);
        var files = MediaArchive.Open(path);
        var launch = MachineLauncher.Launch("pcxt", files, null, _folder);
        var pc = (PcXt)launch.Machine;
        for (int n = 0; n < 3000 && pc.Peek(0x510) != 73; n++) Run(pc, 1);
        Assert.That(pc.Peek(0x510), Is.EqualTo(73), "DOS must load the program, set its working directory and read the INI data.");
        pc.SetKey("A", true); Run(pc, 3); pc.ReleaseAllKeys(); Run(pc, 12);
        Assert.That(pc.Peek(0x511), Is.EqualTo((byte)'a'));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
    }

    [Test]
    public void BootAccelerationEndsBeforeGameplayAndDoesNotResumeOnStateLoad()
    {
        var program = new MediaFile("OWN.COM", ReadingProgram(), "own.com");
        var data = new MediaFile("DATA.INI", [73], "DATA.INI");
        var host = new EmulatorHost();
        host.StartPc(MachineCatalog.Get("pcxt"), program, _folder, [program, data]);
        Assert.That(host.IsFastForwarding, Is.True);
        for (int n = 0; n < 2000 && host.IsFastForwarding; n++) host.Update(1.0 / 60);
        Assert.That(host.IsFastForwarding, Is.False, "Automatic turbo must end after native DOS boot.");
        for (int n = 0; n < 300 && host.Pc!.Peek(0x510) != 73; n++) Run(host.Pc!, 1);
        Assert.That(host.Pc!.Peek(0x510), Is.EqualTo(73));
        host.QuickSave(); host.QuickLoad();
        Assert.That(host.IsFastForwarding, Is.False);
        host.Reset(false); Assert.That(host.IsFastForwarding, Is.True);
        for (int n = 0; n < 2000 && host.IsFastForwarding; n++) host.Update(1.0 / 60);
        Assert.That(host.IsFastForwarding, Is.False);
        host.StartPc(MachineCatalog.Get("pcxt"), new("own.img", new byte[163840], "own.img"), _folder);
        Assert.That(host.IsFastForwarding, Is.False, "A user boot disk keeps its own timing.");
    }
}