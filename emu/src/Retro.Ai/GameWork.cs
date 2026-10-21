using System.Text.Json;
using Retro.Asm;
using Retro.Fusion;

namespace Retro.Ai;

public sealed class WriterCheckpoint
{
    public string Machine { get; set; } = "c64";
    public string Wish { get; set; } = "";
    public string Brief { get; set; } = "";
    public GameDraft? Draft { get; set; }
    public string Author { get; set; } = "";
    public string? Problem { get; set; }
    public List<string> TriedSources { get; set; } = [];
    public int Calls { get; set; }
    public AiOperationStats? Usage { get; set; }
}

public sealed class GameWork
{
    public int Format { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Updated { get; set; } = DateTime.UtcNow;
    public WriterCheckpoint State { get; set; } = new();
}

/// <summary>Only the current source and design brief; no assembled bytes, pictures, credentials or CLI history.</summary>
public static class GameWorkStore
{
    public static string Folder => Path.Combine(CreatedGames.UserFolder, ".trabajos");
    private const int MaxBytes = 4 * 1024 * 1024;
    private static string PathFor(string id)
    {
        if (id.Length != 32 || id.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Identificador de trabajo inválido.");
        return Path.Combine(Folder, id + ".json");
    }
    public static void Save(GameWork work)
    {
        string path = PathFor(work.Id); Directory.CreateDirectory(Folder);
        if (File.GetAttributes(Folder).HasFlag(FileAttributes.ReparsePoint) ||
            File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("El trabajo es un enlace.");
        work.Updated = DateTime.UtcNow;
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
    public static IReadOnlyList<GameWork> Pending()
    {
        var result = new List<GameWork>();
        if (!Directory.Exists(Folder) || File.GetAttributes(Folder).HasFlag(FileAttributes.ReparsePoint)) return result;
        foreach (var file in new DirectoryInfo(Folder).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Take(30))
        {
            try
            {
                if (file.Length > MaxBytes || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var work = JsonSerializer.Deserialize<GameWork>(File.ReadAllText(file.FullName), GameProfile.Json);
                if (work == null || work.Format != 1 || work.Id + ".json" != file.Name || work.State == null ||
                    string.IsNullOrWhiteSpace(work.State.Machine) || Templates.For(work.State.Machine) == null || work.State.Wish == null || string.IsNullOrWhiteSpace(work.State.Brief) ||
                    work.State.Author == null || work.State.TriedSources == null || work.State.Draft is { Source: null or "" }) continue;
                if (work.State.Draft != null)
                {
                    work.State.Draft = GameWriter.ParseDraft(JsonSerializer.Serialize(work.State.Draft, GameProfile.Json), out _);
                    if (work.State.Draft == null) continue;
                }
                _ = PathFor(work.Id); result.Add(work);
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
