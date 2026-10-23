using System.Reflection;
using Retro.Ai;
using Retro.App;
using Retro.Fusion;
using Retro.Machines;

namespace App.Tests;

[TestFixture, NonParallelizable]
public class ModificationAnalysisUiTests
{
    private string _directory = "";
    private string? _oldProfiles;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object? Get(CodeWindow window,string name)=>typeof(CodeWindow).GetField(name,Private)!.GetValue(window);
    private static object? Call(CodeWindow window,string name,params object[] arguments)=>typeof(CodeWindow).GetMethod(name,Private)!.Invoke(window,arguments);
    private static string Answer(int value)=>$$"""
        {"analysis":"El byte se incrementa en 9003. Su significado debe comprobarse.",
         "variables":[{"name":"counter_test","label":"Contador","address":"8000","bytes":1,"observed":{{value}},"reason":"INC (HL) en 9003 escribe el byte observado."}],
         "ideas":[{"title":"Cambiar el contador","effect":"Prueba de comportamiento, sin efecto confirmado.","changes":[{"variable":"counter_test","value":17,"freeze":true}]}]}
        """;
    private sealed class SimulatedAi(string answer,string name="IA simulada"):IAiProvider
    {
        public string Name=>name;
        public Task<string> CompleteAsync(AiRequest request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(answer);}
    }
    private sealed class DelayedAi:IAiProvider
    {
        public string Name=>"IA simulada demorada";
        public readonly TaskCompletionSource<string> Answer=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> CompleteAsync(AiRequest request,CancellationToken ct)=>Answer.Task;
    }

    [SetUp]
    public void Setup()
    {
        _directory=Path.Combine(Path.GetTempPath(),"retro-analysis-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);File.WriteAllBytes(Path.Combine(_directory,"48.rom"),new byte[0x4000]);
        _oldProfiles=Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES",Path.Combine(_directory,"profiles"));
    }
    [TearDown]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES",_oldProfiles);
        Directory.Delete(_directory,true);
    }
    private (EmulatorHost Host,CodeWindow Window) Start()
    {
        var host=new EmulatorHost();
        host.Start(MachineCatalog.Get("zx48"),_directory);
        host.Load(new Retro.Core.MediaFile("analysis-own-"+Guid.NewGuid().ToString("N")+".tap",[3,0,255,0,255],"own.tap"),false);
        host.LoadBinary([0x21,0,0x80,0x34,0xC3,3,0x90],0x9000,true);
        var window=new CodeWindow(host,new AiSettings(),_=>{});
        window.OpenModifications();window.Update();
        for(int step=0;step<7;step++)host.Machine!.StepInstruction();
        return(host,window);
    }
    private static async Task Finish(CodeWindow window)
    {
        var task=(Task)Get(window,"_modificationTask")!;
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        window.Update();
    }

    [Test]
    public async Task AnalysisWithoutAKnownProfilePopulatesTheChangesPageAndDoesNotApplyAnything()
    {
        var(host,window)=Start();using var owned=window;
        byte[] before=host.Machine!.SaveState();
        Call(window,"BeginModificationAnalysis",new SimulatedAi(Answer(host.Machine.Memory.Peek(0x8000))));
        await Finish(window);
        var profile=(GameProfile)Get(window,"_profile")!;
        Assert.That(profile.Variables.ContainsKey("counter_test"),Is.True);
        Assert.That(host.LoadedProfile,Is.Null,"Session hypotheses must not replace the prepared fusion profile.");
        Assert.That(host.Machine.SaveState(),Is.EqualTo(before));
        Assert.That(Get(window,"_status")!.ToString(),Does.Contain("1 variable nueva"));
        Assert.That(((GameModifications)Get(window,"_modifications")!).CanRestore,Is.False);
    }

    [Test]
    public async Task ApplyingAndUndoingAProposalKeepsTheSessionDefinitionsAndRemainingHistory()
    {
        var(host,window)=Start();using var owned=window;
        byte[] before=host.Machine!.SaveState();
        Call(window,"BeginModificationAnalysis",new SimulatedAi(Answer(host.Machine.Memory.Peek(0x8000))));
        await Finish(window);
        Call(window,"ApplyModifications",(object)new VariableEdit[]{new("counter_test",17,true)});
        byte[] first=host.Machine.SaveState();
        Call(window,"ApplyModifications",(object)new VariableEdit[]{new("counter_test",23,true)});
        var manager=Get(window,"_modifications");
        Call(window,"RestoreModifications",true);window.Update();
        Assert.That(host.Machine.SaveState(),Is.EqualTo(first));
        Assert.That(Get(window,"_modifications"),Is.SameAs(manager));
        Assert.That(((GameProfile)Get(window,"_profile")!).Variables.ContainsKey("counter_test"),Is.True);
        Call(window,"RestoreModifications",true);window.Update();
        Assert.That(host.Machine.SaveState(),Is.EqualTo(before));
    }

