# Matriz de compatibilidad

Generada el 2026-10-18 16:08 UTC (commit ab070c0+cambios) con `retro-cli compat` sobre `C:/Users/mauro/Downloads/ROMs`. Los datos completos (hash, procedimiento, evidencia de cada nivel) están en `emu/compat/resultados.json`; los guiones, en `emu/compat/juegos/`. No editar a mano: se regenera.

Niveles, en orden: **formato** reconocido → medio **cargado** → **imagen** (pantalla con contenido y sin signos de fallo) → llega al **título** (comprobación del guion) → **responde** a los controles (una entrada cambia la imagen respecto de la misma corrida sin entradas) → **jugable** (comprobaciones de partida del guion) → listo para **fusionar** (perfil con direcciones verificadas). `sí` = comprobado, **no** = falló, `parcial` = a medias, `·` = no probado. Son pruebas cortas y reproducibles, no partidas completas.

## Commodore 64 (13 archivos, 6 cargan, 6 responden a controles)

Determinismo: 6 de 6 corridas repetidas dieron exactamente la misma imagen final.

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Boulder Dash ⁽ᵃ⁾ | clasicos/C64/boulder-dash.zip → BDASH01.T64 | f1f263754b73 | sí | sí | sí | · | sí | · | · | cambian la imagen: JOY1 RIGHT, JOY1 FIRE, F1, RETURN (de 10 probadas) |
| Bruce Lee ⁽ᵃ⁾ | clasicos/C64/bruce-lee.zip → BRUCELEE.T64 | 81365512a35b | sí | sí | sí | · | sí | · | · | cambian la imagen: JOY2 FIRE, JOY1 FIRE, SPACE (de 10 probadas) |
| Donkey Kong ⁽ᵃ⁾ | clasicos/C64/donkey-kong.zip → DONKEYK2.D64 | 2e8cd98a6fad | sí | **no** | · | · | · | · | · | limitación del emulador: Por ahora el C64 carga .t64 y .prg; los discos .d64, los cartuchos .crt y las cintas .tap llegan en la próxima etapa. |
| Ghostbusters | Ghostbusters_C64_ROM_EN.zip → GHOSTBU1.T64 | 6c332e873650 | sí | sí | sí | · | sí | sí | · | del juego: Intro del grupo cracker y menú de trainer con seis preguntas (se contestan con N); del juego: La pantalla de Activision espera FIRE o ESPACIO; cambian la imagen: 1, 2 (de 3 probadas); Misma secuencia que tests/C64.Tests/GameTests.cs. |
| Jet Set Willy ⁽ᵃ⁾ | clasicos/C64/jet-set-willy.zip → JETSETW1.T64 | 5e40ca0dc577 | sí | sí | sí | · | sí | · | · | cambian la imagen: JOY1 FIRE, SPACE (de 10 probadas) |
| Lode Runner ⁽ᵃ⁾ | clasicos/C64/lode-runner.zip → LODERUN1.D64 | e7eb5220fa10 | sí | **no** | · | · | · | · | · | limitación del emulador: Por ahora el C64 carga .t64 y .prg; los discos .d64, los cartuchos .crt y las cintas .tap llegan en la próxima etapa. |
| Nanako in Classic Japanese Monster Castle ⁽ᵃ⁾ | descargados-autores/C64/Inside--Nanako-in-Classic-Japanese-Monster-Castle--C64.d64 | 011f166daeb4 | sí | **no** | · | · | · | · | · | limitación del emulador: Por ahora el C64 carga .t64 y .prg; los discos .d64, los cartuchos .crt y las cintas .tap llegan en la próxima etapa. |
| Pac-Man | Pac_Man_C64_ROM_EN.zip → PAC-MAN1.T64 | 1f743c4874a5 | sí | sí | sí | sí | sí | sí | · | del juego: Intro del grupo cracker, documentación y preguntas del trainer (Unlimited lives, levelskip...) antes del juego: se contestan con N; cambian la imagen: JOY2 RIGHT, JOY2 UP, JOY2 DOWN (de 4 probadas); Misma secuencia que tests/C64.Tests/GameTests.cs. |
| River Raid ⁽ᵃ⁾ | clasicos/C64/river-raid.zip → RIVERRAI.D64 | 1c8a5754e374 | sí | **no** | · | · | · | · | · | limitación del emulador: Por ahora el C64 carga .t64 y .prg; los discos .d64, los cartuchos .crt y las cintas .tap llegan en la próxima etapa. |
| Sir Ababol ⁽ᵃ⁾ | descargados-autores/C64/Mojon-Twins--Sir-Ababol--C64.d64 | b542e98dcdae | sí | **no** | · | · | · | · | · | limitación del emulador: Por ahora el C64 carga .t64 y .prg; los discos .d64, los cartuchos .crt y las cintas .tap llegan en la próxima etapa. |
| Spy vs Spy | Spy_vs_Spy_C64_EN.zip → SPYVSPY1.T64 | 15ee1d57da46 | sí | sí | sí | sí | sí | · | · | del juego: El jugador 1 usa el puerto 1 del joystick; cambian la imagen: SPACE, RUN/STOP, JOY1 FIRE (de 4 probadas); Misma secuencia que tests/C64.Tests/GameTests.cs; falta una comprobación de partida. |
| Turrican ⁽ᵃ⁾ | clasicos/C64/turrican.zip → TURRI1-0.D64 | db175fbfcf25 | sí | **no** | · | · | · | · | · | limitación del emulador: Por ahora el C64 carga .t64 y .prg; los discos .d64, los cartuchos .crt y las cintas .tap llegan en la próxima etapa. |
| Uwol ⁽ᵃ⁾ | descargados-autores/C64/Mojon-Twins--Uwol-C64.d64 | d4ef44346b2e | sí | **no** | · | · | · | · | · | limitación del emulador: Por ahora el C64 carga .t64 y .prg; los discos .d64, los cartuchos .crt y las cintas .tap llegan en la próxima etapa. |

