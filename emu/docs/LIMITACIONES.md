# Limitaciones y errores conocidos

Registro de lo que no funciona o funciona a medias. Cada entrada dice si es **observado**
(se vio en una prueba) o **probable** (se deduce del código, falta comprobarlo). Al
arreglar algo, se borra la entrada y se menciona en el commit.

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
  cambios; si falta acceso, se pueden volver a buscar desde Inicio. Si el sistema no permite
  vigilar la carpeta, Actualizar sigue disponible. No hay descompresión de RAR/7z.
- Si una colisión requiere cambiar un nombre, `.retroemu/identidades.json` conserva el
  identificador del perfil básico para las fusiones. Hay que conservar esa carpeta al
  trasladar la biblioteca. Las imágenes locales con el mismo nombre acompañan al juego
  cuando pueden moverse; una imagen bloqueada se conserva en su lugar.

## Modificaciones de la partida

- Las ideas de cambios se basan en variables ya conocidas; no equivalen a descubrir
  automáticamente cualquier juego. Los efectos propuestos por IA requieren probarse.
  La integración nueva se verificó con IA simulada, sin comprobar una sesión real de CLI.
- El punto de regreso completo se ofrece para originales. Los cambios avanzados sobre
  una máquina de una fusión no restauran el guion JavaScript completo.
- Un perfil genérico por nombre puede corresponder a otra edición; se prioriza el hash
  cuando existe. Un perfil que enumera hashes ya no se reutiliza por nombre si no coincide.
- El congelado se repone por cuadro; no intercepta cada instrucción que escribe la variable.
  En la vista avanzada de fusiones se repone por tick compartido.

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
- Las referencias muestran el resultado del arranque configurado, que puede incluir una
  intro si el perfil está incompleto. No deducen automáticamente los mapas ni las reglas.
  Las CLI que admiten imágenes pueden leer los PNG; las API directas actuales solo reciben
  tamaño/paleta. En copia manual hay que adjuntar las imágenes al chat. La visión depende
  también del modelo elegido; el nombre de un archivo no prueba que la IA lo haya visto.

## Máquinas

### IBM PC/XT (T-038)

- Hay un núcleo 8088 parcial accesible por `cpu8088 demo`; todavía no hay máquina PC,
  BIOS/DOS ni juegos compatibles. Faltan instrucciones, desensamblador, prelectura y
  temporización del bus. No se verificó con una suite de hardware externa. Cobertura
  observada y próximos pasos en [I8088.md](I8088.md).

### ZX Spectrum

- Solo el modelo 48K. Los juegos 128K (4 de Mojon Twins en `descargados-autores\Spectrum-128K`)
  dan corrupción gráfica o reinicio en el 48K (observado en la auditoría de ROMs). Tarea T-030.
- Jet Set Willy pide la tarjeta de códigos (protección del juego, no fallo de carga).
- Turrican y otros multicarga: solo se comprobó la primera carga.
- La parada automática de la cinta (ver ARQUITECTURA, "Cinta") supone que un juego que
  no lee el puerto $FE durante un segundo y medio no está cargando. Un cargador propio que
  pase ese tiempo sin leer la cinta justo antes de un bloque de velocidad normal la
  encontraría detenida (no se vio en ninguna cinta de la carpeta). Con "pulse una tecla"
  la cinta sigue sonando, como en un casete real: si el juego pide la parte siguiente
  tarde, hay que rebobinar.
- Karateka llega a su pedido de la parte siguiente ("Pulse PLAY y espacio") pero la matriz
  todavía no lo hace avanzar: falta un guion curado con ESPACIO.
- Columns y Uwol de Mojon Twins no responden a ninguna de las 29 teclas de la sonda en el
  momento probado (están en una intro o esperan algo distinto): falta un guion curado.

### Placa Space Invaders

- Sin samples de MAME, los sonidos son sintetizados (aproximan los circuitos analógicos).
- Otros juegos de la placa (Part II, Lunar Rescue) no están soportados.

### Commodore 64

- Solo PAL. Carga `.t64` y `.prg`; los `.d64`, `.crt` y `.tap` de pulsos se reconocen y se
  explican, pero no cargan (T-032). Sin disquetera 1541.
- Con Open ROMs, los juegos que llaman rutinas internas del KERNAL no cubiertas fallan;
  la app lista esas direcciones en la barra de estado y en Máquina (observado: Pac-Man,
  Ghostbusters y Spy vs Spy ya no tienen ninguna sin cubrir).
