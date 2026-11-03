using System.Diagnostics;
using System.Text;
using Retro.Ai;

namespace Fusion.Tests;

[TestFixture, Platform("Win"), NonParallelizable]
public sealed class CliPipeBoundaryTests
{
    private string _folder = "";
    private static readonly AiRequest Request = new("Own instructions", [new(true,"Own prompt")], null);
    [SetUp] public void SetUp() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-cli-pipes-" + Guid.NewGuid().ToString("N"))).FullName;
    [TearDown] public void TearDown() => Directory.Delete(_folder, true);
    private string Script(string tool, string body)
    {
        string path = Path.Combine(_folder, tool + ".cmd");
        File.WriteAllText(path, "@echo off\r\n" + body.Replace("\n", "\r\n") + "\r\n"); return path;
    }
    private static CliTool Tool(string name) => name == "codex" ? CliTools.Codex : name == "claude" ? CliTools.Claude : CliTools.Google;

    [TestCase("claude"), TestCase("codex"), TestCase("gemini"), TestCase("agy")]
    public async Task DeadlineStillAppliesWhenTheParentExitsButAChildKeepsTheOutputPipeOpen(string tool)
    {
        string pidFile = Path.Combine(_folder, "own-child.pid");
        string child = "[IO.File]::WriteAllText('" + pidFile.Replace("'","''") + "',$PID.ToString());[Console]::WriteLine('own child holding pipe');Start-Sleep -Seconds 8";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(child));
        string exe = Script(tool, "more > nul\nstart \"\" /b powershell.exe -NoProfile -NonInteractive -EncodedCommand " + encoded + "\nexit /b 0");
        var provider = new CliAgentProvider(Tool(tool), exe, TimeSpan.FromSeconds(1));
        var watch = Stopwatch.StartNew();
        try
        {
            var pending = provider.CompleteAsync(Request, CancellationToken.None);
            var error = Assert.ThrowsAsync<AiException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(error!.Message, Does.Contain("tardó más"));
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
            Assert.That(provider.Activity!.Running, Is.False);
            Assert.That(provider.Activity.Status, Is.EqualTo("CLI detenido por tiempo máximo"));
        }
        finally
        {
            // Only the deliberately orphaned QA child is retired; the provider cannot kill an already-exited parent's tree.
            if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile), out int pid))
                try
                {
                    using var own = Process.GetProcessById(pid);
                    if (!own.HasExited && own.ProcessName.Equals("powershell", StringComparison.OrdinalIgnoreCase)) { own.Kill(true); await own.WaitForExitAsync(); }
                }
                catch (ArgumentException) { }
        }
    }

    [Test]
    public async Task AnExcessiveCliStreamStopsWithABoundedErrorInsteadOfWaitingForTheWholeRun()
    {
        string code = "[Console]::In.ReadToEnd()|Out-Null; $chunk='x'*16384; for($n=0;$n-lt1100;$n++){[Console]::Out.Write($chunk)}; Start-Sleep -Seconds 15";
        string exe = Script("agy", "powershell.exe -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(code)));
        var provider = new CliAgentProvider(CliTools.Google, exe, TimeSpan.FromSeconds(10));
        var error = Assert.ThrowsAsync<AiException>(async () => await provider.CompleteAsync(Request, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(12)));
        Assert.That(error!.Message, Does.Contain("demasiado grande"));
        Assert.That(provider.Activity!.Running, Is.False);
        Assert.That(provider.Activity.Output.Length, Is.LessThanOrEqualTo(CliActivity.MaxOutputLength));
        await Task.CompletedTask;
    }

    [TestCase("claude"), TestCase("codex"), TestCase("gemini"), TestCase("agy")]
    public void AnOrdinaryGenerationErrorIsNotMisreportedAsAQuotaLimit(string tool)
    {
        var provider = new CliAgentProvider(Tool(tool), Script(tool, "more > nul\necho could not generate code 1>&2\nexit /b 17"));
        var error = Assert.ThrowsAsync<AiException>(() => provider.CompleteAsync(Request, CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("falló").And.Not.Contain("límite de uso"));
    }

    [TestCase("own%PATH%"),TestCase("own!value"),TestCase("own\" & echo unexpected"),TestCase("own\nvalue")]
    public void AmbiguousCmdArgumentsAreRejectedBeforeLaunchingRatherThanExpanded(string model)
    {
        string marker=Path.Combine(_folder,"must-not-start.txt");
        string exe=Script("claude",$"echo started > \"{marker}\"");
        var provider=new CliAgentProvider(CliTools.Claude,exe,model:model);
        var error=Assert.ThrowsAsync<AiException>(()=>provider.CompleteAsync(Request,CancellationToken.None));
        Assert.That(error!.Message,Does.Contain(".cmd"));Assert.That(File.Exists(marker),Is.False);
    }

    [TestCase("claude"), TestCase("codex"), TestCase("gemini"), TestCase("agy")]
    public void AFinalEnvelopeCannotHideThatTheCliClosedItsInputBeforeReceivingTheRequest(string tool)
    {
        string reply = tool switch
        {
            "claude" => "echo {\"type\":\"result\",\"result\":\"own unbound answer\"}",
            "codex" => "echo own unbound answer > \"%~5\"\necho {\"type\":\"turn.completed\"}",
            "gemini" => "echo {\"response\":\"own unbound answer\"}",
            _ => "echo {\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":\"own unbound answer\"}}"
        };
        // Larger than an anonymous pipe: the child exits without reading any input.
        var request = Request with { Turns = [new(true, new string('x', 512 * 1024))] };
        var provider = new CliAgentProvider(Tool(tool), Script(tool, reply), TimeSpan.FromSeconds(5));
        var error = Assert.ThrowsAsync<AiException>(() => provider.CompleteAsync(request, CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("pedido completo"));
        Assert.That(provider.Activity!.Running, Is.False);
    }

    [TestCase(false), TestCase(true)]
    public void TimeoutExplainsTheDeliveryPhaseInTheSelectedEnglishLanguage(bool delivered)
    {
        Retro.Core.UiText.Language = "en";
        try
        {
            string exe = Script("claude", (delivered ? "more > nul\n" : "") + "ping -n 30 127.0.0.1 > nul");
            var provider = new CliAgentProvider(CliTools.Claude, exe, TimeSpan.FromMilliseconds(300));
            var request = delivered ? Request : Request with { Turns = [new(true, new string('x', 512 * 1024))] };
            var error = Assert.ThrowsAsync<AiException>(() => provider.CompleteAsync(request, CancellationToken.None));
            Assert.That(error!.Message, Does.Contain(delivered ? "The request reached the CLI" : "The request did not finish sending")
                .And.Not.Contain("Revisá"));
            Assert.That(provider.Activity!.Running, Is.False);
        }
        finally { Retro.Core.UiText.Language = "es"; }
    }
}
