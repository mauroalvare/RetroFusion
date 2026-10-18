"""Platform-independent description of a loaded program.

Every loader (NES, C64, Atari, Spectrum...) turns its file format into a
Program: a list of memory segments (where each block of bytes lives in the
CPU address space), entry points, hardware names and an ordered list of
chunks that rebuild the original file byte for byte.
"""


class Segment:
    def __init__(self, index, base, data, fixed=True, size=None, label="", growable=False):
        self.index = index
        self.base = base
        self.data = bytes(data)
        self.size = len(self.data) if size is None else size
        self.fixed = fixed            # visible all the time (not bank switched)
        self.label = label            # human description: "PRG banco 3", "archivo GHOST.BIN"
        self.growable = growable      # the container allows the segment to change size
        self.offset = 0               # offset inside the source file (informational)

    @property
    def end(self):
        return self.base + self.size

    def contains(self, addr):
        return self.base <= addr < self.end

    def to_dict(self):
        return {"index": self.index, "base": "%04X" % self.base, "size": self.size,
                "fixed": self.fixed, "label": self.label, "growable": self.growable}


class Program:
    def __init__(self, platform, cpu, name):
        self.platform = platform      # nes, c64, vic20, plus4, atari8, apple2, zx, cpc, pc88, raw
        self.cpu = cpu                # 6502, z80, 68000, x86
        self.name = name
        self.segments = []
        self.entries = []             # (addr, name)
        self.vectors = []             # (segment index, addr, [names]) words pointing to code
        self.hw = {}                  # addr -> name (hardware registers, OS/ROM routines)
        self.ports = {}               # Z80 I/O port -> name
        self.notes = []
        self.chunks = []              # ("raw", bytes, comment) | ("seg", index) | ("xexhdr", index, ffff) | ("applehdr", index)
        self.container = {"format": "raw"}
        self.subprograms = []         # other programs inside a disk/tape image: (title, Program)
        self.supported = True
        self.reason = ""
        self.info = {}

    def add_segment(self, seg):
        seg.index = len(self.segments)
        self.segments.append(seg)
        return seg

    def payload(self):
        """Bytes main.asm must produce for an unmodified program."""
        out = bytearray()
        for ch in self.chunks:
            kind = ch[0]
            if kind == "raw":
                out += ch[1]
            elif kind == "seg":
                out += self.segments[ch[1]].data
            elif kind == "xexhdr":
                seg = self.segments[ch[1]]
                if ch[2]:
                    out += b"\xff\xff"
                out += bytes([seg.base & 0xFF, seg.base >> 8, (seg.end - 1) & 0xFF, (seg.end - 1) >> 8])
            elif kind == "applehdr":
                seg = self.segments[ch[1]]
                out += bytes([seg.base & 0xFF, seg.base >> 8, seg.size & 0xFF, seg.size >> 8])
        return bytes(out)

    def summary(self):
        d = {
            "name": self.name, "platform": self.platform, "cpu": self.cpu,
            "supported": self.supported, "reason": self.reason,
            "format": self.container.get("format"),
            "segments": [s.to_dict() for s in self.segments],
            "entries": ["%04X %s" % (a, n) for a, n in self.entries],
            "notes": self.notes,
            "subprograms": [t for t, _ in self.subprograms],
        }
        d.update(self.info)
        return d


def unsupported(name, platform, cpu, reason):
    p = Program(platform, cpu, name)
    p.supported = False
    p.reason = reason
    return p
