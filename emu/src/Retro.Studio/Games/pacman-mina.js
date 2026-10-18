// Pac-Man en la Mina (aire en el laberinto)
// Juego base: Pac-Man de Commodore 64 (MOS 6510), que corre de verdad: su laberinto, sus
// fantasmas, sus puntos y sus niveles. Manic Miner (ZX Spectrum, Z80) queda en pausa y
// aporta gráficos y reglas: el aire que se acaba, las llaves que lo devuelven y un
// guardián que patrulla el pasillo de abajo.
//
// Dónde está cada cosa en el Pac-Man de C64 (verificado dibujando cajas sobre capturas):
// los sprites 0-3 del VIC son los fantasmas y el 4 es Pac-Man (amarillo). Todos van
// ensanchados al doble (48x21) y el dibujo ocupa más o menos x+16..x+30, y+2..y+14. Los
// puntos del laberinto son del amarillo 0xB8C76F. Las vidas del original (peek $001A) se
// mantienen altas: este guion lleva sus propias vidas y nota cada vez que un fantasma
// atrapa a Pac-Man porque el original resta una.

var pac = games.a, mm = games.b;

var W = 320, H = 200;
var DOT = 0xB8C76F;
var HUD_H = 18, AIR_Y = 191;           // tapan el puntaje y las vidas propios del original
var LIVES_KEEP = 5;                    // vidas que se le dejan al original
var CORRIDOR_Y = 172, CORRIDOR_X0 = 22, CORRIDOR_X1 = 282;   // pasillo de abajo del laberinto

var mode = 'title';                    // title, card, play, gameover
var level = 1, lives = 3, score = 0, best = 0, air = 100;
var dots = [], dotsLeft = 0, scanWait = 0, waitingMaze = false;
var key = null, keyWait = 0, keysTaken = 0;
var guard = null, invulnerable = 0, freeze = 0, timer = 0, clock = 0;
var lastLives = LIVES_KEEP;
var spr = {}, hints = {};

// ------------------------------------------------------------------ dificultad por nivel
function cfg(n) {
  return {
    airPerTick: (100 / Math.max(9, 22 - (n - 1) * 3)) / 60,   // segundos de aire sin comer
    airPerDot: n < 3 ? 3 : 2.5,
    guardian: n >= 2,
    guardSpeed: Math.min(1.6, 0.6 + (n - 2) * 0.25)
  };
}

var NAMES = ['La galería', 'El guardián', 'Aire viciado', 'Las profundidades'];
var NEWS = [                           // como mucho 30 letras: entran en la tarjeta
  'Comé puntos para respirar',
  'Un guardián patrulla abajo',
  'Menos aire, guardián rápido',
  'Cada nivel, menos aire'
];

// ------------------------------------------------------------------ arranque
function init() {
  screen.size(W, H);
  var cav = mm.cavernData(0);
  spr.key = cav.item;
  spr.guard = cav.guardianFrames;
  spr.guardColor = 0xFF40FF;            // magenta: ni el amarillo de Pac-Man ni el de los fantasmas
  mm.pause();
  mm.volume = 0;                       // de Manic Miner solo usamos gráficos y reglas
  pac.volume = 0.8;
  toTitle();
}

function toTitle() {
  mode = 'title';
  pac.pause();
  game.state('title');
}

function newGame() {
  pac.restart();
  pac.resume();
  level = 1; lives = 3; score = 0; air = 100;
  keysTaken = 0; hints = {};
  lastLives = LIVES_KEEP;
  pac.set('lives', LIVES_KEEP);
  game.state('playing');
  startLevel();
}

function startLevel() {
  dots = []; dotsLeft = 0; key = null; keyWait = 240;
  waitingMaze = false; scanWait = 20;  // el original tarda unos cuadros en dibujar el laberinto
  air = 100; invulnerable = 60;
  var c = cfg(level);
  guard = c.guardian ? { x: CORRIDOR_X1 - 20, dir: -1, frame: 0 } : null;
  mode = 'card'; timer = 120;
  pac.pause();
  game.level(level);
}

