# RetroEmu 0.4.0

## Español

Elegí tu interfaz, dale tus colores y conservá tus partidas y notas con más protección.

En el primer inicio y después de actualizar, **Novedades** te cuenta los cambios en el idioma elegido. Si venís de una versión anterior, por ejemplo la 0.2.6, reúne las novedades de las entregas intermedias. Podés cambiar de idioma en el cartel y volver al historial desde Ayuda o la terminal.

- **Nueva función · Elegí cómo querés usar RetroEmu.** Retro, Explorador, Salón y Terminal real tienen distribuciones y navegación distintas. Podés elegir al instalar, después de cada actualización o desde Preferencias.
- **Nueva función · Tus colores, en cada interfaz.** Cada interfaz recuerda su paleta. Probá los temas, incluido Ámbar, o elegí libremente ocho colores RGB; la vista previa permite aplicar o cancelar.
- **Nueva función · Todo en español o en inglés.** Elegí el idioma en Preferencias. Los menús, la ayuda, los mensajes y los comandos de Terminal real acompañan esa elección. Tus nombres, rutas y textos se conservan tal como los escribiste.
- **Nueva función · Novedades que te ponen al día.** Este panel aparece al empezar y al actualizar. Si salteás versiones, reúne sus novedades; podés cambiar de idioma aquí y volver al historial desde Ayuda o la terminal.
- **Nueva función · Una salida si el programa no llega a abrir.** El arranque de emergencia permite revisar requisitos y buscar una actualización aunque falle la interfaz principal. En Windows, si falta Visual C++, la reparación usa el instalador oficial de Microsoft y pide permiso antes de descargarlo y antes de ejecutarlo.
- **Mejora · Arranque con lo necesario.** Los paquetes incluyen el runtime de .NET. Podés abrir sin sonido con --no-audio; la falta del firmware de una máquina no bloquea la biblioteca ni los cartuchos de otros sistemas. Linux comprueba sus bibliotecas del sistema antes de abrir.
- **Corrección · Actualizar con más protección.** Los archivos del programa se reemplazan de forma atómica y el actualizador puede recuperar una instalación interrumpida. Se conservan tus juegos y datos; también se completa la limpieza pendiente al venir de la 0.3.7.
- **Nueva función · El progreso de los cartuchos con batería.** Los cartuchos NES compatibles con batería de 8 KB guardan su progreso entre sesiones y al reiniciar. Desde Juego o la terminal podés exportar una copia y recuperar el respaldo anterior.
- **Nueva función · Tus notas, junto al código.** Comentarios y etiquetas se comparten entre el editor y la terminal, separados por edición del juego. Podés importar las anotaciones anteriores de forma explícita y exportar tus notas.
- **Corrección · Conservar antes de salir.** Si una batería o unas anotaciones no se pueden guardar, podés volver a la sesión, recuperar el archivo con copia previa o conservar una exportación verificada. Los archivos dañados o de otra versión no se sobrescriben a ciegas.
- **Mejora · Una terminal más cómoda.** Terminal real reemplaza el nombre anterior. El buscador ofrece comandos con parámetros y ejemplos: al elegir uno prepara la línea para que la revises antes de ejecutarla. También mejora el prompt en ventanas angostas.
- **Corrección · Favoritos que no se pierden.** Un favorito ausente sigue visible y recupera su marca cuando vuelve el mismo contenido. La búsqueda y los selectores distinguen mejor las ediciones y los archivos.
- **Corrección · Cambios manuales con vuelta atrás.** Los parches se aplican de forma transaccional: si algo falla, conservan el estado anterior y los respaldos. Los archivos de parches incompletos o inválidos quedan protegidos.
- **Mejora · Talleres con resultados claros.** Los talleres distinguen un trabajo completo, un resultado parcial, una cancelación y un error. Si falla el guardado de anotaciones, la respuesta permanece en la sesión. También se evitan tareas duplicadas al reintentar.

### Para empezar

