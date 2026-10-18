"""iNES / NES 2.0 parsing, PRG bank layout per mapper and hardware notes."""

import hashlib

MAPPER_NAMES = {
    0: "NROM", 1: "MMC1 (SxROM)", 2: "UxROM", 3: "CNROM", 4: "MMC3 (TxROM)",
    5: "MMC5 (ExROM)", 7: "AxROM", 9: "MMC2 (PxROM)", 10: "MMC4 (FxROM)",
    11: "Color Dreams", 19: "Namco 163", 21: "VRC4", 22: "VRC2", 23: "VRC2/4",
    24: "VRC6a", 25: "VRC4", 26: "VRC6b", 34: "BNROM/NINA-001", 66: "GxROM",
    69: "Sunsoft FME-7", 71: "Camerica", 118: "TxSROM", 119: "TQROM",
    206: "Namco 108",
}

# Mappers whose PRG is switched in 8 KB units.
MAPPERS_8K = {4, 5, 19, 21, 22, 23, 24, 25, 26, 64, 69, 118, 119, 206, 210}
# Mappers that switch the whole 32 KB at once (no fixed bank).
MAPPERS_32K = {7, 11, 34, 66}
# Mappers with no PRG banking at all (16 or 32 KB visible).
MAPPERS_FLAT = {0, 3, 13, 87, 185}

# Support outside PC emulators (FPGA cores, flashcarts, real carts).
HW_TIER_A = {0, 1, 2, 3, 4, 7}
HW_TIER_B = {5, 9, 10, 11, 19, 21, 22, 23, 24, 25, 26, 34, 66, 69, 71, 206}

NES_REGISTERS = {
    0x2000: "PPUCTRL", 0x2001: "PPUMASK", 0x2002: "PPUSTATUS", 0x2003: "OAMADDR",
    0x2004: "OAMDATA", 0x2005: "PPUSCROLL", 0x2006: "PPUADDR", 0x2007: "PPUDATA",
    0x4000: "SQ1_VOL", 0x4001: "SQ1_SWEEP", 0x4002: "SQ1_LO", 0x4003: "SQ1_HI",
    0x4004: "SQ2_VOL", 0x4005: "SQ2_SWEEP", 0x4006: "SQ2_LO", 0x4007: "SQ2_HI",
    0x4008: "TRI_LINEAR", 0x400A: "TRI_LO", 0x400B: "TRI_HI",
    0x400C: "NOISE_VOL", 0x400E: "NOISE_LO", 0x400F: "NOISE_HI",
    0x4010: "DMC_FREQ", 0x4011: "DMC_RAW", 0x4012: "DMC_START", 0x4013: "DMC_LEN",
    0x4014: "OAMDMA", 0x4015: "SND_CHN", 0x4016: "JOY1", 0x4017: "JOY2",
}


class RomError(Exception):
    pass


class Bank:
    def __init__(self, index, offset, size, base, fixed):
        self.index = index      # bank number inside PRG
        self.offset = offset    # byte offset inside PRG data
        self.size = size
        self.base = base        # CPU address where the bank is seen
        self.fixed = fixed      # always mapped (vectors live here)

    @property
    def end(self):
        return self.base + self.size

    def contains(self, addr):
        return self.base <= addr < self.end

    def to_dict(self):
        return {"index": self.index, "offset": self.offset, "size": self.size,
                "base": "%04X" % self.base, "fixed": self.fixed}


