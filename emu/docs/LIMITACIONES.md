# Limitaciones y errores conocidos

Registro de lo que no funciona o funciona a medias. Cada entrada dice si es **observado**
(se vio en una prueba) o **probable** (se deduce del código, falta comprobarlo). Al
arreglar algo, se borra la entrada y se menciona en el commit.

## Máquinas

### ZX Spectrum

- Solo el modelo 48K. Los juegos 128K (4 de Mojon Twins en `descargados-autores\Spectrum-128K`)
  dan corrupción gráfica o reinicio en el 48K (observado en la auditoría de ROMs). Tarea T-030.
- Jet Set Willy pide la tarjeta de códigos (protección del juego, no fallo de carga).
  Karateka (edición de `clasicos\Spectrum`) se reinicia: falta investigar la edición.
- Turrican y otros multicarga: solo se comprobó la primera carga.
- Dos cintas cargan completas y después el Spectrum vuelve a su pantalla de inicio
  (observado en la matriz): Karateka (las dos copias, multicarga: vuelve a pedir cinta a
  la ROM y no se reinicia en mitad de la carga) y Lode Runner "passworded" (cargador
  propio que usa la rutina de bordes de la ROM). Falta investigar (T-015). Galaxian de
  Atarisoft, que fallaba igual, ya anda: su cargador turbo espera el borde que cierra el
  último pulso de cada bloque, que el emulador no generaba cuando la señal quedaba en
  bajo (arreglado el 18/10, con prueba).
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

- Por ahora clasifica vidas, tiempo/energía y posiciones X/Y. Puntaje, nivel/habitación,
  munición, inventario y tablas de entidades necesitan experimentos guiados (juntar algo,
  pasar de nivel) que todavía no hace (T-017).
- Las vidas solo aparecen si el personaje muere durante la observación (subir `--cuadros`).
- Las hipótesis no se comprueban solas todavía: escribir el valor y ver el efecto es el
  siguiente paso (T-017, T-024).

## Fusiones y estudio

- Las recetas por reglas admiten 2 o 3 juegos (`FusionRecipe` lo valida). T-023.
- Los perfiles de juegos (`Retro.Fusion/Profiles`) se asocian por nombre de archivo, no
  por hash de la edición: otra edición del mismo juego podría usar otras direcciones. T-053.
- Hay perfiles de Manic Miner (Spectrum), Space Invaders (arcade) y Pac-Man de C64 (vidas
  verificadas; X e Y son valores derivados, solo lectura). Pac-Man de Spectrum y los demás
  juegos de C64 todavía no tienen perfil. T-022.
- "Pac-Man en la Mina" (estudio, C64 + Spectrum) lee al Pac-Man de C64 por los sprites del
  VIC (4 = Pac-Man, 0-3 fantasmas) y nota los puntos comidos por su color (0xB8C76F): no
  distingue puntos de pastillas ni ve los fantasmas comidos ni la fruta (el puntaje del
  original no está ubicado en memoria). Cuando la vida se pierde por aire o por el
  guardián, Pac-Man no hace la animación de muerte del original (se frena un momento). Las
  vidas del original se mantienen en 5 y las lleva el guion. T-017.
- En la vista "lado a lado" de la app, la ventana de la fusión tapa parte del juego de la
  derecha (observado con Pac-Miner); falta reservarle espacio como al estudio. T-023.
- El validador de jugabilidad comprueba estados (título, empezar, puntaje, muertes,
  reinicio, pantalla congelada) con 3 robots; no detecta todavía controles sin efecto,
  reglas en bucle, audio saturado ni pantallas ilegibles. T-026. No mide diversión.

## App, CLI y paquetes

- En el C64, F1/F3/F5/F7 son teclas de la máquina: la carga rápida de estado pasa a F4 y
  el reinicio queda en el menú Máquina.
- El release publicado en GitHub (v0.3.0) es anterior al C64. `Desktop\RetroEmu\Programa`
  se compiló del commit d15a6c2 antes de los últimos retoques del CLI (`info` de C64 y
  creación de máquinas C64 en fusiones). T-052.
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
