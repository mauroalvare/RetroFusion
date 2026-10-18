# Ideas: qué más se puede hacer con un emulador real + IA

La base ya está: emulación exacta (cada juego corre su código original), fusiones por
memoria, análisis del programa en rutinas, anotaciones por juego e IA conectable (Claude,
Ollama, cualquier chat por copiar y pegar, agentes por carpeta). Sobre eso se puede
construir lo de abajo. Cada idea dice qué existe ya y qué falta.

## Ya funciona

- **Juegos fusionados con IA:** la IA propone ideas, escribe un guion que mezcla los juegos
  emulados en una sola pantalla, lo prueba con un jugador robot y lo corrige; el jugador
  pide cambios y cada pedido es una versión nueva. Sigue un método escrito
  (GUIA-FUSION.md) y una prueba de jugabilidad con tres robots. Incluye "Manic Invaders",
  con las 20 cavernas originales de Manic Miner.
- Usar la IA con la suscripción (Claude Code, Codex CLI, Antigravity CLI), sin pagar API.
- Fusiones de 2 o 3 juegos con reglas por memoria, tres vistas y controles compartidos.
- La IA diseña fusiones nuevas y el emulador las valida y le devuelve los errores.
- Visor de código: rutinas, nombres, comentarios, explicación y pseudocódigo con IA,
  cambios al código (NOP, bytes), variables congeladas, buscador de variables que crea
  perfiles y exportación a `.asm`.

## Próximas (se apoyan en lo que ya hay)

1. **La IA aprende un juego sola.** Un agente con herramientas (apretar teclas, correr
   cuadros, ver la pantalla, buscar en la memoria, leer variables) juega, encuentra las
   vidas, el puntaje y la posición, y escribe el perfil. Cualquier juego de la carpeta
   pasa a ser fusionable sin que el usuario busque nada. Ya están el ejecutor sin ventana,
   el buscador de memoria y las capturas; falta el bucle de herramientas (Claude con *tool
   use* y visión, o un agente con la carpeta).
2. **Mods pedidos en castellano.** "Que los invasores disparen el doble", "vidas
   infinitas", "Willy salta más alto". La IA lee las rutinas ya explicadas, propone un
   cambio de bytes, el emulador lo aplica en una copia, corre una prueba automática
   (por ejemplo, cuenta disparos por segundo) y muestra el antes y el después.
3. **Director de IA (modo caos).** Mientras se juega una fusión, una IA mira las
   variables y los eventos cada pocos segundos y cambia las reglas en vivo: sube la
   dificultad si vas bien, te salva si estás por perder, inventa eventos. Con Ollama no
   cuesta nada.
4. **Triatlón de 3 juegos.** Invaders, Manic Miner y Pac-Man: el juego del fondo cambia
   cada 30 segundos (`swap`), el puntaje se acumula entre los tres y morir en uno castiga
   a los otros. Las recetas ya soportan 3 ranuras; falta el perfil de Pac-Man (se crea
   con el buscador de variables).
5. **Rebobinar.** Un estado guardado por segundo en un anillo: mantener una tecla vuelve
   el tiempo atrás. `SaveState`/`LoadState` ya existen en todas las máquinas.
6. **Logros automáticos.** La IA lee el perfil y propone logros ("terminar la caverna sin
   que el aire baje de la mitad"); el emulador los vigila con las mismas condiciones de
   las reglas.
7. **Compartir fusiones.** La receta es un JSON chico: se puede compartir como texto o
   código QR, o en una galería de la comunidad. Quien la recibe necesita sus propios
   juegos (las recetas no llevan ROMs).

## Grandes

8. **Hacer un juego nuevo con la base de ROMs.** Con cientos de juegos analizados, las
   rutinas explicadas (dibujar sprites, leer el teclado, hacer sonar el beeper, imprimir
   texto) forman una biblioteca. Le pedís a la IA "un juego de plataformas donde un
   invasor rescata a Willy": escribe ensamblador Z80 reutilizando esas rutinas, un
   ensamblador incluido en el emulador lo arma, corre en el Spectrum emulado, y la IA ve
   la pantalla y los errores y corrige hasta que se puede jugar. Falta el ensamblador Z80
   y el bucle de iteración.
9. **Trasplante de gráficos.** Un visor que muestra la memoria como mapas de bits
   encuentra los sprites; la IA los etiqueta ("Willy caminando", "invasor tipo 2"), y se
   pueden cambiar entre juegos: el cañón de Invaders con la cara de Willy.
10. **Traducir juegos.** Encontrar los textos (ASCII o fuente propia del juego) y
    traducirlos con IA respetando el largo: Manic Miner en castellano.
11. **Del ensamblador a C legible.** Unir el pseudocódigo de todas las rutinas en un
    programa C que compile contra una biblioteca de hardware emulado. Sirve para estudiar
    el juego o portarlo.
12. **Fusión en red.** Cada jugador corre un juego en su PC y las reglas viajan por la
    red: lo que hacés en Invaders le pasa a tu amigo en Manic Miner.
13. **Mapa de calor de memoria.** Ver en vivo qué bytes lee y escribe el juego y poner
    *watchpoints* ("parar cuando alguien escriba en las vidas"). Es la herramienta que
    más acelera encontrar variables y entender rutinas, para personas y para la IA.
