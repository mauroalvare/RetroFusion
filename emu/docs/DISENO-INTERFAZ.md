# Interfaz y herramientas

La biblioteca reúne originales, favoritos, recientes, fusiones y creaciones. **Abrir
archivo** y arrastrar un archivo siguen siendo las formas directas de empezar. Las
tarjetas por sistema muestran los formatos disponibles; reconocer un formato no
certifica que un juego llegue a una partida controlable.

## Identidad de banco de electrónica

La interfaz toma referencias de instrumentos, placas y equipos domésticos: grafito,
marfil, ámbar, rotulado monoespaciado y controles rectos. La biblioteca muestra cada
máquina con su ilustración y sus formatos; el taller y el instrumental usan filas de
herramientas en lugar de una cuadrícula de tarjetas idénticas.

RetroArt dibuja la marca, conectores, instrumentos y el panel de osciloscopio con vectores
propios. El osciloscopio es una ilustración, no una lectura de rendimiento. No requiere
imágenes, descargas ni fuentes distribuidas. Las pantallas de los juegos conservan sus
píxeles; esta identidad pertenece a la interfaz. Los títulos usan Agency FB Bold, el texto Gill Sans MT y el rotulado OCR A Extended
cuando están instaladas; hay alternativas locales para los demás equipos. La composición
tiene carácter industrial sin convertir los párrafos en texto de terminal. Código y
direcciones conservan una fuente de ancho fijo separada.
El alto contraste mantiene su paleta accesible y omite detalles decorativos secundarios.

Retro sigue siendo el estilo principal. **Opciones > Estilos de interfaz** agrega Terminal
verde, con los mismos controles gráficos, y Terminal hardcore, con una consola interna.
Los ajustes, acciones y biblioteca son comunes; las vistas previas y la oferta después
de una actualización confirmada se explican en [Interfaces y datos](INTERFACES-Y-DATOS.md).

## Biblioteca

Filas de altura uniforme, con Jugar y Ficha en la misma línea. En ventanas anchas un panel muestra
la selección con su procedencia; doble clic o Enter sobre la selección juegan. Un dato desconocido
se muestra como desconocido. En listas muy angostas Jugar y Ficha se abrevian a `▶` e `i`, con su
nombre al pasar el mouse, para que el título se pueda leer.

## Navegación y espacios de trabajo

La barra superior mantiene **Biblioteca, Jugar, Crear, Analizar, Herramientas y Ayuda**.
En ventanas chicas se convierte en un selector con las mismas opciones. **Buscar**
abre el catálogo de acciones. Los menús y sus atajos siguen disponibles.

**Crear** presenta los tres puntos de partida: desde cero, fusionar originales y crear
con originales. **Herramientas** reúne investigación automática, análisis, código, depuración, partidas guardadas,
rebobinado, grabación de entradas, capturas, preferencias, sonido, IA y actualizaciones.
**Ayuda** muestra controles de la máquina actual, atajos y cómo seguir si faltan datos.
Las acciones que necesitan una partida explican su requisito.

Los talleres y el analizador ocupan el espacio entre las barras, con tarjetas y columnas
cuando hay ancho suficiente y desplazamiento vertical cuando hace falta. **Jugar** vuelve
a la máquina actual; ocultar un taller no cancela la IA. Mientras la interfaz ocupa ese
espacio, teclado y mando no envían acciones a la máquina y el sonido del juego se silencia.
La biblioteca conserva su comportamiento de pausa y restaura la pausa anterior al salir.

Con una partida abierta, **Volver a la partida** queda a la vista desde cualquier herramienta. La
barra de estado muestra las tareas en curso: su lista dice qué espera cada una y cómo cancelarla.

## Encontrar una acción

**Herramientas > Buscar acción** encuentra opciones por nombre o palabras relacionadas:
volumen, guardar, código, crear, pantalla o IA. También está en la barra lateral de la
biblioteca. El atajo es Ctrl+P, o Ctrl+Shift+P en máquinas que reservan esas teclas para
el juego. Flechas y Enter eligen un resultado; Esc cierra. Las acciones no disponibles
explican qué falta. Buscar no inicia tareas de IA ni cambia la partida.

El menú completo se agrupa bajo **Menú** cuando no cabe. Preferencias conserva tamaños
de texto de 100 a 200 %, alto contraste, controles, pausa por foco y ahorro de energía.
Los espacios se adaptan al texto y usan desplazamiento vertical en pantallas pequeñas. La interfaz usa una fuente de lectura del sistema; código y registros conservan
tipografía de ancho fijo. No se distribuyen fuentes adicionales.
Al seguir PC, el depurador retrocede solo por límites de instrucciones observadas que
conducen a ese PC. Sin esa evidencia empieza en PC: no interpreta operandos anteriores
como si fueran instrucciones conocidas. El listado sigue siendo una vista secuencial,
no un historial de los últimos saltos.

## Crear

El creador separa máquina, idea y método de creación. La plantilla sirve sin IA; la
selección de proveedor, modelo y conexión sigue disponible. El estudio distingue
originales, juego base y aportes. La máquina no se muestra detrás del taller.
Los juegos terminados se abren desde Biblioteca o Jugar; **Trabajos guardados** permite
retomar investigaciones y construcciones. La investigación parcial presenta acciones para
jugar cada original y abrir su analizador, avances por original y guías con pasos/evidencia.
Las notas del jugador se guardan como observaciones que deben reproducirse, sin convertirlas
en variables verificadas. [Preparar una fusión](PREPARAR-FUSION.md) detalla la recuperación.

