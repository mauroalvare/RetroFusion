"""Self tests: CPU semantics, instruction encoding and the full
assemble -> disassemble -> re-assemble round trip on generated ROMs.

Run with:  python retro.py selftest
"""

import os
import shutil
import tempfile

from .asm import assemble_file, AsmError
from .cpus.mos6502 import CPU, C, Z, V, N
from .disasm import Disassembly
from .platforms.nes import NesRom

DEMO_NROM = r"""
PPUCTRL   = $2000
PPUMASK   = $2001
PPUSTATUS = $2002
temp      = $00
ptr       = $02
player_y  = $10
player_vy = $11
frame     = $12
mode      = $13

    .byte "NES", $1A
    .byte $02, $01, $01, $00
    .byte 0,0,0,0,0,0,0,0
.bank 0, $8000, $8000
Reset:
    SEI
    CLD
    LDX #$FF
    TXS
    LDA #$00
    STA PPUCTRL
    STA PPUMASK
    JSR WaitVblank
    JSR WaitVblank
    LDA #$00
    STA mode
MainLoop:
    JSR UpdatePlayer
    LDA mode
    JSR JumpEngine
    .word ModeTitle
    .word ModeGame
    .word ModeOver
WaitVblank:
    BIT PPUSTATUS
    BPL WaitVblank
    RTS
JumpEngine:
    ASL A
    TAY
    PLA
    STA ptr
    PLA
    STA ptr+1
    INY
    LDA (ptr),Y
    STA temp
    INY
    LDA (ptr),Y
    STA temp+1
    JMP (temp)
ModeTitle:
    LDA #$01
    STA mode
    JMP MainLoop
ModeGame:
    LDX frame
    LDA SpeedTable,X
    STA a:player_vy
    JSR Dispatch
    JMP MainLoop
ModeOver:
    JMP MainLoop
Dispatch:
    LDA frame
    AND #$03
    TAX
    LDA ActionHi,X
    PHA
    LDA ActionLo,X
    PHA
    RTS
Act0:
    INC frame
    RTS
Act1:
    DEC frame
    RTS
Act2:
    LDA #$00
    STA frame
    RTS
Act3:
    RTS
ActionLo: .byte <(Act0-1), <(Act1-1), <(Act2-1), <(Act3-1)
ActionHi: .byte >(Act0-1), >(Act1-1), >(Act2-1), >(Act3-1)
SpeedTable: .byte $00, $01, $02, $03, $04, $05, $06, $07
; Gravity: speed grows up to 4, position clamps at 200 (the "floor").
UpdatePlayer:
    LDA player_vy
    CMP #$04
    BCS UpdSkip
    INC player_vy
UpdSkip:
    LDA player_y
    CLC
    ADC player_vy
    CMP #200
    BCC UpdStore
    LDA #200
    LDX #$00
    STX player_vy
UpdStore:
    STA player_y
    RTS
Unreached:
    LDA #$05
    STA $0300
    LDA $0301
    CLC
    ADC #$01
    STA $0301
    RTS
Message: .byte "HOLA", $00
Nmi:
    PHA
    INC frame
    PLA
    RTI
Irq:
    RTI
.org $FFFA
    .word Nmi, Reset, Irq
.endbank
    .incbin "chr.bin"
"""

DEMO_UXROM = r"""
    .byte "NES", $1A
    .byte $04, $00, $20, $00
    .byte 0,0,0,0,0,0,0,0
.bank 0, $8000, $4000
Bank0Entry:
    LDA #$11
    STA $0400
    RTS
Bank0Data: .byte 1,2,3,4
.endbank
.bank 1, $8000, $4000
Bank1Entry:
    LDA #$22
    STA $0401
    JSR Bank1Helper
    LDA Bank1Table,X
    RTS
Bank1Helper:
    INC $0402
    RTS
Bank1Table: .byte 9,8,7
.endbank
.bank 2, $8000, $4000
    .fill 16, $EA
.endbank
.bank 3, $C000, $4000
Reset:
    SEI
    LDX #$FF
    TXS
    LDA #$00
    JSR SwitchBank
    JSR $8000
    LDA #$01
    JSR SwitchBank
    JSR $8000
Loop:
    JMP Loop
SwitchBank:
    TAX
    STA BankTable,X
    RTS
BankTable: .byte 0,1,2,3
Nmi:
    RTI
.org $FFFA
    .word Nmi, Reset, Nmi
.endbank
"""

