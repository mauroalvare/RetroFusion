# Crear, comprobar y continuar un juego

La creación usa el pedido vigente, los datos de esa edición y una sola versión completa
del programa. Una corrección conserva las reglas y partes que funcionan. La guía técnica,
el contrato de diseño y la respuesta esperada se comparten entre proveedor, copiar y pegar
y carpeta para agentes; cada modo tiene su forma de entregar el código.

## Copiar y pegar

En **Crear** elegí el modo manual, prepará la consigna y llevala a tu chat. Traé la ficha
JSON y un único bloque con el programa completo. El ejemplo de la consigna indica los
campos; en una fusión también lleva `playtests`.

Si aparece un campo con tipo o longitud incorrectos, corregí ese campo y enviá la ficha
completa. Si `source`/`script` y el bloque de código contienen versiones diferentes, elegí
la versión vigente y reenviá una sola. Una ficha truncada no se completa con valores
predeterminados para darla por válida. Las respuestas antiguas con solo código siguen
admitiéndose; no sirven para acreditar datos de diseño que no incluyen.

En una fusión manual el guion va en el segundo bloque, fuera del JSON. Sus recorridos
pueden medir cambios finales, extremos y repeticiones: [formato de comprobación](../src/Retro.Studio/Docs/GUIA-FUSION.md#recorridos-dirigidos).

## Carpeta de un agente

Leé `TAREA.md`. En el creador desde cero, el programa va en `juego.s` y la ficha en
`juego.json`; en el estudio, el guion va en `juego.js`. Guardá ambos archivos completos.
Cambiar solo la ficha también vuelve a comprobar el juego. Mientras un archivo está
incompleto, el creador espera y muestra el problema; conserva los archivos del agente.

El informe técnico indica qué se observó. Corregí la causa, guardá y dejá que se repita la
prueba. En el creador podés usar **Volver a probar** después de una cancelación o cuando
quieras repetir la comprobación sin editar el programa.

## Guardado y versiones recuperadas

Continuar el mismo trabajo con el mismo contenido reutiliza su guardado. Cambiar solo la
ficha, un recorrido o los originales también cuenta como una versión nueva, aunque no
cambie el guion. La anterior se conserva y la recuperación sigue dentro del mismo juego.

Una versión aparece cuando están publicados tanto su programa como su ficha. Los archivos
previos no se sobrescriben para completar un guardado interrumpido: se usa otro número.
Si queda un guion sin ficha tras una interrupción, conservá el trabajo pendiente y usá
**Continuar**; no hace falta borrar carpetas para reintentar. El listado de recuperación
salta las fichas dañadas y muestra hasta treinta trabajos válidos sin modificar los demás.

## Actividad, cancelación y presupuestos

La prueba del creador muestra ensamblado, carga, arranque, dibujo, controles y partida
robot. Donde el número de cuadros está definido muestra el avance de esa etapa; no es
un porcentaje de calidad ni una estimación del trabajo total. **Cancelar** se atiende
entre cuadros, durante la carga de cinta y durante las pruebas de controles.

El modo automático guarda la respuesta completa antes de probarla. Si se cancela o se
alcanza un límite, el borrador queda en **Trabajos guardados** y se vuelve a comprobar al
continuar antes de pedir otra respuesta. La carpeta del agente conserva sus archivos.
En el modo manual, mantené la respuesta en tu chat para volver a pegarla si cerrás ese modo.

El creador limita cada tanda a cinco intentos como máximo; la configuración normal puede
usar menos, además de los límites de tiempo y texto vigentes. No se repite un programa que
ya falló sin un cambio. Las capturas se adjuntan solo a herramientas que declaran soporte
visual; las demás reciben el informe de texto y el dibujo aproximado disponible.

## Si la herramienta de IA queda esperando

La actividad distingue **Enviando pedido al CLI** de **Pedido entregado al CLI; esperando
respuesta**. El segundo estado indica que terminó de escribirse la entrada del proceso
local; no confirma una respuesta del modelo. Al agotarse el plazo, el aviso indica la
etapa y conserva el diagnóstico. No tomes las advertencias aisladas como causa confirmada.

Abrí **Modelo y conexión → Conexión avanzada**. **Probar conexión** envía un pedido breve
y cuenta para los límites de tu plan; se puede detener con **Cancelar prueba**. Cancelar
no inicia otra consulta. Si una consigna breve también queda esperando, conservá el aviso
y la actividad observada antes de repetir una creación larga. Cambiar de proveedor o
cerrar la app solicita detener la prueba en curso. Un tiempo agotado sin métricas deja el
consumo desconocido, sin convertirlo en cero.

## Preparación y apertura de una fusión

Una variable marcada como hipótesis no alcanza para dar por resuelto un dato esencial.
La investigación conserva las observaciones nuevas y se detiene tras tres rondas sin
evidencia distinta; volver a datos ya vistos o cambiar su orden no cuenta como avance.
Si sigue faltando una regla, revisá el requisito y su experimento en el plan del original.
Las variables antiguas declaradas por un perfil conservan el alcance de esa ficha.

Un perfil recién preparado conserva el vínculo con su archivo aunque la biblioteca
encuentre otro alias para el mismo original. Al volver a abrir el juego se comprueban
máquina y huella; un nombre parecido no autoriza usar una edición distinta.

Durante el arranque podés usar **Cancelar**. Si pasan dos minutos sin terminar, se detiene
esa carga y se conserva el juego guardado. Abrí el original desde la biblioteca y comprobá
su carga y el plan de arranque antes de repetir. Un error de inicialización muestra su
causa y un resultado que llega después de cancelar no sustituye el juego en uso.

## Alcance de la aprobación

En programas desde cero se comprueban ensamblado, carga, dibujo, respuesta sostenida a
controles conocidos y una partida robot. Esa prueba todavía no describe todos los géneros
ni acredita objetivos, finales o diversión. Un juego de decisiones discretas puede requerir
un comprobador distinto; no se debe convertir en un juego genérico para pasar el robot.

En fusiones, los recorridos del host acreditan las situaciones y variables declaradas,
con muestras a 60 Hz. Una escritura con efecto no demuestra por sí sola su significado.
Si falta una regla esencial, la investigación debe intentar reproducirla. Cuando haga
falta intervención humana, el informe debe decir dónde actuar, qué hacer, qué esperar y
qué entradas, valores o capturas conservar para que el laboratorio lo contraste.

[Preparar una fusión](PREPARAR-FUSION.md) · [Investigación automática](ANALISIS-AUTOMATICO.md) · [Límites](LIMITACIONES.md)
