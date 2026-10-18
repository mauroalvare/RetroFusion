"""Differential verification of translated routines.

The original routine runs on the 6502 interpreter and the translation runs as
Python, both from the same random (or recorded) RAM state. Their RAM, chosen
registers and I/O writes must match in every case. This is what keeps an AI
honest: a translation counts as done only when it passes.

Translation contract (Python):

    def routine_name(s):
        s.ram[addr]            # 2 KB work RAM (mirrored, values wrap to 0-255)
        s.sram[addr - 0x6000]  # 8 KB cartridge RAM
        s.a, s.x, s.y          # registers at entry; set them if callers use them
        s.carry, s.zero, s.negative, s.overflow   # flags at entry (bool)
        s.rom(addr)            # read a ROM byte as the CPU sees it (tables)
        s.read_io(addr)        # read a hardware register ($2002, $4016...)
        s.write_io(addr, v)    # write a hardware register (PPU/APU)
"""

import importlib.util
import os
import random
import sys

from .cpus.mos6502 import CPU, StepLimit, C, Z, V, N, I, U
from .platforms.nes import NES_REGISTERS

SENTINEL = 0x4FF0          # open-bus address: reaching it means the routine returned
IO_READ_DEFAULTS = {0x2002: 0x80}


class Unmapped(Exception):
    pass


class Machine:
    def __init__(self, rom, bank_index, maps=()):
        self.rom = rom
        self.banks = rom.layout()
        self.view = bytearray(0x8000)
        self.mapped = bytearray(0x8000)
        chosen = [b for b in self.banks if b.fixed]
        chosen.append(self.banks[bank_index])
        for (bi, base) in maps:
            b = self.banks[bi]
            chosen.append(type(b)(b.index, b.offset, b.size, base, False))
        for b in chosen:
            data = rom.prg[b.offset:b.offset + b.size]
            start = b.base - 0x8000
            self.view[start:start + b.size] = data
            self.mapped[start:start + b.size] = b"\x01" * b.size
            if b.size == 16384 and len(self.banks) == 1:     # NROM-128 mirror
                other = 0x4000 if start == 0 else 0
                self.view[other:other + b.size] = data
                self.mapped[other:other + b.size] = b"\x01" * b.size

    def rom_byte(self, addr):
        if addr < 0x8000 or not self.mapped[addr - 0x8000]:
            raise Unmapped("$%04X no está mapeada (usá --map banco:base)" % addr)
        return self.view[addr - 0x8000]


class IoSource:
    """Deterministic values for hardware register reads, per test case."""

    def __init__(self, seed):
        self.rng = random.Random(seed ^ 0x5A5A)
        self.reads = []

    def read(self, addr):
        if addr in IO_READ_DEFAULTS:
            v = IO_READ_DEFAULTS[addr]
        elif addr in (0x4016, 0x4017):
            v = 0x40 | self.rng.randint(0, 1)
        else:
            v = self.rng.randint(0, 255)
        self.reads.append((addr, v))
        return v


class Bus:
    def __init__(self, machine, ram, sram, io):
        self.m = machine
        self.ram = ram
        self.sram = sram
        self.io = io
        self.io_writes = []
        self.mapper_writes = []
        self.read_first = set()
        self.written = set()

    def read(self, addr):
        if addr < 0x2000:
            a = addr & 0x7FF
            if a not in self.written:
                self.read_first.add(a)
            return self.ram[a]
        if addr < 0x4020:
            reg = 0x2000 | (addr & 7) if addr < 0x4000 else addr
            return self.io.read(reg)
        if 0x6000 <= addr < 0x8000:
            a = addr
            if a not in self.written:
                self.read_first.add(a)
            return self.sram[addr - 0x6000]
        if addr >= 0x8000:
            return self.m.rom_byte(addr)
        return 0

    def write(self, addr, v):
        if addr < 0x2000:
            a = addr & 0x7FF
            self.written.add(a)
            self.ram[a] = v
        elif addr < 0x4020:
            reg = 0x2000 | (addr & 7) if addr < 0x4000 else addr
            self.io_writes.append((reg, v))
        elif 0x6000 <= addr < 0x8000:
            self.written.add(addr)
            self.sram[addr - 0x6000] = v
        elif addr >= 0x8000:
            self.mapper_writes.append((addr, v))


