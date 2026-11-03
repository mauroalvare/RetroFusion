using System.Reflection;
using System.Text.Json;
using Retro.Ai;
using Retro.App;
using Retro.Compat;
using Retro.Core;
using Retro.Fusion;
using Retro.Machines;

namespace App.Tests;

[NonParallelizable]
public sealed class AutonomousAnalysisUiTests
{
    private string _directory = "";
    private string? _previousProfiles;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Get(CodeWindow window, string name) => typeof(CodeWindow).GetField(name, Private)!.GetValue(window);
    private static void Set(CodeWindow window, string name, object value) => typeof(CodeWindow).GetField(name, Private)!.SetValue(window, value);
    private static void Call(CodeWindow window, string name, params object[] arguments) => typeof(CodeWindow).GetMethod(name, Private)!.Invoke(window, arguments);
    private AutonomousResearchStore Store => new(Path.Combine(_directory, ".retroemu", "analisis"));

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "retro-autonomous-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previousProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_directory, "profiles"));
    }

    [TearDown]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _previousProfiles);
        Assert.That(Path.GetDirectoryName(Path.GetFullPath(_directory)), Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
        Directory.Delete(_directory, recursive: true);
    }

    private string Start(EmulatorHost host, byte edition = 1)
    {
        byte[] rom = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(rom); rom[4] = 1;
        Array.Fill(rom, (byte)0xEA, 16, 16384); rom[20] = edition;
        rom[^4] = 0; rom[^3] = 0x80;
        string path = Path.Combine(_directory, "own-" + edition + ".nes");
        File.WriteAllBytes(path, rom);
        host.StartNes(MachineCatalog.Get("nes"), new MediaFile(Path.GetFileName(path), rom, path));
        return path;
    }

    private CodeWindow Window(EmulatorHost host)
    {
        var window = new CodeWindow(host, new AiSettings { Provider = "manual" }, _ => { }, firmwareDirectory: _directory, annotationsDirectory: Path.Combine(_directory, "notes"));
        window.Update(); Call(window, "LoadAutonomousContext");
        Set(window, "_autonomousCalls", 0); Set(window, "_autonomousPasses", 1); Set(window, "_autonomousFrames", 60);
        Set(window, "_autonomousGoal", "Contrastar el reloj sin inferir su propósito por el nombre.");
        return window;
    }

    private static async Task Pause(CodeWindow window)
    {
        await window.CancelWork().WaitAsync(TimeSpan.FromSeconds(10));
        window.Update();
    }

    [Test]
    public async Task ImmediatePauseCheckpointsTheGoalBeforeResearchAndKeepsThePlayerMachineUntouched()
    {
        var host = new EmulatorHost(); Start(host);
        using var window = Window(host);
        byte[] before = host.Machine!.SaveState();
        try
        {
            Call(window, "BeginAutonomousAnalysis", false);
            Assert.That(window.IsWorking, Is.True);
            await Pause(window);
            var work = Store.Read().Single();
            Assert.That(work.Status, Is.EqualTo("paused"));
            Assert.That(work.Options.Goal, Is.EqualTo("Contrastar el reloj sin inferir su propósito por el nombre."));
            Assert.That(work.AiCallsUsed, Is.Zero);
            Assert.That(host.Machine.SaveState(), Is.EqualTo(before));
            Assert.That(Get(window, "_autonomousWork"), Is.TypeOf<AutonomousResearchWork>());
            Assert.That(window.IsWorking, Is.False);
        }
        finally { await Pause(window); }
    }

    [Test]
    public async Task ChangingGameCancelsTheOldRunAndDoesNotPublishItsResultsIntoTheNewSession()
    {
        var host = new EmulatorHost(); string old = Start(host);
        using var window = Window(host);
        try
        {
            Call(window, "BeginAutonomousAnalysis", false);
            Start(host, 2); window.Update();
            byte[] newState = host.Machine!.SaveState();
            await Pause(window);
            Assert.That(Get(window, "_autonomousTask"), Is.Null);
            Assert.That(Get(window, "_autonomousWork"), Is.Null);
            Assert.That(Get(window, "_autonomousProgress"), Is.Null);
            Assert.That(host.Machine.SaveState(), Is.EqualTo(newState));
            var saved = Store.Read().Single();
            Assert.That(saved.FileSha256, Is.EqualTo(AutonomousResearch.FileHash(old)));
            Assert.That(saved.Status, Is.EqualTo("paused"));
        }
        finally { await Pause(window); }
    }

    [Test]
    public async Task OpeningAPendingAnalysisRetainsItsBudgetAndDoesNotStartResearchOrRewriteIt()
    {
        var host = new EmulatorHost(); Start(host);
        using (var first = Window(host))
        {
            try
            {
                Set(first, "_autonomousMinutes", 7); Set(first, "_autonomousTarget", 16);
                Call(first, "BeginAutonomousAnalysis", false); await Pause(first);
            }
            finally { await Pause(first); }
        }
        string checkpoint = Directory.GetFiles(Store.Folder, "*.json").Single(); byte[] original = File.ReadAllBytes(checkpoint);
        using var reopened = Window(host);
        Call(reopened, "LoadAutonomousContext");
        Assert.That(reopened.IsWorking, Is.False);
        Assert.That(Get(reopened, "_autonomousTask"), Is.Null);
        Assert.That(Get(reopened, "_autonomousAi"), Is.Null);
        Assert.That(Get(reopened, "_autonomousMinutes"), Is.EqualTo(7));
        Assert.That(Get(reopened, "_autonomousTarget"), Is.EqualTo(16));
        Assert.That(File.ReadAllBytes(checkpoint), Is.EqualTo(original));
    }

    [Test]
    public void OpeningEvidenceFromAnEarlierEngineShowsItAsHistoricalWithoutChangingTheSavedRecordOrCallingAi()
    {
        var host = new EmulatorHost(); string source = Start(host);
        using var window = Window(host);
        var work = new AutonomousResearchWork { FileSha256 = AutonomousResearch.FileHash(source), EditionSha256 = AutonomousResearch.FileHash(source),
            Engine = "previous-cpu-and-hardware", Evidence = new() { Findings = [new() { Address = "0010", Status = "verified",
                Verification = new() { Result = "verified", BeforeHex = "2A", TestHex = "2B" } }] } };
        Store.Save(work);
        string path = Path.Combine(Store.Folder, work.Id + ".json"); byte[] saved = File.ReadAllBytes(path);

        Call(window, "SelectAutonomousWork", work);

        var shown = (AutonomousResearchWork)Get(window, "_autonomousWork")!;
        Assert.That(shown.VerifiedWrites, Is.Zero);
        Assert.That(shown.Evidence.Findings.Single().Status, Is.EqualTo("inconclusive"));
        Assert.That(shown.Evidence.Findings.Single().Verification!.BeforeHex, Is.EqualTo("2A"));
        Assert.That(Get(window, "_autonomousMessage")!.ToString(), Does.Contain("histórica"));
        Assert.That(window.IsWorking, Is.False);
        Assert.That(Get(window, "_autonomousAi"), Is.Null);
        Assert.That(work.VerifiedWrites, Is.EqualTo(1), "The selected object is not mutated by the view");
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
    }

    [Test]
    public async Task AnUnreadableCheckpointDoesNotHideTheValidPendingAnalysisOrDeleteEitherFile()
    {
        var host = new EmulatorHost(); Start(host);
        using (var first = Window(host))
        {
            try { Call(first, "BeginAutonomousAnalysis", false); await Pause(first); }
            finally { await Pause(first); }
        }
        string broken = Path.Combine(Store.Folder, new string('f', 32) + ".json"); File.WriteAllText(broken, "{}");
        using var reopened = Window(host);
        Assert.That(Get(reopened, "_autonomousWork"), Is.TypeOf<AutonomousResearchWork>());
        Assert.That(Get(reopened, "_autonomousStoreProblem"), Is.Not.EqualTo(""));
        Assert.That(File.ReadAllText(broken), Is.EqualTo("{}"));
        Assert.That(Directory.GetFiles(Store.Folder, "*.json"), Has.Length.EqualTo(2));
        Assert.That(reopened.IsWorking, Is.False);
    }

    [Test]
    public async Task ContinuingKeepsTheSameWorkAndUsesTheExplicitAdditionalBudget()
    {
        var host = new EmulatorHost(); Start(host);
        using var window = Window(host);
        try
        {
            Call(window, "BeginAutonomousAnalysis", false); await Pause(window);
            var first = Store.Read().Single();
            Set(window, "_autonomousMinutes", 9); Set(window, "_autonomousTarget", 24);
            Assert.That(window.IsWorking, Is.False);
            Assert.That(Store.Read().Single().Options.Minutes, Is.EqualTo(first.Options.Minutes));
            Call(window, "BeginAutonomousAnalysis", true); await Pause(window);
            var continued = Store.Read().Single();
            Assert.That(continued.Id, Is.EqualTo(first.Id));
            Assert.That(continued.Options.Minutes, Is.EqualTo(9));
            Assert.That(continued.Options.TargetVariables, Is.EqualTo(24));
            Assert.That(continued.ElapsedSeconds, Is.GreaterThanOrEqualTo(first.ElapsedSeconds));
            Assert.That(continued.AiCallsUsed, Is.Zero);
            Assert.That(continued.Status, Is.EqualTo("paused"));
        }
        finally { await Pause(window); }
    }

    [Test]
    public async Task DiscardingOneCheckpointPreservesOtherResearchTheRomAndProfilesAndClearsItsSelection()
    {
        var host = new EmulatorHost(); string rom = Start(host);
        using var window = Window(host);
        try
        {
            Call(window, "BeginAutonomousAnalysis", false); await Pause(window);
            var current = Store.Read().Single();
            var other = JsonSerializer.Deserialize<AutonomousResearchWork>(JsonSerializer.Serialize(current, GameProfile.Json), GameProfile.Json)!;
            other.Id = Guid.NewGuid().ToString("N"); other.Game = "Another research"; other.FileSha256 = new string('a', 64);
            Store.Save(other);
            string otherPath = Path.Combine(Store.Folder, other.Id + ".json"); byte[] otherBefore = File.ReadAllBytes(otherPath);
            string note = Path.Combine(Store.Folder, "keep.txt"); File.WriteAllText(note, "Keep this unrelated file.");
            Directory.CreateDirectory(Path.Combine(_directory, "profiles"));
            string profile = Path.Combine(_directory, "profiles", "keep.json"); File.WriteAllText(profile, "{\"keep\":true}");
            byte[] romBefore = File.ReadAllBytes(rom); string profileBefore = File.ReadAllText(profile);
            byte[] playerBefore = host.Machine!.SaveState();
            Call(window, "RefreshAutonomousHistory");
            Call(window, "DiscardAutonomousWork", current.Id);
            Assert.That(File.Exists(Path.Combine(Store.Folder, current.Id + ".json")), Is.False);
            Assert.That(Store.Read().Select(work => work.Id), Is.EqualTo(new[] { other.Id }));
            Assert.That(File.ReadAllBytes(otherPath), Is.EqualTo(otherBefore));
            Assert.That(File.ReadAllText(note), Is.EqualTo("Keep this unrelated file."));
            Assert.That(File.ReadAllBytes(rom), Is.EqualTo(romBefore));
            Assert.That(File.ReadAllText(profile), Is.EqualTo(profileBefore));
            Assert.That(host.Machine.SaveState(), Is.EqualTo(playerBefore));
            Assert.That(Get(window, "_autonomousWork"), Is.Null);
            Assert.That(Get(window, "_autonomousProgress"), Is.Null);
        }
        finally { await Pause(window); }
    }

    [Test]
    public async Task AnActiveResearchCannotBeDeletedWhileItMayStillSaveTheSameCheckpoint()
    {
        var host = new EmulatorHost(); Start(host);
        using var window = Window(host);
        try
        {
            Call(window, "BeginAutonomousAnalysis", false); await Pause(window);
            string id = Store.Read().Single().Id;
            Call(window, "BeginAutonomousAnalysis", true);
            Call(window, "DiscardAutonomousWork", id);
            Assert.That(File.Exists(Path.Combine(Store.Folder, id + ".json")), Is.True);
            await Pause(window);
            Assert.That(Store.Read().Single().Id, Is.EqualTo(id));
            Call(window, "DiscardAutonomousWork", id);
            Assert.That(Store.Read(), Is.Empty);
        }
        finally { await Pause(window); }
    }

    [Test]
    public async Task RetiredWorkersAlsoBlockDeletionUntilTheirFinalSaveHasCompleted()
    {
        var host = new EmulatorHost(); Start(host);
        using var window = Window(host);
        var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Call(window, "BeginAutonomousAnalysis", false); await Pause(window);
            string id = Store.Read().Single().Id;
            ((List<Task>)Get(window, "_retiredWork")!).Add(retired.Task);
            Call(window, "DiscardAutonomousWork", id);
            Assert.That(File.Exists(Path.Combine(Store.Folder, id + ".json")), Is.True);
            retired.SetResult(); window.Update();
            Call(window, "DiscardAutonomousWork", id);
            Assert.That(Store.Read(), Is.Empty);
        }
        finally { retired.TrySetResult(); await Pause(window); }
    }

    [Test]
    public async Task AnUnlistedIdCannotDeleteAnArbitraryFile()
    {
        var host = new EmulatorHost(); string rom = Start(host); byte[] before = File.ReadAllBytes(rom);
        using var window = Window(host);
        try
        {
            Call(window, "BeginAutonomousAnalysis", false); await Pause(window);
            Call(window, "DiscardAutonomousWork", "../../own-1.nes");
            Assert.That(File.ReadAllBytes(rom), Is.EqualTo(before));
            Assert.That(Store.Read(), Has.Count.EqualTo(1));
        }
        finally { await Pause(window); }
    }
}