    [Test]
    public async Task AResponseFromAnEarlierGameIsDiscardedEvenIfItsProviderIgnoresCancellation()
    {
        var(host,window)=Start();using var owned=window;
        int observed=host.Machine!.Memory.Peek(0x8000);var ai=new DelayedAi();
        Call(window,"BeginModificationAnalysis",ai);
        var oldTask=(Task)Get(window,"_modificationTask")!;
        host.Load(new Retro.Core.MediaFile("second-own.tap",[3,0,255,1,254],"second-own.tap"),false);window.Update();
        byte[] before=host.Machine.SaveState();
        ai.Answer.SetResult(Answer(observed));
        try {await oldTask.WaitAsync(TimeSpan.FromSeconds(5));}catch(OperationCanceledException){}
        window.Update();
        Assert.That(Get(window,"_lastAnalysis"),Is.EqualTo(""));
        Assert.That(Get(window,"_lastAnalysisProvider"),Is.EqualTo(""));
        Assert.That(Get(window,"_analysisAnswer"),Is.EqualTo(""));
        Assert.That(host.Machine.SaveState(),Is.EqualTo(before));
        Assert.That(Get(window,"_profile"),Is.Null);
    }

    [Test]
    public async Task SavingProfileSettingsExcludesHypothesesDiscoveredByTheAi()
    {
        var(host,window)=Start();using var owned=window;
        Call(window,"BeginModificationAnalysis",new SimulatedAi(Answer(host.Machine!.Memory.Peek(0x8000))));
        await Finish(window);
        Call(window,"SaveProfile");
        var profile=(GameProfile)Get(window,"_profile")!;
        Assert.That(ProfileLibrary.Find(profile.Id)!.Variables.ContainsKey("counter_test"),Is.False);
        Assert.That(profile.Variables.ContainsKey("counter_test"),Is.True);
    }

    [Test]
    public async Task AManualVariableAddedWhileWaitingCannotBeOverriddenByANewAliasFromTheAi()
    {
        var(host,window)=Start();using var owned=window;
        int observed=host.Machine!.Memory.Peek(0x8000);var ai=new DelayedAi();
        Call(window,"BeginModificationAnalysis",ai);
        typeof(CodeWindow).GetField("_newVarName",Private)!.SetValue(window,"manual_counter");
        Call(window,"AddVariable",host.Machine,0x8000u,(byte)observed);
        ai.Answer.SetResult(Answer(observed));await Finish(window);
        var profile=(GameProfile)Get(window,"_profile")!;
        Assert.That(profile.Variables.ContainsKey("manual_counter"),Is.True);
        Assert.That(profile.Variables.ContainsKey("counter_test"),Is.False);
        Assert.That(Get(window,"_status")!.ToString(),Does.Contain("captura nueva"));
    }

    [Test]
    public async Task AManualFindingAfterAnAiRequestKeepsAnExactMatchToTheLoadedEdition()
    {
        var(host,window)=Start();using var owned=window;
        Call(window,"BeginModificationAnalysis",new SimulatedAi(Answer(host.Machine!.Memory.Peek(0x8000))));
        await Finish(window);
        typeof(CodeWindow).GetField("_newVarName",Private)!.SetValue(window,"manual_counter");
        Call(window,"AddVariable",host.Machine,0x8002u,(byte)0);
        var session=(GameProfile)Get(window,"_profile")!;
        var saved=ProfileLibrary.Find(session.Id)!;
        Assert.That(saved.Match.Sha256,Is.EqualTo(host.LoadedMediaHashes));
        Assert.That(saved.Variables.ContainsKey("manual_counter"),Is.True);
        Assert.That(saved.Variables.ContainsKey("counter_test"),Is.False);
        host.RefreshLoadedProfile();
        Assert.That(host.LoadedProfile?.Id,Is.EqualTo(saved.Id));
    }

