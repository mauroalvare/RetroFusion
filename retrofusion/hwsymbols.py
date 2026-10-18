"""Names for hardware registers and OS/ROM routines of each platform.

Only well-known addresses are listed. Names make the disassembly readable
(`STA BGCOL0` instead of `STA $D021`) and give the AI context.
"""

from .platforms.nes import NES_REGISTERS


def _seq(base, names):
    return {base + i: n for i, n in enumerate(names) if n}


C64 = {}
C64.update(_seq(0xD000, ["SP0X", "SP0Y", "SP1X", "SP1Y", "SP2X", "SP2Y", "SP3X", "SP3Y",
                         "SP4X", "SP4Y", "SP5X", "SP5Y", "SP6X", "SP6Y", "SP7X", "SP7Y",
                         "MSIGX", "SCROLY", "RASTER", "LPENX", "LPENY", "SPENA", "SCROLX", "YXPAND",
                         "VMCSB", "VICIRQ", "IRQMSK", "SPBGPR", "SPMC", "XXPAND", "SPSPCL", "SPBGCL",
                         "EXTCOL", "BGCOL0", "BGCOL1", "BGCOL2", "BGCOL3", "SPMC0", "SPMC1",
                         "SP0COL", "SP1COL", "SP2COL", "SP3COL", "SP4COL", "SP5COL", "SP6COL", "SP7COL"]))
C64.update(_seq(0xD400, ["FRELO1", "FREHI1", "PWLO1", "PWHI1", "VCREG1", "ATDCY1", "SUREL1",
                         "FRELO2", "FREHI2", "PWLO2", "PWHI2", "VCREG2", "ATDCY2", "SUREL2",
                         "FRELO3", "FREHI3", "PWLO3", "PWHI3", "VCREG3", "ATDCY3", "SUREL3",
                         "CUTLO", "CUTHI", "RESON", "SIGVOL", "POTX", "POTY", "RANDOM", "ENV3"]))
C64.update(_seq(0xDC00, ["CIAPRA", "CIAPRB", "CIDDRA", "CIDDRB", "TIMALO", "TIMAHI", "TIMBLO", "TIMBHI",
                         "TODTEN", "TODSEC", "TODMIN", "TODHRS", "CIASDR", "CIAICR", "CIACRA", "CIACRB"]))
C64.update(_seq(0xDD00, ["CI2PRA", "CI2PRB", "C2DDRA", "C2DDRB", "TI2ALO", "TI2AHI", "TI2BLO", "TI2BHI",
                         "", "", "", "", "", "CI2ICR", "CI2CRA", "CI2CRB"]))
CBM_KERNAL = {
    0xFF81: "CINT", 0xFF84: "IOINIT", 0xFF87: "RAMTAS", 0xFF8A: "RESTOR", 0xFF8D: "VECTOR",
    0xFF90: "SETMSG", 0xFFB7: "READST", 0xFFBA: "SETLFS", 0xFFBD: "SETNAM", 0xFFC0: "OPEN",
    0xFFC3: "CLOSE", 0xFFC6: "CHKIN", 0xFFC9: "CHKOUT", 0xFFCC: "CLRCHN", 0xFFCF: "CHRIN",
    0xFFD2: "CHROUT", 0xFFD5: "LOAD", 0xFFD8: "SAVE", 0xFFDB: "SETTIM", 0xFFDE: "RDTIM",
    0xFFE1: "STOP", 0xFFE4: "GETIN", 0xFFE7: "CLALL", 0xFFEA: "UDTIM", 0xFFED: "SCREEN",
    0xFFF0: "PLOT", 0xFFF3: "IOBASE",
}
C64.update(CBM_KERNAL)
C64.update({0x0000: "D6510", 0x0001: "R6510", 0x0314: "CINV", 0x0316: "CBINV", 0x0318: "NMINV",
            0x0302: "IMAIN", 0x0326: "IBSOUT", 0xEA31: "KERNAL_IRQ", 0xEA81: "KERNAL_IRQ_EXIT",
            0xFFFA: "HW_NMI", 0xFFFC: "HW_RESET", 0xFFFE: "HW_IRQ"})