class RamView:
    def __init__(self, buf, mask):
        self._buf = buf
        self._mask = mask

    def __getitem__(self, i):
        return self._buf[i & self._mask]

    def __setitem__(self, i, v):
        self._buf[i & self._mask] = v & 0xFF

    def __len__(self):
        return len(self._buf)

    def word(self, i):
        return self[i] | (self[i + 1] << 8)


class State:
    def __init__(self, machine, ram, sram, a, x, y, p, io):
        self.ram = RamView(ram, 0x7FF)
        self.sram = RamView(sram, 0x1FFF)
        self.a, self.x, self.y = a, x, y
        self.carry = bool(p & C)
        self.zero = bool(p & Z)
        self.negative = bool(p & N)
        self.overflow = bool(p & V)
        self._machine = machine
        self._io = io
        self.io_writes = []

    def rom(self, addr):
        return self._machine.rom_byte(addr)

    def read_io(self, addr):
        return self._io.read(addr)

    def write_io(self, addr, v):
        self.io_writes.append((addr, v & 0xFF))


def load_impl(spec):
    """'path/to/file.py:function' -> callable."""
    if ":" not in spec[2:]:
        raise ValueError("Formato esperado: archivo.py:funcion")
    path, func = spec.rsplit(":", 1)
    path = os.path.abspath(path)
    if not os.path.exists(path):
        raise FileNotFoundError(path)
    folder = os.path.dirname(path)
    if folder not in sys.path:
        sys.path.insert(0, folder)
    name = "rf_impl_%d" % abs(hash(path))
    spec_obj = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec_obj)
    spec_obj.loader.exec_module(mod)
    if not hasattr(mod, func):
        raise AttributeError("%s no define la función %s" % (path, func))
    return getattr(mod, func)


def run_original(machine, entry, ram, sram, a, x, y, p, io, max_steps):
    bus = Bus(machine, ram, sram, io)
    cpu = CPU(bus.read, bus.write)
    cpu.a, cpu.x, cpu.y, cpu.p = a, x, y, (p | U)
    cpu.sp = 0xFD
    ret = SENTINEL - 1
    cpu.push(ret >> 8)
    cpu.push(ret & 0xFF)
    bus.written.clear()
    entry_sp = cpu.sp
    cpu.pc = entry
    min_sp = cpu.sp
    while cpu.pc != SENTINEL:
        if cpu.steps >= max_steps:
            raise StepLimit()
        cpu.step()
        if cpu.sp < min_sp:
            min_sp = cpu.sp
    return cpu, bus, entry_sp, min_sp


