# RetroEmu

Jugá tus juegos originales, combiná sus reglas o creá un juego de 8 bits.

## Empezar

1. Abrí **RetroEmu.exe** en Windows o **RetroEmu** en Linux.
2. En **Tus juegos**, elegí un título y pulsá **Jugar**.
3. Para agregar uno, usá **Abrir archivo** o arrastralo a la ventana.

También podés guardar tus archivos en la carpeta **ROMs** junto al programa.
Los juegos se consiguen por separado. Los originales siguen disponibles después de crear una fusión.

![Tus juegos](docs/img/interfaz/biblioteca.png)

## Durante la partida

Hacé clic en la pantalla para usar el teclado. **Ayuda > Controles de la partida**
muestra las teclas del juego o sistema activo. También se puede usar un mando.

| Atajo | Acción |
|---|---|
| Ctrl+L | Abrir Tus juegos |
| Ctrl+M | Modificar la partida |
| F2 | Guardar estado rápido |
| F3 / F4 en C64 | Cargar estado rápido |
| F8 | Turbo |
| F9 | Pausa |
| F11 | Pantalla completa |

Las cintas pueden tardar en cargar. Si el joystick del C64 no responde,
cambiá el puerto en **Máquina > Joystick**.

## Fusionar o crear

- **Fusionar juegos:** elegí 2 o 3 originales y proponé una idea, o pedí propuestas a la IA.
- **Crear un juego:** elegí la máquina y describí lo que querés. Podés dejar la idea a elección de la IA.
- **Cambiar una fusión:** pedí otra versión desde el estudio. Las anteriores quedan guardadas.

Elegí cómo trabajar: una herramienta instalada con tu sesión iniciada, copiar y pegar
desde un chat, una carpeta para tu agente o una API. Las APIs tienen sus propias
condiciones y cargos. Las herramientas instaladas usan los límites de tu cuenta.

Durante un pedido, **Abrir construcción** muestra la actividad disponible y permite cancelar.

## Modificar

**Ctrl+M** abre las variables conocidas. Podés cambiar valores, congelarlos o pedir propuestas.
**Volver antes de probar** recupera la partida anterior al primer cambio.
[Ver las opciones de modificación](docs/MODIFICACIONES.md).

![Modificar una partida](docs/img/interfaz/modificaciones.png)

## Sistemas y archivos

| Sistema | Archivos |
|---|---|
| ZX Spectrum 48K | TAP, TZX, Z80, SNA |
| Commodore 64 | T64, PRG |
| NES | NES, mappers 0 y 3 |
| Space Invaders | Conjunto de ROMs de la placa en ZIP |

También se aceptan archivos ZIP. La compatibilidad depende de la edición del juego.
El paquete incluye el firmware redistribuible del Spectrum y Open ROMs para C64,
con sus licencias en **firmware**.

**Ver > Herramientas avanzadas** habilita el visor de código y el depurador.
