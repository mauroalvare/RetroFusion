# Consola de Terminal real

Terminal real se maneja escribiendo órdenes en una consola interna de RetroEmu. La
consola procesa solo órdenes del programa: nunca entrega texto a PowerShell, cmd, bash ni
a otro intérprete del equipo. Los juegos siguen mostrando su imagen; el resto de la
ventana es la consola.

Para activarla: **Opciones > Estilos de interfaz**, o `--interface real` al iniciar.
Para salir de ella: `interfaz retro`. Todos los estilos comparten biblioteca, favoritos,
fichas, partidas, proyectos y ajustes.

## Primeros pasos

```text
juegos                      lista la sección actual
filtrar genero Plataformas  combina filtros con la biblioteca gráfica
ficha 1                     datos, procedencia y disponibilidad
jugar 1                     abre el juego y le entrega el teclado
```

Durante la partida, **Ctrl+Shift+T** vuelve a la consola y pausa. `continuar` devuelve el
teclado al juego. Si no sabés cómo se llama una orden, `como <tema>` busca por lo que
querés hacer: `como guardar`, `como subir el volumen`. `ayuda <orden>` explica una orden y
`ayuda <grupo>` lista un grupo.

## Colores y paletas

Para progreso de cartuchos con batería, `bateria` consulta el archivo; `bateria guardar`
lo guarda ahora y `bateria abrir` abre el panel de copias. En inglés: `battery`,
`battery save`, `battery open`. [Guía y recuperación](BATERIA-NES.md).

Cada interfaz conserva sus propios colores al cambiar de diseño y reiniciar. En **Preferencias >
Interfaz**, elegí una paleta o abrí **Personalizar colores**. Hay fósforo verde, ámbar,
hielo y papel claro, además de **Seguir el estilo** y una paleta personalizada.

~~~text
paleta lista
paleta usar amber
paleta color background #100C22
paleta color text #F092FF
paleta editar
paleta restablecer
~~~

La orden actúa sobre la interfaz actual, incluidos sus menús y botones. Se pueden elegir libremente fondo, texto, acento/cursor, texto secundario, selección,
resultados correctos, avisos y errores. El editor muestra una vista previa: **Aplicar**
guarda; **Cancelar** conserva la paleta anterior. Sus botones quedan al pie y, con poca
altura, permite elegir un color por vez. Alto contraste reemplaza temporalmente los
colores; al desactivarlo vuelve la elección. `restablecer` vuelve a la base de este diseño sin borrar
los últimos colores personalizados. En inglés: `palette list`, `palette use amber`,
`palette color text #F092FF`, `palette edit` y `palette reset`.

## Edición de la línea

| Teclas | Función |
| --- | --- |
| Enter | Ejecuta la orden. |
| Tab / Shift+Tab | Completa. Si hay varias alternativas, escribe lo que comparten y después las recorre. |
| Esc | Cancela la sugerencia, la selección o la línea, en ese orden. Ctrl+Z la recupera. |
| ↑ / ↓ | Órdenes anteriores. El borrador sin enviar se conserva. |
| Ctrl+R | Busca en las órdenes anteriores. Enter deja la orden para leerla o editarla; no la ejecuta. |
| ← → · Ctrl+← → | Mueve por carácter o por palabra. |
| Inicio / Fin · Ctrl+A / Ctrl+E | Principio y final de la línea. |
| Shift + movimiento · Ctrl+Shift+A | Selecciona texto de la línea. |
| Retroceso / Supr · con Ctrl | Borra un carácter o una palabra. |
| Ctrl+W · Ctrl+U · Ctrl+K | Borra la palabra anterior, hasta el inicio o hasta el final. |
| Ctrl+Z / Ctrl+Y | Deshace y rehace la edición. |
| Ctrl+X / Ctrl+V | Corta y pega. Shift+Supr y Shift+Insert también. |
| Ctrl+C | Copia la selección. Sin selección deja de seguir una tarea o abandona la línea. Nunca cancela una tarea. |
| Clic derecho | Copia la selección; sin selección, pega. |
| Doble y triple clic | Seleccionan una palabra o una línea de la salida. |
| Ctrl+L | Despeja la vista; la salida anterior sigue arriba. `limpiar` la borra. |
| RePág / AvPág · Ctrl+↑ ↓ · Ctrl+Inicio / Fin | Desplazan la salida. Escribir vuelve al prompt. |

