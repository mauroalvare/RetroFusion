# Modificar y analizar una partida

Abrí un original y usá **Herramientas > Modificar y analizar** o **Ctrl+M**.

En PC/XT todavía no está disponible el análisis ni la edición de variables de este panel.
Podés usar las herramientas de código, parches y depuración; los atajos de herramientas
son **Ctrl+Shift+letra**.

- **Cambios:** editá valores con Enter, congelalos o aplicá una propuesta.
- **Analizar con IA:** elegí proveedor y modelo, escribí tu pregunta y pulsá **Analizar la partida**.
- **Buscar variables:** compará la memoria mientras jugás y filtrá los resultados.
- **Código:** revisá rutinas, explicaciones, parches y controles del perfil.

## Investigar con IA

Usá **Observar desde ahora**, jugá la acción que querés investigar y pedí el análisis.
El contador indica cuántos cuadros y bytes se observaron. Reiniciar la observación
conserva la partida, las variables y los congelados.
Podés preguntar por una regla o pedir un efecto. El análisis recibe valores y un recorte
del código de la partida, junto con el rango y los últimos valores escritos. No necesita
un perfil preparado ni confirma por sí solo qué significa cada dato.

Los hallazgos aparecen en **Cambios**, con una explicación y propuestas para probar.
Las variables nuevas se indican como hipótesis: su significado y efecto requieren
comprobarse jugando. Recibir el análisis no modifica la partida ni el archivo original.

El selector admite las CLI conectadas, Ollama y las APIs configuradas.
**Modelo y conexión** reúne sus ajustes. También podés copiar el pedido a otro chat
o agente y pegar su respuesta. **Ver actividad** muestra la salida disponible y permite cancelar;
**Copiar salida del CLI** copia ese texto.

Cada informe indica quién lo produjo y el cuadro analizado. Si otro intento falla,
el último válido queda marcado como **Informe anterior**. **Pedido, respuesta y errores**
permite copiar lo enviado, la respuesta y el error para revisarlos sin hacer otro pedido.

Al aplicar una propuesta, el resultado aparece junto al botón con el valor anterior,
el pedido y el valor actual. Si se rechaza, el motivo aparece ahí mismo. Congelar mantiene
el valor aunque el juego intente cambiarlo. Si la partida está pausada, **F9** la continúa.
Que la escritura se aplique no confirma el efecto supuesto: comprobalo jugando.

## Volver atrás

**Deshacer último cambio** recupera el estado anterior al último cambio; guarda hasta cinco.
**Restaurar partida** vuelve al momento anterior al primero. Ambas opciones recuperan
también el progreso de juego desde ese punto. **Liberar valores congelados** permite
que vuelvan a cambiar, sin restaurar la partida.

## Conservar lo encontrado

Activá **Conservar análisis y cambios de este juego** para recuperar las variables,
las propuestas y los últimos doce informes al volver a jugar. Cada informe conserva su
proveedor, fecha y cuadro; elegirlo no aplica cambios. Desmarcar borra el historial guardado
de ese juego y deja los hallazgos actuales disponibles durante esta sesión.

**Reaplicar cambios al reiniciar y volver a jugar** vuelve a escribir los valores elegidos
y mantiene los congelados. Es opcional y puede afectar el arranque. Desmarcarla libera
los congelados actuales sin borrar los informes ni la selección recordada.
**Cambios recordados** permite revisar esa selección y aplicarla manualmente.
Los cambios recuperados se pueden deshacer juntos; los puntos de regreso completos
pertenecen a la partida actual y no se guardan entre sesiones.

Los datos se vinculan al contenido del juego, el sistema y el firmware: renombrar o mover
el archivo no los pierde; otra edición tiene su propio historial. Se guardan en
`%APPDATA%/RetroEmu/modificaciones`, fuera de los archivos del programa, y se conservan
al actualizar. Un archivo ilegible se conserva sin aplicar cambios.
Las hipótesis siguen siendo experimentales: guardarlas no las convierte en datos
verificados para fusiones. Sin activar el guardado, duran la sesión actual.

## Fusiones y juegos creados con originales

Sus reglas se editan como una versión nueva desde el estudio. El guardado de regreso
completo de esta herramienta se ofrece para originales; **Código** permite revisar
el original enfocado como herramienta técnica.
