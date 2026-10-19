# RetroFusion

Emulador de computadoras y consolas de 8 bits (**RetroEmu**) que corre los juegos
originales con emulación real de CPU y hardware, y los **fusiona** con IA en juegos
nuevos: el estudio propone ideas, escribe el juego, lo prueba con jugadores robot y saca
versiones nuevas con lo que le pidas. También **crea juegos de 8 bits desde cero**: la IA
los escribe en ensamblador para un Commodore 64 o un ZX Spectrum emulados, un jugador robot
los prueba y quedan como un `.prg` o un `.tap` que corre en cualquier emulador.

- Código del emulador y documentación completa: [emu/README.md](emu/README.md)
- Arquitectura: [emu/docs/ARQUITECTURA.md](emu/docs/ARQUITECTURA.md)
- El método para fusionar juegos: [emu/src/Retro.Studio/Docs/GUIA-FUSION.md](emu/src/Retro.Studio/Docs/GUIA-FUSION.md)
- Qué juegos conseguir para probar cada máquina: [emu/docs/JUEGOS-PARA-PROBAR.md](emu/docs/JUEGOS-PARA-PROBAR.md)

## Fusiones incluidas

Cada fusión dibuja una **escena propia** con sprites, tiles y variables de los originales,
que corren en sus máquinas emuladas. El resultado tiene sus reglas, objetivos y controles.
Se juegan desde **Máquina > Juegos que ya tienen fusión** si los juegos están en tu carpeta
`ROMs`. Con **Fusión** podés crear otro juego con las piezas de los que el emulador corra.
El estudio rechaza el uso de `screen.game` para mostrar las pantallas de los originales.

### Manic Invaders — creado con IA

**Space Invaders** (arcade Taito, Intel 8080) + **Manic Miner** (ZX Spectrum 48K, Zilog Z80)

![Manic Invaders](emu/docs/img/fusiones/manic-invaders.png)

Una flota invasora se posa sobre la mina de Miner Willy. Con un láser que encontró en la
mina, Willy junta las llaves de cada caverna y escapa por el portal antes de que los
invasores aterricen o se le acabe el aire. Son las 20 cavernas originales y la flota del
arcade de verdad, que ahora apunta a Willy.

Las capturas son del emulador corriendo las fusiones con copias propias de los juegos; los
juegos no se incluyen.

## Juegos de 8 bits creados desde cero

Menú **Crear > Juego de 8 bits nuevo**: elegís la máquina y, si querés, qué juego te
gustaría; la IA lo escribe en ensamblador con una guía del hardware, el emulador lo
ensambla, lo carga como cualquier juego y un jugador robot lo prueba; si algo falla, la IA
recibe el informe y una imagen de la pantalla y lo corrige. Sin pedido, cada vez parte de
una semilla distinta (género, mundo, tono y una regla rara). Funciona con tu IA por
suscripción o por API, copiando y pegando en un chat, o con un agente que trabaja en una
carpeta.

Estos dos vienen incluidos como ejemplo (originales, escritos solo con las guías):

| Tormenta (Commodore 64) | El jardín de los topos (ZX Spectrum) |
|---|---|
| ![Tormenta](emu/docs/img/creados/tormenta.png) | ![El jardín de los topos](emu/docs/img/creados/topos.png) |
| Un bote junta salvavidas y esquiva rayos; sprites, sonido del SID y puntaje. | Golpear a los topos que asoman en 24 agujeros antes de que se escapen. |

## Usarlo

1. Bajá el paquete de **Releases** (Windows o Linux) y descomprimilo. No hace falta
   instalar nada.
2. Poné tus juegos en la carpeta `ROMs` (sueltos o en .zip). No se incluyen juegos: usá
   copias que tengas derecho a usar.
3. Tus juegos fusionados van en `Juegos fusionados` (cada juego es una carpeta con un
   `.json` y un `.js`; se pueden compartir).
4. Abrí `RetroEmu` y arrastrá un juego a la ventana, o usá **Máquina** y **Fusión**.

Para compilarlo desde el código hace falta el .NET SDK 10:

```bash
cd emu
dotnet build RetroEmu.slnx -c Release
dotnet run --project src/Retro.App -c Release
```

Las pruebas de conformidad grandes (6502) no están en el repositorio; se bajan con
`python emu/tools/bajar-pruebas-6502.py emu/testdata/6502`. Si faltan, esas pruebas se
omiten.

## Qué se incluye y qué no

- Se incluye la ROM del ZX Spectrum 48K (Amstrad permite distribuirla con emuladores) y
  las Open ROMs del Commodore 64 (libres, LGPL 3, con su licencia).
- No se incluye ningún juego ni otra ROM con copyright.
