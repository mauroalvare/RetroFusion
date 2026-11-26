# RetroEmu 0.4.2

## Español

Capturas ordenadas por juego, un atajo a tu medida y el taller de código desde la terminal.

Esta entrega conserva las interfaces, colores, idiomas, partidas, notas y las mejoras de IA de 0.4.1.

**Novedades** aparece después de actualizar, en tu idioma. Al venir de 0.4.1 muestra estos cambios; si salteaste versiones, reúne también las novedades intermedias. El historial sigue disponible desde Ayuda o la terminal.

- **Nueva función · Capturas ordenadas por juego.** Cada captura se guarda en Imágenes/RetroEmu, en una carpeta propia del juego y con la fecha en el nombre. Nunca reemplaza capturas anteriores; sin un juego abierto van a una carpeta aparte.
- **Mejora · Elegí tu atajo de captura.** F1 captura la pantalla; en PC y C64, donde F1 es una tecla del juego, se usa Ctrl+F1. Podés cambiarlo en Preferencias > Controles o desde la terminal. Si el atajo choca con el juego o con otra herramienta, se rechaza y se explica por qué.
- **Corrección · Capturar ya no toca el juego.** El atajo anterior podía llegar a la máquina emulada como una tecla más. Ahora la combinación de captura no se envía al juego.
- **Nueva función · El taller de código, también desde la terminal.** Terminal real permite ver referencias, preparar parches con vista previa del antes y el después, aplicarlos, deshacerlos o reaplicarlos, exportar código y perfiles, y agregar, cambiar, congelar o deshacer variables, con las mismas protecciones que la interfaz.
- **Corrección · Exportar código sin cerrar el programa.** Exportar .asm crea siempre un archivo nuevo y no deja archivos a medias. Si no se puede escribir, se muestra un aviso en lugar de cerrar RetroEmu.
- **Corrección · Investigaciones bloqueadas a la vista.** Una investigación que se frena porque falta algo queda como Bloqueada, no como terminada. La terminal muestra las mismas investigaciones que el taller para que puedas retomarlas.
- **Corrección · Avisos claros en la línea de comandos.** Un número inválido o incompleto en las opciones de retro-cli indica qué opción revisar y termina con un código de error, en lugar de cerrarse con un error interno.

### Para empezar

- **Windows x64:** extraé el ZIP y abrí `RetroEmu.exe`. Requiere Windows 10 o posterior y OpenGL 3.3. Incluye .NET; jugar no requiere SDK ni herramientas de IA.
- **Linux x64:** extraé el TAR.GZ y ejecutá `sh RetroEmu.sh --check`. Requiere glibc 2.38 o posterior, X11/XWayland, OpenGL 3.3 y las bibliotecas del sistema indicadas por el comprobador.
- **Actualizar:** Ayuda > Actualizaciones. Se conserva la misma carpeta con tus juegos, firmware, proyectos y ajustes. Si la interfaz falla, Windows ofrece `RetroEmu.exe --repair`.

### Alcance de la revisión

Los cambios se comprobaron con la suite automática y con pruebas de la interfaz en ventana oculta. Linux gráfico, Windows 10, teclados y mandos físicos y otros DPI necesitan sus propias pruebas. Los juegos se agregan por separado; no se promete compatibilidad universal. Las [novedades de 0.4.1](NOVEDADES-0.4.1.md) describen las mejoras que esta entrega conserva.

## English

Screenshots sorted by game, a shortcut you choose and the code workshop from the terminal.

This release keeps the interfaces, colours, languages, saves, notes and AI improvements from 0.4.1.

**What's new** appears after updating, in your selected language. Updating from 0.4.1 shows these changes; skipping versions also collects the intermediate release notes. The history remains available from Help or the terminal.

- **New feature · Screenshots sorted by game.** Each screenshot is saved in Pictures/RetroEmu, in a folder for that game, with the date in its name. Earlier screenshots are never replaced; without an open game they go to a separate folder.
- **Improvement · Choose your screenshot shortcut.** F1 takes a screenshot; on PC and C64, where F1 belongs to the game, Ctrl+F1 is used. Change it in Preferences > Controls or from the terminal. A shortcut that clashes with the game or another tool is rejected with the reason.
- **Fix · Screenshots no longer press game buttons.** The previous shortcut could reach the emulated machine as a regular key. The screenshot combination is no longer sent to the game.
- **New feature · The code workshop, from the terminal too.** Real terminal can list references, prepare patches with a before-and-after preview, apply, undo or reapply them, export code and profiles, and add, change, freeze or undo variables, with the same safeguards as the interface.
- **Fix · Exporting code no longer closes the program.** Exporting .asm always creates a new file and never leaves partial files. If it cannot be written, a message is shown instead of closing RetroEmu.
- **Fix · Blocked investigations are visible.** An investigation that stops because something is missing is marked Blocked, not finished. The terminal lists the same investigations as the workshop so you can resume them.
- **Fix · Clear command-line messages.** An invalid or incomplete number in retro-cli options says which option to check and exits with an error code, instead of crashing with an internal error.

### Getting started

- **Windows x64:** extract the ZIP and open `RetroEmu.exe`. Requires Windows 10 or later and OpenGL 3.3. .NET is included; playing needs no SDK or AI tools.
- **Linux x64:** extract the TAR.GZ and run `sh RetroEmu.sh --check`. Requires glibc 2.38 or later, X11/XWayland, OpenGL 3.3 and the system libraries listed by the checker.
- **Updating:** Help > Updates. Your installation folder, games, firmware, projects and settings are preserved. If the main interface fails, Windows offers `RetroEmu.exe --repair`.

### Scope of verification

The changes were checked with the automated suite and hidden-window interface tests. Linux desktop, Windows 10, physical keyboards and controllers and other DPI settings need their own checks. Add games separately; universal compatibility is not claimed. [What changed in 0.4.1](NOVEDADES-0.4.1.md#english) describes the improvements retained by this release.
