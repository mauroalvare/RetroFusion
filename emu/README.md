# RetroEmu

Emulador de computadoras antiguas que ejecuta el software original con emulación real
de CPU y hardware, más un depurador para estudiar los procesadores por dentro, un visor
de código para entender y modificar los juegos, y **fusiones**: dos o tres juegos
corriendo a la vez y conectados entre sí, diseñadas a mano o por la IA que elijas.

**Disponible hoy:**

- **ZX Spectrum 48K** (Zilog Z80): carga cintas TAP y TZX con la rutina de la propia ROM y
  snapshots Z80 y SNA. Probado con Manic Miner y Pac-Man.
- **Space Invaders, placa arcade de Taito/Midway** (Intel 8080): necesita el `invaders.zip`
  de MAME, que no se incluye por copyright.
- **Commodore 64** (MOS 6510): VIC-II con bad lines, sprites y colisiones, SID de tres
  voces con filtro, dos CIA 6526. Carga cintas `.t64` y programas `.prg`. Trae el firmware
  libre Open ROMs; con las ROMs originales es más compatible. Probado con Pac-Man,
  Ghostbusters y Spy vs Spy (con sus intros y menús de trainer).

**Fusión entre CPUs distintas:** la receta "Pac-Miner" corre Pac-Man en un C64 (6510) y
Manic Miner en un Spectrum (Z80) a la vez, conectados por sus variables (un fantasma que
atrapa a Pac-Man le quita aire a Willy; una llave de Willy le da una vida a Pac-Man).
Menú **Fusión > Fusión clásica**.

**Plan:** Atari 800, Apple II y NES (misma CPU 6502), Spectrum 128K, TK90X, MSX y Amstrad
CPC (Z80), IBM PC/XT (8088) y Atari ST y Amiga 500 (68000). Ver [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md)
y, para lo que viene con IA, [docs/IDEAS.md](docs/IDEAS.md).

## Uso

Abrí `RetroEmu` (Windows: `RetroEmu.exe`) y arrastrá un juego a la ventana, o usá
**Archivo > Abrir**: el emulador reconoce para qué máquina es cada archivo. El menú
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
instalarla si falta y un botón **Probar conexión**. RetroEmu nunca ve tu contraseña ni tus
credenciales: la herramienta oficial de cada empresa hace el login.

Los juegos fusionados se guardan con todas sus versiones en `RetroEmu/juegos` (dentro de
la carpeta de datos del usuario).

## Fusión clásica con reglas (menú Fusión)

Dos o tres juegos corren al mismo tiempo, cada uno con su emulación real, y se conectan
con **reglas** que leen y escriben su memoria. En la fusión incluida, *Invaders Miner*,
Miner Willy recorre la Central Cavern mientras los invasores bajan encima de él: las
mismas flechas mueven a Willy y al cañón, cada invasor abatido le da aire a Willy, si
Willy muere perdés una nave y juntar llaves te devuelve naves.

1. **Fusión > Fusionar juegos.** La ventana busca en tu carpeta de juegos los que tienen
   **perfil**: dónde guarda cada juego sus vidas, puntaje y posición, y qué teclas usa.
2. Marcá 2 o 3 juegos. Aparecen las recetas que ya existen para esa combinación:
   **Jugar esta fusión**.
3. Si la combinación no existe, armala con IA. Escribí lo que querés ("que cada salto de
   Willy dispare el cañón", "modo caos", "cooperativo para dos") y elegí la IA:

| IA | Cómo se conecta |
|---|---|
| Claude, ChatGPT o Gemini con tu suscripción | Igual que en los juegos fusionados con IA: Claude Code, Codex CLI o Antigravity CLI con tu cuenta, sin clave de API. |
| Claude (API de Anthropic) | Con tu clave de API (o la variable `ANTHROPIC_API_KEY`). El emulador valida la receta y, si tiene errores, se los devuelve a Claude para que la corrija (hasta 3 intentos). Cada receta cuesta unos centavos de tu cuenta. |
| ChatGPT (API de OpenAI) | Con tu clave de API de OpenAI (o la variable `OPENAI_API_KEY`) y el modelo que elijas (por defecto `gpt-6.1-sol`). Mismo circuito de validación, con la respuesta atada al esquema de la receta. La API se paga aparte de ChatGPT Plus: cada receta cuesta unos centavos de tu cuenta. |
| Gemini (API de Google) | Con tu clave de Google AI Studio (o la variable `GEMINI_API_KEY`) y el modelo que elijas (por defecto `gemini-3.8-flash`). Mismo circuito de validación. La capa gratuita no cobra pero tiene límites por minuto y por día; con facturación activada cada receta cuesta unos centavos. |
| Ollama (IA local, gratis) | Con Ollama abierto en tu PC (`ollama serve`) y un modelo descargado. Mismo circuito de validación. |
| Copiar y pegar | Para ChatGPT, Gemini, Claude.ai o cualquier chat: copiás la consigna, pegás la respuesta y el emulador la valida. Si hay errores, te da el texto para devolvérselo a la IA. |
| Carpeta para agentes | Para Antigravity, Claude Code, Codex o Gemini CLI: el emulador crea una carpeta con `TAREA.md`, los perfiles y las instrucciones (`AGENTS.md`, `CLAUDE.md`, `GEMINI.md`). El agente escribe `receta.json` y el emulador la detecta sola; si tiene errores, los deja en `errores.md` para que el agente los corrija. |

4. Mientras jugás podés cambiar la vista (lado a lado, superpuestos o uno dentro del
   otro) y la transparencia, ver los valores en vivo y los eventos, elegir qué juego
   muestran el depurador y el visor de código, y guardar la receta.

Controles de las fusiones (la receta decide a qué juego llega cada acción):

| PC | Acción |
|---|---|
| Flechas (o gamepad) | Mover |
| Ctrl o Z | Disparar |
| Espacio o X | Saltar |
| Enter o 1 | Empezar |
| 5 o C | Moneda |

Una receta es un JSON chico. Ejemplo de regla: `{"when": "a.aliens decreases", "do":
["b.air += 2"]}`. Las condiciones son `increases`, `decreases`, `changes`, `equals N`,
`above N` y `below N` (o `every` N ticks), y las acciones `=`, `+=`, `-=`, copiar de otra
variable, `press` (apretar un control), `pause`, `swap` (cambiar el juego del fondo) y
`message`. Las recetas y perfiles tuyos se guardan en `RetroEmu/recipes` y
`RetroEmu/profiles` dentro de la carpeta de datos del usuario (`%APPDATA%` en Windows,
`~/.config` en Linux).

¿Un juego no tiene perfil? Abrilo, andá a **Código > Buscar variables**, encontrá sus
vidas o su puntaje y agregalas: desde ese momento aparece en la ventana de fusiones.

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
retro-cli fuse invaders-miner --games ROMs --ticks 1800 --press 120:fire:3 --screenshot fusion.png
```

```bash
retro-cli studio manic-invaders --games ROMs --shots prueba
```

Sirve para pruebas automáticas y para que una IA verifique compatibilidad sin mirar la
pantalla.

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
