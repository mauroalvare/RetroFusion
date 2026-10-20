using System.Text;

namespace Retro.Ai;

/// <summary>
/// A different starting point every time an AI invents a fusion, so the same games with no
/// request from the player do not give the same game twice: a form of play, a presentation
/// (title, HUD, menus), a tone and an unusual rule, drawn at random. The AI may swap a part
/// that does not fit the games, but for something just as surprising, never the obvious.
/// </summary>
public static class CreativeSeeds
{
    public static readonly string[] Forms =
    {
        "carrera contra el tiempo por pantallas", "puzle de acción (pensar rápido, no reflejos)", "sigilo: ganás si no te ven",
        "defensa: algo que proteger, no a vos mismo", "cooperativo para dos jugadores en el mismo teclado", "duelo uno contra uno",
        "exploración con un mapa que se va descubriendo", "supervivencia por oleadas con mejoras entre oleadas",
        "persecución invertida: el jugador es el perseguido", "el jugador es el villano de uno de los juegos",
        "ritmo: todo pasa al compás de la música", "un solo botón", "minijuegos encadenados que cuentan una historia",
        "escape: salir de un lugar que se cierra", "deporte absurdo con las piezas de los juegos", "jefe final que cambia cada vez que lo golpeás",
        "una sola pantalla que se transforma nivel a nivel", "gestión con poco tiempo (repartir, rescatar, construir)",
        "carrera de obstáculos con scroll", "rompecabezas de físicas con los enemigos como piezas",
    };

    public static readonly string[] Presentations =
    {
        "sin HUD: la información está en el mundo (colores, tamaños, sonidos)", "HUD vertical en un costado, como un panel de máquina",
        "pantalla de título que ya es jugable", "menús y mensajes como una terminal de computadora vieja",
        "la historia contada con viñetas o carteles de cine mudo entre niveles", "marcador grande estilo tablero deportivo",
        "transiciones con cortinas, persianas o barridos", "paleta reducida a dos o tres colores por nivel",
        "la escena propia cambia su paleta y su geometría según lo que pasa", "estética de monitor de fósforo verde o ámbar",
        "título minimalista: una palabra gigante que se rompe al empezar", "medidores dibujados con los sprites de los juegos",
        "un narrador que comenta en una línea al pie", "mapa de progreso entre niveles en vez de tarjetas",
    };

    public static readonly string[] Tones =
    {
        "comedia absurda", "terror de clase B", "épica exagerada", "policial negro", "melancólico y tranquilo", "caos de feria",
        "documental falso", "cuento infantil", "parodia de la propia época de los juegos", "ciencia ficción burocrática",
    };

    public static readonly string[] Twists =
    {
        "los roles se invierten: controlás a los enemigos de uno de los juegos", "el tiempo solo avanza cuando te movés",
        "la gravedad o la orientación cambian de nivel a nivel", "el puntaje también es la vida: gastarlo te salva",
        "cada muerte deja un obstáculo o una ayuda para el intento siguiente", "a mitad de nivel el héroe cambia de habilidad en la escena propia",
        "lo que destruís se convierte en terreno o recursos dentro del mundo nuevo", "el héroe no puede atacar: solo esquivar, engañar o empujar",
        "hay que perder a propósito algo para ganar otra cosa", "los enemigos aprenden: repiten lo que hiciste en el nivel anterior",
        "el escenario es el enemigo: se achica, se inunda o se apaga", "dos objetivos que se contradicen y hay que elegir",
    };

    /// <summary>Kinds of game that fit an 8-bit machine and a single assembly file (for games written from scratch).</summary>
    public static readonly string[] Genres =
    {
        "laberinto con enemigos que patrullan", "plataformas de una sola pantalla", "disparos en pantalla fija", "atrapar lo que cae",
        "esquivar en un túnel que se angosta", "puzle de empujar bloques", "carrera vista desde arriba", "serpiente que crece",
        "rompeladrillos con una vuelta de tuerca", "excavar túneles", "rescate: llevar cosas de un lado a otro de la pantalla",
        "defensa de una base contra oleadas", "salto entre plataformas que se mueven", "pesca o caza contra el reloj",
        "pintar el piso entero mientras te persiguen", "memoria: repetir una secuencia que se alarga",
    };

