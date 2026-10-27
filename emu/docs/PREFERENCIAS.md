# Preferencias, accesibilidad y teclado

Abrí **Opciones > Preferencias**. Los cambios se aplican en el momento y se conservan
al cerrar y actualizar el programa.

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

**Juego > Recientes** abre de nuevo los últimos juegos, creaciones y fusiones jugados.

## Mando

Con un mando conectado, **Select + Start** abre la biblioteca y, con un juego abierto,
vuelve a él. En la biblioteca, los diálogos y la pausa, la **cruceta** recorre los
controles, **A** elige y **B** vuelve. Mientras se juega, el mando es de la máquina.

## Salir

Si hay un trabajo de IA en curso (fusión, creación, análisis o una respuesta manual
pendiente), cerrar la ventana o **Juego > Salir** pregunta antes. **Volver** sigue con el
trabajo, **Ver el trabajo** lo muestra y **Salir y cancelar** lo detiene. Lo que ya
estaba guardado se conserva. Sin trabajos en curso, el programa sale como siempre.

## Dónde se guardan

Las preferencias se guardan en preferencias.json dentro de la carpeta de datos de
RetroEmu de tu cuenta, separadas del sonido, la IA, los juegos y las partidas. Si el
archivo no se puede leer, el panel lo indica, no lo reemplaza y conserva los cambios
solo durante la sesión. **Restablecer valores** vuelve a las opciones de fábrica.
