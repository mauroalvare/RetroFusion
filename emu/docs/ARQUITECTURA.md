# Arquitectura de RetroEmu

RetroEmu ejecuta el software original (ROMs, cintas, discos, snapshots) sobre CPUs y
hardware emulados. No hay recreaciones: cada instrucción del programa original se
decodifica y ejecuta, y el programa se comunica con el hardware emulado igual que con
el real (puertos, memoria de video, interrupciones, señal de cinta).

## Capas y proyectos

```
src/
  Retro.Core               Interfaces comunes: CPU para el depurador, máquina, memoria,
                           video, audio, breakpoints, apertura de archivos (.zip), PNG.
  Retro.Cpu.Z80            Núcleo Z80 + desensamblador. No sabe nada del Spectrum.
  Retro.Cpu.I8080          Núcleo Intel 8080 + desensamblador.
  Retro.Cpu.M6502          Núcleo MOS 6502/6510 (NMOS, con los opcodes no documentados)
                           + desensamblador.
  Retro.Systems.Spectrum   ZX Spectrum 48K: memoria, ULA, cinta, snapshots, símbolos.
  Retro.Systems.Arcade     Placa Taito/Midway de Space Invaders: video, shifter, sonido.
  Retro.Systems.C64        Commodore 64: VIC-II, SID, dos CIA 6526, bancos del 6510,
                           cintas .t64 y programas .prg, compatibilidad con Open ROMs.
  Retro.Machines           Catálogo de máquinas y formatos, y el lanzador común que arranca una
                           máquina con un juego (app, CLI, fusiones y compatibilidad).
  Retro.Compat             Matriz de compatibilidad reproducible: guiones, ejecutor sin ventana,
                           sonda de controles, informe con evidencia.
  Retro.Fusion             Perfiles de juegos (variables de RAM, controles, arranque),
                           recetas de fusión y la sesión que corre 2 o 3 máquinas a la vez.
  Retro.Ai                 Proveedores de IA (Claude, ChatGPT, Gemini, Ollama), diseñador de recetas con
                           validación, carpeta para agentes, explicador de rutinas.
  Retro.Studio             Juegos fusionados con guion: sesión que corre los juegos y un
                           JavaScript aislado (Jint), lienzo, validador con jugador robot,
                           diseñador con IA (ideas, guion, correcciones, versiones).
  Retro.App                Aplicación de escritorio (raylib + Dear ImGui): pantalla,
                           sonido, teclado/joystick, depurador, selector de archivos,
                           ventanas de fusión y de código.
  Retro.Cli                Ejecutor sin ventana: carga, corre N cuadros, aprieta teclas,
                           guarda capturas PNG y audio WAV, traza instrucciones.
tests/
  Retro.Cpu.Z80.Tests      FUSE (1.356 casos) + ZEXDOC/ZEXALL.
  I8080.Tests              TST8080, 8080PRE, CPUTEST y 8080EXM con ciclos exactos.
  M6502.Tests              SingleStepTests de Tom Harte (256.000 casos, cada ciclo de
                           bus) y la suite funcional de Klaus Dormann.
  C64.Tests                CIA, VIC-II, arranque, SYS, Open ROMs, estados guardados y
                           los juegos reales de la carpeta ROMs (si están).
  Zx.Tests                 Máquina Spectrum y compatibilidad con juegos reales.
  Arcade.Tests             Placa de Space Invaders con un programa de prueba propio,
                           análisis de rutinas.
  Compat.Tests             Ejecutor de compatibilidad con programas propios (sonda, determinismo,
                           fallos, exclusiones, informe).
  Fusion.Tests             Recetas, reglas, diseñador con una IA simulada, fusión con
                           los juegos reales y análisis de código de Manic Miner.
```

Todas las máquinas implementan `IMachine`. La aplicación y el ejecutor sin ventana
trabajan contra esa interfaz, y lo propio de cada sistema (la cinta del Spectrum, los DIP
switches del arcade) aparece solo cuando esa máquina está encendida.

