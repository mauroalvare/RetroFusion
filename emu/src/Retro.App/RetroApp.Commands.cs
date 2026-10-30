using Raylib_cs;

namespace Retro.App;

public sealed partial class RetroApp
{
    private readonly CommandPalette _commands = new();
    private IReadOnlyList<AppCommand>? _commandCatalog;

    private IReadOnlyList<AppCommand> Commands => _commandCatalog ??= [
        new("library", "Ir a la biblioteca", "Biblioteca", "inicio juegos", ToolShortcut("L"), () => { CloseEditors(); _home.Open(); }),
        new("open", "Abrir juego o programa…", "Biblioteca", "archivo rom cinta disco", ToolShortcut("O"), _browser.Open),
        new("favorites", "Ver favoritos", "Biblioteca", "juegos", "", () => { CloseEditors(); _home.ShowFavorites(); }),
        new("recent", "Ver juegos recientes", "Biblioteca", "continuar historial", "", () => { CloseEditors(); _home.ShowRecents(); }),
        new("fusion", "Fusionar juegos…", "Crear", "base aportes mezclar ia", "", OpenFusionStudio),
        new("new", "Crear un juego desde cero…", "Crear", "ensamblador plantilla ia", "", OpenCreator),
        new("inspired", "Crear con originales…", "Crear", "roms recursos ia", "", OpenInspiredCreator),
        new("modify", "Modificar y analizar el juego…", "Herramientas", "variables vidas cambios", ToolShortcut("M"), OpenModifications,
            () => _host.Machine != null, "Abrí una máquina o un juego."),
        new("code", "Ver código y rutinas…", "Herramientas", "desensamblador parches referencias", ToolShortcut("K"), () => { CloseEditors(); _home.Close(); _code.Open(); },
            () => _host.Machine != null, "Abrí una máquina o un juego."),
        new("debugger", "Mostrar u ocultar el depurador", "Herramientas", "cpu memoria registros", "F12", () => { _home.Close(); _debugMode = !_debugMode; },
            () => _host.Machine != null, "Abrí una máquina o un juego."),
        new("layout", "Reorganizar paneles del depurador", "Herramientas", "ventanas orden", "", () => { _home.Close(); _debugMode = true; _arrangeDebugger = true; },
            () => _host.Machine != null, "Abrí una máquina o un juego."),
        new("save", "Guardar estado rápido", "Partida", "partida progreso", Shortcut("F2"), _host.QuickSave, () => _host.CanUseStates, "Disponible en un original cargado."),
        new("load", "Cargar estado rápido", "Partida", "partida progreso", "", _host.QuickLoad, () => _host.CanUseStates, "Disponible en un original cargado."),
        new("slots", "Abrir partidas guardadas…", "Partida", "ranuras estados", "", OpenStateWindow, () => _host.CanUseStates, "Disponible en un original cargado."),
        new("capture", "Guardar captura de pantalla", "Partida", "imagen foto png", ToolShortcut("S"), SaveCapture, () => _host.CurrentVideo != null, "Encendé una máquina."),
        new("rewind", "Retroceder cinco segundos", "Partida", "rebobinar historial", "", () => TryStateAction(() => _host.Rewind()),
            () => _host.RewindPoints > 0 && !_host.IsRecordingInputs && !_host.IsReplayingInputs, "Activá el historial en Juego > Rebobinado y entradas."),
        new("preferences", "Abrir preferencias…", "Opciones", "configuracion controles joystick accesibilidad", "", () => _preferencesOpen = true),
        new("sound", "Ajustar volumen y sonido…", "Opciones", "audio silenciar", "", () => _soundWindowOpen = true),
        new("ai", "Conectar o configurar IA…", "Opciones", "modelo proveedor conexion", "", () => AiConnectionWindow.Open(_ai)),
        new("contrast", "Activar o desactivar alto contraste", "Opciones", "accesibilidad lectura colores", "", () => SetHighContrast(!_prefs.HighContrast)),
        new("fullscreen", "Alternar pantalla completa", "Opciones", "ventana tamaño", "F11", ToggleFullscreen),
        new("updates", "Buscar actualizaciones…", "Ayuda", "version programa", "", _updates.Open)
    ];
}