- La VIC-II dibuja por línea completa: efectos que cambian registros a mitad de línea
  (FLI, apertura de bordes laterales, divisiones de color en la misma línea) no salen
  exactos (probable).
- SID: las formas de onda combinadas se aproximan con AND, el filtro es un modelo de
  estado variable simple y las envolventes son lineales; solo el modelo 6581 (probable
  diferencia de timbre con el hardware).
- CIA: sin puerto serie (registro SDR sin desplazamiento) ni entrada CNT (probable: afecta
  a muy pocos juegos).
- Joystick: las flechas manejan un solo puerto (2 por defecto, 1 en el menú). Spy vs Spy
  usa el puerto 1 para el jugador blanco.
- Los juegos crackeados de la carpeta (Pac-Man y Ghostbusters) tienen menús de trainer
  antes del juego: la secuencia de teclas está en `tests/C64.Tests/GameTests.cs`.

### Nintendo NES (T-031)

- Corre en la app, el CLI y la matriz: CPU 2A03, PPU, APU, OAM DMA, los dos mandos, PRG RAM
  y estados guardados. Cadaverion, Cave (CNROM), Cheman y Super Uwol llegan al título y
  entran al juego con START (capturas del CLI y de la app oculta, 18/10). Bootee no se
  prueba ni se documenta: la protagoniza Cheril (ver Contenido excluido).
- APU: la lectura de muestras del DMC no le roba ciclos a la CPU (en el chip son hasta 4
  por byte), los registros de longitud no tienen la demora de un ciclo frente al contador
  de cuadros y no hay filtros de salida aparte del pasa altos (el sonido es algo más
  brillante que en una consola). Los estados guardados de la versión sin sonido no se
  pueden cargar (formato 2).
- Solo mappers 0 y 3: un .nes con otro mapper se reconoce y se explica por qué no carga.
- Sin segundo jugador en la app (el mando 2 existe para el CLI y las fusiones) ni Zapper.
- La matriz no ve respuesta en Cave con la sonda automática (sí con START a mano en el
  CLI): falta un guion curado.
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
  "título" y "jugable" solo existen en los curados (6 hoy). "Responde a los controles"
  significa que alguna entrada cambió la imagen, no que el juego se pueda jugar.
- La sonda prueba una lista fija de entradas por máquina; un juego que espera otra tecla
  figura como que no responde.

### Laboratorio de ingeniería inversa

- El puntaje, el nivel y los objetos necesitan un experimento que los haga cambiar (en el
  guion, o la partida al azar de "Preparar"); un juego que no suma puntos jugando al azar
  queda sin puntaje.
- Los nombres son hipótesis aunque la variable esté verificada: en Spy vs Spy el minutero
  del reloj sale como "vidas" (baja de a uno y recarga los segundos). La verificación
  prueba que el juego usa el valor, no qué significa.
- El ancho de un puntaje ASCII o de dígitos se confirma leyendo la pantalla; en máquinas
  sin texto legible (NES, arcade) se supone de 6 dígitos o de los bytes que cambiaron.
- Las tablas de entidades donde todos los bytes se mueven (registros de solo X e Y) se
  descartan como buffers.
- Las vidas solo aparecen si el personaje muere durante la observación o la partida al
  azar.

## Fusiones y estudio

- El rango de tokens es orientativo y local; no incluye imágenes, salida ni el contexto
  que agregue cada CLI. El tope de texto limita caracteres de entrada, no dinero ni cuota.
  Los tokens informados dependen del proveedor; si faltan se indican como desconocidos o
  parciales. El modo económico reduce reintentos automáticos, no los requisitos del juego.

- Los trabajos automáticos de escritura y revisión conservan un pendiente local y pueden
  continuar sin repetir una respuesta ya completa. Si cambian ROMs, perfiles o firmware,
  se detienen; el borrador se puede copiar. La recuperación automática no cubre todavía
  el creador desde cero ni el intercambio manual o por carpeta de agentes.

- El estudio permite elegir 2 o 3 juegos; todavía falta el presupuesto de recursos para N juegos. T-023.
- Los perfiles incluidos (`Retro.Fusion/Profiles`) se asocian por nombre de archivo; los
  que hace "Preparar" van por la huella SHA-256 de la copia (otra edición necesita su
  propio perfil). T-053.
- Los juegos sin preparar se fusionan sin arranque: empiezan en su título o menú y el
  jugador o el guion los lleva a la partida; el guion todavía no conoce sus variables.
