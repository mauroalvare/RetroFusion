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
        bool ConsoleHistory = true, string? LibraryFolder = null, string? LastStartedVersion = null,
        string Language = "es", string ConsolePalette = "auto", TerminalColors? ConsoleColors = null,
        Dictionary<string, InterfacePalette>? InterfacePalettes = null);   // optional v1 fields preserve older preferences
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
    public string? LastStartedVersion { get; set; }
    public string Language { get; set; } = "es";
    public bool OnlineGameInfo { get; set; }
    public TerminalCursor ConsoleCursor { get; set; }
    public bool ConsoleBlink { get; set; } = true;
    public bool ConsoleScanlines { get; set; }
    public bool ConsoleHistory { get; set; } = true;
    private readonly Dictionary<string, InterfacePalette> _interfacePalettes = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, InterfacePalette> InterfacePalettes => _interfacePalettes;
    public InterfacePalette PaletteFor(InterfaceStyle style) => _interfacePalettes.GetValueOrDefault(InterfaceStyles.Describe(style).Id)
        ?? new("auto", TerminalPalettes.DefaultFor(style));
    // Keep the public and persisted legacy names, but address the currently selected interface.
    public string ConsolePalette
    {
        get => PaletteFor(Interface).Palette;
        set => _interfacePalettes[InterfaceStyles.Describe(Interface).Id] = new(value, ConsoleColors);
    }
    public TerminalColors ConsoleColors
    {
        get => PaletteFor(Interface).Colors;
        set => _interfacePalettes[InterfaceStyles.Describe(Interface).Id] = new(ConsolePalette, value);
    }
    /// <summary>Last folder of the file picker; only used again while it exists.</summary>
    public string? LastFolder { get; set; }
    /// <summary>The games folder chosen by the user; without one, the folder next to the program is found.</summary>
    public string? LibraryFolder { get; set; }
    private string? _problem;
    public string? Problem { get => _problem == null ? null : UiText.Get(_problem); private set => _problem = value; }
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
            if (doc is not { Version: >= 1 } || !Valid(doc.TextPercent, doc.C64Port)
                || !Enum.TryParse(doc.SpectrumJoystick, out JoystickMode joystick) || !Enum.IsDefined(joystick)
                || doc.SpectrumJoystick != joystick.ToString()) throw new InvalidDataException();
            TextPercent = doc.TextPercent; HighContrast = doc.HighContrast; IntegerScale = doc.IntegerScale;
            Fullscreen = doc.Fullscreen; PowerSaving = doc.PowerSaving; ShowFps = doc.ShowFps;
            SpectrumJoystick = joystick; C64Port = doc.C64Port; TypeLoad = doc.TypeLoad; TurboWhileLoading = doc.TurboWhileLoading;
            PauseInBackground = doc.PauseInBackground; LastFolder = doc.LastFolder is { Length: > 0 and <= 1024 } folder ? folder : null;
            // A future/removed style falls back to the principal interface without discarding other preferences.
            InterfaceStyles.TryParse(doc.Interface, out var selected); Interface = selected;
            InterfaceOfferedVersion = doc.InterfaceOfferedVersion is { Length: <= 32 } offered && System.Version.TryParse(offered, out _) ? offered : null;
            LastStartedVersion = doc.LastStartedVersion is { Length: <= 32 } started && System.Version.TryParse(started, out _) ? started : null;
            Language = doc.Language is "es" or "en" ? doc.Language : "es";
            OnlineGameInfo = doc.OnlineGameInfo;
            // An unknown cursor from a later version keeps the default and the rest of the file.
            ConsoleCursor = CursorNames.GetValueOrDefault(doc.ConsoleCursor ?? "", TerminalCursor.Block);
            ConsoleBlink = doc.ConsoleBlink; ConsoleScanlines = doc.ConsoleScanlines; ConsoleHistory = doc.ConsoleHistory;
            LibraryFolder = doc.LibraryFolder is { Length: > 0 and <= 1024 } library ? library : null;
            string paletteId = doc.ConsolePalette ?? "auto";
            if (doc.ConsoleColors != null && !TerminalPalettes.Valid(doc.ConsoleColors)) throw new InvalidDataException();
            if (doc.InterfacePalettes != null)
            {
                if (!ValidPalettes(doc.InterfacePalettes)) throw new InvalidDataException();
                foreach (var pair in doc.InterfacePalettes)
                {
                    string key = InterfaceStyles.TryParse(pair.Key, out var style) ? InterfaceStyles.Describe(style).Id : pair.Key;
                    if (!_interfacePalettes.TryAdd(key, pair.Value)) throw new InvalidDataException();
                }
            }
            else if (paletteId != "auto" || doc.ConsoleColors is { } oldColors && oldColors != TerminalPalettes.Green)
            {
                // The old console palette was global. Copy it, then let each layout evolve independently.
                foreach (var style in InterfaceStyles.All)
                    _interfacePalettes[style.Id] = new(TerminalPalettes.Known(paletteId) ? paletteId : "auto", doc.ConsoleColors ?? TerminalPalettes.Green);
            }
            if (doc.Version > 1)
            {
                _unreadable = true;
                Problem = "Estas preferencias son de una versión posterior. Se leen los valores conocidos sin modificar el archivo. Recuperar archivo conserva una copia antes de guardar.";
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            _unreadable = true; Problem = "No pude leer las preferencias. Los cambios quedan solo en esta sesión.";
        }
    }

    internal static readonly IReadOnlyDictionary<string, TerminalCursor> CursorNames = new Dictionary<string, TerminalCursor>
    { ["block"] = TerminalCursor.Block, ["barra"] = TerminalCursor.Bar, ["subrayado"] = TerminalCursor.Underline };

    private static bool Valid(int text, int port) => TextSizes.Contains(text) && port is 1 or 2;
    private static bool ValidPalettes(IReadOnlyDictionary<string, InterfacePalette> palettes) => palettes.Count <= 32
        && palettes.All(pair => pair.Key.Length is > 0 and <= 32 && pair.Key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            && pair.Value != null && TerminalPalettes.Known(pair.Value.Palette) && TerminalPalettes.Valid(pair.Value.Colors));

    public void Defaults()
    {
        TextPercent = 100; HighContrast = Fullscreen = ShowFps = false; IntegerScale = PowerSaving = true;
        SpectrumJoystick = JoystickMode.Kempston; C64Port = 2; TypeLoad = TurboWhileLoading = true; PauseInBackground = false;
        Interface = InterfaceStyle.Retro; OnlineGameInfo = false;
        Language = "es";
        ConsoleCursor = TerminalCursor.Block; ConsoleBlink = ConsoleHistory = true; ConsoleScanlines = false;
        _interfacePalettes.Clear();
    }

    public bool Save()
    {
        if (!Valid(TextPercent, C64Port) || !Enum.IsDefined(SpectrumJoystick) || !Enum.IsDefined(Interface) || Language is not ("es" or "en") || !ValidPalettes(_interfacePalettes)) { Problem = "Las preferencias no son válidas."; return false; }
        if (!_persist) return true;
        if (_unreadable) return false;
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + UiText.Get(".tmp");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(1, TextPercent, HighContrast, IntegerScale, Fullscreen,
                PowerSaving, ShowFps, SpectrumJoystick.ToString(), C64Port, TypeLoad, TurboWhileLoading, PauseInBackground, LastFolder,
                Interface switch { InterfaceStyle.Hardcore => "hardcore", InterfaceStyle.Terminal => "terminal", InterfaceStyle.Amber => "amber", _ => "retro" }, InterfaceOfferedVersion, OnlineGameInfo,
                CursorNames.First(pair => pair.Value == ConsoleCursor).Key, ConsoleBlink, ConsoleScanlines, ConsoleHistory, LibraryFolder, LastStartedVersion, Language, ConsolePalette, ConsoleColors, _interfacePalettes)));
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
        if (!Valid(TextPercent, C64Port) || !Enum.IsDefined(SpectrumJoystick) || !Enum.IsDefined(Interface) || Language is not ("es" or "en") || !ValidPalettes(_interfacePalettes))
        { Problem = "Las preferencias no son válidas."; return false; }
        string backup = _path + UiText.Get(".respaldo-") + Guid.NewGuid().ToString("N");
        try { File.Copy(_path, backup, overwrite: false); RecoveryPath = backup; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Problem = "No pude conservar el archivo anterior. Los cambios siguen solo en esta sesión."; return false; }
        _unreadable = false;
        if (Save()) { RecoveryPath = backup; return true; }
        _unreadable = true;
        return false;
    }
}
