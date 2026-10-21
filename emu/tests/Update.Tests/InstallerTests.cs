using Retro.Update;

namespace Update.Tests;

[TestFixture]
public class InstallerTests
{
    private string _root = "", _old = "", _new = "";
    private readonly Dictionary<string, (byte[] Bytes, DateTime Written)> _data = new();
    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "RetroEmu-install-" + Guid.NewGuid().ToString("N"));
        _old = Path.Combine(_root, "RetroEmu-0.2.1-win-x64"); _new = Path.Combine(_root, "RetroEmu-0.2.2-win-x64");
        PackageTests.OwnPackage(_old, "0.2.1"); PackageTests.OwnPackage(_new, "0.2.2");
        _data.Clear();
        foreach (string path in new[] { "ROMs/own.tap", "ROMs/.retroemu/identidades.json", "Juegos creados/.trabajos/propio.json", "Juegos fusionados/propio/v1.js", "firmware/48.rom", "imgui.ini", "notas/personales.txt" })
        {
            string file = Path.Combine(_old, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("Datos propios de prueba " + path); File.WriteAllBytes(file, bytes);
            File.SetLastWriteTimeUtc(file, new DateTime(2021, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            _data[path] = (bytes, File.GetLastWriteTimeUtc(file));
        }
        File.WriteAllText(Path.Combine(_root, "ajustes-externos.json"), "Datos externos propios");
    }
    [TearDown] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private UpdatePlan Plan()
    {
        var asset = new ReleaseAsset(new(0, 2, 2), "win-x64", "test.zip", new Uri("https://github.com/own.zip"), 1, new string('0', 64));
        var plan = UpdatePlan.Create(_old, asset); plan.Save(); return plan;
    }
    private void AssertData(string root)
    {
        foreach (var (path, expected) in _data)
        {
            Assert.That(File.ReadAllBytes(Path.Combine(root, path)), Is.EqualTo(expected.Bytes), path);
            Assert.That(File.GetLastWriteTimeUtc(Path.Combine(root, path)), Is.EqualTo(expected.Written), path);
        }
        Assert.That(File.ReadAllText(Path.Combine(_root, "ajustes-externos.json")), Is.EqualTo("Datos externos propios"));
    }

    [Test]
    public async Task SuccessfulInstallMovesDataToVersionedFolderWithoutDuplicatingLibraries()
    {
        var plan = Plan(); bool activated = false;
        using (UpdateInstaller.Lock(plan)) await UpdateInstaller.ApplyAsync(plan, () => { AssertData(_new); activated = true; return Task.CompletedTask; });
        Assert.Multiple(() =>
        {
            Assert.That(activated, Is.True); Assert.That(Directory.Exists(_old), Is.False);
            Assert.That(Directory.Exists(Path.Combine(plan.Backup, "ROMs")), Is.False);
            Assert.That(File.Exists(Path.Combine(plan.Backup, "RetroEmu.exe")), Is.True);
            Assert.That(UpdatePlan.Read(plan.FilePath).State, Is.EqualTo("committed"));
        });
        PackageManifest.Read(_new).Verify(_new, exact: false); AssertData(_new);
        Assert.Throws<InvalidOperationException>(() => UpdateInstaller.Rollback(plan));
    }

    [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
    public void AFailureAfterAnyEarlyMoveRestoresOriginalData(int failAt)
    {
        var plan = Plan(); int moves = 0;
        using (UpdateInstaller.Lock(plan))
            Assert.ThrowsAsync<IOException>(() => UpdateInstaller.ApplyAsync(plan, () => Task.CompletedTask, _ => { if (++moves == failAt) throw new IOException("Corte de prueba"); }));
        AssertData(_old); Assert.That(UpdatePlan.Read(plan.FilePath).State, Is.EqualTo("rolled-back"));
        PackageManifest.Read(_new).Verify(_new, exact: true);
        Assert.DoesNotThrow(() => UpdateInstaller.Rollback(UpdatePlan.Read(plan.FilePath)));
    }

    [Test]
    public void LaunchFailureDeactivatesNewProcessBeforeRestoringData()
    {
        var plan = Plan(); bool stopped = false;
        using (UpdateInstaller.Lock(plan))
            Assert.ThrowsAsync<IOException>(() => UpdateInstaller.ApplyAsync(plan, () => throw new IOException("No arrancó"),
                deactivate: () => { stopped = true; AssertData(_new); return Task.CompletedTask; }));
        Assert.That(stopped, Is.True); AssertData(_old);
    }

    [TestCase(false)] [TestCase(true)]
    public void InterruptedRollbackResumesAcrossOverlappingFolderMoves(bool defaultAlreadyRestored)
    {
        var plan = Plan(); plan.State = "applying";
        void Move(string from, string to)
        {
            plan.Moves.Add(plan.MoveFor(from, to)); plan.Save(); Directory.CreateDirectory(Path.GetDirectoryName(to)!); Directory.Move(from, to);
        }
        Move(_old, plan.Backup);
        string defaults = Path.Combine(plan.Job, "defaults/ROMs");
        Move(Path.Combine(_new, "ROMs"), defaults);
        Move(Path.Combine(plan.Backup, "ROMs"), Path.Combine(_new, "ROMs"));
        plan.RollbackNext = 2; plan.Save();
        Directory.Move(Path.Combine(_new, "ROMs"), Path.Combine(plan.Backup, "ROMs"));
        if (defaultAlreadyRestored)
        {
            plan.RollbackNext = 1; plan.Save(); Directory.Move(defaults, Path.Combine(_new, "ROMs"));
        }
        // The last inverse move happened, but its completion was never saved.
        UpdateInstaller.Rollback(UpdatePlan.Read(plan.FilePath));
        AssertData(_old); Assert.That(UpdatePlan.Read(plan.FilePath).State, Is.EqualTo("rolled-back"));
    }

    [Test]
    public void PersonalFileCollisionAbortsInsteadOfOverwritingEitherCopy()
    {
        var next = PackageManifest.Read(_new); string collision = Path.Combine(_new, "imgui.ini");
        File.WriteAllText(collision, "Archivo del paquete de prueba");
        next.Files["imgui.ini"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(collision)));
        File.WriteAllText(Path.Combine(_new, PackageManifest.FileName), System.Text.Json.JsonSerializer.Serialize(next, PackageManifest.Json));
        var plan = Plan();
        using (UpdateInstaller.Lock(plan)) Assert.ThrowsAsync<IOException>(() => UpdateInstaller.ApplyAsync(plan, () => Task.CompletedTask));
        AssertData(_old); Assert.That(File.ReadAllText(collision), Is.EqualTo("Archivo del paquete de prueba"));
    }

    [Test]
    public void JournalCannotEscapeAndOnlyOneHelperCanApply()
    {
        var plan = Plan();
        using (UpdateInstaller.Lock(plan)) Assert.Throws<IOException>(() => { using var other = UpdateInstaller.Lock(plan); });
        plan.Moves.Add(new("../outside", "other"));
        Assert.Throws<InvalidDataException>(() => plan.Save()); AssertData(_old);
    }
}
