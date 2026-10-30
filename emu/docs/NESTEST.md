# Contraste externo de la CPU NES

`NestestVerifier` compara el estado anterior a cada instrucción en el modo automático
C000 de nestest: PC, A, X, Y, P completo, SP y ciclo absoluto. No enmascara diferencias
de flags. Empieza con el estado documentado del diagnóstico: A/X/Y=0, P=$24, SP=$FD,
PC=$C000 y ciclo 7. Ese punto inicial no verifica el encendido físico de la consola.

```powershell
retro-cli nes verificar --rom nestest.nes --registro nestest.log --salida informe.json
```

El informe incluye las huellas de los mismos bytes que se verificaron. Un registro
vacío, mal formado o con otro punto de inicio se rechaza. La primera diferencia informa
línea, esperado y observado; el proceso devuelve 1. Errores de archivo/formato devuelven
2. No se ejecuta una instrucción posterior al último estado entregado.

El corpus autorizado se obtuvo de
[nes-test-roms, revisión 95d8f62](https://github.com/christopherpow/nes-test-roms/tree/95d8f621ae55cee0d09b91519a8989ae0e64753b/other):
ROM, registro y documentación, 901.533 bytes. Se comprobaron tamaños, Git blob SHA-1 y
SHA-256. El test opcional reconoce esa ROM y ese registro por sus SHA-256; se omite si
no están presentes. No descarga nada. `RETRO_NESTEST` permite indicar su carpeta.
Los datos externos quedan privados en `out/mantenimiento/nestest`; no se incluyen en
el repositorio ni en los paquetes.

El contraste local coincidió en **8.991 estados**, hasta el ciclo **26.554**. Los casos
propios comprueban que cambiar un registro, un flag o el ciclo esperado produce un
fallo real, y que un registro inválido no se interpreta como una aprobación vacía.

Esto amplía la evidencia funcional y de ciclos de la CPU. No certifica todos los
opcodes/operandos posibles, variantes de silicio, pines, PPU, APU, DMA, mappers ni
jugabilidad. Los diagnósticos anteriores y sus límites siguen vigentes.