// ------------------------------------------------------------------ posiciones
function sprite(i) {
  var msb = pac.peek(0xD010);
  return { x: pac.peek(0xD000 + 2 * i) + ((msb >> i) & 1) * 256 - 24, y: pac.peek(0xD001 + 2 * i) - 50 };
}
function pacBox() { var s = sprite(4); return { x: s.x + 18, y: s.y + 3, w: 11, h: 10 }; }
function ghostBox(i) { var s = sprite(i); return { x: s.x + 16, y: s.y + 2, w: 15, h: 13 }; }
function guardBox() { return guard ? { x: guard.x + 3, y: CORRIDOR_Y + 2, w: 10, h: 12 } : null; }
function isDot(x, y) { return pac.pixel(pac.area.x + x, pac.area.y + y) === DOT; }

// Un punto por grupo de píxeles amarillos (su esquina de arriba a la izquierda), salvo
// donde está Pac-Man, que es del mismo amarillo.
function scanDots() {
  var p = pacBox(), list = [];
  for (var y = HUD_H; y < AIR_Y; y++)
    for (var x = 1; x < W; x++)
      if (isDot(x, y) && !isDot(x - 1, y) && !isDot(x, y - 1) &&
          !(x >= p.x - 4 && x < p.x + p.w + 4 && y >= p.y - 4 && y < p.y + p.h + 4))
        list.push({ x: x, y: y, eaten: false });
  dots = list;
  dotsLeft = list.length;
}

// ------------------------------------------------------------------ juego
function tick(input) {
  clock++;
  if (mode === 'title') {
    if (input.pressed.start || input.pressed.jump || input.pressed.fire) newGame();
    return;
  }
  if (mode === 'gameover') {
    if (timer > 0) timer--;
    else if (input.pressed.start || input.pressed.jump || input.pressed.fire) newGame();
    return;
  }
  if (mode === 'card') {
    if (--timer <= 0) { mode = 'play'; pac.resume(); }
    return;
  }

  // controles: el joystick del puerto 2 del C64
  pac.control('left', input.left);
  pac.control('right', input.right);
  pac.control('up', input.up);
  pac.control('down', input.down);

  if (freeze > 0) {                    // Pac-Man se queda quieto un momento tras perder una vida
    if (--freeze === 0) pac.resume();
    return;
  }
  if (invulnerable > 0) invulnerable--;

  if (scanWait > 0 && --scanWait === 0) {
    scanDots();
    if (!hints.air) { hints.air = true; ui.message('¡El aire se acaba! Cada punto que comés te da aire'); }
  }

  var c = cfg(level);
  var p = pacBox();

  // fantasmas: los maneja el original; acá solo se nota cuando le resta una vida
  var l = pac.get('lives');
  if (l < lastLives) loseLife('fantasma', false);
  else if (l > lastLives) { lives++; ui.message('¡Vida extra!'); sound.beep(1200, 120, 0.4); }
  if (pac.get('lives') !== LIVES_KEEP) pac.set('lives', LIVES_KEEP);
  lastLives = LIVES_KEEP;
  if (mode !== 'play') return;

  // puntos que Pac-Man comió (el punto ya no está y Pac-Man pasa por ahí)
  if (dots.length > 0 && !waitingMaze) {
    var ghosts = [ghostBox(0), ghostBox(1), ghostBox(2), ghostBox(3)];
    for (var i = 0; i < dots.length; i++) {
      var d = dots[i];
      if (d.eaten || d.x < p.x - 8 || d.x > p.x + p.w + 8 || d.y < p.y - 8 || d.y > p.y + p.h + 8) continue;
      if (isDot(d.x, d.y)) continue;
      var hidden = false;
      for (var k = 0; k < 4; k++) if (inside(d, ghosts[k])) hidden = true;
      if (hidden) continue;
      d.eaten = true; dotsLeft--;
      air = Math.min(100, air + c.airPerDot);
      addScore(10);
    }
    if (dotsLeft <= 0) {
      waitingMaze = true; scanWait = 0; key = null;
      ui.message('¡Laberinto limpio!');
      addScore(500);
    }
  }
  // el original vuelve a dibujar el laberinto lleno: empieza el nivel siguiente
  if (waitingMaze && clock % 30 === 0 && dots.length > 0) {
    var back = 0;
    for (var j = 0; j < dots.length; j += 4) if (isDot(dots[j].x, dots[j].y)) back++;
    if (back * 4 > dots.length / 2) { level++; startLevel(); }
    return;                            // entre laberintos no corre el aire ni el guardián
  }

  // aire
  air -= c.airPerTick;
  if (air < 25 && !hints.lowAir) { hints.lowAir = true; ui.message('¡Poco aire! Buscá puntos o una llave'); }
  if (air <= 0) { loseLife('sin aire', true); return; }

  // llave de Manic Miner: aparece lejos de Pac-Man, sobre un punto todavía sin comer
  if (!key && !waitingMaze && dots.length > 0 && --keyWait <= 0) placeKey(p);
  if (key) {
    key.life--;
    if (overlap(p, { x: key.x, y: key.y, w: 8, h: 8 })) {
      key = null; keyWait = 360; keysTaken++;
      air = 100;
      addScore(100);
      sound.beep(1800, 60, 0.4); sound.beep(2400, 80, 0.4);
      if (!hints.key) { hints.key = true; ui.message('¡Una llave de la mina: aire lleno y 100 puntos!'); }
    } else if (key.life <= 0) { key = null; keyWait = 240; }
  }

  // guardián de Manic Miner en el pasillo de abajo
  if (guard) {
    guard.x += guard.dir * c.guardSpeed;
    if (guard.x < CORRIDOR_X0) { guard.x = CORRIDOR_X0; guard.dir = 1; }
    if (guard.x > CORRIDOR_X1) { guard.x = CORRIDOR_X1; guard.dir = -1; }
    if (clock % 6 === 0) guard.frame = (guard.frame + 1) % 4;
    if (!hints.guard) { hints.guard = true; ui.message('¡Cuidado: el guardián patrulla el pasillo de abajo!'); }
    if (invulnerable === 0 && overlap(p, guardBox())) { loseLife('guardián', true); return; }
  }
}

