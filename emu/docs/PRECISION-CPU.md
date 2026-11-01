# Alcance y verificación de los procesadores

**No está acreditado un 100 % de fidelidad de todos los procesadores.** Hay cuatro
motores de instrucciones y variantes que los reutilizan. Pasar una batería comprueba
los casos que contiene; no demuestra todas las combinaciones de estados, interrupciones,
señales y revisiones físicas. Tampoco equivale a completar la máquina que rodea a la CPU.

## Qué está implementado y cómo se comprueba

| Procesador | Implementación y evidencia | Límite que impide afirmar un 100 % general |
|---|---|---|
| Z80 | Instrucciones base y prefijadas, flags no documentados, MEMPTR y Q. FUSE: 1.356 casos con registros, memoria y tiempos de accesos; ZEXDOC y ZEXALL. IM 0 ejecuta el opcode del bus con reconocimiento de seis T; operandos y colas de prefijos se leen desde PC. HALT ignora cambios del byte leído mientras espera. El estado Spectrum conserva la demora tras EI y la NMI pendiente. | IM 0 y los bordes de restauración se comprueban con programas propios, no con una campaña externa de señales. El bus permite contención y demoras de E/S, sin un modelo general de todas las señales y revisiones del chip. |
| Intel 8080 | Los 256 opcodes, incluidos los alias; TST8080, 8080PRE, CPUTEST y 8080EXM. Modo temporizado optativo con accesos en T3, reloj por T y esperas READY, usado por la placa arcade. Los tres primeros diagnósticos conservan sus totales exactos en ese modo. El estado de la placa conserva la demora tras EI. | Los ciclos internos restantes se agrupan al final; sin contraste externo de cada fase ni pines SYNC/DBIN/WR, sincronización eléctrica de READY o HOLD/HLDA certificados. [Detalle](I8080.md). |
| MOS 6502 NMOS / 6510 | Los 256 opcodes, incluidos JAM y los no documentados; modo decimal y accesos de bus ciclo a ciclo. SingleStepTests: 1.000 casos por opcode; Klaus Dormann. Se contrastan IRQ en las ocho ramas, NMI tardía, anidación durante IRQ/BRK y restauración de las muestras. Los ciclos robados muestrean interrupciones; el C64 cuenta las esperas de VIC-II también en el reloj del 6510. | Se verifica un modelo NMOS, no todas las revisiones ni el 65C02. Los casos propios no certifican todas las coincidencias entre señales ni la aceptación exacta al recuperar RDY/DMA. ANE/LXA usan una constante fija EEh. El 6510 comparte el motor y tiene pruebas de placa, sin una suite externa independiente de esa variante. |
| Ricoh 2A03 | Reutiliza el 6502 con decimal desactivado. nestest: 8.991 estados de CPU, incluidos flags y ciclo absoluto, coincidentes hasta 26.554 T. OAM espera una lectura, conserva la última escritura RMW y alinea 513/514 ciclos; DMC roba 3/4 ciclos y comparte GET con OAM. Se muestrean NMI durante DMA y se guardan la página pendiente, la espera inicial DMC y la muestra NMI. | nestest no contrasta PPU/APU/DMA. La activación DMC se comprueba con programas propios en ambas paridades; siguen pendientes aborto/reinicio fino, bordes del solapamiento, conflictos de registros internos y variantes Famicom/PAL. [DMA](NES-DMA.md), [nestest](NESTEST.md). |
| Intel 8088 | Instrucciones documentadas sin coprocesador, segmentos, interrupciones y reloj optativo con esperas READY. Contraste funcional: 2.559.500 casos seleccionados, con 447.500 excluidos por metadatos. Cadenas, indirectos y marcos de INT/división coinciden en los intervalos contrastados. | Los flags indefinidos apilados difieren. No se certifican reloj absoluto, prelectura completa, sincronización eléctrica de READY/INTA ni señales físicas; multiplicación/división conservan totales conservadores. [Detalle](I8088.md). |
| Intel 8086 | Comparte el motor del 8088; bus de palabra según alineación, cola de seis bytes y estados separados. Corpus independiente P80C86A-2: 552.744 casos funcionales correctos, 93.256 excluidos; tipos/direcciones/orden e intervalos de 1.475.052 pares coinciden. | Conserva 4.787 casos con valores distintos solo en flags indefinidos. BIU y correcciones de fase de indirectos aproximadas; sin reloj absoluto, prelectura final ni placa 8086 certificados. Una suite finita no valida todas las revisiones. |

