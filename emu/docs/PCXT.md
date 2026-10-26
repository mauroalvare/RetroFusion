# PC/XT: estado de la implementación

La placa PC/XT está disponible en la biblioteca y el CLI: 8088, hasta 640 KB, CGA,
teclado, altavoz y disquete. El paquete incluye GLaBIOS, FreeDOS y FreeCOM, con sus
licencias y fuentes; un BIOS del usuario tiene prioridad sobre el incluido.
Para empezar, consultá [PC y DOS](PC-DOS.md). La compatibilidad depende del hardware
y los modos que requiere cada programa.

## Componentes actuales

- `I8088Cpu`: ejecución funcional, registros editables, estado y desensamblador.
  [I8088.md](I8088.md) detalla el contraste externo y la temporización pendiente.
- `PcMemory`: RAM configurable de 16 a 640 KB y una copia protegida del BIOS de
  8, 16, 32 o 64 KB, al final del espacio de 1 MB. El firmware se identifica por SHA-256.
- `PcBus`: conecta esa memoria con el 8259A, el 8253, el registro de habilitación NMI
  y el DMA 8237. Las fuentes NMI externas siguen pendientes.
- `Cga6845`: registros MC6845, cursor, RGBI de texto de 40/80 columnas y gráficos de
  320/640 puntos. El oscilador de vídeo recibe tres pulsos por T de CPU; el estado
  de borrado/retrazo depende de ese reloj y no de cuántas veces se lee el puerto.
- `Ppi8255` y `PcKeyboard`: puertos modo 0, selectores de configuración PC/XT,
  puerta de temporizador 2, recepción de códigos de teclado con latch, IRQ1 y reset
  mediante inhibición prolongada del reloj. No implementa un controlador AT/8042.
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

Las tarjetas no instaladas leen bus abierto FF; un modo no implementado de un
dispositivo existente se rechaza explícitamente.
Se comprobó el arranque con la base libre incluida, entrada por teclado y lectura
de archivos. El reloj y la cola de prelectura tienen un modo funcional temporizado;
las señales eléctricas y la secuencia exacta de silicio siguen pendientes. Incluir
el sistema operativo no amplía el hardware que puede emularse.

## Mejoras de precisión pendientes

1. Contrastar tiempos y fases de bus contra trazas externas de hardware.
2. Completar modos del PIT y particularidades de CGA y teclado.
3. Ampliar controladora, geometrías y periféricos con pruebas por modo.
4. Extender observadores y perfiles de ingeniería inversa a direcciones de 20 bits.

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
El paquete incluye la tipografía redistribuible que forma parte del BIOS libre;
no agrega por separado una ROM de caracteres comercial.

Limitaciones: cuadro RGBI completo, sin señal compuesta, nieve, interlace, efectos
por cambios de registros dentro de una línea ni lápiz óptico externo. El estrobo por
puerto conserva su latch. La compatibilidad no incluye programas que dependan de esos efectos.


El 8255 admite direcciones independientes de A/B/C y BSR; los modos con handshake se
rechazan. PcBus permite elegir cableado 5150 o 5160, con una unidad de disquete,
CGA de 80 columnas y sin coprocesador. El modo 5150 expresa RAM adicional en bloques
de 32 KB por SW2. Los contadores de entrada de teclado aproximan la transmisión y
BAT; hay repetición a 30 Hz tras 500 ms, sin modelar flancos individuales del cable.


## DMA y concesión del bus

Dma8237 implementa registros base/actuales por canal, flip-flop común, máscara,
solicitudes, prioridad fija/rotatoria, verify, lectura/escritura, contador terminal,
autoinicio y dirección creciente/decreciente. Cada byte consume cuatro T con concesión
explícita del bus. Single libera al menos un ciclo para la CPU; demand/block conservan
su contrato de solicitud. La página permanece fija al cruzar 64 KB.

I8088Bus.SetBusGranted reconoce HOLD entre transferencias completas. La DMA no opera
mientras el procesador usa el bus o mantiene LOCK. PIT1 solicita refresco por canal0;
la lectura recorre la RAM sin inventar datos ni usar el contador de instrucciones.
Sin memoria a memoria, cascada, polaridades invertidas, READY ni señales eléctricas.
El 8259A admite ICW4 buffered en un controlador individual; esa configuración del
transceptor no altera los vectores ni el contrato de EOI, y no agrega cascada.


