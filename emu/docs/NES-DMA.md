# Esperas de 6502/6510 y DMA del 2A03

M6502.ClockStall permite que la máquina avance primero sus periféricos y después
muestree las líneas de interrupción de la CPU durante un ciclo robado. Incrementa
el reloj sin ejecutar instrucciones ni acceder a memoria por cuenta del procesador.
Conserva las NMI transitorias. El estado del núcleo distingue el flanco NMI pendiente
de la muestra que permite aceptarlo; su formato actual incluye ambos.

El C64 lo usa mientras VIC-II mantiene BA/RDY bajo en las lecturas. El contador
del 6510 ahora incluye esas esperas: un cuadro completo sigue teniendo un campo PAL
de reloj, aunque con la pantalla encendida quepan menos instrucciones. Las escrituras
conservan su recorrido sin espera. Los calendarios de VIC/CIA/SID no se duplican.

En la NES NTSC, escribir $4014 pide una DMA de OAM. Se atiende en la siguiente lectura
de CPU; escrituras consecutivas, incluidas las dos de una instrucción RMW, dejan la
última página solicitada. Una lectura de parada, la alineación necesaria y 256 pares
GET/PUT producen 513 o 514 ciclos. PPU, APU y el muestreo de interrupciones avanzan
durante cada ciclo robado. Las NMI que se activan y desactivan dentro de la DMA se
conservan para el recorrido de aceptación del núcleo.

El lector DMC de la APU ofrece su dirección pendiente al árbitro. Espera a una lectura
de CPU, realiza parada/dummy/alineación/GET y roba tres o cuatro ciclos. Sus lecturas
tienen prioridad sobre los GET de OAM; OAM reintenta ese byte sin perderlo. La consola
NES evita desplazar otra vez los mandos en cada dummy consecutivo. La APU conserva
su callback de lectura inmediata predeterminado para usos independientes; la NES
configura explícitamente el arbitraje externo.

Al activar una muestra mediante $4015, la máquina demora la petición inicial dos o
tres ciclos según la paridad de la escritura; esos ciclos incluyen el Tick de la
escritura y preceden al intento de detener una lectura. Una petición madura conserva
el recorrido de parada/dummy/alineación/GET. Este calendario se contrastó con el
[modelo DMC de Mesen2, revisión b9fa69d](https://github.com/SourMesen/Mesen2/blob/b9fa69ddc6d0a331fb103fdb5eef6904305703c2/Core/NES/APU/DeltaModulationChannel.cpp)
y se comprobó con programas propios en ambas paridades. No es una medición de silicio.

Los estados de NES usan el formato 7: conservan la página OAM pendiente, la demora
inicial DMC y la muestra NMI del núcleo al final de una instrucción. Se siguen aceptando
los formatos 2, 3, 4, 5 y 6 con sus restricciones de edición y mapper existentes; 2/3 no
contienen una petición OAM pendiente y 2/3/4 restauran la demora DMC en cero
(comportamiento anterior). El bloque CPU pasa de 29 a 30 bytes; al leer NES2–5 la muestra
NMI toma la decisión de flanco pendiente que guardaba el núcleo anterior, sin consumir
bytes del estado de RAM. NES7 agrega 16 bytes con integración y filtro de audio; NES2–6
no guardaban esa historia y la restauran en reposo. La cola ya emitida se descarta.
El formato APU no cambia. Un estado antiguo no permite recuperar
un punto de muestreo que nunca almacenó, y no se promete que un programa anterior lea NES6.
No se guardan puntos intermedios de una DMA ejecutada dentro de una instrucción.

Catorce pruebas propias de NES cubren paridad, RMW, convivencia OAM/DMC, escrituras,
cancelación, mandos, restauración y NMI transitoria; doce detectan el estado anterior.
Se agregan pruebas de ciclos robados del núcleo y de RDY en el 6510. Siete casos nuevos
verifican activación, guardado durante la espera, lectura de NES4 y demoras inválidas;
los dos de activación detectan el inicio prematuro anterior. La suite [nestest](NESTEST.md)
se usa localmente, con descarga autorizada; no se distribuye su ROM.

La revisión de interrupciones agrega 27 casos del núcleo: IRQ en ramas con y sin cruce
de página, NMI en el último ciclo y durante IRQ/BRK, bus y pila de la entrada, reset y
restauración de muestras. Cuatro fixtures de NES2–5 comprueban la alineación de CPU, RAM
y periféricos y la siguiente instrucción al cargarlos en el formato actual. Son pruebas
por ciclo entero; no una campaña de señales físicas. Los detalles y comandos están en
[Precisión de CPU](PRECISION-CPU.md).

Quedan el aborto y reinicio fino del DMC, casos límite de solapamiento
al comienzo/final de OAM, conflictos del bus de registros internos, variantes Famicom/
PAL y el instante preciso de aceptación de interrupciones al recuperar la CPU.
La suite externa específica de DMA sigue pendiente: nestest contrasta CPU, no estos bordes.
La revisión del lector DMC comprueba en la APU aislada que detener la lectura conserva
el byte ya almacenado y su reproducción, y que reactivar espera a liberar ese búfer
antes de leer desde la nueva dirección. Un reset completo, en cambio, limpia búfer,
desplazamiento y temporizador: antes podía reproducir bits anteriores tras reiniciar.
Se contrastó con el estado de reset y el lector del modelo Mesen citado arriba, mediante
programas propios. Estas pruebas no afirman el instante de aborto de una DMA en la NES.
Referencia primaria adicional de comparación:
[arbitraje de DMA de Mesen](https://github.com/SourMesen/Mesen2/blob/master/Core/NES/NesCpu.cpp).

El ciclo de encendido también reconstruye los canales de audio, incluidos los divisores,
secuenciadores, envolventes y paridad de reloj. Antes podían sobrevivir fases de la partida
anterior: repetir el mismo programa tras apagar/encender producía muestras diferentes.
El reinicio suave conserva el tratamiento anterior de los canales y no se presenta como
un modelo completo de reset físico. El DMC inicia su contador a 427 para esperar el primer
período NTSC de 428 ciclos, en lugar de avanzar en el primer ciclo. Este punto se contrasta
con Reset del modelo DMC de Mesen citado y se comprueba con un byte propio conocido.
No cambia el formato NES7 ni resuelve los abortos y variantes pendientes.
