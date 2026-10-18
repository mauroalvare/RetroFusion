// Manic Invaders
// Juego base: Manic Miner. Las cavernas son las 20 originales (leídas de la memoria del
// juego) y Willy usa sus sprites; su física la maneja este guion. Space Invaders corre de
// verdad, oculto: aporta la flota (su marcha, su aceleración, sus sonidos) y quién dispara
// y desde dónde. Las bombas que el original lanza se convierten en bombas de la caverna,
// que los pisos frenan. Regla única para todo: lo que sube atraviesa los pisos (Willy al
// saltar, el láser); lo que cae se apoya en ellos (Willy, las bombas). Las paredes frenan todo.

var inv = games.a, mm = games.b;

var W = 256, H = 256;
var CAV_Y = 96;              // la caverna (256x128) empieza acá; arriba está el cielo de la flota
var NAME_Y = 224, AIR_Y = 232, LIVES_Y = 240;

var spr = {};
var cache = {};              // datos de cada caverna (se leen una sola vez)
var cav = null, tiles = null, crumble = {};
var level = 0, lives = 3, score = 0, best = 0, air = 100;
var willy = null, guardian = null, keys = [];
var lasers = [], bombs = [], sparks = [], booms = [];
var mode = 'title';          // title, card, play, dying, clear, gameover, won
var timer = 0, clock = 0, cooldown = 0, bombWait = 0, invulnerable = 0;
var lastAlive = 0, waveSize = 1, waitingWave = false, deathCause = '';
var tones = [];
var shownHints = {};

// ------------------------------------------------------------------ dificultad por caverna
function cfg(n) {
  return {
    rows: n < 2 ? 2 : 3,        // una novedad por caverna: en la 2 el guardián, en la 3 la tercera fila; como mucho 3 filas: la flota necesita lugar para bajar y no es una pared
    speed: Math.min(1, 0.55 + n * 0.04),
    maxBombs: n === 0 ? 2 : n < 6 ? 3 : 4,
    bombSpeed: Math.min(2.6, 1.5 + n * 0.08),
    bombGap: Math.max(22, 48 - n * 2),
    airPerTick: (100 / (95 - Math.min(n, 15) * 2)) / 60,
    guardian: n >= 1
  };
}

// ------------------------------------------------------------------ arranque
function init() {
  screen.size(W, H);
  spr.right = [mm.sprite('willy'), mm.sprite('willy2'), mm.sprite('willy3'), mm.sprite('willy4')];
  spr.left = [mm.sprite('willyLeft'), mm.sprite('willyLeft2'), mm.sprite('willyLeft3'), mm.sprite('willyLeft4')];
  spr.alien = {
    A: [inv.sprite('alienA'), inv.sprite('alienA2')],
    B: [inv.sprite('alienB'), inv.sprite('alienB2')],
    C: [inv.sprite('alienC'), inv.sprite('alienC2')]
  };
  spr.boom = inv.sprite('explosion');
  mm.pause();
  mm.volume = 0;               // de Manic Miner solo usamos gráficos y niveles
  inv.volume = 0.8;
  loadCavern(0);
  toTitle();
}

function toTitle() {
  mode = 'title';
  inv.pause();
  game.state('title');
}

function startGame() {
  score = 0;
  lives = 3;
  shownHints = {};
  game.state('playing');
  enterLevel(0);
}

function loadCavern(n) {
  if (!cache[n]) cache[n] = mm.cavernData(n);
  cav = cache[n];
  tiles = cav.map.map(function (row) { return row.slice(); });
  // de Manic Miner se descartan las plantas y estalactitas venenosas
  for (var r = 0; r < 16; r++)
    for (var c = 0; c < 32; c++)
      if (tiles[r][c] === 'nasty' || tiles[r][c] === 'extra') tiles[r][c] = 'background';
  crumble = {};
  keys = cav.keys.map(function (k) { return { x: k.x, y: k.y, w: 8, h: 8, taken: false }; });
}

function enterLevel(n) {
  level = n;
  loadCavern(n);
  var c = cfg(n);
  inv.restart();
  inv.speed = c.speed;
  inv.keepRows(c.rows);
  inv.setRack(96);
  lastAlive = waveSize = inv.aliens().length;
  waitingWave = false;
  inv.pause();
  guardian = null;
  if (c.guardian && cav.guardians.length > 0) {
    var g = cav.guardians[0];
    guardian = { x: g.x, y: g.y, minX: g.minX, maxX: g.maxX, dir: 1, speed: g.slow ? 0.3 : 0.45, color: g.color };
  }
  resetWilly();
  air = 100;
  lasers = []; bombs = []; sparks = []; booms = [];
  mode = 'card';
  timer = 0;
  game.level(n + 1);
}

