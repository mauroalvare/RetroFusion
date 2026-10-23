using System.Reflection;
using System.Text.Json;
using Retro.Ai;
using Retro.App;
using Retro.Core;
using Retro.Fusion;
using Retro.Machines;

namespace App.Tests;

[TestFixture, NonParallelizable]
public class ModificationHistoryTests
{
    private string _directory = "";
    private string? _oldProfiles;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object? Get(CodeWindow window, string name) => typeof(CodeWindow).GetField(name, Private)!.GetValue(window);
    private static object? Call(CodeWindow window, string name, params object[] arguments) => typeof(CodeWindow).GetMethod(name, Private)!.Invoke(window, arguments);
    private ModificationHistory Store() => new(Path.Combine(_directory, "history"));
    private string[] SavedFiles() => Directory.Exists(Path.Combine(_directory, "history")) ? Directory.GetFiles(Path.Combine(_directory, "history"), "*.json") : [];
    private sealed class SimulatedAi : IAiProvider
    {
        public string Name => "Proveedor simulado";
        public Task<string> CompleteAsync(AiRequest request, CancellationToken ct) => Task.FromResult("""
            {"analysis":"Se observó un contador; su efecto debe comprobarse.","variables":[
            {"name":"counter","label":"Contador candidato","address":"8000","bytes":1,"observed":1,"reason":"INC (HL) escribe 8000 desde 9003."}],
            "ideas":[{"title":"Fijar el contador","effect":"Comprobar el resultado en la partida.","changes":[{"variable":"counter","value":17,"freeze":true}]}]}
            """);
    }

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "retro-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory); File.WriteAllBytes(Path.Combine(_directory, "48.rom"), new byte[0x4000]);
        _oldProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_directory, "profiles"));
    }
    [TearDown]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _oldProfiles);
        Directory.Delete(_directory, true);
    }
    private (EmulatorHost Host, CodeWindow Window) Start(string name = "own.tap", byte flag = 0)
    {
        var host = new EmulatorHost(); host.Start(MachineCatalog.Get("zx48"), _directory);
        host.Load(new MediaFile(name, [3, 0, 255, flag, (byte)(255 ^ flag)], name), false);
        host.LoadBinary([0x21, 0, 0x80, 0x34, 0xC3, 3, 0x90], 0x9000, true);
        host.Machine!.Memory.Poke(0x8000, 0);
        var window = new CodeWindow(host, new AiSettings(), _ => { }, history: Store());
        window.OpenModifications(); window.Update();
        for (int step = 0; step < 2; step++) host.Machine!.StepInstruction();
        return (host, window);
    }
    private static async Task Analyze(CodeWindow window)
    {
        Call(window, "BeginModificationAnalysis", new SimulatedAi());
        await ((Task)Get(window, "_modificationTask")!).WaitAsync(TimeSpan.FromSeconds(5)); window.Update();
    }
    private static void Apply(CodeWindow window, int value = 17, bool freeze = true) =>
        Call(window, "ApplyModifications", (object)new VariableEdit[] { new("counter", value, freeze) });

    [Test]
    public async Task PersistenceIsOptInAndNeverPromotesAiHypothesesToPreparedProfiles()
    {
        var (host, window) = Start(); using var owned = window;
        await Analyze(window); Apply(window);
        Assert.That(SavedFiles(), Is.Empty);
        Call(window, "SetRememberModifications", true);
        Assert.That(SavedFiles(), Has.Length.EqualTo(1));
        Assert.That(host.LoadedProfile, Is.Null);
        Assert.That(ProfileLibrary.All().Any(p => p.Variables.ContainsKey("counter")), Is.False);
        Assert.That(Get(window, "_historyProblem"), Is.EqualTo(""));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ResetKeepsAnalysisAndReappliesFrozenSelectionOnlyWhenChosen(bool hard)
    {
        var (host, window) = Start(); using var owned = window;
        await Analyze(window); Apply(window);
        Call(window, "SetRememberModifications", true); Call(window, "SetReapplyModifications", true);
        host.Reset(hard); window.Update();
        Assert.That(Get(window, "_lastAnalysis")!.ToString(), Does.Contain("contador"));
        Assert.That(Get(window, "_lastAnalysisProvider"), Is.EqualTo("Proveedor simulado"));
        Assert.That(((ModificationAdvice)Get(window, "_advice")!).Ideas, Has.Count.EqualTo(1));
        Assert.That(host.Machine!.Memory.Peek(0x8000), Is.EqualTo(17));
        host.LoadBinary([0x21, 0, 0x80, 0x34, 0xC3, 3, 0x90], 0x9000, true);
        for (int i = 0; i < 20; i++) host.Machine.StepInstruction();
        Assert.That(host.Machine.Memory.Peek(0x8000), Is.EqualTo(17));
        Call(window, "RestoreModifications", true);
        Assert.That(((GameModifications)Get(window, "_modifications")!).Frozen, Is.Empty);
    }

    [Test]
    public async Task ANewWindowWithRenamedMediaRecoversDefinitionsReportsAndOneShotValues()
    {
        var (host, window) = Start();
        using (window)
        {
            await Analyze(window); Apply(window, 41, false);
            Call(window, "SetRememberModifications", true); Call(window, "SetReapplyModifications", true);
        }
        var (nextHost, nextWindow) = Start("renamed.tap"); using var owned = nextWindow;
        Assert.That(Get(nextWindow, "_lastAnalysisProvider"), Is.EqualTo("Proveedor simulado"));
        Assert.That(((GameProfile)Get(nextWindow, "_profile")!).Variables.ContainsKey("counter"), Is.True);
        Assert.That(nextHost.Machine!.Memory.Peek(0x8000), Is.EqualTo(42), "The program can advance a restored one-shot value.");
        Assert.That(((GameModifications)Get(nextWindow, "_modifications")!).Frozen, Is.Empty);
        Assert.That(Get(nextWindow, "_savedReport"), Is.True);
    }

    [Test]
    public async Task RememberedAnalysisDoesNotWriteRamWhenReapplicationIsDisabled()
    {
        var (_, window) = Start();
        using (window) { await Analyze(window); Apply(window); Call(window, "SetRememberModifications", true); }
        var (host, next) = Start(); using var owned = next;
        Assert.That(host.Machine!.Memory.Peek(0x8000), Is.EqualTo(1));
        Assert.That(Get(next, "_lastAnalysis")!.ToString(), Is.Not.Empty);
        Call(next, "SetReapplyModifications", true);
        host.Reset(true); next.Update();
        Assert.That(host.Machine.Memory.Peek(0x8000), Is.EqualTo(17));
        Call(next, "SetReapplyModifications", false);
        host.LoadBinary([0x21, 0, 0x80, 0x34], 0x9000, true);
        host.Machine.StepInstruction(); host.Machine.StepInstruction();
        Assert.That(host.Machine.Memory.Peek(0x8000), Is.EqualTo(18));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task DifferentMediaOrFirmwareNeverReceivesStoredAddresses(bool changeFirmware)
    {
        var (_, window) = Start();
        using (window)
        {
            await Analyze(window); Apply(window);
            Call(window, "SetRememberModifications", true); Call(window, "SetReapplyModifications", true);
        }
        if (changeFirmware) File.WriteAllBytes(Path.Combine(_directory, "48.rom"), Enumerable.Repeat((byte)0xC9, 0x4000).ToArray());
        var (host, next) = Start(flag: changeFirmware ? (byte)0 : (byte)1); using var owned = next;
        Assert.That(Get(next, "_lastAnalysis"), Is.EqualTo(""));
        Assert.That(Get(next, "_profile"), Is.Null);
        Assert.That(host.Machine!.Memory.Peek(0x8000), Is.EqualTo(1));
    }

    [Test]
    public async Task TurningOffPersistenceDeletesOnlyThisGamesFileAndKeepsCurrentPlay()
    {
        var (host, window) = Start(); using var owned = window;
        await Analyze(window); Apply(window); Call(window, "SetRememberModifications", true);
        byte[] state = host.Machine!.SaveState();
        Call(window, "SetRememberModifications", false);
        Assert.That(SavedFiles(), Is.Empty);
        Assert.That(host.Machine.SaveState(), Is.EqualTo(state));
        Assert.That(Get(window, "_lastAnalysis")!.ToString(), Is.Not.Empty);
        host.Reset(true); window.Update();
        Assert.That(Get(window, "_lastAnalysis"), Is.EqualTo(""));
    }

    [Test]
    public async Task MalformedOrFutureHistoryIsNotAppliedOrOverwritten()
    {
        var (_, window) = Start();
        using (window) { await Analyze(window); Call(window, "SetRememberModifications", true); }
        string file = SavedFiles().Single();
        var original = JsonSerializer.Deserialize<SavedModifications>(File.ReadAllText(file))!;
        foreach (string bad in new[] { "{", JsonSerializer.Serialize(original with { Version = 100 }),
            JsonSerializer.Serialize(original with { Variables = null! }), JsonSerializer.Serialize(original with { Reports = [null!] }) })
        {
            File.WriteAllText(file, bad);
            var (host, next) = Start(); using (next)
            {
                Assert.That(Get(next, "_historyProblem")!.ToString(), Is.Not.Empty);
                Assert.That(host.Machine!.Memory.Peek(0x8000), Is.EqualTo(1));
                Call(next, "SetRememberModifications", true);
            }
            Assert.That(File.ReadAllText(file), Is.EqualTo(bad));
        }
    }

    [Test]
    public async Task ChangedPreparedDefinitionsRejectHistoryWithoutPartialImport()
    {
        var (host, window) = Start();
        using (window) { await Analyze(window); Apply(window); Call(window, "SetRememberModifications", true); }
        var media = new MediaFile("own.tap", [3, 0, 255, 0, 255], "own.tap");
        ProfileLibrary.Save(new GameProfile { Id = "updated", Machine = "zx48", Match = new() { Sha256 = [ProfileMatch.HashOf(media)] },
            Variables = new() { ["counter"] = new() { Address = "8010" } } });
        string file = SavedFiles().Single(); byte[] before = File.ReadAllBytes(file);
        var (_, next) = Start(); using (next)
        {
            Assert.That(Get(next, "_historyProblem")!.ToString(), Does.Contain("no coincide"));
            Assert.That(((GameProfile)Get(next, "_profile")!).Variables["counter"].Address, Is.EqualTo("8010"));
            Assert.That(((GameModifications)Get(next, "_modifications")!).Frozen, Is.Empty);
        }
        Assert.That(File.ReadAllBytes(file), Is.EqualTo(before));
    }

    [Test]
    public void ASelectionLargerThanAProposalIsOneUndoAndRollsBackAllBatchesOnWriteFailure()
    {
        var (host, window) = Start(); using var owned = window;
        var profile = new GameProfile { Machine = "zx48" };
        for (int i = 0; i < 10; i++) profile.Variables["v" + i] = new() { Address = (0x8100 + i).ToString("X4") };
        profile.Variables["rom"] = new() { Address = "0000" };
        using var edits = new GameModifications(host.Machine!, profile);
        edits.Apply([new("v0", 9, true)]);
        byte[] before = host.Machine!.SaveState();
        var selected = Enumerable.Range(0, 10).Select(i => new VariableEdit("v" + i, 40 + i, i % 2 == 0)).ToArray();
        Assert.Throws<InvalidOperationException>(() => edits.ApplySelection([.. selected, new("rom", 100, false)]));
        Assert.That(host.Machine.SaveState(), Is.EqualTo(before));
        Assert.That(edits.Frozen["v0"], Is.EqualTo(9));
        Assert.That(edits.Selection, Has.Count.EqualTo(1));
        edits.ApplySelection(selected);
        Assert.That(edits.Selection, Has.Count.EqualTo(10));
        Assert.That(edits.Frozen, Has.Count.EqualTo(5));
        Assert.That(edits.Undo(), Is.True);
        Assert.That(host.Machine.SaveState(), Is.EqualTo(before));
        Assert.That(edits.Selection.Single(), Is.EqualTo(new VariableEdit("v0", 9, true)));
    }

    [Test]
    public void IdentityDoesNotDependOnNamesOrHashOrderButSeparatesSystemsAndFirmware()
    {
        string a = new('A', 64), b = new('B', 64);
        Assert.That(ModificationHistory.Key("zx48", [a, b], "firmware"), Is.EqualTo(ModificationHistory.Key("zx48", [b.ToLowerInvariant(), a], "firmware")));
        Assert.That(ModificationHistory.Key("zx48", [a], "one"), Is.Not.EqualTo(ModificationHistory.Key("zx48", [a], "two")));
        Assert.That(ModificationHistory.Key("zx48", [a], ""), Is.Not.EqualTo(ModificationHistory.Key("c64", [a], "")));
        Assert.That(ModificationHistory.Key("zx48", [], ""), Is.Null);
    }
    [Test]
    public async Task SelectingOldReportsDoesNotWriteMemoryAndHistoryIsBounded()
    {
        var (host, window) = Start(); using var owned = window;
        await Analyze(window); Call(window, "SetRememberModifications", true);
        for (int i = 0; i < 15; i++)
        {
            typeof(CodeWindow).GetField("_lastAnalysis", Private)!.SetValue(window, "Informe " + i);
            Call(window, "RecordAnalysis");
        }
        var reports = (List<SavedModificationReport>)Get(window, "_reports")!;
        Assert.That(reports, Has.Count.EqualTo(12));
        byte[] before = host.Machine!.SaveState();
        Call(window, "SelectSavedReport", 0);
        Assert.That(Get(window, "_lastAnalysis"), Is.EqualTo("Informe 3"));
        Assert.That(host.Machine.SaveState(), Is.EqualTo(before));
        Assert.That(Store().Read((string)Get(window, "_historyKey")!, "zx48", 16)!.Reports, Has.Count.EqualTo(12));
    }

    [Test]
    public void ANewOverlappingOneShotEditReplacesTheRememberedTargetAndUndoRecoversIt()
    {
        var (host, window) = Start(); using var owned = window;
        var profile = new GameProfile { Machine = "zx48", Variables = new()
        {
            ["word"] = new() { Address = "8000", Encoding = "uint16", Max = 65535 },
            ["byte"] = new() { Address = "8001" }
        } };
        using var edits = new GameModifications(host.Machine!, profile);
        edits.Apply([new("word", 1234, false)]);
        edits.Apply([new("byte", 20, false)]);
        Assert.That(edits.Selection.Single().Variable, Is.EqualTo("byte"));
        edits.Undo();
        Assert.That(edits.Selection.Single().Variable, Is.EqualTo("word"));
    }

    [Test]
    public async Task SavedSelectionRejectsMirrorsInvalidAddressesAndMismatchedIdentity()
    {
        var (_, window) = Start();
        using (window) { await Analyze(window); Apply(window); Call(window, "SetRememberModifications", true); }
        string file = SavedFiles().Single();
        var saved = JsonSerializer.Deserialize<SavedModifications>(File.ReadAllText(file))!;
        var invalidAddress = saved with { Variables = new() { ["counter"] = new() { Address = "10000" } } };
        foreach (var bad in new[] { invalidAddress, saved with { Key = new string('F', 64) },
            saved with { Changes = [new("counter", 1, true), new("counter", 2, true)] } })
        {
            File.WriteAllText(file, JsonSerializer.Serialize(bad));
            var reader = Store();
            Assert.That(reader.Read(saved.Key, "zx48", 16), Is.Null);
            Assert.That(reader.Problem, Is.Not.Empty);
            Assert.That(reader.Write(saved, 16), Is.False);
        }
        var mirrored = new SavedModifications(1, saved.Key, "nes", new()
        {
            ["first"] = new() { Address = "0200" }, ["mirror"] = new() { Address = "0A00" }
        }, new(), [], [new("first", 1, true), new("mirror", 2, true)], true);
        File.WriteAllText(file, JsonSerializer.Serialize(mirrored));
        Assert.That(Store().Read(saved.Key, "nes", 16), Is.Null);
    }

}