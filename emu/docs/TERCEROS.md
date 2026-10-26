# Componentes de terceros

**StbImageSharp 2.30.16** decodifica imágenes PNG y JPEG en C#. No requiere otro
programa instalado ni un servicio externo. El paquete declara la licencia
`Unlicense OR MIT`; esta distribución lo utiliza bajo Unlicense.

- Proyecto y código: [StbSharp/StbImageSharp](https://github.com/StbSharp/StbImageSharp).
- Paquete y licencia: [StbImageSharp 2.30.16](https://www.nuget.org/packages/StbImageSharp/2.30.16).
- Texto de [Unlicense](https://unlicense.org/).

Las ilustraciones del hardware se dibujan con código propio. Las imágenes de los
juegos agregadas por el jugador conservan sus derechos y permanecen en su biblioteca.

## Base de PC/XT

Esta carpeta incluye componentes independientes que se ejecutan dentro del PC emulado.
RetroEmu no es autor de estos componentes. No se distribuye MS-DOS ni un BIOS de IBM.

- **GLaBIOS 0.4.2, compilación 8P:** © 2022–2026 640KB y colaboradores, GPL versión 3.
  Fuentes: sources/glabios-source.zip. Licencia: licenses/glabios-GPL-3.txt.
  Los mapas de caracteres de VileR conservan CC BY-SA 4.0, sin cambios;
  licencia en licenses/font-CC-BY-SA-4.0.txt. Se encuentran en GLABIOS.ASM.
- **FreeDOS kernel 2043, 8086/FAT16:** © Pasquale J. Villani y colaboradores del
  proyecto FreeDOS, GPL versión 2 o posterior. Fuentes completas:
  sources/kernel-source.zip. Licencia: licenses/kernel-GPL-2.txt.
- **FreeCOM 0.86:** sus autores y colaboradores, GPL versión 2 o posterior.
  Fuentes completas: sources/freecom-source.zip. Licencia: licenses/freecom-GPL-2.txt.

Los tres binarios se conservan sin cambios. origin.json registra procedencia, versiones
y SHA-256. Las fuentes de GLaBIOS incluyen src/, README y LICENSE originales; se
omitieron capturas y una herramienta de grabación precompilada. Su fuente sí se conserva.
Los archivos de fuentes contienen las instrucciones de compilación de cada proyecto.
Las licencias y fuentes se entregan con cada paquete del emulador, también sin conexión.

RetroEmu construye su propio disquete FAT12 en memoria con un cargador y una pequeña
señal de fin de arranque propios (PcDos.cs, licencia del proyecto). Las copias privadas
de los archivos del usuario no se distribuyen. No se incluyen juegos ni datos de juegos.

Proyectos originales:
https://github.com/640-KB/GLaBIOS/releases/tag/v0.4.2
https://github.com/FDOS/kernel/releases/tag/ke2043
https://github.com/FDOS/freecom/releases/tag/com086
