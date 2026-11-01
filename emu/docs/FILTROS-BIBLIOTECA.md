# Filtrar y ordenar la biblioteca

Entrá a **Biblioteca**, elegí **Todos los juegos**, una máquina, Favoritos, Recientes,
Fusionados o Creados, y abrí **Filtros**. Los filtros se combinan con la búsqueda:

- **Sistema:** filtra originales y creaciones; una fusión puede coincidir con cualquiera
  de sus sistemas. En la vista de una máquina se conserva el criterio de esa sección.
- **Tipo:** originales, juegos desde cero, fusiones o juegos con originales.
- **Género:** elegí uno o **Sin clasificar**. Un juego puede tener varios géneros.
- **Año de lanzamiento:** cualquiera, **Sin año** o un intervalo con ambos extremos incluidos.
- **Disponibilidad:** todos, los que se pueden abrir o los que requieren revisión.
  Que el formato esté admitido no certifica toda la jugabilidad.
- **Actividad:** sin jugar, jugados en los últimos 7, 30 o 90 días, o agregados en los
  últimos 7 o 30 días. Se usa la actividad registrada, no las fechas del archivo.

La actividad usa el historial conservado por la biblioteca. **Sin jugar** significa
que no queda una partida registrada para esa identidad, no que nunca se haya jugado:
el registro mantiene los últimos 40 recientes y los datos asociados a favoritos.

La búsqueda acepta palabras del nombre y de los géneros, sin distinguir mayúsculas ni
acentos. El contador indica cuántos juegos coinciden dentro de la sección actual.
**Quitar filtros** recupera la lista completa. Elegir otra sección reinicia sus filtros;
volver a abrir la biblioteca conserva la selección de esa sesión.

El selector de orden permite nombre A–Z o Z–A, año de lanzamiento reciente o antiguo,
última partida y agregados recientemente. Los años y fechas desconocidos quedan al final.
Recientes empieza ordenado por última partida; los filtros de la biblioteca no recortan
el menú de juegos recientes. Los filtros son de la sesión y no cambian archivos ni juegos.

## Completar una ficha

Junto a **Jugar**, **Ficha** abre los datos personales del juego:

1. Escribí géneros separados por coma o elegilos de la lista. Se admiten hasta ocho.
2. Si conocés el año de lanzamiento, marcá la opción y completalo. Desmarcarla deja
   el año desconocido; no se inventa uno a partir del archivo.
3. Pulsá **Guardar ficha**. **Cancelar** descarta la edición.

Los géneros sugeridos son opciones de clasificación, no datos atribuidos automáticamente
a los juegos. Un género o año sin completar aparece como **Sin clasificar** o **Sin año**.
La fecha de incorporación indica cuándo este catálogo registró la identidad por primera
vez; los juegos ya existentes se registran al comenzar a usar esta función. No se presenta
como fecha de lanzamiento, creación, descarga ni copia del archivo.

Las fichas se guardan en `%APPDATA%/RetroEmu/catalogo.json`, aparte de favoritos y recientes.
Usan las identidades del contenido: renombrar o mover un original no pierde su ficha;
otra edición con contenido distinto conserva datos independientes. Las creaciones usan
su identidad de trabajo o de creación y las fusiones comparten ficha entre sus versiones.
Dos creaciones independientes con identificador de trabajo vacío no se agrupan por eso.

Si falla el guardado, el aviso indica que los cambios quedan en la sesión. Podés volver
a guardar la misma ficha cuando el archivo esté disponible. Si el archivo no puede leerse,
se conserva y **Recuperar fichas** guarda primero un respaldo único antes de publicar los
valores actuales. Esa recuperación no reconstruye datos ilegibles.

## Texto grande y ventanas pequeñas

Con poca altura, el botón **Filtros** queda junto a la búsqueda. El orden y las acciones
Abrir archivo y Actualizar biblioteca están dentro de ese panel. **Ver resultados** cierra
el panel y recupera espacio para la lista. Abrir también sigue disponible desde Juego.
Los paneles permiten desplazamiento; el listado reserva espacio suficiente para alcanzar
los botones Jugar y Ficha. La edición tiene sus propios botones Guardar y Cancelar.
