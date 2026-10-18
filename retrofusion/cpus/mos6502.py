"""6502 (NES 2A03) opcode table and a small, exact CPU interpreter.

The interpreter is used to verify translated routines: it runs the original
machine code and the translation from the same starting state and compares
the results. The 2A03 has no decimal mode, so ADC/SBC are always binary.
"""

# mode -> instruction size in bytes
MODE_SIZE = {
    "imp": 1, "acc": 1, "imm": 2, "zp": 2, "zpx": 2, "zpy": 2, "rel": 2,
    "izx": 2, "izy": 2, "abs": 3, "abx": 3, "aby": 3, "ind": 3,
}

_TABLE = """
00 BRK imp|01 ORA izx|05 ORA zp|06 ASL zp|08 PHP imp|09 ORA imm|0A ASL acc|0D ORA abs|0E ASL abs
10 BPL rel|11 ORA izy|15 ORA zpx|16 ASL zpx|18 CLC imp|19 ORA aby|1D ORA abx|1E ASL abx
20 JSR abs|21 AND izx|24 BIT zp|25 AND zp|26 ROL zp|28 PLP imp|29 AND imm|2A ROL acc|2C BIT abs|2D AND abs|2E ROL abs
30 BMI rel|31 AND izy|35 AND zpx|36 ROL zpx|38 SEC imp|39 AND aby|3D AND abx|3E ROL abx
40 RTI imp|41 EOR izx|45 EOR zp|46 LSR zp|48 PHA imp|49 EOR imm|4A LSR acc|4C JMP abs|4D EOR abs|4E LSR abs
50 BVC rel|51 EOR izy|55 EOR zpx|56 LSR zpx|58 CLI imp|59 EOR aby|5D EOR abx|5E LSR abx
60 RTS imp|61 ADC izx|65 ADC zp|66 ROR zp|68 PLA imp|69 ADC imm|6A ROR acc|6C JMP ind|6D ADC abs|6E ROR abs
70 BVS rel|71 ADC izy|75 ADC zpx|76 ROR zpx|78 SEI imp|79 ADC aby|7D ADC abx|7E ROR abx
81 STA izx|84 STY zp|85 STA zp|86 STX zp|88 DEY imp|8A TXA imp|8C STY abs|8D STA abs|8E STX abs
90 BCC rel|91 STA izy|94 STY zpx|95 STA zpx|96 STX zpy|98 TYA imp|99 STA aby|9A TXS imp|9D STA abx
A0 LDY imm|A1 LDA izx|A2 LDX imm|A4 LDY zp|A5 LDA zp|A6 LDX zp|A8 TAY imp|A9 LDA imm|AA TAX imp|AC LDY abs|AD LDA abs|AE LDX abs
B0 BCS rel|B1 LDA izy|B4 LDY zpx|B5 LDA zpx|B6 LDX zpy|B8 CLV imp|B9 LDA aby|BA TSX imp|BC LDY abx|BD LDA abx|BE LDX aby
C0 CPY imm|C1 CMP izx|C4 CPY zp|C5 CMP zp|C6 DEC zp|C8 INY imp|C9 CMP imm|CA DEX imp|CC CPY abs|CD CMP abs|CE DEC abs
D0 BNE rel|D1 CMP izy|D5 CMP zpx|D6 DEC zpx|D8 CLD imp|D9 CMP aby|DD CMP abx|DE DEC abx
E0 CPX imm|E1 SBC izx|E4 CPX zp|E5 SBC zp|E6 INC zp|E8 INX imp|E9 SBC imm|EA NOP imp|EC CPX abs|ED SBC abs|EE INC abs
F0 BEQ rel|F1 SBC izy|F5 SBC zpx|F6 INC zpx|F8 SED imp|F9 SBC aby|FD SBC abx|FE INC abx
"""

# opcode byte -> (mnemonic, mode)
OPCODES = {}
for _line in _TABLE.strip().splitlines():
    for _item in _line.split("|"):
        _op, _mn, _mode = _item.split()
        OPCODES[int(_op, 16)] = (_mn, _mode)

# (mnemonic, mode) -> opcode byte
ENCODE = {v: k for k, v in OPCODES.items()}
MNEMONICS = {mn for mn, _ in OPCODES.values()}

BRANCHES = {"BPL", "BMI", "BVC", "BVS", "BCC", "BCS", "BNE", "BEQ"}
# Instructions after which execution never falls through to the next byte.
FLOW_ENDS = {"RTS", "RTI", "JMP", "BRK"}

assert len(OPCODES) == 151, len(OPCODES)

# Status flag bits
C, Z, I, D, B, U, V, N = 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80


class StepLimit(Exception):
    pass