Las rutas de Windows no necesitan barras dobles y las comillas son opcionales cuando no
hay espacios: `abrir "C:\Mis juegos\Faro.nes"`, `carpeta "C:\Mis juegos\"`. Un apóstrofo
dentro de una palabra es una letra. Las órdenes ignoran mayúsculas y acentos: `OPCIÓN` y
`opcion` son la misma.

**Pegar nunca ejecuta nada.** Una línea queda en el editor para revisarla. Varias líneas se
muestran debajo del prompt: Enter las ejecuta en orden y se detiene en la primera que
falla; Esc las descarta. Se admiten hasta 200 líneas y 64 KB.

## Salida

La salida conserva hasta 20 000 líneas o 4 millones de caracteres; lo más antiguo se
descarta primero. Solo se dibujan las filas visibles, así que el largo no cambia el costo de
un cuadro. Las líneas largas se envuelven por palabras y se reacomodan al cambiar el tamaño
de la ventana, sin alterar el texto: copiar no agrega saltos de línea. `copiar` lleva el
resultado de la última orden, sin su eco ni las líneas decorativas de las tablas; `copiar
todo` lleva la salida completa.

Los errores se distinguen por palabras, además del color: `orden desconocida`, `argumento
inválido`, `selección ambigua`, `no disponible ahora`, `todavía no implementado`, `falta un
recurso`. Cada uno indica qué probar a continuación.

**Opciones > Preferencias > Consola**, o `opcion`, ajustan la forma del cursor
(`consola-cursor`), su parpadeo (`consola-parpadeo`), las líneas de monitor
(`consola-crt`, apagadas al inicio) y si las órdenes se recuerdan entre sesiones
(`consola-historial`). El historial queda en `%APPDATA%/RetroEmu/consola-historial.json`,
hasta 1000 órdenes, y el diagnóstico nunca lo incluye.

## Tareas en segundo plano

`validar`, `preparar`, la búsqueda de juegos, la identificación en línea y los trabajos de
los talleres aparecen en `tareas`. El prompt vuelve enseguida: se puede seguir usando el
programa mientras corren.

```text
validar 1          inicia una prueba y dice su número
tareas             lista todas, con estado y avance
seguir 1           muestra cada etapa; Ctrl+C deja de seguirla, no la detiene
tarea 1            detalle: etapa, resultados, errores y archivos
cancelar 1         pide detenerla; lo ya producido se conserva
retomar 1          la vuelve a intentar, si admite
```

El estado distingue trabajo local, espera del proveedor, espera de tu respuesta, pausa,
bloqueo por un recurso y finalización parcial. El avance solo se muestra cuando algo se
contó: sin un conteo real no hay porcentaje. Si una tarea no informa nada durante 45 s
(cuatro minutos cuando espera a un proveedor), se marca sin actividad y se explica cómo
cancelarla. El prompt muestra cuántas siguen vivas: `retro:/biblioteca [nes pausa] (2)>`.

## Foco del teclado

Mientras la consola está abierta, ninguna tecla llega a la máquina. `jugar` y `continuar`
le entregan el teclado; el Enter que ejecutó la orden no llega a la máquina aunque siga
apretado, y Ctrl+Shift+T tampoco. Con el juego en pantalla queda una fila de consola abajo:
un clic permite escribir una orden, Enter o Esc devuelven el teclado al juego. Dentro de la
partida las teclas de función del emulador (F2, F3, F5, F8, F9, según la máquina) funcionan
como en los demás estilos. Cada máquina conserva sus propias teclas: en Spectrum, C64 y PC
los atajos de herramientas llevan Ctrl+Shift.

## Cobertura por órdenes

El buscador gráfico también incluye las órdenes del catálogo, con parámetros y ejemplos
en la ayuda al pasar el puntero. Elegir una abre la consola con su nombre preparado y
su ayuda; completá los argumentos y pulsá Enter para ejecutarla. Al volver a Biblioteca
o a la partida se conserva el estilo gráfico elegido. Las acciones siguen operando
directamente sobre el contexto actual.

