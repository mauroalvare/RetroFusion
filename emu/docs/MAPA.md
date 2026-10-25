# Mapa del repositorio y comandos

Fuente única de rutas y comandos de desarrollo. El diseño interno está en
[ARQUITECTURA.md](ARQUITECTURA.md).

## Entrada para jugar

Al iniciar sin un archivo aparece **Biblioteca**, integrado en el área principal. Ofrece las
máquinas disponibles, sus juegos y **Todos los juegos**, con búsqueda por nombre.
**Todos los juegos** reúne originales, creados y fusiones; cada máquina lista originales
y creaciones para ese sistema. **Fusionados** y **Creados** tienen también secciones
propias. No aparece una ventana flotante.
**Biblioteca** o Ctrl+L pausa la partida; **Volver a la partida** restaura la pausa previa.
Elegir una máquina filtra la biblioteca sin reiniciar la partida; abrir un juego
comienza la partida elegida. Las fusiones y los juegos creados se actualizan al entrar
o pulsar Actualizar. **Herramientas > Modificar y analizar** reúne cambios, IA, búsqueda y código;
**Herramientas > Modo depurador** conserva las herramientas técnicas y sus atajos. `HomeScreen` ocupa el espacio entre menú y barra de estado;
el video y las herramientas quedan ocultos mientras se muestra. La disposición se adapta
al tamaño de ventana; no se guarda como posición de una ventana flotante en imgui.ini.

Los archivos sueltos de ROMs se ordenan automáticamente por sistema tras unos segundos
sin cambios; el escaneo también se actualiza. Se respetan subcarpetas, archivos en uso,
fechas y colisiones. Los dudosos quedan sin mover. `.retroemu/identidades.json` conserva
perfiles cuando cambia un nombre: se lleva junto con la biblioteca al actualizar.
`Retro.Core/LibraryActivity.cs` guarda favoritos y recientes en
`%APPDATA%/RetroEmu/biblioteca.json`; `HomeScreen` ofrece ambas vistas y su búsqueda.
Las claves de originales dependen del contenido y sistema, por lo que ordenar o renombrar
el archivo conserva la marca. Las cargas fallidas no se registran.

`RomOrganizer` y `RomIdentities` viven en Retro.Compat; el observador está en GameLibrary.
Para pruebas nativas sobre bibliotecas propias se puede usar `--no-organize`.

`Retro.Update` y `Retro.App/UpdateWindow` implementan **Ayuda > Actualizaciones** y la
búsqueda automática al abrir. Los paquetes incluyen `actualizacion.json`, generado por
`tools/make_update_manifest.py`. La instalación conserva su ruta y nombre;
el paquete temporal, diario y respaldo usan `.retroemu-updates` durante la operación.
`UpdateCleanup` elimina los trabajos confirmados y la carpeta vacía después de que
sale el ayudante; no borra trabajos activos ni recuperaciones pendientes. Los modos
internos `--apply-update` y `--update-ready` usan el mismo ejecutable. `--no-update`
omite la búsqueda; también se omite con `--hidden` y `--autotest`. Preferencias en
`%APPDATA%/RetroEmu/actualizaciones.json`, separadas de IA. La primera 0.2.1 se instala
manualmente. La operación no modifica bibliotecas externas ni los ajustes de AppData.

`HardwareArt`, `ThumbnailFiles` y `ThumbnailCache` (Retro.App) dibujan el hardware y cargan
PNG/JPEG locales solo para filas visibles, con caché de 32 entradas. Creados usan juego.png;
fusionados, vN.png. Las imágenes de la documentación se actualizan desde capturas reales
inspeccionadas, sin añadir ROMs. El aviso del decodificador está en docs/TERCEROS.md.

El estudio limita la lista de selección a una zona con desplazamiento y búsqueda.
En **Máquina**, los originales, las fusiones y cada sistema abren su biblioteca con
un clic. Las listas de juegos no se despliegan al pasar el mouse. **Encender sin juego**
conserva el inicio de las computadoras sin un medio cargado.
Las ayudas para jugar están en **Ayuda > Abrir y jugar**, **Controles de la partida**
y **Atajos**. Los controles corresponden al sistema o a la fusión activos. El README
del paquete es una guía breve; los contratos para agentes se entregan por separado.

