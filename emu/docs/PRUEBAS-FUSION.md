# Alcance de la auditoría de fusiones

La fusión conserva un original como base; Crear con ROMs produce una escena nueva.
La separación está en el recorrido de la app, los metadatos, las carpetas y el contrato
que recibe el diseñador. La fusión de ejemplo conserva su guion y su categoría anteriores.

## Problemas encontrados y correcciones

| Problema | Corrección | Alcance comprobado |
|---|---|---|
| Elegir una base no impedía sustituir toda su escena. | Composición nativa antes de dibujar; resolución conservada y rechazo de borrado. | Casos de borrado, copia y añadidos legítimos. El filtro visual no juzga creatividad. |
| Recursos extraídos sin sus colores originales. | Capturas pequeñas reutilizables con color y transparencia. | Paleta, máscara y presupuesto por original; no extrae reglas ni mapas. |
| Arranques cacheados podían cruzar ediciones. | Huella de bytes, perfil y firmware; caché acotada. | Cambios de edición, perfil y firmware; reutilización y desalojo. |
| La coordenada horizontal de un perfil C64 no seguía al personaje. | Registros de sprite de 9 bits y coordenadas activas verificadas. | Cuatro direcciones contrastadas con la imagen y la edición identificada. |
| Faltaban posiciones de fantasmas y muerte nativa del cañón. | Ayudas de perfil `ghosts()` y `hitCannon()`. | Cuerpos y colores reales; explosión, pérdida de reserva y reaparición originales. |
| Documentación aún sugería una pantalla nueva o HUD adicional en toda fusión. | API alineada con la modalidad y señales opcionales. | Contratos y entradas separados; no se impone un menú de juego. |

## Casos mecánicos reproducibles

`MechanicalFusionTests` ejecuta tres guiones escritos a mano que están en
`tests/Studio.Tests/Fixtures`. Son pruebas de integración, fuera del menú y de las
instrucciones enviadas a la IA. No son juegos terminados ni nuevas generaciones de IA.
Necesitan copias locales de los originales; el repositorio no las incluye.

| Base y aporte | Interacción observada | Comparación |
|---|---|---|
| Base de plataformas y aporte de flota arcade | Los disparos eliminan invasores reales y reponen aire y puntaje de la base. Los píxeles nativos detienen proyectiles. | Con disparo y movimiento cambia la formación; quieto no obtiene esos puntos. |
| Base de flota arcade y aporte de reserva | El cañón dispara con su código original. Los impactos reponen aire; agotarlo activa su muerte nativa. | El recorrido activo acierta y conserva la reserva que pierde el quieto. Resolución vertical, búnkeres y marcador originales. |
| Base de plataformas y aporte de persecución | Una píldora invierte persecución/huida; alcanzar al fantasma repone aire y suma puntos originales. El contacto sin poder mata al personaje de la base. | Recoger y perseguir produce una consecuencia distinta del disparo. Sin moverse no se come el fantasma. |

Cada recorrido dura 15 segundos, usa entradas normales y compara la imagen resultante
con el cuadro original de ese mismo instante: conserva más del 90 % de sus píxeles.
No se fuerza una colisión desde la prueba ni se modifica un archivo de juego. También
se comprueba el reinicio de la flota y sus reservas. Una comparación de píxeles sola
no demostraría integración; se contrasta con cambios de memoria y reglas concretas.

```powershell
dotnet test tests/Studio.Tests/Studio.Tests.csproj -c Release --filter FullyQualifiedName~MechanicalFusionTests
```

## Límites de esta evidencia

Los casos demuestran conexiones viables con los perfiles disponibles y diferencias al
invertir la base o cambiar el aportante. El ensayo de fantasmas usa un corredor concreto;
no resuelve navegación general, mapas adicionales ni una campaña completa.

No demuestran que una IA produzca siempre juegos fieles, originales o divertidos. Esa
evaluación requiere varias generaciones y partidas comparadas, con autorización para
usar el proveedor. Los robots y el filtro visual son comprobadores técnicos.
Spy vs Spy y Pac-Man aún necesitan más datos verificados de mapas, actores y reglas
para algunas transferencias. El diseñador debe declarar los datos esenciales ausentes;
no sustituir la base por un juego genérico para sortear esa limitación.
