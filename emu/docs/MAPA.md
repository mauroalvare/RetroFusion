# Mapa del repositorio y comandos

Fuente única de rutas y comandos de desarrollo. El diseño interno está en
[ARQUITECTURA.md](ARQUITECTURA.md).

## Raíz

| Ruta | Qué es |
|---|---|
| `README.md` | Presentación para quien baja el paquete |
| `retrofusion/` | Kit Python inicial (ensamblador/desensamblador 6502, NES). No lo usa la app; falta su punto de entrada `retro.py`. Se conserva como referencia |
| `emu/` | Todo el emulador |

## emu/

| Ruta | Qué es |
|---|---|
| `RetroEmu.slnx` | Solución con todos los proyectos |
| `src/Retro.Core` | Interfaces (`IMachine`, `ICpuDebugView`, `IMemoryView`), medios y zips, `MemorySearch`, PNG, mapa de código |
| `src/Retro.Cpu.Z80`, `Retro.Cpu.I8080`, `Retro.Cpu.M6502` | Núcleos de CPU con desensamblador |
| `src/Retro.Systems.Spectrum`, `Retro.Systems.Arcade`, `Retro.Systems.C64`, `Retro.Systems.Nes` | Máquinas |
| `src/Retro.Machines` | Catálogo de máquinas (qué máquina es cada archivo) y `MachineLauncher` (arranca una máquina con un juego); lo usan la app, el CLI, las fusiones y la compatibilidad |
| `src/Retro.Compat` | Matriz de compatibilidad: guiones por juego, ejecutor sin ventana con sonda de controles, informe JSON y Markdown, descubrimiento de guiones |
| `src/Retro.Fusion` | Perfiles de juegos (`Profiles/*.json`), recetas (`Recipes/*.json`), `FusionSession`, `MachineFactory` |
| `src/Retro.Studio` | Juegos fusionados con guion JS: sesión, validador, diseñador IA, `Docs/GUIA-FUSION.md` y `Docs/API.md`, juegos incluidos en `Games/` |
| `src/Retro.Ai` | Proveedores de IA (CLIs por suscripción, APIs, Ollama), carpeta para agentes |
| `src/Retro.App` | App de escritorio (raylib + Dear ImGui): `RetroApp`, `EmulatorHost`, `InputMapper`, `MachineCatalog`, ventanas de código, fusión y estudio |
| `src/Retro.Cli` | `retro-cli`: ejecución sin ventana |
| `tests/*` | NUnit: `Retro.Cpu.Z80.Tests`, `I8080.Tests`, `M6502.Tests`, `Zx.Tests`, `Arcade.Tests`, `C64.Tests`, `Compat.Tests`, `Fusion.Tests`, `Studio.Tests` |
| `compat/juegos/*.json` | Un guion de compatibilidad por archivo de juego (106 hoy; los curados tienen `"curated": true`) |
| `lab/*.json` | Hallazgos del laboratorio de ingeniería inversa por juego (hipótesis, evidencia, confianza, quién escribe) |
| `compat/resultados.json` | Último resultado de la matriz con toda la evidencia (lo genera `retro-cli compat`) |
| `testdata/` | Suites de CPU (FUSE, ZEX, 8080; las grandes de 6502 se bajan aparte) |
| `roms/` | Firmware redistribuible: `48.rom`, `c64/` (Open ROMs + licencia) |
| `tools/` | `publish.ps1` (paquetes), `make_tar.py`, `bajar-pruebas-6502.py` |
| `docs/` | ARQUITECTURA, MAPA, DECISIONES, LIMITACIONES, IDEAS, JUEGOS-PARA-PROBAR, COMPATIBILIDAD (generada) |
| `out/` | Capturas y salidas locales (no se versiona) |
| `dist/` | Paquetes generados (no se versiona) |

## Comandos

Desde `emu/` salvo que se indique otra cosa.