## Placa arcade Taito 8080 (1 archivos, 1 cargan, 1 responden a controles)

Determinismo: 1 de 1 corridas repetidas dieron exactamente la misma imagen final.

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Space Invaders | invaders.zip → invaders.e | 0a97ed62e206 | sí | sí | sí | sí | sí | sí | parcial | cambian la imagen: P1 RIGHT, P1 FIRE (de 3 probadas); Comprobaciones con las direcciones del perfil invaders (verificadas al crear la fusión Manic Invaders). |

## Nintendo NES (5 archivos, 4 cargan, 4 responden a controles)

Determinismo: 4 de 4 corridas repetidas dieron exactamente la misma imagen final.

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Bootee ⁽ᵃ⁾ | descargados-autores/NES/mojon-twins--bootee.nes | 9e3daee355eb | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina nes |
| Cadaverion ⁽ᵃ⁾ | descargados-autores/NES/mojon-twins--cadaverion.nes | e139848c04c7 | sí | sí | sí | · | sí | · | · | cambian la imagen: P1 START (de 8 probadas) |
| Cave Cnrom | descargados-autores/NES/mojon-twins--cave-cnrom.nes | b17032c5e5aa | sí | sí | sí | sí | sí | · | · | sí: START deja el título y empieza la partida (bosque con la protagonista) (la imagen cambió desde el cuadro 280) \| sonda: cambian la imagen: P1 START, P1 LEFT, P1 RIGHT, P1 A (de 4 probadas); Escrito a mano por claude-programado (18/10): la sonda automática no veía respuesta; con START en el cuadro 300 entra al juego (capturas emu/out/nes/cave-cnrom_*.png). Falta una comprobación de partida (vidas en memoria). |
| Cheman ⁽ᵃ⁾ | descargados-autores/NES/mojon-twins--cheman.nes | 380f05602d31 | sí | sí | sí | · | sí | · | · | cambian la imagen: P1 START, P1 UP, P1 DOWN, P1 A, P1 B, P1 SELECT (de 8 probadas) |
| Super Uwol ⁽ᵃ⁾ | descargados-autores/NES/mojon-twins--super-uwol.nes | 67eaa300c8ab | sí | sí | sí | · | sí | · | · | cambian la imagen: P1 START (de 8 probadas) |

## ZX Spectrum 48K (31 archivos, 31 cargan, 29 responden a controles)