- **Windows x64:** descomprimí el ZIP y abrí `RetroEmu.exe`. Windows 10 o posterior y OpenGL 3.3. El runtime .NET está incluido; jugar no requiere SDK ni herramientas de IA.
- **Linux x64:** extraé el TAR.GZ y ejecutá `sh RetroEmu.sh --check` para revisar requisitos. Requiere glibc 2.38 o posterior, X11/XWayland, OpenGL 3.3 y las bibliotecas del sistema indicadas por el comprobador. El runtime .NET está incluido.
- **Actualizar:** usá Ayuda > Actualizaciones. Se conserva la carpeta de instalación, tus juegos, firmware y ajustes. Si la interfaz principal falla, probá `RetroEmu.exe --repair` en Windows.
- Los juegos se agregan por separado. La compatibilidad depende del sistema y del formato; no se afirma compatibilidad universal. Las baterías NES cubren cartuchos compatibles con guardado de 8 KB.

## English

Choose your interface, make its colours your own, and protect your saves and notes.

On first launch and after updates, **What's new** explains the changes in your selected language. If you come from an older version, such as 0.2.6, it collects news from the releases you skipped. Switch languages in the panel and revisit the history from Help or the terminal.

- **New feature · Choose how you use RetroEmu.** Retro, Explorer, Lounge and Real terminal have different layouts and navigation. Choose on first launch, after every update or in Preferences.
- **New feature · Your colours, in every interface.** Each interface remembers its palette. Try the themes, including Amber, or freely choose eight RGB colours; preview them before applying or cancelling.
- **New feature · Everything in Spanish or English.** Choose your language in Preferences. Menus, help, messages and Real terminal commands follow your choice. Your names, paths and writing stay exactly as you entered them.
- **New feature · Catch up with what's new.** This panel appears on first launch and after updates. If you skip versions, it collects their news; switch language here and revisit the history from Help or the terminal.
- **New feature · A way forward when startup fails.** Emergency startup lets you review requirements and check for an update even if the main interface fails. On Windows, if Visual C++ is missing, repair uses Microsoft's official installer and asks before downloading and before running it.
- **Improvement · Start with what you need.** Packages include the .NET runtime. Start without sound with --no-audio; missing firmware for one machine does not block the library or other systems' cartridges. Linux checks its system libraries before launch.
- **Fix · Safer updates.** Program files are replaced atomically and the updater can recover an interrupted installation. Your games and data are preserved; pending cleanup from 0.3.7 is handled too.
- **New feature · Progress in battery-backed cartridges.** Supported NES cartridges with an 8 KB battery save keep progress between sessions and resets. Export a copy or recover the previous backup from Game or the terminal.
- **New feature · Your notes alongside the code.** Comments and labels are shared between the editor and terminal, separately for each game edition. You can explicitly import earlier annotations and export your notes.
- **Fix · Preserve your work before leaving.** If a battery save or annotations cannot be saved, return to the session, recover the file while keeping a copy, or preserve a verified export. Damaged files and files from other versions are not blindly overwritten.
- **Improvement · A more comfortable terminal.** Real terminal replaces the previous name. Search offers commands with parameters and examples: choosing one prepares the line for review before execution. The prompt also fits narrow windows better.
- **Fix · Favourites that stay yours.** A missing favourite stays visible and regains its star when the same content returns. Search and selectors distinguish editions and files more clearly.
- **Fix · Manual changes you can undo.** Patches are applied transactionally: if something fails, the previous state and backups are kept. Incomplete or invalid patch files stay protected.
- **Improvement · Clear workshop results.** Workshops distinguish completed work, partial results, cancellation and errors. If saving annotations fails, the response stays in the session. Retries also avoid duplicate tasks.

### Getting started

- **Windows x64:** extract the ZIP and open `RetroEmu.exe`. Windows 10 or later and OpenGL 3.3 are required. The .NET runtime is included; playing needs no SDK or AI tools.
- **Linux x64:** extract the TAR.GZ and run `sh RetroEmu.sh --check` to review requirements. Requires glibc 2.38 or later, X11/XWayland, OpenGL 3.3 and the system libraries listed by the checker. The .NET runtime is included.
- **Updating:** use Help > Updates. The installation folder, games, firmware and settings are preserved. If the main interface fails, try `RetroEmu.exe --repair` on Windows.
- Add games separately. Compatibility depends on the system and format; universal compatibility is not claimed. NES battery support covers compatible cartridges with 8 KB saves.
