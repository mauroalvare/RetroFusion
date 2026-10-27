# Limitaciones y errores conocidos

Alcance implementado y comportamientos pendientes. Las comprobaciones por edición
de un juego no certifican compatibilidad universal. Cuando se indica **probable**,
se trata de una limitación deducida que todavía necesita una prueba específica.

## Análisis y modificaciones de partidas

- El significado de una variable propuesta por IA es una hipótesis. Ver escrituras y
  código no demuestra que su modificación produzca el efecto descrito.
- El pedido lleva hasta 128 bytes candidatos y 160 instrucciones, a partir de una
  observación acotada a 2.048 direcciones. No es un análisis completo de la ROM.
  Si faltan datos, jugá con la ventana abierta y formulá una pregunta más concreta.
- Las nuevas variables admiten bytes o palabras little endian en RAM de trabajo.
  Se excluyen firmware, hardware y regiones reservadas de dirección fija; una pila
  dinámica o código en RAM pueden entrar en la evidencia. Las áreas conservadoras
  también pueden dejar afuera variables válidas. Las herramientas manuales siguen disponibles.
- Las hipótesis pueden conservarse en el historial privado; no se guardan como perfiles verificados.
  Deshacer conserva los cinco cambios más recientes; restaurar conserva el primero.
  Ambos restauran el progreso de partida desde ese punto. Abrir, recargar o reiniciar
  un juego o cargar un estado externo descarta los puntos de regreso de esa sesión;
  el historial optativo y la selección guardada se pueden recuperar.
- La comprobación nueva usa IA simulada. No demuestra que todos los proveedores
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

## Máquinas

### IBM PC/XT

- Disponible en app y CLI con BIOS libre y FreeDOS incluidos para EXE/COM y ZIP;
  también admite IMG/IMA de 160–720 KB. El juego y sus datos, más DOS, deben entrar
  en 720 KB. Los programas sueltos toman datos de su carpeta; si contiene muchos otros
  programas, separá el juego en su propia carpeta o ZIP. Los nombres de datos deben
  ser DOS 8.3; las carpetas largas reciben alias internos. No se ejecuta código en el host.
- 8088 con prelectura y reloj T; los totales EU siguen una tabla nominal. El contraste
  externo comprobó estado funcional y accesos/intervalos relativos de movimientos, E/S
  y lectura/modificación/escritura de memoria; también el orden de accesos de INT y de
  los errores de división. Cadenas, LES/LDS, PUSH/POP de memoria, llamadas/retornos
  lejanos, IRET, llamadas indirectas, INT y errores de división conservan diferencias de
  intervalos, y los flags indefinidos apilados no se reproducen. No valida reloj absoluto
  ni prelectura; verificar-bus informa cada diferencia por separado.
  La variante 8086
  existe como núcleo/API y diagnóstico, con bus de 16 bits y cola de seis bytes; aún
  sin contraste externo propio ni placa 8086 en la biblioteca. PC/XT usa 8088; sin 8087/AT.
- CGA RGBI, 83 teclas, altavoz y capa privada de disco incluida en guardados completos.
  Sin EGA/VGA, disco duro, AdLib, Sound Blaster, serie ni impresora. Los caracteres
  extendidos necesitan una fuente completa aportada por API.
- PIT binario 0/2/3; siguen pendientes 1/4/5, BCD y el caso 3 en modo 3 del 8253.
  PIC individual por flancos y DMA básico; faltan cascada y varios modos opcionales.
- Código, memoria y puntos de interrupción usan direcciones físicas de 20 bits. Los
  perfiles y el análisis de variables siguen siendo de 16 bits: PC no se ofrece todavía
  para preparar o fusionar. [Detalle de placa](PCXT.md), [uso](PC-DOS.md).

### ZX Spectrum

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

### Placa Space Invaders

- Sin samples de MAME, los sonidos son sintetizados (aproximan los circuitos analógicos).
- Otros juegos de la placa (Part II, Lunar Rescue) no están soportados.

