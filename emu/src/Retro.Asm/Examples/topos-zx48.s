; EL JARDIN DE LOS TOPOS - juego original de ejemplo para el ZX Spectrum 48K (RetroEmu).
; Los topos asoman por los 24 agujeros del jardín. Mové el cursor (blanco) con Q A O P o
; el Kempston y golpeá con ESPACIO o el disparo cuando hay un topo (amarillo). Cada golpe
; suma un punto y apura a los topos; si se escapan 5, se termina y el disparo vuelve a empezar.
; Escrito con la guía de retro-cli crear: HALT por cuadro, IY sin tocar, la ROM para el texto.

attrs   equ $5800
grass   equ $20             ; papel verde
hole    equ $00             ; negro sobre negro: la cara dibujada en el agujero no se ve
mole    equ $70             ; papel amarillo brillante, tinta negra: aparece la cara del topo
hand    equ $7F             ; el cursor: blanco brillante (la cara no se ve)
handmole equ $F0            ; el cursor sobre un topo: amarillo que parpadea
border  equ 4               ; borde verde

        org $8000
start:  ei
        ld a,border
        out ($FE),a
        ld a,r
        or 1
        ld (seed),a

newgame:
        call $0D6B          ; CLS de la ROM
        ld a,2
        call $1601          ; imprimir en la pantalla de arriba
        ld hl,title
        call prints
        ld hl,attrs+4*32    ; el pasto, filas 4 a 21
        ld bc,18*32
@grass: ld (hl),grass
        inc hl
        dec bc
        ld a,b
        or c
        jr nz,@grass
        call faces
        xor a
        ld (score),a
        ld (score+1),a
        ld (lost),a
        ld (state),a
        ld (delay),a
        ld hl,moles
        ld b,24
@clear: ld (hl),a
        inc hl
        djnz @clear
        ld a,14
        ld (cur),a          ; el agujero del medio
        ld a,90
        ld (uptime),a
        ld a,40
        ld (rate),a
        ld (spawnt),a
        ld a,$10
        ld (lastin),a       ; el disparo cuenta como apretado: hay que soltarlo y volver a apretar
        call hud

loop:   halt                ; un cuadro
        call input
        ld (keys),a
        ld a,(state)
        or a
        jr nz,@over
        call move
        call bonk
        call tick
        call spawn
        call draw
        ld a,(keys)
        ld (lastin),a
        jr loop
@over:  ld a,(keys)         ; fin: un disparo nuevo empieza otra partida
        ld b,a
        ld a,(lastin)
        cpl
        and b
        and $10
        ld a,b
        ld (lastin),a
        jr z,loop
        jp newgame

; A = controles como el Kempston (bit 0 derecha, 1 izquierda, 2 abajo, 3 arriba, 4 disparo)
input:  xor a
        in a,($1F)
        and $1F
        ld e,a
        ld bc,$DFFE         ; P O I U Y
        in a,(c)
        bit 0,a
        jr nz,@keyo
        set 0,e             ; P: derecha
@keyo:  bit 1,a
        jr nz,@keyq
        set 1,e             ; O: izquierda
@keyq:  ld bc,$FBFE
        in a,(c)
        bit 0,a
        jr nz,@keya
        set 3,e             ; Q: arriba
@keya:  ld bc,$FDFE
        in a,(c)
        bit 0,a
        jr nz,@space
        set 2,e             ; A: abajo
@space: ld bc,$7FFE
        in a,(c)
        bit 0,a
        jr nz,@done
        set 4,e             ; ESPACIO: golpe
@done:  ld a,e
        ret

; el cursor salta de agujero en agujero; mantener apretado repite cada 8 cuadros
move:   ld a,(keys)
        and $0F
        jr nz,@held
        xor a
        ld (delay),a
        ret
@held:  ld a,(delay)
        or a
        jr z,@go
        dec a
        ld (delay),a
        ret
@go:    ld a,8
        ld (delay),a
        ld a,(keys)
        ld e,a
        ld a,(cur)
        ld b,0              ; fila = cur / 6, columna = cur mod 6
