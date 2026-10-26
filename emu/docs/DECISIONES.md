# Registro de decisiones

Decisiones de arquitectura y de forma de trabajo, con su motivo. Una entrada nueva por
decisión (no se reescriben las viejas: si cambia algo, se agrega otra que la reemplaza).

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
