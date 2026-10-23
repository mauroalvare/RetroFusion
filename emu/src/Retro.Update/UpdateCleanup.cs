using System.Diagnostics;
using System.Text.Json;

namespace Retro.Update;

/// <summary>Deletes updater-owned jobs after the replacement process has finished.</summary>
public static class UpdateCleanup
{
    private sealed record Receipt(string Id, string FromName, string Kind);
    private static string ReceiptPath(UpdatePlan plan) => Path.Combine(UpdatePlan.Metadata(plan.Parent), $"limpiar-{plan.Id}.json");

    public static void Start(string installed)
    {
        installed = Path.GetFullPath(installed).TrimEnd(Path.DirectorySeparatorChar);
        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 120; attempt++)
            {
                try { if (Sweep(installed)) return; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return; }
                await Task.Delay(250);
            }
        });
    }

    // A receipt lives outside the job, so an interrupted deletion remains resumable.
    internal static void MarkPreparation(UpdatePlan plan) => WriteReceipt(plan, "preparing");
    internal static void ClearPreparation(UpdatePlan plan) => File.Delete(ReceiptPath(plan));

    internal static void DiscardPreparation(UpdatePlan plan)
    {
        plan.Validate();
        WriteReceipt(plan, "finished");
        DeleteJob(plan.Job);
        File.Delete(ReceiptPath(plan));
    }

    private static void WriteReceipt(UpdatePlan plan, string kind)
    {
        plan.Validate();
        string path = ReceiptPath(plan), temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new Receipt(plan.Id, plan.FromName, kind), PackageManifest.Json));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <returns>False only while a lock, helper or temporary file lock requires a retry.</returns>
    internal static bool Sweep(string installed)
    {
        installed = Path.GetFullPath(installed).TrimEnd(Path.DirectorySeparatorChar);
        string? parent = Directory.GetParent(installed)?.FullName;
        if (parent == null) return true;
        string metadata = UpdatePlan.Metadata(parent);
        if (!Directory.Exists(metadata)) return true;
        PackageManifest.NoLinks(metadata);
        bool deferred = false;
        try
        {
            using var preparing = new FileStream(Path.Combine(metadata, "prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var applying = new FileStream(Path.Combine(metadata, "apply.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            foreach (string path in Directory.EnumerateFiles(metadata, "limpiar-*.json").ToArray())
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > 4096 || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(path), PackageManifest.Json);
                    if (receipt == null || !Guid.TryParseExact(receipt.Id, "N", out _) ||
                        !PackageManifest.Paths.Equals(receipt.FromName, Path.GetFileName(installed)) ||
                        Path.GetFileName(path) != $"limpiar-{receipt.Id}.json" || receipt.Kind is not ("preparing" or "finished")) continue;
                    string job = Path.Combine(metadata, receipt.Id);
                    if (InUse(job)) { deferred = true; continue; }
                    if (receipt.Kind == "preparing" && File.Exists(Path.Combine(job, "plan.json"))) continue;
                    DeleteJob(job);
                    File.Delete(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { deferred = true; }
                catch (Exception e) when (e is JsonException or InvalidDataException) { }
            }
            PackageManifest? current = null;
            foreach (var plan in UpdatePlan.Pending(installed))
            {
                if (plan.Format != 2 || plan.State != "committed") continue;
                try
                {
                    if (InUse(plan.Job)) { deferred = true; continue; }
                    current ??= PackageManifest.Read(installed);
                    if (current.Runtime != plan.Runtime || ReleaseClient.ParseVersion(current.Version) < ReleaseClient.ParseVersion(plan.ToVersion)) continue;
                    current.Verify(installed, exact: false);
                    WriteReceipt(plan, "finished");
                    DeleteJob(plan.Job);
                    File.Delete(ReceiptPath(plan));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { deferred = true; }
                catch (Exception e) when (e is InvalidDataException or JsonException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        RemoveEmptyMetadata(parent);
        return !deferred;
    }

    internal static void RemoveEmptyMetadata(string parent)
    {
        string metadata = UpdatePlan.Metadata(parent);
        if (!Directory.Exists(metadata)) return;
        try
        {
            PackageManifest.NoLinks(metadata);
            var entries = Directory.GetFileSystemEntries(metadata);
            if (entries.Any(p => Path.GetFileName(p) is not ("prepare.lock" or "apply.lock"))) return;
            // Never recurse here: a new preparation may have started since the inspection.
            foreach (string path in entries) File.Delete(path);
            Directory.Delete(metadata, recursive: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool InUse(string job)
    {
        foreach (string name in new[] { "RetroEmu", "retro-cli" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        string? file = process.MainModule?.FileName;
                        if (file != null && Path.GetFullPath(file).StartsWith(job + Path.DirectorySeparatorChar,
                            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return true;
                    }
                    catch (ArgumentException) { }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return true; }
                }
        return false;
    }

    private static void DeleteJob(string job)
    {
        job = Path.GetFullPath(job);
        string metadata = Directory.GetParent(job)!.FullName;
        if (!Guid.TryParseExact(Path.GetFileName(job), "N", out _) || Path.GetFileName(metadata) != ".retroemu-updates")
            throw new InvalidDataException("Carpeta ajena al actualizador.");
        PackageManifest.NoLinks(job);
        if (!Directory.Exists(job)) return;
        var files = new List<string>();
        var directories = new List<string>();
        Inspect(job);
        foreach (string path in files)
        {
            PackageManifest.NoLinks(Path.GetDirectoryName(path)!);
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Enlace en los temporales del actualizador.");
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
        foreach (string path in directories) Directory.Delete(path, recursive: false);

        void Inspect(string directory)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (!Path.GetFullPath(path).StartsWith(job + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidDataException("Temporal fuera del trabajo de actualización.");
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Enlace en los temporales del actualizador.");
                if (attributes.HasFlag(FileAttributes.Directory)) Inspect(path); else files.Add(path);
            }
            directories.Add(directory);
        }
    }
}
