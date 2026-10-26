# Mapa del repositorio y comandos

Mapa de rutas y comandos de desarrollo. Los ejemplos usan carpetas de la instalación
o rutas que debés adaptar a tus archivos. El diseño interno está en
[ARQUITECTURA.md](ARQUITECTURA.md).

## Entrada para jugar

Al iniciar sin un archivo aparece **Biblioteca**, integrado en el área principal. Ofrece las
máquinas disponibles, sus juegos y **Todos los juegos**, con búsqueda por nombre.
**Todos los juegos** reúne originales, creados y fusiones; cada máquina lista originales
y creaciones para ese sistema. **Fusionados** y **Creados** tienen también secciones
propias. No aparece una ventana flotante.
**Biblioteca** o Ctrl+L (Ctrl+Shift+L en PC) pausa la partida; **Volver a la partida** restaura la pausa previa.
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
**Biblioteca** reúne originales, fusiones, creados y filtros por máquina. El menú
**Juego** concentra apertura, partidas, pausa, reinicio, controles y **Encender sin juego**.
No despliega bibliotecas de juegos al pasar el mouse.

`FileBrowser` filtra una instantánea de carpeta, la actualiza cada dos segundos y dibuja
solo las filas visibles. Un clic selecciona; **Abrir** o **Enter** abre y **Esc** cancela.
Los ZIP de PC con varios ejecutables muestran rutas de carpeta para elegir; **Juego >
Programas del archivo** permite cambiar después. Cancelar devuelve la pausa previa;
una apertura rechazada conserva las opciones del medio anterior.
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
dotnet run --project src/Retro.App -c Release -- --hidden --mute --no-organize --no-update --mods-window --autotest 5 out/modificar.png 'RUTA-AL-JUEGO'
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
| `src/Retro.App` | App de escritorio (raylib + Dear ImGui): `RetroApp`, `EmulatorHost`, `InputMapper`, biblioteca, ventanas de código, estudio (incluye Preparar) y creador |
| `src/Retro.Asm` | Ensambladores 6502 y Z80 (núcleo común de dos pasadas), plantillas de juego y guías del hardware por máquina (`Templates/*.s` y `*.md`) y la cinta `.tap` con cargador BASIC (`SpectrumTap`) |
| `src/Retro.Cli` | `retro-cli`: ejecución sin ventana |
| `src/Retro.Update`, `tests/Update.Tests` | Paquetes verificados, actualización en sitio, reversión y limpieza |
| `tests/App.Tests` | Explorador, carga DOS, estados y herramientas de la aplicación |
| `runtime/pcxt/` | BIOS libre, kernel, shell, licencias, fuentes y procedencia; se copian a `system/pcxt` al compilar |
| `tests/*` | NUnit: núcleos, máquinas, compatibilidad, fusión, estudio, ensambladores, app y actualizador |
| `compat/juegos/*.json` | Un guion de compatibilidad por archivo de juego (los curados tienen `"curated": true`) |
| `lab/*.json` | Hallazgos del laboratorio de ingeniería inversa por juego (hipótesis, evidencia, confianza, quién escribe) |
| `out/compat/` | Informes privados de compatibilidad con evidencias, generados por `retro-cli compat` |
| `testdata/` | Suites de CPU (FUSE, ZEX, 8080; las grandes de 6502 se bajan aparte) |
| `roms/` | Firmware redistribuible: `48.rom`, `c64/` (Open ROMs + licencia) |
| `tools/` | `publish.ps1` (paquetes), `make_tar.py`, `bajar-pruebas-6502.py` |
| `docs/` | ARQUITECTURA, MAPA, DECISIONES, LIMITACIONES, IDEAS, JUEGOS-PARA-PROBAR, COMPATIBILIDAD (generada) |
| `out/` | Capturas y salidas locales (no se versiona) |
| `dist/` | Paquetes generados (no se versiona) |

## Comandos

Desde `emu/` salvo que se indique otra cosa.

