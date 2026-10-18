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

## D-008 Coordinación entre agentes con un archivo JSON por elemento (2026-10-18)

Tareas, mensajes, reclamos y latidos son archivos separados en `coordinacion/` del
checkout principal: dos agentes nunca escriben el mismo archivo, se lee sin herramientas
y funciona entre proveedores. Reclamos con vencimiento (8 h) para recuperar tareas
abandonadas; latidos de 45 minutos para no duplicar trabajo; `PAUSA` manual que prevalece
sobre toda automatización. Reclamos, latidos y `PAUSA` no se versionan (son de la
máquina local); tareas y buzón sí (son el registro).

## D-009 Continuidad con tareas programadas de la app y política de uso (2026-10-18)

La app de Claude ofrece tareas programadas que abren sesiones nuevas (persisten entre
sesiones; corren con la app abierta) y un cron por sesión (solo mientras la sesión vive y
está inactiva). Se usa la tarea programada como supervisor: cada pocas horas comprueba
pausa, límite de uso y latidos, y si nadie trabaja retoma el backlog. El trabajo automático
se detiene al 85 % del cupo semanal o 95 % de la ventana de 5 horas, porque el plan Pro
también es para el uso personal del usuario. Son valores por defecto que ya se aplican;
el usuario los puede cambiar en `politica_autonoma` de `proyecto.json` (está anotado en
HANDOFF.md para que lo confirme). Un cron de la propia sesión interactiva (cada 20
minutos, solo cuando está inactiva) mantiene su latido para que la tarea programada no
arranque trabajo duplicado mientras esa sesión vive.

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

## D-012 Las máquinas informan las escrituras de la CPU (`IWriteWatch`) (2026-10-18)

Para saber qué rutina escribe una variable (y más adelante watchpoints y mapas de calor)
las máquinas implementan `IWriteWatch`: un `Action<dirección, valor, pc>` opcional que se
llama en cada escritura efectiva de la CPU, con la dirección de la instrucción que la hizo.
Es nulo por defecto, así que no cuesta nada si nadie escucha. Es un contrato nuevo de
`Retro.Core`: toda máquina nueva debería implementarlo.