- "Preparar" sin IA solo pasa arranques simples (una tecla para empezar). Los cracks de
  C64 con intro, documentos y preguntas de trucos necesitan la IA o un agente; en esta PC
  no hay ningún CLI de IA con sesión iniciada, así que la app usa la prueba de teclas o la
  carpeta para agentes. Ghostbusters (C64) exige comprar trampas en la tienda y la maneja
  con el joystick del puerto 1; Spy vs Spy, un jugador, con el del puerto 2.
- El detector de partida acepta cualquier pantalla donde una dirección mueve algo cada vez más
  lejos: un cursor de tienda o de menú también lo cumple. Con una IA al mando, la IA tiene que
  confirmarlo; con la prueba de teclas sola, puede tomar un menú por la partida.
- Con Antigravity, Ghostbusters no llegó a la partida en 24 rondas (se trabó en la tienda);
  Codex y Claude sí. Cada consulta de Antigravity tarda hasta un minuto.
- Los perfiles preparados de Spy vs Spy, Ghostbusters y Cheman tienen posición y tiempo o
  vidas. Ghostbusters y Cheman quedaron sin puntaje (jugando al azar no suman puntos); el
  "score" de Spy vs Spy ($B15C) subió una sola vez y puede no ser el puntaje.
- Hay perfiles de Manic Miner (Spectrum), Space Invaders (arcade) y Pac-Man de C64 (vidas
  verificadas; X e Y son valores derivados, solo lectura). Pac-Man de Spectrum y los demás
  juegos de C64 todavía no tienen perfil. T-022.
- La fusión clásica y sus vistas superpuestas se retiraron. El modo predeterminado
  conserva un original base; `screen.base` solo admite su ranura declarada. La opción
  libre conserva el dibujo por sprites/tiles. `screen.game` sigue fallando, incluso con
  catch, y no se permite pegar otra pantalla completa. Estas restricciones no detectan
  identidad completa ni certifican integración mecánica. Algunas comparaciones de imagen
  detectan borrado general o una copia casi exacta de otro original; son un filtro técnico.
  Mantener o ampliar mapas necesita
  sus formatos y datos verificados; elegir una ROM no equivale a conocerlos.
- Pac-Man en la Mina dejó de incluirse: usaba la pantalla entera de Pac-Man. Rehacerlo exige
  extraer el laberinto y los gráficos del VIC y diseñar reglas propias; se priorizó el modelo
  ya jugable de Manic Invaders. Se conservan los archivos guardados por el usuario; una copia
  sin modificar del antiguo ejemplo no se ofrece en el menú.
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
- El creador todavía no se probó con una IA real (sí con una simulada y con dos juegos
  escritos siguiendo solo las guías), para no gastar el cupo del usuario.
- La prueba no juzga si el juego es divertido ni si cumple el pedido: mira que arranque,
  dibuje, responda a los controles y aguante 30 segundos de juego al azar. Un juego que se
  termina solo (sin controles) o que se maneja con teclas que el robot no conoce no pasa.
- La guía del C64 pide no usar el KERNAL porque el emulador puede tener las Open ROMs; con
  las ROMs originales funcionaría, pero la prueba no lo sabe.

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
  El tiempo transcurrido está disponible; la estimación del tiempo restante sigue pendiente.

- En el C64, F1/F3/F5/F7 son teclas de la máquina: la carga rápida de estado pasa a F4 y
  el reinicio queda en el menú Máquina.
- Los estados guardados de la app corresponden a originales. Una fusión necesita guardar
  también el guion y todas sus máquinas; hasta implementar ese formato, estas opciones
  están deshabilitadas durante una fusión. Se puede reiniciar desde el estudio.
- Los estados en archivo validan el formato de máquina, no el hash del juego. Hay que
  usarlos con el mismo juego y firmware con que se guardaron.
- `retrofusion/` (kit Python inicial) no tiene su punto de entrada `retro.py`: no se puede
  correr tal cual.

## Entorno de desarrollo

- La copia vieja del proyecto en la carpeta temporal de la sesión de Claude
  (`scratch-workspaces\...\RetroFusion`, commit d40a49b) no es la canónica.
- La herramienta Bash de Claude Code convierte `\\` en `\` dentro de los heredocs.
- `dotnet test` de toda la solución tarda unos 10 minutos (ZEXDOC/ZEXALL en Debug).

## Contenido excluido

Por pedido del usuario no se usan: contenido pornográfico, *Fundamentally Loathsome* y,
por cautela, las ediciones de Cheril, Phantomasa, Sila Tsikl y Nanako Descends to Hell
(retiradas en la auditoría de ROMs del 18/10/2026).
