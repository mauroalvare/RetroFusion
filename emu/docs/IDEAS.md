# Líneas de mejora

Este documento reúne posibilidades de desarrollo. No es una lista de funciones
prometidas ni fija el orden de implementación. El alcance disponible está en
[LIMITACIONES.md](LIMITACIONES.md).

## Base disponible

- El estudio fusiona dos o tres originales: conserva una base y agrega comportamientos
  de los demás en su mundo. Crear con ROMs es un recorrido aparte para una escena nueva.
  Las vistas separadas y las recetas clásicas ya se retiraron.
- El creador desde cero ensambla programas Z80 o 6502 para Spectrum 48K y C64, los
  ejecuta y admite revisiones guiadas por pruebas.
- El análisis de partidas propone variables y cambios; el usuario decide qué aplicar,
  puede congelarlos, deshacerlos y conservar análisis y ajustes por edición del juego.
  El análisis de variables de PC/XT todavía no está disponible.
- Hay guardados, depurador, código y memoria. Las herramientas de IA se conectan por
  CLI, API, chat manual o carpeta de agentes, según la operación.

## Próximas evaluaciones

1. **Calidad de las fusiones.** Comparar varias propuestas sobre los mismos originales
   y ambas orientaciones de base. Medir consecuencias mecánicas, identidad conservada,
   controles y diferencias de diseño. Las pruebas automáticas no deciden si un juego
   resulta divertido; hace falta jugarlo y revisar casos distintos.
2. **Perfiles con evidencia.** Ampliar la detección de mapas, actores y reglas y verificar
   cada dato contra la edición cargada. El análisis actual no convierte cualquier ROM
   en una fuente lista para fusionar.
3. **Referencias de código para crear.** Extraer rutinas acotadas y explicar sus técnicas
   sin copiar bytes, listados ni recursos protegidos al juego nuevo. El ensamblador y el
   bucle de creación ya existen; falta completar la extracción y el chequeo anti-copia.
4. **Comprobar cambios de variables.** Comparar una partida antes y después de un cambio
   con entradas repetibles. La escritura y el congelado ya funcionan; aún no prueban por
   sí solos el efecto jugable que atribuye la IA.
5. **Rebobinado.** Evaluar un historial acotado de estados para las máquinas que permiten
   guardarlos. Requiere medir memoria, rendimiento y sincronización de audio y entrada.
6. **Edición visual de recursos.** Ofrecer herramientas para inspeccionar mapas, sprites
   y paletas y colocar aportes dentro de una base. Las capturas pequeñas del estudio
   ya existen; no equivalen a conocer la estructura de mapas o colisiones.
7. **Compartir trabajos.** Exportar guiones, metadatos y revisiones con sus requisitos y
   huellas de originales, sin incorporar ROMs. Una galería o intercambio por red
   necesitaría resolver validación, versiones y permisos de los recursos.

## Exploraciones posteriores

- Objetivos y logros observables, basados en variables verificadas de cada edición.
- Traducción de textos y fuentes respetando codificación, espacio y límites del original.
- Navegación de rutinas y visualización de accesos a memoria sobre las observaciones
  que ya recoge el analizador.
- Reglas adaptativas durante la partida, con límites explícitos y control del usuario.
- Colaboración en red, con sincronización y costes medidos antes de ofrecerla.

Cada mejora necesita una implementación accesible y pruebas proporcionales antes de
presentarse como disponible.