Determinismo: 31 de 31 corridas repetidas dieron exactamente la misma imagen final.

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Bomb Jack ⁽ᵃ⁾ | clasicos/Spectrum-seleccion/bomb-jack.tzx | edfb74bc44f8 | sí | sí | sí | · | sí | · | · | cambian la imagen: O, P, Q, A, M, Z, X, N, S, K, Y, SPACE, ENTER, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 (de 29 probadas) |
| Bomb Jack ⁽ᵃ⁾ | clasicos/Spectrum/bomb-jack.zip → Bomb Jack.tzx | edfb74bc44f8 | sí | sí | sí | · | sí | · | · | cambian la imagen: O, P, Q, A, M, Z, X, N, S, K, Y, SPACE, ENTER, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 (de 29 probadas) |
| Chuckie Egg ⁽ᵃ⁾ | clasicos/Spectrum-seleccion/chuckie-egg.tzx | 9082c6d98f12 | sí | sí | sí | · | sí | · | · | cambian la imagen: S (de 29 probadas) |
| Chuckie Egg ⁽ᵃ⁾ | clasicos/Spectrum/chuckie-egg.zip → Chuckie Egg (1984)(Pick 'n' Choose)[re-release].tzx | 9082c6d98f12 | sí | sí | sí | · | sí | · | · | cambian la imagen: S (de 29 probadas) |
| Columns ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Columns.tzx | c7ef3542e7db | sí | sí | sí | · | **no** | · | · |  |
| Fase Bonus The Game ⁽ᵃ⁾ | descargados-autores/Spectrum/Fase-Bonus-The-Game--The-Mojon-Twins.tap | 3098a2eb648d | sí | sí | sí | · | sí | · | · | cambian la imagen: 1, 2, 3 (de 29 probadas) |
| Galaxian | clasicos/Spectrum-seleccion/galaxian.z80 | b1ff28923968 | sí | sí | sí | sí | sí | · | · | cambian la imagen: S (de 29 probadas); Snapshot: arranca directo en la demostración (curado por claude-programado el 18/10: texto leído con la fuente de la ROM). |
| Galaxian | clasicos/Spectrum/galaxian.zip → Galaxian (1984)(Atarisoft).tzx | 212ea75a105d | sí | sí | sí | sí | sí | · | · | cambian la imagen: S (de 29 probadas); Antes se reiniciaba tras cargar (T-015) (curado por claude-programado el 18/10: texto leído con la fuente de la ROM). |
| Horace Goes to the Tower ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Horace-Goes-to-the-Tower.tzx | 5e30d26c859f | sí | sí | sí | · | sí | · | · | cambian la imagen: 1, 2, 3 (de 29 probadas) |
| Jet Set Willy | clasicos/Spectrum-seleccion/jet-set-willy.z80 | 1e16f855d67d | sí | sí | sí | sí | sí | · | · | cambian la imagen: 1, 2, 3, 4 (de 29 probadas); Necesita la tarjeta de códigos para seguir: no se puede llegar a jugar sin ella (curado por claude-programado el 18/10: texto leído con la fuente de la ROM). |
| Jet Set Willy | clasicos/Spectrum/jet-set-willy.zip → Jet Set Willy (1984)(Software Projects).tzx | 0d846f41d311 | sí | sí | sí | sí | sí | · | · | cambian la imagen: 1, 2, 3, 4 (de 29 probadas); Necesita la tarjeta de códigos para seguir (curado por claude-programado el 18/10: texto leído con la fuente de la ROM). |
| Karateka | clasicos/Spectrum-pendiente/karateka.tap | d66e554206ae | sí | sí | sí | sí | sí | · | · | cambian la imagen: S, SPACE, CAPS SHIFT (de 29 probadas); Multicarga: la cinta se detiene sola durante la presentación y la ROM la rearranca (T-015, D-013) (curado por claude-programado el 18/10: texto leído con la fuente de la ROM). |
| Karateka | clasicos/Spectrum/karateka.zip → Karateka (1986)(Dro Soft)(es)[cr Rajsoft].tap | d66e554206ae | sí | sí | sí | sí | sí | · | · | cambian la imagen: S, SPACE, CAPS SHIFT (de 29 probadas); Multicarga: la cinta se detiene sola durante la presentación y la ROM la rearranca (T-015, D-013) (curado por claude-programado el 18/10: texto leído con la fuente de la ROM). |
| Lala Prologue ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Lala-Prologue.tzx | c0e0647efd09 | sí | sí | sí | · | sí | · | · | cambian la imagen: O, P, Q, A, M, Z, X, N, S, K, Y, SPACE, ENTER, CAPS SHIFT, SYMBOL SHIFT, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 (de 29 probadas) |
| Lode Runner ⁽ᵃ⁾ | clasicos/Spectrum-seleccion/lode-runner.z80 | d9d4b5257f9c | sí | sí | sí | · | sí | · | · | cambian la imagen: O, P, Q, A, M, Z, X, N, S, K, Y, SPACE, ENTER, CAPS SHIFT, SYMBOL SHIFT, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 (de 29 probadas) |
| Lode Runner | clasicos/Spectrum/lode-runner.zip → Lode Runner (1984)(Software Projects)[passworded].tzx | 89cfe394312f | sí | sí | sí | sí | sí | · | · | cambian la imagen: 1, 2, 4, 6 (de 29 probadas); Antes se reiniciaba tras cargar (T-015); el código está en el manual original (curado por claude-programado el 18/10: texto leído con la fuente de la ROM). |
| Manic Miner | ManicMiner.tzx.zip → Manic Miner.tzx | 3c9bbf17c2a9 | sí | sí | sí | sí | sí | sí | parcial | cambian la imagen: O, P, SPACE (de 3 probadas); Comprobaciones con las direcciones del perfil manicminer. |
| Mariano the Dragon Capers in Cityland ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Mariano-the-Dragon-Capers-in-Cityland.tzx | e15e686b3688 | sí | sí | sí | · | sí | · | · | cambian la imagen: O, P, Q, A, M, Z, X, N, S, K, Y, SPACE, ENTER, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 (de 29 probadas) |
| Moggy Adventure ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Moggy-Adventure.tzx | 1053e8277ccf | sí | sí | sí | · | sí | · | · | cambian la imagen: 1, 2, 3 (de 29 probadas) |
| Nanako in Classic Japanese Monster Castle ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Nanako-in-Classic-Japanese_Monster_Castle.tzx | 7bd4de6c4224 | sí | sí | sí | · | sí | · | · | cambian la imagen: P, S (de 29 probadas) |
| Pac Man | Pac_Man_ZX-Spectrum_ROM_EN.zip → Pac-Man (1983)(Atarisoft).tzx | 5d182adf7538 | sí | sí | sí | sí | sí | · | · | cambian la imagen: P, Q, A, 2, 3, 4, 6, 7, 8 (de 29 probadas); Los pasos eligen teclado (1) y empiezan (S): la corrida termina en la partida. Falta jugabilidad: el laboratorio no halló vidas (los candidatos $5Cxx son variables del sistema que escribe PRINT de la ROM) (curado por claude-programado el 18/10: texto leído con la fuente de la ROM). |
| Phantomas Saga 1 Infinity ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Phantomas-Saga-1-Infinity.tzx | 561cfbb4c697 | sí | sí | sí | · | sí | · | · | cambian la imagen: O, P, Q, A, M, Z, X, N, S, K, Y, SPACE, ENTER, CAPS SHIFT, SYMBOL SHIFT, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 (de 29 probadas) |
| Sir Ababol ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Sir-Ababol.tzx | 99734c6d10f3 | sí | sí | sí | · | sí | · | · | cambian la imagen: 1, 2, 3 (de 29 probadas) |
| Trabajo Basura ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Trabajo-Basura.tzx | f6365487b26a | sí | sí | sí | · | sí | · | · | cambian la imagen: 1, 2, 3 (de 29 probadas) |
| Turrican ⁽ᵃ⁾ | clasicos/Spectrum-seleccion/turrican.tzx | 6073bb2e61f1 | sí | sí | sí | · | sí | · | · | cambian la imagen: SPACE (de 29 probadas) |
| Turrican ⁽ᵃ⁾ | clasicos/Spectrum/turrican.zip → Turrican (1990)(Rainbow Arts).tzx | 6073bb2e61f1 | sí | sí | sí | · | sí | · | · | cambian la imagen: SPACE (de 29 probadas) |
| Uwol ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Uwol.tzx | 8bc9e6c5e0c4 | sí | sí | sí | · | **no** | · | · |  |
| Viaje al centro de la napia ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Viaje-al-centro-de-la-napia.tzx | 74a61bd23ae7 | sí | sí | sí | · | sí | · | · | cambian la imagen: 1, 2, 3 (de 29 probadas) |
| Xenon ⁽ᵃ⁾ | clasicos/Spectrum-seleccion/xenon.z80 | 72cf9bb3eec5 | sí | sí | sí | · | sí | · | · | cambian la imagen: SPACE, 1, 2, 3, 4 (de 29 probadas) |
| Xenon ⁽ᵃ⁾ | clasicos/Spectrum/xenon.zip → Xenon (1988)(Melbourne House)[48-128K].tzx | 906c6f4da7dc | sí | sí | sí | · | sí | · | · | cambian la imagen: O, P, Q, A, M, Z, X, N, S, K, Y, SPACE, ENTER, CAPS SHIFT, SYMBOL SHIFT, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 (de 29 probadas) |
| Zombie Calavera Prologue ⁽ᵃ⁾ | descargados-autores/Spectrum/Mojon-Twins--Zombie-Calavera-Prologue.tzx | c8f2d431965b | sí | sí | sí | · | sí | · | · | cambian la imagen: 1, 2, 3 (de 29 probadas) |

