using System.IO.Compression;
using Retro.Compat;
using Retro.Core;
using Retro.Fusion;

namespace Compatibility.Tests;

[TestFixture]
public sealed class ResearchIdentityTests
{
    private string _folder = "";
    [SetUp] public void Setup() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-research-identity-" + Guid.NewGuid())).FullName;
    [TearDown] public void Cleanup() => Directory.Delete(_folder, true);

    [Test]
    public void AnArchiveWithSeveralEditionsResearchesTheSelectedHashInsteadOfItsFirstDump()
    {
        byte[] first = PreparationNesFixture.Rom(), selected = first.ToArray();
        selected[^1] ^= 1;
        string path = Path.Combine(_folder, "own.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var (name, bytes) in new[] { ("first.nes", first), ("selected [a].nes", selected) })
            { using var stream = archive.CreateEntry(name).Open(); stream.Write(bytes); }
        var files = MediaArchive.Open(path);
        var profile = new GameProfile { Machine = "nes", Match = new() { Sha256 = [GamePreparer.Sha256Of(files[1])] } };

        Assert.That(GamePreparer.Choose(files).media!.Name, Is.EqualTo("first.nes"));
        var choice = GamePreparer.Choose(files, profile);
        Assert.That(choice.machine, Is.EqualTo("nes"));
        Assert.That(choice.media!.Source, Is.EqualTo(path + "!selected [a].nes"));
        Assert.That(choice.media.Data, Is.EqualTo(selected));
    }

    [Test]
    public async Task ACheckpointCannotResumeAnotherMemberOfTheSameUnchangedArchive()
    {
        string path = Path.Combine(_folder, "own.zip");
        byte[] first = PreparationNesFixture.Rom(), second = first.ToArray(); second[^1] ^= 1;
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var (name, bytes) in new[] { ("one.nes", first), ("two.nes", second) })
            { using var stream = archive.CreateEntry(name).Open(); stream.Write(bytes); }
        var files = MediaArchive.Open(path);
        GameProfile Profile(int member) => new() { Machine = "nes", Match = new() { Sha256 = [GamePreparer.Sha256Of(files[member])] } };
        int calls = 0;
        var engine = new AutonomousResearch { Prepare = (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(new PrepareResult { Ok = true, Lab = new() { Machine = "nes", Sha256 = GamePreparer.Sha256Of(files[0]),
                Findings = [new() { Address = "0010", Status = "verified" }] } });
        } };
        var options = new AutonomousResearchOptions(AiCalls: 0, MaxPasses: 1, LabFrames: 60);
        var saved = await engine.RunAsync(path, Profile(0), "", options);
        string before = System.Text.Json.JsonSerializer.Serialize(saved, GameProfile.Json);

        Assert.ThrowsAsync<InvalidDataException>(() => engine.RunAsync(path, Profile(1), "", options, previous: saved));
        Assert.That(calls, Is.EqualTo(1), "Do not run any experiments against another member");
        Assert.That(System.Text.Json.JsonSerializer.Serialize(saved, GameProfile.Json), Is.EqualTo(before));
        Assert.That(AutonomousResearch.MatchesEdition(saved, [GamePreparer.Sha256Of(files[1])]), Is.False);
        saved.EditionSha256 = ""; // Earlier checkpoints identify the member through their evidence.
        Assert.That(AutonomousResearch.MatchesEdition(saved, [GamePreparer.Sha256Of(files[1])]), Is.False);
        Assert.That(AutonomousResearch.MatchesEdition(saved, [GamePreparer.Sha256Of(files[0])]), Is.True);
    }

    [Test]
    public void SavingOrDiscardingCannotRaceAnotherJobHoldingTheFolderLease()
    {
        var store = new AutonomousResearchStore(_folder);
        var work = new AutonomousResearchWork { FileSha256 = new string('A', 64) };
        store.Save(work);
        string path = Path.Combine(_folder, work.Id + ".json");
        byte[] original = File.ReadAllBytes(path);
        using (var lease = new FileStream(Path.Combine(_folder, ".store.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            work.StopReason = "Concurrent replacement";
            Assert.Throws<IOException>(() => store.Save(work));
            Assert.Throws<IOException>(() => store.Remove(work.Id));
            var other = new AutonomousResearchWork { FileSha256 = work.FileSha256 };
            Assert.Throws<IOException>(() => store.Save(other), "The quota is shared by all records in the folder");
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
        }
        store.Save(work);
        Assert.That(store.Read().Single().StopReason, Is.EqualTo("Concurrent replacement"));
        store.Remove(work.Id);
        Assert.That(File.Exists(path), Is.False);
        Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
        Assert.That(new FileInfo(Path.Combine(_folder, ".store.lock")).Length, Is.Zero);
    }

    [Test]
    public void AResearchEngineKeyIncludesCpuAndHardwareModulesAlongsideTheResearcher()
    {
        string key = AutonomousResearch.EngineFingerprint;
        foreach (var type in new[] { typeof(AutonomousResearch), typeof(MachineFactory), typeof(Retro.Cpu.M6502.M6502), typeof(Retro.Cpu.Z80.Z80Cpu),
            typeof(Retro.Cpu.I8080.I8080Cpu), typeof(Retro.Cpu.I8088.I8088Cpu), typeof(Retro.Systems.Nes.Nes), typeof(Retro.Systems.C64.C64),
            typeof(Retro.Systems.Spectrum.Spectrum48), typeof(Retro.Systems.Arcade.SpaceInvaders), typeof(Retro.Systems.Pc.PcXt) })
            Assert.That(key, Does.Contain(type.Module.ModuleVersionId.ToString()), type.FullName);
    }
}
