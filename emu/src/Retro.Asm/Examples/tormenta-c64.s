; TORMENTA - juego original de ejemplo para el Commodore 64 (RetroEmu).
; Una tormenta en el mar: mové el bote con el joystick 2 (izquierda y derecha), juntá los
; salvavidas que caen y esquivá los rayos. Cada rayo te saca una vida; cada 10 salvavidas
; todo cae más rápido. Al perder las 3 vidas, el disparo empieza otra partida.
; Escrito con la guía de retro-cli crear (sin KERNAL, con sei y la vuelta sincronizada al raster).

screen  = $0400
color   = $D800
sprptr  = $07F8
sprdata = $0340             ; bloques 13, 14 y 15

; variables en la página cero
px      = $02               ; X del bote (se le suma 56 en pantalla)
ix      = $03               ; salvavidas
iy      = $04
bx      = $05               ; rayo
by      = $06
speed   = $07
lives   = $08
score   = $09               ; BCD, 2 bytes
caught  = $0B               ; salvavidas desde el último aumento de velocidad
snd     = $0C               ; cuadros que le quedan al sonido
state   = $0D               ; 0 jugando, 1 fin de partida
msb     = $0E

boaty   = 214

        .org $0801
        .word basic_end, 10 ; BASIC: 10 SYS 2061
        .byte $9E, "2061", 0
basic_end:
        .word 0

start:  sei
        lda #0
        sta $D020
        lda #6
        sta $D021           ; mar azul
        lda #$FF            ; voz 3 con ruido: números al azar en $D41B
        sta $D40E
        sta $D40F
        lda #$80
        sta $D412
        lda #15
        sta $D418
        lda #$09
        sta $D405
        lda #$00
        sta $D406
        ldx #0
@copy:  lda sprites,x
        sta sprdata,x
        inx
        cpx #192
        bne @copy
        lda #13
        sta sprptr
        lda #14
        sta sprptr+1
        lda #15
        sta sprptr+2
        lda #7
        sta $D015           ; sprites 0, 1 y 2
        lda #1
        sta $D027           ; bote blanco
        lda #8
        sta $D028           ; salvavidas naranja
        lda #7
        sta $D029           ; rayo amarillo

newgame:
        jsr clear
        lda #3
        sta lives
        lda #0
        sta score
        sta score+1
        sta caught
        sta state
        sta snd
        lda #1
        sta speed
        lda #120
        sta px
        jsr newitem
        jsr newbolt
        jsr hud

loop:   jsr frame
        jsr sound
        lda state
        bne @over
        jsr move
        jsr fall
        jsr hits
        jsr show
        jmp loop
@over:  lda $DC00           ; fin de partida: el disparo vuelve a empezar
        and #$10
        bne loop
        jmp newgame

; una vuelta por cuadro: esperar la línea 250 del raster
frame:  lda $D012
        cmp #250
        bne frame
@pass:  lda $D012
        cmp #250
        beq @pass
        rts

; joystick 2: izquierda y derecha, dos puntos por cuadro
move:   lda $DC00
        and #$04
        bne @right
        lda px
        cmp #2
        bcc @right
        sbc #2
        sta px
@right: lda $DC00
        and #$08
        bne @done
        lda px
        cmp #252
        bcs @done
        adc #2
        sta px
@done:  rts

fall:   lda iy
        clc
        adc speed
        sta iy
        cmp #240
        bcc @bolt
        jsr newitem         ; se cayó al mar: viene otro
@bolt:  lda by
        sec                 ; el rayo cae un punto más rápido
        adc speed
        sta by
        cmp #240
        bcc @done
        jsr newbolt
@done:  rts

newitem:
        jsr random
        sta ix
        lda #30
        sta iy
        rts

newbolt:
        jsr random
        sta bx
        lda #10
        sta by
        rts

; un número de 0 a 254 (dos lecturas del ruido de la voz 3)
random: lda $D41B
        and #$7F
        sta msb
        lda $D41B
        and #$7F
        clc
        adc msb
        rts

; ¿el bote toca el salvavidas o el rayo?
hits:   lda iy
        cmp #boaty-18
        bcc @bolt
        cmp #boaty+10
        bcs @bolt
        lda ix
        sec
        sbc px
        bcs @far1
        eor #$FF
        adc #1
@far1:  cmp #20
        bcs @bolt
        jsr newitem         ; ¡atrapado!
        sed
        lda score
        clc
        adc #1
        sta score
        lda score+1
        adc #0
        sta score+1
        cld
        lda #$30
        ldx #$11            ; triangular
        jsr beep
        inc caught
        lda caught
        cmp #10
        bcc @hud
        lda #0
        sta caught
        lda speed
        cmp #5
        bcs @hud
        inc speed
@hud:   jsr hud
@bolt:  lda by
        cmp #boaty-18
        bcc @done
        cmp #boaty+10
        bcs @done
        lda bx
        sec
        sbc px
        bcs @far2
        eor #$FF
        adc #1
