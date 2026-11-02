# Novedades de 0.3.7

- **Funciona en más equipos.** Un equipo sin salida de audio utilizable ya no pierde
  velocidad: los juegos corren en silencio a su ritmo normal, el programa lo avisa y puede
  volver a buscar la salida sin reiniciar. Se fue la ventana negra que acompañaba al
  programa. La ventana se adapta a pantallas chicas, y si el equipo no ofrece los gráficos
  necesarios o algo falla, RetroEmu lo explica y deja un informe en lugar de cerrarse sin decir nada.
- **Terminal hardcore real.** La consola se comporta como una terminal: el prompt sigue a
  la salida, se puede seleccionar y copiar texto, buscar órdenes anteriores con Ctrl+R,
  completar con Tab y revisar lo que se pega antes de ejecutarlo. Hay más de setenta órdenes
  para biblioteca, partida, análisis, proyectos y tareas, con ayuda generada y la búsqueda
  `como <tema>`. [Guía de la consola](CONSOLA.md).
- **Tareas en segundo plano.** Validar, preparar y los trabajos de los talleres muestran
  qué están haciendo y qué esperan, se pueden cancelar y no bloquean el programa. El avance
  solo aparece cuando algo se contó de verdad, y una tarea sin actividad se señala.
- **Biblioteca renovada.** Muestra el doble de juegos en la misma ventana, con un panel de
  detalle y la selección conservada al volver de una partida. Ahora se puede elegir la
  carpeta de juegos, y a texto grande la lista sigue siendo usable.
- **Preferencias y estilos.** Preferencias se organiza por grupos, con búsqueda y vuelta al
  valor inicial por ajuste. El selector de estilos muestra una escena con los componentes
  reales de cada interfaz, se puede ampliar y mirar no cambia nada.
- **Identificación revisable.** Cuando no hay una coincidencia segura, el catálogo opcional
  propone candidatos y explica por qué. Tus decisiones perduran aunque el catálogo cambie.
- **Foco y teclado.** El Enter que abre un juego ya no llega a la máquina y los atajos de la
  consola no se filtran a la partida. Todos los textos se leen en cada estilo: se acabaron
  los signos de pregunta en lugar de símbolos.

Los ajustes, partidas, fichas y proyectos anteriores siguen leyéndose.

**Requisitos:** Windows 10 o posterior de 64 bits, o Linux x64, con gráficos compatibles con
OpenGL 3.3. No hace falta instalar .NET ni otros componentes. El sonido es opcional.

La revisión aprobó las pruebas automáticas de todos los módulos y recorridos con eventos reales
en una ventana oculta, en cuatro teclados de máquina, a distintos tamaños y escalas de texto.
La lista se mantiene por debajo de cinco milisegundos por cuadro con 6000 juegos. La actualización
se probó en una copia aislada, conservando los datos.

Esto no certifica todos los equipos: no se probó en Linux real, en Windows 10, con controles
físicos ni en una máquina sin audio físico (ese caso se simuló). Sigue haciendo falta OpenGL
3.3, y algunas funciones siguen en sus instrumentos visuales: la
[matriz de cobertura](CONSOLA.md#cobertura-por-órdenes) lo detalla.
