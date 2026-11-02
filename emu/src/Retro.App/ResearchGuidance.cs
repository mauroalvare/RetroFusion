using System.Text;
using Retro.Studio;

namespace Retro.App;

public sealed record ResearchCheck(string Title, string Needed, IReadOnlyList<string> Steps, string Evidence);

/// <summary>Actionable procedures; they never turn a missing fact or a player's note into verified data.</summary>
public static class ResearchGuidance
{
    public static IReadOnlyList<ResearchCheck> For(GameConcept concept)
    {
        string missing = concept.MissingData.ToLowerInvariant(); var checks = new List<ResearchCheck>();
        bool Has(params string[] words) => words.Any(missing.Contains);
        if (Has("reloj", "tiempo", "contador", "cifra", "timer", "acarreo", "préstamo"))
            checks.Add(new("Reloj y cifras completas", "Comprobar qué bytes forman el contador, su cadencia y sus límites antes de modificarlo.",
            ["Primero usá Analizar > Investigación automática: pedí reloj completo, cadencia, acarreo, préstamo y vencimiento. Elegí tiempo y pedidos; cero pedidos ejecuta pruebas locales. Revisá la evidencia al terminar o continuá con más presupuesto.",
             "Si queda una situación sin reproducir y podés aportarla, abrí el original y guardá una ranura antes de probar: Juego > Partidas guardadas.",
             "En Modificar y analizar > Buscar variables, empezá una búsqueda, dejá cambiar el contador y filtrá con Disminuyó o Es igual a. Repetí varias veces.",
             "Compará también el paso de 9 a 0, el préstamo a la cifra anterior y el vencimiento. Una sola cifra no permite escribir el reloj completo.",
             "Conservá valores antes/después y las entradas de la prueba. Si ensayás una escritura, hacelo en una copia de la partida y restaurá la ranura al terminar."],
            "Direcciones y codificación de todas las cifras, duración observada de cada unidad y resultados de cruces/vencimiento; el laboratorio debe reproducirlos."));
        if (Has("salto", "físic", "fisic", "cámara", "camara", "colis", "geometr", "x/y", "posición", "posicion"))
            checks.Add(new("Movimiento y espacio de juego", "Distinguir el control pulsado del movimiento real, la imagen y la posición física.",
            ["Intentá primero Investigación automática con el objetivo: movimiento real, pulsación corta frente a tecla sostenida, cámara y reaparición. El laboratorio usa copias y conserva las hipótesis que no pudo demostrar.",
             "Si falta alcanzar una situación concreta, guardá un estado en una zona despejada y probá primero una pulsación corta. Repetí desde ese mismo estado manteniendo la tecla.",
             "Observá cuándo empieza y termina el movimiento y cómo cambian las variables propuestas; anotá si el valor representa pantalla, mundo o una copia para dibujar.",
             "Repetí cerca de un borde, con la cámara desplazándose y después de una reaparición. No uses la caja dibujada como caja de colisión sin comprobarla."],
            "Secuencias de entrada repetibles, valores por estado y capturas que permitan refutar la hipótesis. La correlación de una dirección con movimiento no prueba su significado."));
        if (Has("invasor", "bomba", "proyectil", "disparo", "killalien", "formación", "formacion", "impact"))
            checks.Add(new("Actores, disparos e impactos", "Verificar funciones disponibles para esta edición y una interacción cada vez.",
            ["Pedí primero a Investigación automática un experimento por actor: baja, formación, bomba e impacto; incluí el primer disparo, los límites y una nueva oleada. Una meta de cantidad no garantiza esos propósitos.",
             "Revisá las funciones documentadas del original. Las funciones curadas se recuperan sólo si coincide la edición completa.",
             "Desde el mismo estado, compará una corrida sin acción y otra con un disparo o impacto. Registrá el cambio del actor, los recursos y la puntuación.",
             "Comprobá los límites de proyectiles y el retiro de objetos fuera del área de juego, incluyendo la primera acción y una nueva oleada."],
            "Un efecto reproducible por actor y reglas de conteo/límites. Una animación diferente no demuestra una baja ni un impacto físico."));
        if (Has("reinicio", "muerte", "final", "nivel", "estado", "progreso", "transici", "sonido"))
            checks.Add(new("Transiciones y reinicio", "Probar el recorrido completo de la regla y qué se conserva o se reinicia.",
            ["Pedí primero Investigación automática con el objetivo de alcanzar y repetir muerte, nuevo nivel, final y reinicio. Si agota tiempo o pedidos, conserva el avance para continuar; no declara esas transiciones comprobadas.",
             "Si el laboratorio no logra llegar y querés aportar una observación, prepará una secuencia corta que alcance la transición: muerte, nuevo nivel, final o reinicio.",
             "Anotá antes y después las variables, teclas mantenidas, objetos, reloj y sonidos que usa la idea.",
             "Repetí la secuencia dos veces desde la misma ranura. Un reinicio correcto no deja entradas, proyectiles ni contadores de la partida anterior."],
            "Condición de entrada, estado esperado y resultado de dos repeticiones; una aprobación técnica no certifica todas las transiciones."));
        if (checks.Count == 0 && missing.Length > 0)
            checks.Add(new("Comprobación pendiente", concept.MissingData,
            ["Primero usá Investigación automática con el requisito pendiente como objetivo. Elegí tiempo y pedidos; cero pedidos ejecuta pruebas locales. Revisá la evidencia y continuá desde los avances si hace falta.",
             "Si el laboratorio no logra reproducir esa situación y podés aportarla, abrí el original y guardá una ranura antes de probar.",
             "Describí una acción concreta y su resultado esperado. Compará con otra corrida sin esa acción desde la misma ranura.",
             "Registrá entradas, valores y capturas. Agregá la observación a la investigación para que el laboratorio intente reproducirla."],
            "Una diferencia reproducible vinculada al requisito; las observaciones humanas se mantienen sin verificar hasta contrastarlas."));
        return checks;
    }

    public static string Markdown(FusionResearchWork work)
    {
        var text = new StringBuilder(UiText.Format($"# {UiText.Raw(work.Concept.Title)}\n\nInvestigación guardada. Todavía no hay una fusión ejecutable.\n\n"));
        foreach (var original in work.Originals) text.AppendLine(UiText.Format($"- Original {original.Slot}: perfil `{original.Profile}`; SHA-256 del archivo `{original.Fingerprint}`."));
        text.AppendLine(UiText.Get("\n## Qué falta\n\n")+work.Concept.MissingData);
        foreach (var check in For(work.Concept))
        {
            text.AppendLine("\n## "+check.Title+"\n\n"+check.Needed+"\n");
            for (int i=0;i<check.Steps.Count;i++) text.AppendLine(UiText.Format($"{i+1}. {check.Steps[i]}"));
            text.AppendLine(UiText.Get("\nEvidencia necesaria: ")+check.Evidence);
        }
        text.AppendLine("\nLa IA y las pruebas disponibles tienen límites. No se inventan direcciones ni resultados para completar una idea. Conservá los originales y la ranura inicial.");
        if (work.HumanObservation.Length>0) text.AppendLine(UiText.Get("\n## Observación humana sin verificar\n\n")+work.HumanObservation);
        return text.ToString();
    }
}