**Completo** significa que el recorrido llega al resultado escribiendo. **Parcial** significa
que la orden abre o consulta, y una parte sigue en un instrumento visual.

| Función | Por órdenes | Sigue en un instrumento visual |
| --- | --- | --- |
| Biblioteca, secciones, búsqueda, filtros, orden | Completo | — |
| Favoritos, recientes, fichas, correcciones | Completo | — |
| Identificación, candidatos, rechazo | Completo | — |
| Carpeta de juegos y escaneo | Completo | — |
| Abrir originales, creaciones y fusiones | Completo | Elegir entre varios programas de un ZIP de PC abre un diálogo. |
| Pausa, reinicio, capturas, turbo, cinta | Completo | — |
| Ranuras, rebobinado, grabación de entradas | Completo | Importar guardados de versiones anteriores. |
| Configuración e interfaz | Completo | — |
| Tareas, cancelación, reintento | Completo | — |
| Actualizaciones | Parcial | `actualizar` informa; instalar se confirma en su ventana. |
| Registros, memoria, escritura, desensamblado | Completo | — |
| Breakpoints, pasos, ejecución hasta una dirección | Completo | — |
| Búsqueda de variables por comparación | Completo | Observación avanzada de escrituras y propuestas de IA. |
| Variables del perfil | Parcial | Consulta por órdenes; editarlas sigue en Modificar y analizar. |
| Comentarios, etiquetas, consulta y copia JSON de anotaciones | Completo | Importación por nombre con confirmación; no cambia bytes ni usa IA. |
| Referencias de rutinas, parches, exportación ASM y de perfiles | Parcial | Código y rutinas, con `hacer code`. |
| Proyectos: listar, consultar, validar | Completo | — |
| Preparar un original sin IA | Completo | La preparación con IA sigue en el taller de fusión. |
| Crear y fusionar | Parcial | El diseño y la construcción usan sus talleres: `crear`, `fusionar`, `hacer inspired`. |
| Retomar trabajos guardados | Parcial | `retomar-trabajo` abre el taller en ese trabajo. |
| Investigación automática | Parcial | Se inicia en su panel, con `hacer auto-analysis`; se sigue en `tareas`. |
| Conexión de IA | Parcial | `hacer ai` abre su ventana. |

## Referencia de órdenes

La sección siguiente se genera desde las definiciones del programa; una prueba verifica
que coincida. `ayuda <orden>` agrega argumentos, ejemplos y errores.

<!-- referencia-inicio -->

### Interfaz

| Orden | Función |
| --- | --- |
| `paleta [ver\|lista\|usar\|color\|editar\|restablecer] [valor] [rgb]` (`palette`) | Elige una paleta o personaliza ocho colores RGB de la interfaz actual. |

### Biblioteca

| Orden | Función |
| --- | --- |
| `cd <seccion>` (`seccion`, `ir`) | Cambia de sección virtual; no recorre carpetas del sistema. |
| `pwd` | Muestra la sección actual y los filtros activos. |
| `ls` (`dir`) | Lista la sección actual. |
| `secciones` | Muestra cada sección de la biblioteca con su cantidad. |
| `juegos [texto…]` (`lista`) | Lista y numera los juegos de la sección con los filtros activos. |
| `buscar <texto…>` (`find`) | Busca títulos y géneros sin perder los demás filtros. |
| `filtrar <campo> [valor…]` (`filtro`) | Combina filtros compartidos con la biblioteca gráfica. |
| `filtros` | Muestra los filtros activos y el orden. |
| `ordenar <nombre\|nombre-desc\|año-desc\|año\|recientes\|agregados>` | Cambia el orden de la lista. |
| `ficha <juego…>` (`info`) | Muestra sistema, datos, procedencia, disponibilidad y preparación. |
| `corregir <juego> <genero\|año> <valor…>` (`editar-ficha`) | Guarda una corrección personal, que tiene prioridad sobre lo detectado. |
| `favorito <juego> [on\|off]` (`fav`) | Marca o quita el favorito que se ve en la biblioteca. |
| `favoritos` | Lista tus favoritos. |
| `recientes` | Lista los juegos abiertos últimamente. |
| `identificar [estado\|on\|off\|reintentar]` (`catalogo`) | Consulta o controla la identificación de juegos. |
| `omitir <juego> [on\|off]` (`rechazar`) | Rechaza o recupera la identificación automática de un juego. |
| `candidatos <juego…>` | Pide al catálogo posibles identidades de un juego, para revisarlas. |
| `candidato <numero> <aceptar\|rechazar>` | Acepta o rechaza un candidato de la última búsqueda. |
| `carpeta [ruta…]` (`roms`) | Muestra o cambia la carpeta de juegos, y la recuerda. |
| `escanear` (`actualizar-biblioteca`, `refrescar`) | Vuelve a revisar la carpeta de juegos en segundo plano. |

