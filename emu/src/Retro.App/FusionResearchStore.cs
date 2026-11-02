using System.Text.Json;
using Retro.Compat;
using Retro.Fusion;
using Retro.Studio;

namespace Retro.App;

public sealed record ResearchOriginal(string Slot, string Path, string Fingerprint, string Profile, string Firmware);

public sealed class FusionResearchWork
{
    public int Format { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Updated { get; set; } = DateTime.UtcNow;
    public static string CurrentEngine => StudioSession.EngineFingerprint + ";" + AutonomousResearch.EngineFingerprint;
    public string Engine { get; set; } = CurrentEngine;
    public bool Inspired { get; set; }
    public string Status { get; set; } = "research";
    public GameConcept Concept { get; set; } = new("", "", "", "");
    public List<RecipeGame> Slots { get; set; } = [];
    public List<ResearchOriginal> Originals { get; set; } = [];
    public List<PrepareResult> Preparations { get; set; } = [];
    public int Passes { get; set; }
    public string Summary { get; set; } = "";
    public string StopReason { get; set; } = "";
    public string HumanObservation { get; set; } = "";
    public string? ConstructionId { get; set; }
}

/// <summary>Bounded private research checkpoints. Never contains source ROM files or provider logs.</summary>
public sealed class FusionResearchStore(string? folder = null)
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public string Folder { get; } = folder ?? Path.Combine(StudioWorkStore.Folder, "investigaciones");
    public string Problem { get; private set; } = "";

    private string PathFor(string id)
    {
        if (id is not {Length:32} || id.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Identificador de investigación inválido.");
        return Path.Combine(Folder, id + UiText.Get(".json"));
    }

    private void CheckFolder()
    {
        for (var directory = new DirectoryInfo(Folder); directory != null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("La carpeta de investigación contiene un enlace; elegí una carpeta normal.");
    }

    public void Save(FusionResearchWork work)
    {
        Validate(work); string path = PathFor(work.Id); CheckFolder();
        Directory.CreateDirectory(Folder);
        string leasePath=Path.Combine(Folder, work.Id + UiText.Get(".lock"));
        if (File.Exists(leasePath) && File.GetAttributes(leasePath).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("El bloqueo de investigación es un enlace.");
        using var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("El checkpoint es un enlace.");
        // A machine state is private data. Keep only bounded completed observations, not lab screenshots or logs.
        var snapshot = new FusionResearchWork { Id = work.Id, Engine = work.Engine, Inspired = work.Inspired, Status = work.Status,
            Concept = work.Concept, Slots = work.Slots, Originals = work.Originals, Passes = work.Passes,
            Summary = work.Summary, StopReason = work.StopReason, HumanObservation = work.HumanObservation, ConstructionId=work.ConstructionId,
            Preparations = work.Preparations.Select(Trim).ToList() };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, GameProfile.Json);
        if (bytes.Length > MaxBytes) throw new IOException("La investigación supera el límite de 4 MB. Los checkpoints anteriores se conservaron.");
        if (!File.Exists(path) && Directory.EnumerateFiles(Folder, "*.json").Count() >= 12)
            throw new IOException("Hay doce investigaciones guardadas. Descartá una que no necesites antes de empezar otra.");
        string temp = Path.Combine(Folder, work.Id + "."+Guid.NewGuid().ToString("N")+UiText.Get(".tmp"));
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temp, path, true); work.Updated = snapshot.Updated;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static PrepareResult Trim(PrepareResult result) => new()
    {
        Ok = result.Ok, Problem = result.Problem, Game = result.Game, Machine = result.Machine, File = result.File,
        Member = result.Member, Sha256 = result.Sha256, Boot = result.Boot, Controls = result.Controls, Rounds = result.Rounds,
        Goal = result.Goal, Profile = result.Profile, Continuation = result.Continuation,
        CheckedData = result.CheckedData.TakeLast(32).ToList(), UnresolvedData = result.UnresolvedData.TakeLast(24).ToList(),
        Experiments = result.Experiments.TakeLast(144).ToList()
        // ProfilePath is intentionally not trusted from a portable checkpoint. No arbitrary file is fingerprinted on resume.
    };

    private static void Validate(FusionResearchWork work)
    {
        if (work.Format != 1 || work.Engine==null || work.Status==null || work.HumanObservation is not {Length:<=8000} || work.ConstructionId is {Length:>32} || work.Summary==null || work.StopReason==null || work.Concept == null || work.Originals == null || work.Slots == null || work.Preparations == null ||
            work.Concept.Title==null || work.Concept.Pitch==null || work.Concept.Design==null || work.Concept.PlayerRequest==null || work.Concept.MissingData==null || work.Concept.HowToPlay==null ||
            work.Originals.Count is < 2 or > 3 || work.Slots.Count != work.Originals.Count || work.Preparations.Count > 3 ||
            work.Originals.Any(o => o == null || o.Slot is not ("a" or "b" or "c") || string.IsNullOrEmpty(o.Profile) || o.Firmware==null || o.Fingerprint is not {Length:64} || !o.Fingerprint.All(char.IsAsciiHexDigit) || o.Path is not {Length: > 0 and <=2048}) ||
            work.Originals.Select(o => o.Slot).Distinct().Count() != work.Originals.Count ||
            work.Slots.Any(s => s == null || !work.Originals.Any(o => o.Slot == s.Slot)) ||
            work.Slots.Select(s=>s.Slot).Distinct().Count()!=work.Slots.Count ||
            work.Preparations.Any(p => p == null || p.CheckedData==null || p.UnresolvedData==null || p.Experiments==null || (p.Continuation?.State.Length ?? 0) > 1024 * 1024))
            throw new InvalidDataException("Checkpoint de investigación incompleto o fuera de los límites.");
    }

    public IReadOnlyList<FusionResearchWork> Pending()
    {
        Problem = ""; var results = new List<FusionResearchWork>();
        if (!Directory.Exists(Folder)) return results;
        CheckFolder();
        foreach (var file in new DirectoryInfo(Folder).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Take(32))
            try
            {
                if (file.Length > MaxBytes || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Archivo demasiado grande o enlazado.");
                var work = JsonSerializer.Deserialize<FusionResearchWork>(File.ReadAllText(file.FullName), GameProfile.Json)
                    ?? throw new InvalidDataException("Archivo vacío.");
                Validate(work);
                if (Path.GetFileName(PathFor(work.Id)) != file.Name) throw new InvalidDataException("Identificador diferente del archivo.");
                foreach (var result in work.Preparations) result.ProfilePath = null;
                results.Add(work);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException)
            { Problem = UiText.Get("No se pudo leer una investigación guardada. El archivo se conserva: ") + file.Name; }
        return results;
    }

    public void Remove(string id)
    {
        CheckFolder(); string path = PathFor(id);
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("El checkpoint es un enlace.");
        File.Delete(path);
        string lease = Path.Combine(Folder, id + UiText.Get(".lock"));
        if (File.Exists(lease) && !File.GetAttributes(lease).HasFlag(FileAttributes.ReparsePoint)) File.Delete(lease);
    }
}
