# Novedades de 0.3.6

- **Interfaz retro renovada.** Nueva tipografía, navegación común entre biblioteca,
  partida y talleres, buscador de acciones y paneles que se adaptan al espacio. Se
  conservan escala del texto, alto contraste y controles de teclado y mando.
- **Investigación recuperable.** Guarda avances, experimentos y presupuestos por
  original. Permite pausar y continuar sin alterar la partida ni las ROMs; distingue
  escrituras con efecto, hipótesis y significado comprobado de una variable.
- **Creación con pruebas dirigidas.** Además de los robots generales, puede repetir
  entradas y observar cambios de memoria del original. Los errores vuelven al mismo
  borrador; corregir una comprobación no obliga a reescribir un guion que funciona.
- **Partidas y herramientas.** Reinicio completo de las máquinas de una fusión,
  avisos visibles durante el juego, comprobación de entrada y grabación/reproducción
  de controles para originales. El analizador conserva evidencia y contexto por edición.
- **CPU y hardware.** Correcciones de interrupciones, esperas, estado y DMA en los
  núcleos y sus máquinas, con contraste funcional y de bus ampliado. El lector DMC
  y la carga de estados NES conservan correctamente su audio y descartan restos al reiniciar.
- **Preparación de originales.** Repite arranques acotados y reconoce más ejes de
  teclado. No comprobar movimiento ya no se presenta como prueba de que el juego
  sigue en el título, y detectar un eje no inventa un botón de disparo.

Los estados NES anteriores siguen leyéndose. Los nuevos guardan también la historia
del mezclador y requieren esta versión. Los paquetes incluyen los manuales actualizados;
las referencias al código se vinculan a la misma versión publicada.

La revisión funcional aprobó **3298 pruebas**, más **dos de captura nativa**, y recorridos
de **30 minutos simulados** entre dos combinaciones, con 273 reinicios. También se
comprueban transiciones, finales y actualización aislada conservando datos.

Esto no certifica todas las variantes físicas de CPU, todos los niveles de cualquier
original ni diversión. La interfaz de Linux y los controles físicos necesitan validación
directa en esos entornos. [Límites actuales](LIMITACIONES.md) ·
[Precisión de CPU](PRECISION-CPU.md) · [Preparar una fusión](PREPARAR-FUSION.md).
