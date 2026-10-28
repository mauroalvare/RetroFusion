# Alcance y verificación de los procesadores

**No está acreditado un 100 % de fidelidad de todos los procesadores.** Hay cuatro
motores de instrucciones y variantes que los reutilizan. Pasar una batería comprueba
los casos que contiene; no demuestra todas las combinaciones de estados, interrupciones,
señales y revisiones físicas. Tampoco equivale a completar la máquina que rodea a la CPU.

## Qué está implementado y cómo se comprueba

| Procesador | Implementación y evidencia | Límite que impide afirmar un 100 % general |
|---|---|---|
| Z80 | Instrucciones base y prefijadas, flags no documentados, MEMPTR y Q. FUSE: 1.356 casos con registros, memoria y tiempos de accesos; ZEXDOC y ZEXALL. | IM 0 se resuelve como RST 38h, el caso del Spectrum con FFh en el bus; no ejecuta una instrucción arbitraria suministrada por el periférico. El bus permite contención y demoras de E/S, sin un modelo general de todas las señales y revisiones del chip. |
| Intel 8080 | Los 256 opcodes, incluidos los alias. TST8080, 8080PRE, CPUTEST y 8080EXM comprueban resultados y totales exactos de instrucciones/ciclos. | El núcleo suma el tiempo al terminar cada instrucción. Su bus no expone el reloj de cada acceso ni READY/HOLD/INTA; los totales correctos no validan esas fases. |
| MOS 6502 NMOS / 6510 | Los 256 opcodes, incluidos JAM y los no documentados; modo decimal y accesos de bus ciclo a ciclo. SingleStepTests: 1.000 casos por opcode; prueba funcional de Klaus Dormann. El C64 agrega el puerto del 6510 y la espera por la VIC-II. | Se verifica un modelo NMOS, no todas las revisiones ni el 65C02. ANE/LXA usan una constante fija EEh. El 6510 comparte el motor y tiene pruebas de placa, sin una suite externa independiente de esa variante. |
| Ricoh 2A03 | Reutiliza el 6502 con aritmética decimal desactivada; una prueba propia conserva D y comprueba ADC/SBC binarios. Hay pruebas de integración de la NES. | Falta nestest; el DMC no roba ciclos a la CPU y la DMA de OAM no reproduce su alineación completa. Las pruebas del 6502 no certifican esas diferencias de la NES. |
| Intel 8088 | Instrucciones documentadas sin coprocesador, segmentos, interrupciones y reloj optativo. Contraste funcional: 2.559.500 casos seleccionados, con 447.500 excluidos por metadatos. Se contrastan grupos de accesos e intervalos relativos. | Quedan diferencias de temporización, flags indefinidos, entrada de REP y transferencias de control. No se certifican reloj absoluto, prelectura completa, READY/INTA ni señales eléctricas. [Detalle](I8088.md). |
| Intel 8086 | Comparte el motor del 8088; bus de palabra según alineación, cola de seis bytes, estados separados y pruebas propias de ambas variantes. | Sin contraste externo propio ni placa 8086. Hereda las aproximaciones de la EU; una prueba del 8088 no valida los tiempos del 8086. |

Los conteos de casos de las suites se distinguen de las pruebas NUnit: una sola prueba
del 6502 recorre cientos de miles de casos, y una del 8080 ejecuta miles de millones
de instrucciones. No se convierten esos conteos en un porcentaje de fidelidad.

## Qué se revisó en esta pasada

Se contrastaron la documentación, los buses y los ejecutores de pruebas de los cuatro
motores. El Z80 declara tres modos de interrupción, pero el recorrido de aceptación
confirma el alcance limitado de IM 0 descrito arriba. El 8080 cuenta ciclos por
instrucción; el 6502 registra cada acceso, y las variantes 6510/2A03 se conectan en las
placas C64/NES. La variante 8086 comparte su ejecución con el 8088.

La mejora local del 8088 ubica seis T entre las palabras de LES/LDS y siete T entre
la lectura de PUSH de memoria y su escritura en la pila, sin cambiar la tabla nominal.
Sus 30.000 casos externos coinciden en accesos y 84.994 pares de intervalos. POP de
memoria y la entrada de REP siguen pendientes. Esto no completa el 8088 ni certifica
el 8086.

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
[FUSE](../tests/Retro.Cpu.Z80.Tests/FuseTests.cs),
[ZEX](../tests/Retro.Cpu.Z80.Tests/ZexTests.cs),
[diagnósticos del 8080](../tests/I8080.Tests/CpmConformanceTests.cs),
[conformidad del 6502](../tests/M6502.Tests/ConformanceTests.cs),
[bus e interrupciones del Z80](../src/Retro.Cpu.Z80/Z80Cpu.cs),
[núcleo y bus del 8080](../src/Retro.Cpu.I8080/I8080Cpu.cs),
[núcleo del 6502](../src/Retro.Cpu.M6502/M6502.cs),
[placa C64](../src/Retro.Systems.C64/C64.cs),
[placa NES](../src/Retro.Systems.Nes/Nes.cs) y
[pruebas de punteros/pila](../tests/I8088.Tests/PointerStackTimingTests.cs).