ENCODING_CASES = [
    ("LDA #$01", "A9 01"), ("STA $0200,X", "9D 00 02"), ("JMP ($1234)", "6C 34 12"),
    ("LDA ($10),Y", "B1 10"), ("LDA ($10,X)", "A1 10"), ("ASL A", "0A"), ("ASL", "0A"),
    ("LDX $10,Y", "B6 10"), ("STX $10,Y", "96 10"), ("LDA $10,Y", "B9 10 00"),
    ("LDA a:$10", "AD 10 00"), ("INC $10", "E6 10"), ("BIT $2002", "2C 02 20"),
    ("JSR $C000", "20 00 C0"), ("RTS", "60"), ("CPX #$FF", "E0 FF"), ("ROR $0300,X", "7E 00 03"),
]


def make_chr():
    out = bytearray()
    for t in range(512):
        for row in range(8):
            out.append(((t * 7 + row * 13) & 0xFF) ^ (0xFF if row in (0, 7) else 0x81))
        for row in range(8):
            out.append(((t * 3 + row * 5) & 0xFF) & (0x3C if row % 2 else 0xC3))
    return bytes(out)


def _assemble_text(tmp, name, text, extra=None):
    path = os.path.join(tmp, name)
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)
    for fname, blob in (extra or {}).items():
        with open(os.path.join(tmp, fname), "wb") as f:
            f.write(blob)
    data, _ = assemble_file(path)
    return data


def test_cpu():
    mem = bytearray(0x10000)
    cpu = CPU(lambda a: mem[a], lambda a, v: mem.__setitem__(a, v))

    def run(code, a=0, carry=0):
        mem[0x8000:0x8000 + len(code)] = bytes(code)
        cpu.pc = 0x8000
        cpu.a = a
        cpu.p = (cpu.p & ~C) | carry
        cpu.steps = 0
        cpu.run_until(0x8000 + len(code), 100)

    run([0x69, 0x50], a=0x50)                     # ADC #$50
    assert cpu.a == 0xA0 and cpu.p & V and not cpu.p & C and cpu.p & N
    run([0x69, 0x90], a=0xD0)
    assert cpu.a == 0x60 and cpu.p & V and cpu.p & C
    run([0xE9, 0xF0], a=0x50, carry=1)            # SBC #$F0
    assert cpu.a == 0x60 and not cpu.p & C and not cpu.p & V
    run([0xE9, 0xB0], a=0x50, carry=1)
    assert cpu.a == 0xA0 and cpu.p & V and not cpu.p & C
    run([0xC9, 0x10], a=0x10)                     # CMP equal
    assert cpu.p & Z and cpu.p & C
    run([0xC9, 0x20], a=0x10)
    assert not cpu.p & C and cpu.p & N
    # JMP ($10FF) reads the high byte from $1000 (page wrap bug)
    mem[0x10FF], mem[0x1000], mem[0x1100] = 0x34, 0x12, 0x56
    mem[0x8000:0x8003] = bytes([0x6C, 0xFF, 0x10])
    cpu.pc, cpu.steps = 0x8000, 0
    cpu.step()
    assert cpu.pc == 0x1234, hex(cpu.pc)
    # JSR / RTS round trip and stack contents
    mem[0x9000:0x9003] = bytes([0x20, 0x00, 0xA0])
    mem[0xA000] = 0x60
    cpu.pc, cpu.sp, cpu.steps = 0x9000, 0xFD, 0
    cpu.step()
    assert cpu.pc == 0xA000 and mem[0x01FD] == 0x90 and mem[0x01FC] == 0x02
    cpu.step()
    assert cpu.pc == 0x9003 and cpu.sp == 0xFD
    # BIT sets N and V from memory
    mem[0x0040] = 0xC0
    run([0x24, 0x40], a=0x00)
    assert cpu.p & Z and cpu.p & N and cpu.p & V
    # ROR through carry
    run([0x6A], a=0x01, carry=1)
    assert cpu.a == 0x80 and cpu.p & C