El contrato común para las IAs está en `src/Retro.Ai/Docs/CONTRATO-DISENO.md` y se
incrusta con `GameDesignGuide.Compact`; `DISENO-JUEGOS.md` queda como consulta ampliada.
Creador y estudio lo envían por todos los modos de trabajo. Las carpetas para agentes
incluyen `CONTRATO.md` y el documento ampliado; la guía de
fusión y la documentación técnica conservan sus archivos propios.
Los borradores automáticos interrumpidos aparecen como **Trabajos pendientes** en el
estudio; se guardan en `Juegos fusionados/.trabajos`. `StudioWork.cs` contiene el formato
y la escritura atómica, y `StudioDesigner.ResumeAsync` revalida antes de usar la IA.
El creador desde cero ofrece sus pendientes en esa misma ventana: `GameWork.cs` guarda
el estado en `Juegos creados/.trabajos` y `GameWriter.ResumeAsync` lo revalida sin sortear
otra idea ni repetir una respuesta completa.
Muestra los juegos elegidos y separa **Proponer ideas** de **Mi idea**; las carpetas y
las fusiones anteriores se despliegan cuando se necesitan.
**Opciones > Conectar o configurar IA** y los enlaces en el selector abren `AiConnectionWindow` con
instalación, acceso y conexión por herramienta. Modelos/esfuerzos se consultan al abrir
la configuración y por **Actualizar modelos**, sin generar respuestas. Ruta, modelo
manual y prueba de generación quedan en **Conexión avanzada**. Detectar la instalación
o consultar un catálogo no garantiza acceso ni cuota. Guía: `docs/CONECTAR-IA.md`.
**Uso de IA** despliega modo, tope de texto y estimación inicial. **Abrir construcción**
muestra texto enviado y tokens informados, con indicación si los datos son parciales.
Los detalles del juego conservan ese registro cuando vuelve a abrirse.
El progreso muestra un intervalo restante cuando hay al menos tres trabajos completados
con esa herramienta/modelo, esfuerzo y tipo de operación. `AiTiming` usa reloj monótono;
`AiDurationHistory` conserva hasta 20 duraciones por grupo (128 grupos) en
`%APPDATA%/RetroEmu/tiempos-ia.json`, solo claves hash y segundos. Ideas, diseño, cambios
y recuperación se estiman por separado en estudio y creador. Fallos/cancelaciones no
entrenan; al superar el intervalo se muestra un aviso sin un contador clavado en cero.
`AiOperation.cs` concentra medición y límites; el estudio los conserva al continuar.

## Modificar una partida

**Herramientas > Modificar y analizar** (Ctrl+M) reúne edición/congelado, análisis de
partida con cualquier proveedor configurado, búsqueda manual y código. El análisis usa
escrituras y código capturados, propone hipótesis experimentales y no aplica cambios. Hay
cinco pasos de deshacer y un punto de regreso anterior al primer cambio. Cada propuesta
muestra su resultado junto al botón; el congelado se mantiene durante la ejecución
del cuadro, incluso antes de copiar sprites al video. **Observar desde ahora** reúne
un intervalo nuevo sin borrar modificaciones. El estudio comparte este motor con
`g.freeze` y `g.release` para fusiones y creaciones con originales.
`CodeWindow.History.cs` ofrece guardado optativo de informes/variables y reaplicación al
reiniciar. `ModificationHistory.cs` guarda por edición exacta en
`%APPDATA%/RetroEmu/modificaciones`; no escribe perfiles verificados. Guía: [MODIFICACIONES.md](MODIFICACIONES.md).

```powershell
# Interfaz nativa con una cinta/perfil generados, sin juegos del usuario ni pedidos de IA
pwsh tools/test-modifications.ps1
# Abrir el panel al cargar una copia propia; captura relativa al directorio actual
dotnet run --project src/Retro.App -c Release -- --hidden --mute --mods-window --autotest 5 out/modificar.png <juego>
```

## Raíz

| Ruta | Qué es |
|---|---|
| `README.md` | Presentación para quien baja el paquete |
| `retrofusion/` | Kit Python inicial (ensamblador/desensamblador 6502, NES). No lo usa la app; falta su punto de entrada `retro.py`. Se conserva como referencia |
| `emu/` | Todo el emulador |

## emu/

