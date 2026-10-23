using System.Diagnostics;
using System.Text.Json;

namespace Retro.Update;

public sealed record UpdateMove(string From, string To);

public sealed class UpdatePlan
{
    public int Format { get; set; } = 2;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FromName { get; set; } = "";
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string State { get; set; } = "prepared";
    public int ParentProcessId { get; set; }
    public long ParentStarted { get; set; }
    public bool TestMode { get; set; }
    public int RelayProcessId { get; set; }
    public long RelayStarted { get; set; }
    public bool LegacyStaged { get; set; }
    public List<UpdateMove> Moves { get; set; } = [];
    public int? RollbackNext { get; set; }
    public string? Error { get; set; }
    private PackageManifest? _original, _payload;
    [System.Text.Json.Serialization.JsonIgnore] public string FilePath { get; private set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string Job => Path.GetDirectoryName(FilePath)!;
    [System.Text.Json.Serialization.JsonIgnore] public string Parent => Directory.GetParent(Job)!.Parent!.FullName;
    [System.Text.Json.Serialization.JsonIgnore] public string OldRoot => Path.Combine(Parent, FromName);
    [System.Text.Json.Serialization.JsonIgnore] public string PackageName => $"RetroEmu-{ToVersion}-{Runtime}";
    [System.Text.Json.Serialization.JsonIgnore] public string LegacyRoot => Path.Combine(Parent, PackageName);
    [System.Text.Json.Serialization.JsonIgnore] public string NewRoot => Format == 1 ? LegacyRoot : OldRoot;
    [System.Text.Json.Serialization.JsonIgnore] public string PayloadRoot => Format == 1 ? LegacyRoot : Path.Combine(Job, "paquete", PackageName);
    [System.Text.Json.Serialization.JsonIgnore] public string CandidateRoot => Path.Combine(Job, "instalar");
    [System.Text.Json.Serialization.JsonIgnore] public string Backup => Path.Combine(Job, "anterior");

    public static string Metadata(string parent) => Path.Combine(parent, ".retroemu-updates");

    public static UpdatePlan Create(string installed, ReleaseAsset asset)
    {
        installed = Path.GetFullPath(installed).TrimEnd(Path.DirectorySeparatorChar);
        var old = PackageManifest.Read(installed);
        if (old.Runtime != asset.Runtime || ReleaseClient.ParseVersion(old.Version) >= asset.Version)
            throw new InvalidDataException("La actualización no corresponde a esta instalación.");
        var plan = new UpdatePlan { FromName = Path.GetFileName(installed), FromVersion = old.Version, ToVersion = asset.Version.ToString(3), Runtime = asset.Runtime };
        string parent = Directory.GetParent(installed)?.FullName ?? throw new IOException("No se puede actualizar la raíz del disco.");
        plan.FilePath = Path.Combine(Metadata(parent), plan.Id, "plan.json");
        plan.Validate(); return plan;
    }

    public void Arm()
    {
        using var parent = Process.GetCurrentProcess();
        ParentProcessId = parent.Id; ParentStarted = parent.StartTime.ToUniversalTime().Ticks; Save();
    }

    public void Validate()
    {
        if (Format is not (1 or 2) || !Guid.TryParseExact(Id, "N", out _) || Path.GetFileName(Job) != Id ||
            Path.GetFileName(Directory.GetParent(Job)!.FullName) != ".retroemu-updates" || Path.GetFileName(FilePath) != "plan.json" ||
            string.IsNullOrWhiteSpace(FromName) || FromName.StartsWith('.') || FromName is "." or ".." ||
            FromName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || FromName.Contains('/') || FromName.Contains('\\'))
            throw new InvalidDataException("Plan de actualización inválido.");
        var from = ReleaseClient.ParseVersion(FromVersion); var to = ReleaseClient.ParseVersion(ToVersion);
        if (to <= from || Runtime is not ("win-x64" or "linux-x64") ||
            Format == 1 && PackageManifest.Paths.Equals(OldRoot, NewRoot) ||
            State is not ("prepared" or "applying" or "committed" or "rolled-back" or "recovery-needed") || Moves.Count > 20000 ||
            RollbackNext is < -1 || RollbackNext >= Moves.Count || RelayProcessId < 0 || RelayProcessId > 0 && RelayStarted <= 0 ||
            Format == 1 && (LegacyStaged || RelayProcessId != 0))
            throw new InvalidDataException("Versión o estado de actualización inválido.");
        PackageManifest.NoLinks(Parent); PackageManifest.NoLinks(Job);
        foreach (var move in Moves) ValidateMove(move);
    }

    private string Relative(string path) => Path.GetRelativePath(Parent, path).Replace('\\', '/');
    public UpdateMove MoveFor(string from, string to) => new(Relative(from), Relative(to));
    public string Resolve(string relative) => PackageManifest.Within(Parent, relative);

    public void ValidateMove(UpdateMove move)
    {
        string from = Resolve(move.From), to = Resolve(move.To);
        bool Same(string a, string b) => PackageManifest.Paths.Equals(a, b);
        if (Format == 1)
        {
            if (Same(from, OldRoot) && Same(to, Backup)) return;
            foreach (string folder in PackageManifest.ProtectedFolders)
                if (Same(from, Path.Combine(NewRoot, folder)) && Same(to, Path.Combine(Job, "defaults", folder))) return;
            string rel = Path.GetRelativePath(Backup, from).Replace('\\', '/');
            if (Same(PackageManifest.Within(NewRoot, rel), to)) return;
        }
        else
        {
            string relative = Path.GetRelativePath(OldRoot, from).Replace('\\', '/');
            if (!relative.StartsWith("../", StringComparison.Ordinal) && !PackageManifest.Protected(relative) &&
                Same(PackageManifest.Within(Backup, relative), to))
            {
                _original ??= PackageManifest.Read(Path.Combine(Job, "registro"));
                if (_original.Version != FromVersion || _original.Runtime != Runtime) throw new InvalidDataException("Registro anterior incorrecto.");
                if (relative == PackageManifest.FileName || _original.Files.ContainsKey(relative)) return;
            }
            relative = Path.GetRelativePath(CandidateRoot, from).Replace('\\', '/');
            if (!relative.StartsWith("../", StringComparison.Ordinal) && Same(PackageManifest.Within(OldRoot, relative), to))
            {
                _payload ??= PackageManifest.Read(PayloadRoot);
                if (_payload.Version != ToVersion || _payload.Runtime != Runtime) throw new InvalidDataException("Registro nuevo incorrecto.");
                if (relative == PackageManifest.FileName || _payload.Files.ContainsKey(relative)) return;
            }
        }
        throw new InvalidDataException("Movimiento ajeno a la actualización.");
    }

    public void Save()
    {
        Validate(); Directory.CreateDirectory(Job);
        if (Format == 2)
        {
            string snapshot = Path.Combine(Job, "registro", PackageManifest.FileName);
            PackageManifest.NoLinks(Path.GetDirectoryName(snapshot)!);
            if (!File.Exists(snapshot))
            {
                var original = PackageManifest.Read(OldRoot);
                if (original.Version != FromVersion || original.Runtime != Runtime) throw new InvalidDataException("La instalación anterior cambió.");
                Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
                string copy = Path.Combine(Job, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.Copy(Path.Combine(OldRoot, PackageManifest.FileName), copy, overwrite: false);
                    File.Move(copy, snapshot, overwrite: false);
                }
                finally { if (File.Exists(copy)) File.Delete(copy); }
            }
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(this, PackageManifest.Json);
        if (bytes.Length > 4 * 1024 * 1024) throw new IOException("Diario de actualización demasiado grande.");
        string temp = Path.Combine(Job, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
            while (true)
            {
                try { File.Move(temp, FilePath, true); break; }
                catch (Exception error) when ((error is IOException or UnauthorizedAccessException) && Stopwatch.GetTimestamp() < deadline)
                { Thread.Sleep(50); }
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static UpdatePlan Read(string path)
    {
        path = Path.GetFullPath(path); PackageManifest.NoLinks(Path.GetDirectoryName(path)!);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 4 * 1024 * 1024 || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Diario de actualización inválido.");
        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllBytes(path), PackageManifest.Json) ?? throw new InvalidDataException("Diario vacío.");
        plan.FilePath = path; plan.Validate(); return plan;
    }

    public static IReadOnlyList<UpdatePlan> Pending(string installed)
    {
        string? parent = Directory.GetParent(Path.GetFullPath(installed))?.FullName;
        if (parent == null || !Directory.Exists(Metadata(parent))) return [];
        PackageManifest.NoLinks(Metadata(parent));
        var plans = new List<UpdatePlan>();
        foreach (var dir in new DirectoryInfo(Metadata(parent)).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Take(32))
        {
            try
            {
                var plan = Read(Path.Combine(dir.FullName, "plan.json"));
                if (PackageManifest.Paths.Equals(plan.OldRoot, installed.TrimEnd(Path.DirectorySeparatorChar)) ||
                    PackageManifest.Paths.Equals(plan.NewRoot, installed.TrimEnd(Path.DirectorySeparatorChar))) plans.Add(plan);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException) { }
        }
        return plans;
    }
}

public static class UpdatePreparation
{
    internal static void CreateMetadata(UpdatePlan plan)
    {
        string folder = UpdatePlan.Metadata(plan.Parent);
        PackageManifest.NoLinks(folder); Directory.CreateDirectory(folder);
    }

    /// <summary>Adopts a verified download prepared by the published 0.2.1-0.2.3 updater.</summary>
    public static void AdoptLegacy(UpdatePlan plan)
    {
        plan.Validate();
        if (plan.Format != 1 || plan.State != "prepared" || plan.Moves.Count != 0)
            throw new InvalidOperationException("La actualización anterior ya empezó a instalarse.");
        string source = plan.LegacyRoot;
        var next = PackageManifest.Read(source);
        if (next.Version != plan.ToVersion || next.Runtime != plan.Runtime) throw new InvalidDataException("Descarga anterior incorrecta.");
        next.Verify(source, exact: true);
        CreateMetadata(plan);
        string payload = Path.Combine(plan.Job, "paquete", plan.PackageName);
        PackageManifest.CopyPackage(source, payload);
        plan.Format = 2; plan.LegacyStaged = true; plan.Save();
    }

    public static async Task<UpdatePlan> PrepareAsync(ReleaseClient client, ReleaseAsset asset, string installed,
        IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        var previous = PackageManifest.Read(installed); previous.Verify(installed, exact: false);
        var plan = UpdatePlan.Create(installed, asset);
        CreateMetadata(plan);
        try
        {
            using var gate = new FileStream(Path.Combine(UpdatePlan.Metadata(plan.Parent), "prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var prepared = UpdatePlan.Pending(installed).FirstOrDefault(p => p.Format == 2 &&
                p.State is "prepared" or "rolled-back" && p.ToVersion == asset.Version.ToString(3) && p.FromVersion == previous.Version);
            if (prepared != null)
            {
                PackageManifest.Read(prepared.PayloadRoot).Verify(prepared.PayloadRoot, exact: true);
                if (prepared.State == "rolled-back")
                {
                    prepared.State = "prepared"; prepared.Moves.Clear(); prepared.RollbackNext = null; prepared.Error = null; prepared.Save();
                }
                UpdateCleanup.ClearPreparation(prepared);
                return prepared;
            }
            Directory.CreateDirectory(plan.Job);
            UpdateCleanup.MarkPreparation(plan);
            try
            {
                string archive = Path.Combine(plan.Job, "package.part");
                await client.DownloadAsync(asset, archive, progress, ct);
                progress?.Report(new("Verificando los archivos..."));
                await Task.Run(() => SafePackage.Extract(archive, Path.Combine(plan.Job, "paquete"), asset, ct), ct);
                ct.ThrowIfCancellationRequested();
                plan.Save(); File.Delete(archive); UpdateCleanup.ClearPreparation(plan);
                return plan;
            }
            catch
            {
                try { UpdateCleanup.DiscardPreparation(plan); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                throw;
            }
        }
        finally { UpdateCleanup.RemoveEmptyMetadata(plan.Parent); }
    }
}
