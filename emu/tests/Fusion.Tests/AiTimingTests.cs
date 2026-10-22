using System.Text.Json;
using Retro.Ai;

namespace Fusion.Tests;

[TestFixture]
public class AiTimingTests
{
    private string _root = "", _path = "";
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class Fake(Clock clock) : IAiProvider
    {
        public string Name => "Proveedor simulado, modelo-prueba";
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); clock.Advance(60); return Task.FromResult("respuesta privada"); }
    }
    [SetUp] public void Setup() { _root = Path.Combine(Path.GetTempPath(), "RetroEmu-times-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); _path = Path.Combine(_root, "timing.json"); }
    [TearDown] public void Cleanup() => Directory.Delete(_root, true);
    private AiDurationHistory History(params double[] durations)
    {
        var history = new AiDurationHistory(_path);
        foreach (double duration in durations) history.Record(AiDurationHistory.Key("job"), TimeSpan.FromSeconds(duration));
        return history;
    }
    [Test]
    public void MissingHistoryIsExplicitAndCancelledWorkDoesNotTrain()
    {
        var clock = new Clock(); var timing = new AiTiming("job", History(), clock);
        clock.Advance(100); Assert.That(timing.Elapsed.TotalSeconds, Is.EqualTo(100));
        Assert.That(timing.Range, Is.Null); Assert.That(timing.RemainingText, Does.Contain("no hay tiempos"));
        timing.Stop(false); timing.Stop(true); clock.Advance(50);
        Assert.That(timing.Elapsed.TotalSeconds, Is.EqualTo(100)); Assert.That(File.Exists(_path), Is.False);
    }
    [Test]
    public async Task SimulatedProviderTrainsOnlySuccessfulJobsAndStoresNoContent()
    {
        var clock = new Clock(); var provider = new Fake(clock); var history = History();
        for (int n = 0; n < 3; n++)
        {
            var timing = new AiTiming(provider.Name, history, clock);
            await provider.CompleteAsync(new("instrucción privada", [new(true, "pedido privado")], null), CancellationToken.None);
            timing.Stop(true); timing.Stop(true);
        }
        var estimate = new AiTiming(provider.Name, new AiDurationHistory(_path), clock);
        Assert.That(estimate.Range!.Samples, Is.EqualTo(3));
        string stored = File.ReadAllText(_path);
        Assert.That(stored, Does.Not.Contain("privad").And.Not.Contain("Proveedor").And.Not.Contain("modelo"));
        using var json = JsonDocument.Parse(stored);
        Assert.That(json.RootElement.EnumerateObject().Single().Value.GetArrayLength(), Is.EqualTo(3));
        Assert.That(Directory.GetFiles(_root).Length, Is.EqualTo(1));
    }
    [Test]
    public void IntervalAdvancesAndOverrunNeverBecomesAFrozenZero()
    {
        var clock = new Clock(); var timing = new AiTiming("job", History(60, 80, 100), clock);
        Assert.That(timing.Range!.Low.TotalSeconds, Is.EqualTo(45)); Assert.That(timing.Range.High.TotalSeconds, Is.EqualTo(150));
        string start = timing.RemainingText; clock.Advance(50);
        Assert.That(timing.RemainingText, Is.Not.EqualTo(start).And.StartWith("Hasta"));
        clock.Advance(110); Assert.That(timing.RemainingText, Does.StartWith("Superó"));
        clock.Advance(40); Assert.That(timing.RemainingText, Does.Not.Contain("0:00"));
    }
    [TestCase("otro modelo")] [TestCase("otra operación")] [TestCase("otro esfuerzo")]
    public void DifferentGroupingNeverReusesAnEstimate(string identity)
    {
        var history = History(10, 20, 30);
        Assert.That(new AiTiming(identity, history).Range, Is.Null);
    }
    [Test]
    public void SamplesAreBoundedAndInvalidDurationsAreIgnored()
    {
        var history = History(); string key = AiDurationHistory.Key("job");
        for (int n = 1; n <= 35; n++) history.Record(key, TimeSpan.FromSeconds(n));
        history.Record(key, TimeSpan.Zero); history.Record(key, TimeSpan.FromHours(7));
        Assert.That(history.Estimate(key)!.Samples, Is.EqualTo(20)); Assert.That(new FileInfo(_path).Length, Is.LessThan(128 * 1024));
        for (int n = 0; n < 140; n++) history.Record(AiDurationHistory.Key("key" + n), TimeSpan.FromSeconds(3));
        using var json = JsonDocument.Parse(File.ReadAllText(_path)); Assert.That(json.RootElement.EnumerateObject().Count(), Is.EqualTo(128));
    }
    [TestCase("{malformed")] [TestCase("null")] [TestCase("{\"invalid-key\":[60,80,100]}")]
    public void BadHistoryDoesNotPreventCreation(string contents)
    {
        File.WriteAllText(_path, contents); var history = new AiDurationHistory(_path);
        Assert.That(new AiTiming("job", history).Range, Is.Null);
        history.Record(AiDurationHistory.Key("job"), TimeSpan.FromSeconds(10));
        Assert.That(new AiTiming("job", history).Range, Is.Null);
    }
    [Test]
    public void UnwritableHistoryDoesNotLoseSuccessfulResult()
    {
        Directory.CreateDirectory(_path); var clock = new Clock(); var timing = new AiTiming("job", new AiDurationHistory(_path), clock); clock.Advance(30);
        Assert.DoesNotThrow(() => timing.Stop(true)); Assert.That(timing.Elapsed.TotalSeconds, Is.EqualTo(30));
    }
}