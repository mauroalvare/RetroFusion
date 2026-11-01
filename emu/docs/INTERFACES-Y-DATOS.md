# Identificación de juegos y estilos de interfaz

## Una biblioteca, tres formas de usarla

**Opciones > Estilos de interfaz** muestra una vista previa y explica cada estilo.
También está en Preferencias y en Buscar acción.

| Estilo | Cómo se usa |
| --- | --- |
| Retro | Diseño principal y predeterminado: banco de electrónica, instrumentos y tonos cálidos. Menús, botones, teclado y mando. |
| Terminal verde | La misma interfaz gráfica, negra y verde, con texto de ancho fijo. Se siguen usando menús y botones. |
| Terminal hardcore | Consola inventada dentro de RetroEmu: órdenes, secciones virtuales, historial y autocompletado. No abre una terminal del sistema. |

Cambiar de estilo conserva la biblioteca, favoritos, fichas, proyectos y ajustes. La
configuración se guarda en `preferencias.json`. Los archivos de preferencias anteriores
siguen usando Retro; un identificador de estilo desconocido también vuelve a Retro.
Alto contraste puede reemplazar el verde por la paleta accesible.

Las preferencias gráficas, el panel Sonido y la orden `opcion` leen el mismo catálogo
`AppOption`: definición, valores permitidos, lectura y aplicación. El buscador de
acciones y `acciones`/`hacer` usan `AppCommand`, incluidos requisitos y disponibilidad.
Agregar una opción o acción a esos catálogos la incorpora a ambos accesos. No hay
configuraciones separadas por tema. Los filtros y las acciones de juego de la consola
provienen del mismo catálogo que dibuja la biblioteca.

## Después de actualizar

La elección se ofrece después de que el programa actualizado dibuja su primer cuadro
y confirma un recibo válido del actualizador: carpeta, versión y estado en aplicación.
Un inicio normal, un recibo ajeno o una recuperación fallida no activan esa oferta.
Se conserva el estilo actual seleccionado; mirar otra vista previa no lo aplica.
**Usar este estilo** lo cambia; **Conservar el actual** mantiene la elección anterior.
La versión respondida se recuerda para no repetir la pregunta por esa entrega.
Elegir un estilo no demora la confirmación ni la limpieza de la actualización.

En ventanas chicas se compactan los rótulos a **Usar estilo** y **Conservar**. El selector
y el pie permanecen visibles; la vista previa y la explicación permiten desplazamiento.
La partida queda en pausa mientras se elige y recupera su estado previo al conservar
un estilo gráfico. La consola mantiene la pausa hasta `continuar`.

## Datos que se detectan

La identificación no modifica el medio ni certifica su jugabilidad. El sistema y la
identidad por contenido siguen viniendo del detector de medios. Sobre esa identidad
se reúnen datos de catálogo:

1. **Ficha personal:** tus géneros y año completos tienen prioridad, campo por campo.
2. **Colección local:** se leen los campos nombre, género, fecha y editora de un
   `gamelist.xml` existente junto al juego o en la raíz. Se asocian por ruta dentro de
   la biblioteca; las referencias externas y entidades XML no se admiten. Se retienen
   por identidad si el organizador mueve un archivo suelto a su carpeta de sistema.
3. **Catálogo público opcional:** activá **Preferencias > Completar fichas en línea**,
   o `opcion consulta-fichas on`. Se consulta Wikidata en segundo plano; requiere
   título o alias exacto y plataforma compatible. Dos coincidencias quedan sin asignar.
4. **Nombre del archivo:** se limpian marcas usuales de edición y se pueden leer años
   explícitos entre paréntesis o corchetes. Se muestran como indicios, con la edición
   sin verificar. Nunca se deduce un género por palabras del título ni se usa la fecha
   del archivo como lanzamiento.

**Ficha** detalla el origen y método, la editora cuando se conoce y el primer año de
la obra cuando la fuente no permite atribuirlo a la plataforma actual. Ese primer año
general aparece marcado **(obra)** en la lista y puede usarse como referencia para
filtrar; la ficha aclara que el año de esa plataforma sigue sin determinar. Las etiquetas de
género habituales se unifican con las categorías de los filtros; se conservan otras
etiquetas cortas de la fuente.

