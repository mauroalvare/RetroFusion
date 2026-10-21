using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Retro.Update;

/// <summary>The already verified new copy runs this mode before loading window/audio libraries.</summary>
public static class UpdateRunner
{
    public static Version AppVersion
    {
        get { var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0); return new(version.Major, version.Minor, Math.Max(0, version.Build)); }
    }
    private static string Root => Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
    private static string Executable(string directory) => Path.Combine(directory, OperatingSystem.IsWindows() ? "RetroEmu.exe" : "RetroEmu");

    public static void StartHelper(UpdatePlan plan)
    {
        if (!PackageManifest.Paths.Equals(Root, plan.OldRoot)) throw new InvalidOperationException("El ayudante solo se inicia desde la instalación que se actualiza.");
        plan.Arm();
        var start = new ProcessStartInfo(Executable(plan.NewRoot)) { WorkingDirectory = plan.NewRoot, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(plan.FilePath);
        using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar la actualización.");
    }

    public static async Task<int> RunHelperAsync(string path)
    {
        UpdatePlan? plan = null; Process? launched = null; bool owned = false, reopened = false;
        try
        {
            plan = UpdatePlan.Read(path);
            if (!PackageManifest.Paths.Equals(Root, plan.NewRoot) || plan.ToVersion != AppVersion.ToString(3) || plan.ParentProcessId <= 0 || plan.ParentProcessId == Environment.ProcessId)
                throw new InvalidDataException("El ayudante no corresponde a esta actualización.");
            owned = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await WaitParent(plan, timeout.Token);
            EnsureClosed(plan.OldRoot);
            using var gate = UpdateInstaller.Lock(plan);
            var shortcut = new UpdateShortcut(plan);
            shortcut.Capture(); shortcut.PointTo(plan.NewRoot);
            try
            {
                await UpdateInstaller.ApplyAsync(plan, async () =>
                {
                    string ready = Path.Combine(plan.Job, "ready");
                    File.Delete(ready);
                    launched = Launch(plan.NewRoot, plan, acknowledge: true);
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (File.Exists(ready) && File.ReadAllText(ready) == plan.Id) return;
                        if (launched.HasExited) throw new IOException("La versión nueva terminó antes de abrir.");
                        await Task.Delay(100);
                    }
                    throw new IOException("La versión nueva no confirmó su arranque.");
                }, deactivate: async () =>
                {
                    if (launched is { HasExited: false }) { launched.Kill(entireProcessTree: true); await launched.WaitForExitAsync(); }
                });
            }
            catch
            {
                if (Directory.Exists(plan.OldRoot)) { shortcut.Restore(); using var previous = Launch(plan.OldRoot, plan, acknowledge: false); reopened = true; }
                throw;
            }
            if (!plan.TestMode)
                try { var preferences = UpdatePreferences.Load(); preferences.FailedVersion = null; preferences.Save(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return 0;
        }
        catch (Exception error)
        {
            if (plan != null)
            {
                WriteError(plan, error.Message);
                if (owned && !plan.TestMode)
                    try { var preferences = UpdatePreferences.Load(); preferences.FailedVersion = plan.ToVersion; preferences.Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                if (owned && !reopened && Directory.Exists(plan.OldRoot))
                {
                    try
                    {
                        using var parent = Process.GetProcessById(plan.ParentProcessId);
                        if (!parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == plan.ParentStarted) return 1;
                    }
                    catch (ArgumentException) { }
                    try { new UpdateShortcut(plan).Restore(); using var previous = Launch(plan.OldRoot, plan, acknowledge: false); }
                    catch (Exception restart) { WriteError(plan, error.Message + " / " + restart.Message); }
                }
            }
            return 1;
        }
        finally { launched?.Dispose(); }
    }

    private static async Task WaitParent(UpdatePlan plan, CancellationToken ct)
    {
        Process parent;
        try { parent = Process.GetProcessById(plan.ParentProcessId); } catch (ArgumentException) { return; }
        using (parent)
        {
            if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStarted) return;
            if (parent.MainModule?.FileName is not { } executable || !PackageManifest.Paths.Equals(Path.GetFullPath(executable), Executable(plan.OldRoot)))
                throw new InvalidDataException("El proceso que debe cerrar no es esta instalación.");
            await parent.WaitForExitAsync(ct);
        }
    }

    private static void EnsureClosed(string oldRoot)
    {
        foreach (string name in new[] { "RetroEmu", "retro-cli" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                {
                    if (process.Id == Environment.ProcessId || process.HasExited) continue;
                    string? file;
                    try { file = process.MainModule?.FileName; }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { throw new IOException("No pude comprobar que todas las ventanas del programa estén cerradas.", e); }
                    if (file != null && Path.GetFullPath(file).StartsWith(oldRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new IOException("Hay otra ventana o tarea de RetroEmu abierta; no se actualizó.");
                }
    }

    private static Process Launch(string root, UpdatePlan plan, bool acknowledge)
    {
        var start = new ProcessStartInfo(Executable(root)) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--no-update");
        if (acknowledge) { start.ArgumentList.Add("--update-ready"); start.ArgumentList.Add(plan.FilePath); }
        if (plan.TestMode)
            foreach (string arg in new[] { "--hidden", "--mute", "--no-organize", "--autotest", "4", "update-test.png" }) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new IOException("No se pudo abrir RetroEmu.");
    }

    public static void Acknowledge(string path)
    {
        var plan = UpdatePlan.Read(path);
        if (!PackageManifest.Paths.Equals(Root, plan.NewRoot) || plan.ToVersion != AppVersion.ToString(3) || plan.State != "applying")
            throw new InvalidDataException("Confirmación de arranque inválida.");
        string temporary = Path.Combine(plan.Job, Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temporary, plan.Id); File.Move(temporary, Path.Combine(plan.Job, "ready"), true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <returns>True when this process must exit; another copy was started or is updating.</returns>
    public static bool RecoverBeforeStart()
    {
        if (!File.Exists(Path.Combine(Root, PackageManifest.FileName))) return false;
        foreach (var plan in UpdatePlan.Pending(Root))
        {
            bool isNew = PackageManifest.Paths.Equals(Root, plan.NewRoot);
            if (plan.State == "committed") continue;
            if (plan.State is "prepared" or "rolled-back")
            {
                if (!isNew) continue;
                new UpdateShortcut(plan).Restore(); using var old = Launch(plan.OldRoot, plan, acknowledge: false); return true;
            }
            try
            {
                using var gate = UpdateInstaller.Lock(plan);
                EnsureClosed(plan.NewRoot);
                UpdateInstaller.Rollback(plan); new UpdateShortcut(plan).Restore();
                if (isNew) { using var old = Launch(plan.OldRoot, plan, acknowledge: false); return true; }
            }
            catch (Exception error) { WriteError(plan, error.Message); return true; }
        }
        return false;
    }

    private static void WriteError(UpdatePlan plan, string message)
    {
        try { File.WriteAllText(Path.Combine(plan.Job, "error.txt"), "No se pudo completar la actualización. " + message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

internal sealed class UpdateShortcut(UpdatePlan plan, string? link = null)
{
    private string Link => link ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RetroEmu.lnk");
    private string Saved => Path.Combine(plan.Job, "acceso-anterior.lnk");
    public void Capture() { if (!plan.TestMode && OperatingSystem.IsWindows()) CaptureWindows(); }
    public void PointTo(string root) { if (!plan.TestMode && OperatingSystem.IsWindows()) PointWindows(root); }
    public void Restore()
    {
        if (plan.TestMode || !OperatingSystem.IsWindows() || !File.Exists(Saved) || !Directory.Exists(plan.OldRoot)) return;
        try { File.Copy(Saved, Link, overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    [SupportedOSPlatform("windows")]
    private void CaptureWindows()
    {
        if (!File.Exists(Link)) return;
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(Link);
            string target = (string)((dynamic)shortcut).TargetPath;
            if (!string.IsNullOrWhiteSpace(target) && Path.IsPathRooted(target) && PackageManifest.Paths.Equals(Path.GetFullPath(target), Path.Combine(plan.OldRoot, "RetroEmu.exe")))
                if (!File.Exists(Saved)) File.Copy(Link, Saved, overwrite: false);
        }
        finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
    }
    [SupportedOSPlatform("windows")]
    private void PointWindows(string root)
    {
        if (!File.Exists(Saved)) return;
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(Link);
            string target = Path.Combine(root, "RetroEmu.exe");
            ((dynamic)shortcut).TargetPath = target; ((dynamic)shortcut).WorkingDirectory = root;
            ((dynamic)shortcut).IconLocation = target + ",0"; ((dynamic)shortcut).Save();
        }
        finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
    }
}
