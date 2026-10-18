"""Recursive-descent disassembler for NES PRG banks.

Separates code from data by following execution from the vectors, from
entry points in hints.json and from a Code/Data Log (CDL) recorded in an
emulator. Writes re-assemblable source (byte-exact round trip), an index of
routines with cross references, and a report of what still needs a human or
AI decision.
"""

import os
import re

from .cpus.mos6502 import OPCODES, MODE_SIZE, BRANCHES
from .platforms.nes import NES_REGISTERS

UNKNOWN, CODE, OPERAND, DATA = 0, 1, 2, 3
KIND_PRIORITY = {"V": 5, "S": 4, "L": 3, "T": 2, "D": 1}
NAME_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
WRITES = {"STA", "STX", "STY", "INC", "DEC", "ASL", "LSR", "ROL", "ROR"}
RMW = {"INC", "DEC", "ASL", "LSR", "ROL", "ROR"}


def parse_addr(v):
    if isinstance(v, int):
        return v
    s = str(v).strip().lstrip("$")
    if s.lower().startswith("0x"):
        s = s[2:]
    return int(s, 16)


def key(b, a):
    return "%02X:%04X" % (b, a)


def parse_key(k):
    b, a = str(k).split(":")
    return int(b, 16), int(a, 16)


def load_cdl(path, prg_len, chr_len):
    """Load an FCEUX or Mesen CDL file. Returns (prg_flags, is_mesen)."""
    with open(path, "rb") as f:
        data = f.read()
    is_mesen = data.startswith(b"CDL")
    for total in (prg_len + chr_len, prg_len):
        if len(data) == total or (is_mesen and len(data) > total):
            body = data[len(data) - total:]
            return body[:prg_len], is_mesen
    raise ValueError("El CDL no coincide con el tamaño de la ROM (%d bytes)." % len(data))


