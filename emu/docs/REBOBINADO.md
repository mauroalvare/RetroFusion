# Rebobinado y grabación de entradas

Abrí un original y usá **Juego > Rebobinado y entradas**. Estas herramientas trabajan
en memoria durante la sesión; no modifican los archivos del juego ni sus ranuras.

## Volver a un momento anterior

Activá **Conservar historial (hasta 64 MB)**. Se conserva un estado aproximadamente
cada medio segundo. **Retroceder cinco segundos** vuelve al estado disponible cercano
a ese momento y deja la partida pausada. Pulsá **Continuar** para jugar desde ahí.
El historial posterior al punto elegido se descarta para que la partida siga una sola
línea. Si todavía no hay cinco segundos, se usa el estado anterior más antiguo disponible.

El tiempo conservado depende del tamaño de los estados y de la pantalla: el límite
es 64 MB y 600 puntos. El menú muestra el espacio ocupado. Podés vaciar o desactivar
el historial cuando quieras. Viene desactivado para evitar ese costo de memoria.

## Grabar y reproducir controles

1. Elegí **Grabar entradas desde aquí**. Se conserva el estado inicial, se liberan las
   teclas y se cancela la escritura automática pendiente para comenzar desde el mismo
   punto que usará la reproducción.
2. Jugá con teclado o mando y elegí **Terminar grabación**. Se conservan hasta cinco
   minutos, con límites adicionales de entradas para mantener el uso de memoria acotado.
3. Elegí **Reproducir última grabación**. La partida vuelve a ese estado inicial y
   recibe las mismas pulsaciones y liberaciones, en el mismo orden por cuadro.

Durante la reproducción se retienen los controles reales del juego. Podés pausarla
o usar **Detener reproducción**. Al terminar, la partida queda pausada en el último
cuadro y se liberan las teclas. El estado indica cuántos cuadros se verificaron.

La pantalla de cada cuadro se compara con su huella SHA-256 grabada. Si difiere,
la reproducción se detiene en ese punto y lo informa; no se declara verificada.
La comprobación de pantalla no certifica todos los estados internos de un juego.

## Alcance

- Disponible para originales con estados guardados; las fusiones no guardan el estado
  completo de su guion y quedan fuera de este circuito.
- La grabación captura controles del juego. Cambios de memoria, hardware, firmware o
  acciones externas no se convierten en entradas. El depurador interrumpe la captura.
- Un cambio de medio descarta historial y grabación. Reiniciar o cargar un estado
  interrumpe la captura activa y vacía el historial; una grabación ya terminada de la
  misma edición sigue disponible. No se puede empezar mientras arranca DOS o hay
  puntos de interrupción activos.
- Historial y última grabación se pierden al cerrar. Todavía no hay exportación de
  películas ni rebobinado de fusiones. Las cinco ranuras de cada juego siguen siendo
  el medio de conservar partidas entre sesiones.
