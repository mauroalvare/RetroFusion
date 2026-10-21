# RetroEmu

Jugá tus juegos originales, combiná sus reglas o creá un juego de 8 bits.

## Empezar

1. Abrí **RetroEmu.exe** en Windows o **RetroEmu** en Linux.
2. Elegí una máquina o **Todos los juegos**, buscá un título y pulsá **Jugar**.
3. Para agregar uno, usá **Abrir archivo** o arrastralo a la ventana.

El paquete trae estas carpetas junto al programa:

| Carpeta | Qué va ahí |
|---|---|
| **ROMs** | Tus juegos originales, sueltos o en ZIP; admite subcarpetas. |
| **Juegos fusionados** | Las carpetas completas de tus fusiones. |
| **Juegos creados** | Las carpetas completas de los juegos que creaste. |

Los juegos se consiguen por separado. Los originales siguen disponibles después de crear una fusión.
Los archivos sueltos de **ROMs** se ordenan automáticamente por sistema. Los dudosos
quedan donde están; se respetan tus subcarpetas y los archivos con nombres repetidos.
Para mostrar una carátula o captura, poné una imagen PNG o JPG con el mismo nombre
junto al juego. Si no hay imagen, se muestra el sistema al que pertenece.
Para actualizar, cerrá RetroEmu, copiá la entrega nueva sobre la anterior y combiná
las carpetas conservando tus juegos y firmware. Ver **ACTUALIZAR.txt**.

![Inicio con selector de máquinas](docs/img/interfaz/biblioteca.png)

## Durante la partida

Hacé clic en la pantalla para usar el teclado. **Ayuda > Controles de la partida**
muestra las teclas del juego o sistema activo. También se puede usar un mando.

| Atajo | Acción |
|---|---|
| Ctrl+L | Volver a Inicio |
| Ctrl+O | Abrir un archivo |
| Ctrl+M | Modificar la partida |
| F2 | Guardar estado rápido |
| F3 / F4 en C64 | Cargar estado rápido |
| F8 | Turbo |
| F9 | Pausa |
| F11 | Pantalla completa |

Las cintas pueden tardar en cargar. Si el joystick del C64 no responde,
cambiá el puerto en **Máquina > Joystick**.
Los estados guardados están disponibles en originales. Si un estado está dañado,
la partida actual se conserva.

## Fusionar o crear

- **Fusionar juegos:** elegí 2 o 3 originales y cuál es la base. Conserva sus mapas,
  aspecto y reglas, con personajes o mecánicas de los otros dentro de ese mundo.
  **Reinterpretación libre** permite una escena nueva si preferís esa opción.
- **Crear un juego:** elegí la máquina y describí lo que querés. Podés dejar la idea a elección de la IA.
- **Cambiar una fusión:** pedí otra versión desde el estudio. Las anteriores quedan guardadas.

Elegí cómo trabajar: una herramienta instalada con tu sesión iniciada, copiar y pegar
desde un chat, una carpeta para tu agente o una API. Las APIs tienen sus propias
condiciones y cargos. Las herramientas instaladas usan los límites de tu cuenta.
**Ayuda > Conectar IA** guía la instalación y el acceso; después muestra los modelos
y esfuerzos que publica cada herramienta. [Ver los pasos](docs/CONECTAR-IA.md).

Durante un pedido, **Abrir construcción** muestra la actividad disponible y permite cancelar.
**Uso de IA** permite ajustar los intentos y el tope de texto. Los trabajos interrumpidos
del estudio aparecen como pendientes para continuar.
Los juegos nuevos se guardan en las carpetas incluidas. Si ya tenés una biblioteca
en una carpeta superior o en **Documentos/RetroEmu**, se sigue usando mientras las
carpetas nuevas solo tengan ayuda.

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