class Disassembly:
    def __init__(self, rom, hints=None, symbols=None, cdl_path=None):
        self.rom = rom
        self.hints = hints or {}
        self.symbols = symbols or {}
        self.cdl = None
        self.cdl_mesen = False
        if cdl_path:
            self.cdl, self.cdl_mesen = load_cdl(cdl_path, len(rom.prg), len(rom.chr))

        overrides = {k: self.hints[k] for k in ("bank_size", "banks") if k in self.hints}
        self.banks = rom.layout(overrides)
        if self.cdl is not None and not self.cdl_mesen:
            self._bases_from_cdl(overrides.get("banks", {}))
        self.data = [rom.prg[b.offset:b.offset + b.size] for b in self.banks]
        self.single16 = len(self.banks) == 1 and self.banks[0].size == 16384

        self.symbol_errors = []
        self.sym_labels = {}      # (b, a) -> (name, comment)
        self.sym_comments = {}    # (b, a) -> text
        self.ram_names = {}       # addr -> (name, offset)
        self.ram_defs = []        # (addr, name, size, comment)
        self._load_symbols()
        self._reset()

    # -- setup ----------------------------------------------------------------
    def _reset(self):
        self.flags = [("error", e) for e in self.symbol_errors]
        self.kind = [bytearray(b.size) for b in self.banks]
        self.insts = [dict() for _ in self.banks]   # offset -> (mn, mode, operand, size)
        self.labels = {}                             # (b, addr) -> kind letter
        self.vector_names = {}
        self.xrefs = {}                              # (b, addr) -> [(b, addr, type)]
        self.tables = {}                             # (b, addr) -> (kind, target, size)
        self.mid_refs = set()                        # labels that fall inside an item
        self.unresolved_calls = {}
        self.mapper_writes = []
        self.indirect_jumps = []
        self.rts_dispatch = []
        self.region_comments = {}

    def _bases_from_cdl(self, hinted):
        """FCEUX CDL bits 2-3 tell in which 8 KB CPU window each byte ran."""
        for bank in self.banks:
            if bank.fixed or str(bank.index) in hinted:
                continue
            votes = {}
            for i in range(bank.offset, bank.offset + bank.size):
                f = self.cdl[i]
                if f & 1:
                    w = 0x8000 + ((f >> 2) & 3) * 0x2000
                    votes[w] = votes.get(w, 0) + 1
            if votes:
                window = max(votes, key=votes.get)
                if bank.size == 8192:
                    bank.base = window
                elif bank.size == 16384:
                    bank.base = 0x8000 if window < 0xC000 else 0xC000

    def _load_symbols(self):
        seen = {}
        for k, v in self.symbols.get("labels", {}).items():
            try:
                b, a = parse_key(k)
            except ValueError:
                self.symbol_errors.append("Clave de label inválida: %r (formato BB:AAAA)" % k)
                continue
            if isinstance(v, str):
                v = {"name": v}
            name = v.get("name", "")
            if not NAME_RE.match(name):
                self.symbol_errors.append("Nombre de label inválido en %s: %r" % (k, name))
                continue
            if b >= len(self.banks) or not self.banks[b].contains(a):
                self.symbol_errors.append("El label %s (%s) no cae dentro del banco %02X" % (k, name, b))
                continue
            if name in seen:
                self.symbol_errors.append("Nombre repetido %r en %s y %s" % (name, seen[name], k))
                continue
            seen[name] = k
            self.sym_labels[(b, a)] = (name, v.get("comment", ""))
        for k, v in self.symbols.get("comments", {}).items():
            try:
                self.sym_comments[parse_key(k)] = str(v)
            except ValueError:
                self.symbol_errors.append("Clave de comentario inválida: %r" % k)
        for k, v in self.symbols.get("ram", {}).items():
            addr = parse_addr(k)
            if isinstance(v, str):
                v = {"name": v}
            name = v.get("name", "")
            if not NAME_RE.match(name):
                self.symbol_errors.append("Nombre RAM inválido en %s: %r" % (k, name))
                continue
            if name in seen:
                self.symbol_errors.append("Nombre repetido %r en %s y RAM %s" % (name, seen[name], k))
                continue
            seen[name] = k
            size = max(1, int(v.get("size", 1)))
            self.ram_defs.append((addr, name, size, v.get("comment", "")))
            for i in range(size):
                self.ram_names.setdefault(addr + i, (name, i))
        self.ram_defs.sort()

    # -- address helpers ------------------------------------------------------
    def resolve(self, b, addr):
        """Map a CPU address seen from bank b to (bank, canonical addr) or None."""
        bank = self.banks[b]
        if bank.contains(addr):
            return b, addr
        if self.single16 and addr >= 0x8000:
            return 0, bank.base + ((addr - 0x8000) & 0x3FFF)
        for other in self.banks:
            if other.fixed and other.contains(addr):
                return other.index, addr
        return None

    def byte(self, b, addr):
        return self.data[b][addr - self.banks[b].base]

    def kind_at(self, b, addr):
        return self.kind[b][addr - self.banks[b].base]

    def add_label(self, b, addr, kind):
        old = self.labels.get((b, addr))
        if old is None or KIND_PRIORITY[kind] > KIND_PRIORITY[old]:
            self.labels[(b, addr)] = kind

    def add_xref(self, target, src, typ):
        self.xrefs.setdefault(target, []).append((src[0], src[1], typ))

    # -- analysis -------------------------------------------------------------
    def run(self):
        engines = {(int(j["bank"]), parse_addr(j["addr"])) for j in self.hints.get("jump_engines", [])}
        for _ in range(4):
            self._reset()
            self.jump_engines = set(engines)
            self._analyze()
            new = self._detect_jump_engines() - engines
            if not new:
                break
            # Calls to a jump engine are followed by a table, not code: start
            # over knowing it so the table is not decoded as instructions.
            engines |= new
        for je in sorted(self.jump_engines):
            self.flags.append(("info", "Rutina %s es un 'jump engine' (tabla de direcciones después de cada JSR)." % key(*je)))
        self._mark_data_refs()
        self._scan_patterns()
        self._settle_labels()

    def _analyze(self):
        queue = []
        for d in self.hints.get("data", []):
            b = int(d["bank"])
            start, end = parse_addr(d["start"]), parse_addr(d["end"])
            bank = self.banks[b]
            for a in range(max(start, bank.base), min(end + 1, bank.end)):
                self.kind[b][a - bank.base] = DATA
            self.add_label(b, start, "D")
            if d.get("comment"):
                self.region_comments[(b, start)] = d["comment"]

        vec_list = self.rom.vectors(self.banks)
        for b, vecs in vec_list:
            suffix = "" if len(vec_list) == 1 else "_B%02X" % b
            bank = self.banks[b]
            for i, (name, target) in enumerate(vecs.items()):
                vaddr = 0xFFFA + 2 * i
                self.kind[b][vaddr - bank.base] = DATA
                self.kind[b][vaddr + 1 - bank.base] = DATA
                r = self.resolve(b, target)
                self.tables[(b, vaddr)] = ("word", r, 2)
                if r is None:
                    continue
                self.vector_names.setdefault(r, name + suffix)
                self.add_label(r[0], r[1], "V")
                queue.append((r[0], r[1], "vector " + name))

        for e in self.hints.get("entry_points", []) + self.hints.get("code", []):
            b, a = int(e["bank"]), parse_addr(e["addr"])
            self.add_label(b, a, "S")
            queue.append((b, a, "hint"))

        for jt in self.hints.get("jump_tables", []):
            self._apply_jump_table(jt, queue)

        self._trace(queue)
        if self.cdl is not None:
            self._trace_cdl()
        for _ in range(16):
            new_entries = self._parse_engine_tables()
            if not new_entries:
                break
            self._trace(new_entries)

    def _trace(self, queue):
        while queue:
            b, addr, reason = queue.pop()
            self._trace_from(b, addr, reason, queue)

    def _trace_from(self, b, addr, reason, queue):
        bank = self.banks[b]
        kind = self.kind[b]
        data = self.data[b]
        while True:
            if not bank.contains(addr):
                return
            off = addr - bank.base
            k = kind[off]
            if k == CODE:
                return
            if k == OPERAND:
                self.mid_refs.add((b, addr))
                self.flags.append(("conflict", "Entrada a mitad de instrucción en %s (desde %s)" % (key(b, addr), reason)))
                return
            if k == DATA:
                if (b, addr) not in self.tables:
                    self.flags.append(("warn", "El flujo de código llega a datos marcados en %s (desde %s)" % (key(b, addr), reason)))
                return
            op = data[off]
            entry = OPCODES.get(op)
            if entry is None:
                self.flags.append(("invalid", "Opcode inválido $%02X en %s en un camino de código (desde %s). ¿Datos en línea tras un JSR?" % (op, key(b, addr), reason)))
                return
            mn, mode = entry
            size = MODE_SIZE[mode]
            if off + size > bank.size:
                self.flags.append(("warn", "Instrucción cruza el final del banco en %s" % key(b, addr)))
                return
            if any(kind[off + i] != UNKNOWN for i in range(1, size)):
                self.flags.append(("conflict", "Instrucción en %s pisa código/datos existentes (desde %s)" % (key(b, addr), reason)))
                return
            operand = None
            if size == 2:
                operand = data[off + 1]
            elif size == 3:
                operand = data[off + 1] | (data[off + 2] << 8)
            kind[off] = CODE
            for i in range(1, size):
                kind[off + i] = OPERAND
            self.insts[b][off] = (mn, mode, operand, size)
            nxt = addr + size

            if mn in BRANCHES:
                rel = operand - 0x100 if operand >= 0x80 else operand
                r = self.resolve(b, (nxt + rel) & 0xFFFF)
                if r:
                    self.add_label(r[0], r[1], "L")
                    self.add_xref(r, (b, addr), "branch")
                    queue.append((r[0], r[1], "branch " + key(b, addr)))
            elif mn == "JSR":
                r = self.resolve(b, operand)
                if r:
                    self.add_label(r[0], r[1], "S")
                    self.add_xref(r, (b, addr), "call")
                    queue.append((r[0], r[1], "call " + key(b, addr)))
                    if r in self.jump_engines:
                        return
                else:
                    self.unresolved_calls.setdefault(operand, []).append(key(b, addr))
            elif mn == "JMP" and mode == "abs":
                r = self.resolve(b, operand)
                if r:
                    self.add_label(r[0], r[1], "L")
                    self.add_xref(r, (b, addr), "jump")
                    queue.append((r[0], r[1], "jump " + key(b, addr)))
                else:
                    self.unresolved_calls.setdefault(operand, []).append(key(b, addr))
                return
            elif mn == "JMP":
                self.indirect_jumps.append((b, addr, operand))
                return
            elif mn in ("RTS", "RTI", "BRK"):
                return
            if mn in ("STA", "STX", "STY") and mode in ("abs", "abx", "aby") and operand >= 0x8000:
                self.mapper_writes.append((b, addr, operand))
            addr = nxt

    def _trace_cdl(self):
        """Use bytes the emulator logged as executed code as extra entry points."""
        changed = True
        while changed:
            changed = False
            for bank in self.banks:
                b = bank.index
                kind = self.kind[b]
                cdl = self.cdl[bank.offset:bank.offset + bank.size]
                for off in range(bank.size):
                    f = cdl[off]
                    if not f & 1 or kind[off] != UNKNOWN:
                        continue
                    run_start = off == 0 or not cdl[off - 1] & 1
                    after_code = off > 0 and kind[off - 1] in (CODE, OPERAND)
                    mesen_entry = self.cdl_mesen and f & 0x0C
                    if run_start or after_code or mesen_entry:
                        before = len(self.insts[b])
                        queue = [(b, bank.base + off, "cdl")]
                        self._trace(queue)
                        if len(self.insts[b]) != before:
                            changed = True
        for bank in self.banks:
            b = bank.index
            cdl = self.cdl[bank.offset:bank.offset + bank.size]
            for off in range(bank.size):
                if cdl[off] & 2 and not cdl[off] & 1 and self.kind[b][off] == UNKNOWN:
                    self.kind[b][off] = DATA

    def _apply_jump_table(self, jt, queue):
        b = int(jt["bank"])
        addr = parse_addr(jt["addr"])
        count = int(jt["count"])
        fmt = jt.get("format", "word")
        tb = int(jt["target_bank"]) if "target_bank" in jt else None
        minus = 1 if fmt.endswith("minus1") else 0
        split = fmt.startswith("split")
        bank = self.banks[b]
        self.add_label(b, addr, "T")
        hi_addr = parse_addr(jt["hi_addr"]) if split else None
        if split:
            self.add_label(b, hi_addr, "T")
        if jt.get("comment"):
            self.region_comments[(b, addr)] = jt["comment"]
        for i in range(count):
            if split:
                lo_a, hi_a = addr + i, hi_addr + i
            else:
                lo_a = addr + 2 * i
                hi_a = lo_a + 1
            if not (bank.contains(lo_a) and bank.contains(hi_a)):
                break
            target = (((self.byte(b, hi_a) << 8) | self.byte(b, lo_a)) + minus) & 0xFFFF
            if tb is not None and self.banks[tb].contains(target):
                r = (tb, target)
            else:
                r = self.resolve(b, target)
            self.kind[b][lo_a - bank.base] = DATA
            self.kind[b][hi_a - bank.base] = DATA
            sfx = "-1" if minus else ""
            if split:
                self.tables[(b, lo_a)] = ("lo" + sfx, r, 1)
                self.tables[(b, hi_a)] = ("hi" + sfx, r, 1)
            else:
                self.tables[(b, lo_a)] = ("word" + sfx, r, 2)
            if r is None:
                continue
            self.add_label(r[0], r[1], "S")
            self.add_xref(r, (b, lo_a), "table")
            queue.append((r[0], r[1], "tabla " + key(b, addr)))

    def _first_insts(self, b, addr, n):
        out = []
        bank = self.banks[b]
        while len(out) < n and bank.contains(addr):
            inst = self.insts[b].get(addr - bank.base)
            if not inst:
                break
            out.append(inst)
            if inst[0] in ("RTS", "RTI", "JMP"):
                break
            addr += inst[3]
        return out

    def _detect_jump_engines(self):
        """Routines that pull their own return address and then JMP (ptr)."""
        found = set()
        for (b, a), k in self.labels.items():
            if k != "S":
                continue
            seq = self._first_insts(b, a, 16)
            if not seq or seq[-1][:2] != ("JMP", "ind"):
                continue
            pulls = [i for i, ins in enumerate(seq) if ins[0] == "PLA"]
            if len(pulls) < 2:
                continue
            p1, p2 = pulls[0], pulls[1]
            if (seq[p1 + 1][:2] == ("STA", "zp") and p2 + 1 < len(seq)
                    and seq[p2 + 1][:2] == ("STA", "zp")):
                found.add((b, a))
        return found

    def _parse_engine_tables(self):
        entries = []
        for target in sorted(self.jump_engines):
            for (sb, sa, typ) in list(self.xrefs.get(target, [])):
                if typ != "call":
                    continue
                bank = self.banks[sb]
                taddr = sa + 3
                if (sb, taddr) in self.tables or not bank.contains(taddr + 1):
                    continue
                if self.kind_at(sb, taddr) != UNKNOWN:
                    continue
                count = 0
                a = taddr
                while count < 128 and bank.contains(a + 1):
                    off = a - bank.base
                    if count > 0 and ((sb, a) in self.labels or self.kind[sb][off] != UNKNOWN):
                        break
                    if self.kind[sb][off + 1] != UNKNOWN:
                        break
                    w = self.data[sb][off] | (self.data[sb][off + 1] << 8)
                    r = self.resolve(sb, w)
                    if r is None or OPCODES.get(self.byte(*r)) is None or self.kind_at(*r) in (OPERAND, DATA):
                        break
                    self.kind[sb][off] = DATA
                    self.kind[sb][off + 1] = DATA
                    self.tables[(sb, a)] = ("word", r, 2)
                    self.add_label(r[0], r[1], "S")
                    self.add_xref(r, (sb, a), "table")
                    entries.append((r[0], r[1], "jump engine " + key(sb, sa)))
                    count += 1
                    a += 2
                if count:
                    self.add_label(sb, taddr, "T")
                    self.flags.append(("info", "Tabla de %d entradas tras JSR %s (jump engine). Verificar el largo." % (count, key(sb, sa))))
        return entries

    def _mark_data_refs(self):
        for bank in self.banks:
            b = bank.index
            for off, (mn, mode, operand, size) in self.insts[b].items():
                if mode not in ("abs", "abx", "aby", "ind") or (mn in ("JSR", "JMP") and mode == "abs"):
                    continue
                if operand < 0x6000:
                    continue
                r = self.resolve(b, operand)
                if r is None:
                    continue
                tk = self.kind_at(*r)
                self.add_xref(r, (b, bank.base + off), "write" if mn in WRITES else "read")
                if tk == CODE:
                    self.add_label(r[0], r[1], "L")
                else:
                    self.add_label(r[0], r[1], "D")
                    if tk == UNKNOWN:
                        self.kind[r[0]][r[1] - self.banks[r[0]].base] = DATA

    def _scan_patterns(self):
        for bank in self.banks:
            b = bank.index
            window = []
            for off in sorted(self.insts[b]):
                mn = self.insts[b][off][0]
                window = (window + [mn])[-8:]
                if mn == "RTS" and window.count("PHA") >= 2:
                    self.rts_dispatch.append((b, bank.base + off))

    def _settle_labels(self):
        """Labels that cannot be emitted in place become equates."""
        for (b, a) in list(self.labels) + list(self.sym_labels):
            if self.kind_at(b, a) == OPERAND:
                self.mid_refs.add((b, a))
        for (b, a), (k, target, size) in self.tables.items():
            if size == 2 and ((b, a + 1) in self.labels or (b, a + 1) in self.sym_labels):
                self.mid_refs.add((b, a + 1))

    # -- naming ---------------------------------------------------------------
    def has_label(self, b, a):
        return (b, a) in self.labels or (b, a) in self.sym_labels

    def label_name(self, b, addr):
        sym = self.sym_labels.get((b, addr))
        if sym:
            return sym[0]
        if (b, addr) in self.vector_names:
            return self.vector_names[(b, addr)]
        k = self.labels.get((b, addr))
        if k is None:
            return "M%02X_%04X" % (b, addr)
        return "%s%02X_%04X" % (k, b, addr)

    def ram_expr(self, addr):
        hit = self.ram_names.get(addr)
        if hit:
            name, off = hit
            return name if off == 0 else "%s+%d" % (name, off)
        if addr in NES_REGISTERS:
            return NES_REGISTERS[addr]
        return None

    def addr_expr(self, b, value, width=2):
        """Expression that assembles to exactly `value`."""
        if value < 0x8000:
            e = self.ram_expr(value)
            if e:
                return e
        if value >= 0x6000:
            r = self.resolve(b, value)
            if r and (self.has_label(*r) or r in self.mid_refs):
                name = self.label_name(*r)
                diff = r[1] - value
                if diff == 0:
                    return name
                return "%s-$%04X" % (name, diff) if diff > 0 else "%s+$%04X" % (name, -diff)
        return ("$%02X" % value) if width == 1 else ("$%04X" % value)

    # -- output ---------------------------------------------------------------
    def format_inst(self, b, addr, inst):
        mn, mode, operand, size = inst
        if mode == "imp":
            return mn
        if mode == "acc":
            return mn + " A"
        if mode == "imm":
            return "%s #$%02X" % (mn, operand)
        if mode == "rel":
            rel = operand - 0x100 if operand >= 0x80 else operand
            return "%s %s" % (mn, self.addr_expr(b, (addr + 2 + rel) & 0xFFFF))
        if mode in ("zp", "zpx", "zpy", "izx", "izy"):
            e = self.ram_expr(operand) or "$%02X" % operand
            return mn + " " + {"zp": "%s", "zpx": "%s,X", "zpy": "%s,Y",
                               "izx": "(%s,X)", "izy": "(%s),Y"}[mode] % e
        e = self.addr_expr(b, operand)
        if operand < 0x100 and mode != "ind":
            e = "a:" + e
        return mn + " " + {"abs": "%s", "abx": "%s,X", "aby": "%s,Y", "ind": "(%s)"}[mode] % e

    def table_expr(self, tkind, target):
        name = self.label_name(*target)
        inner = name + "-1" if tkind.endswith("-1") else name
        if tkind.startswith("word"):
            return inner
        op = "<" if tkind.startswith("lo") else ">"
        return "%s(%s)" % (op, inner) if inner != name else op + name

    def emit_bank(self, b):
        bank = self.banks[b]
        out = ["; ==== Banco %02X  (PRG offset $%05X, CPU $%04X-$%04X%s) ====" % (
            b, bank.offset, bank.base, bank.end - 1, ", fijo" if bank.fixed else ", intercambiable"),
            "; Generado por RetroFusion. A la derecha: dirección y bytes originales."]
        for m in sorted(x for x in self.mid_refs if x[0] == b):
            out.append("%s = $%04X        ; cae dentro de otra instrucción/dato" % (self.label_name(*m), m[1]))
        out.append("")
        kind = self.kind[b]
        data = self.data[b]
        off = 0
        region = None
        while off < bank.size:
            addr = bank.base + off
            if self.has_label(b, addr):
                sym = self.sym_labels.get((b, addr))
                if sym and sym[1]:
                    out.append("")
                    out.extend("; " + cl for cl in str(sym[1]).splitlines())
                elif self.labels.get((b, addr)) in ("S", "V"):
                    out.append("")
                if (b, addr) in self.region_comments:
                    out.append("; " + self.region_comments[(b, addr)])
                out.append(self.label_name(b, addr) + ":")
            comment = self.sym_comments.get((b, addr))
            k = kind[off]
            if k == CODE:
                region = "code"
                inst = self.insts[b][off]
                raw = " ".join("%02X" % x for x in data[off:off + inst[3]])
                line = "%-36s; %04X: %s" % ("    " + self.format_inst(b, addr, inst), addr, raw)
                out.append(line + ("  | " + comment if comment else ""))
                off += inst[3]
                continue
            t = self.tables.get((b, addr))
            if t:
                tkind, target, tsize = t
                if target is not None:
                    expr = self.table_expr(tkind, target)
                elif tsize == 2:
                    expr = "$%04X" % (data[off] | (data[off + 1] << 8))
                else:
                    expr = "$%02X" % data[off]
                line = "%-36s; %04X" % ("    %s %s" % (".word" if tsize == 2 else ".byte", expr), addr)
                out.append(line + ("  | " + comment if comment else ""))
                off += tsize
                continue
            cls = "data" if k == DATA else "unknown"
            if cls != region:
                region = cls
                out.append("; --- %s ---" % ("datos" if cls == "data" else "sin clasificar (¿datos o código no alcanzado?)"))
            j = off + 1
            while j < bank.size and j - off < 16:
                a2 = bank.base + j
                if (kind[j] == CODE or self.has_label(b, a2) or (b, a2) in self.tables
                        or (b, a2) in self.sym_comments or ("data" if kind[j] == DATA else "unknown") != cls):
                    break
                j += 1
            line = "%-36s; %04X" % ("    .byte " + ",".join("$%02X" % x for x in data[off:j]), addr)
            out.append(line + ("  | " + comment if comment else ""))
            off = j
        out.append("")
        return "\n".join(out)

    def header_source(self):
        h = self.rom.header
        return "\n".join([
            "; --- Cabecera iNES (16 bytes) ---",
            '    .byte "NES", $1A',
            "    .byte $%02X                ; PRG: %d x 16 KB" % (h[4], h[4]),
            "    .byte $%02X                ; CHR: %d x 8 KB%s" % (h[5], h[5], " (0 = CHR-RAM)" if h[5] == 0 else ""),
            "    .byte $%02X                ; flags6: mapper bajo=%d, mirroring %s%s%s" % (
                h[6], h[6] >> 4, self.rom.mirroring, ", bateria" if self.rom.battery else "",
                ", trainer" if self.rom.trainer else ""),
            "    .byte $%02X                ; flags7: mapper alto=%d%s" % (h[7], h[7] >> 4, ", NES 2.0" if self.rom.nes2 else ""),
            "    .byte " + ",".join("$%02X" % x for x in h[8:16]),
        ])

    def write(self, out_dir):
        os.makedirs(out_dir, exist_ok=True)
        rom = self.rom
        with open(os.path.join(out_dir, "nes.inc"), "w", encoding="utf-8") as f:
            f.write("; Registros de hardware NES\n")
            for addr, name in sorted(NES_REGISTERS.items()):
                f.write("%-12s = $%04X\n" % (name, addr))
        with open(os.path.join(out_dir, "ram.inc"), "w", encoding="utf-8") as f:
            f.write("; Variables de RAM con nombre (se generan desde symbols/<rom>.json)\n")
            for addr, name, size, comment in self.ram_defs:
                notes = []
                if size > 1:
                    notes.append("%d bytes" % size)
                if comment:
                    notes.append(comment)
                f.write("%-24s = $%04X%s\n" % (name, addr, ("    ; " + ". ".join(notes)) if notes else ""))
        main = ["; RetroFusion - archivo principal de ensamblado de '%s'" % rom.name,
                "; Construir con:  python retro.py build",
                "", '.include "nes.inc"', '.include "ram.inc"', "", self.header_source(), ""]
        if rom.trainer:
            with open(os.path.join(out_dir, "trainer.bin"), "wb") as f:
                f.write(rom.trainer_data)
            main.append('.incbin "trainer.bin"')
        for bank in self.banks:
            fname = "bank_%02X.asm" % bank.index
            with open(os.path.join(out_dir, fname), "w", encoding="utf-8") as f:
                f.write(self.emit_bank(bank.index))
            main.append(".bank $%02X, $%04X, $%04X" % (bank.index, bank.base, bank.size))
            main.append('.include "%s"' % fname)
            main.append(".endbank")
        if rom.chr:
            with open(os.path.join(out_dir, "chr.bin"), "wb") as f:
                f.write(rom.chr)
            main.append("")
            main.append('.incbin "chr.bin"            ; gráficos (CHR-ROM)')
        if rom.extra:
            with open(os.path.join(out_dir, "extra.bin"), "wb") as f:
                f.write(rom.extra)
            main.append('.incbin "extra.bin"')
        with open(os.path.join(out_dir, "main.asm"), "w", encoding="utf-8") as f:
            f.write("\n".join(main) + "\n")

    # -- index / report -------------------------------------------------------
    def routines(self):
        result = []
        entries = {(b, a) for (b, a), k in self.labels.items() if k in ("S", "V")}
        entries |= {e for e in self.sym_labels if self.banks[e[0]].contains(e[1]) and self.kind_at(*e) == CODE
                    and self.labels.get(e) not in ("L",)}
        by_bank = {}
        for (b, a) in entries:
            by_bank.setdefault(b, []).append(a)
        for v in by_bank.values():
            v.sort()
        for (b, a) in sorted(entries):
            bank = self.banks[b]
            if self.kind_at(b, a) != CODE:
                continue
            seen = set()
            todo = [a]
            calls, ram_r, ram_w, io = set(), set(), set(), set()
            while todo:
                x = todo.pop()
                if x in seen or not bank.contains(x) or (x != a and (b, x) in entries):
                    continue
                inst = self.insts[b].get(x - bank.base)
                if inst is None:
                    continue
                seen.add(x)
                mn, mode, operand, size = inst
                if mode in ("zp", "zpx", "zpy", "abs", "abx", "aby", "izx", "izy") and mn not in ("JSR", "JMP"):
                    if operand < 0x0800 or 0x6000 <= operand < 0x8000:
                        (ram_w if mn in WRITES else ram_r).add(operand)
                        if mn in RMW:
                            ram_r.add(operand)
                    elif 0x2000 <= operand < 0x4020:
                        io.add(NES_REGISTERS.get(operand, "$%04X" % operand))
                if mn in BRANCHES:
                    rel = operand - 0x100 if operand >= 0x80 else operand
                    todo.append(x + 2 + rel)
                if mn == "JSR":
                    r = self.resolve(b, operand)
                    calls.add(self.label_name(*r) if r else "$%04X" % operand)
                    if r in self.jump_engines:
                        continue
                if mn == "JMP" and mode == "abs":
                    r = self.resolve(b, operand)
                    if r and r[0] == b and r not in entries:
                        todo.append(r[1])
                    elif r:
                        calls.add(self.label_name(*r) + " (salto)")
                    continue
                if mn in ("RTS", "RTI", "JMP", "BRK"):
                    continue
                todo.append(x + size)
            callers = set()
            for (sb, sa, t) in self.xrefs.get((b, a), []):
                if t != "call":
                    continue
                owners = [e for e in by_bank.get(sb, []) if e <= sa]
                callers.add(self.label_name(sb, owners[-1]) if owners else key(sb, sa))
            fmt_ram = lambda s: sorted((self.ram_expr(v) or "$%04X" % v) for v in s)
            result.append({
                "name": self.label_name(b, a), "bank": b, "addr": "%04X" % a, "key": key(b, a),
                "size": sum(self.insts[b][x - bank.base][3] for x in seen),
                "callers": sorted(callers), "calls": sorted(calls),
                "ram_reads": fmt_ram(ram_r), "ram_writes": fmt_ram(ram_w), "io": sorted(io),
                "named": (b, a) in self.sym_labels,
            })
        return result

    def coverage(self):
        out = []
        for bank in self.banks:
            k = self.kind[bank.index]
            code = k.count(CODE) + k.count(OPERAND)
            data = k.count(DATA)
            out.append({"bank": bank.index, "base": "%04X" % bank.base, "size": bank.size,
                        "fixed": bank.fixed, "code": code, "data": data,
                        "unknown": bank.size - code - data})
        return out

    def unknown_regions(self, min_len=16):
        regions = []
        for bank in self.banks:
            k = self.kind[bank.index]
            off = 0
            while off < bank.size:
                if k[off] == UNKNOWN:
                    start = off
                    while off < bank.size and k[off] == UNKNOWN:
                        off += 1
                    if off - start >= min_len:
                        regions.append((bank.index, bank.base + start, off - start))
                else:
                    off += 1
        return regions

    def code_candidates(self):
        """Unknown regions whose start decodes as a plausible routine."""
        cands = []
        for b, start, length in self.unknown_regions(16):
            a, n = start, 0
            while a < start + length and n < 200:
                op = self.byte(b, a)
                entry = OPCODES.get(op)
                if entry is None or op == 0x00:
                    break
                n += 1
                a += MODE_SIZE[entry[1]]
                if entry[0] in ("RTS", "RTI", "JMP") and n >= 6:
                    cands.append((b, start, n))
                    break
        return cands

    def index(self):
        xrefs = {}
        for target, refs in self.xrefs.items():
            xrefs[self.label_name(*target)] = sorted({"%s %s" % (key(sb, sa), t) for sb, sa, t in refs})
        return {
            "rom": self.rom.info(),
            "banks": [b.to_dict() for b in self.banks],
            "cdl": self.cdl is not None,
            "coverage": self.coverage(),
            "routines": self.routines(),
            "xrefs": xrefs,
            "jump_engines": [key(*j) for j in sorted(self.jump_engines)],
            "indirect_jumps": [{"at": key(b, a), "pointer": "$%04X" % p} for b, a, p in self.indirect_jumps],
            "rts_dispatch": [key(b, a) for b, a in self.rts_dispatch],
            "unresolved_targets": {"$%04X" % t: s[:8] for t, s in sorted(self.unresolved_calls.items())},
            "mapper_writes": [{"at": key(b, a), "reg": "$%04X" % r} for b, a, r in self.mapper_writes][:200],
            "unknown_regions": [{"bank": b, "addr": "%04X" % a, "len": n} for b, a, n in self.unknown_regions()],
            "code_candidates": [{"bank": b, "addr": "%04X" % a, "instructions": n} for b, a, n in self.code_candidates()],
            "flags": [{"type": t, "msg": m} for t, m in self.flags],
        }