Un campo personal vacío permite usar el detectado. Para rechazar una coincidencia,
marcá **Omitir identificación automática de este juego**. No borra tu ficha personal.
Sin evidencia suficiente se conserva **Sin clasificar** o **Sin año**.

La consulta en línea está desactivada inicialmente. Solo envía el título limpio y usa
la plataforma para contrastar la respuesta; nunca sube ROMs, hashes ni rutas. No usa
IA, claves ni un servicio pago. Se hacen consultas seriales, con plazo de ocho segundos
por juego y separación entre juegos. Un fallo de conexión detiene la cola y ofrece
**Reintentar catálogo en línea**; desactivar la opción cancela la consulta pendiente.
La biblioteca permanece utilizable durante todo el proceso.

`identificaciones.json` es una caché independiente de `catalogo.json`. Conserva datos
y decisiones de omisión por identidad; las coincidencias se revisan tras 90 días y las
ausencias tras siete. Se limita a 8 MB y 16384 identidades. Un archivo ilegible se
preserva y los datos nuevos quedan en la sesión. Los datos estructurados de Wikidata
son [CC0](https://www.wikidata.org/wiki/Wikidata:Data_access); se usa su
[API oficial](https://www.mediawiki.org/wiki/Wikibase/API) y las propiedades de
[género, plataforma, publicación y editora](https://www.wikidata.org/wiki/Wikidata:WikiProject_Video_games/Properties).
Su cobertura varía, especialmente en homebrew, reediciones y colecciones con nombres
abreviados. Reconocer un título por nombre y sistema no verifica una edición por hash.

## Usar la terminal hardcore

Escribí `ayuda` o `ayuda opcion`. Enter ejecuta, ↑/↓ recorren hasta cien órdenes y Tab
completa órdenes e identificadores de opciones/acciones. El registro conserva hasta
600 líneas; `limpiar` lo vacía y `copiar` lo lleva al portapapeles.

```text
juegos
filtrar genero Plataformas
filtrar año 1980:1989
juegos
ficha 1
favorito 1 on
jugar 1
```

**Ctrl+Shift+T** vuelve de una partida a la consola y la pausa. Escribir órdenes no
envía teclas al juego. `continuar` le devuelve el teclado. Los números corresponden
a la última lista; se vuelven a resolver por identidad para no abrir otra fila tras
un cambio de la biblioteca. También se puede usar una clave inequívoca de `ficha`.

| Órdenes | Función |
| --- | --- |
| `cd favoritos`, `cd recientes`, `cd originales`, `cd creados`, `cd fusiones`, `ls` | Navegar secciones virtuales; no carpetas del sistema. |
| `buscar texto`, `filtrar`, `filtrar quitar` | Búsqueda y filtros compartidos por sistema, tipo, género, año, actividad, orden y disponibilidad. |
| `abrir "C:\Juegos\archivo.nes"` | Abrir un medio admitido; las rutas con espacios van entre comillas. |
| `opciones`, `opcion texto 150`, `opcion volumen 60` | Leer y cambiar las mismas preferencias del menú. Valores inválidos se rechazan. |
| `interfaz retro`, `interfaz terminal`, `interfaz hardcore`, `interfaz preview` | Cambiar de estilo o abrir las vistas previas. Siempre se puede salir de la consola. |
| `acciones`, `hacer id` | Descubrir y abrir las funciones del programa con sus requisitos actuales. |
| `guardar 1`, `cargar 1`, `ranuras` | Partidas del original y edición actuales. Una ranura ocupada exige `guardar 1 reemplazar`. |
| `estado`, `pausa`, `reiniciar`, `captura` | Control de la partida. |
| `registros`, `memoria 8000 32`, `codigo 8000`, `paso cuadro` | Inspección de la máquina; las direcciones se escriben en hexadecimal. |
| `maquinas`, `maquina id` | Consultar los sistemas y encender uno implementado con firmware local. |
| `actualizar`, `salir` | Actualizador y cierre con protección de trabajos pendientes. |

Los editores de fusión, creación, código y conexión de IA conservan sus instrumentos
visuales: se abren por `hacer` y la consola queda abajo. La navegación principal del
modo hardcore se hace con órdenes; no reimplementa esos editores como lenguajes de
texto. Abrirlos no inicia por sí solo una consulta a la IA. Las órdenes nunca se
delegan a PowerShell, cmd, bash ni otro intérprete del equipo.
