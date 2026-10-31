using Retro.Compat;
using Retro.Core;
using Retro.Fusion;

namespace Compatibility.Tests;

[NonParallelizable]
public sealed class AutonomousLiveTests
{
    [Test]
    public async Task AProgramWithControlsCanBeInvestigatedFromColdBootWithoutPlayerInput()
    {
        string folder=Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),"retro-auto-live-"+Guid.NewGuid())).FullName;
        string? old=Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        try
        {
            Environment.SetEnvironmentVariable("RETROEMU_PROFILES",Path.Combine(folder,"profiles"));
            string path=Path.Combine(folder,"programa-propio.nes");byte[] bytes=PreparationNesFixture.Rom();File.WriteAllBytes(path,bytes);
            var files=MediaArchive.Open(path);var profile=GameCatalog.BasicProfile(path,files,"nes",files[0]);
            var store=new AutonomousResearchStore(Path.Combine(folder,"checkpoints"));
            var result=await new AutonomousResearch().RunAsync(path,profile,"",new(TargetVariables:2,Minutes:2,AiCalls:0,MaxPasses:1,LabFrames:120,
                Goal:"Investigar movimiento, salto y contadores con entradas automatizadas."),checkpoint:store.Save);
            Assert.That(result.Status,Is.Not.EqualTo("error"),result.StopReason);
            Assert.That(result.CompletedPasses,Is.EqualTo(1),result.StopReason);
            Assert.That(result.Candidates,Is.GreaterThan(0));
            Assert.That(result.Evidence.Findings.Where(f=>f.Kind.StartsWith("position")),
                Has.All.Matches<LabFinding>(f=>f.Verification!=null),"Frequent writes are a hypothesis of a copy, not a reason to skip the write probe.");
            Assert.That(result.Evidence.Findings,Has.Some.Matches<LabFinding>(f=>f.Address=="0010" && f.Status=="verified"),
                "The own program retains its horizontal position and uses the modified value to draw the sprite.");
            Assert.That(result.Checked,Is.Not.Empty,"The laboratory must produce observations, not just a proposed name.");
            Assert.That(result.AiCallsUsed,Is.Zero);
            Assert.That(store.Read().Single().Evidence.Findings,Is.Not.Empty);
            Assert.That(File.ReadAllBytes(path),Is.EqualTo(bytes));
            Assert.That(Directory.Exists(Path.Combine(folder,"profiles"))?Directory.GetFiles(Path.Combine(folder,"profiles"),"*.json"):[],Is.Empty,"Research must not silently promote hypotheses to the user's profiles.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RETROEMU_PROFILES",old);
            if(!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Temporary cleanup escaped its root.");
            Directory.Delete(folder,true);
        }
    }
}