| Ruta | Qué es |
|---|---|
| `RetroEmu.slnx` | Solución con todos los proyectos |
| `src/Retro.Core` | Interfaces (`IMachine`, `ICpuDebugView`, `IMemoryView`), medios y zips, `MemorySearch`, PNG, mapa de código |
| `src/Retro.Cpu.Z80`, `Retro.Cpu.I8080`, `Retro.Cpu.M6502` | Núcleos de CPU con desensamblador |
| `src/Retro.Cpu.I8088`, `tests/I8088.Tests` | 8088, segmentación, depuración, estados, reloj y prelectura optativos |
| `src/Retro.Systems.Pc`, `tests/Pc.Tests` | PcXt: CGA, PIC/PIT/PPI, teclado, DMA/FDC, altavoz y estados completos |
| `src/Retro.Systems.Spectrum`, `Retro.Systems.Arcade`, `Retro.Systems.C64`, `Retro.Systems.Nes` | Máquinas |
| `src/Retro.Machines` | Catálogo de máquinas (qué máquina es cada archivo) y `MachineLauncher` (arranca una máquina con un juego); lo usan la app, el CLI, las fusiones y la compatibilidad |
| `src/Retro.Compat` | Matriz de compatibilidad: guiones por juego, ejecutor sin ventana con sonda de controles, informe JSON y Markdown, descubrimiento de guiones |
| `src/Retro.Fusion` | Perfiles (`Profiles/*.json`), `MachineFactory`, `IFusionRun` y ranuras del estudio |
| `src/Retro.Studio` | Juegos fusionados con guion JS: sesión, validador, diseñador IA, `Docs/GUIA-FUSION.md` y `Docs/API.md`, juegos incluidos en `Games/` |
| `src/Retro.Ai` | Proveedores de IA (CLIs por suscripción, APIs, Ollama), carpeta para agentes |
| `src/Retro.App` | App de escritorio (raylib + Dear ImGui): `RetroApp`, `EmulatorHost`, `InputMapper`, `MachineCatalog`, ventanas de código, estudio (incluye Preparar) y creador |
| `src/Retro.Asm` | Ensambladores 6502 y Z80 (núcleo común de dos pasadas), plantillas de juego y guías del hardware por máquina (`Templates/*.s` y `*.md`) y la cinta `.tap` con cargador BASIC (`SpectrumTap`) |
| `src/Retro.Cli` | `retro-cli`: ejecución sin ventana |
| `tests/*` | NUnit: `Retro.Cpu.Z80.Tests`, `I8080.Tests`, `M6502.Tests`, `Zx.Tests`, `Arcade.Tests`, `C64.Tests`, `Compat.Tests`, `Fusion.Tests`, `Studio.Tests`, `Asm.Tests` |
| `compat/juegos/*.json` | Un guion de compatibilidad por archivo de juego (106 hoy; los curados tienen `"curated": true`) |
| `lab/*.json` | Hallazgos del laboratorio de ingeniería inversa por juego (hipótesis, evidencia, confianza, quién escribe) |
| `compat/resultados.json` | Último resultado de la matriz con toda la evidencia (lo genera `retro-cli compat`) |
| `testdata/` | Suites de CPU (FUSE, ZEX, 8080; las grandes de 6502 se bajan aparte) |
| `roms/` | Firmware redistribuible: `48.rom`, `c64/` (Open ROMs + licencia) |
| `tools/` | `publish.ps1` (paquetes), `make_tar.py`, `bajar-pruebas-6502.py` |
| `docs/` | ARQUITECTURA, MAPA, DECISIONES, LIMITACIONES, IDEAS, JUEGOS-PARA-PROBAR, COMPATIBILIDAD (generada) |
| `out/` | Capturas y salidas locales (no se versiona) |
| `dist/` | Paquetes generados (no se versiona) |

## Comandos

Desde `emu/` salvo que se indique otra cosa.