### Partida

| Orden | Función |
| --- | --- |
| `jugar <juego…>` (`play`, `correr-juego`) | Abre un original, creación o fusión y le entrega el teclado. |
| `abrir <ruta…>` (`open`) | Abre un archivo del equipo con el emulador. |
| `continuar` (`seguir-jugando`, `volver`, `resume`) | Vuelve a la partida y le entrega el teclado. |
| `pausa` (`pausar`) | Pausa la máquina sin salir de la consola. |
| `reiniciar [duro]` (`reset`) | Reinicia la partida o fusión; duro también limpia la memoria del original. |
| `captura` (`foto`, `screenshot`) | Guarda la imagen emulada a su resolución. |
| `ranuras` | Lista las partidas guardadas de este juego y edición. |
| `guardar <ranura> [reemplazar]` (`save`) | Guarda la partida en una ranura. Si está ocupada exige escribir reemplazar. |
| `cargar <ranura>` (`load`) | Recupera una ranura de este juego y edición. |
| `rebobinar [accion]` (`rewind`) | Activa el historial o retrocede en la partida. |
| `entradas [estado\|grabar\|terminar\|reproducir\|detener]` (`grabar`) | Graba y reproduce las entradas de esta sesión. |
| `turbo [on\|off]` | Acelera la emulación todo lo posible. |
| `cinta [estado\|reproducir\|detener\|rebobinar\|expulsar\|load]` | Controla la cinta del ZX Spectrum. |
| `detener` | Termina la fusión o creación en curso. |
| `controles` | Muestra cómo se juega en la máquina actual. |
| `maquinas` (`sistemas`) | Lista los sistemas y su estado real de implementación. |
| `maquina <id>` | Enciende una máquina implementada con su firmware local; no descarga nada. |
| `bateria [estado\|abrir\|guardar\|exportar\|recuperar] [ruta] [reemplazar]` (`battery`) | Consulta, guarda, exporta o recupera la batería del cartucho NES actual. |

### Análisis

| Orden | Función |
| --- | --- |
| `registros` (`regs`, `r`) | Lee los registros de la CPU. |
| `memoria <direccion> [cantidad]` (`mem`, `m`) | Muestra memoria sin efectos de lectura del hardware. |
| `escribir <direccion> <valor>` (`poke`) | Escribe un byte en memoria para probar una hipótesis. |
| `codigo [direccion] [cantidad]` (`dis`, `u`) | Desensambla instrucciones sin modificar la máquina. |
| `paso [cuadro\|saltar]` (`step`, `s`) | Avanza una instrucción, un cuadro o pasa por encima de una rutina. |
| `correr [hasta]` (`run`, `g`) | Ejecuta la máquina con la consola abierta, hasta un breakpoint o una pausa. |
| `bp [lista\|agregar\|quitar\|limpiar] [direccion]` (`breakpoint`) | Administra los breakpoints del depurador. |
| `rastrear <accion> [valor]` (`buscar-memoria`) | Busca una variable comparando la memoria entre momentos de la partida. |
| `variables` | Lista las variables conocidas de este juego, con su valor y su evidencia. |
| `exportar-anotaciones <ruta…>` (`export-notes`) | Exporta una copia JSON de las anotaciones actuales sin sobrescribir archivos. |
| `anotaciones [direccion]` (`notes`) | Lista anotaciones o consulta una dirección de la edición actual. |
| `anotar <direccion> <texto…>` (`note`) | Guarda un comentario manual en una dirección del original actual. |
| `etiquetar <direccion> <texto…>` (`label`) | Guarda un nombre manual para una dirección del original actual. |
| `importar-anotaciones <confirmar>` (`import-notes`) | Incorpora anotaciones anteriores por nombre con confirmación de edición. |