## Disquetes y sistema operativo

Fdc765 implementa Specify, Sense Drive/Interrupt, Seek/Recalibrate, Read ID y lectura /
escritura MFM de sectores de 512 bytes. Conecta DRQ al canal DMA2 e IRQ6 al PIC. Cada
byte tiene tiempo de servicio; omitirlo produce overrun. El contador terminal puede
terminar una lectura justo en EOT o después del ID inicial, incluso si ese ID supera
EOT. El estado busy de un seek permanece hasta Sense Interrupt Status.

FloppyDiskImage reconoce geometrías de 160–1440 KB; la controladora XT actual acepta
160–720 KB a 250 kbit/s. Las escrituras se guardan en una capa privada por sector:
no cambian el archivo original. Esa capa se puede guardar/cargar por identidad SHA.
No hay disco duro, controladora AT de alta densidad, formato de pistas, protecciones
con sectores irregulares ni emulación magnética de CRC, rotación o transiciones de flujo.

El arranque usa el BIOS libre y FreeDOS ejecutados en la CPU emulada; no intercepta
INT 13/16 ni sustituye el cargador o el kernel. Las pruebas comprueban lectura de datos
y teclado, además de partidas concretas. Esto no implica compatibilidad con todos
los programas DOS.
Referencia: [documentación NEC uPD765](https://www.cpctech.cpcwiki.de/docs/upd765a/necfdc.htm).


## Máquina y estados completos

PcXt ofrece IMachine con memoria física de 20 bits, desensamblado, entradas de código,
83 teclas set-1 y repetición del teclado. El altavoz sale de PB1 y PIT2, con salida mono
a 44,1 kHz. El cuadro presentado duplica las líneas CGA para facilitar la lectura.
El BIOS identifica el cableado 5150/5160 mediante su byte de modelo; se puede elegir
explícitamente por API. App y CLI usan el mismo hardware.

RETRO-PCXT versión 1 identifica firmware, imagen, modelo y RAM, y protege el contenido
con SHA-256. Guarda CPU, prelectura, memoria, todos los dispositivos, fases de sonido
y capa de disquete. La carga reconstruye un runtime completo antes de sustituir el
activo. Un archivo truncado, dañado o de otro medio no modifica la partida actual.
El contenedor de placa sigue en versión 1; el estado de CPU nuevo usa formato 3 para
retener la prelectura entre T3/T4, con lectura compatible de formatos anteriores.
También se prueba reanudar una lectura de sector en curso y el audio del altavoz.
Reiniciar conserva las escrituras privadas del disco; guardar una partida las incluye.


## Apertura directa con una base libre

EXE/COM y ZIP se preparan con PcDos en un FAT12 privado de 720 KB. El paquete
incluye GLaBIOS 0.4.2 8P, FreeDOS kernel 2043 y FreeCOM 0.86, con licencias y
fuentes correspondientes en system/pcxt. Un BIOS del usuario sigue teniendo prioridad.
El cargador propio usa lecturas BIOS CHS; el sistema ejecuta sus INT 21h de verdad.
Los directorios y datos auxiliares se conservan. Los nombres DOS válidos se reservan
antes de generar alias para carpetas largas y el ejecutable elegido; los archivos de
datos con nombres largos no compatibles se rechazan. Los signos de exclamación literales
no se confunden con los separadores internos de un ZIP. No se ejecutan programas DOS
en el host ni se escribe en los archivos de origen; los alias pertenecen a la copia privada.
El espacio del sistema reduce la capacidad disponible para el juego. No implica soporte
para otra generación de CPU, gráficos ni discos duros.

Los ZIP con varios programas ofrecen una elección por ruta de carpeta. Cancelar restaura
la pausa previa y una apertura rechazada conserva el conjunto de medios anterior.
La elección no se recuerda al reabrir el ZIP. En el CLI, `info` muestra esos archivos
y `run --pick N` selecciona su índice, conservando el resto de los datos del paquete.