## Amiga 500 (9 archivos, máquina no implementada)

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Arkanoid ⁽ᵃ⁾ | clasicos/Amiga/arkanoid.zip → Arkanoid (1988)(Discovery Software)[cr SF].adf | 83066d4e412d | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina amiga500 |
| Babylonian Twins 1993 Demo ⁽ᵃ⁾ | babylonian-twins-1993-demo.adf | e27400f4b463 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina amiga500 |
| Babylonian Twins 1993 Disk1 ⁽ᵃ⁾ | babylonian-twins-1993-disk1.adf | 8f2f3a5e4452 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina amiga500 |
| Babylonian Twins 1993 Disk2 ⁽ᵃ⁾ | babylonian-twins-1993-disk2.adf | 199b1c3fa838 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina amiga500 |
| Bomb Jack ⁽ᵃ⁾ | clasicos/Amiga/bomb-jack.zip → Bomb Jack (1987)(Elite)[cr DF].adf | 5e2b8603de03 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina amiga500 |
| Prince Of Persia ⁽ᵃ⁾ | clasicos/Amiga/prince-of-persia.zip → Prince of Persia (1990)(Broderbund)[cr Angels][f AGA SKR][t +2 Angels].adf | f132d19b73b8 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina amiga500 |
| Spy vs Spy ⁽ᵃ⁾ | Spy_vs_Spy_Amiga_EN.zip → Spy vs Spy (1989)(First Star Software).adf | 731f8fdd4b75 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina amiga500 |
| Turrican ⁽ᵃ⁾ | clasicos/Amiga/turrican.zip → Turrican (1990)(Rainbow Arts)[f AGA][h][HD].adf | 3f1b9cd45a03 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina amiga500 |
| Xenon ⁽ᵃ⁾ | clasicos/Amiga/xenon.zip → Xenon (1988)(Melbourne House).adf | 1570cb295ce1 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina amiga500 |