function inside(d, b) { return d.x >= b.x && d.x < b.x + b.w && d.y >= b.y && d.y < b.y + b.h; }

function placeKey(p) {
  var far = [];
  for (var i = 0; i < dots.length; i++) {
    var d = dots[i];
    if (!d.eaten && Math.abs(d.x - p.x) + Math.abs(d.y - p.y) > 120 && (!guard || d.y < CORRIDOR_Y - 4)) far.push(d);
  }
  if (far.length === 0) { keyWait = 120; return; }
  var t = far[rnd(far.length)];
  key = { x: t.x - 3, y: t.y - 3, life: 60 * 12 };
}

function addScore(n) {
  score += n;
  if (score > best) best = score;
  game.score(n);
}

// own: la vida la quita este guion (aire, guardián); si no, la quitó el original (fantasma)
function loseLife(cause, own) {
  if (own && invulnerable > 0) return;
  lives--;
  game.death(cause);
  sound.noise(300, 0.5);
  air = 100;
  invulnerable = 90;
  if (lives <= 0) {
    mode = 'gameover'; timer = 60;
    pac.pause();
    game.state('gameover');
    return;
  }
  ui.message(cause === 'fantasma' ? '¡Te atrapó un fantasma!' : cause === 'sin aire' ? '¡Te quedaste sin aire!' : '¡Te tocó el guardián!');
  if (own) { freeze = 45; pac.pause(); }
}

// ------------------------------------------------------------------ dibujo
function draw() {
  screen.clear(0x000000);
  screen.game(pac, { x: 0, y: 0 });

  if (mode === 'title') { drawTitle(); return; }

  // guardián (color propio de Manic Miner) y llave que titila
  if (guard) {
    var f = spr.guard[(guard.dir > 0 ? 0 : 4) + guard.frame];
    screen.sprite(f, Math.round(guard.x), CORRIDOR_Y, spr.guardColor);
  }
  if (key && (key.life > 120 || clock % 10 < 6))
    screen.sprite(spr.key, key.x, key.y, clock % 8 < 4 ? 0xFFFF00 : 0x00FFFF);
  if (invulnerable > 0 && clock % 8 < 4 && mode === 'play') {
    var p = pacBox();
    screen.box(p.x - 3, p.y - 3, p.w + 6, p.h + 6, 0xFFFFFF);
  }

  drawHud();
  if (mode === 'card') drawCard();
  if (mode === 'gameover') drawGameOver();
}