function resetWilly() {
  willy = { x: cav.start.x, y: cav.start.y, vy: 0, ground: true, right: true };
  invulnerable = 90;
}

// ------------------------------------------------------------------ mosaicos
function kindAt(px, py) {
  var c = Math.floor(px / 8), r = Math.floor(py / 8);
  if (c < 0 || c > 31) return 'wall';
  if (r < 0) return 'background';
  if (r > 15) return 'floor';
  return tiles[r][c];
}
function supports(k) { return k === 'floor' || k === 'crumbling' || k === 'conveyor' || k === 'wall'; }

function box(w) { return { x: Math.floor(w.x) + 1, y: w.y, w: 8, h: 16 }; }
function sceneBox(w) { var b = box(w); return { x: b.x, y: b.y + CAV_Y + 1, w: b.w, h: b.h - 2 }; }

function wallAt(x, y, h) {
  for (var yy = y; yy < y + h; yy += 7)
    for (var xx = x; xx < x + 8; xx += 7)
      if (kindAt(xx, yy) === 'wall') return true;
  return kindAt(x, y + h - 1) === 'wall' || kindAt(x + 7, y + h - 1) === 'wall';
}

// ------------------------------------------------------------------ Willy
function moveWilly(input) {
  var w = willy;
  var dx = (input.right ? 1 : 0) - (input.left ? 1 : 0);
  if (dx !== 0) w.right = dx > 0;
  var push = 0;
  if (w.ground && under(w).indexOf('conveyor') >= 0) push = cav.conveyorDir * 0.4;
  var nx = w.x + dx * 0.8 + push;
  if (!wallAt(Math.floor(nx) + 1, w.y, 16)) w.x = Math.max(0, Math.min(240, nx));

  if ((input.jump || input.up) && w.ground) {
    w.vy = -2.7;
    w.ground = false;
    sound.beep(520, 25, 0.12);
  }
  w.vy = Math.min(3, w.vy + 0.13);
  var ny = w.y + w.vy;
  if (w.vy < 0) {
    if (wallAt(Math.floor(w.x) + 1, ny, 2)) { ny = w.y; w.vy = 0; }
  } else {
    var feetBefore = w.y + 16, feetAfter = ny + 16;
    var top = Math.floor(feetAfter / 8) * 8;
    w.ground = false;
    if (feetBefore <= top + 0.5 && feetAfter >= top && standsAt(w, top)) {
      ny = top - 16;
      w.vy = 0;
      w.ground = true;
    }
  }
  w.y = Math.min(ny, 112);
  if (w.ground) crumbleUnder(w);
}

function standsAt(w, top) {
  var b = box(w);
  return supports(kindAt(b.x, top)) || supports(kindAt(b.x + 7, top));
}

function under(w) {
  var b = box(w), top = w.y + 16;
  return [kindAt(b.x, top), kindAt(b.x + 7, top)];
}

function crumbleUnder(w) {
  var b = box(w), r = Math.floor((w.y + 16) / 8);
  [b.x, b.x + 7].forEach(function (x) {
    var c = Math.floor(x / 8);
    if (r < 16 && tiles[r][c] === 'crumbling') damage(r, c, 1 / 45);
  });
}

function damage(r, c, amount) {
  var k = r + ',' + c;
  crumble[k] = (crumble[k] || 0) + amount;
  if (crumble[k] >= 1) {
    tiles[r][c] = 'background';
    delete crumble[k];
  }
}

// ------------------------------------------------------------------ juego
function addScore(p) {
  score += p;
  game.score(p);
}

function kill(cause) {
  if (mode !== 'play' || invulnerable > 0) return;
  mode = 'dying';
  timer = 0;
  deathCause = cause;
  lives--;
  game.death(cause);
  inv.pause();
  queue([[440, 90], [330, 90], [220, 120], [110, 200]]);
}

function hint(id, text) {
  if (shownHints[id]) return;
  shownHints[id] = true;
  ui.message(text);
}

function queue(list) { list.forEach(function (t, i) { tones.push({ at: clock + i * 6, f: t[0], ms: t[1] }); }); }

