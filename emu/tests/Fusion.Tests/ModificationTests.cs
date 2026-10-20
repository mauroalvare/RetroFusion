using Retro.Ai;
using Retro.Core;
using Retro.Fusion;
using Retro.Systems.Spectrum;

namespace Fusion.Tests;

public sealed class ModificationTests
{
    private static GameProfile Profile() => new()
    {
        Id = "own-test", Name = "Prueba propia", Machine = "zx48",
        Variables = new()
        {
            ["lives"] = new() { Address = "8000", Min = 0, Max = 9, Description = "Vidas de reserva." },
            ["air"] = new() { Address = "8001", Min = 36, Max = 63, Description = "Aire restante." },
            ["x"] = new() { Address = "8002", ReadOnly = true, Description = "Posición calculada por el juego." },
        }
    };

    [Test]
    public void ExperimentsRestoreWholeMachineAtFirstEditAndNeverChangeProfile()
    {
        var machine = new Spectrum48(new byte[0x4000]); var profile = Profile();
        machine.Memory.Poke(0x8000, 3); machine.Memory.Poke(0x8001, 60);
        byte[] before = machine.SaveState(); string profileBefore = profile.ToJson();
        var changes = new GameModifications(machine, profile);
        changes.Apply([new("lives", 8, true)]);
        machine.RunFrame(null);
        changes.Apply([new("air", 40, false)]);
        Assert.That(changes.CanRestore, Is.True); Assert.That(machine.FrameCount, Is.GreaterThan(0));
        Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(8));
        Assert.That(changes.Restore(), Is.True);
        Assert.That(machine.SaveState(), Is.EqualTo(before));
        Assert.That(changes.Frozen, Is.Empty); Assert.That(changes.CanRestore, Is.False);
        Assert.That(changes.Restore(), Is.False); Assert.That(profile.ToJson(), Is.EqualTo(profileBefore));
    }

    [Test]
    public void ReleasingFreezeKeepsCurrentPartidaAndRestorationRemainsAvailable()
    {
        var machine = new Spectrum48(new byte[0x4000]); var edits = new GameModifications(machine, Profile());
        machine.Memory.Poke(0x8000, 3);
        edits.Apply([new("lives", 9, true)]);
        machine.Memory.Poke(0x8000, 2); edits.Tick(); Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(9));
        edits.ReleaseAll(); machine.Memory.Poke(0x8000, 1); edits.Tick();
        Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(1)); Assert.That(edits.CanRestore, Is.True);
        edits.Restore(); Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(3));
    }

    [TestCase("unknown", 1)] [TestCase("x", 5)] [TestCase("lives", 10)] [TestCase("air", 0)]
    public void InvalidBatchCannotPartiallyApply(string variable, int value)
    {
        var machine = new Spectrum48(new byte[0x4000]); var edits = new GameModifications(machine, Profile());
        machine.Memory.Poke(0x8000, 3);
        Assert.Throws<InvalidOperationException>(() => edits.Apply([new("lives", 9, true), new(variable, value, false)]));
        Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(3)); Assert.That(edits.CanRestore, Is.False); Assert.That(edits.Frozen, Is.Empty);
    }

    [Test]
    public void ReadOnlyMemoryRollsBackEarlierWritesAndKeepsPreviousReturnPoint()
    {
        var machine = new Spectrum48(new byte[0x4000]); var profile = Profile();
        profile.Variables["rom"] = new() { Address = "0000" };
        var changes = new GameModifications(machine, profile);
        machine.Memory.Poke(0x8000, 3); changes.Apply([new("lives", 8, true)]);
        Assert.Throws<InvalidOperationException>(() => changes.Apply([new("lives", 4, false), new("rom", 99, false)]));
        Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(8)); Assert.That(changes.Frozen["lives"], Is.EqualTo(8));
        changes.Restore(); Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(3));
    }

    [Test]
    public void MaskPreservesUnrelatedBitsAndInvalidRepresentationIsRejected()
    {
        var machine = new Spectrum48(new byte[0x4000]); var profile = Profile();
        profile.Variables["lives"].Mask = 15;
        var changes = new GameModifications(machine, profile);
        machine.Memory.Poke(0x8000, 0xA3); changes.Apply([new("lives", 9, true)]);
        Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(0xA9));
        machine.Memory.Poke(0x8000, 0xC2); changes.Tick(); Assert.That(machine.Memory.Peek(0x8000), Is.EqualTo(0xC9));
        profile.Variables["lives"].Mask = 5;
        Assert.Throws<InvalidOperationException>(() => changes.Apply([new("lives", 2, false)]));
    }

    [TestCase("uint16", "FFFF", 2)] [TestCase("bogus", "8000", 1)] [TestCase("ascii", "FFF8", 9)]
    public void InvalidMemoryOrEncodingIsRejectedBeforeWriting(string encoding, string address, int size)
    {
        var profile = Profile(); profile.Variables["lives"].Encoding = encoding;
        profile.Variables["lives"].Address = address; profile.Variables["lives"].Bytes = size;
        var changes = new GameModifications(new Spectrum48(new byte[0x4000]), profile);
        Assert.Throws<InvalidOperationException>(() => changes.Apply([new("lives", 3, false)]));
    }

    [Test]
    public void HashSpecificProfileCannotMatchAnotherEditionWithTheSameName()
    {
        var first = new MediaFile("sample.z80", [1, 2, 3], "sample.z80");
        var second = new MediaFile("sample.z80", [4, 5, 6], "sample.z80");
        var profile = Profile(); profile.Match.NamePattern = "sample"; profile.Match.Sha256 = [ProfileMatch.HashOf(first)];
        Assert.That(profile.Matches([first]), Is.True); Assert.That(profile.Matches([second]), Is.False);
    }

    private sealed class FakeAi(string answer) : IAiProvider
    {
        public string Name => "IA simulada";
        public AiRequest? Request;
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct) { Request = request; ct.ThrowIfCancellationRequested(); return Task.FromResult(answer); }
    }
    private const string GoodAnswer = """
        {"analysis":"Se pueden combinar reservas y aire; no hay una variable verificada de invulnerabilidad.","ideas":[
        {"title":"Rescate con poco aire","effect":"Muchas reservas, pero aire al límite. Es una hipótesis para probar.",
        "changes":[{"variable":"lives","value":8,"freeze":false},{"variable":"air","value":38,"freeze":true}]}]}
        """;

    [Test]
    public async Task SimulatedAiReceivesKnownValuesAndDoesNotApplyItsIdeas()
    {
        var machine = new Spectrum48(new byte[0x4000]); var profile = Profile();
        machine.Memory.Poke(0x8000, 3); machine.Memory.Poke(0x8001, 60);
        byte[] before = machine.SaveState(); var ai = new FakeAi(GoodAnswer);
        var advice = await GameModificationAdvisor.ProposeAsync(ai, GameModificationAdvisor.Prompt(profile, machine.Memory, "un desafío extraño"), profile, CancellationToken.None);
        Assert.That(advice.Ideas, Has.Count.EqualTo(1));
        Assert.That(ai.Request!.Turns[0].Text, Does.Contain("valor=3").And.Contain("rango=36..63").And.Contain("SOLO LECTURA").And.Contain("un desafío extraño"));
        Assert.That(ai.Request.System, Does.Contain("No inventes").And.Contain("no modifica nada"));
        Assert.That(ai.Request.Schema.HasValue, Is.True); Assert.That(machine.SaveState(), Is.EqualTo(before));
        new GameModifications(machine, profile).Apply(advice.Ideas[0].Changes);
        Assert.That(machine.Memory.Peek(0x8001), Is.EqualTo(38));
    }

    [TestCase("\"variable\":\"air\"", "\"variable\":\"unknown\"")]
    [TestCase("\"variable\":\"air\"", "\"variable\":\"x\"")]
    [TestCase("\"value\":38", "\"value\":255")]
    [TestCase("\"value\":38", "\"value\":\"38\"")]
    [TestCase("\"freeze\":true", "\"freeze\":true,\"address\":\"8001\"")]
    [TestCase("\"value\":38", "\"value\":38,\"value\":39")]
    public void SimulatedAiCannotInventVariablesAddressesOrBypassRanges(string original, string replacement) =>
        Assert.Throws<AiException>(() => GameModificationAdvisor.Parse(GoodAnswer.Replace(original, replacement), Profile()));

    [Test]
    public async Task CancellationDoesNotReturnUsableAdvice()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await GameModificationAdvisor.ProposeAsync(new FakeAi(GoodAnswer), "context", Profile(), cancel.Token));
        await Task.CompletedTask;
    }
}
