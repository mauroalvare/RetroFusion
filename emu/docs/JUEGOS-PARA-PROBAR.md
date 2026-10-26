# Sistemas, formatos y requisitos

Agregá tus archivos en **ROMs**, usá **Abrir archivo** o arrastralos a la ventana.
En **Biblioteca** podés elegir una máquina y ver los juegos detectados para ella.
Los juegos se obtienen por separado; usá copias que tengas derecho a utilizar.

## Sistemas disponibles

| Sistema | Archivos que puede abrir | Requisitos |
|---|---|---|
| ZX Spectrum 48K | TAP, TZX, Z80 y SNA | La ROM 48K viene incluida. Las cintas conservan su proceso de carga. |
| Commodore 64 | T64 y PRG | Incluye Open ROMs libres. Podés aportar tu firmware en firmware/c64; algunas ediciones lo necesitan. |
| Nintendo NES | Cartuchos NES en formato iNES | No necesita firmware. Mappers disponibles: 0, 1, 2, 3, 4 y 7; otros se rechazan al abrir. |
| Placa arcade Taito/Midway 8080 | Conjunto compatible de ROMs en ZIP | Necesita el conjunto completo de la placa implementada. Compartir CPU no hace compatibles otras placas o juegos. |
| IBM PC/XT | EXE, COM, ZIP e imágenes IMG/IMA de 160–720 KB | Incluye BIOS libre y FreeDOS para programas de PC/XT con CGA. No cubre programas para hardware posterior. |

En PC, dejá cada programa suelto junto a sus datos. Un ZIP con varios ejecutables
permite elegir cuál iniciar y muestra sus carpetas. El organizador conserva los
EXE/COM sueltos en su lugar para no separarlos de sus archivos.
El programa y sus datos deben entrar junto con DOS en el disquete privado de 720 KB.
[Uso de PC y límites](PC-DOS.md).

Los ZIP pueden contener subcarpetas y otros ZIP, dentro de los límites de seguridad
y tamaño del lector. Descomprimí RAR y 7z antes de abrirlos. Un archivo vacío,
incompleto o con extensión ambigua puede no identificarse.

## Sistemas previstos

El catálogo también puede reconocer archivos de máquinas que todavía no ejecuta.
La biblioteca indica su estado; aportar firmware no implementa el hardware faltante.

| Familia | Máquinas previstas |
|---|---|
| Z80 | Spectrum 128K, TK90X, MSX, Amstrad CPC y NEC PC-8801 |
| 6502 y derivados | Atari 800/XL, Apple II, VIC-20 y Plus/4 |
| Motorola 68000 | Atari ST y Amiga 500 |

No hay una fecha comprometida para estas máquinas. El estado de cada núcleo y placa
está en [ARQUITECTURA.md](ARQUITECTURA.md) y [LIMITACIONES.md](LIMITACIONES.md).

## Cómo interpretar la compatibilidad

Reconocer un formato, cargarlo, llegar al título y poder jugar son comprobaciones
distintas. Que una edición funcione no garantiza lo mismo para otras revisiones,
mappers o requisitos de hardware. Las pruebas también deben comprobar controles,
progreso y comportamiento durante la partida.

El comando **retro-cli info** identifica los archivos; **retro-cli compat** ejecuta
guiones de comprobación con las copias locales. [Mapa de herramientas](MAPA.md).
[COMPATIBILIDAD.md](COMPATIBILIDAD.md) explica los niveles y cómo generar un informe
local con fecha y edición probadas. Los informes por título permanecen privados.

Las fusiones necesitan además perfiles y datos verificados para la edición elegida.
La disponibilidad de una máquina no implica que todos sus juegos estén listos para
fusionar. [Alcance de las pruebas de fusión](PRUEBAS-FUSION.md).
