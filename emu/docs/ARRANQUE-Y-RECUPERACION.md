# Arranque, audio y recuperación

Estos cambios están preparados después de 0.3.7. No modifican los paquetes ya publicados.

El paquete de Windows x64 incluye el runtime de .NET y las bibliotecas gráficas y de audio.
Se extrae completo y se abre RetroEmu.exe. No hace falta instalar el SDK, Python, una CLI
de IA ni conectar una cuenta para jugar. Los juegos se aportan por separado.
La falta o daño del firmware de la máquina predeterminada no bloquea la biblioteca ni
un cartucho de otro sistema. Si encendés explícitamente una máquina que lo necesita,
su falta se informa en ese momento.
El sistema operativo y el controlador de video deben permitir OpenGL 3.3.
Hay paquetes x64 para Windows y Linux; no se promete compatibilidad con cualquier sistema.
Cada edición del sistema debe estar admitida por [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/install/windows).
En Linux el runtime incluido necesita además las bibliotecas nativas y el escritorio de
la [distribución](https://learn.microsoft.com/en-us/dotnet/core/install/linux).
--no-ai abre una sesión manual sin cargar ni guardar la configuración personal de IA
y bloquea la construcción de proveedores automáticos. Las pruebas --autotest también
usan ese bloqueo. Para preparar desde la GUI sin IA se puede combinar --no-ai --preparar.

Sin salida de audio, el programa puede abrir y jugar en silencio a su velocidad normal.
Sonido y el diagnóstico muestran el problema. Buscar la salida de audio permite reintentar
después de conectar parlantes o auriculares. Un aviso de audio por sí solo no demuestra
por qué una computadora no llegó a mostrar la ventana.

--no-audio evita por completo inicializar el dispositivo y crear el canal de salida.
A diferencia de --mute, sirve para probar un inicio sin pasar por el controlador de audio.
Es una elección de esa sesión; **Buscar la salida de audio** lo habilita explícitamente.

La orden requisitos (requirements en inglés) y **Ayuda > Requisitos para abrir y jugar**
separan archivos presentes, ventana inicializada y componentes opcionales. Para guardar
el informe aunque falten las DLL gráficas, sin abrir ventana, audio, ajustes ni IA:

~~~text
RetroEmu.exe --check-startup comprobacion.json --language es
~~~

El informe describe el runtime que realmente está cargado. La presencia de una DLL no
certifica que pueda cargarse, y este modo sin ventana no prueba el controlador OpenGL.
Necesita que Windows ya haya podido ejecutar el programa y su runtime administrado.
Con archivos requeridos ausentes devuelve código 2 y conserva el informe generado.

**Ayuda > Requisitos > Revisar requisitos y reparaciones** y la acción del buscador
abren el informe con acceso a la guía oficial del sistema. No se necesitan permisos de
administrador para jugar normalmente. Un archivo faltante del propio paquete requiere
reparar o actualizar ese paquete; instalar el SDK no lo reemplaza.

En Windows, si las tablas de importación de raylib/cimgui requieren una DLL de Visual C++
que no está incluida ni disponible en System32, se ofrece repararla. La misma detección
funciona en la recuperación de arranque, antes de abrir OpenGL. Primero se pide permiso
para descargar el [redistribuible oficial de Microsoft](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist).
Se admiten sólo HTTPS y servidores Microsoft del catálogo fijo. Antes de pedir un segundo
permiso para ejecutarlo se verifica Authenticode y que el firmante sea Microsoft Corporation.
Windows puede mostrar UAC y el instalador oficial muestra su licencia. Se prohíbe el
reinicio automático; si hace falta reiniciar se informa. Rechazar cualquiera de las dos
decisiones no instala nada. La prueba de firma usa la caché de certificados de Windows;
si no puede verificarla, rechaza el archivo y muestra la guía oficial.
Salir durante una reparación pide confirmación y cancela la descarga pendiente. Si el
instalador elevado ya empezó, sigue bajo el control de Windows; no se mata ese proceso.

No se ofrecen instaladores por un aviso genérico de audio. Los controladores se obtienen
del sistema operativo o del fabricante. En Linux se ofrece la guía oficial para los
requisitos nativos de la distribución; no se ejecuta un instalador de Windows ni se
modifican repositorios de paquetes.

### Comprobación de Linux antes del runtime

Las bibliotecas raylib/cimgui incluidas requieren **glibc 2.38 o posterior**, libstdc++
y X11 (XWayland al usar Wayland), además del controlador OpenGL 3.3. Ese requisito es más
estricto que el mínimo general de .NET. No sirve instalar otro SDK ni reemplazar glibc
aisladamente en una distribución antigua. ICU/OpenSSL y certificados HTTPS corresponden
a los paquetes oficiales de la distribución cuando se necesitan esas funciones.

El paquete Linux incluye `RetroEmu.sh`. `sh RetroEmu.sh --check --language en` comprueba
arquitectura, glibc, archivos del paquete, permisos, dependencias enlazadas y sesión X11
sin abrir .NET, gráficos, sonido ni archivos de preferencias. Devuelve 2 si falta algo.
Sin `--check` inicia el ejecutable de esa carpeta y conserva sus argumentos.
El tar.gz guarda el lanzador y el ejecutable con permiso de ejecución y el script con LF.
La comprobación no instala ni descarga, ni acredita los componentes cargados dinámicamente
(ICU/OpenSSL), el controlador o una partida. Se comprobó su lógica con sustitutos POSIX;
el arranque en un escritorio Linux real sigue pendiente.

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
