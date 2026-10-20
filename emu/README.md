# RetroEmu

Emulador de computadoras antiguas que ejecuta el software original con emulación real
de CPU y hardware, más un depurador para estudiar los procesadores por dentro, un visor
de código para entender y modificar los juegos, y **fusiones con IA**: juegos nuevos con
escena propia, sprites, tiles y variables de los originales, como **Manic Invaders**.

**Disponible hoy:**

- **ZX Spectrum 48K** (Zilog Z80): carga cintas TAP y TZX con la rutina de la propia ROM y
  snapshots Z80 y SNA. Probado con Manic Miner y Pac-Man.
- **Space Invaders, placa arcade de Taito/Midway** (Intel 8080): necesita el `invaders.zip`
  de MAME, que no se incluye por copyright.
- **Commodore 64** (MOS 6510): VIC-II con bad lines, sprites y colisiones, SID de tres
  voces con filtro, dos CIA 6526. Carga cintas `.t64` y programas `.prg`. Trae el firmware
  libre Open ROMs; con las ROMs originales es más compatible. Probado con Pac-Man,
  Ghostbusters y Spy vs Spy (con sus intros y menús de trainer).

**Fusión como juego nuevo:** Manic Invaders combina las cavernas y a Willy del Spectrum
con la flota y el láser del arcade. El guion dibuja todo en una escena propia; las máquinas
originales aportan sus piezas y variables. **Fusión > Crear juego fusionado con IA**.

- **Nintendo NES**: CPU 2A03, PPU y APU; cartuchos iNES con mappers 0 y 3.

**En desarrollo:** primer núcleo 8088, todavía parcial y sin máquina PC/XT ni DOS.
Su diagnóstico y límites están en [docs/I8088.md](docs/I8088.md). La ampliación prioriza
PC/XT, después PC AT (286) y PC DOS con VGA (386/486), antes de Atari ST y Amiga (68000).
También siguen previstos Atari 800, Apple II, Spectrum 128K, TK90X, MSX y Amstrad CPC.
Ver [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md)
y, para lo que viene con IA, [docs/IDEAS.md](docs/IDEAS.md).

## Uso

Abrí `RetroEmu` (Windows: `RetroEmu.exe`): aparece **Tus juegos**, con búsqueda y pestañas
**Originales**, **Fusionados** y **Creados**. Elegí uno y pulsá **Jugar**; los originales
siguen disponibles aunque tengan una fusión. También podés abrir un archivo, fusionar
o crear desde los botones de arriba. Volvé a la biblioteca con **Jugar** o Ctrl+L.

![Biblioteca de juegos](docs/img/interfaz/biblioteca.png)

Podés arrastrar un juego o usar **Archivo > Abrir**: el emulador reconoce para qué
máquina es cada archivo. **Ver > Herramientas avanzadas** muestra Código y Depurar.
El menú
**Máquina** muestra las máquinas agrupadas por CPU; cada una dice qué necesita y lista los
juegos de tu carpeta que corre (clic para jugar). Las máquinas que todavía no están
implementadas muestran cuáles de tus juegos las van a usar. Acepta `.tap`, `.tzx`, `.z80`, `.sna`, `.t64`, `.prg` y también `.zip`, incluso
zips dentro de zips. Si el zip trae varias versiones (`[a]`, `[h]`, `[t]`...), te deja
elegir y marca la más limpia como recomendada.

Con una cinta, el emulador escribe `LOAD ""` y la carga arranca sola: se ven las
franjas en el borde y se escucha la cinta. Mientras carga, el emulador corre acelerado.

### Teclas

| PC | Spectrum |
|---|---|
| Letras, números, Espacio, Enter | iguales |
| Shift | CAPS SHIFT |
| Ctrl | SYMBOL SHIFT |
| Backspace | DELETE (CAPS SHIFT + 0) |
| Flechas + Alt (o gamepad) | Joystick: Kempston, Cursor o Sinclair (menú Máquina) |
| F2 / F3 | Guardar / cargar estado rápido |
| F5 / Shift+F5 | Reiniciar / reinicio total |
| F6 / F7 | Reproducir o detener la cinta / rebobinar |
| F8 | Turbo |
| F9 | Pausa / continuar |
| F10 / Shift+F10 | Paso / paso por encima |
| F11 | Pantalla completa |
| F12 | Modo depurador |

### Commodore 64

Arrastrá el `.t64` o `.prg` (o el zip que lo trae). El C64 arranca hasta `READY.`, pone el
programa en memoria y lo lanza como lo haría una persona: con el `SYS` de su línea BASIC
o escribiendo `RUN`.

