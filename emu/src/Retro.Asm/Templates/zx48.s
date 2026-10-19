; Plantilla de juego para ZX Spectrum 48K (RetroEmu).
; Escribe el título con la ROM, mueve un bloque de color con Q A O P (o el joystick
; Kempston) y ESPACIO (o disparo) hace un pitido. HALT sincroniza con el cuadro (50 por segundo).
; Ensamblar:  retro-cli asm juego.s --cpu z80 --salida juego.tap   (la cinta arranca sola)

attrs   equ $5800
paper   equ $38             ; papel blanco, tinta negra
hero    equ $50             ; papel rojo brillante

        org $8000
start:  ei
        call $0D6B          ; CLS de la ROM: borra lo que dejó la carga
        ld a,2
        call $1601          ; abrir el canal de la pantalla de arriba
        ld hl,title
@title: ld a,(hl)
        or a
        jr z,@ready
        rst $10             ; imprimir con la ROM
        inc hl
        jr @title
@ready: ld a,15
        ld (px),a
        ld a,12
        ld (py),a

loop:   halt                ; esperar el próximo cuadro
        ld a,(frame)
        inc a
        ld (frame),a
        and 3
        jr nz,@sound        ; moverse cada 4 cuadros
        call cell
        ld (hl),paper       ; borrar el bloque
        call keys
        call cell
        ld (hl),hero        ; dibujarlo en su lugar nuevo
@sound: ld bc,$7FFE         ; fila de ESPACIO
        in a,(c)
        bit 0,a
        jr z,@beep
        xor a
        in a,($1F)          ; Kempston: disparo en el bit 4
        bit 4,a
        jr z,loop
@beep:  ld b,40             ; un pitido corto: el bit 4 del puerto $FE mueve el parlante
@tone:  ld a,$10
        out ($FE),a
        call delay
        xor a
        out ($FE),a
        call delay
        djnz @tone
        jr loop

; Q y A suben y bajan, O y P van a la izquierda y a la derecha (o el Kempston)
keys:   xor a
        in a,($1F)
        ld e,a              ; Kempston: derecha 0, izquierda 1, abajo 2, arriba 3
        ld bc,$FBFE
        in a,(c)
        bit 0,a             ; Q
        jr z,@up
        bit 3,e
        jr z,@noup
@up:    ld a,(py)
        cp 3
        jr c,@noup
        dec a
        ld (py),a
@noup:  ld bc,$FDFE
        in a,(c)
        bit 0,a             ; A
        jr z,@down
        bit 2,e
        jr z,@nodown
@down:  ld a,(py)
        cp 23
        jr nc,@nodown
        inc a
        ld (py),a
@nodown:
        ld bc,$DFFE
        in a,(c)
        ld d,a
        bit 1,d             ; O
        jr z,@left
        bit 1,e
        jr z,@noleft
@left:  ld a,(px)
        or a
        jr z,@noleft
        dec a
        ld (px),a
@noleft:
        bit 0,d             ; P
        jr z,@right
        bit 0,e
        ret z
@right: ld a,(px)
        cp 31
        ret nc
        inc a
        ld (px),a
        ret

; HL = dirección del atributo de la celda (px, py)
cell:   ld a,(py)
        ld l,a
        ld h,0
        add hl,hl
        add hl,hl
        add hl,hl
        add hl,hl
        add hl,hl           ; py * 32
        ld a,(px)
        ld e,a
        ld d,0
        add hl,de
        ld de,attrs
        add hl,de
        ret

delay:  ld c,30
@d:     dec c
        jr nz,@d
        ret

px:     db 0
py:     db 0
frame:  db 0
title:  db "RETROEMU", 0