Las dependencias van en una sola dirección: `Cpu.*` y `Systems.*` no dependen de la
interfaz gráfica, así que se pueden probar sin ventana y reutilizar (el mismo núcleo Z80
servirá para el Spectrum 128K, el TK90X, el MSX y el Amstrad CPC).

## Núcleo de CPU: el bus decide el tiempo

El Z80 lleva su contador de T-states (`T`) y emite cada ciclo de bus en el instante en
que ocurre en el chip real:

| Ciclo | Llamadas al bus |
|---|---|
| M1 (lectura de opcode) | `Contention(PC, T)`, +4 T, `ReadMemory` |
| Lectura / escritura | `Contention(dir, T)`, +3 T, `ReadMemory` / `WriteMemory` |
| Ciclo interno con dirección en el bus | `Contention(dir, T)`, +1 T (por ciclo) |
| E/S | `PortTimingPre`, `ReadPort` / `WritePort`, `PortTimingPost` |

Cada máquina implementa `IZ80Bus`. En una máquina sin contención todo devuelve 0 (o
1 + 3 para los puertos). El Spectrum devuelve las demoras reales de la ULA. Así un mismo
núcleo da el timing exacto en cualquier máquina.

Están implementados los comportamientos no documentados que usan los juegos: flags 3 y
5, MEMPTR (WZ), el registro Q que afecta a SCF/CCF, IXH/IXL/IYH/IYL, SLL, las copias a
registro de DDCB, los opcodes ED duplicados, la demora de interrupciones tras EI, HALT y
los modos de interrupción 0, 1 y 2.

## ZX Spectrum 48K

- **Cuadro:** 312 líneas de 224 T-states = 69.888 T a 3,5 MHz, es decir 50,08 Hz. La
  señal /INT dura los primeros 32 T del cuadro.
- **Contención:** los accesos a $4000–$7FFF durante las 192 líneas de pantalla se demoran
  según el patrón 6,5,4,3,2,1,0,0 desde el T-state 14.335. Los puertos siguen las cuatro
  reglas de la ULA, según el byte alto en $40–$7F y el bit 0 del puerto.
- **Video:** el haz se dibuja T-state por T-state. Antes de cada escritura en memoria de
  pantalla o cambio de borde se completa la imagen hasta ese instante. Por eso salen las
  franjas de carga y los efectos de borde y de multicolor. La imagen es de 352×296: borde
  de 48 px a los lados, 48 px arriba y 56 px abajo.
- **Bus flotante:** leer un puerto no conectado devuelve el byte que la ULA está leyendo
  de la memoria de video en ese ciclo.
- **Teclado:** la matriz de 8 medias filas se lee por el puerto $FE, filtrada por el byte
  alto de la dirección. También hay joystick Kempston en el puerto $1F.
- **Sonido:** se integra el nivel de los bits EAR y MIC más la señal de la cinta, cuyos
  flancos se registran al T-state exacto, y se generan muestras a 44,1 kHz con un filtro
  que quita la continua.
- **Placa issue 3:** el bit 6 del puerto $FE refleja la entrada EAR y la salida EAR.

## Intel 8080

El núcleo implementa los 256 opcodes, incluidos los alias no documentados: $08/$10/…/$38
son NOP, $CB es JMP, $D9 es RET y $DD/$ED/$FD son CALL. Usa los estados de cada
instrucción del manual de Intel: CALL condicional 17 si salta y 11 si no, RET condicional
11 o 5. Los flags siguen al chip real:

- el bit 1 vale siempre 1 y los bits 3 y 5 siempre 0;
- el medio acarreo de ANA/ANI es el bit 3 de (A OR operando);
- las restas se hacen como suma del complemento, que es lo que define el AC en SUB, SBB y CMP;
- DAA aplica el ajuste con la suma interna.

`EI` habilita las interrupciones recién después de la instrucción siguiente. Al aceptar
una interrupción se ejecuta la instrucción que la placa pone en el bus de datos: un RST.

## Placa de Space Invaders (Taito/Midway 8080)

