using Retro.Update;

namespace Update.Tests;

public sealed class StartupReceiptTests
{
    private string _folder = "";
    [SetUp] public void SetUp() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-startup-receipt-" + Guid.NewGuid().ToString("N"))).FullName;
    [TearDown] public void TearDown() => Directory.Delete(_folder, true);

    private UpdatePlan Plan()
    {
        string installed = Path.Combine(_folder, "Programa"); PackageTests.OwnPackage(installed, "0.3.5");
        var asset = new ReleaseAsset(new(0, 3, 6), "win-x64", "own.zip", new("https://github.com/own.zip"), 1, new('0', 64));
        var plan = UpdatePlan.Create(installed, asset); plan.State = "applying"; plan.Save(); return plan;
    }

    [Test]
    public void AValidReceiptReturnsTheNewVersionOnlyAfterPublishingReady()
    {
        var plan = Plan();
        Assert.That(UpdateRunner.ConfirmStartup(plan.FilePath, plan.NewRoot, new(0, 3, 6)), Is.EqualTo("0.3.6"));
        Assert.That(File.ReadAllText(Path.Combine(plan.Job, "ready")), Is.EqualTo(plan.Id));
        Assert.That(Directory.GetFiles(plan.Job, "*.tmp"), Is.Empty);
    }

    [TestCase("wrong-root"), TestCase("wrong-version"), TestCase("prepared"), TestCase("committed"), TestCase("rolled-back")]
    public void UnrelatedOrFailedUpdatesCannotOfferANewInterface(string condition)
    {
        var plan = Plan();
        if (condition is "prepared" or "committed" or "rolled-back") { plan.State = condition; plan.Save(); }
        string root = condition == "wrong-root" ? Path.Combine(_folder, "Other") : plan.NewRoot;
        var version = condition == "wrong-version" ? new Version(0, 3, 5) : new(0, 3, 6);
        Assert.Throws<InvalidDataException>(() => UpdateRunner.ConfirmStartup(plan.FilePath, root, version));
        Assert.That(File.Exists(Path.Combine(plan.Job, "ready")), Is.False);
    }
}
