# Modificar y analizar una partida

Abrí un original y usá **Herramientas > Modificar y analizar** o **Ctrl+M**.

- **Cambios:** editá valores con Enter, congelalos o aplicá una propuesta.
- **Analizar con IA:** elegí proveedor y modelo, escribí tu pregunta y pulsá **Analizar la partida**.
- **Buscar variables:** compará la memoria mientras jugás y filtrá los resultados.
- **Código:** revisá rutinas, explicaciones, parches y controles del perfil.

## Investigar con IA

Jugá unos segundos con la ventana abierta para reunir escrituras de memoria.
Podés preguntar por una regla o pedir un efecto. El análisis recibe valores y un recorte
del código de la partida; no necesita un perfil preparado.

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

## Volver atrás

**Deshacer último cambio** recupera el estado anterior al último cambio; guarda hasta cinco.
**Restaurar partida** vuelve al momento anterior al primero. Ambas opciones recuperan
también el progreso de juego desde ese punto. **Liberar valores congelados** permite
que vuelvan a cambiar, sin restaurar la partida.

El historial y las hipótesis duran esta sesión del original. Abrir, recargar o reiniciar
el juego, o cargar un estado externo, los descarta. Las hipótesis de IA no se guardan
como datos verificados para fusiones.

## Fusiones y juegos creados con originales

Sus reglas se editan como una versión nueva desde el estudio. El guardado de regreso
completo de esta herramienta se ofrece para originales; **Código** permite revisar
el original enfocado como herramienta técnica.