function drawHud() {
  screen.rect(0, 0, W, HUD_H, 0x000000);
  screen.text('PUNTOS ' + score, 4, 5, 0xFFFFFF);
  screen.text('NIVEL ' + level, 150, 5, 0x00FFFF);
  screen.text('VIDAS ' + lives, 248, 5, 0xFFFF00);
  // la barra de aire, como en Manic Miner
  screen.rect(0, AIR_Y, W, H - AIR_Y, 0x000000);
  screen.rect(4, AIR_Y + 1, 36, 8, 0xD70000);
  screen.text('AIRE', 6, AIR_Y + 1, 0xFFFFFF);
  var w = Math.max(0, Math.round(air / 100 * 200));
  screen.rect(40, AIR_Y + 1, 200, 8, 0x003000);
  screen.rect(40, AIR_Y + 1, w, 8, air < 25 && clock % 16 < 8 ? 0xD70000 : 0x00D700);
  screen.text('LLAVES ' + keysTaken, 248, AIR_Y + 1, 0xFFFF00);
}

function drawTitle() {
  screen.rect(0, 0, W, H, 0x000000, 0.8);
  screen.text('PAC-MAN EN LA MINA', W / 2, 14, 0xFFFF00, { scale: 2, center: true });
  screen.text('Aire en el laberinto', W / 2, 34, 0x00FFFF, { center: true });
  screen.text('Pac-Man cayó en la mina de Miner Willy.', W / 2, 56, 0xFFFFFF, { center: true });
  screen.text('Abajo casi no hay aire: cada punto', W / 2, 66, 0xFFFFFF, { center: true });
  screen.text('que come le da un respiro.', W / 2, 76, 0xFFFFFF, { center: true });
  screen.text('Objetivo: limpiar cada laberinto.', W / 2, 96, 0x00FF00, { center: true });
  screen.text('Perdés una vida con un fantasma,', W / 2, 108, 0xFF8080, { center: true });
  screen.text('sin aire o tocando al guardián.', W / 2, 118, 0xFF8080, { center: true });
  if (spr.key) screen.sprite(spr.key, 60, 136, 0xFFFF00);
  screen.text('Las llaves llenan el aire', 76, 136, 0xFFFF00);
  screen.text('Flechas: mover a Pac-Man', W / 2, 156, 0xFFFFFF, { center: true });
  if (clock % 60 < 40) screen.text('Enter o Espacio para empezar', W / 2, 176, 0x00FFFF, { center: true });
  if (best > 0) screen.text('RECORD ' + best, W / 2, 190, 0xFFFFFF, { center: true });
}

function drawCard() {
  var i = Math.min(level, NAMES.length) - 1;
  screen.rect(30, 70, W - 60, 56, 0x000000, 0.9);
  screen.box(30, 70, W - 60, 56, 0x00FFFF);
  screen.text('NIVEL ' + level + ': ' + NAMES[i], W / 2, 80, 0xFFFF00, { center: true });
  screen.text('Limpiá el laberinto', W / 2, 96, 0xFFFFFF, { center: true });
  screen.text(NEWS[i], W / 2, 110, 0x00FF00, { center: true });
}

function drawGameOver() {
  screen.rect(40, 66, W - 80, 64, 0x000000, 0.9);
  screen.box(40, 66, W - 80, 64, 0xFF0000);
  screen.text('FIN DEL JUEGO', W / 2, 76, 0xFF4040, { scale: 2, center: true });
  screen.text('PUNTOS ' + score + '   NIVEL ' + level, W / 2, 98, 0xFFFFFF, { center: true });
  screen.text('Enter para jugar de nuevo', W / 2, 114, 0x00FFFF, { center: true });
}
