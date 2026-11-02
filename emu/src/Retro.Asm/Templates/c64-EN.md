# Commodore 64: writing a game

PAL, 6510 CPU (6502) at about 985 kHz, 50 frames per second (about 19,700 cycles per frame).

## Boot

- Keep the template's .org $0801 and BASIC line 10 SYS 2061. The emulator loads the .prg,
  types RUN and enters start at $080D.
- Begin with sei. With KERNAL interrupts disabled, the game controls the machine.
  Do not return to BASIC.
- Do not call KERNAL or BASIC routines. The free Open ROMs do not place every routine at
  the same address. Work directly with registers and memory.
- Do not use BRK or undocumented opcodes.

## Free memory

- Code and data: $0801–$9FFF while BASIC is inactive. $C000–$CFFF is also free.
- With sei, zero page $02–$8F is available for fast variables. Do not change $00 or $01,
  the 6510 port.
- The VIC sees character ROM at $1000–$1FFF. Put custom sprites or characters elsewhere,
  for example $2000–$3FFF.

## Screen

- 40×25 text at $0400 + row*40 + column. These are screen codes, not ASCII:
  A=1 through Z=26, space=32, digits 0–9=48–57, @=0.
- Cell color: low four bits at $D800 + row*40 + column.
- Border $D020; background $D021.
- Colors: 0 black, 1 white, 2 red, 3 cyan, 4 purple, 5 green, 6 blue, 7 yellow,
  8 orange, 9 brown, 10 light red, 11 dark gray, 12 gray, 13 light green,
  14 light blue, 15 light gray.
- Custom characters use eight bytes each. $D018=$1C puts the screen at $0400 and
  characters at $3000; screen code n starts at $3000+n*8.
- $D012 holds the low eight raster-line bits; bit 7 of $D011 is the ninth.
  Waiting for line 250 and then leaving it gives one update per frame.

## Sprites: eight, 24×21

- Data: 63 bytes (three per row, 21 rows) in an aligned 64-byte block.
  Pointer n at $07F8+n holds address/64; block 13=$0340, block 128=$2000.
- Positions: $D000/$D001 for sprite 0 X/Y through $D00E/$D00F for sprite 7.
  $D010 holds each sprite's high X bit. Visible area: X=24–343, Y=50–249.
- $D015 enables sprites; $D027–$D02E select colors; $D017 doubles height;
  $D01D doubles width; $D01C enables multicolor with shared colors $D025/$D026.
- $D01E reports sprite/sprite collisions and $D01F sprite/background collisions,
  one bit per sprite. Reading clears the flags.

## Controls

- Port 2 joystick is $DC00: bits 0 up, 1 down, 2 left, 3 right, 4 fire.
  Zero means pressed. Port 1 uses the same bits at $DC01.
- Automatic players use port 2 or 1. Support one of those. If there is a title screen,
  fire should start the game.

## SID sound

- $D418 low four bits are volume; 15 is maximum.
- Voice 1: frequency low/high $D400/$D401, pulse width $D402/$D403,
  control $D404: bit 0 gate (1 on, 0 off), $10 triangle, $20 sawtooth,
  $40 pulse, $80 noise. $D405 attack/decay; $D406 sustain/release.
- Voices 2 and 3 use the same layout from $D407 and $D40E.
- Retrigger a sound by writing gate 0 and then gate 1.

For random bytes, set $D40E=$FF, $D40F=$FF and $D412=$80 (voice 3 noise);
each subsequent $D41B read supplies a changing byte.

## RetroEmu 6502 assembler

- Labels end with a colon. @ labels are local to the previous ordinary label;
  each routine may reuse @loop.
- Constants: name = value. Numbers: $FF, %1010 or decimal. <x and >x select low/high
  bytes. Expressions support + - * / & | ^ << >> and parentheses.
- Directives: .org, .byte (numbers or quoted text), .word, .fill count, value.
- Addressing examples: lda #5, lda $10, lda $1234,x, lda ($FB),y, jmp (vector).
- No macros, include or if. Supply one file with one initial .org.