- **Relojes:** cristal de 19,968 MHz. La CPU va a 1,9968 MHz (÷10) y el píxel a 4,992 MHz
  (÷4). Cada línea tiene 320 píxeles (128 ciclos de CPU) y hay 262 líneas: 33.536 ciclos
  por cuadro, es decir 59,54 Hz.
- **Memoria:** ROM de 8 KB en $0000 (cuatro chips de 2 KB), RAM de trabajo de 1 KB en
  $2000 y RAM de video de 7 KB en $2400. La RAM se repite cada 16 KB.
- **Interrupciones:** RST 1 cuando el haz llega a la línea 96 y RST 2 en la línea 224
  (comienzo del retrazado). El pedido queda pendiente hasta que la CPU vuelve a habilitar
  las interrupciones. El juego usa las dos para dibujar cada mitad de la pantalla sin
  parpadeo.
- **Video:** 224 líneas de 256 píxeles a 1 bit, con el bit 0 a la izquierda. El monitor
  está montado girado 90° en sentido antihorario: la imagen final es de 224×256. Las líneas
  se dibujan a medida que el haz pasa, con la memoria de ese momento. Encima va la lámina
  de celofán del gabinete: roja en la zona del OVNI, verde en la de las bases y en las
  vidas de la fila inferior.
- **Registro de desplazamiento:** `OUT 4` mete un byte en un registro de 16 bits, `OUT 2`
  fija un corrimiento de 0 a 7 e `IN 3` devuelve la ventana de 8 bits. El juego lo usa
  para mover los sprites píxel a píxel.
- **Entradas:** el puerto 1 tiene la moneda, los botones de inicio y los controles del
  jugador 1. El puerto 2 tiene los DIP switches, el tilt y los controles del jugador 2.
- **Sonido:** circuitos analógicos activados por bits de los puertos 3 y 5 (ver README).
  Los flancos de subida disparan cada efecto en la muestra que corresponde a su ciclo
  dentro del cuadro. Los efectos se sintetizan o, si están disponibles, se reproducen las
  grabaciones de MAME.

## MOS 6502 / 6510

El núcleo ejecuta cada ciclo de bus en orden, incluidas las lecturas "falsas" que hace el
chip real (la lectura de más al cruzar página, las escrituras dobles de las instrucciones
de lectura-modificación-escritura). Por eso la máquina puede intercalar el resto del
hardware ciclo a ciclo: cada `Read` o `Write` del bus avanza un ciclo de todo el C64.
Las líneas de interrupción se muestrean en cada ciclo y, como en el chip, la decisión de
atender una IRQ usa el penúltimo ciclo de la instrucción; la NMI es por flanco. Están
todos los opcodes no documentados del NMOS (incluidos los que cuelgan la CPU).
Se verifica contra los SingleStepTests de Tom Harte (registros, memoria y cada ciclo de
bus) y la suite funcional de Klaus Dormann.

## Commodore 64 (PAL)

- **Cuadro:** 312 líneas de 63 ciclos a 985.248 Hz (50,12 Hz). La CPU marca el paso:
  cada acceso al bus hace un `Tick` de la VIC-II, las dos CIA y el reloj de muestras del SID.
- **VIC-II:** en las bad lines (una de cada ocho líneas con pantalla) la VIC lee los
  punteros de caracteres y la CPU queda detenida en sus ciclos de lectura (`BaLow`).
  Se dibuja por línea: modos de texto, multicolor, bitmap y color extendido, ocho sprites
  con expansión, prioridad y colisiones, IRQ por línea de raster. La imagen es de
  384×272 con el borde.
- **Bancos:** los bits LORAM/HIRAM/CHAREN del puerto del 6510 deciden si en $A000, $D000
  y $E000 hay ROM, E/S o RAM. La VIC ve un banco de 16 KB elegido por la CIA 2, con la
  ROM de caracteres en los bancos 0 y 2.
- **CIA 6526:** timers A y B (B puede contar los desbordes de A), reloj TOD en BCD con
  alarma y retención al leer la hora, teclado en matriz 8×8 y los dos joysticks.
