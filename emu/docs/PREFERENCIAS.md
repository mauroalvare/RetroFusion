# Preferencias, accesibilidad y teclado

Abrí **Opciones > Preferencias**. Los cambios se aplican en el momento y se conservan
al cerrar y actualizar el programa.

## Idioma y versión del archivo

**Idioma / Language** elige castellano o inglés para menús, mensajes y comandos.
Se conserva al reiniciar y actualizar; --language en permite elegirlo al abrir.
No traduce los títulos, archivos ni contenido de tus juegos.

Si el archivo declara una versión posterior, se leen los campos conocidos y válidos,
pero no se lo sobrescribe. **Recuperar archivo** conserva una copia completa antes de
guardar los valores de esta sesión en el formato actual. Un archivo inválido también
se conserva y permite usar valores temporales.

## Lectura

- **Tamaño del texto:** 100, 125, 150, 175 o 200 %. Agranda letras, botones y márgenes
  en todas las ventanas: biblioteca, depurador, estudio, creador, código y diálogos.
  Con un tamaño grande en una ventana chica, la biblioteca pasa a su vista compacta, las
  filas de botones continúan abajo y los paneles se desplazan. Si moviste los paneles del
  depurador, **Herramientas > Depuración > Reorganizar paneles** los vuelve a ubicar
  dentro de la ventana actual.
- **Alto contraste:** texto blanco sobre negro, bordes visibles en todos los controles
  y el foco del teclado marcado en amarillo.

Los dos también están en **Opciones > Accesibilidad**. No cambian los colores de la
pantalla emulada: el juego se ve como en la máquina original.

## Colores por interfaz

**Interfaz > Paleta de esta interfaz** elige fósforo verde, ámbar, hielo, papel claro,
automática o personalizada. **Personalizar colores** permite editar ocho RGB, con
Aplicar, Cancelar y restauración de la vista previa. Cada diseño guarda sus propios colores;
no cambian la distribución ni la partida. Alto contraste conserva esa elección. Volver a
otro diseño recupera su paleta. Las preferencias anteriores migran la paleta global sin perder RGB.

## Pantalla

- **Pantalla completa** (F11) se recuerda para el próximo inicio.
- **Escala entera** agranda la pantalla del juego en múltiplos exactos para conservar
  píxeles nítidos. Sin ella, la imagen ocupa todo el espacio disponible.
- **Mostrar cuadros por segundo** agrega a la barra de estado cuántas veces por segundo
  se dibuja la ventana.

## Controles y cintas

- **Joystick del Spectrum:** Kempston, Cursor o Sinclair 1, manejado con las flechas,
  Alt o un mando.
- **Joystick del C64:** puerto 2 (el de la mayoría de los juegos) o puerto 1.
- **Escribir LOAD "" al abrir cintas** y **Acelerar durante la carga** se aplican a las
  cintas del Spectrum.
- **Atajo para guardar capturas:** de fábrica es **F1**. **Cambiar…** espera la
  combinación nueva (Esc cancela); **Volver al valor inicial** recupera F1. Se aceptan F1
  (sola o con Ctrl o Shift), **Pausa** y Ctrl con una letra o un número, con Shift
  opcional. Se rechazan, con el motivo, F2 a F12 (estado, reinicio, cinta, turbo, pausa,
  paso, pantalla completa y depurador), Alt, Impr Pant, las teclas que llegan al juego sin
  Ctrl y los atajos de herramientas o de edición (Ctrl+O, L, M, K, P, T, F, A, C, V, X, Z
  e Y). En PC y C64 F1 es una tecla del juego: ahí la captura usa **Ctrl+F1**. Donde Ctrl
  es una tecla del equipo (Spectrum, C64 y PC) una letra suma Shift, como los demás atajos.
  Los menús, la paleta de acciones, **Ayuda > Atajos** y la orden `atajos` muestran la
  combinación que vale para la máquina activa. En la terminal: `opcion atajo-captura`,
  `opcion atajo-captura Ctrl+Shift+S` u `opcion atajo-captura restablecer`.

Estas opciones también están en **Juego > Controles y hardware** y en **Cinta**.

## Rendimiento

**Ahorrar energía cuando no hay partida en curso** está activado de fábrica. Con la
partida en pausa, en la biblioteca o sin máquina, la ventana se dibuja hasta 60 veces
por segundo, y 15 sin foco. Un juego en curso usa siempre la frecuencia del monitor,
porque la emulación y el sonido se reparten en esos cuadros. Desactivá la opción si
preferís que la interfaz se dibuje siempre al máximo. Con la ventana minimizada el
programa espera sin dibujar y la partida queda detenida hasta que vuelvas.

