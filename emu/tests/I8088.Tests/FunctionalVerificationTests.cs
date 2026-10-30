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
}
