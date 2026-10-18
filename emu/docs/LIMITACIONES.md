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
- Tres cintas cargan completas y después el Spectrum vuelve a su pantalla de inicio
  (observado en la matriz): Galaxian de Atarisoft (`clasicos/Spectrum/galaxian.zip`, el
  `.z80` de Spectrum-seleccion sí anda), Karateka (las dos copias) y Lode Runner
  "passworded". Típico de cargadores con protección o timing fino; falta investigar si es
  del emulador (T-015).
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

### Nintendo NES (en construcción, T-031)

- Hecho: la CPU 2A03 (6502 sin modo decimal, `DecimalEnabled = false`, con prueba) y el
  lector de cartuchos iNES/NES 2.0 (`Retro.Systems.Nes.NesCartridge`) con mappers 0 (NROM)
  y 3 (CNROM); los 5 .nes de Mojon Twins se leen bien. Falta la PPU, la APU, los mandos,
  la máquina y su integración en la app: todavía no corre ningún juego.
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
