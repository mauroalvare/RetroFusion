# RetroEmu fusion game API

A fusion is a JavaScript script connecting genuinely emulated 8-bit games. Each original
runs its own code, enemies, physics, levels and sound. The script reads and writes memory,
presses controls, pauses or accelerates machines and composes one screen using their data,
tiles and sprites. Each original must affect decisions rather than merely decorate the scene.

Use standard ES2020, without modules, import, files or network access. Every tick and draw
call has a time limit. Infinite loops are invalid.

## Available data

Use the capabilities present in each original's profile. If a central interaction needs
unsupported data, describe it in missingData, with the concrete experiment needed to
investigate it. Leave the script empty when the central interaction cannot be implemented.

Prepare and create this fusion is a studio action, not a JavaScript function. In creation
with originals it is called Prepare and create this game. The data review shows which facts
will be tested. The emulator boots originals and runs control tests and experiments, then
reevaluates the same idea using updated profiles. An automatic provider continues writing
and validation only when the requirements have evidence. Partial results preserve the reason
and allow another preparation attempt.

Identify evidence by slot and existing capability: variable, control, sprite, documented
helper or checked boot. There is no API for guessing memory or invoking imagined routines.
An observed coordinate or effective write does not by itself prove maps, collision, damage
or rules. Read-only data remains read-only.

## Lifecycle and player input

All three functions are optional:

~~~js
function init() { }       // once, after the originals have booted into play
function tick(input) { }  // 60 times per second, after the machines advance
function draw() { }       // after each tick
~~~

In base mode the engine draws the original before draw() and preserves its resolution.
Add contributions over it. screen.clear during draw is rejected to prevent replacement
by a third scene. Free creation draws the whole scene.

input.left, right, up, down, fire, jump, start and coin are true while held.
input.pressed.fire and the other pressed fields are true only on the first tick.
input.tick counts ticks from startup.

Keyboard: arrows; Ctrl or Z=fire; Space or X=jump; Enter or 1=start; 5 or C=coin.
Gamepads are supported. Originals do not automatically receive these inputs: explicitly
route each action using g.control.

## Originals: games.a, games.b, games.c

Each recipe slot supplies an object g:

| Member | Behavior |
|---|---|
| g.name, g.profile, g.machine | Name, profile ID and machine (zx48, invaders, c64, nes). |
| g.area | {x,y,w,h}, the active image inside the full frame, excluding the Spectrum/C64 border. C64 uses the middle 320×200 of a 384×272 frame. Profile helper coordinates are relative to this area. |
| g.control(name, pressed) | Press or release a profile control (left, right, jump, fire, start...) or machine key ('P', 'SPACE', 'P1 FIRE'...). It remains held until released. |
| g.tap(name, frames) | Hold a control for N original-machine frames, then release automatically. |
| g.get(variable), g.set(variable, value) | Read/write a profile variable. set clamps to its range and releases a freeze on it. This is one write, not a lasting constraint. Read-only variables cannot be written. |
| g.freeze(variable, value) | Maintain a value during CPU writes. Enforce format, range, masks and read-only permissions; reject invalid or overlapping values. This neither pauses the machine nor proves the variable's meaning. |
| g.release(variable), g.release() | Release one or all freezes for this original, keeping current state. g.restart and full restart also release them; init may declare them again. |
| g.peek(address), g.peek16(address), g.poke(address, byte) | Direct memory access with a numeric address, such as 0x806B. |
| g.pause(), g.resume(), g.paused | Pause/resume the machine; a paused image remains still. |
| g.speed = 0.5 | Emulation speed, 0–4. |
| g.volume = 0.3 | Sound volume, 0–2. |
| g.restart() | Restore the original to the start of play. |
| g.pixel(x,y) | Full-frame pixel as 0xRRGGBB; add g.area.x/y to active-area coordinates. Useful for observing platforms or walls by color. |
| g.sprite(name) | A profile's original sprite handle for screen.sprite. Obtain and cache it in init. |
| g.spriteAt(address,width,height,'rows' or 'columns') | Read a sprite at a memory address. |
| g.capture(x,y,width,height,transparent) | Capture a small native-color resource from the active area, without adding the border. Maximum 32×32 per capture and 4096 accumulated pixels per original over the whole game. The optional last argument is a transparent color. Cache and reuse the handle with screen.sprite. Do not capture every frame or reconstruct a screen from tiles. Capturing does not identify a character or its rules. |
| g.frame | Original-machine frames since startup. |
| Profile helpers | Functions such as g.willy() or g.aliens(), documented by that profile. Prefer them to guessed addresses. |

