using Raylib_cs;

namespace Retro.App;

public sealed partial class RetroApp
{
    private readonly CommandPalette _commands = new();
    private IReadOnlyList<AppCommand>? _commandCatalog;
    private (bool Shift, bool Pc, bool C64) _commandContext;

    private IReadOnlyList<AppCommand> Commands
    {
        get
        {
            var context = (ToolShortcutsNeedShift, _host.Pc != null, _host.C64 != null);
            if (context != _commandContext) { _commandCatalog = null; _commandContext = context; }
            return _commandCatalog ??= BuildCommands();
        }
    }

    private IReadOnlyList<AppCommand> BuildCommands() => [
        new("auto-analysis","Investigar variables automáticamente…","Herramientas","memoria candidatos evidencia presupuesto hexadecimal", "",OpenAutomaticAnalysis,
            ()=>_host.LoadedMediaSource!=null && _host.Fusion==null && _host.Machine?.Memory.AddressBits<=16,"Abrí un original de 8 bits."),
        new("create-hub", "Abrir el taller de creación", "Crear", "inicio proyectos", "", () => OpenHub(HubPage.Create)),
        new("tools-hub", "Ver todas las herramientas", "Herramientas", "inicio opciones mesa", "", () => OpenHub(HubPage.Tools)),
        new("help-hub", "Ver ayuda y controles", "Ayuda", "instrucciones teclado mando", "", () => OpenHub(HubPage.Help)),
        new("play", "Volver a jugar", "Partida", "original continuar", "", OpenPlay),
        new("replay-tools", "Rebobinado y grabación de entradas", "Partida", "replay historial", "", () => { OpenHub(HubPage.Tools); _showReplayTools=true; }),
        new("library", "Ir a la biblioteca", "Biblioteca", "inicio juegos", ToolShortcut("L"), () => { CloseEditors(); _home.Open(); }),
        new("open", "Abrir juego o programa…", "Biblioteca", "archivo rom cinta disco", ToolShortcut("O"), _browser.Open),
        new("folder", "Elegir la carpeta de juegos…", "Biblioteca", "importar agregar directorio roms ubicacion", "", () => _browser.OpenFolder(UseLibraryFolder)),
        new("favorites", "Ver favoritos", "Biblioteca", "juegos", "", () => { CloseEditors(); _home.ShowFavorites(); }),
        new("recent", "Ver juegos recientes", "Biblioteca", "continuar historial", "", () => { CloseEditors(); _home.ShowRecents(); }),
        new("fusion", "Fusionar juegos…", "Crear", "base aportes mezclar ia", "", OpenFusionStudio),
        new("new", "Crear un juego desde cero…", "Crear", "ensamblador plantilla ia", "", OpenCreator),
        new("inspired", "Crear con originales…", "Crear", "roms recursos ia", "", OpenInspiredCreator),
        new("modify", "Modificar y analizar el juego…", "Herramientas", "variables vidas cambios", ToolShortcut("M"), OpenModifications,
            () => _host.Machine != null, "Abrí una máquina o un juego."),
        new("code", "Ver código y rutinas…", "Herramientas", "desensamblador parches referencias", ToolShortcut("K"), () => { CloseEditors(); _home.Close(); _code.Open(); },
            () => _host.Machine != null, "Abrí una máquina o un juego."),
        new("debugger", "Mostrar el depurador", "Herramientas", "cpu memoria registros", "F12", OpenDebugger,
            () => _host.Machine != null, "Abrí una máquina o un juego."),
        new("layout", "Reorganizar paneles del depurador", "Herramientas", "ventanas orden", "", () => { OpenDebugger(); _arrangeDebugger = true; },
            () => _host.Machine != null, "Abrí una máquina o un juego."),
        new("save", "Guardar estado rápido", "Partida", "partida progreso", Shortcut("F2"), _host.QuickSave, () => _host.CanUseStates, "Disponible en un original cargado."),
        new("load", "Cargar estado rápido", "Partida", "partida progreso", "", _host.QuickLoad, () => _host.CanUseStates, "Disponible en un original cargado."),
        new("slots", "Abrir partidas guardadas…", "Partida", "ranuras estados", "", OpenStateWindow, () => _host.CanUseStates, "Disponible en un original cargado."),
        new("capture", "Guardar captura de pantalla", "Partida", "imagen foto png", ToolShortcut("S"), SaveCapture, () => _host.CurrentVideo != null, "Encendé una máquina."),
        new("rewind", "Retroceder cinco segundos", "Partida", "rebobinar historial", "", () => TryStateAction(() => _host.Rewind()),
            () => _host.RewindPoints > 0 && !_host.IsRecordingInputs && !_host.IsReplayingInputs, "Activá el historial en Juego > Rebobinado y entradas."),
        new("preferences", "Abrir preferencias…", "Opciones", "configuracion controles joystick accesibilidad", "", () => _preferencesOpen = true),
        new("interface", "Elegir interfaz y ver estilos…", "Opciones", "terminal retro real apariencia tema", "", OpenInterfacePicker),
        new("sound", "Ajustar volumen y sonido…", "Opciones", "audio silenciar", "", () => _soundWindowOpen = true),
        new("audio-retry", "Buscar la salida de audio", "Opciones", "sonido dispositivo auriculares parlantes reintentar", "", RetryAudio, () => !AudioReady, "La salida de audio ya funciona."),
        new("ai", "Conectar o configurar IA…", "Opciones", "modelo proveedor conexion", "", () => AiConnectionWindow.Open(_ai)),
        new("contrast", "Activar o desactivar alto contraste", "Opciones", "accesibilidad lectura colores", "", () => SetHighContrast(!_prefs.HighContrast)),
        new("fullscreen", "Alternar pantalla completa", "Opciones", "ventana tamaño", "F11", ToggleFullscreen),
        new("updates", "Buscar actualizaciones…", "Ayuda", "version programa", "", _updates.Open),
        new("news", "Ver novedades…", "Ayuda", "nuevo cambios versiones actualizacion", "", OpenNews)
    ];
}
