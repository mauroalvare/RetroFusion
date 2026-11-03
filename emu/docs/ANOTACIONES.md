# Anotaciones por edición

Comentarios y etiquetas manuales se comparten entre la terminal y **Herramientas > Ver
código y rutinas**. Anotar no ejecuta instrucciones, no cambia bytes del juego y no
llama a la IA. El texto del usuario conserva su idioma al cambiar el idioma de la app.

| Español | English |
| --- | --- |
| `anotar C000 Entrada del programa` | `note C000 Program entry` |
| `etiquetar C000 entrada principal` | `label C000 main entry` |
| `anotaciones C000` | `notes C000` |
| `anotaciones` | `notes` |
| `importar-anotaciones confirmar` | `import-notes confirm` |
| `exportar-anotaciones "C:\Copias\Notas.json"` | `export-notes "C:\Copies\Notes.json"` |

Las direcciones son hexadecimales y deben existir en la máquina actual. Un comentario
admite hasta 4096 caracteres y una etiqueta hasta 128; sus espacios se convierten en
guiones bajos como en el editor. La consulta sin dirección lista hasta 100 direcciones;
con dirección muestra el texto completo, incluidas explicaciones y pseudocódigo ya
guardados. No inicia explicaciones nuevas. Estas órdenes requieren un original cargado.

## Archivos anteriores y ediciones

Los originales guardan sus notas bajo `%APPDATA%/RetroEmu/anotaciones/edition-<clave>.json`
o el ApplicationData equivalente del sistema. La clave incluye sistema, contenido de los
medios y firmware. Mover o renombrar el mismo contenido conserva sus notas; otra edición
con el mismo nombre queda separada. El JSON exige identidad coincidente; no se adjunta
automáticamente un archivo sin identidad a una edición.

Las notas anteriores se guardaban por nombre o perfil. Se conservan en su archivo y se
ofrecen para importar cuando existen. El usuario debe confirmar que corresponden a esta
edición. La importación valida todas las direcciones y agrega únicamente claves faltantes;
las notas actuales prevalecen. También conserva registros de parches como datos: no aplica
sus bytes. Un origen malformado no se importa. No vuelve a ofrecer el mismo origen ya
importado; si ese archivo cambia, permite revisarlo otra vez.

Las anotaciones de fusiones conservan su alcance anterior por perfil en su editor.
Las órdenes manuales no escriben en una fusión ni en una máquina sin original.

## Errores, copias y recuperación

Un guardado sólo anuncia éxito tras escribir y sincronizar el archivo. Ante una falla,
la edición sigue en memoria y se informa el problema. Cambiar de juego, reiniciar,
restaurar estados o iniciar una actualización se detiene si no se pudo conservar esa
edición. Al salir se puede volver al editor o descartar de forma explícita.
El aviso pausa la partida; volver restaura la pausa anterior. Elegir descartar evita
un guardado tardío durante Shutdown, aunque el almacenamiento vuelva a estar disponible.

Se puede reintentar o exportar una copia JSON a una ubicación disponible. La exportación
crea un archivo nuevo, nunca sobrescribe otro y conserva las notas, explicaciones y
registros de parches. Si el archivo continúa intacto y coincide con la memoria actual,
permite abandonar esa sesión aunque falle el archivo habitual. Si cambia, desaparece o
se editan notas nuevas, deja de contar como copia actual. Exportar no sustituye el archivo
habitual ni aplica parches; no hay una orden para importar una ruta JSON arbitraria.

El editor muestra **Exportar notas** con una ruta editable cuando no se confirmó el guardado
en el archivo habitual. **Recuperar anotaciones**, ante un archivo dañado o conflicto con otra instancia,
conserva primero el archivo de disco bajo `.respaldo-<id>` y después guarda la sesión.
Si no puede conservarlo, no lo reemplaza. Los respaldos permiten inspección y restitución
manual con la app cerrada; no se prometen conversiones desde cualquier JSON ajeno.

El esquema actual es JSON formato 1 con `GameIdentity`, `LegacySourceHash` y cinco mapas:
`Labels`, `Comments`, `Summaries`, `Pseudocode`, `Patches`. Los anteriores sin metadatos
se leen sólo en su alcance anterior para importarlos explícitamente. Una versión futura,
identidad distinta o archivo de más de 4 MiB bloquea la escritura automática. Un `.lock`
estable coordina instancias; cada guardado compara los bytes leídos, usa un temporal y
reemplazo atómico. No escribe encima de enlaces de archivo. Los errores controlados
retiran sus temporales propios. Las pruebas y --autotest usan carpetas aisladas.

In English, use **note**, **label**, **notes**, **import-notes confirm** and **export-notes**.
Manual notes never call AI or patch memory. Legacy import preserves the source and current
entries. JSON exports never overwrite an existing file and do not replace the normal store.
Unsaved notes block session changes unless an intact current export exists; quitting can be
cancelled. Arbitrary JSON copy import is not available through commands yet.