function aliensInScene() {
  return inv.aliens().map(function (a) {
    return { index: a.index, type: a.type, points: a.points, x: a.x + 16, y: a.y - 80, w: 16, h: 8 };
  });
}

function tick(input) {
  clock++;
  for (var t = tones.length - 1; t >= 0; t--)
    if (tones[t].at <= clock) { sound.beep(tones[t].f, tones[t].ms, 0.3); tones.splice(t, 1); }

  if (mode === 'title') {
    if (input.pressed.start || input.pressed.fire || input.pressed.jump) startGame();
    return;
  }
  if (mode === 'gameover' || mode === 'won') {
    timer++;
    if (timer > 30 && (input.pressed.start || input.pressed.fire || input.pressed.jump)) startGame();
    return;
  }
  if (mode === 'card') {
    timer++;
    if (timer >= 100) {
      mode = 'play';
      inv.resume();
      if (level === 0) hint('fire', 'CTRL o Z dispara: el láser atraviesa los pisos y empuja la flota');
      if (guardian) hint('guardian', '¡Cuidado con el guardián! No se puede matar: saltalo');
    }
    return;
  }
  if (mode === 'dying') {
    timer++;
    if (timer >= 70) {
      if (lives <= 0) {
        mode = 'gameover';
        timer = 0;
        best = Math.max(best, score);
        game.state('gameover');
        return;
      }
      resetWilly();
      bombs = [];
      lasers = [];
      air = 100;
      if (deathCause === 'invasión') inv.setRack(96);
      mode = 'play';
      inv.resume();
    }
    return;
  }
  if (mode === 'clear') {
    timer++;
    if (air > 0) {
      var step = Math.min(air, 2);
      air -= step;
      addScore(Math.round(step * 5));
      if (timer % 3 === 0) sound.beep(800 + air * 6, 20, 0.15);
    } else if (timer > 160) {
      if (level >= 19) {
        mode = 'won';
        timer = 0;
        best = Math.max(best, score);
        game.state('gameover');
      } else enterLevel(level + 1);
    }
    return;
  }

  // --- jugando
  var c = cfg(level);
  if (invulnerable > 0) invulnerable--;
  moveWilly(input);
  var wb = sceneBox(willy);

  // el cañón invisible de Invaders sigue a Willy: el original apunta sus bombas a él
  inv.moveCannon(wb.x + 4 - 16 - 8);

  // láser
  if (cooldown > 0) cooldown--;
  if (input.fire && cooldown === 0 && lasers.length < 2) {
    lasers.push({ x: wb.x + 3, y: wb.y - 4, w: 2, h: 6 });
    cooldown = 12;
    sound.beep(1500, 30, 0.2);
  }

  var aliens = aliensInScene();
  for (var i = lasers.length - 1; i >= 0; i--) {
    var l = lasers[i];
    l.y -= 5;
    var gone = l.y < 8;
    if (!gone && l.y >= CAV_Y && kindAt(l.x, l.y - CAV_Y) === 'wall') {
      sparks.push({ x: l.x, y: l.y, t: 8 });
      gone = true;
    }
    for (var a = 0; a < aliens.length && !gone; a++) {
      var al = aliens[a];
      if (overlap(l, { x: al.x + 2, y: al.y, w: 12, h: 8 })) {
        inv.killAlien(al.index);
        booms.push({ x: al.x, y: al.y, t: 16 });
        addScore(al.points);
        pushFleetUp(3);
        sound.noise(110, 0.3);
        gone = true;
      }
    }
    for (var b = bombs.length - 1; b >= 0 && !gone; b--) {
      if (overlap(l, bombs[b])) {
        sparks.push({ x: bombs[b].x, y: bombs[b].y, t: 10 });
        bombs.splice(b, 1);
        addScore(5);
        gone = true;
      }
    }
    if (gone) lasers.splice(i, 1);
  }

  // la flota: oleadas
  var alive = inv.aliens().length;
  if (lastAlive > 0 && alive === 0) {
    addScore(300);
    air = Math.min(100, air + 10);
    ui.message('¡Oleada destruida! +300');
    waitingWave = true;
  }
  if (waitingWave && alive > 0) {
    inv.keepRows(c.rows);
    inv.setRack(96);
    waitingWave = false;
    alive = waveSize = inv.aliens().length;
  }
  lastAlive = alive;
  // el original acelera mucho a los últimos invasores; acá se frenan un poco para que sea justo
  inv.speed = c.speed * clamp(0.4 + 0.6 * alive / waveSize, 0.4, 1);

  // las bombas que lanza el original pasan a la caverna (y el cañón oculto nunca muere)
  if (bombWait > 0) bombWait--;
  inv.bombs().forEach(function (ib) {
    if (bombs.length < c.maxBombs && bombWait === 0 && ib.y < 200) {
      bombs.push({ x: ib.x + 16, y: ib.y - 80, w: 3, h: 7 });
      bombWait = c.bombGap;
    }
    inv.removeBomb(ib.index);
  });
  for (var k = bombs.length - 1; k >= 0; k--) {
    var bomb = bombs[k];
    bomb.y += c.bombSpeed;
    var bottom = bomb.y + bomb.h - CAV_Y;
    var hitFloor = false;
    if (bottom >= 0) {
      var kind = kindAt(bomb.x + 1, bottom);
      if (supports(kind)) {
        hitFloor = true;
        if (kind === 'crumbling') damage(Math.floor(bottom / 8), Math.floor((bomb.x + 1) / 8), 0.5);
        sparks.push({ x: bomb.x, y: bomb.y + 4, t: 8 });
        if (!shownHints.cover && Math.abs(bomb.x - wb.x) < 24) hint('cover', 'Las bombas no atraviesan los pisos: ¡cubrite!');
      }
    }
    if (overlap(bomb, wb)) {
      bombs.splice(k, 1);
      kill('bomba');
      continue;
    }
    if (hitFloor || bomb.y > CAV_Y + 128) bombs.splice(k, 1);
  }

  // si la flota llega al techo de la caverna, aterriza
  var lowest = 0;
  aliens.forEach(function (al) { lowest = Math.max(lowest, al.y + al.h); });
  if (lowest >= CAV_Y - 2) {
    ui.message('¡La flota aterrizó sobre la caverna!');
    kill('invasión');
    inv.setRack(96);
  }

  // guardián de Manic Miner
  if (guardian) {
    guardian.x += guardian.dir * guardian.speed;
    if (guardian.x <= guardian.minX) { guardian.x = guardian.minX; guardian.dir = 1; }
    if (guardian.x >= guardian.maxX) { guardian.x = guardian.maxX; guardian.dir = -1; }
    if (overlap(wb, { x: guardian.x + 4, y: guardian.y + CAV_Y + 2, w: 8, h: 14 })) kill('guardián');
  }

  // llaves y portal
  var b0 = box(willy);
  keys.forEach(function (key) {
    if (!key.taken && overlap({ x: b0.x, y: b0.y, w: 8, h: 16 }, key)) {
      key.taken = true;
      addScore(100);
      air = Math.min(100, air + 30);
      hint('keyair', 'Cada llave te devuelve aire');
      queue([[900, 40], [1200, 60]]);
      if (keysLeft() === 0) {
        ui.message('¡Tenés todas las llaves: el portal está abierto!');
        queue([[600, 60], [800, 60], [1000, 60], [1300, 120]]);
      }
    }
  });
  if (keysLeft() === 0 && overlap({ x: b0.x + 2, y: b0.y + 4, w: 4, h: 8 }, cav.portal)) {
    mode = 'clear';
    timer = 0;
    inv.pause();
    addScore(500);
    ui.message('¡Caverna superada!');
  }

  // aire
  air -= c.airPerTick;
  if (air < 40) hint('air', 'Se acaba el aire: ¡subí a buscar las llaves!');
  if (air <= 0) {
    air = 0;
    kill('sin aire');
  }

  for (var s = sparks.length - 1; s >= 0; s--) if (--sparks[s].t <= 0) sparks.splice(s, 1);
  for (var e = booms.length - 1; e >= 0; e--) if (--booms[e].t <= 0) booms.splice(e, 1);

  ui.hud('Caverna ' + (level + 1) + '/20: ' + cav.name + '   Llaves ' + (keys.length - keysLeft()) + '/' + keys.length +
         '   Invasores ' + alive + '   Vidas ' + lives);
}