VIC20 = dict(CBM_KERNAL)
VIC20.update(_seq(0x9000, ["VICCR0", "VICCR1", "VICCR2", "VICCR3", "VICCR4", "VICCR5", "VICCR6", "VICCR7",
                           "VICCR8", "VICCR9", "VICCRA", "VICCRB", "VICCRC", "VICCRD", "VICCRE", "VICCRF"]))
VIC20.update(_seq(0x9110, ["VIA1PB", "VIA1PA", "VIA1DDRB", "VIA1DDRA", "VIA1T1CL", "VIA1T1CH", "VIA1T1LL", "VIA1T1LH",
                           "VIA1T2CL", "VIA1T2CH", "VIA1SR", "VIA1ACR", "VIA1PCR", "VIA1IFR", "VIA1IER", "VIA1PA2"]))
VIC20.update(_seq(0x9120, ["VIA2PB", "VIA2PA", "VIA2DDRB", "VIA2DDRA", "VIA2T1CL", "VIA2T1CH", "VIA2T1LL", "VIA2T1LH",
                           "VIA2T2CL", "VIA2T2CH", "VIA2SR", "VIA2ACR", "VIA2PCR", "VIA2IFR", "VIA2IER", "VIA2PA2"]))
VIC20.update({0x0314: "CINV", 0x0316: "CBINV", 0x0318: "NMINV", 0xEABF: "KERNAL_IRQ"})

PLUS4 = dict(CBM_KERNAL)
PLUS4.update({0xFF00: "TED_T1L", 0xFF01: "TED_T1H", 0xFF02: "TED_T2L", 0xFF03: "TED_T2H",
              0xFF04: "TED_T3L", 0xFF05: "TED_T3H", 0xFF06: "TED_CTRL1", 0xFF07: "TED_CTRL2",
              0xFF08: "TED_KBD", 0xFF09: "TED_IRQ", 0xFF0A: "TED_IRQMASK", 0xFF0B: "TED_RASTERCMP",
              0xFF0E: "TED_SND1", 0xFF0F: "TED_SND2", 0xFF10: "TED_SND2HI", 0xFF11: "TED_SNDCTRL",
              0xFF12: "TED_BITMAP", 0xFF13: "TED_CHARBASE", 0xFF14: "TED_VIDEOBASE",
              0xFF15: "TED_BGCOL0", 0xFF16: "TED_BGCOL1", 0xFF17: "TED_BGCOL2", 0xFF18: "TED_BGCOL3",
              0xFF19: "TED_BORDER", 0xFF1D: "TED_RASTER", 0xFF3E: "TED_ROMON", 0xFF3F: "TED_RAMON"})

ATARI8 = {}
ATARI8.update(_seq(0xD000, ["HPOSP0", "HPOSP1", "HPOSP2", "HPOSP3", "HPOSM0", "HPOSM1", "HPOSM2", "HPOSM3",
                            "SIZEP0", "SIZEP1", "SIZEP2", "SIZEP3", "SIZEM", "GRAFP0", "GRAFP1", "GRAFP2",
                            "GRAFP3", "GRAFM", "COLPM0", "COLPM1", "COLPM2", "COLPM3", "COLPF0", "COLPF1",
                            "COLPF2", "COLPF3", "COLBK", "PRIOR", "VDELAY", "GRACTL", "HITCLR", "CONSOL"]))
ATARI8.update(_seq(0xD200, ["AUDF1", "AUDC1", "AUDF2", "AUDC2", "AUDF3", "AUDC3", "AUDF4", "AUDC4",
                            "AUDCTL", "STIMER", "RANDOM", "POTGO", "", "SEROUT", "IRQEN", "SKCTL"]))