```bash
# compilar y probar
dotnet build RetroEmu.slnx
dotnet test RetroEmu.slnx -c Release --filter "TestCategory!=Long"   # rápido: sin ZEXDOC/ZEXALL ni 8080EXM
dotnet test RetroEmu.slnx                                    # todo (~10 min en Debug; ZEXALL ~45 s en Release)
dotnet test tests/C64.Tests                                  # un proyecto
dotnet test tests/Retro.Cpu.Z80.Tests -c Release --filter "TestCategory=Long"   # solo ZEXDOC/ZEXALL

# la app (sin ventana ni sonido para pruebas automáticas; la captura va relativa al directorio actual)
dotnet run --project src/Retro.App -- --hidden --mute --autotest 10 out/prueba.png <juego>
src/Retro.App/bin/Debug/net10.0/RetroEmu.exe                 # con ventana, para el usuario

# retro-cli (C=src/Retro.Cli/bin/Debug/net10.0/retro-cli.exe)
$C info <archivo|zip>                                        # qué máquina reconoce
$C run juego.nes --frames 900 --press 300:P1_START:10 --screenshot out/nes.png   # NES sin ventana
$C run <archivo|zip> --frames 3000 --press 400:SPACE:6 --shot-every 300 --shot-prefix out/x
$C run <c64.t64> --frames 2000 --text --disasm C000:20 --dump 0400:40
$C fuse <receta.json> --games <carpeta-juegos> --ticks 600 --screenshot out/f.png
$C studio manic-invaders --games <carpeta-juegos> --shots out/s

# matriz de compatibilidad (Release es mucho más rápido: la matriz entera tarda ~75 s)
$C compat compat/juegos --juegos C:/Users/mauro/Downloads/ROMs --salida compat/resultados.json --md docs/COMPATIBILIDAD.md --capturas out/compat --dos-veces
$C compat compat/juegos --juegos C:/Users/mauro/Downloads/ROMs --descubrir --solo nada   # solo crea guiones para archivos nuevos
$C compat compat/juegos --solo c64-pac --juegos ... --salida compat/resultados.json       # unos pocos, fusionando con el resultado anterior

# laboratorio de ingeniería inversa: llega al juego con un guion y busca vidas, tiempo y posiciones
$C lab compat/juegos/zx48-manicminer.json --cuadros 12000 --salida lab/zx48-manicminer.json

# fusiones con cualquier juego de la carpeta: ids para las recetas, y preparar uno (partida, variables verificadas, perfil por hash)
$C juegos C:/Users/mauro/Downloads/ROMs
$C preparar C:/Users/mauro/Downloads/ROMs/Spy_vs_Spy_C64_EN.zip --ia agent --carpeta out/agente   # o --ia sub-claude, o --ia ninguna

# paquetes autocontenidos (Windows y Linux) en emu/dist
pwsh tools/publish.ps1 -Version 0.4.0
```

Teclas de `--press`: las de cada máquina (`SPACE`, `ENTER`/`RETURN`, `CAPS SHIFT`,
`KEMPSTON FIRE`, `JOY2 FIRE`, `RUN/STOP`, `F1`, `COIN`, `P1 START`...), unidas con `+`;
en la línea de comandos los espacios se escriben `_` (`JOY2_FIRE`).

## Ubicaciones fuera del repositorio

| Ruta | Qué es |
|---|---|
| `C:\Users\mauro\Desktop\RetroEmu\Programa` | Build autocontenido que usa el usuario (`Abrir RetroEmu.bat`); firmware en `Programa\firmware` |
| `C:\Users\mauro\Desktop\RetroEmu\Juegos fusionados` | Juegos fusionados del usuario (`<id>\vN.json` + `vN.js`) |
| `C:\Users\mauro\Downloads\ROMs` | Juegos del usuario; `revision\` tiene el inventario con hashes (`inventario-roms.json`) y los informes de la auditoría de ROMs |
| `C:\Users\mauro\Desktop\RetroEmu\ROMs` | Copia de los juegos que usa la app del Escritorio |
| `%APPDATA%\RetroEmu` | Estados guardados, anotaciones de código y configuración de IA de la app |
