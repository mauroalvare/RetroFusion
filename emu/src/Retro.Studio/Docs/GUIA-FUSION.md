# Método para fusionar juegos y que salga un juego jugable

Este método sirve para cualquier combinación de juegos (2, 3 o más) y para cualquier IA o
agente. Seguilo en orden. El error más común es poner un poco de un juego y un poco del
otro, uno encima del otro: eso no es un juego, es ruido. Una fusión buena se juega como
**un solo juego** que alguien que nunca vio los originales entiende en 10 segundos.

## 1. Entender cada juego

Para cada juego, anotá en una línea:

- **Verbo principal:** qué hace el jugador todo el tiempo (disparar, saltar, esquivar, juntar, manejar).
- **Objetivo:** cómo se gana o se avanza (limpiar la oleada, juntar las llaves y salir).
- **Amenazas:** qué te mata.
- **Recurso o tensión:** qué te apura (aire, tiempo, munición, combustible).
- **Lo más reconocible:** el personaje, los enemigos, un sonido, un gráfico.

## 2. Elegir UN juego base y UN héroe

- El **juego base** inspira el bucle principal (lo que hacés cada segundo) y los
  controles como referencia de diseño; el guion dibuja una escena propia con sus piezas. Normalmente es el más simple de entender.
- El **héroe** es el personaje que maneja el jugador. Puede venir de otro juego (con su
  sprite y su habilidad característica): eso suele ser lo más divertido.
- Los controles siguen las costumbres del género del juego base: en un juego de disparos,
  **Espacio y Ctrl disparan** (en el guion, `input.jump` y `input.fire` disparan los dos);
  en uno de plataformas, Espacio y flecha arriba saltan. Como máximo 3 acciones. Si hay
  disparo y salto a la vez, uno va a la flecha arriba.

## 3. Elegir de 1 a 3 aportes de los otros juegos

Un **aporte** es una pieza de otro juego que **cambia una decisión del jugador** dentro
del bucle base. Cada aporte tiene que responder: "¿qué hace distinto el jugador gracias a
esto?". Si no cambia nada, sacalo.

Patrones de aportes que funcionan:

| Patrón | Ejemplo |
|---|---|
| Héroe trasplantado | El minero de un juego de plataformas reemplaza a la nave: ahora puede saltar a plataformas. |
| Recurso injertado | El aire o el combustible de un juego pasa a ser la presión del otro: si no atacás, te ahogás. |
| Terreno que cumple otra función | Las plataformas que se rompen de un juego son los escudos del otro: te protegen de arriba pero se desgastan. |
| Enemigo invitado | UN tipo de enemigo de otro juego aparece en el base con una regla clara (patrulla el piso, hay que saltarlo). |
| Premio invitado | El objeto que se junta en un juego da un poder en el otro (disparo triple por unos segundos). |
| Máquinas conectadas | Una variable del original cambia una amenaza o un recurso en la escena nueva. |
| Mundo que se transforma | El escenario propio cambia entre niveles con piezas de los originales y una transición clara. |

Escribí también **qué se descarta** de cada juego (enemigos, trampas, reglas). Todo lo que
no esté en la lista de aportes, se descarta. Una trampa que el jugador no puede reconocer
(una planta que mata y nadie avisó) arruina el juego.

## 4. Reglas claras

- **Objetivo** en una frase y **derrota** en una frase.
- **Vidas** (o lo que haga de vida) y **puntaje**: el jugador tiene que poder saber cuánto le
  queda y cómo le va, pero la forma de mostrarlo es libre.
- **Progresión:** oleadas o niveles que empiezan fáciles y se ponen más difíciles.
- **Dificultad inicial baja:** menos enemigos y más lentos que en el original al
  principio. Si traés enemigos de un juego, traé **pocos** (una fila, no una pared).
- **Justicia:** el jugador siempre tiene una manera de evitar cada peligro (moverse,
  saltar, cubrirse, dispararle). Cajas de choque un poco más chicas que el sprite. Medio
  segundo de invulnerabilidad al reaparecer.
- **Recompensa:** cada cosa buena que hace el jugador tiene respuesta: sonido, explosión,
  puntos, mensaje.

## 5. Mundo, niveles e historia

