using System.Security.Cryptography;
using System.Text.Json;
using Retro.Update;

namespace Update.Tests;

[TestFixture]
public class InPlaceInstallerTests
{
    private string _root = "", _installed = "";
    private readonly Dictionary<string, (byte[] Bytes, DateTime Written, FileAttributes Attributes)> _data = new();
    private DateTime _directoryCreated;
    private static readonly string[] UserFiles = ["ROMs/uno.tap", "ROMs/.retroemu/identidades.json",
        "Juegos fusionados/propia/v1.json", "Juegos fusionados/propia/v1.js", "Juegos creados/.trabajos/propio.json",
        "firmware/48.rom", "imgui.ini", "notas/personales.txt", "partida.rfstate"];

    [SetUp] public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "RetroEmu-inplace-" + Guid.NewGuid().ToString("N"));
        _installed = Path.Combine(_root, "Mi emulador");
        var old = PackageTests.OwnPackage(_installed, "0.2.2");
        // A formerly shipped program file must be retired without retiring user files.
        AddProgram(old, _installed, "obsoleto.dll", [1,2,3]);
        AddProgram(old, _installed, "Retro.Update.dll", [2]);
        _directoryCreated = Directory.GetCreationTimeUtc(_installed);
        _data.Clear();
        foreach (string relative in UserFiles)
        {
            string path = Path.Combine(_installed, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("Datos del jugador " + relative);
            File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            if (relative.EndsWith(".tap")) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            _data[relative] = (bytes, File.GetLastWriteTimeUtc(path), File.GetAttributes(path));
        }
        File.WriteAllText(Path.Combine(_root, "ajustes-externos.json"), "Ajustes externos propios");
    }

    [TearDown] public void Cleanup()
    {
        if (!Directory.Exists(_root)) return;
        foreach (string path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        Directory.Delete(_root, true);
    }

    private static void AddProgram(PackageManifest manifest, string root, string relative, byte[] data)
    {
        File.WriteAllBytes(Path.Combine(root, relative), data);
        manifest.Files[relative] = Convert.ToHexString(SHA256.HashData(data));
        File.WriteAllText(Path.Combine(root, PackageManifest.FileName), JsonSerializer.Serialize(manifest, PackageManifest.Json));
    }

    private UpdatePlan Plan()
    {
        var plan = UpdatePlan.Create(_installed, new(new(0,2,4), "win-x64", "own.zip", new Uri("https://github.com/own.zip"), 1, new string('0',64)));
        var next = PackageTests.OwnPackage(plan.PayloadRoot, "0.2.4");
        AddProgram(next, plan.PayloadRoot, "nuevo.dll", [4,5,6]);
        AddProgram(next, plan.PayloadRoot, "Retro.Update.dll", [4]);
        plan.Save(); return plan;
    }

    private void AssertPlayerData()
    {
        foreach (var (relative, expected) in _data)
        {
            string path = Path.Combine(_installed, relative);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(expected.Bytes), relative);
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(expected.Written), relative);
            Assert.That(File.GetAttributes(path), Is.EqualTo(expected.Attributes), relative);
        }
        Assert.That(Directory.GetCreationTimeUtc(_installed), Is.EqualTo(_directoryCreated));
        Assert.That(File.ReadAllText(Path.Combine(_root, "ajustes-externos.json")), Is.EqualTo("Ajustes externos propios"));
    }

    [Test]
    public async Task ReplacesOnlyProgramFilesInTheSameDirectory()
    {
        var plan = Plan(); bool activated = false;
        using (UpdateInstaller.Lock(plan))
            await UpdateInstaller.ApplyAsync(plan, () => { AssertPlayerData(); activated = true; return Task.CompletedTask; });
        Assert.That(activated, Is.True);
        Assert.That(plan.NewRoot, Is.EqualTo(_installed)); Assert.That(plan.State, Is.EqualTo("committed"));
        Assert.That(Directory.Exists(plan.LegacyRoot), Is.False);
        Assert.That(PackageManifest.Read(_installed).Version, Is.EqualTo("0.2.4"));
        PackageManifest.Read(_installed).Verify(_installed, exact:false); AssertPlayerData();
        Assert.That(File.Exists(Path.Combine(_installed,"obsoleto.dll")), Is.False);
        Assert.That(File.Exists(Path.Combine(plan.Backup,"obsoleto.dll")), Is.True);
        Assert.That(File.Exists(Path.Combine(_installed,"nuevo.dll")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(plan.Backup,"ROMs")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(plan.Backup,"Juegos fusionados")), Is.False);
        Assert.Throws<InvalidOperationException>(() => UpdateInstaller.Rollback(plan));
    }

    [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
    [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)] [TestCase(11)] [TestCase(12)]
    [TestCase(13)] [TestCase(14)]
    public void EveryReplacementCanFailWithoutChangingPlayerFiles(int failAt)
    {
        var plan=Plan(); int moves=0;
        using (UpdateInstaller.Lock(plan))
            Assert.ThrowsAsync<IOException>(()=>UpdateInstaller.ApplyAsync(plan,()=>Task.CompletedTask,
                _ => { AssertPlayerData(); if(++moves==failAt) throw new IOException("Corte propio"); }));
        Assert.That(moves, Is.EqualTo(failAt));
        Assert.That(plan.State, Is.EqualTo("rolled-back")); AssertPlayerData();
        Assert.That(PackageManifest.Read(_installed).Version, Is.EqualTo("0.2.2"));
        Assert.That(File.Exists(Path.Combine(_installed,"nuevo.dll")), Is.False);
        PackageManifest.Read(_installed).Verify(_installed,exact:false);
        Assert.DoesNotThrow(()=>UpdateInstaller.Rollback(UpdatePlan.Read(plan.FilePath)));
    }

    [Test]
    public void FailedStartupStopsNewProcessAndRestoresProgram()
    {
        var plan=Plan(); bool stopped=false;
        using(UpdateInstaller.Lock(plan))
            Assert.ThrowsAsync<IOException>(()=>UpdateInstaller.ApplyAsync(plan,()=>throw new IOException("No abre"),
                deactivate:()=>{stopped=true;AssertPlayerData();return Task.CompletedTask;}));
        Assert.That(stopped,Is.True);AssertPlayerData();
        Assert.That(PackageManifest.Read(_installed).Version,Is.EqualTo("0.2.2"));
    }

    [Test]
    public async Task SuccessfulSecondUpdateReusesTheOriginalDirectory()
    {
        var first=Plan();
        using(UpdateInstaller.Lock(first)) await UpdateInstaller.ApplyAsync(first,()=>Task.CompletedTask);
        var second=UpdatePlan.Create(_installed,new(new(0,2,5),"win-x64","own.zip",new Uri("https://github.com/own.zip"),1,new string('0',64)));
        PackageTests.OwnPackage(second.PayloadRoot,"0.2.5");second.Save();
        using(UpdateInstaller.Lock(second)) await UpdateInstaller.ApplyAsync(second,()=>Task.CompletedTask);
        Assert.That(second.NewRoot,Is.EqualTo(_installed));AssertPlayerData();
        Assert.That(Directory.Exists(first.LegacyRoot)||Directory.Exists(second.LegacyRoot),Is.False);
        Assert.That(PackageManifest.Read(_installed).Version,Is.EqualTo("0.2.5"));
    }

    [TestCase(false)] [TestCase(true)]
    public void InterruptedRollbackResumesAfterAnInverseMove(bool restoredOldFile)
    {
        var plan=Plan();
        var candidate=PackageTests.OwnPackage(plan.CandidateRoot,"0.2.4");
        AddProgram(candidate,plan.CandidateRoot,"nuevo.dll",[4,5,6]);AddProgram(candidate,plan.CandidateRoot,"Retro.Update.dll",[4]);
        plan.State="applying";plan.Save();
        string target=Path.Combine(_installed,"RetroEmu.dll"), saved=Path.Combine(plan.Backup,"RetroEmu.dll");
        string incoming=Path.Combine(plan.CandidateRoot,"RetroEmu.dll");
        void Move(string from,string to)
        {
            plan.Moves.Add(plan.MoveFor(from,to));plan.Save();
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Move(from,to);
        }
        Move(target,saved);Move(incoming,target);
        plan.RollbackNext=1;plan.Save();
        File.Move(target,incoming); // inverse completed before the cursor was persisted
        if(restoredOldFile){plan.RollbackNext=0;plan.Save();File.Move(saved,target);}
        var recovered=UpdatePlan.Read(plan.FilePath);UpdateInstaller.Rollback(recovered);
        Assert.That(recovered.State,Is.EqualTo("rolled-back"));AssertPlayerData();
        PackageManifest.Read(_installed).Verify(_installed,exact:false);
    }

    [Test]
    public async Task UnchangedRuntimeLibraryIsNotMovedEvenWhenItIsLocked()
    {
        var plan=Plan();
        var old=PackageManifest.Read(_installed);var next=PackageManifest.Read(plan.PayloadRoot);
        AddProgram(old,_installed,"runtime-sin-cambios.dll",[9,8,7]);
        AddProgram(next,plan.PayloadRoot,"runtime-sin-cambios.dll",[9,8,7]);
        // Refresh the earlier snapshot because this fixture changed its original manifest.
        File.Copy(Path.Combine(_installed,PackageManifest.FileName),Path.Combine(plan.Job,"registro",PackageManifest.FileName),true);
        string path=Path.Combine(_installed,"runtime-sin-cambios.dll");
        using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        using(UpdateInstaller.Lock(plan))await UpdateInstaller.ApplyAsync(plan,()=>Task.CompletedTask);
        Assert.That(plan.Moves.Any(m=>m.From.EndsWith("runtime-sin-cambios.dll")||m.To.EndsWith("runtime-sin-cambios.dll")),Is.False);
        Assert.That(File.ReadAllBytes(path),Is.EqualTo(new byte[]{9,8,7}));
        AssertPlayerData();
    }

    [Test]
    public async Task MissingDefaultFirmwareCanBeAddedWithoutTouchingExistingPlayerFiles()
    {
        var plan=Plan();string relative="firmware/48.rom";
        File.Delete(Path.Combine(_installed,relative));_data.Remove(relative);
        using(UpdateInstaller.Lock(plan))await UpdateInstaller.ApplyAsync(plan,()=>Task.CompletedTask);
        Assert.That(File.ReadAllBytes(Path.Combine(_installed,relative)),Is.EqualTo(File.ReadAllBytes(Path.Combine(plan.PayloadRoot,relative))));
        AssertPlayerData();
    }

    [Test]
    public void UnknownCollisionIsRejectedBeforeAnyReplacement()
    {
        var plan=Plan();string path=Path.Combine(_installed,"nuevo.dll");File.WriteAllText(path,"Archivo del usuario");
        using(UpdateInstaller.Lock(plan))
            Assert.ThrowsAsync<IOException>(()=>UpdateInstaller.ApplyAsync(plan,()=>Task.CompletedTask));
        Assert.That(plan.Moves,Is.Empty);Assert.That(File.ReadAllText(path),Is.EqualTo("Archivo del usuario"));AssertPlayerData();
    }

    [Test]
    public void JournalCannotMoveRomOrUnknownUserFileIntoProgramBackup()
    {
        var plan=Plan();
        foreach(string relative in new[]{"ROMs/uno.tap","notas/personales.txt"})
        {
            var move=plan.MoveFor(Path.Combine(_installed,relative),Path.Combine(plan.Backup,relative));
            Assert.Throws<InvalidDataException>(()=>plan.ValidateMove(move));
        }
        AssertPlayerData();
    }

    [Test]
    public void AdoptsLegacyPreparedPackageWithoutTouchingCurrentLibraries()
    {
        var plan=UpdatePlan.Create(_installed,new(new(0,2,4),"win-x64","own.zip",new Uri("https://github.com/own.zip"),1,new string('0',64)));
        plan.Format=1;PackageTests.OwnPackage(plan.LegacyRoot,"0.2.4");plan.Save();
        UpdatePreparation.AdoptLegacy(plan);
        Assert.That(plan.Format,Is.EqualTo(2));Assert.That(plan.NewRoot,Is.EqualTo(_installed));
        Assert.That(plan.LegacyStaged,Is.True);PackageManifest.Read(plan.PayloadRoot).Verify(plan.PayloadRoot,exact:true);
        Assert.That(UpdatePlan.Read(plan.FilePath).Format,Is.EqualTo(2));AssertPlayerData();
    }
}