## Apple II (8 archivos, máquina no implementada)

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Bruce Lee ⁽ᵃ⁾ | clasicos/Apple-II/bruce-lee.zip → Bruce Lee (1984)(Datasoft)[cr Black Bag].dsk | 3d05073be44d | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina apple2 |
| Karateka ⁽ᵃ⁾ | clasicos/Apple-II/karateka.zip → Karateka (1984)(Broderbund).dsk | 4399db1c5469 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina apple2 |
| Lode Runner ⁽ᵃ⁾ | clasicos/Apple-II/lode-runner.zip → Lode Runner (1983)(Broderbund)[m mod-keyset Nut Cracker - Ace Mechanic].do | 3947fe988bb2 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina apple2 |
| Miner 2049Er ⁽ᵃ⁾ | clasicos/Apple-II/miner-2049er.zip → Miner 2049er (1982)(Micro Fun).dsk | 694250f5f88d | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina apple2 |
| Pac Man ⁽ᵃ⁾ | Pac_Man_Apple-II_ROM_EN.zip → Pacman (1983)(Atari).do | f9e19375b158 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina apple2 |
| Prince Of Persia ⁽ᵃ⁾ | clasicos/Apple-II/prince-of-persia.zip → Prince of Persia (1989)(Broderbund)(Disk 1 of 3)[cr Byte Bastards].dsk | aa2b83589636 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina apple2 |
| Spy vs Spy ⁽ᵃ⁾ | Spy_vs_Spy_AppleII_EN.zip → Spy Vs Spy (1984)(First Star Software).dsk | e466ba2f4ed5 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina apple2 |
| Spy vs Spy ⁽ᵃ⁾ | Spy_vs_Spy_AppleII_EN (1).zip → Spy Vs Spy (1984)(First Star Software).dsk | e466ba2f4ed5 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina apple2 |