    [Test]
    public async Task AFailedNewAttemptKeepsThePreviousReportClearlyAttributedAndTheNewResponseCopyable()
    {
        var(host,window)=Start();using var owned=window;
        const string first="""{"analysis":"Primer informe aceptado.","variables":[],"ideas":[]}""";
        Call(window,"BeginModificationAnalysis",new SimulatedAi(first,"Proveedor anterior"));
        await Finish(window);
        long frame=((GameAnalysisSnapshot)Get(window,"_analysisSnapshot")!).Frame;
        var delayed=new DelayedAi();Call(window,"BeginModificationAnalysis",delayed);
        var task=(Task)Get(window,"_modificationTask")!;
        Assert.That(typeof(CodeWindow).GetProperty("AnalysisReportHeading",Private)!.GetValue(window),Is.EqualTo("Informe anterior"));
        Assert.That(Get(window,"_lastAnalysisProvider"),Is.EqualTo("Proveedor anterior"));
        Assert.That(Get(window,"_lastAnalysisFrame"),Is.EqualTo(frame));
        delayed.Answer.SetResult("Respuesta sin el objeto solicitado.");
        try {await task.WaitAsync(TimeSpan.FromSeconds(5));}catch(AiException){}
        window.Update();
        Assert.That(Get(window,"_lastAnalysis"),Is.EqualTo("Primer informe aceptado."));
        Assert.That(Get(window,"_analysisAnswer"),Is.EqualTo("Respuesta sin el objeto solicitado."));
        Assert.That(Get(window,"_analysisError")!.ToString(),Does.Contain("objeto"));
        Assert.That(Get(window,"_status")!.ToString(),Does.Contain("informe anterior").And.Contain("Proveedor anterior"));
        Assert.That(Get(window,"_analysisPrompt")!.ToString(),Does.Contain("Primer informe aceptado").And.Contain("\"variables\""));
        const string second="""{"analysis":"Segundo informe aceptado.","variables":[],"ideas":[]}""";
        Call(window,"BeginModificationAnalysis",new SimulatedAi(second,"Proveedor nuevo"));
        await Finish(window);
        Assert.That(Get(window,"_lastAnalysis"),Is.EqualTo("Segundo informe aceptado."));
        Assert.That(Get(window,"_lastAnalysisProvider"),Is.EqualTo("Proveedor nuevo"));
        Assert.That(Get(window,"_analysisError"),Is.EqualTo(""));
        Assert.That(typeof(CodeWindow).GetProperty("AnalysisReportHeading",Private)!.GetValue(window),Is.EqualTo("Informe"));
    }

    [Test]
    public async Task ProviderFailuresDoNotEraseTheRequestOrPretendThePreviousReportWasUpdated()
    {
        var(host,window)=Start();using var owned=window;
        const string first="""{"analysis":"Informe previo.","variables":[],"ideas":[]}""";
        Call(window,"BeginModificationAnalysis",new SimulatedAi(first,"Primero"));
        await Finish(window);
        var delayed=new DelayedAi();Call(window,"BeginModificationAnalysis",delayed);
        var task=(Task)Get(window,"_modificationTask")!;
        delayed.Answer.SetException(new AiException("Fallo simulado del proceso."));
        try {await task.WaitAsync(TimeSpan.FromSeconds(5));}catch(AiException){}
        window.Update();
        Assert.That(Get(window,"_analysisProvider"),Is.EqualTo("IA simulada demorada"));
        Assert.That(Get(window,"_lastAnalysisProvider"),Is.EqualTo("Primero"));
        Assert.That(Get(window,"_analysisAnswer"),Is.EqualTo(""));
        Assert.That(Get(window,"_analysisPrompt")!.ToString(),Does.Contain("Pregunta o pedido"));
        Assert.That(Get(window,"_analysisError"),Is.EqualTo("Fallo simulado del proceso."));
        Assert.That(((GameProfile)Get(window,"_profile")!).Variables,Is.Empty);
    }

    [Test]
    public void ClosingTheToolsStopsObservationAndKeepsTheMachineUnchanged()
    {
        var(host,window)=Start();using var owned=window;
        byte[] before=host.Machine!.SaveState();
        Assert.That(Get(window,"_observation"),Is.Not.Null);
        window.Close();window.Update();
        Assert.That(Get(window,"_observation"),Is.Null);
        Assert.That(host.Machine.SaveState(),Is.EqualTo(before));
    }
}
