# Registro de decisiones

Decisiones de arquitectura y de forma de trabajo, con su motivo. Una entrada nueva por
decisión (no se reescriben las viejas: si cambia algo, se agrega otra que la reemplaza).
Las entradas son históricas: D-065 reemplaza el requisito de aportar DOS de D-063;
D-066 amplía la elección de archivos y D-067 fija la autorización para publicar.

## D-001 C# .NET 10 con raylib y Dear ImGui (2026-10)

Un solo lenguaje para núcleos, máquinas, app y pruebas; paquetes autocontenidos para
Windows y Linux sin instalar nada. raylib da ventana, audio y entrada; ImGui, menús y
depurador.

## D-002 La CPU marca el tiempo a través del bus (2026-10)

Cada núcleo emite cada ciclo de bus en orden (`IZ80Bus`, `IM6502Bus`) y la máquina
intercala su hardware ciclo a ciclo (contención del Spectrum, bad lines del C64). Así un
núcleo sirve a varias máquinas con timing exacto, y se verifica con suites que comprueban
ciclos (FUSE, Harte).

## D-003 En una fusión cada juego corre en su propia máquina (2026-10)

No se traduce código entre CPUs: los juegos originales corren sin cambios en sus
máquinas emuladas y la fusión los conecta leyendo y escribiendo su memoria (perfiles con
variables) y componiendo su imagen y sonido. Por eso se pueden mezclar CPUs distintas.

## D-004 Guiones de fusión en JavaScript aislado (Jint) (2026-10)

La IA escribe JS (lo conocen todos los modelos) que corre con límites de memoria, tiempo,
instrucciones y recursión, sin acceso a archivos ni red. API propia (`games`, `screen`,
`ui`, `sound`, `game`) documentada en `Retro.Studio/Docs/API.md`.

## D-005 IA por suscripción a través de las CLIs oficiales (2026-10)

Claude Code (`claude -p`), Codex (`codex exec`) y Antigravity/Gemini se usan como
procesos con la sesión que el usuario ya inició: sin claves de API ni cobro aparte, y el
emulador nunca toca credenciales. También copiar/pegar con cualquier chat y carpeta para
agentes.

## D-006 Firmware en `firmware/` (2026-10)

Windows no distingue mayúsculas: `roms/` (firmware) y `ROMs/` (juegos del usuario) serían
la misma carpeta en el paquete. El firmware va en `firmware/`; la app y el CLI también
buscan `roms/` por compatibilidad con el árbol de desarrollo.

## D-007 C64 con Open ROMs y rutinas internas del KERNAL emuladas (2026-10-18)

Las Open ROMs (LGPL 3) se pueden distribuir, pero no tienen las rutinas internas del
KERNAL original que muchos juegos llaman directo. Se emulan las más usadas (borrar
pantalla, fin de IRQ, CINT, IOINIT, reinicio CBM80...) solo cuando un `JMP`/`JSR` real
desde RAM salta a esas direcciones, nunca en retornos de interrupción ni desde el propio
firmware. Las no cubiertas se informan al usuario; con las ROMs originales no hace falta.
El BASIC `SYS` se reproduce exacto (registros desde $030C-F, pila en $F6) porque hay
programas que dependen de eso.

## D-010 Compatibilidad por guiones JSON y niveles con evidencia (2026-10-18)

Distinguir "se reconoce", "carga", "llega al título", "responde" y "se puede jugar" exige
pruebas por edición, reproducibles y sin ventana. Un guion JSON por archivo (claves en
inglés como los perfiles) es fácil de escribir para personas y agentes de cualquier
proveedor; el ejecutor guarda hash, firmware, procedimiento y la evidencia de cada nivel.
La sonda de controles compara contra la misma corrida sin entradas (determinismo) en
lugar de buscar movimiento en pantalla, porque las animaciones no la engañan.

## D-011 Un lanzador de máquinas compartido (2026-10-18)

El catálogo (qué máquina es cada archivo) estaba en la app y el arranque de máquinas
repetido en el CLI y en las fusiones. `Retro.Machines` concentra los dos para que la app,
el CLI, las fusiones y la compatibilidad identifiquen y arranquen igual. La matriz
encontró enseguida un error que eso evita: las cintas `.cdt` de Amstrad (contenedor TZX)
se identificaban como Spectrum.

## D-013 La cinta del Spectrum también se detiene sola (2026-10-18)

Karateka (.tap, multicarga) se reiniciaba porque la cinta seguía sonando mientras el
juego mostraba su presentación: cuando le pedía a la ROM el bloque siguiente, ya había
pasado. Un .tap no tiene marcas de "parar la cinta", así que el emulador hace lo que haría
una persona: si nadie lee la cinta como un cargador durante 1,5 s, la detiene antes del
próximo bloque que la ROM puede leer (o vuelve al principio de ese bloque si está en su
tono guía), y LD-BYTES la vuelve a arrancar. Se limita a bloques de velocidad normal
porque un cargador turbo no pasa por LD-BYTES y nadie la volvería a arrancar. La ROM tiene
una espera de un segundo sin leer (LD-WAIT): el PC dentro del cargador cuenta como carga.
Cambia lo que ve el usuario (la cinta se detiene sola) y el tiempo que tardan las cargas
de la matriz.

## D-012 Las máquinas informan las escrituras de la CPU (`IWriteWatch`) (2026-10-18)

Para saber qué rutina escribe una variable (y más adelante watchpoints y mapas de calor)
las máquinas implementan `IWriteWatch`: un `Action<dirección, valor, pc>` opcional que se
llama en cada escritura efectiva de la CPU, con la dirección de la instrucción que la hizo.
Es nulo por defecto, así que no cuesta nada si nadie escucha. Es un contrato nuevo de
`Retro.Core`: toda máquina nueva debería implementarlo.


## D-014 Las fusiones dibujan una escena propia (2026-10-19)

Por pedido del jugador se retiran las recetas clásicas, sus vistas side/overlay/pip y el
CLI fuse (solo informa la retirada). Se conservan perfiles, MachineFactory, GameCatalog
y los contratos compartidos con el estudio. Preparar pasa a StudioWindow.

Se elimina la capacidad de copiar cuadros de los juegos: screen.game devuelve un error
con la indicación de dibujar una escena propia con sprites y tiles, como Manic Invaders.
StudioValidator rechaza el intento incluso si el guion captura el error. Las guías y
las semillas piden una escena nueva. Los guiones antiguos que usaban esa API requieren
reescritura; sus archivos se conservan.

## D-015 Actividad observable de las IAs por CLI (2026-10-19)

`CliAgentProvider.Activity` expone una instantánea del proceso: estado, marcas de tiempo,
cantidad de caracteres recibidos y los últimos 12.000 caracteres de stdout/stderr.
La lectura por bloques conserva la respuesta completa para el diseñador; solo la vista
de actividad se limita. La salida queda en memoria y se comparte con el panel
**Abrir construcción** del estudio y del creador. No se cambia `IAiProvider`, el formato
de respuesta ni los argumentos de los CLI. El contador es tiempo transcurrido; no es
un porcentaje de avance ni revela texto que el CLI todavía no haya emitido.

## D-016 Diseños de fusión sin una plantilla universal (2026-10-20)

Las guías dejan de imponer base/héroe invitado, tres niveles y carteles por novedad.
Se piden unión mecánica, regla única y diferencia frente a diseños anteriores. Los
conceptos incorporan `mechanicLink`, `uniqueRule` y `difference`; los conceptos guardados
anteriores siguen leyéndose, y el documento de diseño se guarda como texto igual que antes.
La escritura directa y las propuestas reciben un resumen acotado de los diseños previos
de la misma combinación. No se envía el guion completo de Manic Invaders como molde.
Los estados de prueba no prescriben menús ni presentación. El validador comprueba ejecución
y jugabilidad básica, no puede garantizar originalidad ni reemplaza comparar partidas.

## D-017 Núcleo 8088 funcional antes del bus temporizado de PC/XT (2026-10-20)

`I8088Bus` agrega accesos de byte a direcciones físicas de 20 bits y puertos de 16 bits.
`I8088Cpu` resuelve segmento/offset y ejecuta una instrucción o iteración REP por `Step`;
una IRQ pendiente se conserva hasta poder aceptarla. Es un primer hito de estado y
direccionamiento, sin conteo de ciclos, prelectura ni máquina. No implementa aún
`ICpuDebugView`, cuyo contador de ciclos implicaría una precisión que todavía no tiene.
Los contratos de máquinas actuales no cambian. El CLI agrega `cpu8088 <bin|demo>` para
probar programas crudos con un límite de pasos, sin simular servicios BIOS/DOS ni
declarar disponible PC/XT. Los opcodes pendientes producen errores con CS:IP.

## D-018 Experimentos de RAM reversibles y propuestas declarativas (2026-10-20)

`GameModifications` trabaja sobre una máquina y su perfil: valida nombres, permisos,
rangos, formato y dirección antes de escribir; una prueba es un conjunto de
`VariableEdit(variable, value, freeze)`. Si una escritura no se puede comprobar, restaura
el estado anterior de la prueba completa. El primer cambio exitoso guarda un punto de
regreso de toda la máquina; volver también libera los congelados. No escribe medios ni
perfiles. Los congelados se reponen antes del cuadro emulado.

`GameModificationAdvisor` devuelve análisis e ideas declarativas, sin código ni direcciones.
Valida también respuestas que no respeten el esquema del proveedor. Recibir ideas no las
aplica: el jugador elige una prueba. Los perfiles con hashes de ediciones concretas solo
coinciden por esos hashes; un nombre igual ya no permite aplicarlos a otra copia.
Las propuestas dependen de variables conocidas; no implican descubrir toda la RAM ni
demostrar el efecto sugerido. La UI debe invalidarlas al cambiar/restaurar la partida.

## D-019 Contrato común de diseño y encargos estables (2026-10-20)

`Retro.Ai.GameDesignGuide` incrusta `Docs/DISENO-JUEGOS.md`. El creador y el estudio
incluyen el mismo contrato en los pedidos a proveedores y en copiar/pegar; sus carpetas
para agentes lo exportan con ese nombre y TAREA.md remite a él. Las guías técnicas de
máquina y API siguen separadas. No se impone género, menú, HUD, héroe ni número de niveles.
Los agentes de fusión ya no reciben un ejemplo completo como programa inicial.

El creador conserva el pedido y una única semilla durante los reintentos. Compara hasta
cinco resúmenes de juegos anteriores de la misma máquina, sin leer ni enviar sus programas.
Las correcciones manuales incluyen contexto técnico y la versión actual; el estudio
conserva además su pedido inicial y usa el último borrador como base para campos omitidos.
`StudioWorkspace.Read` importa `design`, conservando los originales seleccionados.

Los esquemas y los modos manual/carpeta piden textos breves: title 60, pitch 240,
howToPlay 320 y changes 400 caracteres; approach de fusión 180. El diseño detallado
queda en design o comentarios, separado de las ayudas del jugador. Los lectores
siguen aceptando juegos guardados anteriores, sin recortar sus textos.

Pasar el robot no certifica diversión ni creatividad. Las guías explicitan ese límite
y exigen distinguir evidencia de hipótesis. No se añade un extractor de rutinas de ROMs
ni se finge que los perfiles sean ese análisis; la parte restante de T-060 sigue pendiente.

## D-020 Bibliotecas portátiles con instrucciones (2026-10-20)

Los paquetes incluyen ROMs, Juegos fusionados y Juegos creados con LEEME.txt. La
búsqueda compartida en PlayerFolders prioriza contenido real por sobre carpetas con
solo ayuda, incluyendo bibliotecas previas en Documentos para creados/fusionados.
Sin datos previos elige la carpeta portátil más cercana. No migra archivos ni cambia
los formatos de juegos. El actualizador local solo añade LEEME si no existe; conserva
todos los archivos del jugador. ACTUALIZAR.txt explica la combinación de carpetas.

## D-021 Catálogos de modelos sin generación (2026-10-20)

AiModelCatalog consulta metadatos de las herramientas instaladas: model/list paginado
del app-server de Codex tras initialize/initialized; initialize de control stream-json
de Claude Code sin mensajes de usuario; agy models; GET /api/tags de Ollama. No inicia
conversaciones ni lee archivos de credenciales. Los modelos y niveles vienen de esas
respuestas, sin catálogo fijo de respaldo. Las variantes de Antigravity conservan su ID
exacto al elegir esfuerzo. Si falta información se ofrece el predeterminado del CLI.

AiSettings añade CliEfforts, compatible con configuraciones anteriores; cada cambio
de modelo en la UI limpia el esfuerzo anterior. CliAgentProvider pasa --effort o la
opción model_reasoning_effort a Codex. SessionModel no hereda un esfuerzo de otro modelo.
Los catálogos se refrescan al volver a abrir la configuración y por pedido explícito;
consultarlos no equivale a comprobar una generación ni garantiza cuota disponible.

## D-022 Fusión dirigida por un original base (2026-10-21)

El pedido nuevo del usuario prioriza conservar mapas, estética, interfaz y reglas de
un original y transferir mecánicas del otro. Esto matiza D-016: una única imagen nativa
es válida si corresponde a la base elegida; pegar pantallas completas secundarias sigue
prohibido. SkyCraft es una referencia de integración entre motores y Manic Invaders de
aprovechamiento de originales, no plantillas visuales ni código que se importe.

StudioGame añade baseSlot opcional; su ausencia conserva el comportamiento de los
juegos guardados. FusionDirection conserva la elección del jugador fuera de la respuesta
de IA. screen.base solo dibuja el original declarado, a resolución y paleta nativas y
sin bordes. Un intento con otro original queda registrado aunque el guion capture el error.
screen.game sigue rechazado. El guion puede conservar escenarios mediante los datos y
ayudas verificadas del original, sin inventar mapas ni direcciones.

El validador permite inicio jugando y no exige puntaje artificial en modo base. Comprueba
que se consulten datos/recursos/controles de los aportantes; eso no demuestra integración
mecánica ni fidelidad visual por sí solo. Las notas exigen contrastar capturas y jugar.
La exploración del robot contempla arriba/abajo en este modo. La reinterpretación libre
mantiene el contrato anterior y queda como alternativa explícita.

## D-023 Recursos con colores nativos acotados (2026-10-21)

2026-10-21. La revisión de fidelidad encontró que todos los sprites extraídos se reducían
a una máscara monocroma. Se conserva esa API y se añade `g.capture(x,y,w,h,transparent)`:
coordenadas del área activa, hasta 32x32 por recurso y 4096 píxeles acumulados por ranura
y partida. Se guardan máscara y colores independientes del framebuffer. Reiniciar libera
recursos y presupuesto. `screen.sprite` sin color usa la paleta guardada; con un color
explícito tiñe la máscara. Los sprites antiguos siguen blancos por defecto.

Los límites se verifican en el host, también si el guion captura la excepción. No se
habilita otra pantalla completa. Capturar un personaje no demuestra conocer sus reglas;
el perfil básico deja de prometer fusiones por controles compartidos y vistas. La prueba
de calidad sigue requiriendo observar consecuencias mecánicas y jugar, además del robot.

## D-024 Referencias de las ediciones y arranque cancelable (2026-10-21)

OriginalReferences observa las máquinas originales después del arranque configurado,
sin ejecutar el guion propuesto. Conserva área activa, colores y una identidad de contenido
que incluye perfil/medios/firmware. Los datos del perfil siguen siendo la fuente de las
direcciones y reglas verificadas; una imagen no permite inventarlas.

