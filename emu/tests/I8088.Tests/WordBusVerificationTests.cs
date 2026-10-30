using System.IO.Compression;
using System.Text.Json;
using Retro.Cpu.I8088;

namespace I8088.Tests;

[NonParallelizable]
public sealed class WordBusVerificationTests
{
    private static object[] Row(int pins, uint address, string kind, string phase, int bhe, int data)
    {
        string active = kind is "MEMR" or "IOR" ? "R--" : "-AW";
        return [pins, address, "DS", kind.StartsWith("MEM") && phase == "T3" ? active : "---",
            kind.StartsWith("IO") && phase == "T3" ? active : "---", bhe, data,
            phase == "T3" ? "PASV" : kind, phase, "-", 0];
    }
    private static List<object[]> Transfer(string kind, uint address, int bhe, int data) =>
    [Row(1, address, kind, "T1", bhe, 0), Row(0, 0xABCDE, kind, "T2", 1, 0),
        Row(0, 0xABCDE, kind, "T3", 1, data), Row(0, 0, kind, "T4", 1, 0)];

    [TestCase("MEMR", false, I8088BusAccessKind.MemoryRead)]
    [TestCase("MEMW", false, I8088BusAccessKind.MemoryWrite)]
    [TestCase("IOR", false, I8088BusAccessKind.Input)]
    [TestCase("IOW", false, I8088BusAccessKind.Output)]
    [TestCase("MEMR", true, I8088BusAccessKind.MemoryRead)]
    [TestCase("MEMW", true, I8088BusAccessKind.MemoryWrite)]
    [TestCase("IOR", true, I8088BusAccessKind.Input)]
    [TestCase("IOW", true, I8088BusAccessKind.Output)]
    public void A0AndLatchedBheSelectOnlyTheActiveWordLanes(string kind, bool odd, I8088BusAccessKind expected)
    {
        var rows = Transfer(kind, odd ? 0x201u : 0x200u, 0, odd ? 0x34AB : 0x1234);
        if (odd) rows.AddRange(Transfer(kind, 0x202, 1, 0xCD12));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(rows));
        var accesses = I8088BusVerification.ReadTransfers(document.RootElement, I8088Model.Intel8086);
        Assert.That(accesses.Select(a => a.Address), Is.EqualTo(odd ? new uint[] { 0x201, 0x202 } : [0x200u, 0x201u]));
        Assert.That(accesses.Select(a => a.Value), Is.EqualTo(new byte[] { 0x34, 0x12 }));
        Assert.That(accesses.Select(a => a.Clock), Is.EqualTo(odd ? new long[] { 3, 7 } : [3L, 3L]));
        Assert.That(accesses.All(a => a.Kind == expected), Is.True);
    }

    [Test]
    public void AnOddTransferWithBheInactiveHasNoActiveLaneAndIsRejected()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Transfer("MEMW", 0x201, 1, 0x1234)));
        Assert.Throws<InvalidDataException>(() => I8088BusVerification.ReadTransfers(document.RootElement, I8088Model.Intel8086));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void VerifierUsesTheWideCpuAndReportsAnIndexWhenTheCorpusHasNoIdx(bool wrong)
    {
        string folder = Path.Combine(Path.GetTempPath(), "retro-word-bus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "metadata.json"), """{"syntax_version":2,"cpu":"8086","opcodes":{"A3":{"status":"normal"}}}""");
            var test = new { name = "own aligned store without idx", bytes = new int[] { 0xA3, 0, 2 },
                initial = new { regs = new { cs = 0x100, ip = 0, ds = 0, ax = 0x1234 }, ram = Array.Empty<int[]>(), queue = new int[] { 0xA3, 0, 2, 0x90, 0x90, 0x90 } },
                cycles = Transfer("MEMW", 0x200, 0, wrong ? 0x1235 : 0x1234) };
            using (var file = File.Create(Path.Combine(folder, "A3.json.gz")))
            using (var gzip = new GZipStream(file, CompressionMode.Compress)) JsonSerializer.Serialize(gzip, new[] { test });
            string path = Path.Combine(folder, "report.json"); var output = Console.Out; var error = Console.Error; int result;
            try
            {
                Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null);
                result = I8088BusVerification.Run(folder, path, "A3", "1", I8088Model.Intel8086);
            }
            finally { Console.SetOut(output); Console.SetError(error); }
            Assert.That(result, Is.EqualTo(wrong ? 2 : 0));
            using var report = JsonDocument.Parse(File.ReadAllText(path));
            Assert.That(report.RootElement.GetProperty("Cpu").GetString(), Is.EqualTo("8086"));
            if (wrong) Assert.That(report.RootElement.GetProperty("Files")[0].GetProperty("Differences")[0].GetProperty("Index").GetInt32(), Is.Zero);
        }
        finally
        {
            foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
