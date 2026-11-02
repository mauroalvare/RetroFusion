# Identificación de juegos y estilos de interfaz

## Una biblioteca, varios estilos

**Opciones > Estilos de interfaz** muestra una vista previa y explica cada estilo.
También está en Preferencias y en Buscar acción.

| Estilo | Cómo se usa |
| --- | --- |
| Retro | Diseño principal y predeterminado: banco de electrónica, instrumentos y tonos cálidos. Menús, botones, teclado y mando. |
| Explorador | Navegación lateral, árbol de sistemas y listado compacto de archivos, con acciones sobre la selección. Se adapta a un selector de sección cuando falta ancho. |
| Terminal real | Consola inventada dentro de RetroEmu: órdenes, secciones virtuales, historial y autocompletado. No abre una terminal del sistema. |
| Salón | Grilla de tarjetas con imágenes, acciones grandes y navegación al pie. En una ventana pequeña las tarjetas son horizontales y conservan Jugar/Ficha. |

Cambiar de estilo conserva la biblioteca, favoritos, fichas, proyectos y ajustes. La
configuración se guarda en `preferencias.json`. Los archivos de preferencias anteriores
siguen usando Retro; un identificador de estilo desconocido también vuelve a Retro.
El diseño y el color se eligen por separado. Cada interfaz guarda su propia paleta:
base, verde, ámbar, hielo, papel claro o personalizada. **Preferencias > Interfaz**,
`paleta editar` y `paleta usar amber` cambian los colores de la interfaz actual,
incluidos menús, botones y consola. Ocho RGB libres y un borrador con Aplicar/Cancelar.
Cambiar de diseño recupera sus colores; alto contraste los reemplaza temporalmente sin
borrarlos. Los colores no cambian la imagen del juego ni reorganizan la biblioteca.
Al leer una antigua paleta global se copia a cada diseño, que después es independiente.

Los identificadores actuales son retro, explorer, lounge y real. Para archivos e
inicios antiguos siguen aceptándose terminal (Explorador), amber/ámbar (Salón) y
hardcore (Terminal real). Ámbar también es el nombre de una paleta, aplicable a todos.

Las preferencias gráficas, el panel Sonido y la orden `opcion` leen el mismo catálogo
`AppOption`: definición, valores permitidos, lectura y aplicación. El buscador de
acciones y `acciones`/`hacer` usan `AppCommand`, incluidos requisitos y disponibilidad.
Agregar una opción o acción a esos catálogos la incorpora a todos sus accesos. Cada
ajuste declara su valor inicial, sus valores permitidos y su alcance, de donde salen el
botón **Volver al valor inicial** y `opcion <id> restablecer`. No hay configuraciones
separadas para juegos, sonido o controles. Las paletas sí pertenecen a cada diseño.
Los filtros y las acciones de juego de la consola
provienen del mismo catálogo que dibuja la biblioteca.

## Después de actualizar

La elección se ofrece después de cada actualización confirmada, al dibujar el primer
cuadro y validar el recibo del actualizador. También se ofrece en el primer inicio y
cuando cambia la versión de una actualización manual. Un reinicio habitual de la misma
versión no repite la pregunta. Un recibo ajeno o una recuperación fallida no la habilitan.

Se conserva el estilo actual seleccionado; mirar otra vista previa no lo aplica.
**Usar este estilo** lo cambia; **Conservar el actual** mantiene la elección anterior.
La versión del último inicio se guarda al responder. Después queda abierta **Novedades**,
que también se abre desde Ayuda o con la orden novedades (news en inglés).
Elegir un estilo no demora la confirmación ni la limpieza de la actualización.

El nombre visible es **Terminal real**, con identificador real. Se sigue aceptando
hardcore al leer preferencias anteriores y como alias de inicio, para conservar la elección.

## Idioma

Preferencias permite elegir Español o English. Los menús, mensajes, ayuda, órdenes y
autocompletado siguen ese idioma; los scripts en castellano siguen siendo aceptados.
Los títulos personales, rutas, código y campos de los archivos guardados conservan su contenido.
Se incluyen las guías de diseño, ensamblador y API en inglés y una
[guía de usuario en inglés](USER-GUIDE-EN.md). La documentación técnica histórica permanece
en castellano. --language en permite seleccionar inglés al arrancar.

Las vistas previas muestran la distribución de cada diseño con datos inventados, usando
sus colores guardados, fuentes, ilustraciones y tablas. El editor de colores previsualiza
el diseño actual con el borrador. **Ampliar** lleva la vista a toda la ventana. Mirar no
aplica nada y no toca el programa en ejecución. En ventanas chicas se compactan los rótulos
a **Usar estilo** y **Conservar**; el selector y el pie permanecen visibles.
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

### Candidatos para revisar

Cuando no hay una coincidencia automática segura, la ficha ofrece **Buscar candidatos en el
catálogo** y la consola `candidatos <juego>`. Hace falta tener activada la consulta en línea
y se pide juego por juego. Cada candidato dice por qué se propone: título exacto y misma
plataforma, título exacto en otra plataforma, o misma plataforma con título parecido.
**Es este** lo acepta y **No es** lo rechaza. Ninguno se aplica sin tu elección, y las
decisiones se conservan por identidad del juego: una actualización del catálogo no deshace
una aceptación ni repite una coincidencia rechazada. Aceptar un candidato identifica la
obra para vos; no verifica la edición por hash.

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

## Usar la terminal real

La [guía de la consola](CONSOLA.md) explica las órdenes, las teclas, las tareas en segundo
plano y qué recorridos se completan por órdenes. Empezá con `ayuda` o `como <tema>`.
**Ctrl+Shift+T** alterna entre la partida y la consola; `continuar` devuelve el teclado al
juego. Las órdenes nunca se delegan a PowerShell, cmd, bash ni otro intérprete del equipo.
