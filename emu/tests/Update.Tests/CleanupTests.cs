using System.Text.Json;
using Retro.Update;

namespace Update.Tests;

[TestFixture]
public class CleanupTests
{
    private string _root = "", _installed = "";
    private UpdatePlan _plan = null!;

    [SetUp] public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "RetroEmu-cleanup-" + Guid.NewGuid().ToString("N"));
        _installed = Path.Combine(_root, "Mi emulador");
        PackageTests.OwnPackage(_installed, "0.2.2");
        _plan = UpdatePlan.Create(_installed, new(new(0,2,5), "win-x64", "own.zip", new Uri("https://github.com/own.zip"),1,new string('0',64)));
        PackageTests.OwnPackage(_plan.PayloadRoot,"0.2.5");
        _plan.Save();
        File.WriteAllText(Path.Combine(_installed,"ROMs/propia.tap"),"Datos propios");
        File.SetLastWriteTimeUtc(Path.Combine(_installed,"ROMs/propia.tap"),new(2020,1,2,3,4,5,DateTimeKind.Utc));
    }

    [TearDown] public void Teardown()
    {
        if (!Directory.Exists(_root)) return;
        foreach (string file in Directory.EnumerateFiles(_root,"*",SearchOption.AllDirectories))
            File.SetAttributes(file,File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(_root,true);
    }

    private async Task Commit()
    {
        using (UpdateInstaller.Lock(_plan)) await UpdateInstaller.ApplyAsync(_plan,()=>Task.CompletedTask);
        Assert.That(_plan.State,Is.EqualTo("committed"));
    }
    private void PlayerData()
    {
        Assert.That(File.ReadAllText(Path.Combine(_installed,"ROMs/propia.tap")),Is.EqualTo("Datos propios"));
        Assert.That(File.GetLastWriteTimeUtc(Path.Combine(_installed,"ROMs/propia.tap")),Is.EqualTo(new DateTime(2020,1,2,3,4,5,DateTimeKind.Utc)));
    }

    [Test] public async Task ConfirmedUpdateRemovesDownloadBackupJournalAndParent()
    {
        await Commit();
        Assert.That(Directory.Exists(_plan.Backup),Is.True);
        UpdateCleanup.Sweep(_installed);
        Assert.That(Directory.Exists(UpdatePlan.Metadata(_root)),Is.False);
        PackageManifest.Read(_installed).Verify(_installed,exact:false);
        PlayerData();
    }

    [TestCase("prepared")] [TestCase("applying")] [TestCase("recovery-needed")] [TestCase("rolled-back")]
    public void NeverDeletesUnfinishedOrRetryableJobs(string state)
    {
        _plan.State=state; _plan.Save();
        UpdateCleanup.Sweep(_installed);
        Assert.That(File.Exists(_plan.FilePath),Is.True);
        Assert.That(Directory.Exists(_plan.PayloadRoot),Is.True);
        PlayerData();
    }

    [Test] public void ACommittedJournalDoesNotDeleteRecoveryFilesWhenTheInstalledVersionIsWrong()
    {
        _plan.State="committed"; _plan.Save();
        UpdateCleanup.Sweep(_installed);
        Assert.That(File.Exists(_plan.FilePath),Is.True);
    }

    [Test] public async Task ChangedInstalledProgramKeepsItsBackup()
    {
        await Commit();
        File.WriteAllText(Path.Combine(_installed,"RetroEmu.dll"),"Programa modificado");
        UpdateCleanup.Sweep(_installed);
        Assert.That(Directory.Exists(_plan.Backup),Is.True);
    }

    [TestCase("apply.lock")] [TestCase("prepare.lock")]
    public async Task DoesNotCleanWhileAnotherUpdateOwnsEitherLock(string name)
    {
        await Commit();
        using(var active = new FileStream(Path.Combine(UpdatePlan.Metadata(_root),name),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            Assert.That(UpdateCleanup.Sweep(_installed),Is.False);
            Assert.That(File.Exists(_plan.FilePath),Is.True);
        }
        UpdateCleanup.Sweep(_installed);
        Assert.That(Directory.Exists(UpdatePlan.Metadata(_root)),Is.False);
    }

    [Test] public async Task CleanupCanResumeAfterAFileLockCausesPartialDeletion()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows file deletion sharing semantics.");
        await Commit();
        string path=Path.Combine(_plan.PayloadRoot,"RetroEmu.dll");
        using(var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Assert.That(UpdateCleanup.Sweep(_installed),Is.False);
            Assert.That(Directory.GetFiles(UpdatePlan.Metadata(_root),"limpiar-*.json"),Has.Length.EqualTo(1));
        }
        UpdateCleanup.Sweep(_installed);
        Assert.That(Directory.Exists(UpdatePlan.Metadata(_root)),Is.False);
        PlayerData();
    }

    [Test] public async Task AnotherInstallationInTheSameParentIsPreserved()
    {
        string other=Path.Combine(_root,"Otro emulador");
        PackageTests.OwnPackage(other,"0.2.2");
        var job=UpdatePlan.Create(other,new(new(0,2,5),"win-x64","own.zip",new Uri("https://github.com/own.zip"),1,new string('0',64)));
        PackageTests.OwnPackage(job.PayloadRoot,"0.2.5"); job.Save();
        await Commit();
        UpdateCleanup.Sweep(_installed);
        Assert.That(Directory.Exists(_plan.Job),Is.False);
        Assert.That(File.Exists(job.FilePath),Is.True);
        PackageManifest.Read(other).Verify(other,exact:false);
    }

    [Test] public async Task AReadOnlyTemporaryDoesNotKeepTheUpdaterFolder()
    {
        await Commit();
        string path=Path.Combine(_plan.PayloadRoot,"firmware/48.rom");
        File.SetAttributes(path,File.GetAttributes(path)|FileAttributes.ReadOnly);
        UpdateCleanup.Sweep(_installed);
        Assert.That(Directory.Exists(UpdatePlan.Metadata(_root)),Is.False);
        PlayerData();
    }

    [Test] public async Task DoesNotDeleteAnUnknownFileInTheMetadataParent()
    {
        await Commit();
        string personal=Path.Combine(UpdatePlan.Metadata(_root),"nota-propia.txt");
        File.WriteAllText(personal,"Conservar");
        UpdateCleanup.Sweep(_installed);
        Assert.That(Directory.Exists(_plan.Job),Is.False);
        Assert.That(File.ReadAllText(personal),Is.EqualTo("Conservar"));
    }

    [Test] public async Task DoesNotFollowLinksOutOfTheJob()
    {
        await Commit();
        string external=Path.Combine(_root,"externo.txt"), link=Path.Combine(_plan.Job,"enlace.txt");
        File.WriteAllText(external,"No borrar");
        try { File.CreateSymbolicLink(link,external); }
        catch (Exception e) when(e is UnauthorizedAccessException or IOException) { Assert.Ignore("This host cannot create symbolic links."); }
        try
        {
            UpdateCleanup.Sweep(_installed);
            Assert.That(File.ReadAllText(external),Is.EqualTo("No borrar"));
            Assert.That(Directory.Exists(_plan.Job),Is.True);
        }
        finally { File.Delete(link); }
    }

    [Test] public void InterruptedDownloadIsDiscardedUsingItsPreparationReceipt()
    {
        UpdateCleanup.MarkPreparation(_plan);
        File.Delete(_plan.FilePath);
        File.WriteAllText(Path.Combine(_plan.Job,"package.part"),"Descarga interrumpida");
        UpdateCleanup.Sweep(_installed);
        Assert.That(Directory.Exists(UpdatePlan.Metadata(_root)),Is.False);
        PlayerData();
    }

    [Test] public void PreparationReceiptNeverDeletesASavedPreparedPlan()
    {
        UpdateCleanup.MarkPreparation(_plan);
        UpdateCleanup.Sweep(_installed);
        Assert.That(File.Exists(_plan.FilePath),Is.True);
    }

    [Test] public void ReceiptCannotNameAPathOutsideTheMetadataFolder()
    {
        string path=Path.Combine(UpdatePlan.Metadata(_root),"limpiar-escape.json");
        File.WriteAllText(path,JsonSerializer.Serialize(new { id="../Mi emulador",fromName="Mi emulador",kind="finished" }));
        UpdateCleanup.Sweep(_installed);
        PackageManifest.Read(_installed).Verify(_installed,exact:false);
        PlayerData();
    }

    [Test] public async Task LegacyWholeFolderBackupsAreNotDiscardedByTheInPlaceCleaner()
    {
        await Commit();
        _plan.Format=1; _plan.Moves.Clear(); _plan.Save();
        File.WriteAllText(Path.Combine(_plan.Backup,"datos-propios.tap"),"No borrar");
        UpdateCleanup.Sweep(_installed);
        Assert.That(File.ReadAllText(Path.Combine(_plan.Backup,"datos-propios.tap")),Is.EqualTo("No borrar"));
    }
}