def test_encoding(tmp):
    for src, expect in ENCODING_CASES:
        text = ".bank 0, $8000, $10\n    %s\n.endbank\n" % src
        data = _assemble_text(tmp, "enc.asm", text)
        got = " ".join("%02X" % x for x in data[:len(expect.split())])
        assert got == expect, "%s -> %s (esperado %s)" % (src, got, expect)
    try:
        _assemble_text(tmp, "bad.asm", ".bank 0, $8000, $4\n    LDA $1234\n    LDA $1234\n.endbank\n")
    except AsmError as e:
        assert "se pasó" in str(e)
    else:
        raise AssertionError("no detectó desborde de banco")


def roundtrip(rom_bytes, tmp, name, hints=None):
    rom = NesRom(rom_bytes, name)
    d = Disassembly(rom, hints=hints)
    d.run()
    out = os.path.join(tmp, name)
    d.write(out)
    rebuilt, _ = assemble_file(os.path.join(out, "main.asm"))
    if rebuilt != rom_bytes:
        diff = next(i for i in range(min(len(rebuilt), len(rom_bytes))) if rebuilt[i] != rom_bytes[i]) \
            if len(rebuilt) == len(rom_bytes) else -1
        raise AssertionError("round trip distinto en %s (offset %d, tamaños %d/%d)" % (name, diff, len(rebuilt), len(rom_bytes)))
    return d


