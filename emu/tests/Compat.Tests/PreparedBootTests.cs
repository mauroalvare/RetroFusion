using Retro.Compat;
using Retro.Core;
using Retro.Fusion;

namespace Compatibility.Tests;

[TestFixture, NonParallelizable]
public sealed class PreparedBootTests
{
    private string _folder = "", _firmware = "";
    [SetUp] public void Setup()
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-prepared-boot-" + Guid.NewGuid())).FullName;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "roms", "c64"))) { _firmware = Path.Combine(dir.FullName, "roms"); break; }
        Assert.That(_firmware, Is.Not.Empty);
    }
    [TearDown] public void Cleanup() => Directory.Delete(_folder, true);

    [TestCase(false)] [TestCase(true)]
    public async Task AMeasuredBootCanBeReplayedBeforeKeyTrialButOnlyExactEditionsReuseVariables(bool exact)
    {
        string path = Path.Combine(_folder, "own.prg"); byte[] bytes = C64TestProgram.Game(C64TestProgram.TypeG); File.WriteAllBytes(path, bytes);
        var previous = new GameProfile { Id = "known-proposal", Machine = "c64", Match = new() { Files = ["own.prg"] },
            Boot = ["wait 100", "press G 8", "wait 120"], Variables = { ["prior"] = new() { Address = "C003" } } };
        if (exact) previous.Match.Sha256.Add(GamePreparer.Sha256Of(new("own.prg", bytes, path)));
        var result = await GamePreparer.PrepareAsync(path, new() { PreviousProfile = previous, FirmwareDirectory = _firmware,
            Save = false, MaxRounds = 1, LabFrames = 60 }, null, CancellationToken.None);
        Assert.That(result.Ok, Is.True, result.Problem);
        Assert.That(result.Rounds, Is.Zero, "Do not rediscover an existing reproducible boot through key trials");
        Assert.That(result.Boot, Does.Contain("press G 8"));
        Assert.That(result.CheckedData, Has.Some.Contains("arranque del perfil"));
        Assert.That(result.Profile!.Variables.ContainsKey("prior"), Is.EqualTo(exact));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
    }
}
