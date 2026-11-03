using Retro.Ai;
using Retro.Compat;
using Retro.Core;

namespace Fusion.Tests;

public sealed class AdvisorCapabilityTests
{
    private sealed class Fake(bool images) : IAiProvider
    {
        public string Name=>"Own provider";
        public bool SupportsImages=>images;
        public AiRequest? Received;
        public Task<string> CompleteAsync(AiRequest request,CancellationToken ct)
        {
            Received=request;
            if(images) Assert.That(request.Images?.All(File.Exists),Is.True);
            return Task.FromException<string>(new AiException("Own simulated rejection"));
        }
    }

    [TestCase(false,"play"),TestCase(false,"variables"),TestCase(false,"experiments")]
    [TestCase(true,"play"),TestCase(true,"variables"),TestCase(true,"experiments")]
    public void AdvisorsAttachOnlySupportedImagesAndCleanThemEvenAfterProviderFailure(bool supportsImages,string action)
    {
        var fake=new Fake(supportsImages);var advisor=new AiPlayAdvisor(fake);var picture=new VideoFrame(2,2);
        Array.Fill(picture.Pixels,unchecked((int)0xff123456));
        Assert.ThrowsAsync<AiException>(async()=>await Run(advisor,action,picture,CancellationToken.None));
        Assert.That(fake.Received,Is.Not.Null);
        if(!supportsImages)Assert.That(fake.Received!.Images,Is.Null);
        else Assert.That(fake.Received!.Images!.All(path=>!File.Exists(path)),Is.True);
    }

    [TestCase("play"),TestCase("variables"),TestCase("experiments")]
    public void APreCancelledAdvisorDoesNotCreateImagesOrDispatch(string action)
    {
        var fake=new Fake(true);using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async()=>await Run(new AiPlayAdvisor(fake),action,new VideoFrame(2,2),cancel.Token));
        Assert.That(fake.Received,Is.Null);
    }

    private static async Task Run(AiPlayAdvisor advisor,string action,VideoFrame picture,CancellationToken ct)
    {
        if(action=="play")await advisor.AdviseAsync(new PlayObservation{Picture=picture},ct);
        else if(action=="variables")await advisor.NameVariablesAsync(new VariableQuestion{Picture=picture},ct);
        else await advisor.PlanExperimentsAsync(new PreparationExperimentQuestion{Picture=picture},ct);
    }
}