@div:   cp 6
        jr c,@got
        sub 6
        inc b
        jr @div
@got:   ld d,a
        bit 0,e
        jr z,@noright
        ld a,d
        cp 5
        jr nc,@noright
        inc d
@noright:
        bit 1,e
        jr z,@noleft
        ld a,d
        or a
        jr z,@noleft
        dec d
@noleft:
        bit 2,e
        jr z,@nodown
        ld a,b
        cp 3
        jr nc,@nodown
        inc b
@nodown:
        bit 3,e
        jr z,@noup
        ld a,b
        or a
        jr z,@noup
        dec b
@noup:  ld a,b              ; cur = fila * 6 + columna
        add a,a
        ld c,a
        add a,a
        add a,c
        add a,d
        ld (cur),a
        ret

; golpe: solo cuando el disparo se acaba de apretar
bonk:   ld a,(lastin)
        cpl
        ld b,a
        ld a,(keys)
        and b
        and $10
        ret z
        ld a,(cur)
        ld e,a
        ld d,0
        ld hl,moles
        add hl,de
        ld a,(hl)
        or a
        jr z,@miss
        ld (hl),0
        ld a,(score)        ; un punto (BCD)
        add a,1
        daa
        ld (score),a
        ld a,(score+1)
        adc a,0
        daa
        ld (score+1),a
        ld a,(uptime)       ; los topos se apuran
        cp 32
        jr c,@fast
        sub 2
        ld (uptime),a
@fast:  ld a,(rate)
        cp 16
        jr c,@sound
        dec a
        ld (rate),a
@sound: call hud
        ld b,40
        ld c,18
        jp beep
@miss:  ld b,6
        ld c,120
        jp beep

; los topos que están afuera se cansan; el que se esconde sin golpe se escapó
tick:   ld hl,moles
        ld b,24
@next:  ld a,(hl)
        or a
        jr z,@skip
        dec a
        ld (hl),a
        jr nz,@skip
        ld a,(state)
        or a
        jr nz,@skip
        push hl
        push bc
        ld a,(lost)
        inc a
        ld (lost),a
        call hud
        ld b,12
        ld c,220
        call beep
        ld a,(lost)
        cp 5
        jr c,@alive
        ld a,1
        ld (state),a
        ld hl,overtext
        call prints
@alive: pop bc
        pop hl
@skip:  inc hl
        djnz @next
        ret

; cada tanto asoma un topo en un agujero vacío al azar
spawn:  ld a,(spawnt)
        dec a
        ld (spawnt),a
        ret nz
        ld a,(rate)
        ld (spawnt),a
@again: call random
        ld a,l
        and 31
        cp 24
        jr nc,@again
        ld e,a
        ld d,0
        ld hl,moles
        add hl,de
        ld a,(hl)
        or a
        ret nz
        ld a,(uptime)
        ld (hl),a
        ret

; cada agujero son 2x2 celdas de atributos
draw:   ld ix,moles
        ld hl,holes
        ld c,0
@next:  ld e,(hl)
        inc hl
        ld d,(hl)
        inc hl
        push hl
        ld a,(cur)
        cp c
        ld a,(ix+0)
        jr z,@cursor
        or a
        ld a,hole
        jr z,@paint
        ld a,mole
        jr @paint
@cursor:
        or a
        ld a,hand
        jr z,@paint
        ld a,handmole
@paint: ex de,hl
        ld (hl),a
        inc hl
        ld (hl),a
        ld de,31
        add hl,de
        ld (hl),a
        inc hl
        ld (hl),a
        pop hl
        inc ix
        inc c
        ld a,c
        cp 24
        jr nz,@next
        ret

; la cara del topo, dibujada una vez en los puntos de cada agujero; el color decide si se ve
faces:  ld hl,holes
        ld b,24