def report(idx):
    info = idx["rom"]
    L = ["# Reporte de desensamblado: %s" % info["name"], ""]
    L.append("- Formato: %s, mapper %d (%s), PRG %d KB, CHR %d KB%s" % (
        info["format"], info["mapper"], info["mapper_name"], info["prg_kb"], info["chr_kb"],
        " (CHR-RAM: los gráficos están dentro del PRG)" if info["chr_ram"] else ""))
    L.append("- Compatibilidad fuera de PC (FPGA/flashcart): nivel %s" % info["hardware_tier"])
    L.append("- CDL usado: %s" % ("sí" if idx["cdl"] else "no (recomendado: grabar uno jugando, ver playbook/02_mapear.md)"))
    L += ["", "## Cobertura por banco", "", "| Banco | CPU | Código | Datos | Sin clasificar |", "|---|---|---|---|---|"]
    for c in idx["coverage"]:
        L.append("| %02X%s | $%s | %d%% | %d%% | %d%% |" % (
            c["bank"], " (fijo)" if c["fixed"] else "", c["base"],
            100 * c["code"] // c["size"], 100 * c["data"] // c["size"], 100 * c["unknown"] // c["size"]))
    named = sum(1 for r in idx["routines"] if r["named"])
    L += ["", "Rutinas encontradas: %d (con nombre: %d)" % (len(idx["routines"]), named), "",
          "## Pendientes para la IA (en orden de prioridad)", ""]
    n = 0
    errors = [f for f in idx["flags"] if f["type"] == "error"]
    if errors:
        n += 1
        L.append("%d. **Errores en symbols/hints** (arreglar primero):" % n)
        L += ["   - " + f["msg"] for f in errors[:20]]
    if idx["indirect_jumps"]:
        n += 1
        L.append("%d. **Saltos indirectos** (`JMP ($xxxx)`): averiguar de dónde sale el puntero y declarar la tabla en hints `jump_tables`:" % n)
        L += ["   - en %s, puntero %s" % (j["at"], j["pointer"]) for j in idx["indirect_jumps"][:20]]
    if idx["rts_dispatch"]:
        n += 1
        L.append("%d. **Posibles despachos por RTS** (PHA, PHA, RTS = tabla de direcciones menos 1). Declarar con formato `word_minus1` o `split_minus1`:" % n)
        L += ["   - RTS en %s" % j for j in idx["rts_dispatch"][:20]]
    if idx["unresolved_targets"]:
        n += 1
        L.append("%d. **Llamadas a la ventana intercambiable** (no se sabe qué banco está mapeado). Encontrar la rutina de cambio de banco y declarar `entry_points` con el banco correcto:" % n)
        L += ["   - %s desde %s" % (t, ", ".join(s[:4])) for t, s in list(idx["unresolved_targets"].items())[:20]]
    if idx["code_candidates"]:
        n += 1
        L.append("%d. **Regiones sin clasificar que parecen código**: confirmar (CDL, `python retro.py show`) y agregar `entry_points`:" % n)
        L += ["   - banco %02X $%s (%d instrucciones válidas seguidas)" % (c["bank"], c["addr"], c["instructions"])
              for c in idx["code_candidates"][:20]]
    conflicts = [f for f in idx["flags"] if f["type"] in ("conflict", "invalid")]
    if conflicts:
        n += 1
        L.append("%d. **Conflictos** (código que pisa código, o datos en un camino de código). Suele ser un JSR seguido de datos en línea:" % n)
        L += ["   - " + f["msg"] for f in conflicts[:20]]
    if idx["jump_engines"]:
        n += 1
        L.append("%d. **Jump engines detectados** (verificar el largo de cada tabla): %s" % (n, ", ".join(idx["jump_engines"])))
    if not n:
        L.append("Nada pendiente en el mapeo automático.")
    L.append("")
    if idx["mapper_writes"]:
        L += ["## Escrituras al mapper (cambio de banco)", ""]
        L += ["- %s escribe en %s" % (w["at"], w["reg"]) for w in idx["mapper_writes"][:15]]
        L.append("")
    L += ["## Rutinas más llamadas", ""]
    for r in sorted(idx["routines"], key=lambda r: -len(r["callers"]))[:25]:
        L.append("- `%s` (%s, %d bytes, %d llamadores) lee: %s | escribe: %s%s" % (
            r["name"], r["key"], r["size"], len(r["callers"]),
            ", ".join(r["ram_reads"][:6]) or "-", ", ".join(r["ram_writes"][:6]) or "-",
            (" | E/S: " + ", ".join(r["io"])) if r["io"] else ""))
    L.append("")
    return "\n".join(L)