@far2:  cmp #16
        bcs @done
        jsr newbolt         ; ¡rayo!
        lda #$08
        ldx #$81            ; ruido
        jsr beep
        lda #2
        sta $D020           ; el borde se pone rojo un momento
        dec lives
        jsr hud
        lda lives
        bne @done
        lda #1
        sta state
        jsr gameover
@done:  rts

; A = frecuencia (byte alto), X = forma de onda con la puerta abierta
beep:   sta $D401
        lda #0
        sta $D400
        txa
        and #$FE
        sta $D404           ; cerrar la puerta y abrirla otra vez: el sonido arranca de nuevo
        stx $D404
        lda #10
        sta snd
        rts

sound:  lda snd
        beq @done
        dec snd
        bne @done
        lda #$10
        sta $D404
        lda #0
        sta $D020
@done:  rts

show:   lda #0
        sta msb
        lda px
        clc
        adc #56
        sta $D000
        bcc @n0
        lda #1
        sta msb
@n0:    lda #boaty
        sta $D001
        lda ix
        clc
        adc #56
        sta $D002
        bcc @n1
        lda msb
        ora #2
        sta msb
@n1:    lda iy
        sta $D003
        lda bx
        clc
        adc #56
        sta $D004
        bcc @n2
        lda msb
        ora #4
        sta msb
@n2:    lda by
        sta $D005
        lda msb
        sta $D010
        rts

; pantalla: título, marcador y las olas abajo
clear:  ldx #0
@c:     lda #32
        sta screen,x
        sta screen+$100,x
        sta screen+$200,x
        sta screen+$2E8,x
        lda #1
        sta color,x
        sta color+$100,x
        sta color+$200,x
        sta color+$2E8,x
        inx
        bne @c
        ldx #0
@t:     lda texts,x
        sta screen,x
        inx
        cpx #40
        bne @t
        lda #7
        ldx #7
@tc:    sta color+16,x
        dex
        bpl @tc
        ldx #39
@w:     lda #$A0            ; bloques llenos: el mar
        sta screen+40*24,x
        lda #14
        sta color+40*24,x
        dex
        bpl @w
        rts

hud:    ldy #7
        lda score+1
        jsr digits
        lda score
        jsr digits
        lda lives
        ora #$30
        sta screen+39
        rts

digits: pha
        lsr
        lsr
        lsr
        lsr
        ora #$30
        sta screen,y
        iny
        pla
        and #$0F
        ora #$30
        sta screen,y
        iny
        rts

gameover:
        ldx #0
@g:     lda overtext,x
        beq @done
        sta screen+40*12+14,x
        lda #7
        sta color+40*12+14,x
        inx
        bne @g
@done:  rts

; códigos de pantalla: "PUNTOS 0000     TORMENTA         VIDAS 3"
texts:  .byte 16, 21, 14, 20, 15, 19, 32, 48, 48, 48, 48, 32, 32, 32, 32, 32
        .byte 20, 15, 18, 13, 5, 14, 20, 1
        .byte 32, 32, 32, 32, 32, 32, 32, 32, 32, 22, 9, 4, 1, 19, 32, 51
; "FIN  DISPARO"
overtext:
        .byte 6, 9, 14, 32, 32, 4, 9, 19, 16, 1, 18, 15, 0

sprites:
        ; bote
        .byte $00,$18,$00, $00,$1C,$00, $00,$1E,$00, $00,$1F,$00, $00,$1F,$80, $00,$1F,$C0
        .byte $00,$1F,$E0, $00,$18,$00, $00,$18,$00, $FF,$FF,$FF, $7F,$FF,$FE, $3F,$FF,$FC
        .byte $1F,$FF,$F8, $00,$00,$00, $00,$00,$00, $00,$00,$00, $00,$00,$00, $00,$00,$00
        .byte $00,$00,$00, $00,$00,$00, $00,$00,$00, 0
        ; salvavidas
        .byte $00,$00,$00, $00,$00,$00, $00,$00,$00, $00,$7E,$00, $01,$FF,$80, $03,$C3,$C0
        .byte $07,$00,$E0, $06,$00,$60, $06,$00,$60, $06,$00,$60, $07,$00,$E0, $03,$C3,$C0
        .byte $01,$FF,$80, $00,$7E,$00, $00,$00,$00, $00,$00,$00, $00,$00,$00, $00,$00,$00
        .byte $00,$00,$00, $00,$00,$00, $00,$00,$00, 0
        ; rayo
        .byte $00,$0F,$00, $00,$1E,$00, $00,$3C,$00, $00,$78,$00, $00,$FF,$00, $00,$1E,$00
        .byte $00,$3C,$00, $00,$78,$00, $00,$F0,$00, $01,$FE,$00, $00,$3C,$00, $00,$70,$00
        .byte $00,$E0,$00, $00,$C0,$00, $00,$00,$00, $00,$00,$00, $00,$00,$00, $00,$00,$00
        .byte $00,$00,$00, $00,$00,$00, $00,$00,$00, 0
