# Qué juegos conseguir para probar cada máquina

Poné los archivos (sueltos o en .zip, aunque haya zips dentro de zips) en la carpeta
`ROMs`. El emulador reconoce solo para qué máquina es cada uno; en **Máquina** ves qué
juegos de tu carpeta corre cada máquina. Los .rar y .7z hay que descomprimirlos antes.

Conseguí solo copias que tengas derecho a usar. Muchos de estos juegos se distribuyen
gratis con permiso de sus autores (por ejemplo, en los archivos de World of Spectrum y
Spectrum Computing), y para todas las máquinas hay juegos caseros (homebrew) libres.

Consejo: si hay varias versiones del mismo juego, preferí la marcada `[!]` (volcado
verificado) y evitá `[h]` (hack), `[t]` (con trucos) y `[cr]` (crackeado).

## Funcionan hoy

| Máquina | CPU | Buscá archivos | Firmware | Para probar |
|---|---|---|---|---|
| ZX Spectrum 48K | Zilog Z80 | `.tzx` `.tap` `.z80` `.sna` | Incluido (`48.rom`) | Jetpac, Lode Runner, Boulder Dash, Jet Set Willy, Knight Lore, Atic Atac, Chuckie Egg, Skool Daze |
| Placa arcade Taito 8080 | Intel 8080 | `invaders.zip` de MAME | Viene en el zip | Space Invaders. Otros juegos de la misma placa (Space Invaders Part II, Lunar Rescue) necesitan pequeños agregados. |
| Commodore 64 | MOS 6510 | `.t64` `.prg` (los `.d64` `.crt` `.tap` llegan en la próxima etapa) | Incluido: Open ROMs libres. Con las originales (`kernal.rom`, `basic.rom`, `chargen.rom` en `firmware/c64`) es más compatible | Impossible Mission, Boulder Dash, Lode Runner, Jumpman, Bruce Lee, Paradroid, Uridium. Preferí versiones "one-file" (un solo archivo .t64 o .prg). |

## Lo próximo: el resto de la familia 6502 (la CPU ya está hecha)

| Máquina | CPU | Buscá archivos | Firmware | Para probar |
|---|---|---|---|---|
| Atari 800/XL | MOS 6502 | `.xex` `.atr` `.car` | Uso AltirraOS (libre); original: `atarixl.rom` | Star Raiders, Boulder Dash, Miner 2049er, Jumpman, River Raid |
| Apple II | MOS 6502 | `.dsk` `.woz` | Hace falta la ROM original del Apple II+ o IIe (con copyright: la tenés que conseguir vos) | Lode Runner, Choplifter, Karateka, Prince of Persia (el original es de Apple II) |
| Nintendo NES | Ricoh 2A03 | `.nes` | No necesita | Juegos de mapper 0 primero: Super Mario Bros, Donkey Kong, Balloon Fight, Excitebike |
| Commodore VIC-20 | MOS 6502 | `.prg` `.crt` | Uso reemplazos libres si existen; original: `vic20-kernal.rom` | Gorf, Omega Race, Pac-Man |
| Commodore Plus/4 | MOS 7501 | `.prg` `.tap` `.d64` | Original: `plus4-kernal.rom` | Spy vs Spy, Kikstart |

## Máquinas Z80 (el CPU ya está hecho)

| Máquina | Buscá archivos | Firmware | Para probar |
|---|---|---|---|
| ZX Spectrum 128K | `.tzx` `.tap` `.z80` | `128-0.rom` y `128-1.rom` (Amstrad permite distribuirlas) | Juegos con música AY: Robocop, Chase H.Q., Where Time Stood Still |
| TK90X | `.tzx` `.tap` | `tk90x.rom` | Los mismos del Spectrum 48K |
| MSX | `.rom` `.dsk` `.cas` | Uso C-BIOS (libre) | Knightmare, Penguin Adventure, Nemesis (Gradius) |
| Amstrad CPC | `.dsk` `.cdt` | Amstrad permite distribuir las ROMs del CPC | Ghostbusters, Sorcery, Gryzor, Head over Heels |
| NEC PC-8801 | `.d88` | ROMs originales (con copyright) | Pac-Man, Thexder |

## 16 bits

| Máquina | CPU | Buscá archivos | Firmware | Para probar |
|---|---|---|---|---|
| IBM PC/XT | Intel 8088 | `.exe` `.com`, discos `.img` `.ima` | BIOS libre (GLaBIOS) y FreeDOS | Digger, Alley Cat, Paratrooper, Sopwith, Styx. Prince of Persia pide al menos un 286: queda para después. |
| Atari ST | Motorola 68000 | `.st` `.msa` | EmuTOS (libre) | Spy vs Spy, Lemmings, Oids, Xenon |
| Amiga 500 | Motorola 68000 | `.adf` | Kickstart 1.3 (`kick13.rom`, con copyright; se compra legalmente en Amiga Forever) o AROS (libre, menos compatible) | Lemmings, Shadow of the Beast, Turrican, Babylonian Twins |

## Lo que ya tenés en tu carpeta

- **Corren hoy:** Manic Miner y Pac-Man (Spectrum), Space Invaders (arcade), y
  Ghostbusters, Pac-Man y Spy vs Spy (C64, los tres en `.t64`).
- **Atari 8 bits:** Alpha Shield, Ghostbusters, Pac-Man, Space Invaders, Spy vs Spy, Terminator.
- **Apple II:** Pac-Man y Spy vs Spy (falta la ROM del Apple II).
- **PC:** Digger, Conquest, Rollo, Styx y la demo de Prince of Persia.
- **Otros:** Ghostbusters (CPC), Pac-Man y Spy vs Spy (PC-88), Pac-Man (VIC-20), Spy vs Spy (Plus/4, Atari ST, Amiga), Babylonian Twins (Amiga).
- **Lemmings.rar:** descomprimilo para que el emulador vea qué versión es.
