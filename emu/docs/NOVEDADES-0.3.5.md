# Novedades de 0.3.5

- **Más precisión del 8088 en PC/XT.** Se distribuye el trabajo interno entre los
  accesos de cadenas, punteros lejanos y pila, conservando la tabla de tiempos nominales.
  Las iteraciones REP ya no vuelven a cobrar prefijos que estaban decodificados.
- **Marcos lejanos corregidos.** POP de memoria, CALL lejano directo, RETF e IRET
  separan sus transferencias como las trazas de hardware disponibles: 50.000 casos y
  170.000 pares de intervalos coinciden. También coinciden los 12.000 casos de
  CMPS/STOS/LODS y los 30.000 de LES/LDS/PUSH contrastados.
- **Alcance de CPU documentado.** La nueva guía explica qué comprueban las suites de
  cada procesador y qué queda pendiente. Los paquetes incluyen esa guía.

Se agregaron 91 pruebas desde 0.3.4. La batería completa tiene 2.857 aprobadas, dos
omitidas y ninguna fallida. Se mantienen las regresiones de movimientos, E/S,
lectura/modificación/escritura y el contraste funcional externo del 8088.

La precisión sigue siendo parcial: quedan la entrada de algunos REP, las transferencias
indirectas y tiempos de interrupciones y errores de división. No se certifican reloj
absoluto, prelectura completa ni señales eléctricas. El 8086 sigue como núcleo y
diagnóstico, sin placa propia ni contraste externo independiente.

[Precisión de CPU](PRECISION-CPU.md) · [Detalle del 8088/8086](I8088.md) ·
[Límites actuales](https://github.com/mauroalvare/RetroFusion/blob/v0.3.5/emu/docs/LIMITACIONES.md)