Los conteos de casos de las suites se distinguen de las pruebas NUnit: una sola prueba
del 6502 recorre cientos de miles de casos, y una del 8080 ejecuta miles de millones
de instrucciones. No se convierten esos conteos en un porcentaje de fidelidad.

## Interrupciones, reinicio y restauración revisados

La revisión del 30 de octubre de 2026 encontró fallos fuera de los casos de instrucción
aislada. En el 6502, una rama tomada conservaba siempre la muestra del penúltimo ciclo
físico: podía perder un pulso IRQ temprano o aceptar otro demasiado pronto. Ahora conserva
la muestra del fetch de opcode y, si cruza una página, también acepta la del ciclo previo
a corregir PCH. Se comprueban los ocho opcodes de rama.

La NMI distingue el flanco pendiente de la muestra disponible para aceptar la interrupción.
Un flanco que aparece en el último ciclo de una instrucción espera otro punto de muestreo.
Los casos cubiertos durante las lecturas del vector IRQ/BRK ejecutan una instrucción del
handler antes de anidar la NMI. El caso tardío comprueba los siete accesos de la entrada,
el PC apilado y P con B=0/U=1. Reset limpia las decisiones pendientes. El criterio sigue
el [análisis de temporización de Visual6502](https://www.nesdev.org/wiki/Visual6502wiki/6502_Timing_of_Interrupt_Handling);
las regresiones usan señales por ciclo entero, sin medir medias fases eléctricas.

En Z80 y 8080, los guardados de máquina omitían la demora de una instrucción tras EI.
Spectrum también omitía la NMI pendiente. Restaurar podía perder esos latches o conservar
los de la ejecución que se abandonaba. Los formatos nuevos los guardan explícitamente.
Se prueba cargar en la misma máquina y en otra nueva, restaurar antes/después de EI,
conservar o descartar NMI y limpiar los latches mediante reset.

| Estado de máquina escrito | Versiones anteriores aceptadas | Tratamiento de los datos que faltaban |
|---|---|---|
| Spectrum 48K, formato 2 | 1 | La demora EI y la NMI pendiente se inicializan en falso; ese formato nunca las almacenó. |
| Placa arcade 8080, formato 2 | 1 | La demora EI se inicializa en falso. |
| C64, formato 2 | 1 | La muestra NMI nueva toma la decisión de flanco pendiente del formato anterior. |
| NES, formato 7 | 2, 3, 4, 5 y 6 | La muestra NMI usa la decisión anterior; se mantienen las restricciones previas de edición y mapper. NES2/3 no tienen OAM pendiente; NES2/3/4 restauran la espera inicial DMC en cero. NES2–6 no guardaban el integrador/filtro de audio y los inicializan en reposo. |

Los formatos nuevos se leen en este programa; no se promete que versiones anteriores del
programa puedan leerlos. Aceptar un estado antiguo tampoco recupera una fase que nunca
se guardó. El bloque directo de `M6502.Save` pasa de 29 a 30 bytes: un consumidor que tenga
su propio contenedor debe versionarlo y usar `Load(reader, hasNmiPoll: false)` para el
bloque antiguo. Los consumidores de máquina actuales, C64 y NES, lo hacen explícitamente.
La máquina sigue configurando la variante: `DecimalEnabled` no forma parte de ese bloque.
Los estados de máquina se toman en límites de instrucción; no reanudan el interior de
una instrucción ni una DMA parcialmente ejecutada.

## Evidencia de esta revisión

Se agregaron 44 casos NUnit: 27 de temporización/restauración del núcleo 6502,
7 de latches Spectrum, 5 de latches arcade, 4 de alineación de estados NES2–5 y
1 de alineación C64 anterior. Veinte casos fallaron antes de las correcciones; no
representan veinte defectos independientes ni un porcentaje de cobertura.

Las pruebas de formatos anteriores usan fixtures con su disposición histórica: verifican
que CPU, RAM y periféricos quedan alineados y comparan el estado y la siguiente instrucción.
No son una campaña con todos los guardados existentes de todos los juegos.

La comprobación dirigida de esta revisión aprobó Z80 1.379, 8080 31, 6502 36 y
8088/8086 624 pruebas; Spectrum 26, NES 114 y C64 24. Arcade aprobó 19 y omitió una
por datos opcionales ausentes. El 6502 incluye los 256.000 casos de SingleStepTests y
Dormann; NES incluye nestest local. Esta pasada dirigida omitió las pruebas `Long`
de Z80/8080 y no añadió código ni corpus externo al 8088/8086. La batería completa
sigue siendo una comprobación separada.
La ejecución integral posterior incluyó también `Long`: Z80 aprobó 1.381 casos y
8080 aprobó 32. Los quince proyectos terminaron sin fallos; las correcciones posteriores
de interfaz y laboratorio tuvieron revalidaciones propias. Esos resultados mantienen
los límites de señales y variantes indicados arriba.

Quedan sin certificar las medias fases, anchos mínimos de pulso y coincidencias de
IRQ/NMI/BRK/reset, la decisión al recuperar RDY/DMA y todas las variantes físicas.
Los guardados nuevos preservan los latches modelados; eso no acredita un modelo de
pines completo ni la emulación completa de sus placas.

## Contrastes previos de 8088/8086

El modelo 8088 ubica seis T entre las palabras de LES/LDS y siete T entre
la lectura de PUSH de memoria y su escritura en la pila, sin cambiar la tabla nominal.
Sus 30.000 casos externos coinciden en accesos y 84.994 pares de intervalos. POP de
memoria, CALL lejano directo, RETF e IRET también ubican el trabajo entre transferencias:
otros 50.000 casos y 170.000 pares coinciden, con la tabla nominal intacta. Las pruebas
propias incluyen HOLD durante prelectura y NMI durante POP. La entrada de REP ya
coincide en los 20.000 casos de cadenas y 1.045.870 pares. Los indirectos coinciden
en 40.000 casos y 132.437 pares; INT/INTO/AAM/DIV/IDIV, en los intervalos de 80.000
casos y 488.394 pares. Se conservan 25.049 casos con flags indefinidos distintos en
la pila. Esto no completa el 8088 ni certifica el 8086.

## Cómo reproducir las comprobaciones

Desde `emu`, con las suites locales disponibles:

```powershell
dotnet build RetroEmu.slnx -c Release
dotnet test RetroEmu.slnx -c Release --no-build
dotnet test tests/Retro.Cpu.Z80.Tests -c Release --no-build
dotnet test tests/I8080.Tests -c Release --no-build
dotnet test tests/M6502.Tests -c Release --no-build
dotnet test tests/I8088.Tests -c Release --no-build
dotnet test tests/M6502.Tests -c Release --no-build --filter FullyQualifiedName~InterruptTimingTests
dotnet test tests/Zx.Tests -c Release --no-build --filter FullyQualifiedName~InterruptStateTests
dotnet test tests/Arcade.Tests -c Release --no-build --filter FullyQualifiedName~InterruptStateTests
dotnet test tests/Nes.Tests -c Release --no-build --filter FullyQualifiedName~LegacyStatesKeepCpuRamAndPeripheralPayloadsAligned
dotnet test tests/C64.Tests -c Release --no-build --filter FullyQualifiedName~LegacyStateTests
```

La primera orden de pruebas ya incluye los cuatro proyectos; las demás permiten
revisarlos por separado. El filtro `TestCategory!=Long` omite ZEXDOC/ZEXALL y 8080EXM,
por lo que no sustituye la batería completa. Las pruebas omitidas por falta de datos
se informan como tales. No se descargan suites automáticamente.

Las fuentes de estas afirmaciones están en [FUSE](../tests/Retro.Cpu.Z80.Tests/FuseTests.cs),
[ZEX](../tests/Retro.Cpu.Z80.Tests/ZexTests.cs),
[diagnósticos del 8080](../tests/I8080.Tests/CpmConformanceTests.cs),
[conformidad del 6502](../tests/M6502.Tests/ConformanceTests.cs),
[temporización IRQ/NMI y restauración](../tests/M6502.Tests/InterruptTimingTests.cs),
[latches Spectrum](../tests/Zx.Tests/InterruptStateTests.cs),
[latches arcade](../tests/Arcade.Tests/InterruptStateTests.cs),
[estados anteriores NES](../tests/Nes.Tests/MachineTests.cs),
[estado anterior C64](../tests/C64.Tests/LegacyStateTests.cs),
[bus e interrupciones del Z80](../src/Retro.Cpu.Z80/Z80Cpu.cs),
[núcleo y bus del 8080](../src/Retro.Cpu.I8080/I8080Cpu.cs),
[núcleo del 6502](../src/Retro.Cpu.M6502/M6502.cs),
[placa C64](../src/Retro.Systems.C64/C64.cs), [placa NES](../src/Retro.Systems.Nes/Nes.cs),
[pruebas de punteros/pila](../tests/I8088.Tests/PointerStackTimingTests.cs) y
[pruebas de marcos y control](../tests/I8088.Tests/StackControlTimingTests.cs).

## Revisión de NMI del Z80 y matriz de alcance

La aceptación de NMI ahora realiza la lectura M1 en PC, ignora el dato y aplica la
contención antes de apilar el retorno y saltar a0066. Antes omitía esa lectura y la
espera de bus. Cuatro casos reproducían el fallo: ejecución/HALT con cero/tres esperas.
Se contrastan orden de lectura/escrituras,11T más esperas,PC/SP,R,flags e IFF.
La descripción del ciclo está en el [manual de Zilog](https://www.zilog.com/docs/z80/z80cpu_um.pdf).
No se incorpora una simulación de medias fases ni se modifica el formato de estados.

| Núcleo | Contraste de instrucciones/ciclos | Señales y estado revisados | Alcance pendiente |
|---|---|---|---|
| Z80 | FUSE, ZEXDOC/ZEXALL, casos de prefijos | IRQ/IM0–2,HALT,EI,NMI y contención; latches guardados en Spectrum | Medias fases, pulsos y todas las coincidencias físicas |
| 8080 | Diagnósticos CP/M y8080EXM | INTE/EI,HALT,READY y latches de la placa arcade | Todos los dispositivos/variantes y coincidencias de pines |
| 6502/6510/2A03 | Dormann,SingleStepTests y nestest para2A03 | IRQ/NMI/BRK,reset,RDY,paradas y DMA modeladas; estados NES/C64 | Concurrencias RDY/DMA y variantes físicas completas |
| 8088/8086 | Suite local y contraste externo conservado | Bus,READY,HOLD,NMI/IRQ/REP y estado modelados | Placa8086,variantes y precisión absoluta; sin nueva ampliación en esta pasada |

La batería integral de esta revisión incluye Long y los cuatro proyectos de CPU:
Z801385,808032,650236 y8088/8086624 aprobados. También se ejecutan sus máquinas y
los recorridos de pausa/guardado/reanudación. Los números cuentan pruebas, no cobertura
porcentual ni certificación de una CPU al100%.
