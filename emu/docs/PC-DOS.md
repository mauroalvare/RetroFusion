# PC y DOS

PC/XT admite disquetes de arranque **IMG o IMA de 160 a 720 KB**, gráficos CGA y altavoz.
BIOS, sistema operativo y juegos se agregan por separado.

1. Poné un BIOS compatible de PC/XT en **firmware/pcxt/pcxt-bios.rom**, junto al programa.
   [GLaBIOS](https://github.com/640-KB/GLaBIOS) ofrece firmware libre; usá una compilación
   para PC/XT con CGA y 8088.
2. Agregá a **ROMs** un disquete que incluya su propio sistema de arranque y el programa.
   [FreeDOS](https://www.freedos.org/download/) ofrece imágenes libres; elegí una de
   720 KB. Un ZIP con EXE/COM sueltos necesita primero prepararse como disco DOS.
3. Abrí la imagen desde **Biblioteca > IBM PC / XT**. Seguí las indicaciones del sistema
   cargado; una imagen de instalación puede abrir su propio instalador.

Hacé clic en la pantalla para usar el teclado. La distribución es estadounidense.
F1–F10 llegan al PC; los atajos del emulador usan **Ctrl+Fn**. Para volver a la biblioteca,
usá **Ctrl+Shift+L**. El menú muestra los atajos correspondientes.

**Juego > Partidas guardadas** conserva la máquina y las escrituras del disquete.
Las escrituras no cambian la imagen original. Para conservarlas entre sesiones,
guardá la partida antes de cerrar y cargala después con la misma imagen y BIOS.

No admite todavía disco duro, alta densidad, EGA/VGA ni tarjetas de sonido. El análisis
de variables y las fusiones de PC siguen pendientes. [Detalle técnico](PCXT.md).