```bash
# compilar y probar
dotnet build RetroEmu.slnx
dotnet test RetroEmu.slnx -c Release --filter "TestCategory!=Long"   # rápido: sin ZEXDOC/ZEXALL ni 8080EXM
dotnet test RetroEmu.slnx                                    # todo (~10 min en Debug; ZEXALL ~45 s en Release)
dotnet test tests/C64.Tests                                  # un proyecto
dotnet test tests/Retro.Cpu.Z80.Tests -c Release --filter "TestCategory=Long"   # solo ZEXDOC/ZEXALL

# la app (sin ventana ni sonido para pruebas automáticas; la captura va relativa al directorio actual)
dotnet run --project src/Retro.App -- --hidden --mute --autotest 10 out/prueba.png <juego>
dotnet run --project src/Retro.App -- --hidden --mute --crear-window --autotest 12 out/crear.png   # ventana Crear
src/Retro.App/bin/Debug/net10.0/RetroEmu.exe                 # con ventana, para el usuario

# retro-cli (C=src/Retro.Cli/bin/Debug/net10.0/retro-cli.exe)
$C info <archivo|zip>                                        # qué máquina reconoce
$C run juego.nes --frames 900 --press 300:P1_START:10 --screenshot out/nes.png   # NES sin ventana
$C run <archivo|zip> --frames 3000 --press 400:SPACE:6 --shot-every 300 --shot-prefix out/x
$C run <c64.t64> --frames 2000 --text --disasm C000:20 --dump 0400:40
$C studio manic-invaders --games <carpeta-juegos> --shots out/s

# matriz de compatibilidad (Release es mucho más rápido: la matriz entera tarda ~75 s)
$C compat compat/juegos --juegos C:/Users/mauro/Downloads/ROMs --salida compat/resultados.json --md docs/COMPATIBILIDAD.md --capturas out/compat --dos-veces
$C compat compat/juegos --juegos C:/Users/mauro/Downloads/ROMs --descubrir --solo nada   # solo crea guiones para archivos nuevos
$C compat compat/juegos --solo c64-pac --juegos ... --salida compat/resultados.json       # unos pocos, fusionando con el resultado anterior

# laboratorio de ingeniería inversa: llega al juego con un guion y busca vidas, tiempo y posiciones
$C lab compat/juegos/zx48-manicminer.json --cuadros 12000 --salida lab/zx48-manicminer.json

# fusiones con cualquier juego de la carpeta: ids para el estudio, y preparar uno (partida, variables verificadas, perfil por hash)
$C juegos C:/Users/mauro/Downloads/ROMs
$C preparar C:/Users/mauro/Downloads/ROMs/Spy_vs_Spy_C64_EN.zip --ia agent --carpeta out/agente   # o --ia sub-claude, o --ia ninguna

# juegos de 8 bits desde cero: plantilla por máquina, ensamblador y cinta que arranca sola
$C crear c64 out/juego.s                                     # deja out/juego.s y out/juego.prg
$C asm out/zx.s --cpu z80 --salida out/zx.tap --lista
$C crear zx48 out/nuevo.s --ia sub-codex --pedido "..."      # la IA lo escribe, se prueba con el robot y se corrige
$C probar out/juego.s                                        # la misma prueba para un juego editado a mano
$C crear c64 out/tarea --ia agent --pedido "..."             # carpeta para un agente (TAREA.md, GUIA.md, juego.s)

# paquetes autocontenidos (Windows y Linux) en emu/dist
pwsh tools/publish.ps1 -Version 0.2.5

# Actualizar la instalación local y el único acceso RetroEmu del escritorio.
# Requiere cerrar la app; conserva juegos y firmware existente y guarda un respaldo.
pwsh tools/update-local.ps1
```

Teclas de `--press`: las de cada máquina (`SPACE`, `ENTER`/`RETURN`, `CAPS SHIFT`,
`KEMPSTON FIRE`, `JOY2 FIRE`, `RUN/STOP`, `F1`, `COIN`, `P1 START`...), unidas con `+`;
en la línea de comandos los espacios se escriben `_` (`JOY2_FIRE`).

## Ubicaciones fuera del repositorio

Los paquetes incluyen `ROMs`, `Juegos fusionados` y `Juegos creados` con `LEEME.txt`,
y `ACTUALIZAR.txt` junto al ejecutable. `Retro.Core.PlayerFolders` busca la biblioteca
con contenido más cercana; una carpeta de solo ayuda no oculta otra superior ni una
biblioteca existente en `Documentos/RetroEmu`. Sin datos anteriores usa la carpeta
portátil incluida; sin ella, creados y fusionados usan `Documentos/RetroEmu`.

La instalación local de uso diario tiene un único acceso `RetroEmu`. Se inició en
`Programa`; el updater actualiza sus archivos sin cambiar la carpeta.
Jugar, fusionar, crear y modificar se abren dentro de esa misma app. Las actualizaciones
locales usan `tools/update-local.ps1`, que sigue ese acceso cuando apunta a una carpeta
activa dentro del espacio de trabajo; no se dejan copias o accesos por función.
Las pruebas automáticas quedan en `out/`, sin accesos en el escritorio. Solo se prepara
otra copia visible si el usuario la pide expresamente.

