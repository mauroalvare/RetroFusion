using System.Text.Json;

namespace Retro.App;

/// <summary>Display, accessibility, control and performance preferences, apart from sound and AI settings.</summary>
public sealed class AppPreferences
{
    public static readonly int[] TextSizes = [100, 125, 150, 175, 200];
    private sealed record Document(int Version, int TextPercent, bool HighContrast, bool IntegerScale, bool Fullscreen,
        bool PowerSaving, bool ShowFps, string SpectrumJoystick, int C64Port, bool TypeLoad, bool TurboWhileLoading,
        bool PauseInBackground = false, string? LastFolder = null, string Interface = "retro", string? InterfaceOfferedVersion = null,
        bool OnlineGameInfo = false, string ConsoleCursor = "block", bool ConsoleBlink = true, bool ConsoleScanlines = false,
        bool ConsoleHistory = true, string? LibraryFolder = null);   // optional v1 fields preserve older preferences
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
    public InterfaceStyle Interface { get; set; }
    public string? InterfaceOfferedVersion { get; set; }
    public bool OnlineGameInfo { get; set; }
    public TerminalCursor ConsoleCursor { get; set; }
    public bool ConsoleBlink { get; set; } = true;
    public bool ConsoleScanlines { get; set; }
    public bool ConsoleHistory { get; set; } = true;
    /// <summary>Last folder of the file picker; only used again while it exists.</summary>
    public string? LastFolder { get; set; }
    /// <summary>The games folder chosen by the user; without one, the folder next to the program is found.</summary>
    public string? LibraryFolder { get; set; }
    public string? Problem { get; private set; }
    public bool CanRecover => _persist && _unreadable;
    public string? RecoveryPath { get; private set; }
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
            PauseInBackground = doc.PauseInBackground; LastFolder = doc.LastFolder is { Length: > 0 and <= 1024 } folder ? folder : null;
            // A future/removed style falls back to the principal interface without discarding other preferences.
            InterfaceStyles.TryParse(doc.Interface, out var selected); Interface = selected;
            InterfaceOfferedVersion = doc.InterfaceOfferedVersion is { Length: <= 32 } offered && System.Version.TryParse(offered, out _) ? offered : null;
            OnlineGameInfo = doc.OnlineGameInfo;
            // An unknown cursor from a later version keeps the default and the rest of the file.
            ConsoleCursor = CursorNames.GetValueOrDefault(doc.ConsoleCursor ?? "", TerminalCursor.Block);
            ConsoleBlink = doc.ConsoleBlink; ConsoleScanlines = doc.ConsoleScanlines; ConsoleHistory = doc.ConsoleHistory;
            LibraryFolder = doc.LibraryFolder is { Length: > 0 and <= 1024 } library ? library : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            _unreadable = true; Problem = "No pude leer las preferencias. Los cambios quedan solo en esta sesión.";
        }
    }

    internal static readonly IReadOnlyDictionary<string, TerminalCursor> CursorNames = new Dictionary<string, TerminalCursor>
    { ["block"] = TerminalCursor.Block, ["barra"] = TerminalCursor.Bar, ["subrayado"] = TerminalCursor.Underline };

    private static bool Valid(int text, int port) => TextSizes.Contains(text) && port is 1 or 2;

    public void Defaults()
    {
        TextPercent = 100; HighContrast = Fullscreen = ShowFps = false; IntegerScale = PowerSaving = true;
        SpectrumJoystick = JoystickMode.Kempston; C64Port = 2; TypeLoad = TurboWhileLoading = true; PauseInBackground = false;
        Interface = InterfaceStyle.Retro; OnlineGameInfo = false;
        ConsoleCursor = TerminalCursor.Block; ConsoleBlink = ConsoleHistory = true; ConsoleScanlines = false;
    }

    public bool Save()
    {
        if (!Valid(TextPercent, C64Port) || !Enum.IsDefined(SpectrumJoystick) || !Enum.IsDefined(Interface)) { Problem = "Las preferencias no son válidas."; return false; }
        if (!_persist) return true;
        if (_unreadable) return false;
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(1, TextPercent, HighContrast, IntegerScale, Fullscreen,
                PowerSaving, ShowFps, SpectrumJoystick.ToString(), C64Port, TypeLoad, TurboWhileLoading, PauseInBackground, LastFolder,
                InterfaceStyles.Describe(Interface).Id, InterfaceOfferedVersion, OnlineGameInfo,
                CursorNames.First(pair => pair.Value == ConsoleCursor).Key, ConsoleBlink, ConsoleScanlines, ConsoleHistory, LibraryFolder)));
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

    /// <summary>Explicit user recovery: preserve the unreadable file before saving the session values.</summary>
    public bool Recover()
    {
        if (!CanRecover) return Save();
        if (!Valid(TextPercent, C64Port) || !Enum.IsDefined(SpectrumJoystick))
        { Problem = "Las preferencias no son válidas."; return false; }
        string backup = _path + ".respaldo-" + Guid.NewGuid().ToString("N");
        try { File.Copy(_path, backup, overwrite: false); RecoveryPath = backup; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Problem = "No pude conservar el archivo anterior. Los cambios siguen solo en esta sesión."; return false; }
        _unreadable = false;
        if (Save()) { RecoveryPath = backup; return true; }
        _unreadable = true;
        return false;
    }
}
