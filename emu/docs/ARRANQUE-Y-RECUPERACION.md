# Arranque, audio y recuperación

Estos cambios están preparados después de 0.3.7. No modifican los paquetes ya publicados.

El paquete de Windows x64 incluye el runtime de .NET y las bibliotecas gráficas y de audio.
Se extrae completo y se abre RetroEmu.exe. No hace falta instalar el SDK, Python, una CLI
de IA ni conectar una cuenta para jugar. Los juegos se aportan por separado.
El sistema operativo y el controlador de video deben permitir OpenGL 3.3.
--no-ai abre una sesión manual sin cargar ni guardar la configuración personal de IA
y bloquea la construcción de proveedores automáticos. Las pruebas --autotest también
usan ese bloqueo. Para preparar desde la GUI sin IA se puede combinar --no-ai --preparar.

Sin salida de audio, el programa puede abrir y jugar en silencio a su velocidad normal.
Sonido y el diagnóstico muestran el problema. Buscar la salida de audio permite reintentar
después de conectar parlantes o auriculares. Un aviso de audio por sí solo no demuestra
por qué una computadora no llegó a mostrar la ventana.

## Si la ventana no llega a abrir

El ejecutable supervisa la apertura desde un proceso que no usa OpenGL, raylib ni el
dispositivo de sonido. Si la apertura falla o no llega al primer cuadro en un minuto,
ofrece buscar una versión nueva mediante un diálogo de Windows.

Primero se confirma la consulta. Si hay una versión posterior para esta plataforma,
se muestran su número y una segunda confirmación para descargarla e instalarla.
Se verifican tamaño, SHA-256 y manifiesto antes de reemplazar el programa en su misma
carpeta. El actualizador conserva juegos, trabajos, partidas y ajustes.

También se puede abrir directamente la recuperación:

~~~text
RetroEmu.exe --repair
RetroEmu.exe --repair --language en
~~~

No requiere que abra la interfaz principal. --no-update desactiva la búsqueda.
Los modos de prueba y los arranques internos del actualizador no muestran estos diálogos
ni hacen consultas de actualización.

El detalle del fallo queda en %LOCALAPPDATA%/RetroEmu/fallo.txt, con el perfil y el nombre
del usuario anonimizados. El proceso gráfico entrega ese diagnóstico por un archivo
temporal: no depende de tener una consola visible.

La recuperación no puede ejecutarse si Windows no consigue cargar el propio ejecutable,
el runtime administrado o las DLL administradas imprescindibles. Tampoco reemplaza un
controlador que no ofrece OpenGL. Requiere un manifiesto válido, una carpeta escribible,
conexión para consultar/descargar y una versión posterior disponible. En esas condiciones
extremas hace falta reparar el paquete; no se promete actualizar cualquier instalación dañada.
El diálogo de emergencia está implementado para Windows; Linux informa el error por stderr.

## Cortes durante la instalación

El reemplazo de archivos usa un diario y un intercambio atómico: el archivo que permite
arrancar permanece presente. Al volver a abrir después de una interrupción, se recupera
el conjunto anterior completo. El paquete nuevo debe confirmar su primer cuadro antes
de conservar la instalación. Los temporales de una instalación confirmada se limpian;
los trabajos con recuperación pendiente se conservan.

No borres manualmente un trabajo pendiente del actualizador. Si una recuperación encuentra
archivos con cambios ajenos a lo registrado, se detiene para preservarlos.

## Evidencia y alcance

En Windows x64 se probó una copia aislada, oculta y silenciada, con PATH reducido,
DOTNET_ROOT inexistente y cartucho propio. Los módulos cargados de .NET, raylib y cimgui
provinieron del paquete. También se cortaron procesos reales al reemplazar las DLL del
actualizador y de la app y antes de la confirmación: las copias se recuperaron y los
datos inventados conservaron sus hashes.

Eso no acredita todas las versiones de Windows, todos los controladores ni la PC de la
foto. Siguen pendientes Windows 10, Linux de escritorio, falta física de audio y un equipo
que realmente no ofrezca OpenGL 3.3.
