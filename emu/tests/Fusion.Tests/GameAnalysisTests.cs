using Retro.Ai;
using Retro.Core;
using Retro.Fusion;
using Retro.Systems.Spectrum;

namespace Fusion.Tests;

public class GameAnalysisTests
{
    private sealed class SimulatedAi(string name, string answer) : IAiProvider
    {
        public string Name => name;
        public AiRequest? Request;
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
        { Request=request; ct.ThrowIfCancellationRequested(); return Task.FromResult(answer); }
    }

    private static (Spectrum48 Machine, ModificationObservation Observation) Running()
    {
        var machine=new Spectrum48(new byte[0x4000]);
        byte[] code=[0x21,0,0x80,0x34,0xC3,3,0x90]; // LD HL,8000; INC (HL); JP 9003.
        for(int i=0;i<code.Length;i++)machine.Memory.Poke((uint)(0x9000+i),code[i]);
        machine.Cpu.ProgramCounter=0x9000;machine.ExecutedMap=new bool[65536];
        var observation=new ModificationObservation(machine);
        for(int step=0;step<7;step++)machine.StepInstruction();
        return(machine,observation);
    }

    private static GameProfile Profile() => new() {Id="own-analysis",Name="Programa propio",Machine="zx48"};
    private static string Answer(int observed) => $$"""
        {"analysis":"La instrucción 9003 incrementa este byte. Su función en el juego es una hipótesis.",
        "variables":[{"name":"pace","label":"Contador experimental","address":"8000","bytes":1,"observed":{{observed}},
        "reason":"Escrituras observadas en 9003 y aumento del valor durante la partida."}],
        "ideas":[{"title":"Alterar el contador","effect":"Comprobar si cambia una regla; no es un efecto verificado.",
        "changes":[{"variable":"pace","value":90,"freeze":true}]}]}
        """;

    [TestCase("Claude Code")] [TestCase("Codex CLI")] [TestCase("Antigravity CLI")] [TestCase("Ollama")]
    public async Task AnyProviderReceivesRealEvidenceAndReturnsEditableHypothesesWithoutChangingTheGame(string name)
    {
        var(machine,observation)=Running();using var owned=observation;
        var snapshot=observation.Capture("Programa propio",Profile());
        byte[] before=machine.SaveState();
        int value=machine.Memory.Peek(0x8000);
        var ai=new SimulatedAi(name,Answer(value));
        var result=await GameModificationAnalyzer.AnalyzeAsync(ai,snapshot,"¿Qué controla este contador?","Investigación anterior.");
        Assert.That(ai.Request!.Turns[0].Text,Does.Contain("0x8000").And.Contain("9003").And.Contain("INC (HL)").And.Contain("¿Qué controla").And.Contain("Investigación anterior"));
        Assert.That(ai.Request.System,Does.Contain("hipótesis").And.Contain("No afirmes haber ejecutado pruebas"));
        Assert.That(ai.Request.Schema!.Value.GetProperty("properties").TryGetProperty("variables",out _),Is.True);
        Assert.That(result.Variables.Single().Name,Is.EqualTo("pace"));
        Assert.That(snapshot.Profile.Variables,Is.Empty,"Analysis must not alter a verified fusion profile.");
        Assert.That(machine.SaveState(),Is.EqualTo(before));
        var profile=Profile();profile.Variables["pace"]=GameModificationAnalyzer.Definition(result.Variables[0]);
        var edits=new GameModifications(machine,profile);edits.Apply(result.Advice.Ideas[0].Changes);
        Assert.That(machine.Memory.Peek(0x8000),Is.EqualTo(90));
        Assert.That(edits.Restore(),Is.True);Assert.That(machine.SaveState(),Is.EqualTo(before));
    }

    [Test]
    public void CaptureIsDetachedFromLaterPlayAndPreservesThePreviousObserver()
    {
        var machine=new Spectrum48(new byte[0x4000]);int observed=0;
        Action<ushort,byte,ushort> prior=(_,_,_)=>observed++;
        machine.WriteWatch=prior;
        using(var observation=new ModificationObservation(machine))
        {
            machine.Memory.Poke(0x8000,5);
            machine.WriteWatch!(0x8000,5,0x9000);
            var profile=Profile();var snapshot=observation.Capture("Propio",profile);
            machine.Memory.Poke(0x8000,99);profile.Name="Otro";
            Assert.That(snapshot.Candidates.Single(c=>c.Address==0x8000).Value,Is.EqualTo(5));
            Assert.That(snapshot.Profile.Name,Is.EqualTo("Programa propio"));
            Assert.That(observed,Is.GreaterThan(0));
        }
        Assert.That(machine.WriteWatch,Is.SameAs(prior));
    }

    [Test]
    public void DisposalDoesNotReplaceAnObserverInstalledByAnotherTool()
    {
        var machine=new Spectrum48(new byte[0x4000]);var observation=new ModificationObservation(machine);
        Action<ushort,byte,ushort> replacement=(_,_,_)=>{};
        machine.WriteWatch=replacement;observation.Dispose();
        Assert.That(machine.WriteWatch,Is.SameAs(replacement));
    }

