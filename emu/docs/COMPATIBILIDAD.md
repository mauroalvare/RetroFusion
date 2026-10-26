# Comprobar compatibilidad

La compatibilidad se comprueba sobre una edición concreta y con el hardware que necesita.
Que dos programas compartan procesador no significa que usen la misma máquina.
[Formatos y sistemas](JUEGOS-PARA-PROBAR.md). [Límites actuales](LIMITACIONES.md).

## Qué demuestra cada nivel

| Nivel | Evidencia necesaria |
|---|---|
| Formato reconocido | La estructura del archivo corresponde al formato. |
| Medio cargado | El emulador lo acepta y lo conecta a la máquina adecuada. |
| Imagen | Produce una pantalla con contenido; por sí sola no demuestra una partida. |
| Título | Llega a la pantalla identificada por el guion de comprobación. |
| Responde | Las entradas producen una diferencia respecto de una ejecución sin ellas. |
| Jugable | Se comprueban controles, progreso y reglas durante una partida. |
| Preparado para fusionar | Tiene perfil y direcciones verificadas para esa edición y su hash. |

Una diferencia de imagen no confirma por sí sola movimiento, colisión ni control.
Los guiones automáticos son pruebas acotadas; no garantizan todo el juego.
Comparar ejecuciones repetidas ayuda a detectar resultados no deterministas.

## Generar un informe local

Desde emu, usá una carpeta de juegos propia y guardá las evidencias en out:

    dotnet run --project src/Retro.Cli -c Release -- compat compat/juegos --juegos "ruta/a/ROMs" --salida out/compat/resultados.json --md out/compat/informe.md --capturas out/compat/capturas --dos-veces

Registrá fecha, commit, máquina, hash de la edición, firmware, pasos, entradas y resultado.
Separá lo observado de lo inferido y de lo que falta probar. Si un guion no corresponde
a la edición disponible, informalo en lugar de atribuirle su resultado.

Los informes por título, capturas y datos de la biblioteca son locales. No incorporarlos
al README, las notas de entrega ni al repositorio público. Las pruebas nuevas pueden
usar programas propios para reproducir errores sin distribuir juegos comerciales.
[Método y herramientas](MAPA.md). [Alcance de la auditoría de fusiones](PRUEBAS-FUSION.md).
