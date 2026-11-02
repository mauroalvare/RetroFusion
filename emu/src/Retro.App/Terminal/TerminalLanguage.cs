namespace Retro.App;

/// <summary>Visible vocabulary changes; handlers, saved identifiers and Spanish scripts keep their stable contract.</summary>
internal static class TerminalLanguage
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ayuda"]="help", ["como"]="how", ["limpiar"]="clear", ["historial"]="history",
        ["proyectos"]="projects", ["proyecto"]="project", ["validar"]="validate", ["retomar-trabajo"]="resume-work", ["preparar"]="prepare",
        ["tareas"]="tasks", ["tarea"]="task", ["seguir"]="follow", ["cancelar"]="cancel", ["retomar"]="resume-task",
        ["jugar"]="play", ["abrir"]="open", ["continuar"]="continue", ["pausa"]="pause", ["reiniciar"]="reset",
        ["captura"]="capture", ["ranuras"]="slots", ["guardar"]="save", ["cargar"]="load", ["rebobinar"]="rewind", ["entradas"]="inputs",
        ["turbo"]="turbo", ["cinta"]="tape", ["detener"]="stop", ["controles"]="controls", ["maquinas"]="machines", ["maquina"]="machine",
        ["crear"]="create", ["fusionar"]="fuse", ["registros"]="registers", ["memoria"]="memory", ["escribir"]="write", ["codigo"]="code",
        ["paso"]="step", ["correr"]="run", ["bp"]="bp", ["rastrear"]="scan-memory", ["variables"]="variables", ["cd"]="cd", ["pwd"]="pwd", ["ls"]="ls",
        ["secciones"]="sections", ["juegos"]="games", ["buscar"]="search", ["filtrar"]="filter", ["filtros"]="filters", ["ordenar"]="sort",
        ["ficha"]="info", ["corregir"]="correct", ["favorito"]="favorite", ["favoritos"]="favorites", ["recientes"]="recent",
        ["identificar"]="identify", ["omitir"]="reject", ["candidatos"]="candidates", ["candidato"]="candidate", ["carpeta"]="folder", ["escanear"]="rescan",
        ["estado"]="status", ["opciones"]="options", ["opcion"]="option", ["interfaz"]="interface", ["acciones"]="actions", ["hacer"]="do",
        ["actualizar"]="update", ["diagnostico"]="diagnostics", ["copiar"]="copy", ["atajos"]="shortcuts", ["salir"]="quit", ["novedades"]="news"
    };
    private static readonly IReadOnlyDictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["todos"]="all", ["originales"]="originals", ["fusionados"]="fusions", ["creados"]="created", ["favoritos"]="favorites", ["recientes"]="recent",
        ["nombre"]="name", ["genero"]="genre", ["año"]="year", ["ano"]="year", ["sistema"]="system", ["tipo"]="type", ["disponible"]="available",
        ["actividad"]="activity", ["ninguno"]="none", ["fecha"]="date", ["asc"]="asc", ["desc"]="desc", ["limpiar"]="clear", ["borrar"]="clear",
        ["abrir"]="open", ["cerrar"]="close", ["estado"]="status", ["iniciar"]="start", ["grabar"]="record", ["detener"]="stop", ["reproducir"]="replay",
        ["activar"]="enable", ["desactivar"]="disable", ["restablecer"]="reset", ["si"]="yes", ["no"]="no", ["todas"]="all",
        ["rebobinar"]="rewind", ["reiniciar"]="reset", ["biblioteca"]="library", ["inicio"]="home", ["texto"]="text", ["pantalla"]="screen",
        ["partida"]="play", ["variables"]="variables", ["cambios"]="changes", ["anterior"]="previous", ["siguiente"]="next"
        , ["duro"]="hard", ["reemplazar"]="replace", ["terminar"]="finish", ["expulsar"]="eject", ["cuadro"]="frame", ["saltar"]="over",
        ["lista"]="list", ["agregar"]="add", ["quitar"]="remove", ["igual"]="equal", ["cambio"]="changed", ["igual-que-antes"]="unchanged",
        ["subio"]="increased", ["bajo"]="decreased", ["reintentar"]="retry", ["aceptar"]="accept", ["rechazar"]="reject",
        ["ver"]="view", ["copiar"]="copy", ["ultimo"]="last", ["todo"]="all", ["orden"]="order", ["disponibles"]="available",
        ["nombre-desc"]="name-desc", ["año-desc"]="year-desc", ["agregados"]="added", ["buscar"]="search", ["vaciar"]="clear",
        ["creado"]="created", ["inspirado"]="inspired", ["sin"]="unknown", ["nunca"]="never", ["agregados7"]="added7", ["agregados30"]="added30",
        ["fusiones"]="fusions", ["herramientas"]="tools", ["opciones"]="options", ["ayuda"]="help", ["barra"]="bar", ["subrayado"]="underline"
    };
    private static readonly IReadOnlyDictionary<string, string> Options = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["idioma"]="language", ["interfaz"]="interface", ["texto"]="text-size", ["contraste"]="contrast", ["consulta-fichas"]="online-metadata",
        ["pantalla-completa"]="fullscreen", ["escala-entera"]="integer-scale", ["fps"]="fps", ["joystick-zx"]="zx-joystick", ["puerto-c64"]="c64-port",
        ["load-cintas"]="tape-load", ["turbo-carga"]="loading-turbo", ["ahorro"]="power-saving", ["pausa-fondo"]="background-pause",
        ["consola-cursor"]="console-cursor", ["consola-parpadeo"]="console-blink", ["consola-crt"]="console-scanlines", ["consola-historial"]="console-history",
        ["volumen"]="volume", ["silencio"]="mute", ["silencio-fondo"]="background-mute", ["buffer-audio"]="audio-buffer"
    };
    public static string Name(string canonical) => UiText.English ? Names.GetValueOrDefault(canonical, canonical) : canonical;
    public static string Choice(string canonical) => UiText.English ? Values.GetValueOrDefault(canonical, canonical) : canonical;
    public static string Option(string canonical) => UiText.English ? Options.GetValueOrDefault(canonical, canonical) : canonical;
    public static string CanonicalOption(string input) => Options.FirstOrDefault(pair => pair.Value == input).Key ?? input;
    public static string CanonicalChoice(string input, IReadOnlyList<string> allowed)
    {
        if (input.Equals("hardcore", StringComparison.OrdinalIgnoreCase) && allowed.Contains("real")) return "real";
        string folded = CommandLine.Fold(input);
        // Accepted canonical words keep their spelling, as in the original interpreter contract.
        if (allowed.Any(value => CommandLine.Fold(value) == folded)) return input;
        return allowed.FirstOrDefault(value =>
            CommandLine.Fold(Values.GetValueOrDefault(value, value)) == folded || CommandLine.Fold(UiText.Get(value)) == folded ||
            value == "reproducir" && folded == "play") ?? input;
    }
    public static string Example(string line) => UiText.English ? string.Join(' ', line.Split(' ').Select((word, index) => index == 0 ? Name(word) : Options.GetValueOrDefault(word, Values.GetValueOrDefault(word, word)))) : line;
}