// cada invasor derribado empuja la flota hacia arriba (sin pasar de donde empezó)
function pushFleetUp(px) {
  var top = 999;
  inv.aliens().forEach(function (a) { top = Math.min(top, a.y); });
  if (top < 999) inv.setRack(Math.max(96, top - px));
}

function keysLeft() {
  var n = 0;
  keys.forEach(function (k) { if (!k.taken) n++; });
  return n;
}

// ------------------------------------------------------------------ dibujo
var KEY_COLORS = [0xFF00FF, 0xFFFF00, 0x00FFFF, 0x00FF00];
var ALIEN_COLORS = { A: 0x40FFFF, B: 0x40FF40, C: 0xFF60FF };

function pad(n, d) { var s = String(Math.floor(n)); while (s.length < d) s = '0' + s; return s; }

function drawCavern() {
  for (var r = 0; r < 16; r++)
    for (var c = 0; c < 32; c++) {
      var kind = tiles[r][c];
      if (kind === 'background') continue;
      var tile = null;
      for (var i = 0; i < 8; i++) if (cav.tiles[i].kind === kind) { tile = cav.tiles[i]; break; }
      if (!tile) continue;
      var x = c * 8, y = CAV_Y + r * 8;
      var paper = mm.paper(tile.attr);
      if (paper !== 0) screen.rect(x, y, 8, 8, paper);
      var sink = kind === 'crumbling' ? Math.floor((crumble[r + ',' + c] || 0) * 8) : 0;
      screen.sprite(tile.sprite, x, y + sink, mm.ink(tile.attr));
    }
  var open = keysLeft() === 0;
  var p = cav.portal;
  var pink = mm.ink(p.attr), ppaper = mm.paper(p.attr);
  if (open && Math.floor(clock / 6) % 2 === 0) { var tmp = pink; pink = ppaper; ppaper = tmp; }
  screen.rect(p.x, CAV_Y + p.y, 16, 16, ppaper);
  screen.sprite(p.sprite, p.x, CAV_Y + p.y, pink);
  keys.forEach(function (k, i) {
    if (!k.taken) screen.sprite(cav.item, k.x, CAV_Y + k.y, KEY_COLORS[(Math.floor(clock / 6) + i) % 4]);
  });
}