IAiProvider añade SupportsImages con valor predeterminado false; las CLI usan las vías
de imágenes que ya tenían. Escritura y revisión reciben referencias opcionales. Si el
proveedor no admite imágenes, se envían observaciones de tamaño/paleta sin fingir visión.
Los PNG temporales duran la operación y se liberan ante éxito, fallo o cancelación. Manual
y carpeta permiten adjuntar/leer archivos portátiles con el papel de cada original.

StudioSession.Start, MachineFactory.Boot y MachineLauncher.LoadTape aceptan un token de
cancelación opcional. Se comprueba entre pasos y cuadros, se sueltan teclas y no se cachea
un arranque incompleto. La preparación no bloquea la ventana ni inicia una IA al cancelarla.

## D-025 La sesión compone el original base (2026-10-21)

Declarar una base y pedirlo en el prompt no impedía que el guion dibujara un tercer juego.
Ahora StudioSession conserva una copia de los metadatos y repone la imagen nativa antes
de draw(). El guion agrega los aportes. Se rechazan screen.clear durante draw y tamaños
distintos del nativo, aunque JavaScript capture la excepción. Las creaciones libres y
los juegos guardados sin baseSlot, incluido Manic Invaders, conservan su funcionamiento.

El validador revisa una vez por segundo la imagen: una copia casi exacta de otra pantalla
o el borrado general de la base debe sostenerse tres observaciones para fallar. Se
consideran coincidencias de color y geometría de primer plano; una variación de paleta
que conserva el dibujo no equivale a borrarlo. Es un filtro contra sustituciones claras,
no una prueba universal de fidelidad ni una certificación de unión mecánica.

En paralelo se corrige una inferencia de dificultad: ensayos de menos de seis segundos
sin muertes tomaban su duración como primera muerte. Solo se juzga el promedio cuando
los robots activos registraron realmente una primera muerte.

## D-026 Contexto por operación y pedido independiente de la respuesta (2026-10-21)

Las ideas reciben el contrato compacto y un resumen de capacidades; implementar recibe
la API completa. El método extenso sigue disponible en archivos de consulta, sin imponer
su lectura en cada corrección. No se envía un juego de ejemplo como molde. Los originales
autónomos reciben la plantilla de arranque solo mientras no hay un programa vigente.

Cada corrección automática contiene un turno: pedido estable, base elegida, último código
y reporte local. Revisión conserva el pedido nuevo y retira el código de la versión anterior
cuando ya hay un borrador actualizado. Manual y carpeta usan el mismo contrato. PlayerRequest
se guarda aparte de Request (última revisión); la respuesta no puede sobrescribirlo.

Los esquemas de ideas y juegos admiten missingData: un dato esencial ausente detiene la
implementación sin inventar un sustituto. Respuestas antiguas sin ese campo siguen entrando.
Repetir exactamente un programa ya fallido detiene el ciclo y conserva el borrador; no se
recorta ninguna validación para ahorrar contexto. Las referencias se adjuntan al comienzo
y ante problemas funcionales; una corrección de sintaxis no necesita reenviar esos PNG.
Los tamaños UTF-8 medidos localmente no equivalen a consumo de tokens del proveedor.

## D-027 Organización conservadora e identidad portable de ROMs (2026-10-21)

RomOrganizer ordena únicamente archivos sueltos, por sistema y con confianza alta de
MachineCatalog. No clasifica solo por CPU o nombre; todos los medios de un ZIP deben
pertenecer al mismo sistema. Se excluyen copias recientes, archivos en uso, enlaces,
destinos fuera de la raíz y formatos débiles. Un conjunto arcade se mueve completo.
MZ con cabecera Windows/OS2 no se identifica como DOS. El reconocimiento no promete
emulación de máquinas pendientes. MediaArchive comparte el límite expandido entre ZIP anidados.

File.Move dentro de la biblioteca conserva bytes y fechas; nunca sobrescribe ni deduplica.
Las colisiones usan sufijos portátiles, sin distinguir mayúsculas. Antes de un cambio de
nombre, un perfil básico registra su identidad en `.retroemu/identidades.json`, vinculada
a ruta relativa, sistema y SHA-256 del medio. GameCatalog la aplica solo a perfiles básicos
del mismo contenido. Se guarda atómicamente antes de mover; fallar conserva el original.
BuildProfile acepta sourcePath opcional; Preparar lo usa para conservar esa misma identidad.
El bloqueo de organización evita que dos instancias actualicen esos metadatos a la vez.

GameLibrary observa cambios, espera estabilidad y organiza/escanea en una sola tarea.
La app habilita esta conducta solo para la biblioteca ROMs, nunca para la carpeta personal
usada como alternativa. El estudio renueva sus rutas cuando termina el escaneo. La carpeta
`.retroemu` no cuenta como contenido para elegir una biblioteca ni aparece como juego.
MediaSources/UsesMedia permiten posponer los originales activos aunque ya estén en RAM;
reintentar un archivo bloqueado no obliga a releer todas las ROMs que no cambiaron.

## D-028 Miniaturas locales con recursos acotados (2026-10-21)

Inicio conserva navegación, tarjetas y acciones. HardwareArt dibuja ilustraciones propias
de las máquinas y el chip de la placa, sin archivos externos ni texturas. Las filas solicitan
imágenes solo cuando son visibles. ThumbnailCache mantiene LRU de 32 entradas, cola máxima
de ocho y una decodificación en curso. La CPU trabaja fuera del hilo gráfico; crear y liberar
texturas ocurre en ese hilo. Se reduce a 256x192 conservando proporción y se libera al cerrar.
Archivos ausentes o dañados muestran la ilustración del sistema, sin interrumpir la biblioteca.

Se leen PNG/JPG locales homónimos y las imágenes de los creados. StudioLibrary.Save acepta
una captura opcional y guarda vN.png sin cambiar metadatos ni guiones; una miniatura fallida
no impide jugar. Las versiones anteriores y juegos sin imagen siguen funcionando. La captura
de Manic Invaders ya existente se incluye en compilaciones y paquetes. No se descargan portadas.