class CPU:
    """Interpreter for official 6502 opcodes.

    `read(addr)` and `write(addr, value)` are supplied by the caller so the
    memory map (RAM mirrors, I/O, PRG banks) stays outside the CPU.
    """

    def __init__(self, read, write):
        self.read = read
        self.write = write
        self.a = self.x = self.y = 0
        self.sp = 0xFD
        self.p = I | U
        self.pc = 0
        self.steps = 0

    # -- helpers -----------------------------------------------------------
    def _r16(self, addr):
        return self.read(addr) | (self.read((addr + 1) & 0xFFFF) << 8)

    def _r16_zp(self, zp):
        return self.read(zp & 0xFF) | (self.read((zp + 1) & 0xFF) << 8)

    def push(self, v):
        self.write(0x100 | self.sp, v & 0xFF)
        self.sp = (self.sp - 1) & 0xFF

    def pull(self):
        self.sp = (self.sp + 1) & 0xFF
        return self.read(0x100 | self.sp)

    def _nz(self, v):
        self.p = (self.p & ~(N | Z)) | (v & N) | (0 if v else Z)
        return v

    def _flag(self, bit, on):
        if on:
            self.p |= bit
        else:
            self.p &= ~bit

    def _addr(self, mode):
        """Return the effective address for `mode` and advance PC."""
        pc = self.pc
        if mode == "imm":
            self.pc = (pc + 1) & 0xFFFF
            return pc
        if mode == "zp":
            self.pc = (pc + 1) & 0xFFFF
            return self.read(pc)
        if mode == "zpx":
            self.pc = (pc + 1) & 0xFFFF
            return (self.read(pc) + self.x) & 0xFF
        if mode == "zpy":
            self.pc = (pc + 1) & 0xFFFF
            return (self.read(pc) + self.y) & 0xFF
        if mode == "abs":
            self.pc = (pc + 2) & 0xFFFF
            return self._r16(pc)
        if mode == "abx":
            self.pc = (pc + 2) & 0xFFFF
            return (self._r16(pc) + self.x) & 0xFFFF
        if mode == "aby":
            self.pc = (pc + 2) & 0xFFFF
            return (self._r16(pc) + self.y) & 0xFFFF
        if mode == "izx":
            self.pc = (pc + 1) & 0xFFFF
            return self._r16_zp(self.read(pc) + self.x)
        if mode == "izy":
            self.pc = (pc + 1) & 0xFFFF
            return (self._r16_zp(self.read(pc)) + self.y) & 0xFFFF
        if mode == "ind":
            self.pc = (pc + 2) & 0xFFFF
            ptr = self._r16(pc)
            # 6502 bug: the high byte is read from the same page.
            hi_addr = (ptr & 0xFF00) | ((ptr + 1) & 0xFF)
            return self.read(ptr) | (self.read(hi_addr) << 8)
        if mode == "rel":
            self.pc = (pc + 1) & 0xFFFF
            off = self.read(pc)
            if off >= 0x80:
                off -= 0x100
            return (self.pc + off) & 0xFFFF
        raise ValueError(mode)

    def _adc(self, m):
        a = self.a
        t = a + m + (self.p & C)
        self._flag(C, t > 0xFF)
        t &= 0xFF
        self._flag(V, (~(a ^ m) & (a ^ t) & 0x80) != 0)
        self.a = self._nz(t)

    def _cmp(self, reg, m):
        self._flag(C, reg >= m)
        self._nz((reg - m) & 0xFF)

    # -- execution ---------------------------------------------------------
    def step(self):
        op = self.read(self.pc)
        entry = OPCODES.get(op)
        if entry is None:
            raise RuntimeError("opcode no oficial/invalido $%02X en $%04X" % (op, self.pc))
        mn, mode = entry
        self.pc = (self.pc + 1) & 0xFFFF
        self.steps += 1

        if mode in ("imp", "acc"):
            self._exec_implied(mn, mode)
            return
        addr = self._addr(mode)

        if mn in BRANCHES:
            cond = {
                "BPL": not self.p & N, "BMI": bool(self.p & N),
                "BVC": not self.p & V, "BVS": bool(self.p & V),
                "BCC": not self.p & C, "BCS": bool(self.p & C),
                "BNE": not self.p & Z, "BEQ": bool(self.p & Z),
            }[mn]
            if cond:
                self.pc = addr
        elif mn == "LDA":
            self.a = self._nz(self.read(addr))
        elif mn == "LDX":
            self.x = self._nz(self.read(addr))
        elif mn == "LDY":
            self.y = self._nz(self.read(addr))
        elif mn == "STA":
            self.write(addr, self.a)
        elif mn == "STX":
            self.write(addr, self.x)
        elif mn == "STY":
            self.write(addr, self.y)
        elif mn == "ADC":
            self._adc(self.read(addr))
        elif mn == "SBC":
            self._adc(self.read(addr) ^ 0xFF)
        elif mn == "AND":
            self.a = self._nz(self.a & self.read(addr))
        elif mn == "ORA":
            self.a = self._nz(self.a | self.read(addr))
        elif mn == "EOR":
            self.a = self._nz(self.a ^ self.read(addr))
        elif mn == "CMP":
            self._cmp(self.a, self.read(addr))
        elif mn == "CPX":
            self._cmp(self.x, self.read(addr))
        elif mn == "CPY":
            self._cmp(self.y, self.read(addr))
        elif mn == "BIT":
            m = self.read(addr)
            self._flag(Z, (self.a & m) == 0)
            self.p = (self.p & ~(N | V)) | (m & (N | V))
        elif mn in ("INC", "DEC"):
            m = (self.read(addr) + (1 if mn == "INC" else -1)) & 0xFF
            self.write(addr, m)
            self._nz(m)
        elif mn in ("ASL", "LSR", "ROL", "ROR"):
            self.write(addr, self._shift(mn, self.read(addr)))
        elif mn == "JMP":
            self.pc = addr
        elif mn == "JSR":
            ret = (self.pc - 1) & 0xFFFF
            self.push(ret >> 8)
            self.push(ret)
            self.pc = addr
        else:
            raise RuntimeError("sin implementar: " + mn)

    def _shift(self, mn, m):
        c_in = self.p & C
        if mn == "ASL":
            self._flag(C, m & 0x80)
            m = (m << 1) & 0xFF
        elif mn == "LSR":
            self._flag(C, m & 0x01)
            m >>= 1
        elif mn == "ROL":
            self._flag(C, m & 0x80)
            m = ((m << 1) | c_in) & 0xFF
        else:  # ROR
            self._flag(C, m & 0x01)
            m = (m >> 1) | (0x80 if c_in else 0)
        return self._nz(m)

    def _exec_implied(self, mn, mode):
        if mode == "acc":
            self.a = self._shift(mn, self.a)
        elif mn == "NOP":
            pass
        elif mn == "TAX":
            self.x = self._nz(self.a)
        elif mn == "TAY":
            self.y = self._nz(self.a)
        elif mn == "TXA":
            self.a = self._nz(self.x)
        elif mn == "TYA":
            self.a = self._nz(self.y)
        elif mn == "TSX":
            self.x = self._nz(self.sp)
        elif mn == "TXS":
            self.sp = self.x
        elif mn == "INX":
            self.x = self._nz((self.x + 1) & 0xFF)
        elif mn == "INY":
            self.y = self._nz((self.y + 1) & 0xFF)
        elif mn == "DEX":
            self.x = self._nz((self.x - 1) & 0xFF)
        elif mn == "DEY":
            self.y = self._nz((self.y - 1) & 0xFF)
        elif mn == "CLC":
            self.p &= ~C
        elif mn == "SEC":
            self.p |= C
        elif mn == "CLI":
            self.p &= ~I
        elif mn == "SEI":
            self.p |= I
        elif mn == "CLV":
            self.p &= ~V
        elif mn == "CLD":
            self.p &= ~D
        elif mn == "SED":
            self.p |= D
        elif mn == "PHA":
            self.push(self.a)
        elif mn == "PHP":
            self.push(self.p | B | U)
        elif mn == "PLA":
            self.a = self._nz(self.pull())
        elif mn == "PLP":
            self.p = (self.pull() & ~B) | U
        elif mn == "RTS":
            lo = self.pull()
            hi = self.pull()
            self.pc = (((hi << 8) | lo) + 1) & 0xFFFF
        elif mn == "RTI":
            self.p = (self.pull() & ~B) | U
            lo = self.pull()
            hi = self.pull()
            self.pc = (hi << 8) | lo
        elif mn == "BRK":
            ret = (self.pc + 1) & 0xFFFF
            self.push(ret >> 8)
            self.push(ret)
            self.push(self.p | B | U)
            self.p |= I
            self.pc = self._r16(0xFFFE)
        else:
            raise RuntimeError("sin implementar: " + mn)

    def run_until(self, stop_pc, max_steps):
        """Run until PC == stop_pc. Raises StepLimit after max_steps."""
        while self.pc != stop_pc:
            if self.steps >= max_steps:
                raise StepLimit()
            self.step()


def decode(read, pc):
    """Decode one instruction at pc. Returns (mnemonic, mode, operand, size) or None."""
    entry = OPCODES.get(read(pc))
    if entry is None:
        return None
    mn, mode = entry
    size = MODE_SIZE[mode]
    if size == 1:
        operand = None
    elif size == 2:
        operand = read((pc + 1) & 0xFFFF)
    else:
        operand = read((pc + 1) & 0xFFFF) | (read((pc + 2) & 0xFFFF) << 8)
    return mn, mode, operand, size
