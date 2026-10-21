# API de los juegos fusionados (RetroEmu)

Un juego fusionado es un guion de JavaScript que corre encima de juegos de 8 bits
**emulados de verdad**. Cada juego original sigue funcionando con su propio código (sus
enemigos, su física, sus niveles, su sonido). El guion los conecta: lee y escribe su
memoria, aprieta sus teclas, los pausa o acelera, y arma **una sola pantalla** con partes
de sus datos, tiles y sprites originales. La unión tiene que afectar las decisiones del
jugador; un original no puede quedar reducido a decoración.

El guion es JavaScript estándar (ES2020, sin módulos ni `import`, sin acceso a archivos ni
a internet). Cada llamada a `tick` o `draw` tiene un límite de tiempo: nada de bucles
infinitos.

## Ciclo de vida

Definí estas funciones (todas opcionales):

```js
function init() { }          // una vez, con los juegos ya arrancados y jugando
function tick(input) { }     // 60 veces por segundo, después de que los juegos avanzan
function draw() { }          // después de cada tick: dibujá toda la pantalla
```

`input` trae lo que aprieta el jugador: `input.left`, `right`, `up`, `down`, `fire`,
`jump`, `start`, `coin` (true mientras está apretado) y `input.pressed.fire`, etc. (true
solo en el tick en que se apretó). `input.tick` cuenta los ticks desde el arranque.

En el teclado: flechas, Ctrl o Z = fire, Espacio o X = jump, Enter o 1 = start, 5 o C = coin.
Con gamepad también.

**Los juegos no reciben las teclas del jugador solos:** el guion decide qué tecla de qué
juego aprieta cada acción, con `g.control(...)`.

## Los juegos: `games.a`, `games.b`, `games.c`

Cada juego (por la ranura de la receta) es un objeto `g`:

| Miembro | Qué hace |
|---|---|
| `g.name`, `g.profile`, `g.machine` | Nombre, id del perfil, máquina (`zx48`, `invaders`, `c64`) |
| `g.area` | `{x, y, w, h}`: dónde está la imagen del juego dentro de su cuadro (sin el borde del Spectrum ni del C64: en el C64 son los 320x200 del medio de un cuadro de 384x272). Las coordenadas de las ayudas del perfil son relativas a esta área. |
| `g.control(nombre, apretado)` | Aprieta o suelta un control del perfil (`left`, `right`, `jump`, `fire`, `start`...) o una tecla de la máquina (`'P'`, `'SPACE'`, `'P1 FIRE'`...). Queda apretado hasta que lo sueltes. |
| `g.tap(nombre, cuadros)` | Aprieta un control durante N cuadros del juego y lo suelta solo. |
| `g.get(variable)`, `g.set(variable, valor)` | Lee o escribe una variable del perfil (vidas, puntaje...). Las de solo lectura no se escriben. |
| `g.peek(dir)`, `g.peek16(dir)`, `g.poke(dir, byte)` | Memoria directa (dirección numérica, ej. `0x806B`). |
| `g.pause()`, `g.resume()`, `g.paused` | Congela el juego (su imagen queda quieta) o lo deja seguir. |
| `g.speed = 0.5` | Velocidad de emulación (0 a 4). Cámara lenta, turbo. |
| `g.volume = 0.3` | Volumen de su sonido (0 a 2). |
| `g.restart()` | Vuelve el juego al momento en que arrancó la partida. |
| `g.pixel(x, y)` | Color 0xRRGGBB de un píxel de su cuadro (coordenadas del cuadro completo: sumá `g.area.x`/`y`). Sirve para detectar plataformas o paredes por color. |
| `g.sprite(nombre)` | Un sprite original del juego (ver perfil). Devuelve un número para `screen.sprite`. Pedilos en `init()` y guardalos. |
| `g.spriteAt(dir, ancho, alto, 'rows'|'columns')` | Un sprite desde cualquier dirección de memoria. |
| `g.frame` | Cuadros emulados desde el arranque. |
| Ayudas del perfil | Funciones como `g.willy()` o `g.aliens()`: cada perfil documenta las suyas. Usalas antes que adivinar direcciones. |

## La pantalla: `screen`

Todo se dibuja de nuevo en cada `draw()`. Colores 0xRRGGBB.