    [Test]
    public void ObservationAndTheRequestStayBoundedAfterManyWrites()
    {
        var(machine,observation)=Running();using var owned=observation;
        for(int address=0x6000;address<0xD000;address++)machine.WriteWatch!((ushort)address,1,0x9003);
        var snapshot=observation.Capture("Propio",Profile());
        Assert.That(observation.WrittenAddresses,Is.EqualTo(2048));
        Assert.That(snapshot.Candidates.Count,Is.LessThanOrEqualTo(128));
        Assert.That(snapshot.Listing.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length,Is.LessThanOrEqualTo(160));
        Assert.That(snapshot.Prompt("",new string('x',10000)).Length,Is.LessThan(50000));
    }

    [TestCase("\"address\":\"8000\"","\"address\":\"D020\"")]
    [TestCase("\"address\":\"8000\"","\"address\":\"FFFF\"")]
    [TestCase("\"address\":\"8000\"","\"address\":\"0x8000\"")]
    [TestCase("\"observed\":3","\"observed\":77")]
    [TestCase("\"bytes\":1","\"bytes\":3")]
    [TestCase("\"name\":\"pace\"","\"name\":\"bad name\"")]
    [TestCase("\"value\":90","\"value\":256")]
    [TestCase("\"value\":90","\"value\":-1")]
    [TestCase("\"value\":90","\"value\":90,\"command\":\"write\"")]
    [TestCase("\"bytes\":1","\"bytes\":1,\"bytes\":2")]
    public void UnobservedAddressesInvalidValuesAndUnexpectedFieldsAreRejected(string old,string replacement)
    {
        var snapshot=new GameAnalysisSnapshot("Propio","zx48",0,"",[new(0x8000,3,0,1,0x9003,"INC")],Profile());
        Assert.Throws<AiException>(()=>GameModificationAnalyzer.Parse(Answer(3).Replace(old,replacement),snapshot));
    }

    [Test]
    public void ReadOnlyKnownVariablesAndNamesCannotBeOverridden()
    {
        var profile=Profile();profile.Variables["pace"]=new(){Address="8000",ReadOnly=true};
        var snapshot=new GameAnalysisSnapshot("Propio","zx48",0,"",[new(0x8000,3,0,1,0x9003,"INC")],profile);
        Assert.Throws<AiException>(()=>GameModificationAnalyzer.Parse(Answer(3),snapshot));
        Assert.That(profile.Variables["pace"].ReadOnly,Is.True);
    }

    [TestCase("uint8", "8000", false)] [TestCase("uint8", "8000", true)]
    [TestCase("uint16", "7FFF", false)] [TestCase("uint9", "8001", true)]
    public void ANewAliasCannotBypassTheRangeOrReadOnlyStatusOfKnownMemory(string encoding,string address,bool readOnly)
    {
        var profile=Profile();
        profile.Variables["known"]=new(){Address=address,Encoding=encoding,Min=0,Max=9,ReadOnly=readOnly,
            HighBitAddress="8000",HighBitMask=1};
        var snapshot=new GameAnalysisSnapshot("Propio","zx48",0,"",[new(0x8000,3,0,1,0x9003,"INC")],profile);
        Assert.Throws<AiException>(()=>GameModificationAnalyzer.Parse(Answer(3),snapshot));
    }

    [Test]
    public void AWordRequiresBothObservedBytesAndTheCorrectLittleEndianValue()
    {
        var first=new MemoryEvidence(0x8000,3,0,1,0x9003,"INC");
        var snapshot=new GameAnalysisSnapshot("Propio","zx48",0,"",[first,new(0x8001,1,0,1,0x9004,"")],Profile());
        string answer=Answer(259).Replace("\"bytes\":1","\"bytes\":2");
        var result=GameModificationAnalyzer.Parse(answer,snapshot);
        Assert.That(GameModificationAnalyzer.Definition(result.Variables[0]).Max,Is.EqualTo(65535));
        Assert.Throws<AiException>(()=>GameModificationAnalyzer.Parse(answer,snapshot with {Candidates=[first]}));
    }

    [Test]
    public void AQuestionCanReturnAnExplanationWithNoVariablesOrIdeas()
    {
        var result=GameModificationAnalyzer.Parse("""{"analysis":"Falta observar la acción indicada durante la partida.","variables":[],"ideas":[]}""",
            new("Propio","zx48",0,"",[],Profile()));
        Assert.That(result.Variables,Is.Empty);Assert.That(result.Advice.Ideas,Is.Empty);
    }

    [TestCase("zx48",0x4000,false)] [TestCase("zx48",0x8000,true)]
    [TestCase("c64",0xD020,false)] [TestCase("c64",0xC000,true)]
    [TestCase("nes",0x100,false)] [TestCase("nes",0x2000,false)] [TestCase("nes",0x200,true)]
    [TestCase("invaders",0x2400,false)] [TestCase("invaders",0x2000,true)]
    public void DiscoveryExcludesSystemRomHardwareAndStackRegions(string machine,int address,bool expected) =>
        Assert.That(ModificationObservation.IsWorkRam(machine,(uint)address),Is.EqualTo(expected));

    [Test]
    public void ACancelledRequestCannotProduceUsableChanges()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(()=>GameModificationAnalyzer.AnalyzeAsync(new SimulatedAi("Simulada",Answer(3)),
            new("Propio","zx48",0,"",[],Profile()),"",ct:cancellation.Token));
    }
}
