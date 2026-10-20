# Probar cambios mientras jugás

Abrí un juego original y entrá en **Modificar > Variables y cambios divertidos** o
apretá **Ctrl+M**. El panel queda junto a la partida, para ver qué ocurre al cambiarla.

## Cambiar y volver atrás

- Las variables conocidas muestran su valor y qué significan. Escribí otro número y
  apretá **Enter** para aplicarlo. Mientras escribís, el juego conserva el valor anterior.
- **Congelar** repone ese valor antes de cada cuadro emulado, también con turbo.
  **Soltar todos los congelados** permite que el juego siga cambiándolos.
- El primer cambio exitoso guarda un punto de regreso. **Volver antes de probar**
  restaura CPU, memoria y pantalla a ese momento y suelta los congelados. También
  retrocede el progreso hecho desde entonces.
- Los cambios se hacen en la partida en memoria; tu archivo de juego se conserva.
  Abrir otro juego, recargar, reiniciar o cargar un estado descarta el experimento y
  las propuestas anteriores. El punto de regreso es temporal: no es un archivo guardado.

Por ejemplo, Manic Miner tiene aire y vidas de reserva conocidos. Podés aumentar las
reservas o mantener el aire, o probar poco aire como desafío. Vidas de reserva extra no
equivalen a invulnerabilidad. Las coordenadas marcadas como **solo lectura** se pueden
observar; el editor conserva esa restricción.

## Pedir propuestas a la IA

Elegí tu IA en el mismo panel: incluye Claude Code, Codex CLI y Antigravity CLI, además
de los otros proveedores. En **Cambiar modelo o conexión** están los ajustes de las
herramientas que ya se encontraron.

Escribí qué te gustaría explorar y pulsá **Pedir ideas para modificar**. La IA recibe el
nombre del juego, las variables conocidas, sus valores actuales, rangos y descripciones.
Devuelve una explicación y propuestas con sus efectos esperados. Las propuestas quedan
para revisar: cada una tiene un botón **Probar estos cambios**.

**Ver actividad de la IA** muestra el tiempo transcurrido y la salida que emita el CLI;
podés cancelar el análisis. Encontrar la herramienta instalada no comprueba su sesión.
Pedir ideas usa una llamada a la IA elegida; abrir el panel no hace ese pedido.
También podés copiar la consigna a un chat o agente y pegar su respuesta para revisarla.

El programa valida nombres, rangos y permisos incluso si la respuesta no respeta el
formato pedido. Un efecto sugerido sigue siendo una hipótesis para comprobar jugando.
No se ejecutan comandos ni código recibidos de la IA.

## Cuando faltan variables

El análisis de propuestas parte del perfil de esa edición; no descubre por sí solo toda
la memoria del juego. **Buscar otras variables** abre el buscador: empezá una búsqueda,
hacé algo en el juego y filtrá por **Cambió**, **Disminuyó**, **Aumentó** o un valor concreto.
Al reducir las direcciones podés nombrar y guardar una variable en tu perfil.
**Preparar un juego en el estudio** abre las herramientas de preparación automática:
buscá tu juego y pulsá Preparar. Al terminar, volvé con Ctrl+M; el panel busca el perfil
nuevo de esa copia sin reiniciar tu partida. La preparación usa la IA elegida si está
configurada, y se inicia cuando la pedís.

**Ver rutinas y análisis del código** abre las herramientas anteriores: desensamblado,
nombres, comentarios y explicación con IA de una rutina. Esa explicación también tiene
su panel de actividad y se descarta si cambiaste de partida mientras la IA respondía.

En una fusión, los experimentos con punto de regreso completo se reservan a los
originales. Para cambiar las reglas del juego fusionado, pedile otra versión al estudio.
La vista avanzada permite inspeccionar y editar variables del original enfocado, como antes;
eso no guarda ni restaura el estado completo del guion de fusión.