@next:  ld e,(hl)
        inc hl
        ld d,(hl)
        inc hl
        push hl
        push bc
        ex de,hl            ; HL = atributo de la celda de arriba a la izquierda
        push hl
        call cellbits
        ld de,face
        call face8
        pop hl
        ld bc,32            ; la celda de abajo
        add hl,bc
        call cellbits
        ld de,face+16
        call face8
        pop bc
        pop hl
        djnz @next
        ret

; HL: de la dirección del atributo de una celda a la de su primera fila de puntos
cellbits:
        ld a,h
        sub $58
        add a,a
        add a,a
        add a,a
        add a,$40
        ld h,a
        ret

; 8 filas de 2 bytes desde DE en las celdas HL y HL+1 (bajar una fila es inc h)
face8:  ld b,8
@line:  ld a,(de)
        ld (hl),a
        inc de
        inc hl
        ld a,(de)
        ld (hl),a
        inc de
        dec hl
        inc h
        djnz @line
        ret

; B = ondas, C = espera (más grande, más grave); el borde sigue verde
beep:   ld a,border+$10
        out ($FE),a
        ld d,c
@up:    dec d
        jr nz,@up
        ld a,border
        out ($FE),a
        ld d,c
@down:  dec d
        jr nz,@down
        djnz beep
        ret

; xorshift de 16 bits: HL = el próximo número
random: ld hl,(seed)
        ld a,h
        rra
        ld a,l
        rra
        xor h
        ld h,a
        ld a,l
        rra
        ld a,h
        rra
        xor l
        ld l,a
        xor h
        ld h,a
        ld (seed),hl
        ret

hud:    ld hl,scoretext
        call prints
        ld a,(score+1)
        and $0F
        add a,'0'
        rst $10
        ld a,(score)
        call bcd
        ld hl,losttext
        call prints
        ld a,(lost)
        add a,'0'
        rst $10
        ld hl,ofive
        jp prints

bcd:    push af
        rrca
        rrca
        rrca
        rrca
        and $0F
        add a,'0'
        rst $10
        pop af
        and $0F
        add a,'0'
        rst $10
        ret

; imprime desde HL hasta un $FF (22, fila, columna mueve el cursor; la fila 0 es un 0,
; por eso los textos no terminan en 0)
prints: ld a,(hl)
        cp $FF
        ret z
        push hl
        rst $10
        pop hl
        inc hl
        jr prints

title:  db 22, 0, 5, "EL JARDIN DE LOS TOPOS", $FF
scoretext:
        db 22, 2, 2, "TOPOS ", $FF
losttext:
        db 22, 2, 18, "ESCAPADOS ", $FF
ofive:  db "/5", $FF
overtext:
        db 22, 3, 8, "FIN  -  DISPARO", $FF

holes:  dw attrs+6*32+5, attrs+6*32+9, attrs+6*32+13, attrs+6*32+17, attrs+6*32+21, attrs+6*32+25
        dw attrs+10*32+5, attrs+10*32+9, attrs+10*32+13, attrs+10*32+17, attrs+10*32+21, attrs+10*32+25
        dw attrs+14*32+5, attrs+14*32+9, attrs+14*32+13, attrs+14*32+17, attrs+14*32+21, attrs+14*32+25
        dw attrs+18*32+5, attrs+18*32+9, attrs+18*32+13, attrs+18*32+17, attrs+18*32+21, attrs+18*32+25

; 16x16 puntos: orejas, ojos, nariz y boca
face:   db %00000000, %00000000
        db %00011000, %00011000
        db %00111100, %00111100
        db %00000000, %00000000
        db %00000000, %00000000
        db %00110000, %00001100
        db %00110000, %00001100
        db %00000000, %00000000
        db %00000000, %00000000
        db %00000011, %11000000
        db %00000011, %11000000
        db %00000001, %10000000
        db %00000000, %00000000
        db %00010000, %00001000
        db %00001111, %11110000
        db %00000000, %00000000

cur:    db 0
keys:   db 0
lastin: db 0
delay:  db 0
state:  db 0
lost:   db 0
score:  dw 0
uptime: db 0
rate:   db 0
spawnt: db 0
seed:   dw 1
moles:  ds 24