## Atari 800/XL (12 archivos, máquina no implementada)

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Alley Cat ⁽ᵃ⁾ | clasicos/Atari-8-bit/alley-cat.zip → Alley Cat (1983)(Synapse Software)(US).xex | 94a9d1e309d0 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| Alpha Shield ⁽ᵃ⁾ | Alpha_Shield_Atari-8-bit_ROM_EN.zip → Alpha Shield (1983)(Sirius Software)(US).xex | b52e6d0d6e94 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| Boulder Dash ⁽ᵃ⁾ | clasicos/Atari-8-bit/boulder-dash.zip → Boulder Dash (1984)(First Star Software)(US).xex | 0f23a0aa174d | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| Bruce Lee ⁽ᵃ⁾ | clasicos/Atari-8-bit/bruce-lee.zip → Bruce Lee (1984)(Datasoft)(US).xex | d3657d21939d | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| Chuckie Egg ⁽ᵃ⁾ | clasicos/Atari-8-bit/chuckie-egg.zip → Chuckie Egg (1985)(A&F Software)(GB).xex | caa98bbb8003 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| Ghostbusters ⁽ᵃ⁾ | Ghostbusters_Atari-8-bit_ROM_EN.zip → Ghostbusters (1984)(Activision)(US).xex | 0819a07c396d | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| Miner 2049Er ⁽ᵃ⁾ | clasicos/Atari-8-bit/miner-2049er.zip → Miner 2049er (1982)(Big Five Software)(US).xex | a93438c2a5e9 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| Pac Man ⁽ᵃ⁾ | Pac_Man_Atari-8-bit_ROM_EN.zip → Pac-Man (1982)(Atari)(US).xex | 6ca7e8d2dd96 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| River Raid ⁽ᵃ⁾ | clasicos/Atari-8-bit/river-raid.zip → River Raid (1983)(Activision)(US).xex | d0db4df02919 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| Space Invaders ⁽ᵃ⁾ | Space_Invaders_Atari-8-bit_ROM_EN.zip → Space Invaders (1980)(Atari)(US).xex | dc0d592b9389 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| Spy vs Spy ⁽ᵃ⁾ | Spy_vs_Spy_Atari8bit_EN.zip → Spy vs Spy (1984)(First Star Software)(US).xex | 4aa5fc21f4a7 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |
| Terminator ⁽ᵃ⁾ | Terminator_Atari-8-bit_ROM_EN.zip → Terminator, The (1993)(Domain Software)(PL)[cr Clever People][t].atr | 0ad0d08cc788 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atari800 |

## Atari ST (4 archivos, máquina no implementada)

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Prince Of Persia ⁽ᵃ⁾ | clasicos/Atari-ST/prince-of-persia.zip → Prince of Persia (1990)(Broderbund)[m].st | c1293c76006c | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atarist |
| Spy vs Spy ⁽ᵃ⁾ | Spy_vs_Spy_AtariST_EN.zip → Spy vs Spy (1987)(First Star Software).st | d6f729d9cad1 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atarist |
| Super Game [No Publisher] ⁽ᵃ⁾ | super_game_[no_publisher].zip → super_game_[no_publisher].msa | f142e5298682 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atarist |
| Xenon ⁽ᵃ⁾ | clasicos/Atari-ST/xenon.zip → Xenon (1988)(Melbourne House).st | 660e544ffa7e | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina atarist |

## Amstrad CPC (6 archivos, máquina no implementada)

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Cheman ⁽ᵃ⁾ | descargados-autores/CPC/mojon-twins--cheman.dsk | b896f85f84e8 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina cpc |
| Ghostbusters ⁽ᵃ⁾ | Ghostbusters_Amstrad-CPC_ROM_EN.zip → GHOST.SCR | 9ea47d768de3 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina cpc |
| Lala Prologue ⁽ᵃ⁾ | descargados-autores/CPC/Mojon-Twins--Lala-Prologue--CPC.cdt | 84a64ab44707 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina cpc |
| Nanako In Classic Japanese Monster Castle ⁽ᵃ⁾ | descargados-autores/CPC/Mojon-Twins--Nanako-In-Classic-Japanese-Monster-Castle--CPC.cdt | 592b18079289 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina cpc |
| Sir Ababol ⁽ᵃ⁾ | descargados-autores/CPC/Mojon-Twins--Sir-Ababol-CPC.cdt | 4f4dad531aac | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina cpc |
| Uwol 2 ⁽ᵃ⁾ | descargados-autores/CPC/Mojon-Twins--Uwol-2--CPC.dsk | 9b72102f3614 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina cpc |

