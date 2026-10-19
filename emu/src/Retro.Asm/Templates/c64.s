; Plantilla de juego para Commodore 64 (RetroEmu).
; Arranca sola (10 SYS 2061), escribe el título, mueve un sprite con el joystick del
; puerto 2 y el disparo suena por el SID y cambia el color del borde.
; Ensamblar:  retro-cli asm juego.s     Probar:  retro-cli run juego.prg --frames 300

screen  = $0400
sprptr  = $07F8             ; puntero del sprite 0
sprdata = $0340             ; bloque 13 (13 * 64)

        .org $0801
        .word basic_end, 10 ; BASIC: 10 SYS 2061
        .byte $9E, "2061", 0
basic_end:
        .word 0

start:  sei                 ; sin interrupciones del KERNAL: el juego manda
        ; pantalla en blanco
        ldx #0
        lda #$20
@clear: sta screen,x
        sta screen+$100,x
        sta screen+$200,x
        sta screen+$2E8,x
        inx
        bne @clear
        ; título (códigos de pantalla)
        ldx #0
@title: lda title,x
        beq @sprite
        sta screen+40*1+16,x
        inx
        bne @title
        ; el sprite 0: un bloque lleno de 24x21
@sprite:
        lda #sprdata/64
        sta sprptr
        ldx #62
        lda #$FF
@fill:  sta sprdata,x
        dex
        bpl @fill
        lda #1
        sta $D015           ; sprite 0 encendido
        lda #7
        sta $D027           ; amarillo
        lda #160
        sta px
        lda #140
        sta py
        ; SID: volumen al máximo, voz 1 con ataque corto
        lda #15
        sta $D418
        lda #$09
        sta $D405
        lda #$00
        sta $D406

loop:   ; una vuelta por cuadro: esperar la línea 250 del raster
@wait:  lda $D012
        cmp #250
        bne @wait
@pass:  lda $D012
        cmp #250
        beq @pass
        lda $DC00           ; joystick 2: un bit en 0 por dirección apretada
        lsr
        bcs @noup
        dec py
@noup:  lsr
        bcs @nodown
        inc py
@nodown:
        lsr
        bcs @noleft
        dec px
@noleft:
        lsr
        bcs @noright
        inc px
@noright:
        lsr
        bcs @nofire
        inc $D020           ; disparo: el borde cambia de color
        lda #$20
        sta $D401           ; frecuencia
        lda #$11
        sta $D404           ; onda triangular con la puerta abierta
        jmp @show
@nofire:
        lda #$10
        sta $D404           ; puerta cerrada: el sonido se apaga
@show:  lda px
        sta $D000
        lda py
        sta $D001
        jmp loop

px:     .byte 0
py:     .byte 0
title:  .byte 18, 5, 20, 18, 15, 5, 13, 21, 0   ; "RETROEMU"