## Drawing: screen

Redraw during every draw call. Colors are 0xRRGGBB.

| Member | Behavior |
|---|---|
| screen.size(width,height) | Free creation defaults to 256×192, maximum 640×480. Base mode accepts only its original's native resolution. |
| screen.width, screen.height | Current dimensions. |
| screen.base(g) | Restore the native image of the sole original selected in baseSlot, without border, palette or resolution changes. The engine already draws it before draw(). A second original is forbidden; unavailable in free reinterpretation. Coordinates are relative to the base's active area. |
| screen.clear(color) | Fill the free-creation scene. Rejected during draw in base mode. |
| screen.sprite(s,x,y,color,{scale,flip,alpha}) | Draw a resource. Without color, captures retain their colors and one-bit memory sprites are white. color tints the mask; flip mirrors it. |
| screen.spriteSize(s) | Return {w,h}. |
| screen.rect(x,y,w,h,color,alpha) | Filled rectangle. |
| screen.box(x,y,w,h,color) | Outline rectangle. |
| screen.line(x0,y0,x1,y1,color) | Line. |
| screen.text(text,x,y,color,{scale,center}) | ZX Spectrum 8×8 font. With center:true, x is the center. |

## Game state: game (required)

Instrumentation does not impose a universal menu, banner, HUD or level structure.
An introduction may be inside the world. A level may mean a round or phase.
With baseSlot, starting directly in playing may preserve the original's entry and interface.
Do not add fake points to pass a test. Report actual points and objectives when they exist.
A robot failing to score is an observation, not automatically a defect.

The report separates technical failures and observations. Idle, explorer and shooter patterns
do not prove every objective or the mechanical union. Investigate controls, progress,
consequences, ending and restart using experiments aimed at this design. Passing does not
certify fidelity, balance or fun, and does not require the player to complete a manual game
before further automatic investigation.

| Call | When |
|---|---|
| game.state('title') | On entering or returning to the title state. |
| game.state('playing') | When play begins. |
| game.state('gameover') | When play ends, after defeat or final victory. Start must allow retrying. |
| game.score(points) | When the player earns points. |
| game.death('cause') | On losing a life, with the actual cause. |
| game.level(n) | At the beginning of level/phase n, starting at 1. |
| game.restart() | Request full restart after the current tick: all machines, script init, inputs, sounds, objects and counters. Return from tick after requesting it. |

## Other functions

| Call | Behavior |
|---|---|
| ui.message(text) | Temporary status message next to the HUD, outside the original image. |
| ui.hud(text) | Persistent status line for lives, level or objectives. |
| sound.beep(frequency,ms,volume) | Square-wave beep. |
| sound.noise(ms,volume) | Noise effect. |
| log(...) | Write the tester's log. |
| rnd(n) | Random integer from 0 through n-1. |
| clamp(value,min,max) | Clamp a number. |
| overlap(a,b) | True when two {x,y,w,h} boxes overlap. |

## Design and composition

Use one screen and one objective. In a directed fusion, choose a world and bring the other's
behaviors into it. A new scene built from both belongs to creation with originals.
Let original machines supply their working enemies, movement, collisions and progression.
Connect them rather than reimplementing them.

Use one coordinate-conversion function for both drawing and collisions. Fair collision
boxes are slightly smaller than the sprites. Preserve native indicators and sounds that
already explain play. Add only needed information; ui.hud and ui.message are optional.
Detect endings and support restart. Tune speeds, frequencies and rewards when necessary.
Use the requested text language and the machine's characters.
Do not use async, setTimeout or import: all activity happens in tick.

With baseSlot, preserve native maps, resolution and palette. Expanding maps requires a
verified format and memory changes, not a replacement scene. Contributions interact
within that space using coherent controls.

Free creation may draw with sprites, rectangles, lines and text, reading tiles/sprites
through documented helpers. There is no universal visual design.

screen.game is retired: it draws nothing and causes validation to fail with a correction
message. Only the declared base may provide a full native image via screen.base.
Transfer a small character or tile with g.capture within its limits. Do not use secondary
full screens, thumbnails or capture mosaics. g.pixel observes state; it must not copy
an entire screen pixel by pixel.