- **SID:** tres osciladores (triángulo, diente de sierra, pulso, ruido con su registro de
  23 bits), sincronización y modulación en anillo, envolventes ADSR y filtro de estado
  variable. Las muestras salen a 44,1 kHz.
- **Firmware:** se buscan las ROMs originales en `firmware/c64`; si no están se usan las
  Open ROMs (libres, LGPL 3). Muchos juegos llaman a rutinas internas del KERNAL original
  que las Open ROMs no tienen; la máquina detecta los `JMP`/`JSR` desde RAM a esas
  direcciones (no los retornos de interrupción) y hace las más comunes: borrar pantalla,
  valores del editor y de la VIC, fin del manejador de IRQ, CINT/IOINIT/RAMTAS/RESTOR y el
  reinicio con la firma "CBM80" que usan los crackers. Las que quedan sin cubrir se le
  muestran al usuario.
- **Carga:** la máquina arranca hasta `READY.`, copia el programa a su dirección, ajusta
  los punteros del BASIC y lo lanza como `SYS` (A, X, Y y flags desde $030C-$030F, pila en
  $F6 con la dirección de retorno, como el BASIC real: hay programas que dependen de eso)
  o escribiendo `RUN` en el buffer del teclado.

## Compatibilidad reproducible (Retro.Compat)

Cada edición de cada juego tiene un guion JSON (`emu/compat/juegos/`): archivo y miembro
del zip, SHA-256 de la edición para la que se escribió, arranque (las cintas se cargan con
`LOAD ""` por la ROM), pulsaciones por cuadro y comprobaciones (texto en pantalla, memoria,
colores, cambio de imagen). `retro-cli compat` los corre sin ventana, en paralelo, y
produce para cada uno el nivel alcanzado con su evidencia: formato reconocido → medio
cargado → imagen sin signos de fallo (BASIC, mensaje de error, pantalla de inicio,
pantalla vacía) → título → responde a los controles → jugable → listo para fusionar.

- **Texto en pantalla:** en el C64 se lee la matriz de texto de la VIC-II; en el Spectrum
  cada celda se compara con la fuente de la ROM (`ScreenText`).
- **Sonda de controles:** desde un estado guardado al final del guion se aprieta cada
  entrada candidata (8 cuadros, después se suelta) y se compara la imagen, mientras está
  apretada y 60 cuadros después, con la misma corrida sin entradas. Como la emulación es
  determinista, una diferencia significa que el juego reaccionó. Si la corrida sin
  entradas no se repite igual, la sonda se declara no fiable.
- **Determinismo:** `--dos-veces` corre cada guion dos veces y compara el hash de la
  imagen final.
- **Multicarga:** la carga termina al final de la cinta o cuando un bloque "parar la
  cinta" la detiene; para las que no tienen ese bloque, el guion fija `bootFrames` y
  `loadEndsByTime`.
- **Descubrimiento:** `--descubrir` crea guiones para los archivos sin guion (sin pasos ni
  comprobaciones: llegan como mucho a "imagen" y "responde") y salta el contenido excluido.

## Cinta: señal, no atajos

TAP y TZX se convierten en una secuencia de segmentos de la forma (nivel, duración en
T-states): tono guía, pulsos de sincronismo y dos pulsos por bit, con los tiempos de la
ROM (2168 / 667 / 735 / 855 / 1710 T) o los que defina cada bloque del TZX (turbo, tonos
puros, secuencias de pulsos, datos puros, grabación directa, pausas, bucles). El
reproductor avanza con el reloj de la CPU. La rutina LD-BYTES de la ROM, o el cargador
propio del juego, lee el bit 6 del puerto $FE y decodifica los datos como con un
casete real.

Lo que se agrega por comodidad no altera la ejecución:

- **Arranque automático:** la cinta empieza a sonar cuando el PC llega a LD-BYTES ($0556).
- **LOAD "" automático:** se teclea a través de la matriz de teclado, como lo haría una persona.
- **Turbo:** mientras la cinta suena, el emulador corre más cuadros por segundo real, con
  las mismas instrucciones y el mismo timing interno.

