using Retro.App;

namespace App.Tests;

public class AiConnectionProbeTests
{
    private static async Task Finish(AiConnectionProbe probe)
    {
        for (int i = 0; i < 150 && probe.Busy; i++) { probe.Poll(); await Task.Delay(10); }
        Assert.That(probe.Busy, Is.False, "The check should settle without another dispatch.");
    }

    [Test]
    public async Task Success_RetainsTheConfigurationKeyAndAnswer()
    {
        var probe = new AiConnectionProbe();
        Assert.That(probe.Start("configured-check", _ => Task.FromResult("listo")), Is.True);
        await Finish(probe);
        Assert.That(probe.Key, Is.EqualTo("configured-check"));
        Assert.That(probe.Result, Is.EqualTo("Funciona. Respondió: \"listo\""));
    }

    [Test]
    public async Task Cancellation_ReachesTheRunningCheckWithoutRetryOrFailureMessage()
    {
        var probe = new AiConnectionProbe();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        probe.Start("first", async ct =>
        {
            Interlocked.Increment(ref calls); entered.SetResult();
            await Task.Delay(Timeout.Infinite, ct); return "unreachable";
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(probe.Start("second", _ => Task.FromResult("must not run")), Is.False);
        probe.Cancel();
        Assert.That(probe.IsCancelling, Is.True);
        await Finish(probe);
        Assert.That(probe.IsCancelling, Is.False);
        Assert.That(probe.Result, Is.EqualTo("Prueba cancelada. No se iniciará otro pedido."));
        probe.Cancel(); probe.Poll();
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(probe.Key, Is.EqualTo("first"));
    }

    [Test]
    public async Task ExplicitRestart_AfterCancellationUsesAFreshToken()
    {
        var probe = new AiConnectionProbe();
        probe.Start("first", async ct => { await Task.Delay(Timeout.Infinite, ct); return "unreachable"; });
        probe.Cancel(); await Finish(probe);
        bool canceled = true;
        Assert.That(probe.Start("second", ct => { canceled = ct.IsCancellationRequested; return Task.FromResult("listo"); }), Is.True);
        await Finish(probe);
        Assert.That(canceled, Is.False);
        Assert.That(probe.Key, Is.EqualTo("second"));
        Assert.That(probe.Result, Does.StartWith("Funciona."));
    }

    [Test]
    public async Task Failure_PreservesTheDiagnosticAndDoesNotRetry()
    {
        var probe = new AiConnectionProbe();
        int calls = 0;
        probe.Start("configured-check", _ => { calls++; throw new InvalidOperationException("local transport failure"); });
        await Finish(probe);
        Assert.That(probe.Result, Is.EqualTo("No funcionó: local transport failure"));
        probe.Poll(); Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task ShutdownCancellation_ReturnsTheRunningTaskForTheSharedDeadline()
    {
        var probe = new AiConnectionProbe();
        probe.Start("running", async ct => { await Task.Delay(Timeout.Infinite, ct); return "unreachable"; });
        var pending = probe.CancelWork();
        Assert.CatchAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(3)));
        await Finish(probe);
        Assert.That(probe.Result, Does.StartWith("Prueba cancelada."));
        Assert.That(probe.CancelWork().IsCompleted, Is.True);
    }
}