def make_states(rng, count, inputs, snapshots):
    special = [0x00, 0x01, 0x02, 0x7F, 0x80, 0x81, 0xFE, 0xFF]
    for i in range(count):
        if snapshots and i % 2 == 0:
            ram = bytearray(snapshots[(i // 2) % len(snapshots)])
            for a in inputs:
                if a < 0x800 and rng.random() < 0.15:
                    ram[a] = rng.randint(0, 255)
        else:
            style = i % 4
            if style == 0 or not inputs:
                ram = bytearray(rng.randint(0, 255) for _ in range(0x800))
            elif style == 1:
                ram = bytearray(0x800)
                for a in inputs:
                    if a < 0x800:
                        ram[a] = rng.randint(0, 255)
            elif style == 2:
                ram = bytearray(rng.randint(0, 255) for _ in range(0x800))
                for a in inputs:
                    if a < 0x800:
                        ram[a] = rng.choice(special)
            else:
                ram = bytearray(0x800)
                for a in inputs:
                    if a < 0x800:
                        ram[a] = rng.choice(special + [rng.randint(0, 255)])
        sram = bytearray(rng.randint(0, 255) for _ in range(0x2000)) if i % 3 == 0 else bytearray(0x2000)
        a, x, y = rng.randint(0, 255), rng.randint(0, 255), rng.randint(0, 255)
        if i % 4 == 1:
            x, y = rng.randint(0, 15), rng.randint(0, 15)
        p = (rng.randint(0, 255) & (C | Z | V | N)) | I | U
        yield ram, sram, a, x, y, p


def load_snapshots(folder):
    snaps = []
    if not folder:
        return snaps
    for name in sorted(os.listdir(folder)):
        path = os.path.join(folder, name)
        if os.path.isfile(path):
            with open(path, "rb") as f:
                data = f.read()
            if len(data) >= 0x800:
                snaps.append(data[:0x800])
    return snaps


def addr_name(addr, symbols):
    ram = (symbols or {}).get("ram", {})
    for k, v in ram.items():
        base = int(k, 16)
        size = int(v.get("size", 1)) if isinstance(v, dict) else 1
        name = v["name"] if isinstance(v, dict) else v
        if base <= addr < base + size:
            return name if addr == base else "%s+%d" % (name, addr - base)
    if addr in NES_REGISTERS:
        return NES_REGISTERS[addr]
    return "$%04X" % addr


def profile(rom, bank, entry, cases=200, seed=1, maps=(), states_dir=None, max_steps=200000):
    """Which RAM a routine reads first (inputs) and writes (outputs)."""
    machine = Machine(rom, bank, maps)
    rng = random.Random(seed)
    snaps = load_snapshots(states_dir)
    inputs, outputs, io_r, io_w, mapper = set(), set(), set(), set(), set()
    steps, timeouts, errors = [], 0, []
    regs_changed = {"A": 0, "X": 0, "Y": 0, "C": 0}
    for i, (ram, sram, a, x, y, p) in enumerate(make_states(rng, cases, [], snaps)):
        io = IoSource(seed * 1000 + i)
        try:
            cpu, bus, _, _ = run_original(machine, entry, ram, sram, a, x, y, p, io, max_steps)
        except StepLimit:
            timeouts += 1
            continue
        except (Unmapped, RuntimeError) as e:
            errors.append(str(e))
            continue
        inputs |= bus.read_first
        outputs |= bus.written
        io_r |= {r for r, _ in io.reads}
        io_w |= {r for r, _ in bus.io_writes}
        mapper |= {r for r, _ in bus.mapper_writes}
        steps.append(cpu.steps)
        regs_changed["A"] += cpu.a != a
        regs_changed["X"] += cpu.x != x
        regs_changed["Y"] += cpu.y != y
        regs_changed["C"] += bool(cpu.p & C) != bool(p & C)
    done = max(1, len(steps))
    return {
        "inputs": sorted(inputs), "outputs": sorted(outputs),
        "io_reads": sorted(io_r), "io_writes": sorted(io_w), "mapper_writes": sorted(mapper),
        "cases": cases, "completed": len(steps), "timeouts": timeouts, "errors": errors[:3],
        "avg_steps": sum(steps) // done if steps else 0, "max_steps": max(steps) if steps else 0,
        "regs_changed_pct": {k: 100 * v // done for k, v in regs_changed.items()},
    }


def verify_routine(rom, bank, entry, impl_spec, cases=300, seed=1, maps=(), states_dir=None,
                   regs="", compare_io=False, symbols=None, max_steps=200000):
    machine = Machine(rom, bank, maps)
    func = load_impl(impl_spec)
    prof = profile(rom, bank, entry, cases=min(60, cases), seed=seed + 99, maps=maps,
                   states_dir=states_dir, max_steps=max_steps)
    inputs = prof["inputs"]
    rng = random.Random(seed)
    snaps = load_snapshots(states_dir)
    regs = regs.upper()
    ran = timeouts = 0
    mismatches = []
    errors = []
    for i, (ram, sram, a, x, y, p) in enumerate(make_states(rng, cases, inputs, snaps)):
        init_ram = bytes(ram)
        ram_t, sram_t = bytearray(ram), bytearray(sram)
        try:
            cpu, bus, entry_sp, min_sp = run_original(machine, entry, ram, sram, a, x, y, p,
                                                      IoSource(seed * 7919 + i), max_steps)
        except StepLimit:
            timeouts += 1
            continue
        except (Unmapped, RuntimeError) as e:
            errors.append("original: " + str(e))
            continue
        io_t = IoSource(seed * 7919 + i)
        st = State(machine, ram_t, sram_t, a, x, y, p, io_t)
        try:
            func(st)
        except Exception as e:  # the translation crashed: report as mismatch
            mismatches.append({"case": i, "error": "%s: %s" % (type(e).__name__, e),
                               "inputs": {addr_name(k, symbols): "$%02X" % init_ram[k] for k in inputs if k < 0x800},
                               "regs_in": "A=$%02X X=$%02X Y=$%02X C=%d" % (a, x, y, p & C)})
            ran += 1
            if len(mismatches) >= 5:
                break
            continue
        ran += 1
        diffs = []
        stack_lo = 0x100 + min_sp + 1
        for addr in range(0x800):
            if stack_lo <= addr <= 0x1FF:
                continue
            if ram[addr] != ram_t[addr]:
                diffs.append("%s: original=$%02X traducido=$%02X" % (addr_name(addr, symbols), ram[addr], ram_t[addr]))
        if sram != sram_t:
            for off in range(0x2000):
                if sram[off] != sram_t[off]:
                    diffs.append("SRAM $%04X: original=$%02X traducido=$%02X" % (0x6000 + off, sram[off], sram_t[off]))
                    if len(diffs) > 12:
                        break
        for r in regs:
            if r == "A" and (cpu.a != st.a & 0xFF):
                diffs.append("A: original=$%02X traducido=$%02X" % (cpu.a, st.a & 0xFF))
            if r == "X" and (cpu.x != st.x & 0xFF):
                diffs.append("X: original=$%02X traducido=$%02X" % (cpu.x, st.x & 0xFF))
            if r == "Y" and (cpu.y != st.y & 0xFF):
                diffs.append("Y: original=$%02X traducido=$%02X" % (cpu.y, st.y & 0xFF))
            if r == "C" and (bool(cpu.p & C) != bool(st.carry)):
                diffs.append("C: original=%d traducido=%d" % (bool(cpu.p & C), bool(st.carry)))
            if r == "Z" and (bool(cpu.p & Z) != bool(st.zero)):
                diffs.append("Z: original=%d traducido=%d" % (bool(cpu.p & Z), bool(st.zero)))
        if compare_io and bus.io_writes != st.io_writes:
            diffs.append("escrituras de E/S distintas: original=%s traducido=%s" % (
                ["%s=$%02X" % (NES_REGISTERS.get(r, "$%04X" % r), v) for r, v in bus.io_writes[:8]],
                ["%s=$%02X" % (NES_REGISTERS.get(r, "$%04X" % r), v) for r, v in st.io_writes[:8]]))
        if diffs:
            mismatches.append({
                "case": i, "diffs": diffs[:12],
                "inputs": {addr_name(k, symbols): "$%02X" % init_ram[k] for k in inputs if k < 0x800},
                "regs_in": "A=$%02X X=$%02X Y=$%02X C=%d" % (a, x, y, p & C),
            })
            if len(mismatches) >= 5:
                break
    ok = not mismatches and not errors and ran >= max(1, cases // 2)
    if mismatches:
        summary = "FALLA: %d caso(s) distintos de %d probados." % (len(mismatches), ran)
    elif errors:
        summary = "ERROR al correr el original: %s" % errors[0]
    elif ran < max(1, cases // 2):
        summary = ("NO CONCLUYENTE: solo %d de %d casos terminaron (%d sin terminar). "
                   "¿La rutina espera un NMI o lee hardware en un loop? Probá con --states (RAM real)." % (ran, cases, timeouts))
    else:
        summary = "OK: %d casos idénticos (%d descartados por no terminar)." % (ran, timeouts)
    return {"ok": ok, "summary": summary, "ran": ran, "timeouts": timeouts,
            "mismatches": mismatches, "errors": errors[:3],
            "inputs": [addr_name(k, symbols) for k in inputs],
            "outputs": [addr_name(k, symbols) for k in prof["outputs"]]}
