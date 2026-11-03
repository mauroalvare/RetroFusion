# Limitaciones y errores conocidos

Alcance implementado y comportamientos pendientes. Las comprobaciones por edición
de un juego no certifican compatibilidad universal. Cuando se indica **probable**,
se trata de una limitación deducida que todavía necesita una prueba específica.

## Recuperación de archivos y alcance de las comprobaciones

- Los paquetes actuales son x64 para Windows y Linux; no prueban todas las versiones del
  sistema ni todos los controladores. El runtime incluido no reemplaza las bibliotecas
  nativas de Linux. Las gráficas incluidas requieren glibc 2.38 o posterior y X11/XWayland;
  el comprobador POSIX no sustituye una prueba de escritorio ni verifica ICU/OpenSSL.
  La reparación automática consentida cubre sólo Visual C++ x64 cuando
  falta una importación comprobada en Windows. No instala SDK, firmware ni controladores,
  no modifica repositorios Linux y no alcanza un runtime administrado que ya no arranca.
  La firma Microsoft se verifica con certificados en caché; si falla, no se ejecuta el
  instalador. Ver [requisitos y recuperación](ARRANQUE-Y-RECUPERACION.md).

- La identificación combina fichas personales, listas locales, indicios del nombre y
  Wikidata opcional. El catálogo público tiene cobertura incompleta; exige título/alias
  exacto y plataforma, sin elegir entre coincidencias ambiguas. No certifica la edición
  por hash. No se atribuye a una plataforma el primer lanzamiento de otra. Los datos
  desconocidos permanecen explícitos. [Fuentes y terminal](INTERFACES-Y-DATOS.md).
  La incorporación se registra desde la primera observación por esta función, sin
  reconstruir la antigüedad de archivos ya existentes. [Uso y datos](FILTROS-BIBLIOTECA.md).
  La actividad se limita al historial conservado; la ausencia de una partida registrada
  no prueba que nunca se haya abierto ese juego.
