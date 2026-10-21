using System.Text.Json;

namespace Retro.Update;

public sealed class UpdatePreferences
{
    public bool Automatic { get; set; } = true;
    public string? FailedVersion { get; set; }
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu");
    public static UpdatePreferences Load(string? folder = null)
    {
        try
        {
            string path = Path.Combine(folder ?? Folder, "actualizaciones.json");
            if (File.Exists(path) && new FileInfo(path).Length <= 8192) return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path), PackageManifest.Json) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    public void Save(string? folder = null)
    {
        folder ??= Folder; Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, "actualizaciones.json"), temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this, PackageManifest.Json)); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