## NEC PC-8801 (2 archivos, máquina no implementada)

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Pac Man ⁽ᵃ⁾ | Pac_Man_PC-88_ROM_EN.zip → Pacman (1983)(Dempa Shinbunsha).d88 | 9598cd898ae3 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina pc88 |
| Spy vs Spy ⁽ᵃ⁾ | Spy_vs_Spy_PC8800_EN.zip → Spy vs Spy (1986)(Hot-B).d88 | 8e4693e9703a | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina pc88 |

## IBM PC / XT (7 archivos, máquina no implementada)

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Alley Cat ⁽ᵃ⁾ | clasicos/PC-DOS/alley-cat.zip → CAT.EXE | 4979c8867826 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina pcxt |
| Arkanoid ⁽ᵃ⁾ | clasicos/PC-DOS/arkanoid.zip → ARKANOID.COM | 455ca976a7ae | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina pcxt |
| Donkey Kong ⁽ᵃ⁾ | clasicos/PC-DOS/donkey-kong.zip → Donkey_Kong_1983.exe | 8b58637d82f7 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina pcxt |
| Karateka ⁽ᵃ⁾ | clasicos/PC-DOS/karateka.zip → KARATEKA.EXE | c8736bba30cd | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina pcxt |
| Pop1demo ⁽ᵃ⁾ | pop1demo.zip → pop1demo.exe | 26a85f7ae197 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina pcxt |
| Prince Of Persia ⁽ᵃ⁾ | clasicos/PC-DOS/prince-of-persia.zip → PRINCE.EXE | 09451d0b1975 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina pcxt |
| Wmdemo ⁽ᵃ⁾ | wmdemo.zip → DIGGER.EXE | c94ea1bc7247 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina pcxt |

## Commodore Plus/4 (1 archivos, máquina no implementada)

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Spy vs Spy ⁽ᵃ⁾ | Spy_vs_Spy_CommodorePlus4_EN.zip → Spy Vs Spy (19xx)(Tynesoft Computer Software)(GB).tap | eda492fd04e7 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina plus4 |

## Commodore VIC-20 (3 archivos, máquina no implementada)

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Galaxian ⁽ᵃ⁾ | clasicos/VIC-20/galaxian.zip → Galaxian (1983)(Atari)(NTSC-PAL)[A000].crt | 0d2bf5295263 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina vic20 |
| Miner 2049Er ⁽ᵃ⁾ | clasicos/VIC-20/miner-2049er.zip → Miner 2049'er (1984)(Reston)(PAL)[A000].crt | 2d766aa08979 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina vic20 |
| Pac Man ⁽ᵃ⁾ | Pac_Man_VIC-20_ROM_EN.zip → Pac-Man (1983)(Atarisoft)[A000].crt | aa72664e277b | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina vic20 |

## ZX Spectrum 128K (4 archivos, máquina no implementada)

| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |
|---|---|---|---|---|---|---|---|---|---|---|
| Biniax 2 ⁽ᵃ⁾ | descargados-autores/Spectrum-128K/Mojon-Twins--Biniax-2.tzx | 7a166bce0599 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina zx128; Está en la carpeta de 128K: necesita el Spectrum 128K (T-030). |
| Sgt. Helmet Zero ⁽ᵃ⁾ | descargados-autores/Spectrum-128K/Mojon-Twins--Sgt.-Helmet-Zero.tzx | 0a28aef93bee | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina zx128; Está en la carpeta de 128K: necesita el Spectrum 128K (T-030). |
| Subaquatic ⁽ᵃ⁾ | descargados-autores/Spectrum-128K/Mojon-Twins--Subaquatic.tzx | 9c12a2c4eeb9 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina zx128; Está en la carpeta de 128K: necesita el Spectrum 128K (T-030). |
| Subaquatic Reloaded ⁽ᵃ⁾ | descargados-autores/Spectrum-128K/Mojon-Twins--Subaquatic-Reloaded.tzx | 878dc8b68a81 | sí | · | · | · | · | · | · | limitación del emulador: falta la máquina zx128; Está en la carpeta de 128K: necesita el Spectrum 128K (T-030). |

⁽ᵃ⁾ Guion generado automáticamente (sin pasos ni comprobaciones escritos a mano): solo puede llegar a «imagen» y «responde».