### Proyectos

| Orden | Función |
| --- | --- |
| `crear` | Abre el taller visual de creación. |
| `fusionar` | Abre el taller visual de fusión. |
| `proyectos` (`trabajos`) | Lista fusiones, creaciones, trabajos guardados e investigaciones. |
| `proyecto <proyecto…>` | Muestra una creación: idea, originales, versiones y qué falta comprobar. |
| `validar <proyecto…>` (`probar`) | Prueba una creación con jugadores automáticos, en segundo plano. |
| `retomar-trabajo <proyecto…>` | Abre el taller visual en un trabajo o una investigación guardados. |
| `preparar <juego…>` (`investigar`) | Investiga un original sin IA para usarlo en fusiones, en segundo plano. |

### Tareas

| Orden | Función |
| --- | --- |
| `tareas [limpiar]` (`jobs`, `ps`) | Lista los procesos largos de esta sesión y qué está haciendo cada uno. |
| `tarea <tarea>` | Muestra el detalle de una tarea: etapa, resultados, errores y archivos. |
| `seguir <tarea>` (`follow`, `tail`) | Muestra en la consola cada avance de una tarea hasta que termine. |
| `cancelar <tarea>` (`kill`, `detener-tarea`) | Pide detener una tarea. Lo que ya produjo se conserva. |
| `retomar <tarea>` | Vuelve a intentar una tarea cancelada o fallida que lo admita. |

### Sistema

| Orden | Función |
| --- | --- |
| `requisitos` (`requirements`) | Muestra los requisitos y qué se comprobó en este arranque. |
| `novedades` (`news`) | Muestra las funciones nuevas y los cambios de las últimas versiones. |
| `estado` (`status`) | Muestra máquina, partida, biblioteca, interfaz y tareas. |
| `opciones [texto…]` (`ajustes`, `preferencias`) | Lista los ajustes del programa con su valor actual. |
| `opcion <id> [valor…]` (`ajuste`) | Lee, cambia o restablece un ajuste e indica su alcance. |
| `interfaz <retro\|explorer\|real\|lounge\|preview>` (`estilo`, `tema`) | Cambia el estilo o abre la elección con vistas previas. |
| `acciones [texto…]` | Lista las funciones del programa con su disponibilidad actual. |
| `hacer <id>` | Ejecuta una acción del catálogo; los instrumentos visuales abren su panel. |
| `actualizar [estado\|abrir]` (`version`, `actualizaciones`) | Consulta la versión o abre el actualizador del programa. |
| `diagnostico [ver\|copiar]` | Muestra un resumen técnico sin datos personales; copiarlo es una acción explícita. |
| `salir` (`exit`, `quit`) | Cierra el programa, con la misma protección de trabajos pendientes del menú. |

### Consola

| Orden | Función |
| --- | --- |
| `copiar [ultimo\|todo]` (`copy`) | Copia el resultado de la última orden o toda la salida, sin decoraciones. |
| `atajos` (`teclas`) | Lista las teclas de la consola y de la partida. |
| `ayuda [tema]` (`help`, `?`, `man`) | Explica una orden, un grupo o lista todas. |
| `como <tema…>` (`apropos`) | Busca cómo hacer algo sin conocer el nombre de la orden. |
| `limpiar` (`clear`, `cls`) | Borra la salida guardada de la consola. |
| `historial [texto…]` (`history`) | Lista, busca o borra las órdenes anteriores. |

<!-- referencia-fin -->

## Agregar una orden

Una orden es una definición `CommandSpec` en los archivos `RetroApp.Terminal*.cs`: nombre,
grupo, resumen, argumentos con su tipo, ejemplos, requisitos y qué hacer. Sintaxis,
validación, autocompletado y ayuda salen de esa definición; el intérprete no se toca.
`TerminalCatalogTests` rechaza nombres repetidos, ejemplos que no respetan su sintaxis y
referencias desactualizadas: después de cambiar una orden, ejecutá esa prueba con
`RETROEMU_WRITE_REFERENCE=1`. Las acciones (`AppCommand`) y los ajustes (`AppOption`) que
se agregan a sus catálogos aparecen solos en `acciones`, `opciones`, `como` y Tab.
