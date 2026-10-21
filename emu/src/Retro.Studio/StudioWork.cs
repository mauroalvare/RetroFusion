using System.Text.Json;
using Retro.Fusion;
using Retro.Ai;

namespace Retro.Studio;

/// <summary>The current design state, not the conversation or a copy of the API documentation.</summary>
public sealed class DesignCheckpoint
{
    public StudioGame Template { get; set; } = new();
    public string FirstPrompt { get; set; } = "";
    public string Brief { get; set; } = "";
    public StudioGame? Draft { get; set; }
    public ValidationReport? Report { get; set; }
    public string? ParseError { get; set; }
    public List<string> TriedScripts { get; set; } = new();
    public int Calls { get; set; }
    public AiOperationStats? Usage { get; set; }
}

public sealed class StudioWork
{
    public int Format { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Updated { get; set; } = DateTime.UtcNow;
    public string Engine { get; set; } = typeof(StudioSession).Module.ModuleVersionId.ToString();
    public Dictionary<string, string> Originals { get; set; } = new();
    public DesignCheckpoint State { get; set; } = new();

    public bool Matches(OriginalReferences references) => Originals.Count == references.Items.Count &&
        references.Items.All(r => Originals.TryGetValue(r.Slot, out var hash) && hash == r.Fingerprint);
}

/// <summary>Atomic, portable recovery next to the player's fusion games. Contains no ROM bytes, credentials or CLI logs.</summary>
public static class StudioWorkStore
{
    public static string Folder => Path.Combine(StudioLibrary.UserFolder, ".trabajos");
    private const int MaxBytes = 4 * 1024 * 1024;
    private static string PathFor(string id)
    {
        if (id.Length != 32 || id.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Identificador de trabajo inválido.");
        return Path.Combine(Folder, id + ".json");
    }

    public static void Save(StudioWork work)
    {
        string path = PathFor(work.Id);
        Directory.CreateDirectory(Folder);
        if (File.GetAttributes(Folder).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("La carpeta de trabajos es un enlace.");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("El trabajo es un enlace.");
        work.Updated = DateTime.UtcNow;
        if (work.State.Report is { } report) work.State.Report = report with { Picture = null, Shots = null, Robots = null };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(work, GameProfile.Json);
        if (bytes.Length > MaxBytes) throw new IOException("El trabajo supera el tamaño permitido para recuperarlo.");
        string temp = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static IReadOnlyList<StudioWork> Pending()
    {
        var result = new List<StudioWork>();
        if (!Directory.Exists(Folder) || File.GetAttributes(Folder).HasFlag(FileAttributes.ReparsePoint)) return result;
        foreach (var file in new DirectoryInfo(Folder).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Take(30))
        {
            try
            {
                if (file.Length > MaxBytes || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var work = JsonSerializer.Deserialize<StudioWork>(File.ReadAllText(file.FullName), GameProfile.Json);
                if (work == null || work.Format != 1 || work.Id + ".json" != file.Name || work.State == null ||
                    work.State.Template == null || work.State.Brief == null || work.State.FirstPrompt == null ||
                    work.Originals == null || work.State.TriedScripts == null) continue;
                _ = PathFor(work.Id);
                work.State.Template = StudioGame.Parse(work.State.Template.ToJson());
                if (work.State.Draft != null) work.State.Draft = StudioGame.Parse(work.State.Draft.ToJson());
                result.Add(work);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        return result;
    }

    public static void Remove(string id)
    {
        string path = PathFor(id);
        if (Directory.Exists(Folder) && File.GetAttributes(Folder).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("La carpeta de trabajos es un enlace.");
        if (File.Exists(path)) File.Delete(path);
    }
}