- La terminal real es interna y navega con órdenes. Los editores avanzados conservan
  sus paneles visuales, abiertos mediante `hacer`; no se duplican como editores de texto.
  La [matriz de cobertura](CONSOLA.md#cobertura-por-órdenes) dice qué recorridos se
  completan escribiendo y cuáles son parciales: crear, fusionar, conectar la IA, editar
  variables, anotar rutinas y exportar siguen en sus instrumentos. Las opciones y acciones
  usan los catálogos comunes de los estilos gráficos. Los metadatos no se consultan en línea
  hasta habilitarlo; las pruebas usan respuestas simuladas, no certifican disponibilidad
  permanente del proveedor.
- Los contratos de orden, acción y ajuste son tres definiciones. El buscador combina las
  acciones con las órdenes de `CommandSpec`, incluidos parámetros, ejemplos y ayuda.
  Elegir una orden prepara una línea editable y muestra su ayuda; no ejecuta la operación.
  La consola se superpone al estilo gráfico actual sin cambiar la preferencia guardada.
  Una acción opera sobre el contexto actual; su orden equivalente puede pedir argumentos.
- La consola dibuja una grilla de ancho fijo. Un carácter fuera de las fuentes instaladas se
  ve como signo de pregunta, aunque se conserva al copiarlo: los emojis y la escritura de
  derecha a izquierda no tienen soporte. Las fuentes dependen del sistema: la auditoría de
  caracteres se comprobó con las fuentes de Windows, no en Linux ni macOS.
- Los ocho colores de cada interfaz admiten cualquier RGB; una combinación elegida puede ser
  difícil de leer. El editor permite previsualizar, cancelar y restaurar la base. Alto contraste
  aplica colores accesibles sin borrar la paleta guardada. Retro, Explorador, Salón y Terminal
  real tienen distribuciones y navegación propias; comparten los editores avanzados y servicios
  de juegos, estados y proyectos. Ámbar es una paleta, aplicable a cualquier diseño.
  No se importan temas de otras terminales. La preferencia de colores nueva sigue en v1;
  un programa antiguo puede leer los campos comunes, pero al guardar pierde los campos nuevos.
- `tareas` conoce el avance que cada proceso informa. Una espera de IA por copiar y pegar o por
  carpeta de agente figura como esperando tu respuesta y no se cancela desde la lista, sino
  en su taller. `retomar` solo vale para tareas que dejaron cómo reintentarse; las de los
  talleres se continúan desde **Trabajos guardados**. Una tarea sin avances no se interrumpe
  sola: se marca sin actividad y la decisión es tuya.
- Requiere Windows 10 o posterior de 64 bits, o Linux x64, y OpenGL 3.3. Sin esos gráficos no
  puede abrir su ventana y lo explica; no incluye un dibujo por software. El ejecutable no está
  firmado, así que Windows puede advertir la primera vez. No se comprobó en Linux real ni en
  Windows 10; las pruebas nativas corren en Windows 11 con una ventana oculta.
- Sin una salida de audio utilizable el programa funciona en silencio, a la velocidad normal, y la
  barra de estado lo indica. **Opciones > Sonido** o `hacer audio-retry` la vuelven a buscar. Si el
  dispositivo deja de pedir sonido con el programa abierto se trata igual, y vuelve solo si responde.
  `--no-audio` evita inicializar el dispositivo hasta pedir **Buscar salida**; `--mute` solo silencia.
  La comprobación `--check-startup informe.json` funciona antes de abrir gráficos y enumera archivos
  y requisitos. Encontrar una DLL no demuestra que pueda cargarse ni certifica el controlador gráfico.
  El paquete autocontenido lleva .NET; jugar no requiere SDK, Python ni una cuenta de IA. El firmware
  depende de la máquina. Una prueba con audio desactivado no certifica una PC sin dispositivo físico.
- El historial de órdenes se guarda en claro en la cuenta del usuario; las rutas escritas en una
  orden quedan ahí. `historial borrar` lo vacía y `consola-historial` lo desactiva.
- Un favorito o reciente ausente se muestra deshabilitado después de escanear. La actividad
  guarda solo identidad, sin rutas ni títulos: si no hay identificación local se muestra una
  clave corta. Agregar la carpeta y escanear recupera el acceso cuando vuelve el mismo
  contenido. No se busca automáticamente fuera de la biblioteca elegida.

- Rechazar una cabecera inválida, bytes sobrantes o valores fuera de rango no detecta
  toda alteración posible de RAM en un estado. Varios formatos anteriores no tienen
  checksum. Los formatos y versiones admitidos se conservan; no se certifica cualquier
  archivo externo solo porque pueda leerse. El host protege la partida al cargar; las
  API internas de cada máquina no ofrecen todas la misma garantía de transacción.
- Recuperar ajustes o anotaciones conserva el archivo ilegible y guarda los valores
  actuales de la sesión. No reconstruye valores que no pudieron leerse. Los respaldos
  completados se conservan también si falla el guardado posterior y no se eliminan
  automáticamente.
- Un parche rechazado restaura la partida y sus bytes de programa; no se permiten rangos
  superpuestos entre parches guardados. Deshacé el anterior para reemplazar ese rango.
  Un respaldo creado por una versión anterior con longitudes inconsistentes no permite
  reconstruir los bytes originales que nunca se guardaron. Se conserva y se pide cargar
  el original antes de crear otro parche. Aplicar varios parches informa resultados por
  parche; no es una transacción única para toda la lista.
- Las pruebas de interfaz incluyen ejecución oculta y eventos de ImGui. No sustituyen
  una comprobación física de cada teclado, mando, pantalla o entorno gráfico soportado.

## Investigación para crear juegos

- La IA no puede garantizar el descubrimiento de todas las reglas de cualquier original.
  Arranque, entradas reproducibles, escrituras y mecánica completa requieren pruebas distintas.
  El plan de comprobación conserva los faltantes, ofrece instrucciones y muestra la evidencia
  por original. Una nota del jugador no se promueve a dato verificado.
- Los recorridos dirigidos de una fusión repiten entradas y comparan cambios de variables
  desde el host. Solo acreditan esos intervalos y situaciones, con el alcance semántico
  del perfil. No prueban por sí solos causalidad entre originales ni todos los finales.
  Los juegos anteriores sin planes conservan la comprobación general de robots y ese límite
  explícito; [formato y límites](../src/Retro.Studio/Docs/GUIA-FUSION.md#recorridos-dirigidos).
  Se pueden medir extremos y cantidad de cambios entre muestras a 60 Hz. Un evento que
  empieza y termina entre dos muestras puede pasar inadvertido; tampoco se corrigen
  automáticamente los saltos de valor de un campo que envuelve su rango.
- La investigación guarda la propuesta antes de empezar y los resultados completados por
  original/revisión. No recupera el interior de una prueba interrumpida antes de su checkpoint.
  Los registros antiguos pueden contener objetivos recortados sin orden de ranuras ni base;
  requieren revisión y no contienen un juego ejecutable si nunca se escribió.
  La recuperación conserva los registros dañados y los descarta del listado sin bloquear
  los válidos. El guardado publica la ficha después del guion y conserva los archivos
  previos. Una interrupción entre ambas publicaciones puede dejar un guion sin ficha;
  el trabajo pendiente permite continuar y no se borra ese archivo por inferencia.
- El creador desde cero comprueba movimiento sostenido con controles conocidos; no es
  todavía un comprobador universal para juegos de decisiones discretas. La cancelación
  se atiende entre cuadros y durante carga/controles, no dentro de una instrucción de CPU
  o del ensamblador. Las respuestas inválidas y los presupuestos no se convierten en una
  aprobación. [Corrección y recuperación](CREACION-Y-RECUPERACION.md).
- Observar bytes vecinos de variables conocidas ayuda a encontrar campos omitidos, pero
  proximidad y cambio de valor no prueban su propósito. Se observa una máquina privada y se
  restaura su estado; los candidatos necesitan contraste. Las funciones curadas solo se agregan
  a un alias compatible cuando coincide el SHA-256 del programa completo conocido.
- La revisión combina proveedores simulados, programas propios y evidencia por edición.
  La primera campaña real contrastó análisis individual. La siguiente, limitada a25minutos,
  produjo un borrador de fusión y se detuvo durante su corrección. El mismo guion pudo
  revalidarse localmente sin nuevos pedidos tras corregir el validador, con observaciones
  de dificultad. Es una aprobación técnica, no de diversión ni de todos los finales.
  Las implementaciones locales y sus experimentos dirigidos se prueban por separado.
  El contraste posterior del flujo completo reutiliza dos pares de originales ya
  preparados y respuestas locales simuladas: observa corrección, validación, guardado,
  arranque y reapertura. No mide descubrimiento autónomo desde cero ni creatividad del
  proveedor externo. Una hipótesis explícita o una observación sin evidencia no puede
  levantar un requisito esencial; los perfiles anteriores mantienen su alcance declarado.

## Análisis y modificaciones de partidas

- La [investigación automática](ANALISIS-AUTOMATICO.md) arranca y prueba entradas en máquinas
  privadas de los cuatro sistemas con laboratorio. No requiere jugar antes, pero tampoco
  garantiza encontrar todas las variables o certificar su semántica. La meta cuenta efectos
  observados de escritura. Los cambios se contrastan con una corrida de control; las
  diferencias finales de RAM incluyen el dato probado y consecuencias, sin distinguir
  automáticamente todas las copias ni todos los efectos que aparecen más tarde.
- La pausa conserva etapas completadas, no el interior de una prueba interrumpida. Continuar
  agrega el presupuesto elegido; abrir un guardado no gasta IA. Edición/firmware distintos
  impiden mezclar datos; cambios de CPU, hardware, laboratorio o perfil invalidan estados
  y marcan los efectos anteriores como históricos hasta repetir el contraste. Los guardados
  tienen límites de tamaño/cantidad y se administran explícitamente, sin borrar los antiguos.

- El hallador de rutinas observa una muestra acotada en una máquina separada; agrupa
  candidatos por llamadas y no certifica su propósito. Código cambiante impide ofrecer
  un mapa estable. Solo ve los accesos que la máquina expone; las lecturas E/S no están
  monitorizadas en todos los sistemas. Direcciones de 20 bits no están admitidas en
  esta muestra. El visor puede citar una instantánea sin tener identificada la edición
  de una ranura de fusión, y lo declara. Detalles en [DISENO-INTERFAZ.md](DISENO-INTERFAZ.md).

- El significado de una variable propuesta por IA es una hipótesis. Ver escrituras y
  código no demuestra que su modificación produzca el efecto descrito.
- En observación avanzada de la partida, el pedido lleva hasta 128 bytes candidatos y 160 instrucciones, a partir de una
  observación acotada a 2.048 direcciones. No es un análisis completo de la ROM.
  Ese modo usa la partida actual; el automático genera sus propias entradas y estados.
- Las nuevas variables admiten bytes o palabras little endian en RAM de trabajo.
  Se excluyen firmware, hardware y regiones reservadas de dirección fija; una pila
  dinámica o código en RAM pueden entrar en la evidencia. Las áreas conservadoras
  también pueden dejar afuera variables válidas. Las herramientas manuales siguen disponibles.
- Las hipótesis pueden conservarse en el historial privado; no se guardan como perfiles verificados.
  Deshacer conserva los cinco cambios más recientes; restaurar conserva el primero.
  Ambos restauran el progreso de partida desde ese punto. Abrir, recargar o reiniciar
  un juego o cargar un estado externo descarta los puntos de regreso de esa sesión;
  el historial optativo y la selección guardada se pueden recuperar.
- La comprobación usa IA simulada y una campaña real acotada sobre un programa propio.
  Esa campaña obtuvo siete candidatos y cero efectos verificados dentro de dos pedidos;
  no alcanzó su objetivo de dos efectos. No demuestra que todos los proveedores
  encuentren variables útiles ni valida semántica o calidad creativa de sus respuestas.
- Las listas de proyectos se actualizan al abrir, guardar o pulsar Actualizar.
  Los cambios externos no se descubren leyendo todos sus archivos en cada cuadro.

- El historial de modificaciones se conserva únicamente si se activa para el juego.
  Guarda hasta doce informes y 256 variables/cambios por edición y firmware; no guarda
  los estados de deshacer entre sesiones. No recupera análisis de versiones anteriores
  que nunca se guardaron. Reaplicar es opcional y puede alterar el arranque si una variable
  también se usa durante la carga; se puede desactivar y aplicar la selección manualmente.
  Las hipótesis persistidas no se consideran perfiles verificados para fusión.

- Las ideas de cambios se basan en variables ya conocidas; no equivalen a descubrir
  automáticamente cualquier juego. Los efectos propuestos por IA requieren probarse.
  La observación reúne un intervalo y un recorte acotado de escrituras/código; no registra
  las acciones del jugador ni demuestra causalidad. Se prueba con IA simulada.
- El punto de regreso completo se ofrece para originales. Los cambios avanzados sobre
  una máquina de una fusión no restauran el guion JavaScript completo.
- Un perfil genérico por nombre puede corresponder a otra edición; se prioriza el hash
  cuando existe. Un perfil que enumera hashes ya no se reutiliza por nombre si no coincide.
- En originales, el congelado repone los bits elegidos después de cada escritura de CPU,
  antes de la siguiente lectura, y al empezar cada cuadro. No modifica ROM ni registros
  de hardware. Una escritura aplicada no confirma el significado de una hipótesis ni
  garantiza un efecto visible. El estudio comparte el congelado mediante g.freeze;
  g.set sigue siendo una escritura puntual. No hay undo completo del guion.

## Tiempo de creación con IA

- Los pedidos por una instancia de operación se serializan y la espera cancelada no reserva
  cupo. Operaciones distintas que compartan un proveedor necesitan coordinación separada.
  Una respuesta fallida sin metadata nueva deja su uso desconocido; no se copia el consumo
  anterior. El límite de texto mide caracteres enviados, no tokens del contexto interno del
  CLI, salida, saldo o coste. La caché informada pertenece a los tokens de entrada.
- La validación de creación comprueba ejecución, controles y evidencias disponibles.
  No exige puntaje a géneros que no lo usan ni demuestra por sí sola objetivos alcanzables,
  equilibrio, originalidad o diversión. Las pruebas específicas del diseño siguen siendo
  necesarias; falta una campaña amplia de generaciones reales de distintas mecánicas.

- El estimado es un intervalo orientativo de trabajos completos anteriores, incluida
  la validación y los reintentos. No es una ETA informada por el proveedor ni mide su
  cola. Requiere tres trabajos satisfactorios con la misma configuración/operación.
- El tamaño del juego, el número de correcciones y la carga del proveedor pueden
  cambiar la duración. Si se supera el intervalo se mantiene el tiempo transcurrido
  y el acceso a actividad/cancelación. Sin historial suficiente no se inventa una ETA.
- Los modos manual y carpeta de agente no ofrecen esta estimación. No se guardan
  pedidos, respuestas, ROMs ni credenciales en el historial de tiempos.

## Actualizaciones del programa

- La primera 0.2.1 se instala manualmente: las versiones anteriores no incluyen el
  actualizador. Solo los paquetes Windows x64 y Linux x64 incorporan este circuito.
  El código ejecutado con dotnet run y las pruebas ocultas no buscan actualizaciones.
- Se necesita red y permiso de escritura en la carpeta superior de la instalación.
  Sin red, con otra instancia abierta o ante un paquete inválido se conserva la versión
  instalada. Se puede buscar manualmente en Ayuda > Actualizaciones.
- Un archivo propio que coincide con uno nuevo o una instalación con enlaces requiere
  revisión manual: no se sobrescriben esos datos. Desde 0.2.5 los temporales y respaldos
  de actualizaciones confirmadas se eliminan automáticamente. Una actualización pendiente
  o interrumpida conserva sus archivos para recuperarse. Desde 0.2.4 se mantiene la carpeta de instalación
  y los accesos que apuntan a ella. No se combinan automáticamente otras copias del
  programa o bibliotecas en carpetas vecinas.
- Comprobado en Windows con archivos propios: cierre, actualización en la misma carpeta, reapertura,
  reversión al fallar el arranque y conservación de contenido/fechas. Linux tiene pruebas
  de extracción TAR y paquete inspeccionado; falta ejecutar su interfaz y reinicio nativos.
  Confirmar el primer cuadro no prueba la compatibilidad de todos los juegos.
- El paquete preparado por 0.2.1–0.2.3 se adopta al instalar el nuevo: el actualizador
  viejo puede mostrar momentáneamente la carpeta de descarga que creó; el ayudante nuevo
  la aparta al área interna y conserva la raíz original. Si esa carpeta fue modificada,
  no se retira ni sobrescribe.
- El diario permite recuperar movimientos interrumpidos desde el ayudante interno.
  Si un corte deja sin iniciar el ejecutable principal, el respaldo del programa queda
  en .retroemu-updates hasta recuperar la operación. Si Windows bloquea un temporal,
  el borrado reintenta y puede continuar en el próximo inicio. Los archivos de juegos
  no forman parte de esos movimientos. Los respaldos completos de diarios antiguos
  de formato 1 requieren revisión manual; no se eliminan con el limpiador en sitio.

## Imágenes del selector

- Se admiten PNG y JPEG locales, hasta 8 MiB, 4096 píxeles por lado y 16 megapíxeles.
  No se buscan carátulas en Internet ni se arranca cada ROM para obtener su imagen.
  Sin una imagen válida se muestra una ilustración del sistema. Una captura de validación
  no equivale a una carátula elegida ni demuestra la calidad del juego.
- La caché guarda hasta 32 entradas; al recorrer bibliotecas grandes algunas imágenes
  vuelven a cargarse. Las capturas anteriores a esta función se conservan tal como estaban;
  los juegos sin vN.png siguen siendo compatibles.

## Organización de ROMs

- Solo se mueven archivos sueltos de la biblioteca ROMs con un sistema identificable por
  formato/contenido. ZIP mixtos, formatos débiles o archivos incompletos quedan en su lugar.
  No se reordenan subcarpetas existentes ni se borran duplicados. Identificar un sistema
  todavía no implementado permite ordenarlo; no hace que sus juegos se puedan ejecutar.
- Los archivos recientes o en uso se posponen. La detección espera unos segundos sin
  cambios; si falta acceso, se pueden volver a buscar desde Biblioteca. Si el sistema no permite
  vigilar la carpeta, Actualizar sigue disponible. No hay descompresión de RAR/7z.
- Los EXE/COM sueltos permanecen donde están para no separarlos de sus archivos de datos.
- Si una colisión requiere cambiar un nombre, `.retroemu/identidades.json` conserva el
  identificador del perfil básico para las fusiones. Hay que conservar esa carpeta al
  trasladar la biblioteca. Las imágenes locales con el mismo nombre acompañan al juego
  cuando pueden moverse; una imagen bloqueada se conserva en su lugar.

## Recursos e identidad de las fusiones

- Ideas y correcciones usan contexto por operación, sin acumular borradores anteriores.
  Los tamaños de texto medidos localmente no son tokens informados por el proveedor.
  El ahorro real y la calidad creativa dependen del modelo; la comparación actual usa IA
  simulada y conserva las mismas validaciones. Un dato esencial ausente detiene la generación.

- Los sprites de memoria documentados son máscaras de un bit. `g.capture` permite
  conservar colores de personajes o tiles observados, hasta 32x32 por recurso y 4096
  píxeles por original en toda la partida. La captura no descubre comportamiento,
  posiciones ni el formato de un mapa. Esos datos deben comprobarse por separado.
- Preparar puede identificar variables; no certifica que se puedan reemplazar personajes,
  editar mapas o transferir cualquier regla. Una lectura de un sprite o el envío de
  controles al segundo original tampoco demuestra una integración mecánica.
- Los casos manuales de [PRUEBAS-FUSION.md](PRUEBAS-FUSION.md) comprueban mecánicas,
  memoria e imagen con ambas bases y otro aportante; no son campañas terminadas ni
  generaciones nuevas de IA. Siguen pendientes las comparaciones creativas con varios
  modelos y más datos verificados de mapas y reglas para nuevas combinaciones.
- Las referencias muestran el resultado del arranque configurado, que puede incluir una
  intro si el perfil está incompleto. No deducen automáticamente los mapas ni las reglas.
  Las CLI que admiten imágenes pueden leer los PNG; las API directas actuales solo reciben
  tamaño/paleta. En copia manual hay que adjuntar las imágenes al chat. La visión depende
  también del modelo elegido; el nombre de un archivo no prueba que la IA lo haya visto.

## Procesadores

Los conjuntos de instrucciones y las suites aprobadas no acreditan un 100 % general
de fidelidad. El Z80 tiene IM 0 con opcode del bus y cola en memoria, probado con
programas propios; falta contraste externo de señales. El 8080 tiene bus temporizado
optativo y READY, sin certificar todos los ciclos internos ni pines. El 6502 se contrasta
como NMOS; 6510/2A03 reutilizan ese motor con diferencias de placa. IRQ en ramas tomadas,
NMI tardía y anidación durante IRQ/BRK tienen regresiones por ciclo entero, con bus/pila
y restauración. No certifican medias fases, todas las coincidencias entre señales ni
el instante exacto de aceptación al recuperar RDY/DMA. La NES conserva sus límites de DMA.
8088/8086 tienen pendientes explícitos de temporización y contraste; esta revisión no
amplió sus modelos.

Spectrum y arcade guardan los latches de EI en formato 2; Spectrum también la NMI pendiente.
C64 formato 2 y NES desde formato 6 agregan la muestra NMI del núcleo 6502. NES7 conserva
también integración y filtro de audio; NES2–6 reinician esa historia en reposo al cargarla.
El reinicio descarta muestras anteriores; no modela por ello todos los detalles del filtro
analógico ni el aborto fino DMC. Se leen los formatos
anteriores soportados con valores de recuperación definidos: los latches que nunca se
almacenaron no se pueden reconstruir. Los estados se toman al terminar instrucciones,
sin reanudación del interior de una instrucción o DMA. La lectura de un formato anterior
en el programa nuevo no promete lectura de formatos nuevos en programas anteriores.
[Alcance y pruebas por procesador](PRECISION-CPU.md).

## Rebobinado y reproducción de controles

- Historial optativo, en memoria: hasta 64 MB y 600 estados, aproximadamente dos por
  segundo. El tiempo disponible depende del tamaño del estado y la pantalla.
- Una grabación de hasta cinco minutos conserva el estado inicial, entradas y hashes
  de pantalla. Ignora controles reales durante reproducción y se pausa al discrepar.
  No registra cambios externos, sesiones de IA, herramientas ni configuración de hardware.
- Solo originales con estados guardados; no fusiones ni exportación de películas.
  Empezar una grabación libera teclas y cancela escritura pendiente. El depurador,
  reinicio y carga de estado cancelan la captura; abrir otro medio también descarta la
  grabación terminada. [Guía](REBOBINADO.md).

## Máquinas

### IBM PC/XT

- Disponible en app y CLI con BIOS libre y FreeDOS incluidos para EXE/COM y ZIP;
  también admite IMG/IMA de 160–720 KB. El juego y sus datos, más DOS, deben entrar
  en 720 KB. Los programas sueltos toman datos de su carpeta; si contiene muchos otros
  programas, separá el juego en su propia carpeta o ZIP. Los nombres de datos deben
  ser DOS 8.3; las carpetas largas reciben alias internos. No se ejecuta código en el host.
- 8088 con prelectura y reloj T; los totales EU siguen una tabla nominal. El contraste
  externo comprobó estado funcional y accesos/intervalos relativos de movimientos, E/S
  y lectura/modificación/escritura de memoria, CMPS, STOS y LODS; también el orden de
  accesos de INT y de los errores de división. Los 20.000 casos de cadenas coinciden
  en accesos y 1.045.870 pares de intervalos, incluidos los arranques de REP.
  LES/LDS y PUSH de memoria coinciden en 30.000 casos y 84.994 pares de intervalos.
  POP de memoria, CALL lejano directo, RETF e IRET coinciden en otros 50.000 casos y
  170.000 pares. Llamadas/saltos indirectos coinciden en 40.000 casos y 132.437 pares;
  INT/INTO/AAM/DIV/IDIV coinciden en los intervalos de 80.000 casos y 488.394 pares.
  De estos últimos, 25.049 conservan diferencias solo en flags indefinidos apilados.
  No valida reloj absoluto
  ni prelectura; verificar-bus informa cada diferencia por separado.
  READY permite esperas lógicas en memoria/E/S/prelectura, probadas con buses propios;
  no se certifica su sincronización eléctrica ni la fase de INTA.
  La variante 8086
  existe como núcleo/API y diagnóstico, con bus de 16 bits y cola de seis bytes,
  y contraste funcional propio (552.744 correctos y 93.256 excluidos). Coinciden tipos,
  direcciones, orden y 1.475.052 pares de intervalos de datos. Conserva 4.787 casos con
  flags indefinidos distintos. La BIU y las correcciones de fase de indirectos son un
  modelo aproximado: no validan reloj absoluto ni cola final. Sin placa 8086 en la
  biblioteca. PC/XT usa 8088; sin 8087/AT.
- CGA RGBI, 83 teclas, altavoz y capa privada de disco incluida en guardados completos.
  Sin EGA/VGA, disco duro, AdLib, Sound Blaster, serie ni impresora. Los caracteres
  extendidos necesitan una fuente completa aportada por API.
- PIT binario 0/2/3; siguen pendientes 1/4/5, BCD y el caso 3 en modo 3 del 8253.
  PIC individual por flancos y DMA básico; faltan cascada y varios modos opcionales.
- Código, memoria y puntos de interrupción usan direcciones físicas de 20 bits. Los
  perfiles y el análisis de variables siguen siendo de 16 bits: PC no se ofrece todavía
  para preparar o fusionar. [Detalle de placa](PCXT.md), [uso](PC-DOS.md).

### ZX Spectrum

- Los estados en formato 2 conservan la demora tras EI y la NMI pendiente. Se acepta el
  formato 1, que las restaura en falso porque no las almacenaba. No se recupera la fase
  de interrupción perdida en un guardado antiguo; no se certifican todas las señales del Z80.
- Solo el modelo 48K. El software que necesita paginación de 128K o AY no es compatible
  con este modelo. Spectrum 128K sigue previsto.
- Una protección que pide códigos u otro material original no implica un fallo del cargador.
- Reconocer una cinta multicarga no demuestra que todas sus partes hayan sido comprobadas.
- La parada automática de la cinta (ver ARQUITECTURA, "Cinta") supone que un juego que
  no lee el puerto $FE durante un segundo y medio no está cargando. Un cargador propio que
  pase ese tiempo sin leer la cinta justo antes de un bloque de velocidad normal la
  encontraría detenida (no se vio en ninguna cinta de la carpeta). Con "pulse una tecla"
  la cinta sigue sonando, como en un casete real: si el juego pide la parte siguiente
  tarde, hay que rebobinar.
- Las cargas por partes, intros y controles no incluidos en la sonda necesitan un guion
  curado. Un resultado sin respuesta no identifica por sí solo un fallo de emulación.

### Placa arcade 8080

- Los estados en formato 2 conservan la demora tras EI. El formato 1 sigue admitido y
  la inicializa en falso; no puede reconstruirse una demora que nunca se guardó.
- Sin samples de MAME, los sonidos son sintetizados (aproximan los circuitos analógicos).
- Otros juegos de esta placa no están soportados.

### Commodore 64

- El formato 2 del estado agrega la muestra NMI del 6510. El formato 1 sigue admitido
  con la decisión NMI pendiente anterior. La alineación de CPU/RAM/periféricos y la siguiente
  instrucción se comprueban con un fixture propio; no con todos los guardados existentes.
  La aceptación de interrupciones al recuperar RDY y las medias fases siguen sin certificar.
- Solo PAL. Carga `.t64` y `.prg`; los `.d64`, `.crt` y `.tap` de pulsos se reconocen y se
  explican, pero no cargan (T-032). Sin disquetera 1541.
- Con Open ROMs, los juegos que llaman rutinas internas del KERNAL no cubiertas fallan;
  la barra de estado avisa y Juego > Controles y hardware lista las llamadas no cubiertas.
  La cobertura comprobada depende de cada edición.
- La VIC-II dibuja por línea completa: efectos que cambian registros a mitad de línea
  (FLI, apertura de bordes laterales, divisiones de color en la misma línea) no salen
  exactos (probable).
- SID: las formas de onda combinadas se aproximan con AND, el filtro es un modelo de
  estado variable simple y las envolventes son lineales; solo el modelo 6581 (probable
  diferencia de timbre con el hardware).
- CIA: sin puerto serie (registro SDR sin desplazamiento) ni entrada CNT (probable: afecta
  a muy pocos juegos).
- Joystick: las flechas manejan un solo puerto (2 por defecto); se puede elegir el puerto 1
  en Juego > Controles y hardware. La elección depende del programa.
- Las ediciones con intros o trainers pueden requerir pasos antes de la partida.
  Sus secuencias de prueba no forman parte de un arranque universal.

### Nintendo NES (T-031)

- Corre en la app, el CLI y la matriz: CPU 2A03, PPU, APU, OAM DMA, dos mandos,
  PRG RAM y estados guardados. La compatibilidad depende también del mapper y del
  uso de vídeo, sonido y periféricos de cada cartucho; reconocerlo no acredita jugabilidad.
- APU: el DMC roba tres/cuatro ciclos por byte y comparte el bus con OAM. La activación
  espera dos/tres ciclos según paridad; quedan abortos, reinicios finos y conflictos
  de registros internos. Los registros de
  longitud no tienen la demora de un ciclo frente al contador
  de cuadros y no hay filtros de salida aparte del pasa altos (el sonido es algo más
  brillante que en una consola). Los estados guardados de la versión sin sonido no se
  pueden cargar. El formato NES7 conserva la página OAM, la espera DMC, la muestra NMI
  del núcleo y la historia de integración/filtro de audio; acepta NES2–6 con sus restricciones
  de edición y mapper. Las versiones
  anteriores no contienen toda esa información y usan los valores de recuperación
  documentados. [Detalle de DMA y esperas](NES-DMA.md), [formatos de CPU](PRECISION-CPU.md).
  El ciclo de encendido reinicia las fases de los cinco canales; el primer período del
  DMC se espera completo. Esto no certifica la temporización física del reinicio suave.
- Mappers 0/1/2/3/4/7: NROM, MMC1, UxROM, CNROM, MMC3 y AxROM. Los demás se reconocen y se
  explica por qué no cargan. MMC1 admite bancos PRG/CHR, puerto serie, espejado,
  PRG RAM de 8 KB y la selección exterior SUROM. No admite MMC1A, EEPROM ni RAM mayor;
  la selección exterior con registros CHR divergentes en modo 4 KB todavía no se sigue
  por acceso PPU. El control adicional de RAM de SNROM no está modelado. Los tamaños
  ampliados NES2 y submappers MMC1 ajenos a 0/5 se rechazan.
- MMC3 (mapper 4, submapper 0): bancos PRG de 8 KB, CHR de 1/2 KB, espejado,
  protección de RAM e IRQ por flancos A12 del bus PPU. El calendario de lecturas es
  independiente del dibujo por línea; los accesos del depurador no generan IRQ.
  El filtro aproxima tres flancos M2 con diez puntos PPU: no reproduce la fase exacta
  CPU/PPU ni la revisión MMC3A. MMC6 y las placas con RAM ampliada se rechazan.
- La app guarda la batería PRG de 8 KB entre sesiones y la conserva al reiniciar.
  Soporta iNES con un banco y NES 2.0 con 8 KB PRG NVRAM, sin PRG RAM volátil ni CHR
  NVRAM. Otras variantes muestran un aviso y siguen necesitando estados guardados.
  Fusiones, ensayos y CLI no comparten este archivo. No importa archivos .sav de otros
  emuladores. Ante un cierre abrupto se pueden perder los últimos dos segundos; errores
  de almacenamiento y conflictos entre instancias impiden el reemplazo silencioso.
  [Guardado, copias y recuperación](BATERIA-NES.md).
- Sin segundo jugador en la app (el mando 2 existe para el CLI y las fusiones) ni Zapper.
- La sonda automática no sustituye un guion de arranque curado para intros, selección
  de controles u otros menús del cartucho.
- OAM espera una lectura y alinea513/514 ciclos; DMC tiene prioridad en GET. Quedan
  los bordes del solapamiento, variantes Famicom/PAL y el momento preciso de recuperar
  interrupciones. Las pruebas propias no sustituyen la suite externa específica.
- PPU: cada línea visible se dibuja entera en su punto 1 con el scroll de ese momento.
  Las divisiones de pantalla hechas en el borrado horizontal o con sprite 0 funcionan;
  los cambios de scroll, paleta o patrones en mitad de una línea no se ven hasta la
  siguiente. El desbordamiento de sprites no reproduce el error de evaluación del 2C02
  (marca el noveno sprite de verdad), no hay colores de énfasis de $2001, la carrera de
  leer $2002 justo al empezar el vblank no está y la paleta es una aproximación común del
  2C02 (no medida).
- CNROM: no se emulan los conflictos de bus al elegir banco (los juegos bien hechos no
  dependen de eso).
- [nestest](NESTEST.md) autorizado y contrastado localmente: 8991 estados de CPU y
  ciclos coincidentes. No certifica PPU/APU/DMA ni todas las variantes de instrucciones.
  El corpus es opcional y no se distribuye con el programa.

### Matriz de compatibilidad

- Los guiones descubiertos automáticamente no tienen pasos ni comprobaciones: el nivel
  "título" y "jugable" solo existen en los curados. "Responde a los controles"
  significa que alguna entrada cambió la imagen, no que el juego se pueda jugar.
- La sonda prueba una lista fija de entradas por máquina; un juego que espera otra tecla
  figura como que no responde.

### Laboratorio de ingeniería inversa

- El puntaje, el nivel y los objetos necesitan una acción que los haga cambiar.
  Preparar puede usar la partida de ensayo y experimentos de controles dirigidos al
  objetivo. Si no provocan el cambio necesario, ese dato queda sin identificar.
- Los experimentos de IA son planes de teclas con límites de cantidad y cuadros, no
  programas ni escrituras libres. Sus resultados son candidatos que deben contrastarse.
  Una escritura con efecto no demuestra el significado de una variable.
- El sondeo de acciones NES busca relaciones observables entre RAM, OAM y controles,
  incluso salto o segunda acción. Puede no hallar una relación estable. Encontrarla
  no acredita una entidad física, coordenadas de mundo ni colisiones del original.
- Los nombres son hipótesis aunque la variable esté verificada: un contador de tiempo
  puede confundirse con vidas porque ambos bajan de a uno. La verificación prueba que
  el juego usa el valor, no qué significa.
- El ancho de un puntaje ASCII o de dígitos se confirma leyendo la pantalla; en máquinas
  sin texto legible (NES, arcade) se supone de 6 dígitos o de los bytes que cambiaron.
- Las tablas de entidades donde todos los bytes se mueven (registros de solo X e Y) se
  descartan como buffers.
- Las vidas solo aparecen si el personaje muere durante la observación o la partida al
  azar.

## Fusiones y estudio

- Fusión y creación con originales tienen entradas y categorías separadas. Los juegos
  anteriores con escena propia mantienen su categoría. Esta
  separación no certifica que una generación nueva conserve la identidad o sea divertida.

- El rango de tokens es orientativo y local; no incluye imágenes, salida ni el contexto
  que agregue cada CLI. El tope de texto limita caracteres de entrada, no dinero ni cuota.
  Los tokens informados dependen del proveedor; si faltan se indican como desconocidos o
  parciales. El modo económico reduce reintentos automáticos, no los requisitos del juego.

- Los trabajos automáticos de escritura y revisión conservan un pendiente local y pueden
  continuar sin repetir una respuesta ya completa. Si cambian ROMs, perfiles o firmware,
  se detienen; el borrador se puede copiar. El creador desde cero también recupera su código
  y vuelve a probarlo con el hardware actual. El intercambio manual o por carpeta de
  agentes conserva su propio flujo de archivos.

- El estudio permite elegir 2 o 3 juegos; todavía falta el presupuesto de recursos para N juegos. T-023.
- Los perfiles incluidos (`Retro.Fusion/Profiles`) se asocian por nombre de archivo; los
  que hace "Preparar" van por la huella SHA-256 de la copia (otra edición necesita su
  propio perfil). T-053.
- Un perfil básico no prueba que existan los datos para la idea. **Preparar y crear
  esta fusión** puede arrancar y jugar los originales, probar entradas y actualizar sus
  perfiles sin exigir una partida manual previa. Un perfil parcial también puede
  revisarse; no basta con que ya exista en el catálogo.
- La preparación puede investigar en varias rondas y retomar su partida privada,
  dentro de un trabajo de hasta una hora que también incluye escritura y comprobación.
  La estimación previa es orientativa; no obliga a consumir el tiempo completo.
  Aprende de tres creaciones guardadas comparables o usa un intervalo general cuando
  faltan muestras. No es una duración garantizada ni una medición de cada proveedor.
  El reloj total no se reinicia al cambiar de etapa.
  Puede no atravesar una intro, completar una secuencia o descubrir datos útiles.
  Se detiene si no aparecen pruebas ni datos nuevos, si vence el tiempo o ante errores;
  conserva los perfiles de las rondas completadas. El punto privado de investigación
  no persiste al cerrar la aplicación. [Detalles](PREPARAR-FUSION.md).
  Sin un proveedor automático, la revisión final se intercambia con el chat o agente.
  La disponibilidad del CLI depende de su instalación y sesión.
- La revisión conserva idea, base y pedido. Solo continúa la creación si se respaldan
  los requisitos; si falta algo, mantiene el motivo y permite reintentar. No garantiza
  descubrir mapas, cajas de choque, cámara, daño, progresión ni todas las reglas.
  Cambiar originales, perfiles, selección o configuración durante el trabajo puede
  invalidarlo; también se comparan archivos, perfiles y firmware antes de una creación
  directa y de guardar. La copia privada evita mezclar datos durante la escritura;
  un rechazo conserva el borrador. Cancelar llega a las pruebas de los robots y no
  habilita la creación ni provoca reintentos por sí solo.
- El detector de partida puede confundir un cursor de menú que se mueve con una entidad
  jugable. La IA o una comprobación manual deben confirmar que llegó a la partida.
- Los perfiles solo describen variables, controles y recursos que se comprobaron;
  no todos incluyen puntaje, posición o mapas. Un nombre genérico no prueba su semántica.
- La fusión clásica y sus vistas superpuestas se retiraron. El modo predeterminado
  conserva un original base; `screen.base` solo admite su ranura declarada. La opción
  libre conserva el dibujo por sprites/tiles. `screen.game` sigue fallando, incluso con
  catch, y no se permite pegar otra pantalla completa. Estas restricciones no detectan
  identidad completa ni certifican integración mecánica. Algunas comparaciones de imagen
  detectan borrado general o una copia casi exacta de otro original; son un filtro técnico.
  Mantener o ampliar mapas necesita
  sus formatos y datos verificados; elegir una ROM no equivale a conocerlos.
- Los ejemplos clásicos que pegaban pantallas completas ya no se ofrecen. Los archivos
  guardados por el usuario se conservan; no se convierten automáticamente a una fusión
  con integración de reglas o escena propia.
- El modo base permite empezar jugando y no exige puntaje artificial. Consultar datos
  de todos los aportantes es una comprobación técnica mínima, no evidencia de que sus
  mecánicas estén bien integradas. Hay que comparar capturas y probar controles/objetivos.
- El validador de jugabilidad comprueba estados (inicio, empezar, puntaje cuando corresponde, muertes,
  reinicio, pantalla congelada) con 3 robots; no detecta todavía controles sin efecto,
  reglas en bucle, audio saturado ni pantallas ilegibles. T-026. No mide diversión.

## Creador de juegos (T-060)

- Las instrucciones de diseño son comunes a proveedores/CLI, copiar y pegar y carpetas
  para agentes. Su entrega y la conservación del pedido se prueban con IA simulada;
  eso no mide la creatividad ni la calidad de una generación real.
- El creador compara hasta cinco resúmenes previos de la misma máquina; no recibe sus
  programas ni analiza automáticamente rutinas de ROMs. Ese análisis y su comprobación
  anti-copia siguen pendientes. Los perfiles de fusión solo aportan los datos declarados.
- Los textos nuevos se piden breves en las instrucciones y esquemas. Los juegos antiguos
  o las respuestas manuales pueden conservar textos más largos; no se recortan sus controles.
- Hay plantillas para el C64 y el Spectrum 48K; NES y Space Invaders todavía no.
- Los ensambladores no tienen macros, `include`, ensamblado condicional ni segmentos: un
  archivo, un origen.
- La plantilla del Spectrum lee el Kempston en el puerto $1F; en un Spectrum real sin la
  interfaz ese puerto devuelve basura y el bloque se movería solo. El emulador siempre
  tiene el Kempston conectado.
- La plantilla del C64 mueve el sprite solo en X de 0 a 255 (no usa el bit 9 de $D010).
- Con copiar y pegar, la IA del chat no ve la imagen de la prueba (solo el informe en texto;
  el jugador ve la pantalla en el emulador).
- Las pruebas automáticas del creador usan IA simulada y programas de referencia.
  Esto no certifica la calidad de una generación real ni de todos los proveedores.
- La prueba no juzga si el juego es divertido ni si cumple el pedido: mira que arranque,
  dibuje, responda a los controles y aguante 30 segundos de juego al azar. Un juego que se
  termina solo (sin controles) o que se maneja con teclas que el robot no conoce no pasa.
- La guía del C64 pide no usar el KERNAL porque el emulador puede tener las Open ROMs; con
  las ROMs originales funcionaría, pero la prueba no lo sabe.

## Favoritos y recientes

- Se muestran juegos disponibles en las bibliotecas actuales. Un archivo abierto fuera
  de ellas aparecerá al agregarlo a la biblioteca. No se guarda una copia del juego.
- Recientes conserva 40 entradas y vuelve a cargar el juego; no reanuda una partida
  guardada. Los favoritos admiten hasta 4096 marcas.
- Mover o renombrar un original conserva su identidad si el contenido y sistema no
  cambian. Reempaquetarlo en un ZIP de un solo juego también; agregar otras ediciones
  al ZIP cambia su identidad. Las copias idénticas comparten la marca.
- Creados anteriores sin WorkId se identifican por fecha de creación y sistema.
  Editar esa fecha en sus metadatos puede cambiar la identidad.
- Los datos son locales a la cuenta del sistema operativo. Un archivo ilegible o con
  un formato futuro se conserva intacto y la app avisa que los cambios de esa sesión
  no se guardarán. Si dos instancias escriben a la vez, prevalece la última.

## App, CLI y paquetes

- **Conexión de IA:** los catálogos dependen de la versión instalada y de lo que la
  herramienta publique. Codex y Claude informan esfuerzos por modelo; Antigravity los
  ofrece como variantes. Gemini CLI anterior no tiene catálogo integrado compatible.
  Ollama informa modelos instalados, sin niveles de esfuerzo en ese listado. Un catálogo
  no certifica cuota ni una generación exitosa. No se instalan herramientas ni se inicia
  sesión automáticamente. Ver `CONECTAR-IA.md`.
  Probar conexión se puede cancelar; también se solicita cancelación al cambiar de
  proveedor o cerrar la app. Un envío terminado al proceso local no confirma recepción
  del servidor ni una respuesta. Un plazo agotado sin métricas deja el consumo desconocido.

- **Construcción con IA:** el panel muestra actividad observada del proceso y la salida
  que el CLI emite. No revela razonamiento interno ni puede obtener texto que la herramienta
  todavía no envió. Puede mostrar JSON o código según el formato del CLI. Conserva solo
  los últimos 12.000 caracteres en memoria; no crea un registro de conversaciones en disco.
  El tiempo transcurrido está disponible; el estimado restante por operación requiere
  al menos tres trabajos completos comparables. En la creación de fusiones se ofrece
  además una orientación general para el trabajo completo cuando faltan muestras.
  Los modos manual y carpeta requieren respuestas y no predicen cuánto tardará el chat
  o agente externo.
  El arranque posterior tiene cancelación y un plazo de dos minutos. Su vencimiento o un
  error de inicialización conserva el juego guardado; no implica que los originales o
  todas las mecánicas estén comprobados. El esquema estructurado de respuesta reduce
  errores de formato, pero no asegura que el modelo responda dentro del plazo.

- En el C64, F1/F3/F5/F7 son teclas de la máquina: la carga rápida de estado pasa a F4 y
  el reinicio se hace desde Juego.
- Los estados guardados de la app corresponden a originales. Una fusión necesita guardar
  también el guion y todas sus máquinas; hasta implementar ese formato, estas opciones
  están deshabilitadas durante una fusión. Se puede reiniciar desde el estudio.
- Los estados NES nuevos validan la edición del cartucho. Los anteriores de NES y
  los de otras máquinas requieren usar el mismo juego y firmware con que se guardaron.
- `retrofusion/` (kit Python inicial) no tiene su punto de entrada `retro.py`: no se puede
  correr tal cual.

## Salida de sonido

- La salida actual es mono a 44,1 kHz y usa el dispositivo predeterminado del sistema.
  Volumen y margen no agregan canales ni dispositivos a las máquinas emuladas.
- Un margen amplio puede reducir cortes por pausas breves, a cambio de más demora.
  No compensa un equipo que no sostenga la emulación.
- El silencio al perder el foco no pausa la partida. [Opciones de sonido](SONIDO.md).

## Interfaz y apertura

- **Idioma y recuperación posteriores a 0.3.7:** hay catálogo local es/en para la
  interfaz, comandos, CLI e informes, con guías de usuario y diseño en inglés.
  El contenido de juegos, títulos personales, rutas, código y documentos históricos
  conserva su idioma. Los detalles de excepciones del sistema pueden conservar el
  idioma de Windows. [Guía en inglés](USER-GUIDE-EN.md).
- **Arranque de emergencia:** Windows puede consultar y preparar una actualización
  sin cargar la interfaz gráfica ni audio. Sigue requiriendo un ejecutable y runtime
  que el sistema pueda cargar, un manifiesto válido, permisos de escritura y una
  versión posterior disponible. No resuelve por sí mismo la falta de OpenGL ni repara
  cualquier instalación dañada. Windows 10, Linux gráfico y la PC de los avisos de audio
  todavía requieren una comprobación propia. [Alcance y uso](ARRANQUE-Y-RECUPERACION.md).
- **Pruebas ocultas:** --autotest fuerza proveedores de IA deshabilitados y usa carpetas
  de fusiones, creaciones y perfiles junto a su evidencia. No debe heredar bibliotecas
  del jugador al buscar en las carpetas superiores.

- La tipografía usa fuentes instaladas o la incorporada de ImGui; puede variar entre sistemas.
- **Tamaño del texto y contraste:** 100 a 200 % en pasos de 25. Escalan menús, barras,
  biblioteca, diálogos, depurador, estudio, creador, código, selector de archivos y
  ventanas de IA y actualización. Una ventana ya ubicada conserva su tamaño guardado en
  imgui.ini; el depurador se reacomoda con Herramientas > Depuración > Reorganizar
  paneles. Con 175–200 % en una ventana chica los paneles necesitan desplazamiento.
  El alto contraste no cambia los colores de la pantalla emulada.
  [Preferencias y teclado](PREFERENCIAS.md).
- **Capturas:** PNG a la resolución de la máquina (con su borde), sin escalar ni filtros.
  La miniatura solo se ofrece para originales de la biblioteca y nunca reemplaza una imagen
  existente. Se verificaron el PNG decodificado y sus CRC con una captura real oculta; la
  opción de menú y el atajo no se pulsaron en una sesión visible.
- **Lectores de pantalla:** la interfaz se dibuja con Dear ImGui y no publica sus controles
  al sistema operativo; un lector de pantalla no puede recorrerla.
- **Teclado:** la navegación con Tab, flechas, Enter y Esc funciona en la biblioteca, los
  diálogos, sin máquina y con la partida en pausa; mientras un juego corre, el teclado es
  de la máquina emulada. Alt izquierdo lleva a la barra de menús en esos mismos estados.
  Esta navegación se verificó en la lógica y en capturas ocultas; no hubo una sesión
  visible con teclado real.
- **Ahorro de energía:** sin partida en curso la ventana se dibuja hasta 60 veces por
  segundo y 15 sin foco. Medido con la ventana oculta, que el sistema informa con foco:
  la biblioteca pasó de 120 a 60 cuadros por segundo y de 0,12 a 0,09 s de CPU por
  segundo; con la máquina en marcha no cambió. El caso sin foco se comprueba en pruebas
  de la regla, no con una medición nativa. Minimizada, raylib 5.5 espera eventos sin
  dibujar (no se usa FLAG_WINDOW_ALWAYS_RUN), así que la emulación se detiene hasta
  restaurar la ventana, como antes de estas preferencias. Esto sale del código de raylib;
  no se midió, porque minimizar la ventana de prueba la haría visible.
- **Mando y salida:** la navegación con mando usa el primer mando y la traducción de
  rlImGui; no se probó con un mando físico. Después de Select + Start el juego no recibe
  el mando hasta soltar todos los botones, pero Select ya llegó mientras se mantenía
  apretado antes de Start. La confirmación de salida se comprobó con una orden de cierre
  real sin trabajos en curso (sale de inmediato); el diálogo con un trabajo en curso solo
  tiene pruebas de su texto, porque iniciarlo requiere un proveedor de IA real.
- El selector de archivos refresca su instantánea cada dos segundos mientras está abierto.
  Conserva el filtro entre cuadros y dibuja solo las filas visibles; la velocidad también
  depende del disco y del acceso a la carpeta.
- La elección de ejecutable de PC se hace al abrir cada ZIP; no recuerda una selección
  anterior. Conserva subcarpetas y datos, sujetos al límite de 720 KB junto con DOS.
- En el CLI, --pick selecciona un archivo; una placa arcade que necesita varios chips
  requiere el conjunto completo.

## Contenido excluido

Por pedido del usuario no se usan: contenido pornográfico, *Fundamentally Loathsome* y,
por cautela, las ediciones de Cheril, Phantomasa, Sila Tsikl y Nanako Descends to Hell
(retiradas en la auditoría de ROMs del 18/10/2026).