## Pruebas: cómo se sostiene la precisión

1. **FUSE (1.356 casos):** para cada opcode compara registros, memoria y la lista
   completa de ciclos de bus con su T-state. Si un acceso ocurre un T antes, la prueba falla.
2. **ZEXDOC / ZEXALL:** recorren cada grupo de instrucciones con miles de combinaciones
   de operandos y comparan un CRC de los resultados con el medido en un Z80 real.
   ZEXALL incluye los flags no documentados.
3. **Máquina:** arranque hasta "© 1982 Sinclair Research Ltd" (texto reconocido con la
   fuente de la ROM), comando BASIC tecleado por la matriz, carga de una cinta generada
   por la ROM, patrón de contención, una interrupción por cuadro, bus flotante y estados
   guardados que se restauran idénticos.
4. **Compatibilidad:** juegos reales que se cargan de la carpeta `ROMs/` si están
   presentes: Manic Miner se carga por cinta, suena su música, ENTER lleva a Central
   Cavern y Willy se mueve con P y O. También se prueba la demo de Pac-Man y que el tono
   guía de la cinta salga a 807 Hz.
5. **8080:** TST8080, 8080PRE, CPUTEST y 8080EXM deben imprimir su resultado correcto y,
   además, coincidir exactamente con la cantidad de instrucciones y de ciclos publicada.
   Por ejemplo, 8080EXM da 2.919.050.698 instrucciones en 23.803.381.171 ciclos. Así se
   verifica el timing de cada instrucción, no solo su resultado.
6. **Placa arcade:** un programa 8080 propio verifica que RST 1 y RST 2 entran en las
   líneas 96 y 224, que una interrupción espera mientras están deshabilitadas, el
   registro de desplazamiento con los ocho corrimientos, los puertos y DIP switches, la
   rotación y los colores de la lámina, el sonido (solo con el amplificador, 735–746
   muestras por cuadro) y los estados guardados.

## Fusiones

Una fusión no mezcla el código de los juegos: corre cada juego en su propia máquina
emulada y los conecta por la memoria.

- **Perfil** (`GameProfile`, JSON): para qué máquina es el juego, cómo reconocer sus
  archivos, sus variables (dirección, codificación `uint8`/`uint16`/`bcd`/`ascii`,
  máscara, rango, solo lectura), qué tecla es cada acción abstracta (`left`, `right`,
  `fire`, `jump`, `start`, `coin`...) y el guion de arranque (`load-tape`, `wait N`,
  `press TECLA N`). Las direcciones se encuentran con `MemorySearch` y se verifican
  jugando.
- **Receta** (`FusionRecipe`, JSON): qué juego va en cada ranura (a, b, c), la vista
  (`side`, `overlay`, `pip`), a qué juegos llega cada acción del jugador y las reglas.
  `FusionRecipe.Validate` revisa todo contra los perfiles y devuelve errores en
  castellano, que es lo que se le reenvía a la IA para que corrija.
- **Sesión** (`FusionSession`): 60 ticks por segundo. En cada tick cada máquina avanza el
  tiempo real a su propia frecuencia de cuadro (50 Hz el Spectrum, 59,54 Hz la placa
  arcade), se leen las variables, se disparan las reglas por flanco (comparando con el
  tick anterior y con un tiempo de espera por regla), se mezclan los sonidos y el
  `Compositor` arma la imagen. Los primeros 30 ticks no disparan reglas, para que el
  arranque de los juegos no cuente como evento.

## Juegos fusionados con guion (Retro.Studio)

Las recetas con reglas conectan juegos que siguen viéndose por separado. Un juego
fusionado, en cambio, es un juego nuevo en una sola pantalla:

- **`StudioSession`** corre las máquinas (cada una a su frecuencia, con pausa, velocidad y
  volumen propios) y, en cada tick, llama al guion: `tick(input)` y después `draw()`. El
  jugador no maneja los juegos directamente: el guion decide qué tecla de qué juego aprieta
  cada acción.