| PC | C64 |
|---|---|
| Letras, números, puntuación | por posición, como en el teclado del C64 |
| Esc | RUN/STOP |
| Re Pág | RESTORE |
| F1 F3 F5 F7 | las teclas de función del C64 (los juegos dicen "press F1") |
| Ctrl izquierdo / Tab | C= / CTRL |
| Flechas + Alt o Ctrl derecho (o gamepad) | Joystick en el puerto 2 (en **Máquina** se cambia al 1) |
| F2 / F4 | Guardar / cargar estado rápido (el reinicio queda en el menú **Máquina**) |

Si el personaje no se mueve, el juego lee el otro puerto: **Máquina > Joystick > Puerto 1**
(por ejemplo, el jugador blanco de Spy vs Spy). Con las Open ROMs, si un juego llama a
rutinas internas del KERNAL original que no tienen, la barra de estado lo avisa con las
direcciones: con las ROMs originales (`kernal.rom`, `basic.rom`, `chargen.rom` en
`firmware/c64`) esos juegos andan. Las rutinas más usadas (borrar pantalla, reinicio con
firma de cartucho, fin del manejador de interrupciones) ya están cubiertas.

Los discos `.d64`, los cartuchos `.crt` y las cintas de pulsos `.tap` llegan en la próxima
etapa; el emulador lo explica si abrís uno.

En **Manic Miner**: O y P (o Q–T y W–P alternadas) para caminar, la fila de abajo o
Espacio para saltar, ENTER para empezar.

### Space Invaders

Arrastrá el `invaders.zip` de MAME (con `invaders.e`, `.f`, `.g` y `.h`) a la ventana. El
emulador verifica los CRC de la versión original.

| PC | Placa |
|---|---|
| 5 o C (gamepad: Select) | Moneda |
| 1 / 2 (gamepad: Start) | Empezar con 1 o 2 jugadores |
| Flechas o A/D (o gamepad) | Mover el cañón |
| Espacio, Ctrl o Alt (o gamepad) | Disparar |
| T | Tilt |

Los DIP switches de la placa (cantidad de naves, nave extra a los 1000 o 1500 puntos y la
información de monedas) están en **Máquina > DIP switches de la placa**.

Sobre el sonido: la placa original no tiene un chip de sonido, sino circuitos analógicos
que el juego dispara con bits de los puertos 3 y 5. Cuándo suena cada efecto sale exacto
del código del juego. Los efectos en sí están sintetizados para parecerse a esos
circuitos. Si tenés el paquete de samples de MAME (grabaciones de una placa real,
`0.wav`…`9.wav`), poné esos archivos en `samples/invaders/` junto al zip o en `roms/` y
se usan automáticamente.

### Modo depurador (F12)

- **CPU Z80:** registros (editables en pausa), flags (clic para cambiarlos), estado de las
  interrupciones (IM, IFF1/IFF2, pulso /INT), T-states y posición del haz.
- **Desensamblado:** sigue al PC, muestra los nombres de las rutinas de la ROM y tiene
  breakpoints (clic en `( )`). Con clic derecho: ejecutar hasta ahí, mover el PC o ver en
  memoria.
- **Memoria:** volcado hexadecimal y ASCII. Un clic en un byte permite editarlo.
- **Pila**, **Cinta** (bloques, progreso, controles) y **Pantalla**.
- **Programa de prueba:** cargá bytes en hexadecimal o un `.bin` en cualquier dirección
  y ejecutalo paso a paso. Ejemplo: `3E 02 D3 FE 3C 18 FB` cambia el color del borde en bucle.

## Crear juego fusionado con IA (menú Fusión)

Elegís 2 o 3 juegos y la IA inventa cómo mezclarlos en **un juego nuevo, en una sola
pantalla, con historia y niveles**. Los juegos originales siguen corriendo de verdad, y
un guion de JavaScript los conecta: lee y escribe su memoria, aprieta sus teclas, los
pausa o acelera, y arma la pantalla con sus gráficos y sus niveles originales.

Viene con un ejemplo, **Manic Invaders** (Space Invaders + Manic Miner): una flota invasora
se posó sobre las minas de Surbiton y Miner Willy, con un láser, tiene que juntar las llaves
de cada caverna y escapar por el portal antes de que la flota aterrice. Son las **20
cavernas originales** de Manic Miner, leídas de la memoria del juego, y la flota es la del
arcade de verdad, que apunta sus bombas a Willy. Hay dos presiones: el aire se acaba y solo
las llaves lo devuelven (están arriba, donde caen las bombas), y la flota baja y cada
invasor derribado la empuja hacia arriba. Los pisos frenan las bombas y el láser los
atraviesa. Cada caverna presenta una novedad: el guardián en la 2, la tercera fila en la 3.