Las guías compartidas de IA exigen conservar lo que funciona, diseñar transiciones y
respuesta visual, y separar lo observado de lo supuesto. Los robots comprueban parte
del funcionamiento; sus aprobaciones no certifican diversión, fidelidad ni calidad.

El visor de rutinas mantiene la lista y el detalle en paralelo cuando hay ancho;
con texto grande ofrece un selector de rutina. La explicación con IA se despliega a
pedido y el listado conserva un área mínima con desplazamiento. Al volver de Jugar,
Analizar conserva la página elegida para continuar una búsqueda.

## Investigación automática

La [investigación automática](ANALISIS-AUTOMATICO.md) muestra etapas, tiempo, candidatos y
efectos comprobados, con pausa y continuación. Usa máquinas privadas y distingue una
hipótesis de un experimento; no exige jugar antes ni promete una cantidad de hallazgos.
Las direcciones y comparaciones conservan tipografía monoespaciada. Los indicadores de
actividad acompañan trabajos reales sin representar porcentajes; son estáticos en alto
contraste. Las capturas de QA guardan el framebuffer con su tamaño físico, sin aplicar
dos veces la escala DPI.

## Referencias de rutinas

Al preparar referencias para escribir una fusión, el estudio toma la captura del
arranque y después observa 12.000 pasos en una **máquina separada**. Registra sitios de
escritura y E/S cuando el sistema ofrece esos monitores. Agrupa candidatos mediante las
llamadas observadas; limita raíces, accesos y resultados. Si ve cambiar instrucciones
en una dirección, no publica una agrupación estable para esa muestra.

Las referencias identifican los archivos por SHA-256 y el conjunto por una huella que
incluye perfil y firmware. Incluyen direcciones y frecuencias, sin bytes de instrucciones.
Un acceso de hardware no demuestra el propósito de una rutina. Interrupciones, saltos
indirectos, bancos y código compartido requieren revisión. Sin monitor de lecturas E/S,
la ausencia de lecturas no se interpreta como evidencia de que no existan.

En **Herramientas > Código y rutinas**, **Copiar referencia** identifica la edición del
original cargado, firmware e instantánea de código. En una fusión el visor declara que
no tiene asociada la edición de la ranura; las referencias del proyecto sí la identifican.
Una cita no autoriza copiar código de originales en una creación autónoma ni añadir
direcciones sin comprobar al perfil.
Podés pegar esa referencia en la idea del creador para estudiar una técnica, conservando
nombre del original, edición y dirección. El juego autónomo sigue requiriendo código y
recursos propios; el creador no importa instrucciones de la ROM.

El mismo hallador está en el ejecutor sin ventana:

```powershell
retro-cli run programa.nes --frames 120 --rutinas observaciones.json --pasos-rutinas 12000
```

Admite direcciones de 16 bits. La muestra avanza únicamente la instancia de diagnóstico,
después de los cuadros pedidos; las capturas de `run` corresponden al momento anterior
a esa muestra. `RoutineFinder.Sample` requiere una máquina descartable y no debe usarse
sobre la partida del jugador. No es una certificación de controles ni un análisis completo.

La revisión nativa se ejecuta con `--hidden --mute --autotest`. Ese modo no guarda
posiciones de ventanas. `--ui-size 640x480` permite comprobar el tamaño mínimo; se acepta
solo en esa instancia oculta de prueba. La instalación y las bibliotecas reales no se
reemplazan para estas comprobaciones.

En **Ayuda > Comprobar teclado y mando** se ve qué teclas están apretadas, cuándo se
sueltan, la última entrada y la palanca/botones del primer mando. Si el dispositivo
responde ahí pero no en el original, la vista indica revisar foco, controles y puerto.
Es una comprobación de recepción; no acredita por sí sola las reglas del juego.

Durante una fusión, la barra inferior muestra el HUD y sus avisos temporales antes del
estado genérico del emulador. El texto completo queda en su ayuda al pasar el puntero;
la imagen nativa no se usa para presentar instrucciones técnicas ni se tapa con paneles.
La entrada `--games-folder <carpeta>` selecciona otra biblioteca para esa sesión sin
mover archivos ni cambiar preferencias. `--no-organize` conserva su distribución.

## Ventanas pequeñas y recuperación de ajustes

Con poca altura y texto grande, las acciones quedan en **Menú** y **Buscar acción**;
se retira la segunda barra de navegación para dejar lugar a los controles. El menú
conserva **Volver a jugar**, incluso al encender una máquina sin un archivo de juego.
El buscador compacto dedica el área a los resultados y se cierra con Esc o la cruz.
Los encabezados reducen su altura; los detalles siguen en su ayuda al pasar el puntero.

**Partidas guardadas** respeta el área entre menú y estado. Usa ranuras en vertical
cuando no cabe la tabla; Guardar y Cargar conservan sus confirmaciones y su vínculo
con el juego y la sesión. En **Sonido**, las etiquetas están encima de los controles
y los campos usan el ancho disponible.

Preferencias, Sonido y Conectar IA retienen las teclas y el mando para navegar por
la configuración. Los atajos de la partida se suspenden mientras están abiertas.
La partida puede seguir ejecutándose y sonar para probar volumen y opciones.
Pantalla completa y Buscar acción conservan sus atajos cuando no se está editando texto.

Si un archivo de ajustes no se puede leer, el aviso indica que los cambios duran
solo esa sesión. **Recuperar archivo** conserva el original en una copia única y
guarda los valores actuales. La ruta del respaldo aparece en la ayuda del aviso.
El analizador ofrece **Recuperar anotaciones** con la misma protección. Si falla
el respaldo, el original se conserva; si falla el guardado posterior, se informa
el problema y sigue disponible la copia que llegó a completarse.
