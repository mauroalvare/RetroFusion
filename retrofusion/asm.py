"""Two-pass 6502 assembler for the RetroFusion source dialect.

Syntax (a small, ca65-like subset):
    label:                      code/data label
    name = expr                 constant
    LDA #$10 / LDA a:$0010      immediate / force absolute addressing
    .byte $01, "text", <label   bytes (strings allowed)
    .word label, label-1        16-bit little endian
    .bank n, base, size         start PRG bank n seen by the CPU at `base`
    .endbank                    pad the bank with $FF up to its size
    .org addr                   pad inside the bank up to addr
    .fill count[, value]        repeat a byte
    .incbin "file"[, off[, len]]
    .include "file"
Expressions: $hex %bin decimal 'c' names * (current address) + - * / & | ^
<< >> ~ and unary < (low byte) > (high byte), parentheses.
"""

import os
import re

from .cpus.mos6502 import ENCODE, MNEMONICS, BRANCHES


class AsmError(Exception):
    pass


class Unresolved(Exception):
    pass


LABEL_RE = re.compile(r"^\s*([A-Za-z_][A-Za-z0-9_]*):")
EQU_RE = re.compile(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+)$")
TOKEN_RE = re.compile(r"\s*(?:(\$[0-9A-Fa-f]+)|(%[01]+)|(0x[0-9A-Fa-f]+)|(\d+)|('(?:\\.|[^'])')"
                      r"|([A-Za-z_][A-Za-z0-9_]*)|(<<|>>|[-+*/&|^~<>()]))")


def strip_comment(line):
    out = []
    quote = None
    for ch in line:
        if quote:
            out.append(ch)
            if ch == quote:
                quote = None
        elif ch in "\"'":
            quote = ch
            out.append(ch)
        elif ch == ";":
            break
        else:
            out.append(ch)
    return "".join(out).rstrip()


def split_args(text):
    """Split on commas that are not inside quotes or parentheses."""
    args, cur, depth, quote = [], [], 0, None
    for ch in text:
        if quote:
            cur.append(ch)
            if ch == quote:
                quote = None
            continue
        if ch in "\"'":
            quote = ch
        elif ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        elif ch == "," and depth == 0:
            args.append("".join(cur).strip())
            cur = []
            continue
        cur.append(ch)
    if cur or args:
        args.append("".join(cur).strip())
    return args


class Expr:
    def __init__(self, text, symbols, pc):
        self.text = text
        self.symbols = symbols
        self.pc = pc
        self.tokens = []
        pos = 0
        text = text.strip()
        while pos < len(text):
            m = TOKEN_RE.match(text, pos)
            if not m or m.end() == pos:
                raise AsmError("expresión inválida: %r" % self.text)
            self.tokens.append(m)
            pos = m.end()
        self.i = 0

    def peek(self):
        if self.i < len(self.tokens):
            return self.tokens[self.i].group(7)
        return None

    def value(self):
        if not self.tokens:
            raise AsmError("expresión vacía")
        v = self.parse_or()
        if self.i != len(self.tokens):
            raise AsmError("expresión inválida: %r" % self.text)
        return v

    def _binary(self, ops, sub):
        v = sub()
        while self.peek() in ops:
            op = self.peek()
            self.i += 1
            r = sub()
            if op == "|":
                v |= r
            elif op == "^":
                v ^= r
            elif op == "&":
                v &= r
            elif op == "<<":
                v <<= r
            elif op == ">>":
                v >>= r
            elif op == "+":
                v += r
            elif op == "-":
                v -= r
            elif op == "*":
                v *= r
            elif op == "/":
                if r == 0:
                    raise AsmError("división por cero")
                v //= r
        return v

    def parse_or(self):
        return self._binary(("|",), self.parse_xor)

    def parse_xor(self):
        return self._binary(("^",), self.parse_and)

    def parse_and(self):
        return self._binary(("&",), self.parse_shift)

    def parse_shift(self):
        return self._binary(("<<", ">>"), self.parse_add)

    def parse_add(self):
        return self._binary(("+", "-"), self.parse_mul)

    def parse_mul(self):
        return self._binary(("*", "/"), self.parse_unary)

    def parse_unary(self):
        op = self.peek()
        if op in ("-", "~", "<", ">", "+"):
            self.i += 1
            v = self.parse_unary()
            return {"-": -v, "~": ~v & 0xFFFF, "<": v & 0xFF, ">": (v >> 8) & 0xFF, "+": v}[op]
        return self.parse_atom()

    def parse_atom(self):
        if self.i >= len(self.tokens):
            raise AsmError("expresión incompleta: %r" % self.text)
        m = self.tokens[self.i]
        self.i += 1
        hexv, binv, cx, dec, ch, name, op = m.groups()
        if hexv:
            return int(hexv[1:], 16)
        if binv:
            return int(binv[1:], 2)
        if cx:
            return int(cx[2:], 16)
        if dec:
            return int(dec)
        if ch:
            body = ch[1:-1]
            return ord(body[1]) if body.startswith("\\") else ord(body)
        if name:
            if name in self.symbols:
                return self.symbols[name]
            raise Unresolved(name)
        if op == "*":
            if self.pc is None:
                raise AsmError("'*' fuera de un banco")
            return self.pc
        if op == "(":
            v = self.parse_or()
            if self.peek() != ")":
                raise AsmError("falta ')' en %r" % self.text)
            self.i += 1
            return v
        raise AsmError("token inesperado %r en %r" % (op, self.text))


