using System.Text.Json;

namespace Retro.App;

/// <summary>Player audio preferences, independent of machine state and AI settings.</summary>
public sealed class SoundSettings
{
    private sealed record Document(int Version, int VolumePercent, bool Muted, bool MuteInBackground, int BufferFrames);
    private readonly string _path;
    private readonly bool _persist;
    private bool _unreadable;
    public int VolumePercent { get; set; } = 100;
    public bool Muted { get; set; }
    public bool MuteInBackground { get; set; }
    public int BufferFrames { get; set; } = 2;
    public string? Problem { get; private set; }

    public SoundSettings(string? path = null, bool persist = true)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "sonido.json");
        _persist = persist;
        if (!persist) return;
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > 16384) throw new InvalidDataException();
            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path));
            if (doc is not { Version: 1 } || !Valid(doc.VolumePercent, doc.BufferFrames)) throw new InvalidDataException();
            VolumePercent = doc.VolumePercent; Muted = doc.Muted; MuteInBackground = doc.MuteInBackground; BufferFrames = doc.BufferFrames;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            _unreadable = true; Problem = "No pude leer las opciones de sonido. Los cambios quedan solo en esta sesión.";
        }
    }
    private static bool Valid(int volume, int frames) => volume is >= 0 and <= 100 && frames is 1 or 2 or 4;
    public void Defaults() { VolumePercent = 100; Muted = MuteInBackground = false; BufferFrames = 2; }
    public bool Save()
    {
        if (!Valid(VolumePercent, BufferFrames)) { Problem = "Las opciones de sonido no son válidas."; return false; }
        if (!_persist) return true;
        if (_unreadable) return false;
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(1, VolumePercent, Muted, MuteInBackground, BufferFrames)));
            File.Move(temporary, _path, overwrite: true); Problem = null; return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Problem = "No pude guardar el sonido. Los cambios quedan en esta sesión."; return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