| Función | Qué hace |
|---|---|
| `screen.size(ancho, alto)` | Tamaño de la pantalla del juego fusionado (en `init`; por defecto 256x192, máximo 640x480). |
| `screen.width`, `screen.height` | Tamaño actual. |
| `screen.base(g)` | Dibuja la imagen nativa del único original elegido en `baseSlot`, sin borde ni cambio de paleta o resolución. Llamalo primero en `draw()` e integrá después los elementos del otro juego. No admite un segundo original ni funciona en reinterpretaciones libres. Las coordenadas quedan relativas al área activa del base. |
| `screen.clear(color)` | Pinta todo. |
| `screen.sprite(s, x, y, color, {scale, flip, alpha})` | Dibuja un sprite (1 bit) en un color. `flip` lo espeja. |
| `screen.spriteSize(s)` | `{w, h}` del sprite. |
| `screen.rect(x, y, w, h, color, alpha)` | Rectángulo lleno. |
| `screen.box(x, y, w, h, color)` | Rectángulo vacío. |
| `screen.line(x0, y0, x1, y1, color)` | Línea. |
| `screen.text(texto, x, y, color, {scale, center})` | Texto con la letra del ZX Spectrum (8x8 píxeles por letra). Con `center: true`, `x` es el centro. |

## El estado del juego: `game` (obligatorio)

Estos estados son instrumentación: no obligan a usar el mismo menú, carteles, HUD o
estructura de niveles en todas las fusiones. Una introducción puede estar integrada en
el mundo; un nivel puede representar una ronda o fase sin una tarjeta de transición.
Con `baseSlot` declarado podés empezar directamente con `game.state('playing')` y
conservar el arranque y la interfaz del original. No agregues un título ni puntaje
artificial para pasar la prueba. Informá los puntos y objetivos reales cuando existan.

La prueba automática y la ventana del emulador necesitan saber qué pasa en el juego:

| Función | Cuándo llamarla |
|---|---|
| `game.state('title')` | Al mostrar la pantalla de título (al empezar y cuando se vuelve a ella). |
| `game.state('playing')` | Cuando empieza la partida. |
| `game.state('gameover')` | Al terminar (sin vidas, o después de ganar el último nivel). Desde ahí, start vuelve a empezar. |
| `game.score(puntos)` | Cada vez que el jugador gana puntos. |
| `game.death('causa')` | Cada vez que pierde una vida ('bomba', 'guardián', 'sin aire'...). |
| `game.level(n)` | Al empezar el nivel n (1, 2, 3...). |

## Otros

| Función | Qué hace |
|---|---|
| `ui.message(texto)` | Cartel que aparece unos segundos arriba de la pantalla. |
| `ui.hud(texto)` | Línea de estado fija junto al juego (vidas, nivel, objetivos). |
| `sound.beep(frecuencia, ms, volumen)` | Pitido de onda cuadrada. |
| `sound.noise(ms, volumen)` | Ruido (explosiones). |
| `log(...)` | Escribe en el registro (lo ve quien prueba el guion). |
| `rnd(n)` | Entero al azar de 0 a n-1. |
| `clamp(v, min, max)` | Limita un número. |
| `overlap(a, b)` | true si dos cajas `{x, y, w, h}` se tocan. |

## Cómo hacer una buena fusión

- **Una sola pantalla y un solo objetivo.** No pongas un juego al lado del otro: elegí
  un juego como mundo y traé al otro adentro (sus enemigos, sus disparos, su
  personaje, sus reglas), o armá una pantalla nueva con piezas de los dos.
- **Que los juegos originales hagan el trabajo pesado.** Sus enemigos, movimiento,
  colisiones y progresión ya funcionan: usalos y conectalos,
  no los reprogrames.
- **Convertí coordenadas** entre pantallas con una función (`toWorld(o)`) y usala igual
  para dibujar y para los choques.
- **Choques justos:** cajas un poco más chicas que el sprite.
- **Retroalimentación:** mensajes cortos (`ui.message`), sonidos al pegar y al morir, y un
  `ui.hud` con lo importante.
- **Fin y revancha:** detectá el fin del juego, mostralo en pantalla y dejá empezar de nuevo
  con start (usá `g.restart()` en cada juego y reiniciá tus variables).
- **Dificultad:** si algo resulta imposible o aburrido, ajustá velocidades (`g.speed`),
  frecuencias o premios.
- Textos en castellano. No uses `async`, `setTimeout` ni `import`: todo pasa en `tick`.

## Conservar la base o reinterpretar

Con `baseSlot` declarado, conservá su mundo con `screen.base(g)` o con una reconstrucción
fiel mediante tiles y mapas verificados. Su imagen conserva la resolución y paleta nativas.
Integrá el aporte en ese espacio, con interacción real y controles coherentes.

En reinterpretación libre dibujá la escena con `screen.sprite`, `screen.rect`,
`screen.line` y `screen.text`. Leé sprites y tiles con `g.sprite`/`g.spriteAt`, y mapas
y variables con las ayudas del perfil o `peek`. No hay un diseño visual universal.

`screen.game` se retiró: no dibuja nada y hace fallar la prueba automática con un mensaje
para corregir el guion. La única imagen original completa permitida es la base declarada,
mediante `screen.base(g)`. No uses otras pantallas completas, recortes, miniaturas ni mosaicos de
capturas para reconstruir los originales. `g.pixel` sirve para observar el estado del
juego, no para copiar su pantalla píxel por píxel.
