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
        "la pantalla se divide, gira o cambia de tamaño según lo que pasa", "estética de monitor de fósforo verde o ámbar",
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
        "cada muerte deja un obstáculo o una ayuda para el intento siguiente", "a mitad de nivel los juegos intercambian protagonistas",
        "lo que destruís en un juego aparece en el otro", "el héroe no puede atacar: solo esquivar, engañar o empujar",
        "hay que perder a propósito algo para ganar otra cosa", "los enemigos aprenden: repiten lo que hiciste en el nivel anterior",
        "el escenario es el enemigo: se achica, se inunda o se apaga", "dos objetivos que se contradicen y hay que elegir",
    };

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
        sb.AppendLine("Cada idea inventa su propia presentación (pantalla de título, HUD, menús, transiciones y paleta): nada de copiar la del ejemplo " +
                      "ni repetir la de otra idea.");
        return sb.ToString();
    }
}
