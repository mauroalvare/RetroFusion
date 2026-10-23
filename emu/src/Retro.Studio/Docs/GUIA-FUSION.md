# Método para fusionar originales conservando su identidad

Por defecto, fusionar significa conservar un juego como base e incorporar comportamientos
del otro dentro de su mundo. La orientación importa: invertir la base produce otra fusión.
El jugador debe reconocer el original por sus mapas, gráficos, reglas, interfaz y ritmo.
No conviertas automáticamente la combinación en un tercer juego de estética genérica.
Crear con originales es una modalidad independiente: diseña una escena y un juego nuevos
a partir de esos recursos. Se abre desde Crear; no es la dirección alternativa de una fusión.

## 1. Entender qué hace especial a cada original

Para cada juego identificá su verbo, objetivo, riesgo, recurso y una regla reconocible.
En la base, identificá también mapas, distribución, escala, paleta, animación, tipografía,
marcadores, menús y sonidos que se deben conservar. Separá esos invariantes de lo que
aporta el otro original. No cambies todos los sistemas para justificar una idea nueva.
Buscá relaciones entre esas reglas, no solo parecidos visuales. Describí una consecuencia
compartida: lo que hacés con una regla debe cambiar cómo tomás decisiones con la otra.
Aplicá también el contrato común de diseño incluido en el pedido.

## 2. Encontrar el punto de unión

Proponé una **regla inseparable**: explicá cómo una acción procedente de un juego transforma
las decisiones del otro. Escribí un ejemplo concreto de diez segundos de partida.

- **Prueba de quitar un original:** si quitás cualquiera de los juegos y el bucle principal
  sigue siendo el mismo, falta integración. Un aporte solo gráfico o sonoro no alcanza.
- Respetá la base declarada en el pedido. Diseñá el injerto de movimientos, enemigos,
  acciones, recursos o reglas del aportante dentro de ella. Puede conservarse el reparto
  original o cambiar protagonistas si lo pide el diseño; tienen que poder hacer algo del
  otro juego y afectar situaciones reales de la base, no limitarse a decorar.
- Solo en reinterpretación libre puede no haber un original dominante ni un género heredado.
- Elegí un protagonista, varios, un tablero o un sistema según lo que se juega. No todos
  los diseños necesitan un héroe, vidas, disparos, plataformas o enemigos.
- Conservá lo reconocible de cada original, transformá sus reglas y explicá qué descartás.
  Nunca inventes direcciones de memoria ni capacidades que los perfiles no ofrecen.

## 3. Diferenciar las propuestas y las versiones existentes

Compará tus ideas con las fusiones anteriores incluidas en el pedido. Cambiar nombre,
color, cartel o posición del HUD no constituye una idea distinta. Variá el bucle de
decisiones incorporado o la relación entre los juegos. Conservar el mismo mapa, paleta
o HUD del original elegido como base es correcto y no cuenta como repetir una plantilla.
Si el jugador pide una corrección pequeña, conservá su juego; si pide una fusión nueva,
justificá en qué cambia realmente.

Para cada propuesta describí la **unión mecánica**, su **regla única**, su **identidad**
visual y jugable y la **diferencia** frente a las otras ideas y fusiones previas.

## 4. Diseñar la partida y su presentación juntos

Definí objetivo, controles y condición de final adecuados al género. La progresión puede
ser una sala que evoluciona, rondas, un recorrido continuo, un mapa, una campaña o una
partida breve. No hay una cantidad obligatoria de niveles ni una plantilla de menús.
En modo base heredá primero el recorrido, los objetivos, los controles y la progresión
del original. Si se piden mapas adicionales, usá su estructura y piezas verificadas,
con la misma escala y lenguaje visual. Si falta el formato de mapas, señalá ese dato
pendiente; no sustituyas los escenarios por niveles inventados con rectángulos genéricos.

El título puede integrarse en el escenario, ser jugable o aparecer de forma breve. Una
transición puede ocurrir dentro del mundo. Los avisos pueden ser animaciones, sonidos,
cambios en el entorno o instrucciones contextuales. No detengas cada novedad con un cartel.
El HUD puede ser lateral, mínimo, diegético o no hacer falta: mostrale al jugador solo la
información que necesita, en el lugar y momento que le sirva.

Los mensajes del emulador y los estados para la prueba automática **no son un menú de
juego obligatorio**. En modo base conservá el lenguaje visual del original. No le agregues
una pantalla de título, tarjetas ni un HUD universal que tape o reemplace los suyos.

## 5. Escenario reconocible y controles claros

- En modo base el motor conserva la imagen original; `screen.base(games.<base>)` permite
  reponerla dentro de draw(). Para ampliar mapas necesitás el formato y los cambios de
  memoria verificados del original, manteniendo su motor. Una reconstrucción con otra
  escena corresponde al modo libre; no la uses para evadir datos que faltan.
  Los añadidos usan sus coordenadas, escala, límites y colisiones. No son otra ventana jugable.
- En reinterpretación libre dibujá la escena elegida con sprites, tiles y datos originales.
- Para personajes o tiles con varios colores, `g.capture` conserva un recorte pequeño
  verificado de la imagen nativa. Guardalo y reutilizalo; los límites impiden capturar
  una pantalla entera. No reemplaza analizar posiciones, movimiento ni colisiones.
- En modo base el motor compone la imagen original antes de `draw()`. Agregá los aportes
  sobre ella, sin borrarla con `screen.clear` ni cambiar su resolución. La creación libre
  permite una escena distinta. Los datos ausentes de mapas o personajes no autorizan a
  sustituir el original por una versión genérica más fácil de programar.
- `screen.game` sigue retirado. No pegues pantallas completas de aportantes, miniaturas
  ni mosaicos con `g.pixel`; esa función sirve para observar, no para copiar cuadros.
