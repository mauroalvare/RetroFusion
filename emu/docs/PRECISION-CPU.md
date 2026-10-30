# Alcance y verificación de los procesadores

**No está acreditado un 100 % de fidelidad de todos los procesadores.** Hay cuatro
motores de instrucciones y variantes que los reutilizan. Pasar una batería comprueba
los casos que contiene; no demuestra todas las combinaciones de estados, interrupciones,
señales y revisiones físicas. Tampoco equivale a completar la máquina que rodea a la CPU.

## Qué está implementado y cómo se comprueba

| Procesador | Implementación y evidencia | Límite que impide afirmar un 100 % general |
|---|---|---|
| Z80 | Instrucciones base y prefijadas, flags no documentados, MEMPTR y Q. FUSE: 1.356 casos con registros, memoria y tiempos de accesos; ZEXDOC y ZEXALL. IM 0 ejecuta el opcode del bus con reconocimiento de seis T; operandos y colas de prefijos se leen desde PC. HALT ignora cambios del byte leído mientras espera. | IM 0 se comprueba con programas propios, no con una campaña externa de señales. El bus permite contención y demoras de E/S, sin un modelo general de todas las señales y revisiones del chip. |
| Intel 8080 | Los256 opcodes, incluidos los alias; TST8080, 8080PRE, CPUTEST y 8080EXM. Modo temporizado optativo con accesos en T3, reloj por T y esperas READY, usado por la placa arcade. Los tres primeros diagnósticos conservan sus totales exactos en ese modo. | Los ciclos internos restantes se agrupan al final; sin contraste externo de cada fase ni pines SYNC/DBIN/WR, sincronización eléctrica de READY o HOLD/HLDA certificados. [Detalle](I8080.md). |
| MOS 6502 NMOS / 6510 | Los 256 opcodes, incluidos JAM y los no documentados; modo decimal y accesos de bus ciclo a ciclo. SingleStepTests: 1.000 casos por opcode; Klaus Dormann. Los ciclos robados muestrean interrupciones; el C64 cuenta las esperas de VIC-II también en el reloj del 6510. | Se verifica un modelo NMOS, no todas las revisiones ni el 65C02. ANE/LXA usan una constante fija EEh. El 6510 comparte el motor y tiene pruebas de placa, sin una suite externa independiente de esa variante. |
| Ricoh 2A03 | Reutiliza el 6502 con decimal desactivado. OAM espera una lectura, conserva la última escritura RMW y alinea 513/514 ciclos; DMC roba 3/4 ciclos y comparte GET con OAM. Se muestrean NMI durante DMA y se guarda la página pendiente. | Falta nestest; activación/aborto exactos del DMC, bordes del solapamiento, conflictos de registros internos y variantes Famicom/PAL pendientes. Las pruebas propias no certifican esas fases completas. [Detalle](NES-DMA.md). |
| Intel 8088 | Instrucciones documentadas sin coprocesador, segmentos, interrupciones y reloj optativo con esperas READY. Contraste funcional: 2.559.500 casos seleccionados, con 447.500 excluidos por metadatos. Cadenas, indirectos y marcos de INT/división coinciden en los intervalos contrastados. | Los flags indefinidos apilados difieren. No se certifican reloj absoluto, prelectura completa, sincronización eléctrica de READY/INTA ni señales físicas; multiplicación/división conservan totales conservadores. [Detalle](I8088.md). |
| Intel 8086 | Comparte el motor del 8088; bus de palabra según alineación, cola de seis bytes y estados separados. Corpus independiente P80C86A-2: 552.744 casos funcionales correctos, 93.256 excluidos; tipos/direcciones/orden e intervalos de 1.475.052 pares coinciden. | Conserva 4.787 casos con valores distintos solo en flags indefinidos. BIU y correcciones de fase de indirectos aproximadas; sin reloj absoluto, prelectura final ni placa 8086 certificados. Una suite finita no valida todas las revisiones. |

Los conteos de casos de las suites se distinguen de las pruebas NUnit: una sola prueba
del 6502 recorre cientos de miles de casos, y una del 8080 ejecuta miles de millones
de instrucciones. No se convierten esos conteos en un porcentaje de fidelidad.

## Qué se revisó en esta pasada

Se contrastaron la documentación, los buses y los ejecutores de pruebas de los cuatro
motores. El Z80 ejecuta el opcode proporcionado por el periférico en IM 0, sin apilar
un retorno salvo que lo requiera la instrucción. El 8080 ofrece ahora reloj de bus
optativo y esperas READY; el 6502 registra cada acceso y las variantes6510/2A03 se conectan en las
placas C64/NES. La variante 8086 comparte su ejecución con el 8088.

La mejora local del 8088 ubica seis T entre las palabras de LES/LDS y siete T entre
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
```

La primera orden de pruebas ya incluye los cuatro proyectos; las demás permiten
revisarlos por separado. El filtro `TestCategory!=Long` omite ZEXDOC/ZEXALL y 8080EXM,
por lo que no sustituye la batería completa. Las pruebas omitidas por falta de datos
se informan como tales. No se descargan suites automáticamente.

Las fuentes de estas afirmaciones están en
[FUSE](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/tests/Retro.Cpu.Z80.Tests/FuseTests.cs),
[ZEX](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/tests/Retro.Cpu.Z80.Tests/ZexTests.cs),
[diagnósticos del 8080](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/tests/I8080.Tests/CpmConformanceTests.cs),
[conformidad del 6502](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/tests/M6502.Tests/ConformanceTests.cs),
[bus e interrupciones del Z80](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/src/Retro.Cpu.Z80/Z80Cpu.cs),
[núcleo y bus del 8080](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/src/Retro.Cpu.I8080/I8080Cpu.cs),
[núcleo del 6502](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/src/Retro.Cpu.M6502/M6502.cs),
[placa C64](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/src/Retro.Systems.C64/C64.cs),
[placa NES](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/src/Retro.Systems.Nes/Nes.cs) y
[pruebas de punteros/pila](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/tests/I8088.Tests/PointerStackTimingTests.cs) y
[pruebas de marcos y control](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/tests/I8088.Tests/StackControlTimingTests.cs).