    /// <summary>Who plays and where, so the same genre never looks like the last game.</summary>
    public static readonly string[] Worlds =
    {
        "un faro en medio de una tormenta", "una oficina de correos en la Luna", "el interior de una computadora vieja", "un hormiguero",
        "una cocina donde la comida se rebela", "una biblioteca embrujada", "un submarino que hace agua", "un circo de pulgas",
        "un volcán que se despierta", "un tren nocturno", "un jardín con topos", "una fábrica de juguetes sin control",
        "un museo de noche", "el fondo del mar con medusas", "una ciudad de nieve que se derrite", "un laboratorio de un científico distraído",
    };

    /// <summary>Unusual rules that need no other game to make sense.</summary>
    public static readonly string[] OriginalTwists =
    {
        "el tiempo solo avanza cuando te movés", "el puntaje también es la vida: gastarlo te salva", "cada muerte deja una ayuda para el intento siguiente",
        "el héroe no puede atacar: solo esquivar, engañar o empujar", "el escenario se achica, se inunda o se apaga", "dos objetivos que se contradicen y hay que elegir",
        "salir por un borde de la pantalla es entrar por el otro", "lo que juntás te hace más lento", "los enemigos solo se mueven cuando no los mirás de frente",
        "una misma acción cambia de función según el estado visible del mundo", "las acciones transforman las rutas disponibles", "el sonido avisa un cambio de estado antes de que ocurra",
    };

    public static readonly string[] OriginalDecisions =
    {
        "arriesgar un recurso limitado para abrir otra posibilidad", "cambiar el espacio para controlar una amenaza",
        "elegir entre avanzar ahora o preparar una ruta segura", "coordinar acciones con efectos que tardan en aparecer",
        "transformar un obstáculo en una herramienta", "administrar espacio sin perder libertad de movimiento",
        "alternar dos usos de la misma acción con un coste visible", "predecir un patrón y decidir cuándo interrumpirlo",
    };

    /// <summary>A seed for a game written from scratch: genre, world, tone and twist.</summary>
    public static string DrawOriginal(Random rng) =>
        $"género: {Pick(rng, Genres)}; protagonista y mundo: {Pick(rng, Worlds)}; tono: {Pick(rng, Tones)}; regla insólita: {Pick(rng, OriginalTwists)}; decisión a explorar: {Pick(rng, OriginalDecisions)}";

    /// <summary>One seed: form, presentation, tone and twist, drawn with <paramref name="rng"/>.</summary>
    public static string Draw(Random rng) =>
        $"forma de juego: {Pick(rng, Forms)}; presentación: {Pick(rng, Presentations)}; tono: {Pick(rng, Tones)}; regla insólita: {Pick(rng, Twists)}";

    private static string Pick(Random rng, string[] list) => list[rng.Next(list.Length)];

    /// <summary>
    /// The creative part of a request: one seed per idea and the fusions that already exist
    /// for these games (to do something else). With a request from the player, it asks to
    /// go even further from the originals.
    /// </summary>
    public static string Brief(Random rng, int ideas, IEnumerable<string> existing, bool playerAsked)
    {
        var sb = new StringBuilder();
        sb.AppendLine(playerAsked
            ? "Creatividad: el pedido del jugador manda; usá las semillas solo si lo hacen más sorprendente. Llevá los juegos más lejos que nunca: " +
              "cambiá su género, su perspectiva o el papel del héroe; que no parezca una fusión que ya viste."
            : "Creatividad: el jugador no pidió nada, así que tiene que sorprenderlo. Cada idea parte de su propia semilla (sorteada esta vez, " +
              "distinta cada vez que se pide). Si una parte de la semilla no encaja con estos juegos, cambiala por otra igual de inesperada, nunca por lo obvio.");
        for (int i = 0; i < ideas; i++)
            sb.AppendLine($"- Semilla {i + 1}: {Draw(rng)}.");
        var list = existing.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
        if (list.Count > 0)
            sb.AppendLine("Ya existen estas fusiones con estos juegos: no las repitas ni copies su presentación (título, HUD, menús): " +
                          string.Join("; ", list) + ".");
        sb.AppendLine("Las semillas son posibilidades, no una receta: la unión mecánica de los originales manda. Cada idea debe cambiar decisiones centrales, " +
                      "no solo el estilo. Aplicá la prueba de quitar un original y describí la regla única. No hay menú ni progresión universal: " +
                      "diseñá presentación, avisos y ritmo para esta idea. Dibujá una escena propia con sprites y tiles, sin copiar la del ejemplo.");
        return sb.ToString();
    }
}
