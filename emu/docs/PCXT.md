# PC/XT: estado de la implementación

El PC/XT todavía no está disponible para jugar. La CPU, la placa, el firmware y el
sistema operativo son etapas distintas. Implementar el 8088 no incorpora BIOS ni DOS.

## Componentes actuales

- `I8088Cpu`: ejecución funcional, registros editables, estado y desensamblador.
  [I8088.md](I8088.md) detalla el contraste externo y la temporización pendiente.
- `PcMemory`: RAM configurable de 16 a 640 KB y una copia protegida del BIOS de
  8, 16, 32 o 64 KB, al final del espacio de 1 MB. El firmware se identifica por SHA-256.
- `PcBus`: conecta esa memoria con el 8259A, el 8253, el registro de habilitación NMI
  y los registros de página DMA. Aún no implementa el motor DMA ni fuentes NMI.
- `Cga6845`: registros MC6845, cursor, RGBI de texto de 40/80 columnas y gráficos de
  320/640 puntos. El oscilador de vídeo recibe tres pulsos por T de CPU; el estado
  de borrado/retrazo depende de ese reloj y no de cuántas veces se lee el puerto.
- El canal 0 del PIT entrega flancos a IRQ0. `TickPit` recibe un pulso externo por canal;
  el modo CPU temporizado entrega además un pulso cada cuatro T. El contador
  de instrucciones sigue siendo independiente del reloj.

| Región física | Acceso actual |
| --- | --- |
| Desde 00000 hasta el final de la RAM instalada | Lectura y escritura |
| B8000–BBFFF | 16 KB de RAM de vídeo CGA |
| BC000–BFFFF | Espejo de la misma RAM de vídeo |
| Final del espacio hasta FFFFF, según el tamaño del BIOS | ROM; escrituras ignoradas |
| Regiones sin memoria instalada | Lectura FF; escrituras ignoradas |

La RAM de vídeo ya produce un cuadro CGA por API. Los puertos pendientes fallan con
su dirección para localizar el trabajo restante. Ese comportamiento sirve para diagnóstico;
la placa sigue incompleta. No hay teclado, discos, arranque DOS ni integración en el
selector de máquinas. El reloj y la cola de prelectura tienen un modo funcional temporizado; las señales
eléctricas y la secuencia exacta de silicio siguen pendientes. No se incluye firmware nuevo en el paquete.

## Orden de trabajo pendiente

1. Reloj del 8088 y acoplamiento de los dispositivos, con evidencia independiente de
   instrucciones, transferencias y tiempos.
2. Completar integración CGA, 8255 y teclado, DMA 8237 y conexiones de placa.
3. Completar el POST, la comprobación de encendido, con firmware libre aportado por
   separado. Las pruebas deben usar el comportamiento real de los dispositivos.
4. Controladora de disquetes, carga de imágenes y sistema operativo libre; estados,
   controles e integración en app y CLI. Comprobar partidas antes de anunciar jugabilidad.

Referencias: [IBM 5150, mapa de E/S y NMI](https://www.minuszerodegrees.net/manuals/IBM_5150_Technical_Reference_6322507_APR84.pdf),
[implementación CGA de MAME](https://github.com/mamedev/mame/blob/master/src/devices/bus/isa/cga.cpp).

## Alcance de CGA

Puertos 3D0–3D7 espejan índice/datos del MC6845; 3D8/3D9 seleccionan modo/color y
3DA devuelve DE invertida, VSYNC y latch de lápiz óptico. Los puertos MDA devuelven
bus abierto porque no hay una segunda tarjeta instalada. Se respetan las máscaras
y los registros de solo lectura del controlador.

El dibujo usa la dirección inicial, atributos, cursor y bancos entrelazados de 8 KB.
Acepta una fuente de 1024/2048 bytes aportada por el llamador. Sin ella usa los 128
caracteres del BIOS compatible en FFA6E; los extendidos necesitan la fuente completa.
No se incorporó una ROM de caracteres nueva al paquete.

Limitaciones: cuadro RGBI completo, sin señal compuesta, nieve, interlace, efectos
por cambios de registros dentro de una línea ni lápiz óptico externo. El estrobo por
puerto conserva su latch. No equivale todavía a una máquina PC lista para jugar.