- **El guion** corre en Jint sin acceso a archivos, red ni .NET: solo ve los métodos de
  `ScriptHost`, que un preludio en JavaScript convierte en la API (`games.a`, `screen`,
  `ui`, `sound`). Cada llamada tiene límites de tiempo (0,5 s), instrucciones, memoria y
  recursión. Un error detiene el guion y pausa los juegos; el mensaje dice archivo, línea y
  columna.
- **Los perfiles** agregan lo que un guion necesita para mezclar de verdad: `sprites` (dónde
  está cada gráfico en ROM o RAM y su formato: filas en el Spectrum, columnas en la placa
  arcade) y `helpers` (funciones en JavaScript como `g.aliens()` o `g.willy()` que traducen
  la memoria del juego a posiciones en pantalla, verificadas con capturas). La IA usa las
  ayudas en vez de adivinar direcciones.
- **`Canvas`** dibuja imágenes de los juegos (recorte, escala, transparencia), sprites de 1
  bit, rectángulos, líneas y texto con la letra de la ROM del Spectrum.
- **Arranque instantáneo:** la primera vez cada juego arranca con su guion de arranque (la
  cinta carga por la ROM); el estado queda en memoria y reiniciar o probar otra versión
  carga ese estado.
- **`StudioValidator`** es la prueba de jugabilidad: tres robots (quieto, explorador al azar,
  tirador) empiezan desde el título, juegan y, si llegan al fin, prueban la revancha. Con
  lo que el guion informa (`game.state`, `game.score`, `game.death`, `game.level`) mide la
  estructura (título, juego, fin, revancha), si se pueden sumar puntos, cuándo y de qué se
  muere, y si la imagen está viva. Los problemas hacen fallar la prueba; las notas son
  consejos. Su informe, con capturas, es lo que se le devuelve a la IA o al agente.
- **El método** (`Docs/GUIA-FUSION.md`) va en cada pedido junto con la API y un ejemplo
  que lo sigue (Manic Invaders, con su documento de diseño). Las ideas y los juegos llevan
  un documento de diseño (juego base, héroe, aportes, descartes, objetivo, derrota,
  historia, niveles, controles, legibilidad) que la IA escribe antes del guion y mantiene
  en cada versión.
- **`StudioDesigner`** tiene tres pasos: ideas (3 conceptos con enfoques distintos), escribir
  (guion + prueba + correcciones) y revisar (pedido del jugador + versión nueva). Las
  respuestas usan salidas estructuradas por API, o JSON + bloque de código desde un chat.
  `StudioWorkspace` arma la carpeta para agentes y `retro-cli studio` deja que el agente
  pruebe su `juego.js`.

## IA

`IAiProvider` tiene una sola operación: mandar un sistema, los turnos y un esquema JSON, y
recibir texto. `CliAgentProvider` usa las herramientas oficiales que vienen con las
suscripciones, en modo no interactivo y sin clave de API: Claude Code (`claude -p`,
salida JSON), Codex CLI (`codex exec -`, última respuesta en un archivo) y Antigravity CLI
(`agy -p`) o Gemini CLI; el pedido va por la entrada estándar y RetroEmu nunca maneja las
credenciales. `ClaudeProvider` usa el SDK oficial de Anthropic con salidas
estructuradas (la respuesta respeta el esquema), esfuerzo medio y modelo de respaldo si
el principal rechaza el pedido. `OpenAiProvider` (API Responses de OpenAI) y
`GeminiProvider` (`generateContent` de Google) hablan HTTP directo con `HttpClient`, sin
SDK: el primero usa salidas estructuradas en modo estricto (que exige todas las
propiedades, así que las opcionales viajan como anulables y los `null` se quitan de la
respuesta) y el segundo `responseSchema`, quitando las palabras del esquema que Gemini no
acepta, como `additionalProperties`. `OllamaProvider` usa la API local de Ollama con el
mismo esquema. Los chats sin API y los agentes se conectan copiando la consigna o con la
carpeta de `AgentWorkspace`.