```powershell
# Compilar y probar desde emu.
dotnet build RetroEmu.slnx -c Release
dotnet test RetroEmu.slnx -c Release --filter "TestCategory!=Long"
dotnet test RetroEmu.slnx -c Release                      # incluye las pruebas largas
dotnet test tests/C64.Tests -c Release                    # un proyecto
dotnet test tests/Retro.Cpu.Z80.Tests -c Release --filter "TestCategory=Long"

# Pruebas de interfaz: ventana oculta, sin audio, organización ni red.
# Las capturas usan rutas relativas al directorio actual.
dotnet run --project src/Retro.App -c Release -- --hidden --mute --no-organize --no-update --autotest 10 out/prueba.png
dotnet run --project src/Retro.App -c Release -- --hidden --mute --no-organize --no-update --crear-window --autotest 10 out/crear.png
dotnet run --project src/Retro.App -c Release -- --hidden --mute --no-organize --no-update --studio-window --autotest 10 out/estudio.png

# CLI compilado. Reemplazá las rutas de medios por tus archivos.
$Cli = './src/Retro.Cli/bin/Release/net10.0/retro-cli.exe'
$Juegos = 'RUTA-A-TUS-JUEGOS'
& $Cli info "$Juegos/juego.zip"
& $Cli run "$Juegos/juego.zip" --pick 0 --frames 900 --screenshot out/juego.png
& $Cli run "$Juegos/juego.nes" --frames 900 --press 300:P1_START:10 --screenshot out/nes.png
& $Cli run "$Juegos/juego.t64" --frames 2000 --text --disasm C000:20 --dump 0400:40
& $Cli run "$Juegos/programa.com" --frames 2400 --screenshot out/pc.png
& $Cli run "$Juegos/disco.img" --bios 'RUTA-A-TU-BIOS' --frames 2400 --screenshot out/disco.png

# Compatibilidad y laboratorio. Usar un guion del sistema/edición correspondiente.
& $Cli compat out/compat/guiones --juegos $Juegos --descubrir --solo nada
& $Cli compat out/compat/guiones --juegos $Juegos --salida out/compat/resultados.json --md out/compat/informe.md --capturas out/compat/capturas --dos-veces
& $Cli lab 'RUTA-AL-GUION.json' --juegos $Juegos --cuadros 12000 --salida out/hallazgos.json

# Perfiles, estudio y creación sin consumir una IA real.
& $Cli juegos $Juegos
& $Cli preparar "$Juegos/juego.zip" --ia ninguna --perfiles out/perfiles
& $Cli studio 'RUTA-AL-JUEGO.js' --games $Juegos --shots out/estudio
& $Cli crear c64 out/juego.s
& $Cli asm out/zx.s --cpu z80 --salida out/zx.tap --lista
& $Cli probar out/juego.s
& $Cli crear c64 out/tarea --ia agent --pedido "Pedido del juego"

# Paquetes locales autocontenidos. Este paso no publica una entrega.
pwsh tools/publish.ps1
```

Las opciones `--ia sub-codex`, `sub-claude`, `sub-google`, APIs u Ollama generan pedidos
al proveedor elegido: ejecutarlas requiere autorización para usar esa herramienta.
El modo `agent` prepara archivos para que un agente continúe; no ejecuta por sí solo
un CLI externo. En Linux se usa `retro-cli` sin `.exe`; desde código también puede
ejecutarse `dotnet run --project src/Retro.Cli -c Release --` seguido de los argumentos.

Para actualizar una instalación local se usa `tools/update-local.ps1` solo con destino
confirmado, app cerrada y paquete verificado. No forma parte de una prueba de desarrollo
normal: preserva datos del jugador y puede modificar la instalación real.

Teclas de `--press`: las de cada máquina (`SPACE`, `ENTER`/`RETURN`, `CAPS SHIFT`,
`KEMPSTON FIRE`, `JOY2 FIRE`, `RUN/STOP`, `F1`, `COIN`, `P1 START`...), unidas con `+`;
en la línea de comandos los espacios se escriben `_` (`JOY2_FIRE`).

## Ubicaciones fuera del repositorio

