# ZX Spectrum 48K: writing a game

Z80 at 3.5 MHz, 50 frames per second, 69,888 cycles per frame.

## Boot

- org $8000. The emulator loads a tape with the BASIC loader
  10 CLEAR 32767: LOAD "" CODE: RANDOMIZE USR 32768 and enters $8000.
- CLEAR places the stack below $8000. Do not return to BASIC with ret from start.
- In interrupt mode 1, begin with ei; halt waits for the next 50 Hz frame.
  The ROM interrupt routine needs IY. Do not change IY, or use di and avoid halt.

## Memory and screen

Code and data may use $8000–$FF57. BASIC's user-defined graphics at $FF58 may be overwritten.

- Pixels: 256×192, one bit per pixel, $4000–$57FF in non-linear order.
  For pixel row y=0–191 and byte column x=0–31:
  $4000 + ((y & $C0) << 5) + ((y & 7) << 8) + ((y & $38) << 2) + x.
  Within an 8×8 cell, inc h moves down a pixel row (adds $100).
- Attributes: $5800+row*32+column, 24 rows by 32 columns.
  Bit 7 flash, bit 6 bright, bits 5–3 paper/background, bits 2–0 ink.
- Colors: 0 black, 1 blue, 2 red, 3 magenta, 4 green, 5 cyan, 6 yellow, 7 white.
- Border: out ($FE),a with color in bits 0–2.
- Permitted ROM routines: call $0D6B clears the screen. ld a,2 followed by
  call $1601 selects the upper screen; rst $10 prints ASCII A.
  Bytes 22,row,column position the cursor; 16,n sets ink and 17,n paper.
  Row or column zero is a zero byte, so positioned strings cannot use zero as
  their terminator; use $FF, for example.

## Controls

Read keyboard rows with ld bc,port and in a,(c). Zero means pressed.
The first key is bit 0, the second bit 1, and so on:

| Port | Keys |
|---|---|
| $FEFE | SHIFT Z X C V |
| $FDFE | A S D F G |
| $FBFE | Q W E R T |
| $F7FE | 1 2 3 4 5 |
| $EFFE | 0 9 8 7 6 |
| $DFFE | P O I U Y |
| $BFFE | ENTER L K J H |
| $7FFE | SPACE SYM M N B |

Kempston: in a,($1F); bits 0 right, 1 left, 2 down, 3 up, 4 fire.
One means pressed.

Automatic players use Kempston, Q/A/O/P and SPACE, cursor keys 5/6/7/8 and 0,
or Sinclair joystick 6/7/8/9 and 0. Support one of these schemes.
On a title screen, fire or SPACE should start play.

## Sound and randomness

The beeper is bit 4 of port $FE, sharing the output with border bits 0–2.
Toggle it with delays to make a tone. The CPU cannot do other work during that
loop, so keep effects short.

Alternatively call ROM $03B5 with HL=437500/frequency-30 and DE=frequency*seconds.

ld a,r changes with instruction execution. For better sequences, implement your
own shift register, such as a 16-bit xorshift.

## RetroEmu Z80 assembler

- Labels end with a colon. @ labels are local to the previous ordinary label;
  each routine may reuse @loop.
- Constants: name equ value, or =. Numbers: $FF, 0FFh, %1010 or decimal.
  Expressions support + - * / & | ^ << >> and parentheses. $ is the current line address.
- Directives: org, db/defb/defm (numbers or quoted text), dw/defw, ds count, value.
- Zilog syntax: ld a,(ix+5), ld (hl),a, ex af,af', in a,(c), out ($FE),a,
  jr nz,@loop, djnz @loop.
- No macros, include or if. Supply one file with one initial org.
