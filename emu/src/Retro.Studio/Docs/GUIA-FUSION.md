# Método para inventar una fusión única y jugable

Fusionar significa crear una regla de juego que nace del encuentro de los originales.
No alcanza con cambiar sprites, juntar enemigos o decorar un juego con elementos del otro.
La misma pareja puede dar juegos completamente diferentes: no existe una receta universal.

## 1. Entender qué hace especial a cada original

Para cada juego identificá su verbo, objetivo, riesgo, recurso y una regla reconocible.
Buscá relaciones entre esas reglas, no solo parecidos visuales. Un laberinto puede cambiar
la trayectoria de disparos; robar un objeto puede cambiar quién persigue a quién; destruir
enemigos puede abrir rutas que cambian cómo se combate. Son posibilidades, no moldes.

## 2. Encontrar el punto de unión

Proponé una **regla inseparable**: explicá cómo una acción procedente de un juego transforma
las decisiones del otro. Escribí un ejemplo concreto de diez segundos de partida.

- **Prueba de quitar un original:** si quitás cualquiera de los juegos y el bucle principal
  sigue siendo el mismo, falta integración. Un aporte solo gráfico o sonoro no alcanza.
- Puede haber un juego base, una mezcla equilibrada o un género nuevo. No impongas siempre
  base + héroe invitado + recurso. En `baseGame` podés escribir "ninguno dominante".
- Elegí un protagonista, varios, un tablero o un sistema según lo que se juega. No todos
  los diseños necesitan un héroe, vidas, disparos, plataformas o enemigos.
- Conservá lo reconocible de cada original, transformá sus reglas y explicá qué descartás.
  Nunca inventes direcciones de memoria ni capacidades que los perfiles no ofrecen.

## 3. Diferenciar las propuestas y las versiones existentes

Compará tus ideas con las fusiones anteriores incluidas en el pedido. Cambiar nombre,
color, cartel o posición del HUD no constituye una idea distinta. Variá el bucle de
decisiones, el objetivo, la organización del espacio o la relación entre los juegos.
Si el jugador pide una corrección pequeña, conservá su juego; si pide una fusión nueva,
justificá en qué cambia realmente.

Para cada propuesta describí la **unión mecánica**, su **regla única**, su **identidad**
visual y jugable y la **diferencia** frente a las otras ideas y fusiones previas.

## 4. Diseñar la partida y su presentación juntos

Definí objetivo, controles y condición de final adecuados al género. La progresión puede
ser una sala que evoluciona, rondas, un recorrido continuo, un mapa, una campaña o una
partida breve. No hay una cantidad obligatoria de niveles ni una plantilla de menús.

El título puede integrarse en el escenario, ser jugable o aparecer de forma breve. Una
transición puede ocurrir dentro del mundo. Los avisos pueden ser animaciones, sonidos,
cambios en el entorno o instrucciones contextuales. No detengas cada novedad con un cartel.
El HUD puede ser lateral, mínimo, diegético o no hacer falta: mostrale al jugador solo la
información que necesita, en el lugar y momento que le sirva.

Los mensajes del emulador y los estados para la prueba automática **no son un menú de
juego obligatorio**. Cada fusión inventa su lenguaje visual, sus pantallas y su ritmo.

## 5. Escena propia y controles claros

- Dibujá una **escena propia** con sprites, tiles y datos originales. Manic Invaders sirve
  como referencia de integración técnica, no como plantilla de género, menú o estructura.
  `screen.game` está retirado: no uses pantallas completas, miniaturas ni reconstrucciones
  de la imagen original con `g.pixel`; esa función sirve para observar, no para copiar cuadros.
- Mantené legibles el espacio, las amenazas y los elementos interactivos. No hay una
  distribución obligatoria de enemigos arriba y plataformas abajo: resolvelo según este
  diseño, sin ocultar al jugador ni las amenazas detrás del terreno.
- Usá pocos controles y explicalos dentro de la presentación elegida. En un juego de
  disparos, Espacio/Ctrl disparan; si hay salto y disparo, diferenciá las acciones.
- No copies título, carteles, HUD, mensajes, paleta o navegación de otra fusión como un
  conjunto. No basta con recolorearlos.

## 6. Instrumentación para poder probarlo

El guion informa `game.state('title')`, `game.state('playing')` y `game.state('gameover')`,
además de `game.score`, `game.death` y `game.level` cuando corresponde. Es el contrato de
la prueba, no una orden de dibujar tres pantallas, tres niveles o un marcador convencional.

El estado inicial permite entender objetivo y controles y empezar con start, Espacio o
disparo; puede ser una introducción integrada en el mundo. El fin permite reiniciar con
start, restaurando variables y los juegos originales que se usen. El contador de nivel
puede representar una fase o ronda; no exige tarjetas entre ellas.

## 7. Usar las máquinas originales con criterio

Reutilizá comportamientos originales cuando suman al diseño; el guion los conecta y puede
transformar sus consecuencias. Un original puede estar pausado si se usan sus datos o
recursos para implementar su regla dentro de la nueva escena. Cada juego debe aportar
una mecánica identificable aunque no haga falta ejecutar su pantalla todo el tiempo.

Controlá efectos ocultos que rompan la partida: pausar, reiniciar, ajustar velocidad y
volumen o usar ayudas del perfil. Convertí coordenadas con una sola función y usala para
dibujar y para los choques. Si falta un dato, indicá la limitación; no lo adivines.

## 8. Justicia, aprendizaje y prueba

Empezá con una situación comprensible y un riesgo que se pueda evitar. Enseñá una novedad
por vez mediante la forma que tenga sentido en este juego. Evitá trampas invisibles,
reapariciones mortales y estrategias que ganen sin tomar decisiones.

Los robots quieto, caminante y tirador ayudan a detectar fallos, pero no juzgan creatividad
ni entienden todos los géneros. Interpretá el informe según el diseño: un puzle puede no
tener disparos y una carrera puede no tener muertes. Explicá los límites de la prueba y
probá a propósito las decisiones difíciles; no conviertas todo en un shooter para aprobar.

Mirá las capturas y verificá controles, progreso, final y reinicio. Si el jugador no puede
entender la regla nueva, simplificá o enseñala mejor antes de sumar más elementos.

## Lista de control

- [ ] Quitar cualquiera de los originales cambia una decisión central, no solo su aspecto.
- [ ] Puedo describir la regla única y diez segundos concretos de partida.
- [ ] La propuesta cambia el bucle o la relación entre juegos respecto de las anteriores.
- [ ] Menús, HUD, avisos y progresión nacen de este diseño, sin una plantilla universal.
- [ ] La escena es propia y no reconstruye pantallas originales.
- [ ] Controles, objetivo y final se entienden; se puede reiniciar.
- [ ] Los estados e informes permiten probar el juego, sin dictar su presentación.
- [ ] Revisé jugabilidad y capturas; pasar el validador no demuestra calidad creativa.

## Anti-patrones

- Superponer juegos enteros o alternarlos sin una regla que los vincule.
- Un juego intacto con el héroe, el color o los enemigos del otro como adorno.
- Repetir base + héroe invitado + tres niveles + los mismos carteles en cada fusión.
- Llamar "única" a una variante que solo cambia textos, colores o posiciones del HUD.
- Usar el ejemplo técnico como guion inicial para cambiarle nombres.
- Inventar datos de memoria, esconder peligros o declarar calidad creativa solo por aprobar robots.
