using System.IO.Compression;
using System.Text.Json;
using Retro.Cpu.I8088;

namespace I8088.Tests;

[NonParallelizable]
public sealed class BusVerificationTests
{
    private string _folder = null!;
    [SetUp]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "retrofusion-bus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "metadata.json"),
            """{"syntax_version":2,"cpu":"8088","opcodes":{"A3":{"status":"normal"}}}""");
    }
    [TearDown]
    public void Cleanup()
    {
        foreach (string file in Directory.GetFiles(_folder)) File.Delete(file);
        Directory.Delete(_folder);
    }
    private static object[] Row(int pins, int address, string memory, int data, string kind, string phase, string ports = "---")
        => [pins, address, "--", memory, ports, 0, data, kind, phase, "-", 0];
    private void WriteCase(bool warm = false, bool stale = false, bool wrongValue = false, bool delayed = false)
    {
        var cycles = new List<object[]> {
            Row(1, 0x200, "---", 0, "MEMW", "T1"), Row(0, 0, "-A-", 0, "MEMW", "T2"),
            Row(0, 0, "-AW", 0x34, "PASV", "T3"), Row(0, 0, "---", 0, "PASV", "T4")
        };
        if (delayed) cycles.Add(Row(0, 0, "---", 0, "PASV", "Ti"));
        cycles.AddRange([
            Row(1, 0x201, "---", 0, "MEMW", "T1"), Row(0, 0, "-A-", 0, "MEMW", "T2"),
            Row(0, 0, "-AW", wrongValue ? 0x56 : 0x12, "PASV", "T3"), Row(0, 0, "---", 0, "PASV", "T4")
        ]);
        var test = new {
            name = "own word-store fixture", idx = 0, bytes = new int[] { stale ? 0xA2 : 0xA3, 0, 2 },
            initial = new { regs = new { ax = 0x1234, cs = 0x100, ip = 0, ds = 0 }, ram = Array.Empty<int[]>(),
                queue = warm ? new int[] { 0xA3, 0, 2, 0x90 } : Array.Empty<int>() }, cycles
        };
        using var file = File.Create(Path.Combine(_folder, "A3.json.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        JsonSerializer.Serialize(gzip, new[] { test });
    }
    private int Run(string? opcodes = "A3", string? cases = "1")
    {
        var output = Console.Out; var error = Console.Error;
        try { Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null);
            return I8088BusVerification.Run(_folder, Path.Combine(_folder, "report.json"), opcodes, cases); }
        finally { Console.SetOut(output); Console.SetError(error); }
    }
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void MatchesIndependentWordTraceWithEmptyFullOrStaleQueue(bool warm, bool stale)
    {
        WriteCase(warm, stale);
        Assert.That(Run(), Is.Zero);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "report.json")));
        Assert.That(report.RootElement.GetProperty("Checked").GetInt32(), Is.EqualTo(1));
        Assert.That(report.RootElement.GetProperty("TimingComparedPairs").GetInt32(), Is.EqualTo(1));
        Assert.That(report.RootElement.GetProperty("SequenceFailures").GetInt32(), Is.Zero);
    }
    [Test]
    public void WrongDataAndWrongSpacingAreReportedAsDifferentFailures()
    {
        WriteCase(wrongValue: true); Assert.That(Run(), Is.EqualTo(2));
        using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "report.json"))))
        {
            Assert.That(report.RootElement.GetProperty("SequenceFailures").GetInt32(), Is.EqualTo(1));
            Assert.That(report.RootElement.GetProperty("RelativeTimingFailures").GetInt32(), Is.Zero);
        }
        WriteCase(delayed: true); Assert.That(Run(), Is.EqualTo(2));
        using var timing = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "report.json")));
        Assert.That(timing.RootElement.GetProperty("SequenceFailures").GetInt32(), Is.Zero);
        Assert.That(timing.RootElement.GetProperty("RelativeTimingFailures").GetInt32(), Is.EqualTo(1));
    }
    [TestCase("../A3", "1")]
    [TestCase("A3", "0")]
    [TestCase("A3", "10001")]
    [TestCase("FE.8", "1")]
    [TestCase("FE-0", "1")]
    [TestCase("A3.0", "1")]
    public void InvalidSelectionOrBudgetIsRejected(string opcodes, string cases)
    {
        WriteCase(); Assert.That(Run(opcodes, cases), Is.EqualTo(1));
    }
    private void WriteGroupCase(int idle)
    {
        // Own INC byte [BX] trace: read, the EU's increment, and the write of the incremented value.
        var cycles = new List<object[]> {
            Row(1, 0x200, "---", 0, "MEMR", "T1"), Row(0, 0, "R--", 0, "MEMR", "T2"),
            Row(0, 0, "R--", 0x41, "PASV", "T3"), Row(0, 0, "---", 0, "PASV", "T4")
        };
        for (int n = 0; n < idle; n++) cycles.Add(Row(0, 0, "---", 0, "PASV", "Ti"));
        cycles.AddRange([
            Row(1, 0x200, "---", 0, "MEMW", "T1"), Row(0, 0, "-A-", 0, "MEMW", "T2"),
            Row(0, 0, "-AW", 0x42, "PASV", "T3"), Row(0, 0, "---", 0, "PASV", "T4")
        ]);
        var test = new {
            name = "own inc fixture", idx = 0, bytes = new int[] { 0xFE, 0x07 },
            initial = new { regs = new { bx = 0x200, cs = 0x100, ip = 0, ds = 0 }, ram = new[] { new[] { 0x200, 0x41 } },
                queue = new int[] { 0xFE, 0x07, 0x90, 0x90 } }, cycles
        };
        using var file = File.Create(Path.Combine(_folder, "FE.0.json.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        JsonSerializer.Serialize(gzip, new[] { test });
    }
    [Test]
    public void GroupFileUsesItsOpcodeMetadataAndComparesTheReadModifyWriteSpacing()
    {
        File.WriteAllText(Path.Combine(_folder, "metadata.json"),
            """{"syntax_version":2,"cpu":"8088","opcodes":{"FE":{"reg":{"0":{"status":"normal"}}},"A3":{"status":"normal"}}}""");
        WriteGroupCase(idle: 6);
        Assert.That(Run("FE.0"), Is.Zero);
        using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "report.json"))))
            Assert.That(report.RootElement.GetProperty("TimingComparedPairs").GetInt32(), Is.EqualTo(1));
        WriteGroupCase(idle: 5);
        Assert.That(Run("FE.0"), Is.EqualTo(2), "One clock less of EU work between read and write must be reported.");
    }
    [Test]
    public void EmptyFileAndFullyExcludedSelectionCannotPassWithoutEvidence()
    {
        using (var file = File.Create(Path.Combine(_folder, "A3.json.gz")))
        using (var gzip = new GZipStream(file, CompressionMode.Compress)) JsonSerializer.Serialize(gzip, Array.Empty<object>());
        Assert.That(Run(), Is.EqualTo(1));
        WriteCase();
        File.WriteAllText(Path.Combine(_folder, "metadata.json"),
            """{"syntax_version":2,"cpu":"8088","opcodes":{"A3":{"status":"undefined"}}}""");
        Assert.That(Run(), Is.EqualTo(1));
    }
    [Test]
    public void TraceLatchesAddressAtAleAndSamplesOnlyTheLastWaitState()
    {
        object[][] cycles = [
            Row(1, 0xABCDE, "---", 0, "MEMR", "T1"),
            Row(0, 0xAA, "R--", 0, "MEMR", "T2"),
            Row(0, 0xBB, "R--", 0x11, "PASV", "T3"),
            Row(0, 0xCC, "R--", 0x22, "PASV", "Tw"),
            Row(0, 0xDD, "R--", 0x33, "PASV", "Tw"),
            Row(0, 0xEE, "---", 0, "PASV", "T4")
        ];
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(cycles));
        Assert.That(I8088BusVerification.ReadTransfers(document.RootElement), Is.EqualTo(new[] {
            new I8088BusAccess(5, I8088BusAccessKind.MemoryRead, 0xABCDE, 0x33)
        }));
    }
    [Test]
    public void TraceWithoutAddressLatchIsRejected()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new[] { Row(0, 42, "R--", 1, "PASV", "T3") }));
        Assert.Throws<InvalidDataException>(() => I8088BusVerification.ReadTransfers(document.RootElement));
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(new[] { Row(0, 42, "R--", 0, "MEMR", "T2"), Row(0, 42, "R--", 1, "PASV", "T3") }));
        Assert.Throws<InvalidDataException>(() => I8088BusVerification.ReadTransfers(data.RootElement));
    }
    [Test]
    public void TraceMayStartInsideAPrefetchWhoseAddressWasLatchedEarlier()
    {
        object[][] cycles = [
            Row(0, 0x1234, "R--", 0, "CODE", "T2"), Row(0, 0x1234, "R--", 0x90, "PASV", "T3"), Row(0, 0, "---", 0, "PASV", "T4"),
            Row(1, 0x500, "---", 0, "MEMR", "T1"), Row(0, 0, "R--", 0, "MEMR", "T2"), Row(0, 0, "R--", 0x42, "PASV", "T3")
        ];
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(cycles));
        Assert.That(I8088BusVerification.ReadTransfers(document.RootElement), Is.EqualTo(new[] {
            new I8088BusAccess(6, I8088BusAccessKind.MemoryRead, 0x500, 0x42)
        }));
    }
}