function drawWilly() {
  if (!willy) return;
  if (mode === 'dying') {
    if (Math.floor(timer / 4) % 2 === 0) return;
  } else if (invulnerable > 0 && Math.floor(invulnerable / 4) % 2 === 0) return;
  var xi = Math.floor(willy.x), cell = xi - (xi % 8), frame = (xi % 8) >> 1;
  var color = mode === 'dying' ? 0xFF4040 : 0xFFFFFF;
  screen.sprite((willy.right ? spr.right : spr.left)[frame], cell, CAV_Y + Math.floor(willy.y), color);
}

function drawGuardian() {
  if (!guardian) return;
  var xi = Math.floor(guardian.x), cell = xi - (xi % 8), frame = (xi % 8) >> 1;
  screen.sprite(cav.guardianFrames[(guardian.dir > 0 ? 0 : 4) + frame], cell, CAV_Y + guardian.y, guardian.color);
}

function drawSky() {
  var frame = inv.alienFrame();
  aliensInScene().forEach(function (a) {
    if (a.y > -8 && a.y < CAV_Y) screen.sprite(spr.alien[a.type][frame], a.x, a.y, ALIEN_COLORS[a.type]);
  });
  booms.forEach(function (b) { screen.sprite(spr.boom, b.x, b.y, 0xFFFFFF, { alpha: b.t / 16 }); });
}

function drawHud() {
  screen.rect(0, 0, W, 8, 0x000000);
  screen.text('PUNTOS ' + pad(score, 6), 0, 0, 0xFFFFFF);
  screen.text('RECORD ' + pad(Math.max(best, score), 6), 152, 0, 0xFFFF00);
  screen.rect(0, NAME_Y, W, 8, 0xD7D700);
  screen.text(cav.name, 128, NAME_Y, 0x000000, { center: true });
  screen.rect(0, AIR_Y, 40, 8, 0xD70000);
  screen.text('AIRE', 4, AIR_Y, 0xFFFFFF);
  screen.rect(40, AIR_Y, W - 40, 8, 0x00D700);
  var low = air < 25 && Math.floor(clock / 8) % 2 === 0;
  screen.rect(44, AIR_Y + 2, Math.max(0, (W - 50) * air / 100), 4, low ? 0xFF2020 : 0xFFFFFF);
  for (var i = 0; i < Math.max(0, lives - 1); i++)
    screen.sprite(spr.right[(Math.floor(clock / 8) + i) % 4], 4 + i * 20, LIVES_Y, 0x00FFFF);
  var label = 'CAVERNA ' + (level + 1) + '/20';
  screen.text(label, W - 4 - label.length * 8, LIVES_Y + 4, 0xFFFFFF);
}

