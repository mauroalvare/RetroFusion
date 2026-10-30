using Retro.App;
using Retro.Compat;
using Retro.Fusion;
using Retro.Studio;

namespace App.Tests;

public sealed class FusionResearchStoreTests
{
    private string _folder = "";
    [SetUp] public void Setup() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "research-store-" + Guid.NewGuid())).FullName;
    [TearDown] public void Cleanup() => Directory.Delete(_folder, true);
    private static FusionResearchWork Work() => new()
    {
        Concept = new("Idea propia", "Base", "Un objetivo", "Flechas", "Reglas", new("b"), "Mantener la idea", "Comprobar el reloj"),
        Slots = [new() { Slot = "a", Profile = "first" }, new() { Slot = "b", Profile = "second" }],
        Originals = [new("a", "first.nes", new string('A',64), "first", "firmware"), new("b", "second.nes", new string('B',64), "second", "firmware")],
        Preparations = [new() { Ok = true, Machine = "nes", Sha256 = new string('A',64), Profile = new() { Id = "first", Machine = "nes" },
            ProfilePath = "untrusted.json", Continuation = new("nes",new string('A',64),[1,2,3],["wait 12"],42),
            CheckedData = ["Un dato comprobado"], Experiments = [new("signature","Acción","Resultado")] }]
    };

    [Test]
    public void AResearchCheckpointKeepsTheIdeaProgressAndPrivateMachineState()
    {
        var store = new FusionResearchStore(_folder); var work = Work(); store.Save(work);
        var saved = store.Pending().Single();
        Assert.That(saved.Concept, Is.EqualTo(work.Concept)); Assert.That(saved.Slots.Select(s => s.Profile), Is.EqualTo(new[] {"first","second"}));
        Assert.That(saved.Preparations[0].Continuation!.State, Is.EqualTo(new byte[] {1,2,3}));
        Assert.That(saved.Preparations[0].ProfilePath, Is.Null, "Un archivo portable no elige rutas de perfiles para leer al reanudar.");
        Assert.That(saved.Preparations[0].Experiments, Has.Count.EqualTo(1));
    }

    [Test]
    public void CorruptOrOversizedResearchIsReportedAndNeverDeleted()
    {
        var store = new FusionResearchStore(_folder); var work = Work(); store.Save(work);
        string path = Path.Combine(_folder,work.Id+".json"); File.WriteAllText(path,"incompleto");
        Assert.That(store.Pending(), Is.Empty); Assert.That(store.Problem, Is.Not.Empty); Assert.That(File.Exists(path), Is.True);
        File.WriteAllBytes(path,new byte[FusionResearchStore.MaxBytes+1]);
        Assert.That(store.Pending(), Is.Empty); Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public void InvalidIdentifiersAndLargeStatesCannotReplaceThePreviousCheckpoint()
    {
        var store = new FusionResearchStore(_folder); var work = Work(); store.Save(work);
        byte[] original = File.ReadAllBytes(Path.Combine(_folder,work.Id+".json"));
        var changed = Work(); changed.Id = "../escape";
        Assert.Throws<InvalidDataException>(()=>store.Save(changed));
        work.Preparations[0].Continuation = work.Preparations[0].Continuation! with { State = new byte[1024*1024+1] };
        Assert.Throws<InvalidDataException>(()=>store.Save(work));
        Assert.That(File.ReadAllBytes(Path.Combine(_folder,work.Id+".json")), Is.EqualTo(original));
    }

    [Test]
    public void ACheckpointUpdateAndDiscardTouchOnlyTheirOwnWork()
    {
        var store = new FusionResearchStore(_folder); var first = Work(); var second = Work(); store.Save(first); store.Save(second);
        first.StopReason="Necesita observación humana"; store.Save(first); store.Remove(first.Id);
        Assert.That(store.Pending().Single().Id, Is.EqualTo(second.Id));
    }
}