**Pausar el juego al pasar a otra ventana** está desactivado de fábrica. Al volver, la
partida sigue sola solo si la pausó esta opción: una pausa tuya se respeta. Para
silenciar sin pausar, usá **Silenciar al perder el foco** en Sonido.

## Teclado

Mientras un juego corre, el teclado es de la máquina emulada. En la biblioteca, en los
diálogos y con la partida en pausa (F9; Ctrl+F9 en PC):

- **Alt izquierdo** lleva a la barra de menús; las flechas recorren los menús.
- **Tab** y las **flechas** recorren los controles de la ventana activa.
- **Enter** o **Espacio** activan el control marcado y **Esc** vuelve o cierra.

En Spectrum, C64 y PC, Ctrl es una tecla del equipo (SYMBOL SHIFT, C= y Ctrl), por eso
los atajos de herramientas usan **Ctrl+Shift**: O abre un archivo, L la biblioteca,
M modificar y analizar, y K el código. En NES, arcade, fusiones y sin máquina alcanza con
Ctrl. La lista completa para la máquina activa está en **Ayuda > Atajos**.

En la biblioteca, **Ctrl+F** lleva a la búsqueda; desde el inicio busca en todos los juegos.

El atajo de capturas no se activa mientras escribís en un campo o en la consola, ni con
Preferencias o la paleta de acciones abiertas. Mantener la tecla guarda una sola imagen.

**Juego > Recientes** abre de nuevo los últimos juegos, creaciones y fusiones jugados.

## Mando

Con un mando conectado, **Select + Start** abre la biblioteca y, con un juego abierto,
vuelve a él; el juego no recibe el mando hasta que soltás los botones. En la biblioteca,
los diálogos y la pausa, la **cruceta** recorre los controles, **A** elige y **B** vuelve.
Mientras se juega, el mando es de la máquina.

## Capturas y miniaturas

**Juego > Guardar captura de pantalla** (F1 de fábrica; Ctrl+F1 en PC y C64; se cambia
en **Preferencias > Controles**) guarda la pantalla emulada en PNG, a su resolución
original. La paleta de acciones y la orden `captura` hacen lo mismo.

Dentro de la carpeta RetroEmu de tus imágenes, cada juego tiene su propia carpeta:
`RetroEmu/<juego> [<tipo>-<código>]/<juego>-<fecha>-<hora>.png`. El tipo es la máquina de
un original (nes, zx48, c64, pcxt…), `fusion` o `creado`; el código corto distingue juegos
distintos con el mismo título y se mantiene entre sesiones. Un original se reconoce por su
contenido, una fusión o creación del estudio por su identificador, y un juego creado de
8 bits por su carpeta. Si una máquina funciona sin juego, la imagen va a **Sin juego
activo**. Los caracteres no válidos en Windows y los nombres reservados se reemplazan, así
la carpeta también sirve al copiarla a otro sistema.

Ninguna captura reemplaza otra: dos en el mismo segundo llevan -2, -3… Las capturas
anteriores, también las que quedaron sueltas en la raíz por versiones previas, no se
mueven. Si no se puede escribir (sin permiso, disco lleno o un archivo con el nombre de
la carpeta), aparece el motivo con la carpeta, el juego sigue y no queda un PNG a medias.

**Juego > Usar la pantalla como miniatura** guarda esa imagen junto al archivo del juego,
con su mismo nombre, para que la biblioteca la muestre. Solo está disponible para los
juegos originales de tu biblioteca y no reemplaza una imagen que ya esté ahí: para
cambiarla, borrá o renombrá la anterior.

## Salir

Si hay un trabajo de IA en curso (fusión, creación, análisis o una respuesta manual
pendiente), cerrar la ventana o **Juego > Salir** pregunta antes. **Volver** sigue con el
trabajo, **Ver el trabajo** lo muestra y **Salir y cancelar** lo detiene. Lo que ya
estaba guardado se conserva. Sin trabajos en curso, el programa sale como siempre.

## Consola

Cursor, parpadeo, líneas de monitor e historial de órdenes de Terminal real. Están en el grupo
**Consola**; la [guía de la consola](CONSOLA.md) explica cada uno.

## Dónde se guardan

Las preferencias se guardan en preferencias.json dentro de la carpeta de datos de
RetroEmu de tu cuenta, separadas del sonido, la IA, los juegos y las partidas. También
recuerdan la última carpeta del selector de archivos, que se usa mientras exista. Si el
archivo no se puede leer, el panel lo indica, no lo reemplaza y conserva los cambios
solo durante la sesión. **Restablecer valores** vuelve a las opciones de fábrica.