function panel(lines, y) {
  var h = lines.length * 10 + 10;
  screen.rect(16, y, W - 32, h, 0x000000, 0.88);
  screen.box(16, y, W - 32, h, 0xFFFF00);
  lines.forEach(function (l, i) { screen.text(l[0], 128, y + 6 + i * 10, l[1], { center: true }); });
}

function draw() {
  screen.clear(0x000000);
  if (mode === 'title') { drawTitle(); return; }
  drawSky();
  screen.line(0, CAV_Y - 1, W, CAV_Y - 1, 0x303030);
  drawCavern();
  drawGuardian();
  drawWilly();
  bombs.forEach(function (b) { screen.rect(b.x, b.y, 2, b.h, Math.floor(clock / 4) % 2 ? 0xFF3030 : 0xFFA020); });
  lasers.forEach(function (l) { screen.rect(l.x, l.y, 2, 6, 0xFFFF40); });
  sparks.forEach(function (s) {
    screen.rect(s.x - 1, s.y, 4, 1, 0xFFFFFF, s.t / 10);
    screen.rect(s.x, s.y - 1, 1, 4, 0xFFFFFF, s.t / 10);
  });
  drawHud();

  if (mode === 'card') {
    var lines = [['CAVERNA ' + (level + 1) + ' DE 20', 0xFFFF00], [cav.name, 0x00FFFF],
                 ['Junta las ' + keys.length + ' llaves y sali', 0xFFFFFF], ['por el portal. Las llaves dan aire,', 0xFFFFFF],
                 ['los disparos frenan la flota', 0xFFFFFF]];
    if (level === 0) lines.push(['CTRL o Z dispara  ESPACIO salta', 0xFF8080]);
    if (guardian && level === 1) lines.push(['Nuevo: el guardian. Saltalo!', 0xFF8080]);
    if (level === 2) lines.push(['La flota ahora tiene 3 filas', 0xFF8080]);
    panel(lines, 120);
  } else if (mode === 'clear') {
    panel([['CAVERNA SUPERADA!', 0x00FF00], ['El aire que sobra suma puntos', 0xFFFFFF], ['Puntos: ' + pad(score, 6), 0xFFFF00]], 130);
  } else if (mode === 'gameover') {
    panel([['FIN DEL JUEGO', 0xFF4040], ['Puntos: ' + pad(score, 6), 0xFFFF00], ['Llegaste a la caverna ' + (level + 1), 0xFFFFFF],
           [Math.floor(clock / 20) % 2 ? 'ENTER para jugar de nuevo' : '', 0x00FFFF]], 120);
  } else if (mode === 'won') {
    panel([['WILLY SALVO LAS MINAS!', 0x00FF00], ['Las 20 cavernas superadas', 0xFFFFFF], ['Puntos: ' + pad(score, 6), 0xFFFF00],
           ['ENTER para jugar de nuevo', 0x00FFFF]], 120);
  }
}

function drawTitle() {
  drawSky();
  screen.text('MANIC INVADERS', 128, 100, 0xFFFF00, { center: true, scale: 2 });
  screen.text('Willy contra la invasion', 128, 122, 0x00FFFF, { center: true });
  var story = [
    'Una flota invasora se poso',
    'sobre las minas de Surbiton.',
    'Con su laser, Willy tiene que',
    'juntar las llaves de cada',
    'caverna y escapar por el portal',
    'antes de que la flota aterrice.'
  ];
  story.forEach(function (l, i) { screen.text(l, 128, 140 + i * 9, 0xD7D7D7, { center: true }); });
  screen.text('FLECHAS caminar  ESPACIO saltar', 128, 202, 0xFFFFFF, { center: true });
  screen.text('CTRL o Z disparar hacia arriba', 128, 212, 0xFFFFFF, { center: true });
  if (Math.floor(clock / 20) % 2 === 0) screen.text('ENTER para empezar', 128, 228, 0x00FF00, { center: true });
  var wx = (clock % 288) - 16;
  screen.sprite(spr.right[(wx & 7) >> 1], wx - (wx & 7), 240, 0xFFFFFF);
}
