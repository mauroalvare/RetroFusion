# Novedades de 0.3.4

- **Preferencias en un solo lugar.** **Opciones > Preferencias** reúne tamaño del texto,
  alto contraste, pantalla completa, escala entera, joysticks, cintas y ahorro de energía.
  Se conservan al cerrar y al actualizar.
- **Lectura más cómoda.** El texto se agranda de 100 a 200 % en todas las ventanas,
  incluidos el depurador, el estudio y el creador. El alto contraste muestra texto blanco
  sobre negro, bordes visibles y el foco del teclado en amarillo.
- **Teclado y mando en la interfaz.** En la biblioteca, los diálogos y con la partida en
  pausa, Alt izquierdo lleva a los menús y Tab y las flechas recorren los controles. Con un
  mando, la cruceta recorre, A elige y **Select + Start** abre la biblioteca. **Ctrl+F** busca.
- **Atajos que respetan el teclado del equipo.** En Spectrum y C64, como en PC, los atajos
  de herramientas usan Ctrl+Shift: Ctrl solo vuelve a escribir con SYMBOL SHIFT o C=.
- **Menús más completos.** **Juego > Recientes** reabre lo último que jugaste,
  **Ayuda > Atajos** muestra una tabla para la máquina activa y hay una ayuda de
  accesibilidad y teclado.
- **Capturas y miniaturas.** **Juego > Guardar captura de pantalla** guarda un PNG de la
  pantalla emulada. **Usar la pantalla como miniatura** la muestra en la biblioteca, sin
  reemplazar una imagen que ya esté junto al juego.
- **Depurador ordenado.** Sus paneles se ubican según el tamaño de la ventana y
  **Herramientas > Depuración > Reorganizar paneles** los vuelve a acomodar.
- **Menos consumo sin partida en curso.** La ventana se dibuja hasta 60 veces por segundo
  y 15 sin foco. Una opción pausa el juego al pasar a otra ventana.
- **Salir sin perder trabajo por error.** Si hay un trabajo de IA en curso, el programa
  pregunta antes de cerrarse.
- **Más precisión del 8088 en PC/XT.** Las instrucciones que leen y escriben memoria
  separan sus accesos como en el hardware, y las interrupciones leen su vector antes de
  guardar el retorno.
- **Correcciones.** El programa de prueba del depurador ya no cierra la aplicación con
  bytes inválidos; la imagen del juego ya no queda bajo la barra de menús.

La interfaz no se comunica con lectores de pantalla. Con texto grande en una ventana chica,
algunos paneles necesitan desplazamiento. La precisión del 8088 todavía no cubre todas las
instrucciones.

[Preferencias y teclado](PREFERENCIAS.md) · [Límites actuales](https://github.com/mauroalvare/RetroFusion/blob/v0.3.4/emu/docs/LIMITACIONES.md)