class NesRom:
    def __init__(self, data, name="rom"):
        if len(data) < 16 or data[:4] != b"NES\x1a":
            raise RomError("No es una ROM iNES (.nes): falta la cabecera 'NES\\x1A'.")
        self.name = name
        self.raw = bytes(data)
        self.header = bytes(data[:16])
        h = self.header
        self.nes2 = (h[7] & 0x0C) == 0x08
        prg_units, chr_units = h[4], h[5]
        mapper = (h[6] >> 4) | (h[7] & 0xF0)
        self.submapper = 0
        if self.nes2:
            if (h[9] & 0x0F) == 0x0F or (h[9] >> 4) == 0x0F:
                raise RomError("NES 2.0 con tamaño exponencial: no soportado.")
            prg_units |= (h[9] & 0x0F) << 8
            chr_units |= (h[9] >> 4) << 8
            mapper |= (h[8] & 0x0F) << 8
            self.submapper = h[8] >> 4
        elif any(h[12:16]):
            # Old dumps with garbage ("DiskDude!") in bytes 7-15.
            mapper = h[6] >> 4
        self.mapper = mapper
        self.mirroring = "four-screen" if h[6] & 0x08 else ("vertical" if h[6] & 1 else "horizontal")
        self.battery = bool(h[6] & 0x02)
        self.trainer = bool(h[6] & 0x04)
        pos = 16
        self.trainer_data = b""
        if self.trainer:
            self.trainer_data = bytes(data[pos:pos + 512])
            pos += 512
        prg_len = prg_units * 16384
        chr_len = chr_units * 8192
        if prg_len == 0:
            raise RomError("La ROM declara 0 bytes de PRG.")
        self.prg = bytes(data[pos:pos + prg_len])
        if len(self.prg) != prg_len:
            raise RomError("ROM truncada: faltan bytes de PRG.")
        pos += prg_len
        self.chr = bytes(data[pos:pos + chr_len])
        if len(self.chr) != chr_len:
            raise RomError("ROM truncada: faltan bytes de CHR.")
        pos += chr_len
        self.extra = bytes(data[pos:])
        self.chr_ram = chr_len == 0

    @property
    def mapper_name(self):
        return MAPPER_NAMES.get(self.mapper, "mapper %d" % self.mapper)

    def sha1(self):
        return hashlib.sha1(self.prg + self.chr).hexdigest()

    def hardware_tier(self):
        if self.mapper in HW_TIER_A:
            return "A"
        if self.mapper in HW_TIER_B:
            return "B"
        return "C"

    def info(self):
        return {
            "name": self.name, "format": "NES 2.0" if self.nes2 else "iNES",
            "mapper": self.mapper, "mapper_name": self.mapper_name,
            "submapper": self.submapper, "prg_kb": len(self.prg) // 1024,
            "chr_kb": len(self.chr) // 1024, "chr_ram": self.chr_ram,
            "mirroring": self.mirroring, "battery": self.battery,
            "trainer": self.trainer, "sha1_prg_chr": self.sha1(),
            "hardware_tier": self.hardware_tier(),
        }

    # -- layout --------------------------------------------------------------
    def layout(self, overrides=None):
        """Return the list of PRG banks with the CPU address each one runs at.

        `overrides` comes from hints: {"bank_size": 16384, "banks": {"3": {"base": "A000"}}}
        """
        overrides = overrides or {}
        prg_len = len(self.prg)
        m = self.mapper
        if "bank_size" in overrides:
            size = int(overrides["bank_size"])
        elif m in MAPPERS_FLAT or prg_len <= 32768 and m not in MAPPERS_8K:
            size = min(prg_len, 32768)
        elif m in MAPPERS_32K:
            size = 32768
        elif m in MAPPERS_8K:
            size = 8192
        else:
            size = 16384
        count = prg_len // size
        banks = []
        for i in range(count):
            off = i * size
            if size == 32768 or (count == 1 and size == 16384):
                if size == 16384:
                    # NROM-128: same 16 KB seen at $8000 and $C000. Pick the base
                    # the reset vector points into.
                    reset = self.prg[0x3FFC] | (self.prg[0x3FFD] << 8)
                    base = 0xC000 if reset >= 0xC000 else 0x8000
                else:
                    base = 0x8000
                banks.append(Bank(i, off, size, base, fixed=True))
            elif size == 16384:
                last = i == count - 1
                banks.append(Bank(i, off, size, 0xC000 if last else 0x8000, fixed=last))
            else:  # 8 KB
                if i == count - 1:
                    banks.append(Bank(i, off, size, 0xE000, fixed=True))
                elif i == count - 2 and m in (4, 118, 119, 206):
                    banks.append(Bank(i, off, size, 0xC000, fixed=True))
                else:
                    base = self._guess_base(off, size, (0x8000, 0xA000, 0xC000))
                    banks.append(Bank(i, off, size, base, fixed=False))
        for key, ov in overrides.get("banks", {}).items():
            idx = int(key)
            if 0 <= idx < len(banks):
                if "base" in ov:
                    banks[idx].base = int(str(ov["base"]), 16)
                if "fixed" in ov:
                    banks[idx].fixed = bool(ov["fixed"])
        return banks

    def _guess_base(self, off, size, candidates):
        """Pick the base address that makes most JSR/JMP targets land inside the bank."""
        data = self.prg[off:off + size]
        targets = []
        for i in range(len(data) - 2):
            if data[i] in (0x20, 0x4C):
                targets.append(data[i + 1] | (data[i + 2] << 8))
        best, best_score = candidates[0], -1
        for base in candidates:
            score = sum(1 for t in targets if base <= t < base + size)
            if score > best_score:
                best, best_score = base, score
        return best

    def vectors(self, banks):
        """NMI/RESET/IRQ vectors read from the bank mapped at $FFFA."""
        top = [b for b in banks if b.contains(0xFFFA) and b.fixed]
        if not top:
            top = [b for b in banks if b.contains(0xFFFA)]
        out = []
        for b in top:
            rel = 0xFFFA - b.base + b.offset
            nmi = self.prg[rel] | (self.prg[rel + 1] << 8)
            reset = self.prg[rel + 2] | (self.prg[rel + 3] << 8)
            irq = self.prg[rel + 4] | (self.prg[rel + 5] << 8)
            out.append((b.index, {"NMI": nmi, "RESET": reset, "IRQ": irq}))
        return out


def hardware_report(rom):
    tier = rom.hardware_tier()
    lines = ["Mapper %d (%s) - nivel de compatibilidad fuera de PC: %s" % (rom.mapper, rom.mapper_name, tier)]
    if tier == "A":
        lines.append("OK: mapper muy común. Lo soportan los cores NES de FPGA (MiSTer, Multicore), flashcarts y emuladores.")
    elif tier == "B":
        lines.append("ATENCION: mapper conocido pero no universal. Revisá la lista de mappers del core que vas a usar.")
    else:
        lines.append("RIESGO: mapper poco común. Probablemente solo funcione en emuladores de PC.")
    lines.append("Nota: el core NES del Multicore (Victor Trucco) es un port; su lista de mappers puede ser menor que la de MiSTer.")
    if rom.mapper == 4 and len(rom.prg) > 512 * 1024:
        lines.append("RIESGO: MMC3 con más de 512 KB de PRG no existe en cartuchos reales.")
    if rom.mapper == 1 and len(rom.prg) > 512 * 1024:
        lines.append("RIESGO: MMC1 con más de 512 KB de PRG no existe en cartuchos reales.")
    if rom.mapper == 0 and len(rom.prg) > 32768:
        lines.append("ERROR: NROM no puede tener más de 32 KB de PRG.")
    if rom.mapper == 0 and len(rom.chr) > 8192:
        lines.append("ERROR: NROM no puede tener más de 8 KB de CHR.")
    return lines