`FusionDesigner` arma la consigna con los perfiles de los juegos elegidos y el pedido del
usuario, valida la respuesta y, si tiene errores, la devuelve con la lista de problemas
(hasta 3 intentos). `CodeExplainer` le pasa a la IA una rutina desensamblada, las rutinas
que llama con los nombres ya conocidos y las variables del perfil, y guarda la respuesta
(nombre, explicación, pseudocódigo, comentarios) en las anotaciones del juego.

## Análisis de código

Cada máquina expone `Flow` (un `IFlowAnalyzer` por CPU que clasifica cada instrucción:
normal, CALL, salto, retorno, salto indirecto), sus `CodeEntryPoints` (reset e
interrupciones) y un `ExecutedMap` opcional donde marca cada instrucción ejecutada.
`CodeMap.Build`:

1. Recorre el código desde las entradas siguiendo saltos y llamadas (descenso recursivo),
   más los bloques que el emulador vio ejecutarse (código alcanzado por tablas de saltos
   o `JP (HL)`, que el análisis estático no puede seguir).
2. Agrupa las instrucciones en rutinas que empiezan en cada destino de CALL: todo lo
   alcanzable sin cruzar el comienzo de otra rutina. Registra quién llama a quién.
3. El código ejecutado que ninguna rutina cubre (por ejemplo el bucle principal de un
   juego de Spectrum, al que se llega desde el cargador) forma una rutina propia.

Las anotaciones (`CodeAnnotations`: nombres, comentarios, explicaciones, pseudocódigo y
cambios al código) se guardan por juego en `RetroEmu/anotaciones`. Los cambios se
escriben con `ICodePatch` en las máquinas cuyo programa está en ROM (la placa arcade) y
con `Poke` en las demás; el archivo original nunca se modifica.

## Cómo agregar una CPU o una máquina

1. Crear `src/Retro.Cpu.<Nombre>` con el intérprete, su interfaz de bus con timing y un
   desensamblador. Implementar `ICpuDebugView` para que el depurador lo muestre.
2. Agregar las pruebas de conformidad de esa CPU antes de usarla en una máquina:
   SingleStepTests para 6502, 8088 y 68000, 8080EXM/CPUTEST para el 8080, la suite
   funcional de Klaus Dormann para el 6502.
3. Crear `src/Retro.Systems.<Máquina>` implementando `IMachine` y el bus de la CPU:
   mapa de memoria, chips de video y sonido, entrada, firmware y medios.
4. Registrar la máquina y sus extensiones en `MachineCatalog` (Retro.App). Si un formato
   es ambiguo (`.dsk`, `.tap`, `.rom`), la aplicación le pregunta al usuario.
5. Para el visor de código y las fusiones: implementar `Flow` (un `IFlowAnalyzer` para la
   CPU), `CodeEntryPoints` y `ExecutedMap`, y agregar la máquina a `MachineFactory`
   (Retro.Fusion) para que los perfiles puedan usarla.

## Etapas siguientes

| Etapa | CPU | Sistemas | Estado |
|---|---|---|---|
| 1 | Z80 | ZX Spectrum 48K | Hecha |
| 1b | Z80 | Spectrum 128K (AY, paginación), TK90X, MSX | Pendiente |
| 2 | Intel 8080 | Placa arcade de Space Invaders | Hecha (probada con la ROM real) |
| F | - | Fusiones, IA y visor de código | Hecha para Spectrum 48K y Space Invaders |
| 3 | 6502 / 6510 | Commodore 64 (.t64, .prg) | Hecha (Pac-Man, Ghostbusters, Spy vs Spy) |
| 3b | 6502 | Discos .d64 y cartuchos del C64, Atari 800, Apple II, NES | Pendiente |
| 4 | 8086 / 8088 | IBM PC / XT con DOS | Pendiente |
| 5 | 68000 | Atari ST, Amiga 500 | Pendiente |