La prueba nativa mostró que Raylib incluido no decodifica JPEG. El usuario autorizó agregar
StbImageSharp 2.30.16 (C# portable, sin binarios nativos adicionales) para PNG/JPEG. Su aviso va
en docs/TERCEROS.md. Se verifican tamaño de archivo (8 MiB), dimensiones (4096 por lado y
16 megapíxeles) antes de decodificar, y se comprueban las dimensiones devueltas. El límite de
texturas RGBA es 6 MiB; la imagen de trabajo tiene un presupuesto aparte y se procesa de a una.

## D-029 Recuperación local de la construcción (2026-10-21)

StudioDesigner publica DesignCheckpoint antes de cada llamada y al recibir y validar un
borrador. Guarda pedido, dirección/base, metadatos, último código e informe acotado; los
intentos anteriores se identifican por SHA-256 para frenar respuestas repetidas. No guarda
la conversación, la documentación, las imágenes, bytes de ROM ni datos del proveedor.
StudioWorkStore escribe un JSON atómico por operación en Juegos fusionados/.trabajos,
con identificador propio, versión de formato y huellas de los originales/perfil/firmware.

La ventana ofrece continuar o descartar pendientes. Al continuar comprueba los originales
y revalida el último borrador localmente; un resultado válido no vuelve a llamar a la IA.
Una revisión sin respuesta conserva el pedido y el código anterior como contexto, pero
no lo aprueba como una versión nueva. Base, pedido y versión proceden del trabajo, no
de los campos del borrador. WorkId en la versión guardada evita una copia duplicada si
el programa se interrumpe antes de retirar el pendiente. Los juegos viejos no lo necesitan.

Este flujo cubre la escritura y revisión automáticas del estudio. Las carpetas de agentes
y el intercambio manual conservan su mecanismo de archivos; el creador desde cero todavía
no dispone de esta recuperación automática. Cambiar las huellas detiene la continuación
para no ejecutar un guion sobre otra edición: el borrador sigue disponible para copiarlo.

## D-030 Consumo informado y límites por operación (2026-10-21)

IAiProvider.LastUsage es opcional; AiOperation reúne únicamente contadores publicados,
indica cuántas llamadas los aportaron y no convierte datos ausentes en cero. OpenAI y
Codex incluyen caché dentro de entrada; Claude suma entrada sin caché, lectura y escritura
de caché. Gemini suma pensamiento a salida y los prompts de herramientas a entrada.
Gemini CLI usa stats.models sin repetir los desgloses de roles ni sumar otra vez cached.
Ollama aporta prompt_eval_count/eval_count. Versiones sin esos metadatos quedan desconocidas.
Codex usa --json y conserva -o para la respuesta; la ventana extrae el texto de los eventos.

La estimación local cuenta texto de sistema, turnos y esquema. El rango orientativo usa
caracteres/5 a bytes UTF-8/2: es una heurística, no un tokenizador ni una garantía de que
el conteo real caiga dentro. Imágenes, salida y contexto propio de una CLI quedan aparte.
No calcula precios ni cuota restante y no hace una llamada extra para contar tokens.

Equilibrado permite tres pedidos de escritura/revisión y Económico dos, con los mismos
contratos, modelos, capacidad de salida y pruebas. El tope configurable cuenta caracteres
de entrada acumulados: se comprueba antes de enviar, sin truncar contenido. Cero deja ese
tope desactivado. Cada continuación permite otra tanda; los caracteres y el consumo ya
registrados permanecen en el checkpoint y no se reinician al continuar. Las ideas usan
un pedido separado. retro-cli respeta el modo guardado; --intentos explícito prevalece.
Un límite de proveedor o de texto conserva el último borrador completo; el estudio lo
reanuda y el creador desde cero guarda el último código recibido, aún pendiente de corregir.
Los metadatos de una versión terminada conservan Usage opcional con esos mismos totales
y su cobertura. La interfaz permite consultarlos después de volver a abrir el programa.
Los juegos antiguos y los cambios manuales sin medición no reciben contadores inventados.

Formatos comprobados contra documentación oficial y pruebas de transporte simulado:
[Codex](https://learn.chatgpt.com/docs/non-interactive-mode),
[Responses](https://platform.openai.com/docs/api-reference/responses-streaming),
[Claude](https://platform.claude.com/docs/en/build-with-claude/prompt-caching),
[Ollama](https://docs.ollama.com/api/chat),
[Gemini API](https://ai.google.dev/api/generate-content#UsageMetadata) y
[telemetría de Gemini CLI](https://github.com/google-gemini/gemini-cli/blob/main/packages/core/src/telemetry/uiTelemetry.ts).

## D-031 Fusión y creación con originales como recorridos separados (2026-10-21)

Fusión conserva un original base; Crear ofrece desde cero o a partir de ROMs. Se reutiliza
el motor del estudio para esta última modalidad, con escena propia y sin selector de base.
La elección viaja en StudioGame.kind: fusion por defecto, inspired para nuevas creaciones
con originales. El modelo y los metadatos de una carpeta de agentes no pueden cambiarla.
La recuperación restaura también esta elección. Un trabajo activo impide cambiar de
modalidad sin terminarlo o cancelarlo, conservando su consigna y borrador.

Los nuevos inspired se guardan como vN.json/vN.js/vN.png en Juegos creados y se muestran
en Creados y Todos los juegos; los juegos en ensamblador conservan su formato. StudioLibrary
lee ambos directorios y consulta sus identificadores juntos para evitar colisiones. Los
juegos anteriores sin kind siguen como fusiones aunque no tengan baseSlot: Manic Invaders
conserva su categoría y escena. No se mueven ni reclasifican carpetas del usuario.

La separación y la autoridad de modalidad/base se probaron con IA simulada y en la app.
La navegación Spy/Pac en ambos sentidos y el prototipo vacío de biblioteca no demuestran
una fusión nueva jugable ni creatividad. Esa evaluación mecánica sigue pendiente.

## D-032 Recuperación del creador desde cero (2026-10-21)

GameWriter publica WriterCheckpoint antes de llamar, al recibir código completo y al
probarlo. GameWorkStore lo guarda atómicamente en Juegos creados/.trabajos, con máquina,
pedido, idea/semilla ya elegida, último código, autor de esa respuesta, huellas de intentos
y consumo opcional. No almacena conversación, guías, imágenes ni programas ensamblados.
ResumeAsync vuelve a probar el código con el hardware actual antes de otra respuesta;
sin código conserva el mismo plan, y las respuestas fallidas repetidas también se frenan.

CreatorWindow muestra pendientes y permite continuar, copiar código o descartarlos. Un
límite o una cancelación conserva el trabajo. Si se editó el código guardado de ese trabajo,
se prueba esa edición. WorkId en CreatedGame mantiene una carpeta por construcción al
continuar, incluso si se interrumpió la limpieza del pendiente. Descartarlo no borra juegos.
Código, programa y metadatos se reemplazan mediante temporales y renombre; una miniatura
bloqueada no impide guardar el juego. Así un corte no presenta un archivo parcial como
una edición manual. Manual/carpeta de agentes conservan su flujo de archivos independiente.
Al cerrar la app normalmente se cancelan los trabajos de estudio, creador y análisis y se espera
brevemente su salida: CliAgentProvider detiene el proceso que inició. El checkpoint
permanece para continuar. Un cierre forzado del proceso por el sistema no ejecuta esa
limpieza; no se promete detener un CLI huérfano en ese caso.

## D-033 Posición de Pac-Man y variables con un bit alto separado (2026-10-21)

El diagnóstico real de PAC-MAN1.T64 (SHA-256 1f743c4874a521d116d9d45f8401b36655339b8244d61ac1948059cd4561eee6)
mostró que $003C cambiaba al caminar pero no era la X del personaje. Se reemplazó por
la posición del sprite 4 del VIC: $D008 más el bit 4 de $D010; Y se lee de $D009.
Ambas quedan de solo lectura. El helper pacman() devuelve la caja gráfica en el área
activa, con expansión y visibilidad; incluye transparencia, no se presenta como hitbox.
Se contrastó con controles, registros e imagen en cuatro direcciones. No basta que un
byte cambie con una tecla para certificar que es la coordenada del personaje.

GameVariable admite uint9 con highBitAddress y highBitMask (una máscara de un bit).
Al escribir conserva los otros bits de ese registro; respeta rango y solo lectura.
Los campos nuevos se omiten cuando no se usan, conservando las huellas de otros perfiles.
No se modifican los bytes de ROM ni el informe histórico del laboratorio; el perfil y
la documentación aclaran que aquella hipótesis no era una posición fiable.

## D-034 Ayudas verificadas y evidencia mecánica de fusión (2026-10-21)

El perfil Pac-Man C64 expone actor(0..4), pacman() y ghosts(): registros VIC,
expansión, color y visibilidad en coordenadas activas. Se comprobaron cuatro cuerpos
de 122 píxeles del color correspondiente en la edición identificada. Son cajas gráficas,
no hitboxes ni estados de miedo, objetivos o navegación. No autorizan escribir posiciones.

Invaders agrega hitCannon(): solo escribe el estado de muerte si el cañón está vivo.
El original sigue encargado de explosión, reserva y reaparición. La prueba conecta el
agotamiento de un recurso con esa muerte, y contrasta el resultado con disparos reales.

Tres fixtures manuales, fuera de la app y de las instrucciones del diseñador, prueban
ambas bases y otro aportante, memoria e imagen nativas. PRUEBAS-FUSION.md registra
alcance y límites. No se usan como plantilla de generación ni se declara calidad de
IA por aprobarlos. Los cambios de perfil invalidan su caché y un trabajo interrumpido
con huella anterior exige comprobar sus datos antes de continuar.

## D-035 Protocolo inicial del actualizador (2026-10-21; instalación reemplazada por D-038)

Retro.Update es una biblioteca del programa, no otra aplicación para el usuario.
Consulta exclusivamente el release estable público de mauroalvare/RetroFusion; elige
una versión de tres componentes superior a la instalada y el paquete de su plataforma.
Se exige URL oficial exacta, tamaño publicado y digest SHA-256 del asset. No se usan
credenciales, herramientas de IA, instaladores externos ni servicios de pago.

Cada paquete nuevo incluye actualizacion.json, formato 1: version, runtime y files
(ruta relativa a SHA-256, sin incluir al propio registro). La extracción se hace en
una carpeta temporal nueva; rechaza rutas externas, nombres ambiguos, duplicados,
enlaces, archivos especiales y límites excedidos. Se comprueban todos los archivos.
La carpeta instalada puede tener datos propios y firmware distinto: esos datos no se
confunden con binarios del paquete. La primera versión con updater se instala manualmente.

UpdateWindow consulta al abrir, permite omitir/desactivar la actualización y ofrece
búsqueda manual en Ayuda. Sin red se puede seguir usando la versión instalada. Las
ejecuciones de prueba y --no-update no consultan releases. Preferencias del updater
en actualizaciones.json, separadas de la configuración de IA y sin modificarla.

El mismo ejecutable nuevo actúa como ayudante con --apply-update, antes de abrir video
o audio. Espera el cierre de la instancia original y rechaza otras instancias de esa
instalación. UpdateInstaller guarda cada intención antes de moverla; conserva un cursor
de recuperación para que un segundo corte durante la reversión tampoco pierda datos.
Se aparta el programa anterior, se trasladan las cuatro carpetas protegidas completas y
los archivos del usuario desconocidos por el manifiesto. No se sobreescriben colisiones.
Los ajustes de AppData y las bibliotecas externas permanecen en sus ubicaciones.

La carpeta final es RetroEmu-VERSION-RUNTIME. Solo el acceso RetroEmu del escritorio
que apuntaba a la instalación anterior se redirige; se conserva su copia para revertir.
El programa nuevo confirma su primer cuadro con --update-ready. Si no abre, se detiene
esa instancia propia y se recuperan carpeta, datos y acceso anteriores. Tras una
interrupción se revisa el diario antes de iniciar. .retroemu-updates conserva respaldos;
no se limpia automáticamente. El usuario retuvo la publicación de 0.2.1.


## D-036 Tiempos locales de construcción (2026-10-22)

AiTiming mide una operación completa con reloj monótono. El historial tiempos-ia.json
es independiente de la configuración de conexión: diccionario de clave SHA-256 a lista
de segundos, como máximo 128 grupos y 20 duraciones por grupo. La identidad combina
herramienta/modelo elegidos, esfuerzo, límite de intentos, ventana y operación (ideas,
diseño, cambios o continuar). No recibe prompts, respuestas, ROMs ni rutas de juegos.

Solo las tareas satisfactorias entrenan el estimado; cancelar, fallar o reabrir un panel
no añade muestras. A partir de tres muestras se amplía el intervalo de duraciones
recientes y se resta el tiempo transcurrido. Al superarlo se informa el exceso, sin
presentar un contador detenido en cero. La etapa/intento siguen siendo los mensajes
reales del diseñador; el intervalo corresponde al trabajo completo, no a cada llamada.
Un archivo dañado, bloqueado o sin permiso nunca impide crear/guardar el juego.

## D-037 Identidad y actividad de biblioteca (2026-10-22)

`biblioteca.json` en AppData tiene Version=1 y Games: diccionario de clave hash a
Favorite y Played (UTC opcional). Original: sistema más hashes SHA-256 de los miembros
de medios, ordenados y sin repetidos; no incluye nombres/rutas. Fusión: Id estable;
creado: sistema y WorkId, o fecha UTC de creación si no hay WorkId. Se separan los tres
tipos antes de derivar la clave. Los bytes se aprovechan del escaneo/carga existentes.

Se retienen 40 recientes y hasta 4096 favoritos, aunque algún archivo no esté disponible.
La interfaz cruza esas claves con las bibliotecas actuales. Solo se registra una carga
satisfactoria; una sesión con error de guion no se agrega. Vaciar recientes conserva
favoritos y archivos. Escritura temporal seguida de sustitución atómica; un error deja
la selección en memoria e informa al jugador. Metadatos dañados/futuros no se sobrescriben.
No contiene ROMs, títulos, rutas ni ajustes de conexión; el updater conserva AppData.

El modo --autotest usa actividad en memoria: las verificaciones automáticas no leen
ni modifican los favoritos y recientes del jugador.

## D-038 Actualizar en la carpeta en uso (2026-10-22)

El pedido vigente reemplaza el cambio de carpeta por versión: la actualización debe
conservar la raíz instalada, también al llegar desde 0.2.2. El manifiesto del paquete
sigue en formato 1. El diario pasa a formato 2: OldRoot y NewRoot coinciden; PayloadRoot
está en Job/paquete y CandidateRoot en Job/instalar. El ayudante corre desde el paquete
inmutable, para poder reemplazar y recuperar binarios de Windows sin bloquearlos.

Solo se respaldan/reemplazan archivos del manifiesto del programa cuyo hash cambió. Las carpetas de
juegos, firmware existente, estados y archivos desconocidos permanecen en su lugar.
Se agregan archivos protegidos del paquete solo si faltan. Se rechazan colisiones
con archivos propios. Un registro anterior independiente valida el alcance del diario;
cada intento se anota antes de mover y la reversión usa cursor persistente.

El paquete que preparó el actualizador publicado se verifica y adopta antes de instalar:
formato 1 preparado -> copia interna -> relevo al mismo ejecutable allí -> espera del
proceso anterior -> descarga vieja apartada dentro del trabajo -> instalación formato 2.
No se renombran ni borran instalaciones de terceros. Se conservan recuperación y lectura
del formato 1 para diarios anteriores. El primer reinicio usa --no-organize mientras
confirma el arranque; --recover-update ejecuta la recuperación fuera de los binarios
en uso. El acceso existente sigue apuntando a la misma ruta.

Los archivos idénticos del runtime se conservan: no se renombran innecesariamente
bibliotecas que Windows pueda retener. Un bloqueo transitorio tiene reintento acotado;
no se cambian permisos para forzar el reemplazo. Si falta un archivo protegido del
paquete, se puede agregar sin alterar los que ya conserva el usuario.

## D-039 Eliminar temporales tras confirmar la actualización (2026-10-22)

El usuario rechaza conservar oculta la carpeta de trabajo junto a la instalación.
No se oculta como mecanismo de limpieza. Tras el primer cuadro confirmado y la salida
del ayudante, el proceso instalado elimina los trabajos terminados de formato 2,
incluido el respaldo, y quita .retroemu-updates si queda vacía. No hay otro ejecutable.
Los bloqueos globales de preparación/aplicación y la comprobación de procesos impiden
borrar trabajos activos. Antes de autorizar la eliminación se verifica la instalación.

Un comprobante limpiar-ID.json, limitado a un ID GUID y nombre de instalación bajo
la carpeta de trabajo, permite completar un borrado interrumpido. No admite rutas
libres ni enlaces. Las descargas propias canceladas o inválidas también se descartan;
una preparación interrumpida se reconoce por su comprobante. Un plan preparado válido,
una recuperación pendiente, otros programas y respaldos completos de formato 1 se
conservan. El borrado de la raíz nunca es recursivo: archivos ajenos la mantienen.

## D-040 Aceptación de interrupciones del 8088 (2026-10-22)

El núcleo conserva el constructor de un bus y la inyección de vector de diagnóstico.
Los dispositivos usan I8088InterruptSource: exponen INTR y resuelven el vector en
Acknowledge solo cuando IF y las sombras permiten aceptarlo. No se consumen pedidos
mientras están inhibidos. El contrato representa una secuencia funcional, no pulsos INTA.

TF se muestrea antes de la instrucción; su trampa pendiente se acepta en otro Step
sin sumar Instructions. Las interrupciones externas tienen prioridad y la trampa puede
anidarse antes de la primera instrucción de su manejador, como documenta el 8088.
MOV/POP hacia cualquier segmento admitido protegen la instrucción siguiente también
de NMI/TF. HLT conserva la trampa pendiente hasta ser reanudado por una interrupción externa. No se añaden ciclos ficticios.

## D-041 Primer componente PC: 8259A individual (2026-10-22)

Se incorpora Retro.Systems.Pc y Pc.Tests para desarrollar los dispositivos sin presentar
como disponible una máquina PC incompleta. Pic8259A implementa I8088InterruptSource;
su interfaz separa comandos/datos de puertos, cuyo mapeo corresponde a la placa.
Inicialización individual 8086 por flancos, máscaras, prioridades, EOI/rotación, AEOI,
máscara especial, estado y poll son funcionales; no se asignan ciclos ficticios.
Los modos de nivel/cascada/8080/buffer/SFNM no soportados fallan explícitamente.
La combinación poll/AEOI también se rechaza hasta contrastar sus efectos; no se
inventa cuándo borrar ISR en un modo de consulta sin pulsos INTA.

El propio programa de cpu8088 irq-demo inicializa los puertos 20h/21h y atiende pedidos
que se inyectan mientras espera en HLT. Los manejadores propios incrementan contadores,
mandan EOI y vuelven por IRET. El diagnóstico tiene presupuesto y resultado verificable.
No requiere juegos, ROMs ni descargas. Las pruebas recorren pedidos/máscaras exhaustivos
y combinan el controlador con el 8088, incluidas prioridad, anidación y trampa pendiente.

## D-042 Temporizador 8253 con reloj externo (2026-10-22)

Pit8253 comparte Retro.Systems.Pc con el PIC. WriteControl, WriteCounter y ReadCounter
separan E/S del mapeo de puertos que decide la placa. Cada Tick(channel) representa un
pulso completo externo, con GATE estable. Instructions del 8088 nunca se usa como reloj.
SetGate actúa entre pulsos; OutputChanged comunica también los cambios de programación
y GATE, para no perder flancos al conectarlo al PIC. Notificación síncrona sin reentrada.

Se implementan contadores binarios en modos 0/2/3 y alias 6/7: carga en un pulso propio,
divisor cero = 65.536, recargas, pausa/reinicio y latch. La cuenta impar del modo 3 sigue
el 8253, sin sustituirla por el 8254. Las transferencias de palabra se completan antes de
otra operación de carga/latch; los usos indefinidos se rechazan. Reprogramar el modo
permite abandonarlas. Reset es determinista de placa; el silicio carece de pin RESET.

Modos 1/4/5, BCD, divisor 1 en modos 2/3, divisor 3 en modo 3 y read-back se rechazan
explícitamente. No se simulan tiempos eléctricos ni se ofrece PC/XT o DOS. Fuentes,
semántica y pruebas en [PIT8253.md](PIT8253.md).

El CLI agrega cpu8088 timer-demo: un programa propio configura PIT/PIC por puertos y
maneja tres IRQ0 con EOI/IRET; lee el contador con latch y verifica la pila. El ejecutor
entrega pulsos durante HLT y congela ese reloj al ejecutar instrucciones. Es un cronograma
explícito de diagnóstico, nunca temporización de PC. --steps presupuesta ambas acciones
y la salida 0 exige el resultado completo; 2 indica presupuesto agotado. La conexión
escucha todos los cambios de OUT0, incluidos programación y GATE; el invitado programa
el PIT antes de inicializar el PIC para descartar aquel flanco de arranque.

## D-043 Análisis de modificaciones con evidencia y deshacer (2026-10-22)

ModificationObservation registra hasta 2.048 direcciones de RAM mientras se observa
la partida, conservando el observador previo. La captura copia valores, escrituras y
hasta 160 instrucciones; el pedido incluye hasta 128 bytes candidatos. El proveedor
recibe datos separados de la máquina y no puede escribirla.

GameModificationAnalyzer usa IAiProvider sin distinguir proveedores. Su respuesta
incluye explicación, hasta ocho variables nuevas y cuatro propuestas. Las direcciones
y valores deben coincidir con la evidencia; se conservan restricciones de variables
conocidas. Solo se aceptan bytes y palabras little endian en RAM de trabajo del sistema.
Las nuevas definiciones son hipótesis de sesión, no perfiles verificados de fusión.

GameModifications agrega Undo y CanUndo: conserva los cinco cambios más recientes
con estado, imagen y congelados. Restore conserva el punto anterior a la primera
prueba incluso si el historial se recorta. Dos cambios o congelados que comparten
bits se rechazan antes de escribir; máscaras disjuntas siguen permitidas.
El análisis no prueba el significado ni la jugabilidad de un valor. La aplicación
continúa siendo explícita; no modifica los archivos originales.

## D-044 Navegación y proyectos de creación (2026-10-23)

Biblioteca es el acceso a las colecciones. Juego reúne archivos, estados y ejecución;
Crear contiene las tres modalidades; Herramientas reúne modificaciones y depurador;
Opciones contiene conexión de IA, sonido y pantalla. Los espacios de trabajo se alternan
sin cancelar sus tareas; siguen procesando resultados cuando el panel está cerrado.

CodeWindow distingue cuatro páginas: Cambios, Analizar con IA, Buscar variables y Código.
El selector de proveedor y modelo está junto al pedido. La reconstrucción local de rutinas
se nombra aparte de la consulta a IA. El modo copiar/pegar de rutinas usa un estado explícito,
sin depender del texto de un mensaje. WorkspaceLayout mantiene paneles dentro del área visible.

El estudio usa una consigna tanto para pedir ideas como para construir la propuesta
del jugador. La IA se elige antes de preparar originales. El creador separa proyecto nuevo
de proyectos guardados; abrirlo no exporta demos sin una acción del jugador.
Versiones, perfiles y disponibilidad se cachean al abrir, guardar o actualizar; la
estimación de contexto se calcula como máximo una vez por segundo, no en cada cuadro.

Los hallazgos de IA son definiciones privadas de sesión; guardar controles o una variable
manual excluye esas hipótesis. Los perfiles manuales conservan hashes del medio cargado.
Tanto el parseo como la aceptación de un resultado tardío comprueban que nuevas
definiciones no invadan nombres o direcciones ya conocidos y sus restricciones.
App.Tests agrega pruebas del recorrido completo sin abrir una ventana ni llamar IAs reales.

## D-045 Respuestas del analizador y procedencia de informes (2026-10-23)

El analizador normaliza direcciones hexadecimales de cuatro cifras sin prefijo, con 0x
o con $. La notación no cambia las comprobaciones de evidencia, valores, RAM, alias,
rangos ni solo lectura. El esquema publica también los límites de textos que exige
el parser. Request construye una única consigna para CLI y modos manuales.

Cada intento conserva su pedido y, cuando existe, la respuesta original. El informe
aceptado mantiene proveedor, cuadro y hora propios: un fallo o cancelación posteriores
no lo hacen pasar por un resultado nuevo. Cambiar de partida descarta sus detalles.
La interfaz permite copiar mensajes, informe, pedido, respuesta, error y salida del CLI.
Los diagnósticos permanecen en memoria; no se escriben registros de conversaciones
automáticamente ni se repite un pedido para obtenerlos.


## D-046 Congelado durante el cuadro y confirmación de propuestas (2026-10-23)

Reponer un valor solo al empezar el cuadro permite que el programa lo sobrescriba antes
de consumirlo. `GameModifications` conserva los bits fijados después de cada escritura
CPU y antes de su siguiente lectura, incluida la copia DMA de sprites. La escritura
normal de CPU y sus ciclos siguen ejecutándose; la reposición usa el acceso de depuración
sin efectos de entrada/salida. Se conserva `Tick` para accesos directos fuera de la CPU.
Los espejos de RAM se normalizan al detectar solapamientos y mantener el congelado.

`CpuWriteObservers.Subscribe` comparte `IWriteWatch` entre herramientas en el hilo de
emulación, conserva el observador previo y permite retirar suscripciones en cualquier
orden. No reemplaza un observador externo instalado después. `GameModifications.Dispose`
libera sus congelados sin cargar un estado anterior. Cerrar el panel conserva el efecto;
cambiar de sesión o salir lo libera. `EmulatorHost.SessionChanging` avisa antes de que
un cargador ejecute el arranque de otro medio o un reinicio; para estados externos avisa
tras aceptar la carga. Deshacer dentro del panel conserva su selección previa de congelados.

Cada propuesta confirma o rechaza la aplicación junto al botón y muestra el valor actual.
El aviso de pausa refleja el estado presente. Una escritura confirmada no certifica el
significado de la variable: las hipótesis siguen necesitando comprobarse en la partida.


## D-047 Evidencia temporal y variables compartidas con el estudio (2026-10-23)

La captura de análisis conserva intervalo de cuadros, rango escrito, cambios de valor
y hasta seis últimos valores distintos consecutivos por byte. Son escrituras de CPU,
no valores verificados de reglas ni acciones del jugador. La admisión acotada de2048
bytes reparte espacio entre páginas; la selección de128bytes combina perfil, referencias
y regiones diversas. El listado de160instrucciones prioriza contexto alrededor de los
escritores y marca los puntos cuya ejecución no está registrada. Las copias asíncronas
son independientes de la máquina. Observar desde ahora reinicia solo esa evidencia.
No cambia el esquema de respuesta de la IA ni convierte hipótesis en perfiles verificados.

VariableMemory comparte huella de bytes/máscaras, espejos y uint9 de bit alto separado.
El parser rechaza alias entre nuevos hallazgos y las propuestas rechazan solapamientos
antes de mostrarse. FrozenVariables mantiene bits sin estados completos; GameModifications
conserva encima su undo5 y restauración. El laboratorio de preparación se suscribe al
observador común sin reemplazar herramientas existentes.

El estudio incorpora g.freeze(nombre,valor) y g.release(nombre opcional), disponibles
también para juegos creados con originales. g.set conserva su ajuste al rango y libera
el congelado de esa variable; valores no finitos y permisos/formato inválidos se rechazan.
Las escrituras se verifican por lectura; un fallo no deja un lote parcialmente escrito.
Reiniciar un original o toda la sesión libera sus pins antes de cargar el arranque;
init puede declararlos de nuevo. Un fallo del guion también los libera. No se añade
undo de guiones ni un formato de estado nuevo. El generador autónomo de ensamblador
no usa máquinas originales ni necesita este congelado; conserva su contrato propio.

## D-048 — Historial optativo por edición y reaplicación reversible

El análisis, sus variables experimentales y la selección de cambios pertenecen al jugador.
No se descartan al reiniciar cuando se elige conservarlos. Se guardan en AppData/RetroEmu/
modificaciones, separados de los perfiles preparados de fusión y de los paquetes del programa.

El documento privado formato 1 contiene clave SHA-256 de sistema/medios/firmware,
definiciones de variables, etiquetas experimentales, hasta doce informes con proveedor,
fecha, cuadro, pedido y propuestas, selección de valores pedidos y opción de reaplicación.
No contiene ROMs, volcados de RAM, listados de código, credenciales ni puntos de regreso.
El guardado usa reemplazo atómico; un formato futuro o dañado no se sobrescribe.

Guardar no activa cambios al abrir: reaplicar requiere otra opción explícita por juego.
El cargador se ejecuta sin los congelados anteriores. Al restaurar la sesión se valida
la identidad y las definiciones del perfil antes de importar; otra edición o firmware
usa otra clave. Una selección se aplica como transacción y se puede deshacer en un paso.
Los estados completos y cinco pasos de deshacer siguen siendo propios de cada sesión.

## D-049 — Bancos NES y estados vinculados al cartucho

La primera ampliación añade mappers 2 (UxROM) y 7 (AxROM), con bancos PRG y espejado
por cartucho. Los parches del visor actúan en el banco visible. Reiniciar la CPU no
borra los registros de la placa; un reinicio total vuelve a su selección inicial.

El estado NES formato 3 incluye el SHA-256 de la imagen iNES original y rechaza otra
edición antes de modificar memoria. Guarda también registros de los nuevos mappers.
Sigue leyendo formato 2 para mappers 0/3; ese formato anterior no identifica el juego.
La ampliación no acredita variantes de placa, conflictos de bus ni temporización completa.
Referencias: [UxROM](https://www.nesdev.org/wiki/UxROM),
[AxROM](https://www.nesdev.org/wiki/AxROM).

## D-050 — Contraste funcional externo del 8088

`cpu8088 verificar --suite <carpeta-v2> [--salida informe.json]` compara registros y
memoria con SingleStepTests/8088. La suite se obtiene por separado; el comando no
hace descargas ni usa IA. Se respetan estados y máscaras de metadatos, se excluye OF
indefinido de desplazamientos múltiples y se enmascaran los flags indefinidos que
un error aritmético apila. El informe separa aprobados, fallidos y excluidos.
No acredita ciclos, señales de bus, prelectura ni los casos excluidos.

Las regresiones de hardware corrigen DAA/DAS en entradas no BCD, el byte de base de
AAM/AAD, los selectores de segmento y REP sobre IDIV. Origen de los datos y revisión:
[SingleStepTests/8088](https://github.com/SingleStepTests/8088/tree/aea84484abc79d09639d855b7b0ab32bc9e4dbeb).

## D-051 — Herramientas 8088 y direcciones segmentadas

ICpuDebugView.HasCycleTiming distingue un reloj medido de un núcleo funcional: el 8088
expone cero ciclos y declara que no tiene temporización. Su estado RETRO-8088 versión 1
guarda registros, interrupciones y esperas a frontera de instrucción; no contiene RAM
ni dispositivos y valida los datos antes de cambiarlos. WAIT acepta TEST y ESC realiza
la lectura de memoria sin emular un coprocesador. SetLock representa propiedad lógica
del bus, sin pulsos ni tiempos eléctricos.

IFlowAnalyzer.AddressBits conserva 16 por defecto y admite 20 para el 8088. FlowInfo.Next
opcional permite el salto de offset al finalizar un segmento sin alterar los analizadores
anteriores. El desensamblador puede recibir CS para el visor en vivo; un mapa estático
por dirección física no reconstruye todos los alias de segmentos de un programa.
Estas herramientas no convierten el núcleo en una máquina PC/XT ni acreditan su reloj.

## D-052 — MMC1 y validación de cartuchos sin cargar sus bancos

MMC1 añade transferencia serie de 5 bits con registro elegido por la quinta dirección,
reset D7, filtro D0 de ciclos consecutivos del 6502, bancos PRG de 16/32 KB y CHR de 4/8 KB,
cuatro modos de espejado y deshabilitación de PRG RAM. El ciclo opcional de CpuWrite
conserva los llamadores anteriores; las escrituras directas sin ciclo no simulan ese
filtro. Los estados 3 guardan también la transferencia incompleta y su último ciclo.
Las placas SEROM mantienen 32 KB fijos; SUROM extiende la selección a 512 KB. Las variantes
no implementadas se explican en LIMITACIONES; no acredita compatibilidad de todas las
placas ni persistencia de batería entre sesiones.

ValidateImage comparte la validación usada por Load, sin copiar ni calcular el hash
de la ROM. El catálogo usa esta vía al inspeccionar candidatos; solo la carga real
crea bancos e identidad SHA. No cambia la firma de los archivos de juego.
Referencia: [MMC1](https://www.nesdev.org/wiki/MMC1).


## D-053 — Ranuras por contenido y acceso a guardados anteriores

Juego > Partidas guardadas reúne cinco ranuras con fecha y acciones de guardar/cargar.
La carpeta states/<SHA>/slotN.rfstate separa sistema, medios y firmware, usando la
misma identidad por contenido que el historial de modificaciones. Renombrar un medio
conserva sus ranuras. Una sesión sin medio tiene identidad propia por sistema/firmware.
El estado binario y su escritura atómica no cambian.

Los archivos anteriores states/<maquina>_slotN.rfstate siguen disponibles en un grupo
separado para carga. No se migran ni se sobrescriben automáticamente porque no identifican
el juego. El guardado rápido conserva su alcance temporal. La lista se actualiza al abrir,
guardar/cargar o cambiar la sesión, sin inspeccionar archivos en cada cuadro de interfaz.


## D-054 — Memoria y bus parcial PC/XT

PcMemory separa RAM instalada, RAM de vídeo CGA con su espejo y un BIOS de 8–64 KB
alineado al final de 1 MB. Protege y copia el firmware aportado, identifica sus bytes
por SHA y no inventa RAM en los huecos. PcBus conecta 8259A/8253, máscara NMI por bit 7
en A0–AF y páginas DMA; estas últimas todavía no realizan transferencias.
TickPit es un pulso externo, independiente del contador de instrucciones. No se
registró una máquina jugable ni se incluyó firmware en la distribución. Los puertos
pendientes fallan explícitamente durante el diagnóstico. Alcance y orden restante en
[PCXT.md](PCXT.md);no implica POST completo, DOS ni temporización validada.


## D-055 — MMC3 e interrupciones desde el bus PPU

Mapper 4 implementa MMC3B/C, selección de bancos PRG/CHR, WRAM protegible y espejado
con cuatro pantallas respetadas. La IRQ comparte la línea de CPU con la APU.
ClockPpuDot avanza tiempo incluso en blanking; ObservePpuAddress recibe los cambios
de dirección del calendario de fetches y de los registros CPU. Leer patrones para
renderizar una línea o inspeccionarlos no debe generar flancos adicionales.
El filtro temporal se aproxima en puntos PPU; sus límites están en LIMITACIONES.

El formato NES 3 incorpora registros y filtro solo para mapper 4, que no era cargable
en versiones anteriores. Los estados previos de los otros mappers conservan sus bytes.
Se rechazan submappers y RAM no implementados, sin asumir que MMC6 equivale a MMC3.
Referencias: [MMC3](https://www.nesdev.org/wiki/MMC3) y
[Programación MMC3](https://www.nesdev.org/wiki/Programming_MMC3).


## D-056 — Reloj 8088 optativo y estado CPU 2

TimingEnabled activa contador T, byte bus, cola de cuatro bytes y Clock/HoldRequested
en I8088Bus. Por defecto sigue desactivado para el verificador funcional y sus buses.
HasCycleTiming informa esa selección; Instructions conserva su contrato independiente.
PcBus divide T por cuatro para alimentar el PIT sin usar Instructions como reloj.

La precisión EU es nominal y tiene limitaciones explícitas en I8088.md: no se presenta
como contraste ciclo a ciclo del hardware. El estado CPU 2 incluye cola, T y repetición;
la lectura de versión 1 continúa disponible sin temporización. La carga valida antes
de mutar registros y no toca el bus ni los dispositivos de la máquina.


## D-057 — CGA/6845 en el bus PC parcial

Cga6845 recibe tres pulsos de oscilador por T de PcBus. DE/VSYNC progresan desde los
registros del controlador, sin alternar bits por lectura de puerto. La memoria CGA
existente alimenta texto y gráficos RGBI. Fuente opcional aportada por el llamador,
sin incorporar una ROM comercial. Los huecos MDA son una tarjeta ausente, no un
controlador de vídeo alternativo.

Se prueba el recorrido 8088 temporizado → PIT → PIC → salida de HLT → rutina de
interrupción, manteniendo separado Instructions de T. El firmware libre avanza hasta
el próximo dispositivo pendiente; no se interceptan sus servicios ni se omite POST.


## D-058 — PPI y teclado PC/XT

Ppi8255 implementa modo 0 y BSR con latches separados de las entradas. PcBus selecciona
el cableado de interruptores 5150/5160 mediante constructor; no interpreta un firmware
como si ambos modelos tuvieran los mismos puertos. Port B alimenta GATE2 y control de
teclado, y la recepción completa genera IRQ1 hasta el pulso de clear.

PcKeyboard recibe bytes set-1 aportados por el host. La transmisión consume reloj,
el latch bloquea el siguiente byte y un inhibit prolongado inicia BAT del teclado.
No hay respuestas condicionadas a una dirección BIOS. El firmware real avanza hasta
DMA pendiente, sin saltarse comprobaciones ni interceptar servicios.


## D-059 — DMA 8237 con concesión de bus

SetBusGranted en I8088Bus separa petición HOLD de HLDA: Dma8237 no transfiere durante
un ciclo activo de CPU ni LOCK. PcBus concede con reloj T y mantiene la página externa
al envolver el contador de 16 bits. PIT1 pide refresco por canal0; el reconocimiento
limpia ese pedido de placa. Los periféricos conectan bytes y TC por callbacks.

La emulación conserva registros y modos reales, sin saltarse pruebas del firmware.
Memoria a memoria, cascada y variantes de señales/timing no implementadas se rechazan.
ICW4 buffered del PIC individual controla sus propiedades de transceptor; el resto
de la lógica sigue siendo un solo controlador por flancos, sin SP/EN temporizados.


## D-060 — Disquete MFM y capa de escritura privada

Fdc765 conecta fases de comando, ejecución y resultado, DRQ/DMA2 e IRQ6 sin servicios
BIOS interceptados. Los puertos de adaptadores que no están instalados leen FF; las
funciones faltantes de dispositivos sí presentes fallan de forma explícita.
La placa usa el cableado XT de READY, con un reporte de reset, sin polling de una
controladora AT. Los seeks conservan busy hasta Sense Interrupt Status.

FloppyDiskImage copia e identifica los bytes aportados y guarda solo cambios por
sector. LoadOverlay valida identidad, índices y contenido antes de reemplazar la capa.
No escribe la fuente. Las limitaciones de formato y densidad están en PCXT.md.
Las pruebas reales de sistema operativo quedan en evidencia privada y no se empaquetan.


## D-061 — PcXt y estado de placa completo

PcXt implementa IMachine e IMemoryView de 20 bits. Runtime es dueño de placa, CPU,
presentación y callbacks del audio: restaurar no deja eventos ligados al runtime viejo.
Los dispositivos cargan campos sin disparar eventos; las líneas PIC/DMA y los latches
ya representan los niveles guardados. Se publica el nuevo runtime solo tras validar
todo, incluida coincidencia de reloj CPU/placa, longitud e integridad SHA.

RETRO-PCXT 1 incluye medio por SHA y overlay, firmware/modelo/RAM, CPU y componentes,
prelectura y transferencias parciales, sonido y teclas físicas. Al cargar se liberan las
teclas físicas y se cancela la secuencia pendiente de entrada. Reset conserva el disco.
La memoria no implementa un watch de 16 bits: no se truncan direcciones físicas para
fingir compatibilidad con ese contrato anterior de ingeniería inversa.


## D-062 — Carga efectiva de uno en PIT

Una partida real programa PIT2 en modo 3 con cuenta 1. Rechazarla abortaba el juego.
Se acepta la escritura y se usa carga efectiva 0 en modo 3 y 2 en modo 2, según las
observaciones de hardware documentadas en la implementación MAME. El registro de
carga no es legible directamente; lecturas, GATE y recargas ven el contador efectivo.
El caso 3 del 8253 y las variantes no contrastadas mantienen su límite explícito.


## D-063 — PC/XT en biblioteca y direcciones de depuración

MachineLauncher habilita pcxt con BIOS aportado en firmware/pcxt/pcxt-bios.rom
(también acepta el nombre en la raíz del firmware) y disquetes IMG/IMA de 160–720 KB.
Reconocer EXE/COM no equivale a cargarlos: necesitan un disco con DOS y el programa.
Cada apertura crea una máquina y vincula los estados al disco y al firmware por SHA.

El host, los mapas de ejecución, las vistas y los parches usan AddressBits; RunTo acepta
uint y StepOver respeta FlowInfo.Next. Los perfiles y observadores de variables actuales
siguen limitados a 16 bits: PC no se ofrece en Preparar ni en el catálogo de fusiones,
y el análisis de variables se bloquea antes de llamar a un proveedor. El código y la
memoria de PC sí son accesibles. No se truncan direcciones para salvar ese límite.
F1–F10 llegan a PC; Ctrl+Fn controla el host y Ctrl+Shift+O/L/M/K abre herramientas.
Los parches NOP usan el opcode de la CPU: 00 en Z80/8080, EA en 6502 y 90 en 8088.

El CLI run reconoce IMG/IMA y acepta --bios <ruta>; sin esa opción busca el BIOS
en las carpetas de firmware habituales. info distingue reconocimiento de carga.


## D-064 — Pulido de la interfaz en 0.3.0

La biblioteca conserva las mismas secciones y acciones en diseño ancho y compacto.
Los avisos de formatos pendientes forman parte de la fila. FileBrowser toma extensiones
del catálogo, separa selección/apertura y comprueba existencia al confirmar. No carga ni
mueve archivos al seleccionarlos. La instantánea evita enumeraciones por cuadro.

La tipografía procede del sistema o de ImGui, sin incorporar fuentes al paquete. Las
ayudas usan texto sin formato y ancho limitado. El pie reabre trabajos existentes y el
progreso permanece al comienzo del recorrido. Mostrar trabajo no llama al proveedor.
Guardar sobre una ranura requiere confirmación ligada a identidad/revisión actuales;
no cambia el formato del estado ni sus rutas. La versión sigue siendo 0.3.0.


## D-065 — Arranque libre y apertura directa de programas DOS

Se incluyen GLaBIOS/FreeDOS/FreeCOM como componentes independientes ejecutados en el
PC emulado, con fuentes y licencias en el mismo paquete. Los juegos permanecen externos.
PcDos construye FAT12 privado y un cargador propio; no se interceptan servicios DOS.
MediaArchive.Open agrega keepAuxiliary opcional (false conserva el recorrido anterior).
StartPc acepta el conjunto de archivos opcional para conservar datos del ZIP. Los estados
usan la identidad del disco generado; las imágenes de usuario mantienen su contrato previo.
El BIOS aportado por el usuario prevalece sobre el incluido. El sistema incluido vive
fuera de firmware/, que el actualizador protege como datos del jugador.
La versión sigue en 0.3.0 por pedido expreso. Quien ya tenga esa versión descarga el
paquete revisado manualmente; no se cambia el protocolo de versiones del actualizador.

El organizador conserva los EXE/COM sueltos en su ubicación para no separarlos de sus datos.

## D-066 — Selección explícita de programas y contexto de apertura (0.3.1)

El CLI aplica --pick al índice publicado por info antes de elegir la máquina;
rechaza índices inválidos con código 2. El conjunto original sigue disponible para
los datos DOS. La app pide elegir cuando un paquete de PC contiene varios programas,
distingue opciones por máquina/origen y conserva el contexto anterior hasta una carga
correcta. Cancelar restaura la pausa previa. No cambia formatos de perfiles o estados.

## D-067 — Publicar requiere indicación expresa del usuario

Preparar código, compilar, probar, escribir notas y generar paquetes locales no autoriza
publicar una entrega. Crear o modificar un release, subir o reemplazar sus archivos y
publicar sus etiquetas requiere que el usuario lo indique expresamente para esa entrega.
El permiso de una versión anterior no se hereda. Hasta entonces se informa qué quedó
preparado y se conserva el candidato verificable, sin alterar la entrega pública vigente.

## D-068 — Preparación dirigida y revisión de una idea con datos actuales

Una propuesta con missingData ofrece preparación automática en lugar de una acción
deshabilitada. El objetivo llega al arranque, a la observación y al asesor opcional de
experimentos; los planes declarativos se limitan a controles y duraciones comprobables.
Goal y las listas de datos comprobados/pendientes son campos opcionales compatibles
con el recorrido previo. Un arranque correcto no certifica todas las capacidades.

La revisión conserva idea, pedido, modalidad y ranuras, y recibe los perfiles actuales.
Las citas comprueban que el recurso existe; su interpretación requiere evidencia y el
validador normal. Escritura, revisión y preparación comparten un presupuesto acotado.
Si falta respaldo, se conserva el requisito y no se escribe un sustituto genérico.
Cambios de originales, perfiles o configuración invalidan resultados en curso; cancelar
no crea una fusión. Los perfiles parciales de la edición exacta pueden enriquecerse;
los ayudantes útiles de los aportantes se conservan. No se modifica el formato de juego.

## D-069 — Variante 8086 explícita en el núcleo compartido

I8088Cpu conserva los constructores de 8088 y añade I8088Model inmutable para elegir
Intel8086. Se comparte el motor de instrucciones; la variante cambia el ancho de bus
y la cola de prelectura. En 8086, palabras pares usan una transferencia y las impares
dos, sin conceder HOLD entre sus mitades. El contrato de I8088Bus sigue siendo de byte:
ambas mitades de una transferencia alineada se entregan al mismo reloj.

Los estados 8088 mantienen firma/formato; el 8086 usa RETRO-8086 formato 2 y valida
su cola de seis bytes. Cargar otra variante falla antes de mutar el estado. PC/XT sigue
usando 8088 y conserva su formato de estado completo. cpu8086 expone solo diagnóstico
de programas crudos; no cambia la máquina de run ni anuncia otra placa en la biblioteca.
La temporización EU sigue aproximada y las trazas externas de 8088 no validan el 8086.

## D-070 — Datos en T3, cierre de bus en T4 y contraste parcial de trazas

El bus temporizado toma memoria/E/S en T3 y termina cada ciclo de cuatro T antes de
cederlo. La prelectura guarda el dato en T3 y lo incorpora a la cola en T4. BusAccessed
observa bytes sin reemplazar dispositivos. InitializePrefetchQueue permite iniciar
diagnósticos con la cola del caso, únicamente antes de cualquier reloj/instrucción.

El estado CPU 3 agrega el dato ya tomado y evita repetir lecturas al restaurar entre
T3/T4. Conserva firmas por variante y lectores 8088 de formatos 1/2 y 8086 de formato 2;
una prelectura antigua sin dato guardado se completa una vez en T4. Esto amplía D-069.
El contenedor de estado de placa PC/XT sigue en versión 1.

cpu8088 verificar-bus distingue accesos de datos e intervalos relativos al primero.
Separa casos sin pares de intervalos; entradas vacías/inválidas no pasan. No valida
el reloj absoluto, prelectura, INTA, señales eléctricas ni estado final. Las diferencias
internas de lectura/modificación/escritura se conservan explícitas, sin declararlas
resueltas por coincidir valores o por pasar la batería funcional.

## D-071 — Opciones de salida y depuración coherente para 0.3.2

Las preferencias de sonido tienen documento versión1 propio: volumen0–100, silencio,
silencio al perder el foco y margen de1/2/4 cuadros. Se guardan atómicamente al cambiar
opciones; un archivo ilegible se conserva y los cambios quedan en la sesión. Autotest
no lee ni escribe preferencias reales y --mute fuerza salida cero en ese inicio.
El callback siempre consume la cola; volumen y silencio usan transición corta.
La reserva cambia el pacing del host, no las instrucciones o relojes de las máquinas.

La cola copia tramos contiguos preservando FIFO, capacidad, descarte y fade previo.
La caché de vídeo compara contenido y usa conversión vectorial/escalar equivalente;
una textura nueva invalida la caché. Ninguna omite restauraciones por fiarse de FrameCount.

El editor valida anchos8/16/20bits y rechaza desbordes antes de mutar registros o bus.
StepOver clasifica llamadas con IFlowAnalyzer, sin inferir la arquitectura por el
nombre visible. El paquete local se numera0.3.2; el release público sigue0.3.1 hasta
que el usuario indique publicarlo. La preparación de paquetes no es una autorización.


## D-072 — Investigación iterativa y tiempo previo para una fusión

La preparación de una idea no termina automáticamente después de su primera revisión.
Los requisitos que siguen faltando guían nuevas rondas, conservando idea, base, pedido
y modalidad. La estimación aparece antes de iniciar; el trabajo comparte una hora
entre preparación, referencias, escritura y comprobación, con reserva para terminar.

PrepareOptions admite perfil previo, continuación privada por máquina/SHA-256,
historial y número de ronda. PrepareResult devuelve continuación y resultados de
experimentos. PreparationExperimentQuestion informa rondas anteriores y variables
disponibles; el esquema declarativo de entradas mantiene sus límites y no acepta
código de host ni escrituras propuestas por la IA.

Las pruebas se comparan y verifican restaurando el estado incluso al cancelar.
Después se conserva el avance real de la última secuencia válida. Una firma por
entradas y estado evita repetir la misma prueba cambiando solo su descripción.
La revisión y la escritura pueden devolver requisitos al mismo ciclo de investigación.
Los cambios de texto o fechas del perfil no cuentan como capacidades nuevas.

El mismo plazo y AiOperation alcanzan todas las etapas. Se detiene por cancelación,
plazo, ausencia repetida de pruebas/capacidades nuevas o bloqueo de escritura repetido
sin evidencia adicional. Nunca se habilita una fusión por agotamiento del tiempo.
Las rondas completadas y los borradores se conservan; la continuación de partida
es privada y permanece en memoria, sin sustituir partidas guardadas del jugador.

Un cambio externo del perfil se detecta antes de guardarlo; se conservan campos
previamente comprobados y solo se mejora una observación de solo lectura si coincide
el campo completo verificado. Otra edición no hereda esos datos. Las CLI del estudio
admiten un timeout largo explícito; el valor predeterminado de otros usos se conserva.

## D-073 — Tiempo total y datos inmóviles durante una creación

Una creación conserva un reloj monótono entre preparación, referencias, escritura,
validación y vueltas de investigación. El historial del trabajo completo se separa
de las muestras de etapas anteriores y solo aprende tras guardar una fusión válida.
Agrupa configuración y alcance, sin nombres de juegos, pedidos ni respuestas en disco.
Tres éxitos comparables permiten un intervalo observado; sin ellos se usa la
orientación general. El intervalo nunca amplía el plazo compartido de una hora.

FusionInputSnapshot valida caché frente a disco antes de trabajar, incluso sin
preparación, y congela perfiles y medios seleccionados para la creación. La guarda
antes del guardado vuelve a comprobar contenido, perfiles y firmware. Las marcas de
archivos solo anticipan la cancelación, no sustituyen esa comprobación de contenido.
La preparación puede guardar sus propios perfiles; conserva originales/firmware
y toma una captura nueva de los perfiles comprobados antes de escribir.

StudioValidator acepta CancellationToken opcional en ambas entradas, lo propaga al
arranque y comprueba cada tick del robot. La cancelación se propaga como tal y libera
entradas. Un resultado rechazado por datos cambiados o una cancelación conserva los
trabajos pendientes y no entrena las estimaciones del trabajo completo.

## D-074 — Trabajo interno del 8088 ubicado entre sus transferencias

El reloj optativo conserva el total nominal de cada instrucción y ubica dentro de ella
el trabajo que precede o separa sus transferencias: cálculo de dirección con lectura de
desplazamientos, dos T por prefijo y la espera entre lectura y escritura de memoria.
Los valores salen de las trazas locales de SingleStepTests/8088 v2 y se documentan en
I8088.md. La BIU no inicia una prelectura en los dos T previos a un pedido de la EU.
El inmediato de 80h–83h se toma de la cola después de leer el operando.

Los desplazamientos de memoria por CL escriben el operando aunque la cuenta sea cero.
INT, INTO, AAM 0 y los errores de división leen el vector antes de apilar el retorno;
NMI, INTR y la trampa comparten ese recorrido. Valor, flags y formatos de estado no
cambian: los contadores nuevos solo existen dentro de un paso. La variante 8086 comparte
estas reglas sin contraste externo propio.

cpu8088 verificar-bus acepta archivos de grupo `XX.R` con los metadatos del opcode base y
trazas que empiezan dentro de una prelectura; un dato sin ALE sigue siendo inválido.
El informe agrega `ValueOnlyFailures` para mismo orden y dirección con otro valor, y
compara intervalos siempre que coincidan tipo y dirección. El alcance no cambia:
intervalos relativos, sin reloj absoluto ni prelectura.

## D-075 — Preferencias de la app, teclado compartido y cuadros sin partida

La app guarda sus preferencias en preferencias.json v1, aparte de sonido.json, IA,
biblioteca y partidas: tamaño del texto (100–200 % en pasos de 25), alto contraste,
escala entera, pantalla completa, ahorro de energía, cuadros por segundo visibles,
joystick del Spectrum, puerto del C64, LOAD automático y aceleración de cintas. Un archivo
de otra versión o con valores desconocidos no se carga ni se reemplaza; los cambios
quedan en la sesión. `--autotest` no lee ni escribe el archivo; `--text-size N` y
`--high-contrast` cambian solo la sesión, como `--mute`.

Ctrl es una tecla del Spectrum (SYMBOL SHIFT) y del C64 (C=), además del PC. En esas
máquinas, fuera de una fusión, los atajos de herramientas pasan de Ctrl+letra a
Ctrl+Shift+letra (O, L, M, K) y el acorde completo no llega a la máquina. Antes Ctrl+M
escribía un punto en el Spectrum y además abría Modificar; Ctrl+L, el signo igual, abría
la biblioteca. NES, arcade, fusiones y el arranque sin máquina conservan Ctrl+letra.

La navegación con teclado de ImGui solo se habilita cuando ningún juego en curso tiene
el teclado: biblioteca, selector de archivos, avisos, sin máquina, actualización en curso
o pausa. Así Tab, las flechas, Alt, Enter y Espacio siguen siendo de la máquina mientras
se juega.

Sin partida en curso la ventana se limita a 60 cuadros por segundo, 15 sin foco y 5
minimizada; mientras se emula, sigue la frecuencia del monitor porque la emulación y
la cola de audio avanzan por cuadro del host. El renglón por segundo de `--autotest`
informa en `fps=` los cuadros realmente dibujados (antes repetía Raylib.GetFPS, que
consultado una vez por segundo sobrestimaba) y agrega `objetivo=`.

## D-076 — Salida con trabajos en curso, pausa en segundo plano y mando

El bucle principal ya no termina directamente con `WindowShouldClose`: la orden de cierre
llega a `RetroApp.RequestQuit`, que sale si no hay trabajos de IA en curso (estudio,
creador, análisis o respuestas manuales pendientes) y si no, pide confirmación. Se actúa
al comenzar la orden, de modo que el diálogo no reaparece aunque raylib la mantuviera.
`--autotest` y la actualización conservan su salida directa.

preferencias.json v1 agrega `PauseInBackground` (falso de fábrica). Un archivo v1 anterior
sin el campo se lee con ese valor, sin cambiar de versión. La pausa automática solo
reanuda lo que pausó y respeta una reanudación manual hecha sin foco.

La navegación con mando de ImGui sigue la misma condición que la del teclado (D-075).
Select + Start abre la biblioteca o vuelve al juego; Ctrl+F enfoca la búsqueda de la
biblioteca. Corrección de D-075: minimizada, raylib espera eventos sin dibujar, así que
el límite de 5 cuadros solo rige para los cuadros que esos eventos despiertan.

## D-077 — Capturas, miniaturas, mando retenido y última carpeta

Las capturas son PNG RGB de 8 bits escritos en código propio (zlib de .NET y CRC32), a
la resolución de la máquina, en Imágenes/RetroEmu (o la carpeta personal si el sistema
no tiene Imágenes). Se crean con `FileMode.CreateNew`: nunca reemplazan un archivo y un
fallo de escritura borra el parcial. La miniatura se escribe como `<juego>.png` junto al
original solo si `ThumbnailFiles.Beside` no encuentra ya una imagen. Ctrl+S (Ctrl+Shift+S
donde Ctrl es una tecla del equipo) se suma a las teclas de herramientas que no llegan a
la máquina.

Al terminar `--autotest` fuera de la biblioteca también se guarda la pantalla emulada en
`out/autotest-capturas` del directorio actual. Después de Select + Start el mando no
llega al juego hasta soltar todos sus botones. preferencias.json v1 agrega `LastFolder`
(opcional, hasta 1.024 caracteres); se usa solo si la carpeta existe.

## D-078 — Trabajo de cadenas y prefijos de REP ya decodificados

Las cadenas ubican su trabajo EU antes del pedido de datos y entre origen y destino:
3 T en MOVS y 4 T en CMPS, dentro de la tabla nominal existente. Los bytes de palabra
conservan transferencias consecutivas de 4 T en el 8088. Al repetir un paso se reutilizan
los prefijos y no se vuelve a sumar su costo de decodificación al remanente nominal.
REP vacío sigue sin acceder a datos; no cambian valores, flags, interfaces ni estados.
El 8086 comparte el recorrido funcional sin que las trazas del 8088 validen su reloj.

El contraste completo de A4–AF (sin A8/A9) mantiene 20.000 secuencias de accesos
correctas. CMPS/STOS/LODS coinciden en todos sus intervalos (12.000 casos); MOVS/SCAS
sin REP también coinciden. Se conservan explícitos 1.968 casos de REP MOVS/SCAS con
un intervalo inicial un T más largo. El trabajo de entrada a REP y la prelectura
necesitan otro hito; no se modifica el presupuesto nominal para hacer pasar esas trazas.
El diagnóstico conserva su formato, códigos de salida y alcance relativo de D-070/D-074.

## D-079 — Intervalos de LES/LDS y PUSH de memoria

LES/LDS ubican seis T de trabajo EU entre la lectura de la palabra de desplazamiento
y la del segmento; ambos valores se leen antes de sustituir el registro o segmento
de destino. PUSH de memoria ubica siete T entre la lectura del operando y la escritura
en la pila. El trabajo se descuenta del remanente existente, sin modificar la tabla
nominal, formatos de estado, valores ni flags. Las formas de registro, incluido PUSH SP
del 8088, conservan su recorrido.

C4/C5/FF.6 completos coinciden en 30.000 casos y 84.994 pares de intervalos de la suite
local v2. POP de memoria conserva sus diferencias y no se declara corregido. Tampoco
se amplía el alcance del verificador a reloj absoluto, prelectura o señales eléctricas.
El 8086 comparte los cambios y las pruebas funcionales propias; falta su contraste
externo independiente.

PRECISION-CPU.md explicita el alcance de los cuatro motores y sus variantes: un
conjunto de instrucciones o una suite aprobada no equivale a certificar todas las
combinaciones de señales, revisiones físicas y dispositivos de una máquina.

## D-080 — POP de memoria y marcos lejanos con trabajo EU distribuido

POP de memoria termina su trabajo previo antes de leer SS:SP: dos T, o tres en
las formas de desplazamiento de 16 bits con BX+DI/BP+SI. Después de leer la palabra
ejecuta tres T de EU, termina una prelectura activa y recién entonces entrega el
pedido de escritura con sus dos T. Eso produce el intervalo observado de 9/10 T
entre el último byte leído y el primero escrito, según la cola, sin condicionar
el tiempo a un tamaño de cola supuesto.

CALL lejano directo ubica nueve T de EU entre CS e IP apilados. RETF ubica cinco T
entre IP y CS; IRET agrega cuatro entre CS y FLAGS. Las transferencias de palabra
conservan su orden, valores y cruces. Se descuenta el trabajo del remanente existente:
la tabla nominal, los flags y el formato de estado no cambian.

Los archivos completos 8F/9A/CA/CB/CF coinciden en 50.000 casos y 170.000 pares de
intervalos. Los mismos casos conservaban diferencias de intervalos con el núcleo
anterior. Las pruebas propias verifican marcos en ambos modelos, NMI durante POP y
HOLD solicitado durante una prelectura. El 8086 comparte el recorrido, sin contraste
externo propio.

La entrada de REP, las llamadas indirectas, JMP lejano indirecto, INT y los errores de
división siguen pendientes. No se conservan los ensayos parciales de indirectos ni
se amplía el alcance a reloj absoluto, prelectura completa o señales eléctricas.

## D-081 — Entrada REP, transferencias indirectas y marcos de INT/división

MOVS ejecuta un T más de EU antes del primer acceso al entrar a REP. SCAS repetido
emite su pedido con tres T sin iniciar otra prelectura. El trabajo se descuenta del
remanente nominal; no se recobran prefijos ni se modifican valores o estado guardado.
Las 20.000 cadenas coinciden en accesos y 1.045.870 pares, resolviendo los 1.968
intervalos iniciales pendientes de D-078.

CALL/JMP indirectos ubican el trabajo entre las palabras del puntero y de la pila.
Se considera el espacio libre de cola al terminar de leer el destino: el bus termina
su prelectura antes del siguiente pedido. CALL lejano separa CS e IP apilados con
nueve T; JMP lejano inhibe nuevas prelecturas en los tres T previos al acceso.
FF.2/FF.3/FF.4/FF.5 completos coinciden en 40.000 casos y 132.437 pares.

INT/INTO/AAM 0 y los fallos DIV/IDIV siguen leyendo IP/CS del vector antes de apilar
FLAGS/CS/IP. Se distribuyen los 31 T de entrada software entre esos accesos. DIV
distingue el fallo temprano por magnitud del fallo después de completar el cociente;
IDIV incluye la normalización de signos y el trabajo de los bits del cociente. La
aritmética funcional, máscaras de flags indefinidos y recorrido hardware de NMI/INTR/TF
no cambian. Coinciden los 488.394 pares de los 80.000 casos, conservando 25.049
casos distintos solo en FLAGS indefinidos apilados. El verificador devuelve 2 por
esos valores; no se cambian su criterio, formato ni alcance.

La tabla nominal y el formato CPU3 se conservan; los totales de multiplicación/división
siguen siendo conservadores. Las fórmulas de estos grupos proceden de las trazas locales
8088; no acreditan reloj absoluto, prelectura completa, señales ni tiempos del 8086.
Las 45 pruebas propias incluyen restauración de REP y pila solapada en ambos modelos;
39 detectan el núcleo anterior. Las notas D-078 a D-080 se conservan como antecedentes.

## D-082 — READY lógico del bus 8088/8086

I8088Bus agrega la propiedad opcional Ready, alta por defecto. Solo en modo temporizado,
una transferencia espera después de T3 mientras READY esté bajo; cada T de espera
mantiene activo el ciclo y avanza Clock. El acceso y su observación ocurren al recibir
READY alto; T4 termina el ciclo. Una bajada en T4 no repite el acceso. La prelectura
conserva su T3 pendiente entre llamadas y estados CPU3, sin cambios de formato.

HOLD permanece sin concesión durante las esperas y hasta cerrar T4. Una palabra
alineada del 8086 sigue siendo un solo ciclo; la impar mantiene el bus entre mitades.
La placa es responsable de guardar su nivel/dispositivo externo. Los buses existentes
conservan su tiempo al usar Ready=true, y el modo funcional no consulta la señal.

Diez regresiones propias comprueban lecturas/escrituras, puertos, anchos, prelectura,
restauración y HOLD/T4; nueve detectan el núcleo sin READY. Esto agrega un contrato
lógico de esperas, sin afirmar precisión de pines, sincronizador 8284, reloj absoluto
ni forma de onda INTA. PcBus conserva el nivel alto y su formato de placa.

## D-083 — Historial acotado y reproducción de controles de originales

EmulatorHost conserva opcionalmente estados/píxeles cada medio segundo en un anillo
de hasta 64 MiB y 600 puntos. Rebobinar restaura mediante el cargador transaccional
existente, pausa y poda el futuro. Cambiar de sesión, reiniciar o restaurar desde otro
origen vacía el historial. No se cambia el formato de guardado de ninguna máquina.

La grabación temporal conserva identidad de máquina/edición/firmware, estado inicial,
transiciones de controles por cuadro y SHA-256 de cada pantalla. Su inicio normaliza
el estado a través del mismo cargador usado al reproducir, liberando teclas y escritura
pendiente. InputMapper entrega teclado y mando al SetInput del host; durante reproducción
ese canal real queda retenido. La reproducción verifica cada cuadro, pausa al diferir
y libera teclas al terminar o cancelar. Los estados cargados, depuración y cambios de
sesión cancelan la captura activa; un medio nuevo descarta también la película terminada.

Límites: cinco minutos, 100.000 transiciones totales y 1.024 pendientes por cuadro.
No se persiste en disco ni se certifican acciones externas, mutaciones de herramientas
o el JavaScript completo de fusiones. La UI lo ofrece en Juego > Rebobinado y entradas.
Pruebas con programa propio verifican pantallas y estado final completo, poda/memoria,
ediciones, límites, entradas reales retenidas y detección de divergencia.

IMachine agrega CancelQueuedKeys opcional para máquinas sin escritura programada.
Las cinco placas existentes lo implementan y KeySequencer.Clear libera sus teclas
retenidas y vacía el resto. La app lo usa al restaurar o comenzar la grabación para
no dejar una macro externa pendiente sobre el punto recuperado.

## D-084 — Contraste independiente del 8086 y lectura de su bus de palabra

cpu8086 acepta verificar y verificar-bus con el corpus propio SingleStepTests/8086,
revisión e71c68d215a6bb8c356bd4cb3842de3bef345ca9. Fue descargado por autorización
expresa, con licencia MIT y hashes de todos los archivos. v1 declara sintaxis JSON 2
y cpu=8086; los comandos rechazan metadatos de otro modelo y usan Intel8086.

El lector retiene dirección/BHE de T1 y compara únicamente los bancos activos por
A0/BHE. Expande una palabra par a dos bytes en el mismo reloj; no compara los bits
del banco inactivo. Los casos sin idx conservan su posición de archivo como índice.
El informe 8086 agrega Cpu y, para el bus, TransferUnits=bytes. El formato del informe
8088 se conserva. Una selección funcional vacía o totalmente excluida devuelve 1.

El núcleo no cambia en este hito. 552.744 casos funcionales coinciden, con 93.256
excluidos. Todo el bus coincide en tipo/orden/dirección: 4.787 casos difieren solo en
valores de flags indefinidos y 15.144 conservan diferencias de intervalos. Los 32.000
movimientos/E/S coinciden también en sus 15.025 pares. No se presenta este contraste
como certificación de reloj absoluto, prelectura final, pines ni todas las revisiones
del 8086; el corpus identifica un P80C86A-2. La placa propia sigue pendiente.

## D-085 — Intervalos de datos del bus de palabra 8086

El modelo 8086 coloca un T adicional al consumir un inmediato de palabra alineado,
y uno al entrar a INT/INTO/AAM 0/errores de división. El segundo pedido de vector
retiene tres T para impedir una prelectura nueva. POP permite prelectura en su trabajo
anterior a la lectura de pila. Los indirectos cuentan slots de palabra y corrigen la
fase observada de punteros desplazados, incluidas las variantes con prefijo e IP impar.

Estas ubicaciones se descuentan del presupuesto EU existente. No se cambia la tabla
nominal, el estado CPU3, los resultados funcionales ni la ruta de interrupción hardware.
El modelo 8088 conserva sus recorridos. La BIU sigue siendo aproximada: los ajustes de
fase de indirectos son empíricos y no equivalen a reproducir su microcódigo completo.

En el corpus 8086 completo coinciden los intervalos de 1.475.052 pares y los tipos,
direcciones y orden de 552.744 casos. Resuelve los 15.144 casos con intervalos distintos
de D-084. Permanecen los 4.787 casos con flags indefinidos distintos. No certifica reloj
absoluto, cola final, pines/INTA, interrupciones externas ni todas las revisiones del chip.
Veintisiete regresiones con programas propios cubren paridad de instrucción/operando/
pila, prefijos, rutas de dirección y errores; 26 fallan con el núcleo D-084.

## D-086 — Excepción de flags del oráculo limitada al marco esperado

El contraste funcional identifica una excepción aritmética a partir del opcode, el
vector 0 leído antes de ejecutar y los registros finales esperados del marco. Ya no
supone el destino 0000:0400 ni decide por los registros observados del núcleo.
Solo aplica la máscara de metadatos a los dos bytes de FLAGS apilado, calculando cada
dirección con el desbordamiento de offset SS. El byte físico contiguo puede quedar
fuera del marco y siempre conserva una comparación completa.

Doce casos propios comprueban ambos modelos, vector personalizado, SP=0001, flags
definidos erróneos y RAM ajena al marco. Ocho detectan el verificador anterior:
cuatro falsos rechazos de flags indefinidos y cuatro falsos aprobados de memoria.
Una división normal que termine en 0000:0400 no obtiene una excepción. Este hito
modifica únicamente el oráculo; no cambia el núcleo ni los tiempos o los guardados.

## D-087 — Historial local de pruebas deduplicado y verificable

workspace_cleanup.py administra exclusivamente emu/out y emu/dist. Por defecto solo
informa; --apply archiva el historial en un ZIP con contenidos únicos por SHA-256 e
índice de rutas. Conserva corpus de CPU, último runtime de QA e informes actuales.
Verifica todos los contenidos antes de borrar originales, vuelve a comprobar cada
original y elimina únicamente directorios vacíos. No sigue puntos de reanálisis.

La restauración selecciona un prefijo del índice, verifica SHA-256 y no sobrescribe
archivos distintos. El ZIP y su informe quedan en out/historial, excluido de próximas
pasadas. Las fuentes/Git, la instalación y los datos del usuario quedan fuera del
alcance. La evidencia histórica se recupera por ruta con la herramienta; el archivo
local no se distribuye. Se comprueban ida/vuelta, duplicados, corrupción, rutas y
protección de trabajo posterior con fixtures propios.

## D-088 — Z80 IM0 con opcode del bus y HALT latched

IM0 sustituye el primer M1 por seis T de reconocimiento y ejecuta InterruptDataBus
sin consumir PC ni apilar incondicionalmente. El recorrido normal de instrucciones
lee operandos y colas de prefijos en memoria, actualiza Q/flags y conserva EI/DI.
Un RST sigue durando13T; NOP6T, JP12T y CALL19T. IM1 ignora ese byte e IM2 conserva
su tabla. El Spectrum sigue proporcionando FFh y conserva su RST38h.

HALT conserva su representación PC de guardados/depuración y ciclos M1, pero ignora
el byte leído mientras espera: un cambio de memoria no ejecuta una instrucción.
No cambia el formato de estado. Veintitrés casos propios cubren vectores, datos,
prefijos, pila, Q, R, EI, HALT y conservación de IM1/IM2;21 detectan el núcleo previo.
Las1356 trazas FUSE conservan su resultado. No certifica señales ni todas las
particularidades de silicio de la aceptación de interrupciones.

Referencias primarias de implementación: [FUSE](https://github.com/fuse-emulator/fuse/blob/master/z80/z80.c)
y [núcleo de ciclos chips](https://github.com/floooh/chips/blob/master/chips/z80.h).

## D-089 — Reloj y transferencias del bus8080, con READY optativo

I8080Cpu.TimingEnabled conserva por defecto el ejecutor funcional anterior. El modo
temporizado incrementa Cycles y llama I8080Bus.Clock por T; memoria/E/S transfieren
en T3 y READY bajo agrega esperas entre T2 y T3. Los miembros nuevos del bus tienen
implementaciones predeterminadas, y el reconocimiento de Interrupt no lee RAM por
el opcode proporcionado. M1 distingue4/5T antes de los operandos; la tabla nominal
y resultados de los256 opcodes se conservan. El resto de trabajo interno queda al
final, sin declarar exactitud de toda la microsecuencia ni HOLD/HLDA o pines.

La placa arcade habilita el modo para que los accesos de vídeo/sonido vean el reloj
del acceso. Sus guardados conservan formato: el modo forma parte de la configuración
de placa y no quedan transferencias pendientes entre instrucciones. Veinticinco
regresiones propias (24 detectan antes), equivalencia de todos los opcodes con dos
estados de flags y tres diagnósticos externos temporizados conservan los resultados.

## D-090 — Ciclos robados del6502/6510 y arbitraje DMA del2A03

M6502.ClockStall incrementa el reloj y muestrea IRQ/NMI después del avance de los
periféricos, sin ejecutar una instrucción. C64.Read lo aplica durante la espera VIC-II;
el contador6510 incluye RDY, conservando la ausencia de espera en escrituras y el
calendario de dispositivos. El formato de CPU y el de C64 no cambian.

NES.$4014 deja una página pendiente y la siguiente lectura arbitra OAM. RMW conserva
su última escritura; GET/PUT se alinean en513/514 ciclos. El lector DMC externo de
APU espera lectura, roba3/4 ciclos y tiene prioridad en GET sobre OAM. Los ciclos
robados avanzan PPU/APU y muestras de interrupción. La lectura de parada afecta los
mandos; los dummy consecutivos no vuelven a desplazar el registro en NES NTSC.

NES4 agrega la página pendiente y sigue leyendo NES2/3; la configuración externa DMC
es de la máquina, sin modificar el estado APU. Se comprueban guardados al límite de
instrucción, no en mitad de la DMA. Catorce pruebas NES propias(12 fallan antes), dos
de núcleo y dos de RDY6510(una detecta antes) comprueban el recorrido. Los detalles
de activación/aborto DMC, conflictos de bus, bordes de solapamiento, Famicom/PAL y la
aceptación exacta de interrupciones siguen pendientes de contraste externo.

## D-091 — Interfaz adaptable y referencias de técnicas con evidencia

La interfaz usa fuente de lectura del sistema y reserva ancho fijo para listados y
depuración. Conserva bibliotecas, atajos, accesibilidad, guardados y flujos de creación.
Los paneles fijan un ancho mínimo proporcional al texto, limitado por el área disponible;
el menú se agrupa cuando no cabe. El creador diferencia máquina, idea y método, y la
vista previa vacía evita mostrar el arranque de una máquina ajena al nuevo proyecto.

CommandPalette busca acciones sin ejecutarlas ni gastar IA; muestra causas de
indisponibilidad y conserva la separación de entrada de la máquina. El acorde nuevo
Ctrl+P/Ctrl+Shift+P respeta el modificador de herramientas de cada sistema. La prueba
oculta no guarda imgui.ini y admite un tamaño explícito para revisar disposición.

RoutineFinder.Sample es una API para máquinas descartables de 16 bits: avanza de 1 a
50000 pasos (12000 por defecto), conserva observadores existentes y acota accesos,
raíces y resultados. Las agrupaciones son candidatas. Detectar instrucciones cambiantes
inhabilita el mapa estable. Las referencias del estudio conservan la captura anterior
a la muestra y citan archivos SHA-256, huella de conjunto (perfil/firmware incluidos),
direcciones y frecuencias, sin listados ni bytes de originales. Son observaciones de
hardware, no una interpretación probada de la mecánica ni nuevas variables verificadas.

RoutineCodeIdentity v1 usa prefijo UTF-8 RetroEmu-routine-v1 y registros ordenados por
dirección: uint32 dirección, uint32 longitud, ambos little-endian, y bytes de instrucción;
SHA-256. Visor y hallador comparten ese algoritmo. Es una instantánea de código, diferente
del hash del archivo y de la huella de conjunto. No cambia los formatos de juegos ni
estados. Guías de diseño, carpetas de agentes y pedidos comparten la distinción entre
presentación, evidencia, hipótesis y pruebas; los robots no certifican calidad creativa.

## D-092 — Contraste nestest y petición inicial DMC

NestestVerifier compara PC/A/X/Y/P/SP y ciclo absoluto antes de cada instrucción en el
modo C000 del diagnóstico. No ignora flags ni interpreta entradas vacías como pruebas
aprobadas. Se detiene en la primera diferencia, no ejecuta una instrucción sin estado
posterior y el CLI identifica los mismos bytes que usó. El corpus externo autorizado,
revisión95d8f62, coincide en8991 estados hasta26554T. Está excluido del repositorio;
las regresiones propias detectan cambios de flags, registros y ciclos. La prueba es de
CPU, no un contraste de DMA/PPU/APU ni de todas las variantes de silicio.

La máquina NES retrasa2/3 ciclos el pedido inicial DMC al escribir$4015, según la
paridad del reloj. Se conserva el arbitraje existente cuando el pedido madura. El
calendario se contrastó conceptualmente con Mesen2 b9fa69d y dos programas propios
reproducen el inicio prematuro anterior. La espera pertenece a la máquina, no a la
APU independiente ni al núcleo6502. NES5 agrega un int al final del estado (0..3);
sigue leyendo NES2/3/4 con espera cero. No cambian los formatos deCPU ni deAPU.
Siete casos cubren las dos paridades, restauración, compatibilidad e inválidos.

El aborto fino, la repetición de DMA cerca del contador de bits, bordes OAM y fases de
registros internos requieren contraste específico. No se extiende la evidencia de
nestest a esos comportamientos. 8088/8086 sigue pospuesto por pedido del usuario.

## D-093 — Espacios de trabajo y recuperación de investigación

La barra de navegación persiste entre Biblioteca, Jugar, Crear, Analizar, Herramientas y
Ayuda; se convierte en selector al faltar ancho. Talleres y analizador usan el área común
bajo menú/navegación y sobre estado, conservando desplazamiento y accesibilidad. La entrada
de teclado/mando pertenece a la interfaz mientras ese espacio está activo. Abrir un original
desde una investigación cierra el editor visible, sin cancelar su trabajo en segundo plano.

FusionResearchWork formato1 registra la propuesta antes de investigar y resultados completados
tras cada original/revisión. FusionResearchStore usa escritura atómica, bloqueo exclusivo,
hasta 12 trabajos / 4 MB por archivo en la carpeta privada de originales .retroemu/investigaciones.
No conserva archivos ROM, capturas, salida de proveedor ni rutas de perfil que un checkpoint
portable pueda imponer. Conserva estados privados acotados y no reemplaza trabajos para hacer
lugar. ConstructionId enlaza el borrador de StudioWork al pasar a escritura. La idea/data se
preservan al cancelar; cambios de motor/perfil invalidan la continuación, edición/firmware
cambiados impiden reusar el punto. Continuar requiere una acción explícita, con el proveedor
actual. Notas humanas se marcan sin verificar y se mandan para contrastar, sin alterar perfiles.

El límite de estancamiento depende de cambios en la evidencia del perfil, no de que una prueba
se haya ejecutado con otra firma de estado. Los resultados parciales ofrecen originales,
analizador, pruebas por tema y evidencia. La importación antigua lee objetivos de laboratorio
como propuestas sin base ni orden inferidos y sin inventar un programa.

ProfileLibrary.ForFiles resuelve funciones curadas ocultas por un alias automático únicamente
si coincide SHA-256 del programa completo conocido; preserva identidad, arranque y datos del
alias y no reemplaza funciones personalizadas. Las consultas del diseñador usan instantáneas
de esos perfiles. PreparationNeighborhoods observa hasta 6 candidatos junto a datos conocidos
en RAM de las 4 máquinas preparables, restaura estado/píxeles y no atribuye propósito por
adyacencia. El planificador recibe direcciones/codificaciones/permisos ya disponibles como
contexto, sin ampliar su contrato de salida: solo planes de entrada acotados, sin código ni
escrituras de direcciones propuestas por IA. No cambian formatos deCPU, estados ni juegos.

El buscador manual comparte el área conservadora de RAM del laboratorio: en NES limita
la búsqueda a0000–07FF, evitando espejos, E/S y ROM como candidatos de variables.

## D-094 — Identidad visual de electrónica retro

El pedido del usuario reemplaza la estética genérica de paneles redondeados por un banco
de electrónica: superficies de grafito, marfil/ámbar, controles rectos, rotulado técnico,
marca de circuito e ilustraciones originales de hardware. RetroArt usa vectores de ImGui;
no agrega texturas, fuentes redistribuidas ni dependencias. El panel de osciloscopio es
ilustración; no se presenta como telemetría. Los originales conservan el render sin filtros.

Los modos de navegación tienen numeración y una señal activa visible. Catálogo mantiene
los cinco sistemas, teclado/foco, formatos y acceso a archivos; talleres e instrumental
presentan herramientas en filas, con requisitos y atajos visibles. El selector compacto,
escalas de texto y alto contraste se conservan. La composición usa Gill Sans MT para lectura, Agency FB Bold para títulos y OCR A
Extended para rótulos, desde las fuentes instaladas. Si faltan, se usan alternativas
locales (Trebuchet, Bahnschrift o fuentes del sistema). Los archivos de fuente no se
redistribuyen. El cuerpo parte de 20 px y las barras/márgenes se adaptan al tamaño elegido;
el listado conserva una fuente monoespaciada separada. No cambian contratos, estados,
IA, investigación, formatos ni emulación de D093.

## D-095 — Muestreo de interrupciones y estados internos al restaurar

6502 conserva la muestra de IRQ del fetch de opcode para ramas tomadas sin cruce y la
combina con el punto de corrección al cruzar página. NMI usa el punto de muestreo de la
instrucción, no solo el latch de borde; una NMI tardía espera la instrucción siguiente y
no se anida dentro de la entrada IRQ/BRK. El estadoCPU añade el bool de muestra NMI;
M6502.Load explicita si ese campo está presente. NES guarda6/lee2–6 y C64 guarda2/lee1–2;
los lectores legacy conservan alineación y reconstruyen la decisión disponible antigua.

Spectrum y arcade guardan formato2, leyendo1: conservan inhibición EI y NMI pendiente
cuando corresponde, y restaurar no hereda latches de la ejecución abandonada. Los formatos
viejos no registraban esos flags y no permiten reconstruirlos fielmente: se inicializan
explícitamente, en lugar de conservar basura del estado actual. Reset limpia las muestras.

Las regresiones usan programas propios, comparan ciclos/bus/pila y restauración/legacy.
No certifican bordes de media fase, todas las variantes ni toda la máquina. Referencias:
[Visual6502](https://www.nesdev.org/wiki/Visual6502wiki/6502_Timing_of_Interrupt_Handling) y
[notas de interrupciones](https://www.nesdev.org/wiki/Interrupts).

## D-096 — Investigación autónoma privada, evidencia y presupuestos

AutonomousResearch usa GamePreparer en máquinas descartables con Save=false: arranca,
reproduce entradas y contrasta escrituras sin exigir una partida manual previa. Un trabajo
formato1 conserva objetivo/opciones, huellas de original/firmware/perfil/motor, perfil de
trabajo, continuación acotada, experimentos, comprobaciones y LabReport. El hash del
contenedor y el de la edición seleccionada son identidades distintas; GamePreparer.Choose
admite un perfil preferido y prioriza el miembro reconocido por su hash y máquina.
EditionSha256 agrega la identidad del miembro al registro formato1. Los anteriores usan
la identidad disponible en evidencia, continuación o perfil; sin identidad de miembro
no se reusan para otra edición del mismo contenedor. La huella de motor incluye módulos
de CPU, máquinas, núcleo y laboratorio; las fusiones incluyen también Studio y JavaScript.
Abrir datos de otro contexto conserva evidencia histórica sin contarla como efecto actual.

AutonomousResearchStore guarda hasta12 trabajos/4MB en .retroemu/analisis junto al original,
con reemplazo atómico, Flush y bloqueo exclusivo compartido de carpeta (.store.lock vacío).
No borra trabajos por cuota ni confía rutas externas de un checkpoint. Un inválido se
conserva y se informa; descartar exige un ID válido y una acción explícita. La UI intercambia
copias JSON por cola y generaciones de sesión, sin leer listas mutables del trabajador.
Cambiar de juego cancela el trabajo anterior; el checkpoint final puede terminar en segundo
plano y su resultado no se incorpora a otra partida. La gestión espera esos guardados.

Cada ejecución explícita agrega minutos/pedidos/rondas a los contadores acumulados. El
asesor comparte el máximo entre navegación, experimentos y nombres. Cero pedidos conserva
pruebas deterministas. Dos rondas sin evidencia nueva cortan la repetición. Pausa, error y
límite guardan lo completado; una ronda interrumpida puede repetirse. No hay porcentajes
de descubrimiento ni número garantizado de variables.

LabVerification amplía JSON de forma compatible con valor/bytes iniciales y probados,
cantidad de bytes RAM finales distintos y hasta8 diferencias de muestra. Las corridas
restauran estado, imagen y entradas en finally. Un efecto comprobado no certifica el
nombre ni la semántica propuesta. Los candidatos no se promueven a perfiles del usuario.
La misma restauración de imagen se aplica a MemoryLab y LabExperiments, también al cancelar.
VerifyBest no excluye un candidato por su frecuencia de escritura: la clasificación de
copia se contrasta antes de decidir si admite modificaciones. La prueba puede refutar
esa clasificación sin certificar el propósito del campo.
La vista Cambios previsualiza bytes mediante GameVariable.Write en memoria sombra y
conserva la aplicación/undo existente; el análisis no escribe archivos originales.

AiOperation serializa consultas por instancia y atribuye metadata a la llamada que la
produjo. Una espera cancelada no reserva cupo; una llamada fallida no hereda el uso anterior
y una respuesta vacía falla. Las operaciones distintas necesitan coordinación separada.
Los tokens desconocidos siguen desconocidos; caché informada es parte de entrada, no se
suma otra vez. La fusión usa un máximo práctico de pedidos compartidos, además del reloj.
Studio valida mecánica/errores/estados y evidencias disponibles sin exigir puntuación a
todos los diseños. La aprobación técnica sigue separada de valoración creativa o diversión.

## D-097 — Captura nativa y escalas de interfaz

La QA captura con LoadImageFromScreen/ExportImageToMemory y escritura .NET, respetando
GetRenderWidth/Height y rutas Unicode. Evita el doble escalado de TakeScreenshot en la
combinación raylib6/DPI125%. No altera el video de las máquinas ni ScreenCapture.WritePng.
Pruebas nativas explícitas verifican dimensiones, orientación y cuatro esquinas a100/125%.
Los controles del analizador se adaptan al ancho real del texto; el desplazamiento mantiene
accesibles presupuesto y evidencia con escala200% en la ventana mínima.
Las flechas y símbolos técnicos se cargan en el atlas; si faltan en la fuente elegida,
se combinan únicamente esos glifos desde una fuente de símbolos instalada. La fuente de
lectura, títulos y rótulos se conserva. No se descargan ni redistribuyen tipografías.

## D-098 — Arranque medido, campos completos y significado separado

La preparación intenta primero un arranque del perfil con pasos/tiempo/teclas acotados,
en máquina privada y con comprobación de control reproducible. Un perfil por nombre
puede proponer un arranque; sus variables y helpers no se heredan sin SHA exacta.
PlayDetector acepta cancelación y restaura estado, imagen y teclas, como el sondeo NES.
La cuenta decimal se analiza como campo de2–4dígitos, con cadencia y cruces, antes de
preferir una cifra aislada. LabVerification agrega CadenceMinFrames/MaxFrames/Changes.

GameVariable agrega PurposeStatus/PurposeEvidence opcionales. Los campos automáticos
son hypothesis aunque tengan efecto de escritura; una etiqueta de IA no limita vidas
a9 ni certifica unidad/propósito. Perfiles anteriores con descripciones Hipótesis/Nombre
propuesto conservan esa condición. observed requiere evidencia explícita; sin metadatos
el propósito es declarado por el perfil. UI y contextos de diseño/modificación/preparación
muestran esta distinción. El cambio es compatible con los perfiles anteriores.

AiOperation coordina también operaciones que comparten proveedor y las instancias del
mismo CLI por herramienta en el proceso. La espera se cancela sin reservar texto/pedidos
y conserva la atribución del uso. Llamadas directas necesitan su propio presupuesto.

## D-099 — Reinicio completo y pruebas de escenas nativas

game.restart() solicita un reinicio al salir del tick, antes de dibujar/mezclar audio.
Restablece todas las máquinas, entradas, guion, recursos, contadores y audio mezclado,
incluida la fracción de muestras. Evita recrear el motor JavaScript dentro de su llamada.
El validador captura puntuación/muertes antes de probar revancha; el reinicio no borra
los resultados del robot. Una imagen monocolor breve que coincide con el original se
contrasta por duración: no equivale a que draw esté vacío. La escena permanentemente
vacía y el tapado del original siguen fallando; una aprobación no certifica diversión.

Ayuda permite comprobar recepción de teclado/mando y muestra pasos si no llega al juego.
--games-folder selecciona una biblioteca solo para esa sesión, sin instalar ni mover ROMs.
La QA de interacción usa eventos reales de ImGui a100/200%, sin afirmar entrada física.
La barra de juego muestra el HUD del guion y sus avisos temporales; el estado genérico
del host ya no los oculta. La imagen nativa mantiene su resolución y área de dibujo.
Las muertes rápidas de patrones fijos sobre una base nativa se presentan como revisión
de dificultad, no como un fallo técnico inventado: esos controles pueden morir también
en el original. No se elimina el aviso ni se certifica equilibrio. La regla estricta de
creación libre y los errores reales de ejecución, escena/reinicio se mantienen.

## D-100 — Recorridos dirigidos con observaciones del host

StudioGame admite `playtests` opcionales, hasta tres recorridos de entradas abstractas.
Cada uno reinicia la sesión completa y declara pasos y cambios esperados de variables
existentes del perfil entre dos ticks. El host lee la memoria: los mensajes y contadores
declarados por el guion no sustituyen estas observaciones. Los planes no ejecutan código,
no escriben memoria, no aceptan direcciones ni cargan estados. Tienen límites de pasos,
duración y observaciones; se pueden cancelar y liberan entradas al terminar o fallar.

El validador los ejecuta además de los robots generales y envía discrepancias a la misma
corrección recuperable. La respuesta de diseño incluye la lista, que puede estar vacía
si faltan datos observables. Archivos anteriores conservan la lista vacía; una revisión
que no suministra planes no hereda expectativas del guion anterior. La copia y los
checkpoints conservan los planes. El informe distingue cambios medidos del propósito
declarado o hipotético del perfil; pasar esos casos no acredita todos los niveles,
finales, equilibrio ni diversión. Sin planes se informa explícitamente ese límite.
La detección de reintentos compara guion y comprobaciones ejecutables; corregir una
observación no exige reescribir código que funciona. Renombrar un caso no habilita
repetir el mismo fallo. Los candidatos sin planes conservan las huellas anteriores;
los nuevos usan el prefijo `playtest-v1:` en el historial de intentos. Las consignas de
corrección incluyen las entradas y expectativas actuales, además del guion y el informe.

## D-101 — Continuidad del audio en los estados NES

NES7 agrega 16 bytes al final del estado: suma y cantidad de ciclos del integrador de
audio, entrada anterior y salida del filtro que elimina continua. Conservar solo la APU
y la fase de muestreo hacía que cargar una partida produjera muestras distintas según
el audio reproducido después de guardarla. El reinicio también descarta muestras ya
encoladas, suma parcial y fase antes de ejecutar los ciclos de reset.

Se siguen leyendo NES2–6; mantienen CPU, RAM, mapper, periféricos y los campos que
guardaban. Como no contenían la historia del filtro, ese audio se inicia en reposo y
el integrador vacío. No se promete reproducir el transitorio que esos archivos nunca
guardaron. NES7 valida fase finita, acumuladores finitos y cantidad acotada de ciclos.
La cola de salida ya emitida no forma parte de la continuación. Este cambio no resuelve
el aborto fino DMC ni certifica un modelo físico del filtro analógico.

## D-102 — Ejes de teclado adicionales sin inventar acciones

El detector conserva los mandos conocidos y, si no responden en Spectrum, prueba los
pares Z/X, Q/P y 1/0 con el mismo contraste de RAM, imagen y duración. Son candidatos
de movimiento: no declaran disparo ni otras acciones. Los controles vacíos no se
enumeran ni se exportan al perfil. El resto del laboratorio sigue distinguiendo los
efectos observados del propósito propuesto.

Una prueba negativa de movimiento ya no afirma que el juego necesariamente sigue en
el título: puede usar otras teclas, movimientos discretos o reglas que ese contraste no
reconoce. La preparación informa que no comprobó controles en su presupuesto. El
encabezado de diagnóstico de entrada usa una etiqueta corta cuando no cabe, manteniendo
el mismo identificador al redimensionar la ventana.

## D-103 — Fases de audio tras un ciclo de encendido NES

Apu conserva Reset() y agrega Reset(bool hard). NES transmite el tipo de reinicio.
Un ciclo de encendido reconstruye los cinco canales y reinicia la paridad; no hereda
temporizadores, secuenciadores, envolventes ni divisores de otra partida. El reinicio
suave conserva el tratamiento previo de los canales; su precisión física no se amplía.
El DMC espera su primer período NTSC completo tras reset. La lectura de estados y el
formato NES7 no cambian: todos estos campos ya se almacenaban. Las pruebas comparan
audio de un programa propio tras reproducción previa contra una máquina recién creada,
y comprueban el instante del primer bit DMC. No certifican todo el circuito analógico.

## D-104 — Efectos transitorios en los recorridos dirigidos

Cada check admite measure opcional: delta conserva la resta entre extremos; minDelta
y maxDelta miden extremos respecto al inicio, incluyendo cero; changes cuenta diferencias
entre muestras consecutivas. Se muestrea una vez por tick del estudio, no por escritura
de CPU. Los informes conservan valores inicial/final, medición y primer tick del extremo.
Así se distingue un efecto breve que volvió al inicio de una variable que nunca cambió.

Archivos anteriores usan delta y conservan las huellas playtest-v1. Las mediciones nuevas
usan playtest-v2 con finales de línea normalizados. La huella incluye el modo: corregirlo
puede reparar el plan sin cambiar código; renombrar un fallo no habilita repetirlo. Guías,
esquema de respuestas y carpeta del agente comparten las reglas. Variables ilegibles
producen un resultado fallido con campo/tick y pasos para revisar el perfil, conservando
la liberación de entradas. No certifica causalidad, eventos entre muestras ni diversión.

## D-105 — Respuestas completas y pruebas del creador recuperables

Creador y estudio validan la ficha textual antes de aceptar código adjunto. Se rechazan
JSON incompleto, raíz incorrecta, tipos/longitudes inválidos, campos textuales repetidos y versiones
de código contradictorias. Se conserva la lectura antigua de código sin ficha; no se usa
para acreditar metadatos que no suministra. ResponseMetadata comparte las reglas del
objeto; el programa y su lenguaje siguen teniendo lectores propios.

GameWorkspace.Read conserva su firma y agrega diagnóstico opcional. Tiene en cuenta la
fecha del programa y de la ficha; una ficha a medio escribir espera, sin aprobar valores
anteriores. La interfaz permite volver a probar y muestra el problema. El creador limita
MaxAttempts a 1–5 y solo crea capturas temporales dentro del pedido que las necesita,
si la herramienta declara soporte visual. No quedan imágenes entre reintentos.

HomebrewTrial.Run mantiene la entrada anterior y agrega token/progreso optativos. Carga,
arranque, controles y robot comprueban cancelación; el avance se expresa en etapas y
cuadros, sin porcentajes de calidad. Una cancelación no se captura como fallo de carga y
libera entradas. GameWriter conserva el borrador completo antes de ejecutar esa prueba.
Los modos automático, manual y carpeta del creador pueden cancelar la prueba desde la UI.
Esto no añade guardado de la respuesta manual al cerrar ese modo ni un modelo universal
de objetivos y finales. El límite de cinco intentos es por tanda, además del presupuesto.

## D-106 — Contratos coherentes para revisión y asistencia

La salida manual del estudio muestra JSON válido con playtests y separa el guion en un
solo bloque. Los perfiles describen variables y su evidencia; no se denominan todas
verificadas. Las instrucciones de investigación priorizan el experimento automático
también para requisitos que no coinciden con categorías conocidas. Si hace falta una
persona, se exige impedimento, lugar, acción, resultado y evidencia que conservar.

Las descripciones del juego son datos y una captura ausente no se trata como observada.
Los contratos mantienen libertad de género, base e identidad del original y las reglas
correctas del pedido. Los prompts locales de continuidad remiten al estado vivo en vez
de congelar versiones, conteos o prioridades antiguas; los adjuntos originales quedan
intactos. Estas revisiones se contrastan con proveedores simulados y flujos locales,
sin atribuirles una mejora de creatividad de proveedores reales todavía no medida.

## D-107 — Envío y espera del CLI distinguibles; prueba de conexión cancelable

La actividad del CLI distingue el envío del texto de la espera posterior. Se vacía la
entrada con el mismo token de cancelación antes de cerrarla, para no dejar una escritura
sin plazo dentro del cierre sincrónico. Al vencer el tiempo, el diagnóstico informa la
etapa y muestra segundos para límites menores a un minuto. Completar la escritura al
proceso local no acredita recepción del servidor, respuesta del modelo ni consumo cero.
Las advertencias del proceso se conservan como salida observada, sin atribuirles una causa
que no se haya reproducido. No cambia el formato de AiRequest ni la configuración elegida.

Probar conexión conserva un único trabajo, permite cancelarlo y observa su terminación
sin tratar una cancelación como fallo ni iniciar otra consulta. Cambiar de proveedor o
cerrar la app también solicita la cancelación; al cerrar comparte el plazo existente de
dos segundos con los otros trabajos. El estado de cancelación permanece visible mientras
termina. Una prueba nueva sigue siendo una acción explícita que puede consumir cupo.

El contraste local cubre un texto UTF-8 mayor que el búfer de la tubería a través de un
shim Windows, envío bloqueado, espera sin respuesta y ciclo de éxito/error/cancelación.
La evaluación externa acotada posterior a D106 agotó su plazo sin respuestas ni métricas
confirmadas; no se presenta como validación de calidad ni se repite automáticamente.

## D-108 — Requisitos con evidencia y continuidad de los originales preparados

La revisión de una idea no puede resolver un requisito citando una variable marcada como
hipótesis, un estado de propósito desconocido o una observación sin evidencia. Las fichas
anteriores sin estado explícito conservan su alcance declarado; una escritura con efecto
no pasa por eso a propósito observado. Lectura y permiso de escritura siguen siendo
independientes. La revisión manual comparte estos controles con la automática.

La investigación reconoce una observación de propósito o un arranque recién comprobado
aun si dirección y codificación no cambian. Cambiar la secuencia de arranque cuenta;
reformular el texto de una verificación existente no cuenta. Su huella ordena los originales y diccionarios; reordenarlos o reformular una
hipótesis no genera avance. Volver a una huella ya vista tampoco reinicia la cuenta de
tres rondas sin datos nuevos. Esto no conserva un historial ilimitado de huellas entre
trabajos ni demuestra causalidad por sí solo.

Al preparar se conservan el perfil resultante y la ruta del original por ranura, aunque
un nuevo escaneo prefiera un alias anterior. Al abrir una fusión guardada, un alias
seleccionado puede resolverse con un perfil disponible para la misma máquina y SHA-256;
no se enlaza una edición diferente solo por nombre. Así la selección, las referencias,
la validación, el guardado y el siguiente arranque usan la edición preparada.

El arranque recibe cancelación, ofrece Cancelar y tiene un plazo propio de dos minutos.
Un resultado tardío tras cancelar y una sesión con error de inicialización no reemplazan
el juego en uso. Se conserva el juego guardado y se muestra la causa o la etapa agotada.

La integración Codex entrega el esquema por `--output-schema`, además de describirlo en
la consigna; lo admiten la documentación oficial y la CLI local comprobada. El archivo
temporal pertenece a ese envío y se elimina en éxito, error o cancelación. Una solicitud
precancelada no busca ni inicia el CLI. El esquema no acredita comportamiento ni corrige
una conexión sin respuesta. [Referencia oficial](https://learn.chatgpt.com/docs/non-interactive-mode).

## D-109 — Guardado completo y recuperación resistente a fichas dañadas

El trabajo identifica la creación que se recupera; el guion por sí solo no identifica
su contenido completo. Un reintento idéntico reutiliza la versión y su ruta real. Si
cambian ficha, pruebas, originales, base o programa, se conserva la identidad del juego
y se guarda una versión nueva. Fecha y contabilidad no generan otra versión. Un WorkId
vacío no agrupa creaciones independientes. Los identificadores de entrada y de fichas
anteriores no pueden redirigir el guardado fuera de la biblioteca.

La fuente se escribe en un temporal propio, se vacía a disco y se publica sin reemplazar
archivos existentes. Después se publica la ficha completa, que habilita su aparición en
la biblioteca. Los nombres ocupados por fuentes o fichas anteriores se conservan, aun si
no representan una versión completa. Un fallo antes de publicar la ficha puede dejar una
fuente sin ficha; no se elimina por asumir que está abandonada. El trabajo pendiente sigue
permitiendo recuperar. El bloqueo interno serializa guardados del mismo proceso; otro
proceso puede provocar un conflicto de escritura, sin autorizar sobrescribir su archivo.

El listado conserva la ruta donde encontró cada versión, incluso si está en otra biblioteca
compatible. No devuelve una ubicación inventada al recuperar. La exportación de ejemplos
tampoco pisa un guion previo solo porque no encuentre su ficha.

La recuperación cuenta hasta treinta trabajos válidos después de descartar registros
ilegibles o dañados. Una ficha con estructura inválida no impide listar las demás. Se
preservan esos archivos para diagnóstico y recuperación manual; no se reparan ni borran
sin evidencia de cuál era su contenido.