Los paquetes incluyen `ROMs`, `Juegos fusionados` y `Juegos creados` con `LEEME.txt`,
y `ACTUALIZAR.txt` junto al ejecutable. `Retro.Core.PlayerFolders` busca la biblioteca
con contenido más cercana; una carpeta de solo ayuda no oculta otra superior ni una
biblioteca existente en `Documentos/RetroEmu`. Sin datos anteriores usa la carpeta
portátil incluida; sin ella, creados y fusionados usan `Documentos/RetroEmu`.

La instalación puede conservar el nombre de una versión anterior: el actualizador
reemplaza el programa en la carpeta en uso. Jugar, fusionar, crear y modificar se abren
dentro de esa misma app. Las pruebas se hacen en `out/`, con datos propios y sin
accesos nuevos en el escritorio.

| Ubicación | Contenido |
|---|---|
| Junto al ejecutable: `ROMs/` | Juegos originales del usuario |
| `Juegos fusionados/` | Carpeta por fusión, con metadatos y guion de cada versión |
| `Juegos creados/` | Juegos y trabajos de creación |
| `firmware/` | Firmware aportado por el usuario; se conserva al actualizar |
| `system/pcxt/` | Componentes libres incluidos; se actualizan con el programa |
| `docs/` | Guías incluidas en el paquete |
| `%APPDATA%/RetroEmu/` | Ajustes, anotaciones, actividad, historial de modificaciones y guardados en Windows |

En otros sistemas, los datos de cuenta usan la carpeta `ApplicationData` que informa .NET.
Las rutas personales de desarrollo e instalaciones del mantenedor no son parte del
contrato del paquete. Verificarlas antes de abrir o reemplazar una instalación.

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
El tiempo transcurrido está siempre disponible; el intervalo restante aparece cuando
hay tres trabajos satisfactorios comparables en el historial local. No se inventa
un porcentaje ni una ETA cuando no hay datos suficientes.

Las instrucciones para la IA sí forman parte del producto: `src/Retro.Studio/Docs/GUIA-FUSION.md`
y `API.md` están versionadas y se incorporan a la app. Los archivos locales de coordinación
de agentes quedan fuera del repositorio.

Comprobación de la ventana (sin molestar al jugador):

```powershell
dotnet run --project src/Retro.App -c Release -- --hidden --mute --no-organize --no-update --studio-window --autotest 10 out/estudio.png
```

Ya no existen FusionWindow, FusionDesigner, recetas clásicas ni vistas side/overlay/pip.
Los ejemplos incluidos se abren desde sus secciones del estudio o del creador.

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


## PC/XT y base DOS

PC/XT abre EXE/COM y ZIP con GLaBIOS/FreeDOS incluidos, además de IMG/IMA de arranque.
[PC-DOS.md](PC-DOS.md) es la guía del jugador; [PCXT.md](PCXT.md) detalla placa y límites.
La cuenta de instrucciones y el reloj de placa son independientes.

- `Retro.Machines/PcDos.cs`: FAT12 privado, arranque DOS, alias y datos asociados.
- `runtime/pcxt`: BIOS, kernel, shell, licencias, fuentes y procedencia con SHA.
- `App.Tests/PcDosTests.cs`: datos INI, teclado DOS nativo, identidad, límites y fin de
  aceleración; ejecutables homónimos con datos distintos en sus carpetas.
- `App.Tests/PcLaunchTests.cs`: carga, identidad de estados y direcciones superiores a 64 KB.
- `info` muestra las rutas internas; `run --pick N` respeta su índice para las máquinas
  de archivo único. En arcade se necesita el conjunto de ROMs completo.

## Presentación y estados

`Retro.App/UiTheme.cs` centraliza colores y tipografía. `FileBrowserTests` comprueba
disquetes, filtros, navegación y selecciones que desaparecen. `StateSlotTests` prueba
confirmación de reemplazo y rechazo de confirmaciones de otra sesión. El pie de RetroApp
recupera trabajos activos sin iniciar una generación nueva.