### Commodore 64

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
- APU: la lectura de muestras del DMC no le roba ciclos a la CPU (en el chip son hasta 4
  por byte), los registros de longitud no tienen la demora de un ciclo frente al contador
  de cuadros y no hay filtros de salida aparte del pasa altos (el sonido es algo más
  brillante que en una consola). Los estados guardados de la versión sin sonido no se
  pueden cargar (formato 2).
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
- La RAM de cartuchos con batería se conserva en estados guardados, sin archivo de
  batería independiente entre sesiones todavía.
- Sin segundo jugador en la app (el mando 2 existe para el CLI y las fusiones) ni Zapper.
- La sonda automática no sustituye un guion de arranque curado para intros, selección
  de controles u otros menús del cartucho.
- La DMA de OAM no se alinea con los ciclos de lectura y escritura del 2A03 al detalle
  (uno o dos ciclos de espera según la paridad del ciclo).
- PPU: cada línea visible se dibuja entera en su punto 1 con el scroll de ese momento.
  Las divisiones de pantalla hechas en el borrado horizontal o con sprite 0 funcionan;
  los cambios de scroll, paleta o patrones en mitad de una línea no se ven hasta la
  siguiente. El desbordamiento de sprites no reproduce el error de evaluación del 2C02
  (marca el noveno sprite de verdad), no hay colores de énfasis de $2001, la carrera de
  leer $2002 justo al empezar el vblank no está y la paleta es una aproximación común del
  2C02 (no medida).
- CNROM: no se emulan los conflictos de bus al elegir banco (los juegos bien hechos no
  dependen de eso).
- Sin `nestest.nes` (prueba estándar de la CPU): hay que descargarla y espera la
  confirmación del usuario.

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

- **Construcción con IA:** el panel muestra actividad observada del proceso y la salida
  que el CLI emite. No revela razonamiento interno ni puede obtener texto que la herramienta
  todavía no envió. Puede mostrar JSON o código según el formato del CLI. Conserva solo
  los últimos 12.000 caracteres en memoria; no crea un registro de conversaciones en disco.
  El tiempo transcurrido está disponible; el estimado restante por operación requiere
  al menos tres trabajos completos comparables. En la creación de fusiones se ofrece
  además una orientación general para el trabajo completo cuando faltan muestras.
  Los modos manual y carpeta requieren respuestas y no predicen cuánto tardará el chat
  o agente externo.

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

- La tipografía usa fuentes instaladas o la incorporada de ImGui; puede variar entre sistemas.
- **Tamaño del texto y contraste:** 100 a 200 % en pasos de 25. Escalan menús, barras,
  biblioteca, diálogos, depurador, estudio, creador, código, selector de archivos y
  ventanas de IA y actualización. Una ventana ya ubicada conserva su tamaño guardado en
  imgui.ini; el depurador se reacomoda con Herramientas > Depuración > Reorganizar
  paneles. Con 175–200 % en una ventana chica los paneles necesitan desplazamiento.
  El alto contraste no cambia los colores de la pantalla emulada.
  [Preferencias y teclado](PREFERENCIAS.md).
- **Lectores de pantalla:** la interfaz se dibuja con Dear ImGui y no publica sus controles
  al sistema operativo; un lector de pantalla no puede recorrerla.
- **Teclado:** la navegación con Tab, flechas, Enter y Esc funciona en la biblioteca, los
  diálogos, sin máquina y con la partida en pausa; mientras un juego corre, el teclado es
  de la máquina emulada. Alt izquierdo lleva a la barra de menús en esos mismos estados.
  Esta navegación se verificó en la lógica y en capturas ocultas; no hubo una sesión
  visible con teclado real.
- **Ahorro de energía:** sin partida en curso la ventana se dibuja hasta 60 veces por
  segundo, 15 sin foco y 5 minimizada. Medido con la ventana oculta, que el sistema informa
  con foco: la biblioteca pasó de 120 a 60 cuadros por segundo y de 0,12 a 0,09 s de CPU
  por segundo; con la máquina en marcha no cambió. Los casos sin foco y minimizada se
  comprueban en pruebas de la regla, no con una medición nativa.
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
