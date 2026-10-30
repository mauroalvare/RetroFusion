# Esperas de 6502/6510 y DMA del 2A03

M6502.ClockStall permite que la máquina avance primero sus periféricos y después
muestree las líneas de interrupción de la CPU durante un ciclo robado. Incrementa
el reloj sin ejecutar instrucciones ni acceder a memoria por cuenta del procesador.
Conserva las NMI transitorias y usa el formato de estado existente del núcleo.

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

Los estados de NES pasan a formato 5 para conservar la página OAM pendiente y la
demora inicial DMC al final de una instrucción. Se siguen aceptando los formatos 2,
3 y 4 con sus restricciones de mapper existentes; 2/3 no contienen una petición OAM
pendiente y 2/3/4 restauran la demora DMC en cero (comportamiento anterior). El estado
de APU y el de CPU conservan sus formatos. No se guardan puntos intermedios de una DMA
ejecutada dentro de una instrucción.

Catorce pruebas propias de NES cubren paridad, RMW, convivencia OAM/DMC, escrituras,
cancelación, mandos, restauración y NMI transitoria; doce detectan el estado anterior.
Se agregan pruebas de ciclos robados del núcleo y de RDY en el 6510. Siete casos nuevos
verifican activación, guardado durante la espera, lectura de NES4 y demoras inválidas;
los dos de activación detectan el inicio prematuro anterior. La suite [nestest](NESTEST.md)
se usa localmente, con descarga autorizada; no se distribuye su ROM.

Quedan el aborto y reinicio fino del DMC, casos límite de solapamiento
al comienzo/final de OAM, conflictos del bus de registros internos, variantes Famicom/
PAL y el instante preciso de aceptación de interrupciones al recuperar la CPU.
La suite externa específica de DMA sigue pendiente: nestest contrasta CPU, no estos bordes.
Referencia primaria adicional de comparación:
[arbitraje de DMA de Mesen](https://github.com/SourMesen/Mesen2/blob/master/Core/NES/NesCpu.cpp).