class Line:
    __slots__ = ("file", "num", "text", "label", "op", "args", "mode", "equ")

    def __init__(self, file, num, text):
        self.file, self.num, self.text = file, num, text
        self.label = self.op = self.args = self.mode = self.equ = None


class Assembler:
    def __init__(self):
        self.symbols = {}
        self.lines = []
        self.bank_map = []   # (bank, out_offset, base, size)

    # -- parsing ----------------------------------------------------------------
    def load(self, path, depth=0):
        if depth > 16:
            raise AsmError("demasiados .include anidados")
        path = os.path.abspath(path)
        try:
            with open(path, encoding="utf-8") as f:
                raw = f.read().splitlines()
        except OSError as e:
            raise AsmError("no se puede abrir %s: %s" % (path, e))
        base_dir = os.path.dirname(path)
        for num, text in enumerate(raw, 1):
            body = strip_comment(text)
            if not body.strip():
                continue
            ln = Line(path, num, text)
            m = LABEL_RE.match(body)
            if m:
                ln.label = m.group(1)
                body = body[m.end():]
            m = EQU_RE.match(body)
            if m and not ln.label:
                ln.equ = (m.group(1), m.group(2).strip())
                self.lines.append(ln)
                continue
            body = body.strip()
            if body:
                parts = body.split(None, 1)
                ln.op = parts[0].lower()
                ln.args = parts[1].strip() if len(parts) > 1 else ""
                if ln.op == ".include":
                    if ln.label:
                        self.lines.append(self._label_only(ln))
                    inc = self._string_arg(ln, ln.args)
                    self.load(os.path.join(base_dir, inc), depth + 1)
                    continue
                if ln.op == ".incbin":
                    args = split_args(ln.args)
                    args[0] = os.path.join(base_dir, self._string_arg(ln, args[0]))
                    ln.args = args
            self.lines.append(ln)

    def _label_only(self, ln):
        x = Line(ln.file, ln.num, ln.text)
        x.label = ln.label
        return x

    def _string_arg(self, ln, s):
        s = s.strip()
        if len(s) >= 2 and s[0] == s[-1] == '"':
            return s[1:-1]
        raise self.err(ln, "se esperaba un texto entre comillas")

    def err(self, ln, msg):
        return AsmError("%s:%d: %s\n    %s" % (os.path.basename(ln.file), ln.num, msg, ln.text.strip()))

    # -- evaluation ---------------------------------------------------------------
    def eval(self, ln, text, pc, final):
        try:
            return Expr(text, self.symbols, pc).value()
        except Unresolved as u:
            if final:
                raise self.err(ln, "símbolo no definido: %s" % u.args[0])
            return None
        except AsmError as e:
            raise self.err(ln, str(e))

    def operand_mode(self, ln, mn, arg, pc, final):
        """Return (mode, expression text)."""
        a = arg.strip()
        if a == "" or a.upper() == "A":
            if (mn, "acc") in ENCODE:
                return "acc", None
            if (mn, "imp") in ENCODE and a == "":
                return "imp", None
            raise self.err(ln, "%s necesita un operando" % mn)
        if mn in BRANCHES:
            return "rel", a
        if a.startswith("#"):
            return "imm", a[1:]
        force = None
        if a[:2].lower() in ("a:", "z:"):
            force = a[0].lower()
            a = a[2:].strip()
        m = re.match(r"^\((.*),\s*[xX]\s*\)$", a)
        if m:
            return "izx", m.group(1)
        m = re.match(r"^\((.*)\)\s*,\s*[yY]$", a)
        if m:
            return "izy", m.group(1)
        if mn == "JMP":
            m = re.match(r"^\((.*)\)$", a)
            if m:
                return "ind", m.group(1)
        idx = None
        m = re.match(r"^(.*?),\s*([xXyY])$", a)
        if m:
            a, idx = m.group(1), m.group(2).lower()
        zp_mode = {None: "zp", "x": "zpx", "y": "zpy"}[idx]
        abs_mode = {None: "abs", "x": "abx", "y": "aby"}[idx]
        has_zp = (mn, zp_mode) in ENCODE
        has_abs = (mn, abs_mode) in ENCODE
        if force == "a" and has_abs:
            return abs_mode, a
        if force == "z" and has_zp:
            return zp_mode, a
        v = self.eval(ln, a, pc, False)
        if has_zp and (not has_abs or (v is not None and 0 <= v <= 0xFF)):
            return zp_mode, a
        if has_abs:
            return abs_mode, a
        raise self.err(ln, "modo de direccionamiento inválido para %s" % mn)

    # -- passes -----------------------------------------------------------------
    def run_pass(self, final):
        out = bytearray()
        pc = None
        bank = None  # (index, out_start, base, size)
        if final:
            self.bank_map = []
        for ln in self.lines:
            if ln.equ:
                name, text = ln.equ
                v = self.eval(ln, text, pc, final)
                if not final:
                    if name in self._labels_seen or (name in self._equs and v is not None and self._equs[name] not in (None, v)):
                        raise self.err(ln, "símbolo definido dos veces: %s" % name)
                    self._equs[name] = v
                if v is not None:
                    self.symbols[name] = v
                continue
            if ln.label:
                if not final:
                    if ln.label in self._labels_seen or ln.label in self._equs:
                        raise self.err(ln, "label definido dos veces: %s" % ln.label)
                    self._labels_seen.add(ln.label)
                self.symbols[ln.label] = pc if pc is not None else 0
            op = ln.op
            if op is None:
                continue
            if op == ".bank":
                if bank is not None:
                    raise self.err(ln, "falta .endbank antes de un nuevo .bank")
                args = split_args(ln.args)
                if len(args) != 3:
                    raise self.err(ln, ".bank necesita: número, dirección base, tamaño")
                idx, base, size = (self.eval(ln, x, None, True) for x in args)
                bank = (idx, len(out), base, size)
                pc = base
                continue
            if op == ".endbank":
                if bank is None:
                    raise self.err(ln, ".endbank sin .bank")
                idx, start, base, size = bank
                used = len(out) - start
                if used > size:
                    raise self.err(ln, "el banco $%02X se pasó %d bytes de su tamaño ($%04X). Liberá espacio o mové código a otro banco." % (idx, used - size, size))
                out.extend(b"\xFF" * (size - used))
                if final:
                    self.bank_map.append((idx, start, base, size, used))
                bank = None
                pc = None
                continue
            if op == ".org":
                if bank is None:
                    raise self.err(ln, ".org solo dentro de un .bank")
                target = self.eval(ln, ln.args, pc, True)
                if target < pc:
                    raise self.err(ln, ".org $%04X está antes de la posición actual $%04X" % (target, pc))
                out.extend(b"\xFF" * (target - pc))
                pc = target
                continue
            start_len = len(out)
            if op in (".byte", ".db", ".word", ".dw", ".fill", ".res", ".incbin"):
                self.data_directive(ln, op, out, pc, final)
            elif op.startswith("."):
                raise self.err(ln, "directiva desconocida: %s" % op)
            else:
                self.instruction(ln, op.upper(), out, pc, final)
            if pc is not None:
                pc += len(out) - start_len
        if bank is not None:
            raise self.err(self.lines[-1], "falta .endbank al final")
        return bytes(out)

    def data_directive(self, ln, op, out, pc, final):
        if op == ".incbin":
            args = ln.args
            try:
                with open(args[0], "rb") as f:
                    blob = f.read()
            except OSError as e:
                raise self.err(ln, "no se puede abrir %s: %s" % (args[0], e))
            off = self.eval(ln, args[1], pc, True) if len(args) > 1 else 0
            length = self.eval(ln, args[2], pc, True) if len(args) > 2 else len(blob) - off
            out.extend(blob[off:off + length])
            return
        if op in (".fill", ".res"):
            args = split_args(ln.args)
            count = self.eval(ln, args[0], pc, True)
            value = self.eval(ln, args[1], pc, True) if len(args) > 1 else 0xFF
            out.extend(bytes([value & 0xFF]) * count)
            return
        width = 1 if op in (".byte", ".db") else 2
        for arg in split_args(ln.args):
            if width == 1 and len(arg) >= 2 and arg[0] == arg[-1] == '"':
                out.extend(arg[1:-1].encode("latin-1"))
                continue
            v = self.eval(ln, arg, pc, final)
            if v is None:
                v = 0
            if final:
                lo, hi = (-0x80, 0xFF) if width == 1 else (-0x8000, 0xFFFF)
                if not lo <= v <= hi:
                    raise self.err(ln, "valor fuera de rango: %s = %d" % (arg, v))
            v &= 0xFF if width == 1 else 0xFFFF
            out.append(v & 0xFF)
            if width == 2:
                out.append(v >> 8)

    def instruction(self, ln, mn, out, pc, final):
        if mn not in MNEMONICS:
            raise self.err(ln, "instrucción desconocida: %s" % mn)
        if pc is None:
            raise self.err(ln, "instrucción fuera de un .bank")
        if final:
            mode, text = ln.mode
        else:
            mode, text = self.operand_mode(ln, mn, ln.args, pc, False)
            ln.mode = (mode, text)
        opcode = ENCODE[(mn, mode)]
        out.append(opcode)
        if mode in ("imp", "acc"):
            return
        v = self.eval(ln, text, pc, final)
        if v is None:
            v = 0 if mode in ("zp", "zpx", "zpy", "izx", "izy", "imm", "rel") else 0x8000
        if mode == "rel":
            if not final:
                out.append(0)
                return
            off = v - (pc + 2)
            if not -128 <= off <= 127:
                raise self.err(ln, "salto fuera de rango (%d bytes). Usá un salto condicional invertido + JMP." % off)
            out.append(off & 0xFF)
            return
        if mode in ("imm", "zp", "zpx", "zpy", "izx", "izy"):
            if final and not -128 <= v <= 0xFF:
                raise self.err(ln, "valor de 8 bits fuera de rango: %d" % v)
            out.append(v & 0xFF)
            return
        if final and not 0 <= v <= 0xFFFF:
            raise self.err(ln, "dirección fuera de rango: %d" % v)
        out.append(v & 0xFF)
        out.append((v >> 8) & 0xFF)

    def assemble(self, path):
        self.lines = []
        self.symbols = {}
        self.load(path)
        self._labels_seen = set()
        self._equs = {}
        self.run_pass(False)
        return self.run_pass(True)


def assemble_file(path):
    asm = Assembler()
    data = asm.assemble(path)
    return data, asm