Un juego se siente como un juego (y no como una mezcla al azar) cuando tiene un mundo
propio y un camino:

- **Premisa (historia):** 2 o 3 frases que expliquen por qué se cruzan los mundos y qué
  quiere el héroe ("una flota invasora se posó sobre las minas; Willy tiene que juntar las
  llaves y escapar antes de que aterrice"). Va en el título y le da sentido a cada regla.
- **Niveles:** al menos 3, con mapas distintos. Si un perfil ofrece los niveles originales
  (por ejemplo `g.cavernData(n)` en Manic Miner), usalos: son mapas diseñados y probados.
  Si no, armá mapas propios con los mosaicos o sprites de los juegos.
- **Una novedad por nivel:** cada nivel presenta UNA cosa nueva (un enemigo, una mecánica,
  más velocidad) y la anuncia con un cartel. Nunca todo junto en el primer nivel.
- **Presentar cada nivel:** el jugador se entera del nivel nuevo y de su novedad con una
  forma propia de este juego (una tarjeta, un mapa de progreso, una viñeta, un narrador, un
  cambio de paleta), con un momento de calma antes de que empiece.
- **Final:** qué pasa al terminar el último nivel (pantalla de victoria) o, si es infinito,
  cómo sigue subiendo la dificultad.
- **Puntaje con sentido:** puntos por lo que importa (enemigos, objetos, terminar un nivel,
  tiempo o recurso sobrante) y un récord visible.

## 6. Legibilidad (que se entienda la pantalla)

- **Una escena propia obligatoria.** El modelo es **Manic Invaders**: lee cavernas,
  sprites y variables de los originales y dibuja su propio mundo. Nunca muestra sus
  pantallas. `screen.game` se retiró y hace fallar la prueba. Tampoco reconstruyas las
  pantallas con recortes, miniaturas o `g.pixel`; dibujá tiles y sprites con reglas nuevas.
- Los enemigos y proyectiles **no se dibujan encima de las plataformas o del terreno**:
  reservá zonas (los enemigos arriba, el terreno abajo) o mové el terreno.
- **Cada peligro con un color o forma propia**, distinto del héroe y del terreno. El héroe
  siempre visible.
- Como máximo unos 25 enemigos y 4 proyectiles enemigos en pantalla a la vez.
- **Presentación propia:** el HUD, la pantalla de título, los menús y las transiciones se
  diseñan para ESTE juego (un panel lateral, la información dentro del mundo, colores en vez
  de números, un tablero, un narrador...). No uses siempre el mismo esquema de puntaje
  arriba y barra abajo: dos fusiones no deberían parecerse al mirarlas.
- **Mensajes cortos** cuando algo nuevo aparece por primera vez ("¡Cuidado: el guardián
  patrulla el piso, saltalo!").

## 7. Estructura obligatoria del guion

El guion tiene que avisar en qué parte está con `game.state(...)`, e informar lo que pasa
con `game.score`, `game.death` y `game.level`: así se puede probar automáticamente.

1. **Título** (`game.state('title')`): nombre, objetivo y controles en pantalla. Los juegos
   en pausa. Empieza con start, Espacio o disparo. La forma es libre: animado, jugable, una
   viñeta, un menú propio.
2. **Jugando** (`game.state('playing')`).
3. **Fin** (`game.state('gameover')`): puntaje final y "start para jugar de nuevo". Al
   apretar start, todo vuelve a empezar (`g.restart()` en cada juego, variables del guion
   reiniciadas) y se pasa a jugando o al título.
4. Cada vez que el jugador suma puntos: `game.score(puntos)`. Cada vez que pierde una vida:
   `game.death('causa')` (ej. 'bomba', 'guardián', 'sin aire'). Al pasar de nivel u oleada:
   `game.level(n)`.

## 8. Usar los juegos originales con criterio

- Lo que el juego base hace bien (la formación que acelera, la física, la música)
  **dejalo correr de verdad** y conectalo; no lo reprogrames.
- Los otros juegos pueden aportar solo **gráficos y sonido** (sus sprites, sus mosaicos)
  y estar en pausa: no hace falta que corran si no aportan comportamiento. Pausados y con
  volumen 0 no molestan.
- Si el original hace algo que rompe tu diseño (mata al personaje oculto, frena todo),
  controlalo desde el guion: ayudas del perfil, `poke`, pausar, reiniciar.
- Convertí coordenadas con UNA función (`toScene(o)`) y usala igual para dibujar y para
  los choques.

## 9. Probar y ajustar con números

La prueba automática juega con tres robots (uno quieto, uno que camina y salta al azar,
uno que dispara sin parar) y mide:

- Si hay título, si start empieza el juego y si después del fin se puede volver a jugar.
- Cuánto tarda cada robot en morir por primera vez, de qué muere y cuántos puntos suma.

Valores sanos para un juego de acción: los robots activos sobreviven en promedio **más de 8
segundos**; el que dispara **suma puntos**; ninguna causa de muerte explica casi todas
las muertes (si todas son "bomba", hay demasiadas bombas).

Los robots juegan al azar: no suben plataformas ni buscan llaves. Si el peligro está donde
ellos no llegan, la prueba puede decir "nadie perdió una vida" aunque arriba haya tensión.
En ese caso probá también la parte difícil a propósito (por ejemplo, poniendo al héroe ahí
al empezar en una copia del guion) y mirá las capturas.

**Buscá estrategias que rompan el juego:** si quedarse quieto en un lugar seguro es la
mejor jugada (disparar desde abajo de un piso sin riesgo para siempre), falta una presión
que obligue a moverse. Lo mejor es tener dos presiones, cada una resuelta con el verbo de
un juego distinto (en Manic Invaders el aire solo vuelve con las llaves, que están donde
caen las bombas, y disparar frena a la flota que baja).

Cuidado con lo que el original hace "bien" pero rompe la fusión: el último invasor de
Space Invaders es rapidísimo; en una pantalla más chica aterriza enseguida. Ajustalo
(`g.speed`) para que sea justo. Si la prueba dice que es muy
difícil, bajá cantidad y velocidad de enemigos (`g.speed`), agrandá los huecos, sumá
cobertura. Si nadie suma puntos, revisá que los choques y los disparos funcionen.

Mirá también las capturas: ¿se distingue el héroe? ¿se entiende qué te mata?

## 10. Lista de control (todo tiene que ser "sí")

- [ ] Se entiende como UN juego: hay un juego base, un héroe y de 1 a 3 aportes.
- [ ] Cada aporte cambia una decisión del jugador.
- [ ] Todo lo que no es aporte se descartó (no quedan trampas sin explicar).
- [ ] Objetivo y derrota claros, vidas, puntaje, progresión.
- [ ] Hay premisa (historia), al menos 3 niveles con mapas distintos, una novedad por nivel y un final.
- [ ] La presentación (título, HUD, menús, transiciones) es propia de este juego, no la de otra fusión.
- [ ] Empieza fácil: pocos enemigos y lentos.
- [ ] Controles del género del juego base, como máximo 3 acciones, explicados en el título.
- [ ] Nada se dibuja encima del terreno; cada peligro tiene color propio.
- [ ] Título → jugando → fin → revancha, con `game.state`, `game.score`, `game.death`, `game.level`.
- [ ] La prueba automática pasa sin problemas.

## Anti-patrones (no hacer)

- Dos juegos enteros corriendo a la vez y uno encima del otro.
- Traer todos los enemigos del otro juego (una pared de 55 invasores que tapa el nivel).
- Peligros del juego original que no se ven venir o no se explican.
- Disparos enemigos constantes sin cobertura ni manera de esquivarlos.
- Controles que contradicen al género (en un juego de naves, Espacio no dispara).
- Ganar puntos sin acción del jugador, o no poder ganarlos nunca.
- Que el jugador no sepa qué hacer al empezar.
- Repetir la presentación de otra fusión (el mismo título, PUNTOS y RECORD arriba, la misma
  barra de recurso) o hacer siempre el mismo tipo de juego con los mismos juegos.

## Si a un juego le falta información

Las ayudas del perfil (`g.willy()`, `g.aliens()`...) son la manera segura de saber dónde
está cada cosa. Si hace falta algo que el perfil no tiene, se encuentra con el buscador de
memoria del emulador (Código > Buscar variables) y se verifica dibujando cajas sobre
capturas antes de usarlo. Nunca adivines una dirección de memoria.