| Ruta | Qué es |
|---|---|
| `C:\Users\mauro\Desktop\RetroEmu\Programa` | Instalación única que usa el usuario, acceso `RetroEmu` en el escritorio; firmware en `Programa\firmware`, documentos en `Programa\docs` |
| `C:\Users\mauro\Desktop\RetroEmu\Juegos fusionados` | Juegos fusionados del usuario (`<id>\vN.json` + `vN.js`) |
| `C:\Users\mauro\Downloads\ROMs` | Juegos del usuario; `revision\` tiene el inventario con hashes (`inventario-roms.json`) y los informes de la auditoría de ROMs |
| `C:\Users\mauro\Desktop\RetroEmu\ROMs` | Copia de los juegos que usa la app del Escritorio |
| `%APPDATA%\RetroEmu` | Estados guardados, anotaciones de código y configuración de IA de la app |

## Fusiones con base y creación con originales

**Biblioteca** permite buscar por nombre y abrir los originales, incluso después de
jugar una fusión.
Los juegos fusionados tienen su propia entrada; guardar o jugar una fusión no quita
archivos originales de la biblioteca ni los reemplaza.

**Crear > Fusionar juegos** es la entrada para elegir juegos, prepararlos e
definir la unión. El selector conserva por defecto un original y permite invertir la base.
`FusionDirection` acompaña ideas, escritura y correcciones; `StudioGame.baseSlot` se
guarda sin dejar que la IA o la carpeta cambien la elección. `screen.base` dibuja solo
la imagen nativa elegida; `screen.game` y pantallas secundarias completas siguen rechazados.
**Crear > Juego nuevo con tus ROMs** permite diseñar una escena nueva. Esos juegos aparecen
en Creados y se guardan en Juegos creados. Las pruebas de `tests/Studio.Tests` usan IA simulada.

Mientras la IA propone, escribe o corrige un juego, **Abrir construcción** muestra la
etapa, el tiempo transcurrido, el estado del proceso CLI y su salida conforme llega.
Está disponible en el estudio y en el creador; permite cancelar y conserva en memoria
los últimos 12.000 caracteres de esa construcción. Algunos CLI entregan el texto recién
al terminar: que no haya texto todavía no significa que el proceso haya fallado.
El contador mide tiempo transcurrido; la estimación del tiempo restante sigue pendiente.

Las instrucciones para la IA sí forman parte del producto: `src/Retro.Studio/Docs/GUIA-FUSION.md`
y `API.md` están versionadas y se incorporan a la app. Los archivos locales de coordinación
de agentes quedan fuera del repositorio.

Comprobación de la ventana (sin molestar al jugador):

```powershell
dotnet run --project src/Retro.App -c Release -- --hidden --mute --studio-window --autotest 10 out/estudio.png
```

Ya no existen FusionWindow, FusionDesigner, recetas clásicas ni vistas side/overlay/pip.
Los ejemplos incluidos son Manic Invaders (estudio), Tormenta y Topos (creador).

Diagnóstico de interrupciones 8088+8259A (programa propio, sin BIOS/DOS): desde emu,
`dotnet run --project src/Retro.Cli -c Release -- cpu8088 irq-demo --steps 1000`.
Detalles y resultado esperado en [I8088.md](I8088.md).

Diagnóstico del temporizador 8253 conectado a IRQ0/8088 (reloj externo, programa propio):
`dotnet run --project src/Retro.Cli -c Release -- cpu8088 timer-demo --steps 1000`.
Desde emu. Contadores, contrato de reloj y límites en [PIT8253.md](PIT8253.md);
resultado esperado y presupuesto en [I8088.md](I8088.md).

Los nuevos trabajos en carpetas para agentes usan rutas únicas para evitar reemplazar
un guion pendiente al crear otro trabajo. La ruta elegida se muestra en la ventana;
los archivos ya creados permanecen donde estaban.

El contraste funcional del 8088 se ejecuta desde el mismo `retro-cli` mediante
`cpu8088 verificar --suite <carpeta-v2> --salida informe.json`. La suite externa y
su licencia se conservan aparte; no se descargan automáticamente.


**Juego > Partidas guardadas** muestra cinco ranuras por juego y edición, con fecha.
Los guardados de versiones anteriores se pueden cargar desde el grupo del mismo nombre;
conviene comprobar que correspondan al juego abierto. Los archivos nuevos se guardan
en AppData/RetroEmu/states/<identidad>/slotN.rfstate. StateSlotStore.cs resuelve rutas y
fechas; RetroApp.States.cs dibuja el panel. El guardado rápido es temporal.


PC/XT se abre desde la biblioteca con un IMG/IMA de arranque y BIOS externo.
Guía del jugador: [PC-DOS.md](PC-DOS.md). Desde emu:
`dotnet run --project src/Retro.Cli -c Release -- run disco.img --bios ruta/al/bios.rom --frames 2400 --screenshot out/pc.png`.
`PcLaunchTests` comprueba carga, identidad de guardados y direcciones por encima de 64 KB.
`PCXT.md` detalla modos implementados y pendientes. La cuenta de instrucciones y el
reloj de placa son independientes.
