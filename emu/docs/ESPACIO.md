# Espacio de trabajo y salidas de pruebas

Las compilaciones, paquetes extraídos y copias usadas para probar actualizaciones pueden
acumular varias decenas de GB. El código fuente y el programa instalado son cosas
distintas. La limpieza de desarrollo no modifica la instalación ni sus bibliotecas.

Desde `emu`, primero consultá el alcance:

```powershell
python tools/workspace_cleanup.py archive
```

Para consolidar las salidas generadas:

```powershell
python tools/workspace_cleanup.py archive --apply
```

Solo administra `emu/out` y `emu/dist`. Conserva las suites de CPU descargadas, el
último runtime de QA y los informes actuales. Reúne el resto en `out/historial`:
cada contenido se guarda una sola vez, identificado por SHA-256, y el índice conserva
sus rutas, tamaños y fechas. Las fuentes, Git, juegos, firmware y ajustes situados
fuera de esas dos carpetas quedan fuera del alcance.

Antes de quitar el primer original se descomprimen y verifican todos los contenidos
del archivo. Después se verifica cada original antes de borrarlo. Un archivo que
cambió se conserva; los directorios solo se eliminan si quedaron vacíos. El informe
JSON junto al ZIP registra archivos, bytes recuperados, su SHA-256 y las excepciones.
Una interrupción previa a la verificación deja los originales y un archivo `.partial`.
Los originales de solo lectura pueden quitarse una vez comprobados; el índice conserva
su modo para restaurarlo. Si hubo archivos ocupados o una interrupción después de
guardar el archivo, `prune` reanuda solo la eliminación de copias idénticas:

```powershell
python tools/workspace_cleanup.py prune --archive out/historial/ARCHIVO.zip --apply
```

El ZIP contiene `index.json` y `blobs/`; para recuperar una ruta original usá la
herramienta, que también verifica el contenido y evita sobrescribir trabajo distinto:

```powershell
python tools/workspace_cleanup.py verify --archive out/historial/ARCHIVO.zip
python tools/workspace_cleanup.py restore --archive out/historial/ARCHIVO.zip --prefix out/MI-PRUEBA
```

Los informes históricos pueden referir rutas consolidadas: restaurá solo el prefijo
necesario para consultarlas. Un auditor de un candidato antiguo sigue necesitando sus
binarios correspondientes; restaurar evidencia no convierte un núcleo nuevo en aquel
candidato. Los archivos de historial son locales y no se publican con el emulador.

Antes de publicar se regeneran únicamente los paquetes de la versión pedida. Las
carpetas extraídas de versiones anteriores sirven para QA temporal; no necesitan
permanecer multiplicadas en `dist` una vez guardada su evidencia.
