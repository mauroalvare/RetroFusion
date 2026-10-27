using System.Text.Json;

namespace Retro.App;

/// <summary>Display, accessibility, control and performance preferences, apart from sound and AI settings.</summary>
public sealed class AppPreferences
{
    public static readonly int[] TextSizes = [100, 125, 150, 175, 200];
    private sealed record Document(int Version, int TextPercent, bool HighContrast, bool IntegerScale, bool Fullscreen,
        bool PowerSaving, bool ShowFps, string SpectrumJoystick, int C64Port, bool TypeLoad, bool TurboWhileLoading,
        bool PauseInBackground = false);   // added later in v1: an older file without it keeps the default
    private readonly string _path;
    private readonly bool _persist;
    private bool _unreadable;
    public int TextPercent { get; set; } = 100;
    public bool HighContrast { get; set; }
    public bool IntegerScale { get; set; } = true;
    public bool Fullscreen { get; set; }
    public bool PowerSaving { get; set; } = true;
    public bool ShowFps { get; set; }
    public JoystickMode SpectrumJoystick { get; set; } = JoystickMode.Kempston;
    public int C64Port { get; set; } = 2;
    public bool TypeLoad { get; set; } = true;
    public bool TurboWhileLoading { get; set; } = true;
    public bool PauseInBackground { get; set; }
    public string? Problem { get; private set; }
    public float TextScale => TextPercent / 100f;

    public AppPreferences(string? path = null, bool persist = true)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "preferencias.json");
        _persist = persist;
        if (!persist) return;
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > 16384) throw new InvalidDataException();
            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path));
            if (doc is not { Version: 1 } || !Valid(doc.TextPercent, doc.C64Port)
                || !Enum.TryParse(doc.SpectrumJoystick, out JoystickMode joystick) || !Enum.IsDefined(joystick)
                || doc.SpectrumJoystick != joystick.ToString()) throw new InvalidDataException();
            TextPercent = doc.TextPercent; HighContrast = doc.HighContrast; IntegerScale = doc.IntegerScale;
            Fullscreen = doc.Fullscreen; PowerSaving = doc.PowerSaving; ShowFps = doc.ShowFps;
            SpectrumJoystick = joystick; C64Port = doc.C64Port; TypeLoad = doc.TypeLoad; TurboWhileLoading = doc.TurboWhileLoading;
            PauseInBackground = doc.PauseInBackground;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            _unreadable = true; Problem = "No pude leer las preferencias. Los cambios quedan solo en esta sesión.";
        }
    }

    private static bool Valid(int text, int port) => TextSizes.Contains(text) && port is 1 or 2;

    public void Defaults()
    {
        TextPercent = 100; HighContrast = Fullscreen = ShowFps = false; IntegerScale = PowerSaving = true;
        SpectrumJoystick = JoystickMode.Kempston; C64Port = 2; TypeLoad = TurboWhileLoading = true; PauseInBackground = false;
    }

    public bool Save()
    {
        if (!Valid(TextPercent, C64Port) || !Enum.IsDefined(SpectrumJoystick)) { Problem = "Las preferencias no son válidas."; return false; }
        if (!_persist) return true;
        if (_unreadable) return false;
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(1, TextPercent, HighContrast, IntegerScale, Fullscreen,
                PowerSaving, ShowFps, SpectrumJoystick.ToString(), C64Port, TypeLoad, TurboWhileLoading, PauseInBackground)));
            File.Move(temporary, _path, overwrite: true); Problem = null; return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Problem = "No pude guardar las preferencias. Los cambios quedan en esta sesión."; return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