ATARI8.update({0xD300: "PORTA", 0xD301: "PORTB", 0xD302: "PACTL", 0xD303: "PBCTL"})
ATARI8.update(_seq(0xD400, ["DMACTL", "CHACTL", "DLISTL", "DLISTH", "HSCROL", "VSCROL", "", "PMBASE",
                            "", "CHBASE", "WSYNC", "VCOUNT", "PENH", "PENV", "NMIEN", "NMIRES"]))
ATARI8.update({
    0x000A: "DOSVEC", 0x000C: "DOSINI", 0x0012: "RTCLOK", 0x0013: "RTCLOK1", 0x0014: "RTCLOK2",
    0x0042: "CRITIC", 0x004D: "ATRACT", 0x0058: "SAVMSC", 0x0200: "VDSLST", 0x0222: "VVBLKI",
    0x0224: "VVBLKD", 0x022F: "SDMCTL", 0x0230: "SDLSTL", 0x0231: "SDLSTH", 0x0244: "COLDST",
    0x026F: "GPRIOR", 0x0278: "STICK0", 0x0279: "STICK1", 0x0284: "STRIG0", 0x0285: "STRIG1",
    0x02C0: "PCOLR0", 0x02C1: "PCOLR1", 0x02C2: "PCOLR2", 0x02C3: "PCOLR3", 0x02C4: "COLOR0",
    0x02C5: "COLOR1", 0x02C6: "COLOR2", 0x02C7: "COLOR3", 0x02C8: "COLOR4", 0x02E0: "RUNAD",
    0x02E2: "INITAD", 0x02F4: "CHBAS", 0x02FC: "CH", 0xE450: "DISKIV", 0xE453: "DSKINV",
    0xE456: "CIOV", 0xE459: "SIOV", 0xE45C: "SETVBV", 0xE45F: "SYSVBV", 0xE462: "XITVBV",
    0xE465: "SIOINV", 0xE468: "SENDEV", 0xE46B: "INTINV", 0xE46E: "CIOINV", 0xE474: "WARMSV",
    0xE477: "COLDSV",
})

APPLE2 = {
    0xC000: "KBD", 0xC010: "KBDSTRB", 0xC020: "TAPEOUT", 0xC030: "SPKR", 0xC050: "TXTCLR",
    0xC051: "TXTSET", 0xC052: "MIXCLR", 0xC053: "MIXSET", 0xC054: "LOWSCR", 0xC055: "HISCR",
    0xC056: "LORES", 0xC057: "HIRES", 0xC061: "BUTN0", 0xC062: "BUTN1", 0xC064: "PADDL0",
    0xC065: "PADDL1", 0xC070: "PTRIG", 0xC080: "LC_RAM_RD", 0xC081: "LC_ROM_RD_WR", 0xC083: "LC_RAM_RW",
    0xC0E8: "DISK_MOTOROFF", 0xC0E9: "DISK_MOTORON", 0xC0EC: "DISK_Q6L", 0xC0ED: "DISK_Q6H",
    0xC0EE: "DISK_Q7L", 0xC0EF: "DISK_Q7H",
    0xF800: "PLOT", 0xF819: "HLINE", 0xF828: "VLINE", 0xF832: "CLRSCR", 0xF836: "CLRTOP",
    0xF847: "GBASCALC", 0xF864: "SETCOL", 0xF871: "SCRN", 0xFB2F: "INIT", 0xFB1E: "PREAD",
    0xFC58: "HOME", 0xFCA8: "WAIT", 0xFD0C: "RDKEY", 0xFD1B: "KEYIN", 0xFD6A: "GETLN",
    0xFD8E: "CROUT", 0xFDDA: "PRBYTE", 0xFDED: "COUT", 0xFDF0: "COUT1", 0xFF3A: "BELL",
    0xFF69: "MONZ", 0x03D0: "DOSWARM", 0x03D3: "DOSCOLD", 0x03D9: "RWTS",
}

