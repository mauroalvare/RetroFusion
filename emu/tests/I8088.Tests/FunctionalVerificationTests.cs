using System.IO.Compression;
using System.Text.Json;
using Retro.Cpu.I8088;

namespace I8088.Tests;

[NonParallelizable]
public sealed class FunctionalVerificationTests
{
    private string _folder = null!;
    [SetUp]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "retro-functional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }
    [TearDown]
    public void Cleanup()
    {
        foreach (string path in Directory.GetFiles(_folder)) File.Delete(path);
        Directory.Delete(_folder);
    }
    private void Fixture(string cpu, int syntax = 2, bool wrong = false, bool empty = false, string status = "normal", int[]? bytes = null, bool indexed = true)
    {
        File.WriteAllText(Path.Combine(_folder, "metadata.json"), JsonSerializer.Serialize(new
        { syntax_version = syntax, cpu, opcodes = new Dictionary<string, object> { ["A3"] = new { status } } }));
        var test = new { name = "own word store", idx = 0, bytes = bytes ?? [0xA3, 0x20, 0],
            initial = new { regs = new { cs = 0x100, ip = 0, ds = 0x200, ss = 0x300, sp = 0x100,
                es = 0, ax = 0x1234, bx = 0, cx = 0, dx = 0, bp = 0, si = 0, di = 0, flags = 0xF002 }, ram = Array.Empty<int[]>() },
            final = new { regs = new { ip = 3 }, ram = new int[][] { [0x2020, 0x34], [0x2021, wrong ? 0x13 : 0x12] } } };
        using var file = File.Create(Path.Combine(_folder, "A3.json.gz")); using var gzip = new GZipStream(file, CompressionMode.Compress);
        var cases = JsonSerializer.SerializeToNode(empty ? [] : new[] { test })!;
        if (!empty && !indexed) cases.AsArray()[0]!.AsObject().Remove("idx");
        JsonSerializer.Serialize(gzip, cases);
    }
    private int Run(I8088Model model)
    {
        var output = Console.Out; var error = Console.Error;
        try { Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null); return I8088Verification.Run(_folder, Path.Combine(_folder, "report.json"), model); }
        finally { Console.SetOut(output); Console.SetError(error); }
    }
    [TestCase(I8088Model.Intel8088, "8088")]
    [TestCase(I8088Model.Intel8086, "8086")]
    public void SelectedModelMatchesOwnFunctionalFixture(I8088Model model, string cpu)
    {
        Fixture(cpu); int code = Run(model);
        Assert.That(code, Is.Zero, File.ReadAllText(Path.Combine(_folder, "report.json")));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "report.json")));
        Assert.That(report.RootElement.GetProperty("Passed").GetInt32(), Is.EqualTo(1));
        Assert.That(report.RootElement.TryGetProperty("Cpu", out var value), Is.EqualTo(model == I8088Model.Intel8086));
        if (model == I8088Model.Intel8086) Assert.That(value.GetString(), Is.EqualTo("8086"));
    }
    [TestCase(I8088Model.Intel8088, "8086")]
    [TestCase(I8088Model.Intel8086, "8088")]
    public void AnotherModelsCorpusIsRejected(I8088Model model, string cpu)
    { Fixture(cpu); Assert.That(Run(model), Is.EqualTo(1)); }
    [TestCase(I8088Model.Intel8088, "8088")]
    [TestCase(I8088Model.Intel8086, "8086")]
    public void WrongMemoryIsAFailureInsteadOfAPass(I8088Model model, string cpu)
    {
        Fixture(cpu, wrong: true); Assert.That(Run(model), Is.EqualTo(2));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "report.json")));
        Assert.That(report.RootElement.GetProperty("Files")[0].GetProperty("Errors")[0].GetProperty("Problem").GetString(), Does.StartWith("RAM "));
    }
    [TestCase(true, "normal")]
    [TestCase(false, "alias")]
    public void EmptyOrEntirelyExcludedSelectionDoesNotCertifyAnyCases(bool empty, string status)
    { Fixture("8086", empty: empty, status: status); Assert.That(Run(I8088Model.Intel8086), Is.EqualTo(1)); }
    [Test]
    public void DatasetVersionDoesNotReplaceItsDeclaredSyntaxVersion()
    { Fixture("8086", syntax: 1); Assert.That(Run(I8088Model.Intel8086), Is.EqualTo(1)); }
    [Test]
    public void AStreamContainingOnlyPrefixesIsAnInputError()
    { Fixture("8086", bytes: [0x26, 0xF3]); Assert.That(Run(I8088Model.Intel8086), Is.EqualTo(1)); }
    [Test]
    public void AFailedCaseWithoutIdxStillProducesAReproducibleReport()
    {
        Fixture("8086", wrong: true, indexed: false); Assert.That(Run(I8088Model.Intel8086), Is.EqualTo(2));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "report.json")));
        Assert.That(report.RootElement.GetProperty("Files")[0].GetProperty("Errors")[0].GetProperty("Index").GetInt32(), Is.Zero);
    }
    private void DivideFixture(string cpu, int sp, bool customVector = false, bool wrongCarry = false,
        bool wrongAdjacent = false, bool successful = false)
    {
        var detail = new Dictionary<string, object> { ["status"] = "normal", ["flags-mask"] = I8088Cpu.Carry };
        var members = new Dictionary<string, object> { ["6"] = detail };
        var opcodes = new Dictionary<string, object> { ["F7"] = new { reg = members } };
        File.WriteAllText(Path.Combine(_folder, "metadata.json"), JsonSerializer.Serialize(new
        { syntax_version = 2, cpu, opcodes }));
        int ss = 0x320, cs = successful ? 0 : 0x100, ip = successful ? 0x3FE : 0;
        int vectorIp = customVector ? 0x40 : 0x400, vectorCs = customVector ? 0x110 : 0;
        int low = (int)I8088Cpu.Physical((ushort)ss, (ushort)(sp - 2));
        int high = (int)I8088Cpu.Physical((ushort)ss, (ushort)(sp - 1));
        var memory = new List<int[]>();
        if (successful) memory.Add([low, 0x10]); // RAM stays 90h; this is unrelated to undefined CPU flags.
        else
        {
            memory.Add([low, wrongCarry ? 0x83 : wrongAdjacent ? 0x02 : 0x82]);
            memory.Add([high, wrongAdjacent ? 0xF0 : 0xF8]);
            if (wrongAdjacent) memory.Add([(low + 1) & 0xFFFFF, 0]);
        }
        var test = new { name = "own division frame", bytes = new[] { 0xF7, 0xF0 },
            initial = new { regs = new { cs, ip, ds = 0x200, ss, sp, es = 0, ax = successful ? 1 : 0,
                bx = 0, cx = 0, dx = 0, bp = 0, si = 0, di = 0, flags = 0xF002 },
                ram = new int[][] { [0, vectorIp & 255], [1, vectorIp >> 8], [2, vectorCs & 255], [3, vectorCs >> 8] } },
            final = new { regs = new { cs = successful ? cs : vectorCs, ip = successful ? 0x400 : vectorIp,
                sp = successful ? sp : (ushort)(sp - 6), flags = 0xF082 }, ram = memory } };
        using var file = File.Create(Path.Combine(_folder, "F7.6.json.gz")); using var gzip = new GZipStream(file, CompressionMode.Compress);
        JsonSerializer.Serialize(gzip, new[] { test });
    }
    [TestCase(I8088Model.Intel8088, "8088", 0x100)]
    [TestCase(I8088Model.Intel8086, "8086", 0x100)]
    [TestCase(I8088Model.Intel8088, "8088", 1)]
    [TestCase(I8088Model.Intel8086, "8086", 1)]
    public void UndefinedStackFlagsUseTheActualVectorAndSegmentOffsetWrap(I8088Model model, string cpu, int sp)
    {
        DivideFixture(cpu, sp, customVector: true);
        Assert.That(Run(model), Is.Zero, File.ReadAllText(Path.Combine(_folder, "report.json")));
    }
    [TestCase(I8088Model.Intel8088, "8088", 0x100)]
    [TestCase(I8088Model.Intel8086, "8086", 0x100)]
    [TestCase(I8088Model.Intel8088, "8088", 1)]
    [TestCase(I8088Model.Intel8086, "8086", 1)]
    public void DefinedStackFlagsStillRejectAWrongCarry(I8088Model model, string cpu, int sp)
    { DivideFixture(cpu, sp, wrongCarry: true); Assert.That(Run(model), Is.EqualTo(2)); }
    [TestCase(I8088Model.Intel8088, "8088")]
    [TestCase(I8088Model.Intel8086, "8086")]
    public void LinearAddressAfterWrappedFlagsIsOrdinaryMemory(I8088Model model, string cpu)
    { DivideFixture(cpu, 1, wrongAdjacent: true); Assert.That(Run(model), Is.EqualTo(2)); }
    [TestCase(I8088Model.Intel8088, "8088")]
    [TestCase(I8088Model.Intel8086, "8086")]
    public void ARegularDivideEndingAt0400DoesNotCreateAnExceptionMask(I8088Model model, string cpu)
    { DivideFixture(cpu, 0x100, successful: true); Assert.That(Run(model), Is.EqualTo(2)); }
}