| PC | Manic Invaders |
|---|---|
| Flechas | Caminar |
| Espacio o flecha arriba | Saltar |
| Ctrl o Z | Disparar hacia arriba |
| Enter | Empezar |

### Cómo se crea uno

1. **Fusión > Crear juego fusionado con IA** y marcá 2 o 3 juegos.
2. Si ya hay juegos fusionados con esos juegos, aparecen con **Jugar**.
3. Para uno nuevo, escribí un enfoque si querés ("cooperativo", "que Willy maneje el
   cañón", "algo caótico") y tocá **Proponer 3 ideas**. Cada idea trae su diseño completo
   (juego base, héroe, aportes, qué se descarta, historia, niveles, controles). Elegí una con
   **Hacer este juego**, o contá tu propia idea y tocá **Hacer mi idea**.
4. La IA escribe el juego y el emulador lo prueba solo, sin ventana, con **tres jugadores
   robot** (uno quieto, uno que explora, uno que dispara sin parar). Revisa que haya pantalla
   de título, que start empiece, que se puedan sumar puntos, cuánto se sobrevive y de qué se
   muere, que haya fin del juego y revancha. Si algo falla, se lo devuelve a la IA con los
   números (hasta 3 intentos). Cuando pasa, arranca.
5. Mientras jugás, **Pedile un cambio a la IA**: una corrección ("los invasores son muy
   rápidos") o una visión totalmente distinta ("que ahora sea cooperativo"). Cada pedido es
   una **versión nueva**, y las anteriores quedan en el selector de versiones.
6. **Ver y editar el guion** muestra el JavaScript: lo podés cambiar a mano y probarlo.

### El método (para cualquier IA o agente)

Lo que hace jugable a una fusión está escrito en
[src/Retro.Studio/Docs/GUIA-FUSION.md](src/Retro.Studio/Docs/GUIA-FUSION.md), y es lo
que recibe la IA en cada pedido: un juego base y un héroe; de 1 a 3 aportes de los otros
juegos, cada uno cambiando una decisión del jugador; una lista explícita de lo que se
descarta; reglas claras; mundo, niveles e historia, con una novedad por nivel; legibilidad
(nada encima del terreno, cada peligro con su color); título, juego, fin y revancha; y
probar con números, buscando estrategias que rompan el juego. Termina con una lista de
control y los anti-patrones (por ejemplo, poner un juego encima del otro, que es lo que la
prueba automática rechaza). La API de los guiones está en
[src/Retro.Studio/Docs/API.md](src/Retro.Studio/Docs/API.md).

### Qué IA usar (con o sin pagar la API)

| Opción | Qué hace falta |
|---|---|
| **Claude con tu suscripción** | Claude Pro o Max y **Claude Code** instalado, con tu cuenta iniciada una vez. RetroEmu lo usa en modo no interactivo: no hace falta clave de API ni se paga aparte; cada pedido cuenta para los límites de tu plan. |
| **ChatGPT con tu suscripción** | ChatGPT Plus o Pro y **Codex CLI** instalado (`codex login`, entrando con tu cuenta de ChatGPT). Sin API ni cobro aparte. |
| **Gemini con tu cuenta de Google** | **Antigravity CLI** (`agy`), con tu cuenta de Google (gratis con límites, más con Google AI Pro). Si todavía tenés el viejo Gemini CLI, también lo usa. |
| Copiar y pegar | Cualquier chat (ChatGPT, Gemini, Claude.ai) con cualquier plan, sin instalar nada: copiás la consigna, pegás la respuesta y el emulador prueba el juego. |
| Carpeta para agentes | Antigravity, Claude Code, Codex o Gemini CLI trabajan en una carpeta con la tarea, el método, la API y los juegos. El agente escribe `juego.js`, se prueba con `retro-cli studio juego.js --games <carpeta> --shots prueba` (informe de los robots y capturas) y el emulador lo arranca cuando pasa. |
| Claude, ChatGPT o Gemini por API | Una clave de API de cada empresa; se paga aparte de la suscripción. |
| Ollama | Gratis y local, pero los modelos chicos programan peor. |

Para las tres opciones de suscripción, la ventana muestra si encontró la herramienta, cómo
instalarla si falta, el **modelo** que usa (vacío = el de la herramienta; por ejemplo
`opus`, `sonnet` o `haiku` en Claude Code; en el CLI, `--modelo`) y un botón **Probar
conexión**.

**Cómo se portan al preparar un juego** (pruebas del 18/10 con dos cracks de C64 que tienen
intro, documentos, menú de trucos, nombre y tienda antes de la partida; cada ronda de IA ve
el texto, un dibujo y la captura de la pantalla):

| IA | Spy vs Spy | Ghostbusters | Tendencia |
|---|---|---|---|
| Codex CLI | 4 rondas, 58 s | 14 rondas: dedujo que la tienda exige trampas | Prudente; con la instrucción de juntar acciones ya contesta un menú entero por ronda. |
| Claude Code | 2 rondas, 63 s; nombró bien el reloj (`clockMinutes`) | 15 rondas, 261 s | Se adelanta: manda teclas para pantallas que todavía no vio; mantenía el joystick y se pasaba de casilla. |
| Antigravity CLI | 4 rondas, 205 s; también nombró el reloj | No llegó en 24 rondas: entendió que las trampas son obligatorias, pero cambiaba de categoría con la trampa levantada y la soltaba | Muy prudente: una acción por ronda y esperas largas; lento por consulta (hasta un minuto). |

Cada ronda manda solo la pantalla actual con el historial de lo hecho (no la conversación
entera), para gastar menos cupo de tu plan. Cuando una IA maneja, la partida la confirma ella:
el cursor de una tienda o de un menú también se mueve con el joystick. La instrucción común se ajustó con lo que se vio: juntar las acciones de la pantalla que se
ve (sin adivinar las siguientes), saltear o esperar de una vez las presentaciones, mover los
cursores de a una casilla y buscar qué falta cuando una tecla no responde. RetroEmu nunca ve tu contraseña ni tus
credenciales: la herramienta oficial de cada empresa hace el login.

Los juegos fusionados se guardan con todas sus versiones en **Juegos fusionados** junto
al programa, o en Documentos/RetroEmu. Las copias antiguas de `%APPDATA%/RetroEmu/juegos`
se siguen leyendo. La actualización conserva los archivos del jugador; una copia sin
modificar del ejemplo retirado Pac-Man en la Mina deja de ofrecerse en el menú.

## Preparar los juegos desde el estudio

En **Fusión > Crear juego fusionado con IA**, cada juego sin variables verificadas lleva
el botón **Preparar**. Busca la partida, encuentra vidas, puntaje, tiempo y posición,
verifica cada hallazgo y guarda el perfil para esa copia (SHA-256) en
`%APPDATA%/RetroEmu/profiles`. El progreso y **Cancelar la preparación** aparecen en la
misma ventana. Puede usar la IA elegida para superar menús difíciles.

Sin preparar, el estudio tiene controles, máquina y un arranque básico; necesita conocer
las piezas o variables que usa su diseño. Preparar ayuda a obtenerlas, pero no garantiza
que cualquier combinación produzca un buen juego.

La fusión clásica por recetas se retiró. Ya no hay vistas superpuestas, lado a lado ni
una pantalla dentro de otra. `screen.game` tampoco dibuja capturas: el validador rechaza
su uso y pide dibujar una escena con sprites y tiles. El ejemplo incluido es Manic
Invaders. Pac-Man en la Mina se retiró porque mostraba la pantalla completa de Pac-Man;
una versión futura necesitaría un laberinto y una jugabilidad propios.

## Código del juego (menú Código o Ctrl+K)

- **Rutinas:** el emulador anota cada instrucción que ejecuta la CPU y, al analizar, divide
  el programa en rutinas (sigue los CALL y saltos, y suma el código que solo se alcanza
  por tablas de saltos). En blanco las que se ejecutaron, en gris las que todavía no, en
  verde las que ya tienen explicación. Se navega por "llama a" y "la llaman", o con doble
  clic en un CALL.
- **Nombres y comentarios:** renombrá rutinas y comentá líneas. Las direcciones de las
  variables del perfil y las rutinas con nombre aparecen por su nombre en el listado.
- **Explicar con IA:** la IA recibe la rutina desensamblada, las rutinas que llama y las
  variables conocidas, y devuelve un nombre, una explicación, pseudocódigo tipo C y
  comentarios por línea. Funciona con Claude, ChatGPT, Gemini, Ollama o copiando la consigna
  a cualquier chat.
- **Cambios al código:** cambiá los bytes de una instrucción o anulala (NOP). Ejemplo:
  anular la instrucción que resta una vida da vidas infinitas. El cambio se aplica a la
  copia en memoria (también a la ROM de la placa arcade), nunca al archivo, y queda
  guardado para deshacerlo o volver a aplicarlo.
- **Variables y controles:** editá o congelá las variables del perfil (aire infinito,
  más naves) y configurá las teclas y el arranque que usan las fusiones.
- **Buscar variables:** el buscador de trucos clásico. Empezás, hacés algo en el juego y
  filtrás por "aumentó", "disminuyó", "no cambió" o "es igual a". Cada variable encontrada
  se agrega al perfil del juego.
- **Exportar listado .asm:** todo el programa con nombres, comentarios y pseudocódigo, en
  `Documentos/RetroEmu/codigo/`.

## Ejecutor sin ventana (`retro-cli`)

```bash
retro-cli info juego.zip
```

```bash
retro-cli run juego.tzx --autoload --until-tape-end --frames 200 --screenshot fin.png
```

```bash
retro-cli run juego.tzx --autoload --frames 11000 --press 9900:ENTER:40 --press 10050:P:60 --shot-every 25 --shot-prefix captura --wav audio.wav
```

```bash
retro-cli run snapshot.z80 --trace 200
```

```bash
```

```bash
retro-cli studio manic-invaders --games ROMs --shots prueba
```

```bash
retro-cli juegos ROMs
```

```bash
retro-cli preparar ROMs/Spy_vs_Spy_C64_EN.zip --ia sub-claude
```

```bash
retro-cli lab ../compat/juegos/zx48-manicminer.json --cuadros 12000
```

`juegos` lista todo lo que se puede fusionar con el id que usa el estudio; `preparar`
hace el perfil de un juego (con `--ia agent --carpeta X` responde un agente por archivos, y
con `--ia ninguna` solo se prueban teclas); `lab` estudia la memoria de un juego y verifica
cada hallazgo.

Sirve para pruebas automáticas y para que una IA verifique compatibilidad sin mirar la
pantalla.

## Crear un juego de 8 bits desde cero (menú Crear)

**Crear > Juego de 8 bits nuevo** abre una ventana donde elegís la máquina (Commodore 64 o
ZX Spectrum), la IA y, si querés, qué juego te gustaría. La IA lo escribe en ensamblador,
el emulador lo prueba con un jugador robot y la IA lo corrige hasta que anda; cuando pasa,
arranca solo. **Empezar desde la plantilla** crea uno mínimo para editar a mano, sin IA.

Sin API también se puede: con **copiar y pegar**, la ventana te da la consigna para el chat
de tu IA, pegás la respuesta y el emulador la prueba (si falla, te da el pedido de
corrección). Con la **carpeta para agentes**, arma una carpeta con la tarea, la guía y la
plantilla; tu agente (Claude Code, Codex, Antigravity...) escribe `juego.s` y el emulador lo
prueba cada vez que lo guarda y le deja `informe.md` y `captura.png`.

Cada juego queda en una carpeta de **Juegos creados** (junto al programa, como Juegos
fusionados): `juego.s` (el programa), `juego.prg` o `juego.tap` (se cargan en este o en
cualquier emulador), `juego.png` y `juego.json`. Después de editar `juego.s`, **Probar de
nuevo** lo ensambla y lo prueba otra vez. La primera vez que se abre la ventana aparecen dos
juegos de ejemplo listos para jugar.

### Desde la línea de comandos

`crear` empieza un juego nuevo para el Commodore 64 o el ZX Spectrum desde una plantilla
mínima en ensamblador: arranca sola, escribe un título, mueve un personaje con los
controles y suena. El ensamblador incluido (6502 y Z80) lo deja listo para cargar como
cualquier juego: un `.prg` en el C64 y, en el Spectrum, una cinta `.tap` con un cargador
BASIC que arranca el código solo.

```bash
retro-cli crear c64 mijuego.s
```

```bash
retro-cli run mijuego.prg --frames 300 --press 100:JOY2_RIGHT:60 --screenshot mijuego.png
```

```bash
retro-cli crear zx48 otro.s
```

```bash
retro-cli asm otro.s --cpu z80 --salida otro.tap --lista
```

```bash
retro-cli run otro.tap --autoload --until-tape-end --frames 300 --press 50:P:40 --screenshot otro.png
```

Con `--ia`, la IA elegida escribe un juego original partiendo de la plantilla y de una guía
del hardware de la máquina (memoria, pantalla, sprites, controles, sonido). El juego se
ensambla, se carga y se prueba en el emulador: que arranque, que dibuje, con qué controles
se mueve el jugador (también después de una pantalla de título) y un jugador robot que
juega al azar 30 segundos mirando que no se trabe. La IA recibe los errores o lo que se vio,
con una imagen de la pantalla, y corrige hasta que pasa (5 intentos). Sin pedido, cada vez
parte de una semilla distinta (género, mundo, tono y una regla insólita).

```bash
retro-cli crear c64 faro.s --ia sub-codex --pedido "un faro que guía barcos en una tormenta"
```

```bash
retro-cli probar faro.s
```

`probar` le hace la misma prueba a un juego editado a mano y deja el `.prg` o `.tap` y una
captura al lado. `retro-cli crear zx48 carpeta --ia agent` arma la carpeta de un agente que
trabaja desde la terminal (prueba con `probar`).

En `src/Retro.Asm/Examples/` hay dos juegos originales escritos solo con esas guías, que
pasan la misma prueba: **Tormenta** (C64: un bote junta salvavidas y esquiva rayos, con
sprites, sonido del SID y puntaje) y **El jardín de los topos** (Spectrum: golpear topos que
asoman en 24 agujeros, con atributos, caras dibujadas en la pantalla de puntos y el texto
de la ROM).

```bash
retro-cli probar src/Retro.Asm/Examples/tormenta-c64.s
```

- **C64** (`.org $0801`): la línea BASIC `10 SYS 2061`, la pantalla limpia, el sprite 0
  movido con el joystick del puerto 2 y el disparo que cambia el borde y suena por el SID.
- **Spectrum** (`org $8000`): el título impreso con la ROM, un bloque de color movido con
  Q A O P o el Kempston, y un pitido con ESPACIO o el disparo.
- **Ensamblador:** etiquetas (las que empiezan con `@` son locales a la última etiqueta
  común), constantes con `=` o `EQU`, expresiones con `+ - * / & | ^ << >>`, `<` y `>`
  (byte bajo y alto), `$` (dónde empieza la línea) y números `$FF`, `0FFh`, `0xFF` o
  `%1010`. Directivas con o sin punto: `org`, `byte`/`db`/`defb`/`defm`/`text`,
  `word`/`dw`/`defw`, `fill`/`ds`/`defs`/`res`. Los errores dicen la línea y qué está mal;
  `--lista` muestra cada línea con su dirección y sus bytes.

## Compilar y probar

Requiere el .NET SDK 10.

```bash
dotnet build RetroEmu.slnx -c Release
```

```bash
dotnet test RetroEmu.slnx -c Release --filter "TestCategory!=Long"
```

```bash
dotnet test tests/Retro.Cpu.Z80.Tests -c Release --filter "TestCategory=Long"
```

La última corre ZEXDOC y ZEXALL (unos 45 segundos cada una). El ejercitador 8080EXM
(unos 20 segundos) corre con:

```bash
dotnet test tests/I8080.Tests -c Release --filter "TestCategory=Long"
```

Las pruebas de compatibilidad usan los juegos de una carpeta `ROMs/` ubicada en algún nivel por encima
del proyecto, o los de la variable de entorno `RETRO_GAMES`. Si no los encuentran, se
omiten.

Para armar los paquetes de Windows y Linux:

```bash
pwsh tools/publish.ps1
```

## ROMs y juegos

`roms/48.rom` es la ROM original del ZX Spectrum 48K (© Amstrad). Amstrad permite
distribuirla junto con emuladores. Los juegos no se incluyen: usá copias que tengas
derecho a usar.

## Licencias de terceros

- raylib (zlib) y Raylib-cs (zlib): ventana, audio y entrada.
- Dear ImGui (MIT), ImGui.NET (MIT) y rlImgui-cs (zlib): interfaz del depurador.
- SDK de Anthropic para C# (MIT): conexión con Claude.
- Jint (BSD-2) y Acornima (BSD-3): intérprete de JavaScript aislado para los juegos fusionados.
- Pruebas: el test suite Z80 de FUSE (GPL), ZEXDOC/ZEXALL de Frank Cringle (GPL),
  TST8080 (Microcosm Associates), 8080PRE/8080EXM (Ian Bartholomew y Frank Cringle) y
  CPUTEST (SuperSoft Associates) se usan solo para verificar; no forman parte del
  programa distribuido.