ZX = {
    0x0000: "ROM_START", 0x0008: "ERROR_1", 0x0010: "PRINT_A_1", 0x0018: "GET_CHAR",
    0x0020: "NEXT_CHAR", 0x0028: "FP_CALC", 0x0030: "BC_SPACES", 0x0038: "MASK_INT",
    0x0066: "NMI_RESET", 0x028E: "KEY_SCAN", 0x02BF: "KEYBOARD", 0x03B5: "BEEPER", 0x03F8: "BEEP",
    0x04C2: "SA_BYTES", 0x0556: "LD_BYTES", 0x0D6B: "CLS", 0x0DAF: "CL_ALL", 0x0DD9: "CL_SET",
    0x0E44: "CL_LINE", 0x1601: "CHAN_OPEN", 0x203C: "PR_STRING", 0x22AA: "PIXEL_ADD",
    0x2D2B: "STACK_BC", 0x2DA2: "FP_TO_BC", 0x3D00: "ROM_CHARSET",
    0x4000: "SCREEN", 0x5800: "ATTRS", 0x5B00: "PRINTER_BUF",
    0x5C00: "KSTATE", 0x5C08: "LAST_K", 0x5C09: "REPDEL", 0x5C0A: "REPPER", 0x5C36: "CHARS",
    0x5C3A: "ERR_NR", 0x5C3B: "FLAGS", 0x5C3C: "TV_FLAG", 0x5C3D: "ERR_SP", 0x5C48: "BORDCR",
    0x5C4B: "VARS", 0x5C53: "PROG", 0x5C78: "FRAMES", 0x5C7B: "UDG", 0x5C8D: "ATTR_P",
    0x5C8F: "ATTR_T", 0x5CB2: "RAMTOP",
}
ZX_PORTS = {0xFE: "ULA", 0x1F: "KEMPSTON"}

CPC = {
    0x0000: "RST0_RESET", 0x0038: "RST7_INTERRUPT",
    0xBB00: "KM_INITIALISE", 0xBB06: "KM_WAIT_CHAR", 0xBB09: "KM_READ_CHAR", 0xBB18: "KM_WAIT_KEY",
    0xBB1B: "KM_READ_KEY", 0xBB1E: "KM_TEST_KEY", 0xBB5A: "TXT_OUTPUT", 0xBB5D: "TXT_WR_CHAR",
    0xBB6C: "TXT_CLEAR_WINDOW", 0xBB75: "TXT_SET_CURSOR", 0xBB90: "TXT_SET_PEN", 0xBB96: "TXT_SET_PAPER",
    0xBBDE: "GRA_SET_PEN", 0xBBEA: "GRA_PLOT_ABSOLUTE", 0xBBF6: "GRA_LINE_ABSOLUTE",
    0xBC0E: "SCR_SET_MODE", 0xBC14: "SCR_CLEAR", 0xBC32: "SCR_SET_INK", 0xBC38: "SCR_SET_BORDER",
    0xBC77: "CAS_IN_OPEN", 0xBC7A: "CAS_IN_CLOSE", 0xBC83: "CAS_IN_DIRECT", 0xBCA7: "SOUND_RESET",
    0xBCAA: "SOUND_QUEUE", 0xBD19: "MC_WAIT_FLYBACK", 0xC000: "SCREEN",
}

PLATFORM_SYMBOLS = {
    "nes": NES_REGISTERS, "c64": C64, "vic20": VIC20, "plus4": PLUS4, "atari8": ATARI8,
    "apple2": APPLE2, "zx": ZX, "cpc": CPC, "pc88": {}, "raw": {},
}
PLATFORM_PORTS = {"zx": ZX_PORTS}

PLATFORM_NAMES = {
    "nes": "Nintendo NES / Famicom", "c64": "Commodore 64", "vic20": "Commodore VIC-20",
    "plus4": "Commodore Plus/4 / C16", "atari8": "Atari 8-bit (400/800/XL/XE)",
    "apple2": "Apple II", "zx": "ZX Spectrum", "cpc": "Amstrad CPC", "pc88": "NEC PC-8801",
    "amiga": "Commodore Amiga", "atarist": "Atari ST", "dos": "PC con MS-DOS", "raw": "binario sin formato",
}
