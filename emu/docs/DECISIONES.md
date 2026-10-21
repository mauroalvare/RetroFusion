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