def run_all(verbose=True):
    tmp = tempfile.mkdtemp(prefix="retrofusion_selftest_")
    results = []

    def step(name, fn):
        try:
            fn()
            results.append((name, True, ""))
        except Exception as e:  # report every failure, keep going
            results.append((name, False, "%s: %s" % (type(e).__name__, e)))

    try:
        step("CPU 6502 (flags, saltos, pila)", test_cpu)
        step("Codificación de instrucciones", lambda: test_encoding(tmp))
        state = {}

        def nrom():
            data = _assemble_text(tmp, "demo_nrom.asm", DEMO_NROM, {"chr.bin": make_chr()})
            state["nrom"] = data
            d = roundtrip(data, tmp, "demo_nrom")
            idx = d.index()
            names = {r["name"] for r in idx["routines"]}
            assert idx["jump_engines"], "no detectó el jump engine"
            assert idx["rts_dispatch"], "no detectó el despacho por RTS"
            assert any(n.startswith("S00_") for n in names)
            state["nrom_d"] = d

        def nrom_hints():
            data = state["nrom"]
            rom = NesRom(data, "demo")
            lo = data.index(bytes([0x60]), 16)  # irrelevant, just make sure hints parse
            del lo
            d0 = state["nrom_d"]
            disp = d0.rts_dispatch[0]
            # ActionLo/ActionHi tables follow the dispatcher's 4 small routines.
            text = open(os.path.join(tmp, "demo_nrom", "bank_00.asm"), encoding="utf-8").read()
            assert "JMP (temp)" in text or "JMP ($0000)" in text
            hints = {"jump_tables": [], "entry_points": []}
            # Locate tables by assembling symbols from the source.
            _, asm = assemble_file(os.path.join(tmp, "demo_nrom.asm"))
            s = asm.symbols
            hints["jump_tables"].append({"bank": 0, "addr": "%04X" % s["ActionLo"], "hi_addr": "%04X" % s["ActionHi"],
                                         "count": 4, "format": "split_minus1"})
            hints["entry_points"].append({"bank": 0, "addr": "%04X" % s["Unreached"]})
            symbols = {"labels": {"00:%04X" % s["UpdatePlayer"]: {"name": "UpdatePlayer", "comment": "Gravedad"}},
                       "ram": {"0010": {"name": "player_y"}, "0011": {"name": "player_vy"}, "0002": {"name": "ptr", "size": 2}},
                       "comments": {"00:%04X" % s["UpdSkip"]: "suma la velocidad"}}
            d = Disassembly(rom, hints=hints, symbols=symbols)
            d.run()
            out = os.path.join(tmp, "demo_nrom_h")
            d.write(out)
            rebuilt, _ = assemble_file(os.path.join(out, "main.asm"))
            assert rebuilt == data, "round trip con hints/symbols distinto"
            text = open(os.path.join(out, "bank_00.asm"), encoding="utf-8").read()
            assert "UpdatePlayer:" in text and "player_vy" in text and "a:player_vy" in text
            assert "<(S00_" in text, "la tabla partida no se emitió simbólica"
            del disp

        def uxrom():
            data = _assemble_text(tmp, "demo_ux.asm", DEMO_UXROM)
            d = roundtrip(data, tmp, "demo_ux")
            idx = d.index()
            assert "$8000" in idx["unresolved_targets"], "no reportó llamadas a la ventana intercambiable"
            hints = {"entry_points": [{"bank": 0, "addr": "8000"}, {"bank": 1, "addr": "8000"}]}
            d2 = roundtrip(data, tmp, "demo_ux_h", hints)
            assert any(r["key"] == "01:8000" for r in d2.index()["routines"])

        step("Round trip NROM (jump engine, RTS dispatch)", nrom)
        step("Round trip con hints y symbols", nrom_hints)
        step("Round trip UxROM con bancos", uxrom)

        def verify_demo():
            from .verify import verify_routine
            data = state["nrom"]
            rom = NesRom(data, "demo")
            _, asm = assemble_file(os.path.join(tmp, "demo_nrom.asm"))
            impl = os.path.join(tmp, "impl_ok.py")
            with open(impl, "w") as f:
                f.write(IMPL_OK)
            res = verify_routine(rom, 0, asm.symbols["UpdatePlayer"], impl + ":update_player", cases=300, seed=7)
            assert res["ok"], res["summary"]
            with open(os.path.join(tmp, "impl_bad.py"), "w") as f:
                f.write(IMPL_BAD)
            res = verify_routine(rom, 0, asm.symbols["UpdatePlayer"], os.path.join(tmp, "impl_bad.py") + ":update_player",
                                 cases=300, seed=7)
            assert not res["ok"], "el verificador no detectó una traducción incorrecta"

        step("Verificador diferencial", verify_demo)

        def gfx_demo():
            from . import gfx
            chr_data = make_chr()
            png = os.path.join(tmp, "chr.png")
            gfx.chr_to_png(chr_data, png)
            back = gfx.png_to_chr(png, len(chr_data))
            assert back == chr_data, "PNG -> CHR no devuelve los mismos bytes"

        step("Gráficos CHR <-> PNG", gfx_demo)

        def ips_demo():
            from . import patch
            a = bytes(range(256)) * 64
            b = bytearray(a)
            b[100] ^= 0xFF
            b[5000:5010] = b"RETROFUSIO"
            b += b"EXTRA"
            p = patch.make_ips(a, bytes(b))
            assert patch.apply_ips(a, p) == bytes(b)

        step("Parches IPS", ips_demo)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    if verbose:
        for name, ok, msg in results:
            print("[%s] %s%s" % ("OK" if ok else "FALLA", name, (" -> " + msg) if msg else ""))
    return all(ok for _, ok, _ in results)


IMPL_OK = '''
def update_player(s):
    vy = s.ram[0x11]
    if vy < 4:
        vy += 1
        s.ram[0x11] = vy
    y = s.ram[0x10] + vy
    if (y & 0xFF) >= 200 or y > 0xFF:
        # original: CLC/ADC then CMP #200 on the 8-bit result
        pass
    y8 = y & 0xFF
    if y8 >= 200:
        y8 = 200
        s.ram[0x11] = 0
        s.x = 0
    s.ram[0x10] = y8
    s.a = y8
'''

IMPL_BAD = '''
def update_player(s):
    vy = min(s.ram[0x11] + 1, 4)
    s.ram[0x11] = vy
    y = min(s.ram[0x10] + vy, 200)
    if y == 200:
        s.ram[0x11] = 0
    s.ram[0x10] = y
'''