- Mantené legibles el espacio, las amenazas y los elementos interactivos. No hay una
  distribución obligatoria de enemigos arriba y plataformas abajo: resolvelo según este
  diseño, sin ocultar al jugador ni las amenazas detrás del terreno.
- Usá pocos controles y explicalos dentro de la presentación elegida. En un juego de
  disparos, Espacio/Ctrl disparan; si hay salto y disparo, diferenciá las acciones.
- No copies la presentación de una fusión anterior como un conjunto. Heredar la del
  original base sí es parte del objetivo. Los aportes nuevos deben encajar con ella.

Antes de escribir, describí una situación concreta en la que una acción del aportante
cambie lo que conviene hacer en la base y la base condicione esa acción. Si al quitar
un original solo desaparece una decoración o un minijuego separado, rediseñá ese vínculo.
En una revisión conservá lo que el jugador reconoce; ajustá respuesta, colisiones,
ritmo y comunicación antes de sumar sistemas o reemplazar la presentación.

## 6. Instrumentación para poder probarlo

El guion informa `game.state('title')`, `game.state('playing')` y `game.state('gameover')`,
además de `game.score`, `game.death` y `game.level` cuando corresponde. Es el contrato de
la prueba, no una orden de dibujar tres pantallas, tres niveles o un marcador convencional.

En modo base se puede empezar directamente con game.state('playing'), conservar el
inicio nativo o hacer una introducción breve dentro del mundo. No agregues puntos si
el original usa otros objetivos: informá el progreso real. En modo libre, el estado
inicial permite entender objetivo y controles y empezar con start, Espacio o disparo.
El fin permite reiniciar con
start, restaurando variables y los juegos originales que se usen. El contador de nivel
puede representar una fase o ronda; no exige tarjetas entre ellas.

## 7. Usar las máquinas originales con criterio

Reutilizá comportamientos originales cuando suman al diseño; el guion los conecta y puede
transformar sus consecuencias. Un original puede estar pausado si se usan sus datos o
recursos para implementar su regla dentro de la nueva escena. Cada juego debe aportar
una mecánica identificable aunque no haga falta ejecutar su pantalla todo el tiempo.

Controlá efectos ocultos que rompan la partida: pausar, reiniciar, ajustar velocidad y
volumen o usar ayudas del perfil. Convertí coordenadas con una sola función y usala para
dibujar y para los choques. Si falta un dato, indicá la limitación; no lo adivines.

La disponibilidad de una ROM no demuestra que conozcas sus rutinas. Trabajá con las
variables, controles, sprites, ayudas y huellas realmente proporcionados en el pedido.
Una captura o el nombre del archivo no justifican inventar direcciones. Si el perfil
no permite la interacción central, explicá qué dato debe prepararse antes de implementarla.

Una escritura puntual con `g.set` puede ser reemplazada por el original dentro del
cuadro siguiente. Si la regla exige un valor constante, `g.freeze` lo mantiene durante
las escrituras de CPU; `g.release` lo libera. No congeles por rutina ni bloquees los
contadores que hacen avanzar una transición. Respetá rangos, máscaras y solo lectura.
Comprobá por separado el estado de juego, el buffer gráfico y las colisiones: desplazar
un sprite no demuestra que se haya desplazado su entidad. Verificá también liberar y
reiniciar. Estos métodos sirven en fusiones y creaciones con originales.

## 8. Justicia, aprendizaje y prueba

Empezá con una situación comprensible y un riesgo que se pueda evitar. Enseñá una novedad
por vez mediante la forma que tenga sentido en este juego. Evitá trampas invisibles,
reapariciones mortales y estrategias que ganen sin tomar decisiones.

Los robots quieto, caminante y tirador ayudan a detectar fallos, pero no juzgan creatividad
ni entienden todos los géneros. Interpretá el informe según el diseño: un puzle puede no
tener disparos y una carrera puede no tener muertes. Explicá los límites de la prueba y
probá a propósito las decisiones difíciles; no conviertas todo en un shooter para aprobar.

Mirá las capturas y verificá controles, progreso, final y reinicio. Si el jugador no puede
entender la regla nueva, simplificá o enseñala mejor antes de sumar más elementos.

## Lista de control

- [ ] Quitar cualquiera de los originales cambia una decisión central, no solo su aspecto.
- [ ] Puedo describir la regla única y diez segundos concretos de partida.
- [ ] La propuesta cambia el bucle o la relación entre juegos respecto de las anteriores.
- [ ] En modo base se reconocen sus mapas, gráficos, reglas e interfaz; los aportes encajan en ese mundo.
- [ ] Menús, HUD y progresión conservan lo pedido, sin una plantilla universal añadida.
- [ ] Solo la base declarada puede aportar una imagen nativa completa; los otros juegos aportan elementos y comportamientos.
- [ ] Controles, objetivo y final se entienden; se puede reiniciar.
- [ ] Los estados e informes permiten probar el juego, sin dictar su presentación.
- [ ] Revisé jugabilidad y capturas; pasar el validador no demuestra calidad creativa.

## Anti-patrones

- Superponer juegos enteros o alternarlos sin una regla que los vincule.
- Cambiar colores o protagonistas sin incorporar comportamientos del otro juego.
- Imponer tres niveles, un HUD y los mismos carteles en cada fusión.
- Borrar el aspecto o los mapas del original base para hacer más fácil la implementación.
- Llamar "única" a una variante que solo cambia textos, colores o posiciones del HUD.
- Usar el ejemplo técnico como guion inicial para cambiarle nombres.
- Inventar datos de memoria, esconder peligros o declarar calidad creativa solo por aprobar robots.
