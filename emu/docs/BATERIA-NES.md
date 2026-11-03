# Batería de cartuchos NES

La batería conserva lo que el propio juego escribe en la PRG RAM del cartucho. No es
un estado completo: no guarda el instante de CPU, video ni los datos que el juego no
almacenó. Se carga antes de arrancar el cartucho y se conserva al reiniciar, incluso
con **Reinicio total**. Cargar un estado o rebobinar selecciona también la RAM de ese
momento; el siguiente guardado de batería conserva ese progreso elegido.

La app guarda los cambios cada dos segundos, al cambiar de juego y al salir. Antes de
iniciar una actualización también exige conservarlos. Un cierre forzado puede perder
lo ocurrido desde el último guardado; esto no sustituye copias de seguridad.

## Consultar, exportar y recuperar

En **Juego > Batería del cartucho** se puede guardar ahora, elegir la ruta de una copia,
exportarla o recuperarla. Abrir el panel pausa la partida. La recuperación requiere
marcar la confirmación; cambiar la ruta la desmarca. Los controles se desplazan con la
rueda y **Cerrar batería** queda al pie, también con texto al 200%.

| Español | English |
| --- | --- |
| `bateria` | `battery` |
| `bateria guardar` | `battery save` |
| `bateria abrir` | `battery open` |
| `bateria exportar "C:\Copias\Faro.rfbattery"` | `battery export "C:\Copies\Lighthouse.rfbattery"` |
| `bateria recuperar "C:\Copias\Faro.rfbattery" reemplazar` | `battery restore "C:\Copies\Lighthouse.rfbattery" replace` |

La exportación crea un archivo nuevo y nunca sobrescribe otro. Recuperar valida la
edición y el contenido antes de cambiar nada; conserva una copia de la RAM actual en
`battery/recovery/` cuando difiere y el archivo de disco anterior con sufijo `.preserved`.
La partida queda en pausa. Esos respaldos también se pueden recuperar como copias.

Ante un error de escritura o un conflicto con otra instancia, la app mantiene la RAM
actual y el archivo anterior. No permite cambiar de juego sin guardar o exportar una
copia que siga coincidiendo con la RAM. Al salir avisa y permite volver al panel o
salir descartando los cambios. Si exportás y después seguís jugando, la copia anterior
ya no basta para conservar los nuevos cambios.

## Archivos y límites

- Carpeta normal: `%APPDATA%/RetroEmu/battery` en Windows, o la carpeta ApplicationData
  equivalente del sistema. El nombre es el SHA-256 completo del cartucho más `.rfbattery`;
  renombrar o mover el mismo contenido conserva la batería, otra edición queda separada.
- Archivo versión 1 de 8274 bytes: firma ASCII `RF-NES-BATTERY` (14), versión int32 LE
  (4), identidad SHA-256 (32), SHA-256 de la RAM (32) y PRG RAM (8192).
- Se escribe y sincroniza un temporal en la misma carpeta antes del reemplazo atómico.
  `.previous` conserva el archivo previo al último guardado normal. El archivo `.lock`
  vacío se mantiene para coordinar instancias; el contenido leído se compara antes de
  escribir. Una recuperación explícita preserva el archivo encontrado bajo el mismo
  bloqueo. No se sobrescriben automáticamente archivos inválidos ni versiones futuras.
- Soporta iNES con batería y un banco PRG RAM de 8 KB, y NES 2.0 con exactamente 8 KB
  PRG NVRAM, sin PRG RAM volátil ni CHR NVRAM. Otros diseños requieren estados guardados
  y muestran un aviso; no se inventa una conversión de su memoria.
- No importa .sav ajenos. Archivos mayores de 64 KiB, enlaces y ubicaciones inaccesibles
  no se reemplazan desde la recuperación. Se puede exportar a otra ubicación disponible.
- Fusiones, ensayos, investigaciones y CLI no cargan ni guardan esta batería compartida.
  Las pruebas automáticas usan datos propios y una carpeta aislada, sin AppData del usuario.

The English interface exposes **Game > Cartridge battery** and the commands above.
Exports never overwrite an existing file. Recovery requires **replace**, preserves
previous data and pauses the session. Only the supported 8 KB PRG battery layout is
saved automatically. Fusions and trials do not share the player's save. A forced exit
may lose up to two seconds; storage failures require saving or exporting before leaving.
