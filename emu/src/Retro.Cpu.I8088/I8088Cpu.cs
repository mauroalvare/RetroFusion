namespace Retro.Cpu.I8088;

/// <summary>
/// First functional 8088 milestone, not a timed PC/XT CPU yet. Executes data movement,
/// integer ALU including multiply/divide, shifts/rotates, control flow, stack, strings,
/// I/O, decimal adjustments and interrupts. Single stepping uses a pending type-1 boundary.
/// The 8087 remains unsupported.
/// Optional clocked bus/prefetch mode advances peripherals; EU timing is approximate.
/// </summary>
public sealed partial class I8088Cpu(I8088Bus bus, I8088InterruptSource? interrupts)
{
    public I8088Cpu(I8088Bus bus) : this(bus, null) { }

    public const ushort Carry = 0x0001, Parity = 0x0004, Auxiliary = 0x0010, Zero = 0x0040,
        Sign = 0x0080, Trap = 0x0100, InterruptEnable = 0x0200, Direction = 0x0400, Overflow = 0x0800;
    private readonly ushort[] _r = new ushort[8], _s = [0, 0xFFFF, 0, 0];
    private ushort _flags = 0xF002;
    private int _segmentOverride = -1;
    private bool _interruptShadow, _segmentShadow;
    private byte? _irq;
    private bool _nmi, _trapPending, _waitingForTest, _busLocked;
    private ushort _startCs, _startIp;

    public ushort AX { get => _r[0]; set => _r[0] = value; }
    public ushort CX { get => _r[1]; set => _r[1] = value; }
    public ushort DX { get => _r[2]; set => _r[2] = value; }
    public ushort BX { get => _r[3]; set => _r[3] = value; }
    public ushort SP { get => _r[4]; set => _r[4] = value; }
    public ushort BP { get => _r[5]; set => _r[5] = value; }
    public ushort SI { get => _r[6]; set => _r[6] = value; }
    public ushort DI { get => _r[7]; set => _r[7] = value; }
    public ushort ES { get => _s[0]; set => _s[0] = value; }
    public ushort CS { get => _s[1]; set => _s[1] = value; }
    public ushort SS { get => _s[2]; set => _s[2] = value; }
    public ushort DS { get => _s[3]; set => _s[3] = value; }
    public ushort IP { get; set; }
    public ushort Flags { get => _flags; set => _flags = (ushort)((value & 0x0FD5) | 0xF002); }
    public bool Halted { get; private set; }
    public bool TrapPending => _trapPending;
    /// <summary>Instructions executed; each REP iteration is a restartable step.</summary>
    public long Instructions { get; private set; }
    public uint PhysicalPC => Physical(CS, IP);
    public static uint Physical(ushort segment, ushort offset) => (((uint)segment << 4) + offset) & 0xFFFFF;
    public bool Flag(ushort bit) => (Flags & bit) != 0;

    public void Reset()
    {
        Array.Clear(_r); Array.Clear(_s);
        CS = 0xFFFF; IP = 0; Flags = 0;
        Halted = false; Instructions = 0;
        _interruptShadow = _segmentShadow = _nmi = _trapPending = _waitingForTest = false; _irq = null;
        _busLocked = false; bus.SetLock(false); bus.SetBusGranted(false);
        _cycles = 0; _timingStep = _replaying = _repeatAgain = false; _repeatCode.Clear(); FlushQueue();
    }

    /// <summary>Latched diagnostic vector, retained while IF is clear. Devices use I8088InterruptSource.</summary>
    public void RequestInterrupt(byte vector) => _irq = vector;
    public void RequestNmi() => _nmi = true;

    public byte ReadByte(ushort segment, ushort offset) { DataBusCycle(); return bus.ReadMemory(Physical(segment, offset)); }
    public ushort ReadWord(ushort segment, ushort offset) => (ushort)(ReadByte(segment, offset) | ReadByte(segment, (ushort)(offset + 1)) << 8);
    public void WriteByte(ushort segment, ushort offset, byte value) { DataBusCycle(); bus.WriteMemory(Physical(segment, offset), value); }
    public void WriteWord(ushort segment, ushort offset, ushort value)
    {
        WriteByte(segment, offset, (byte)value); WriteByte(segment, (ushort)(offset + 1), (byte)(value >> 8));
    }
    private byte Fetch() { if (TimingEnabled) return FetchTimed(); byte value = ReadByte(CS, IP); IP++; return value; }
    private ushort FetchWord() { int low = Fetch(); return (ushort)(low | Fetch() << 8); }
    private void Push(ushort value) { SP -= 2; WriteWord(SS, SP, value); }
    private ushort Pop() { ushort value = ReadWord(SS, SP); SP += 2; return value; }
    private void SetFlag(ushort bit, bool value) => Flags = value ? (ushort)(Flags | bit) : (ushort)(Flags & ~bit);
    private int GetReg(int reg, bool word) => word ? _r[reg] : reg < 4 ? _r[reg] & 255 : _r[reg - 4] >> 8;
    private void SetReg(int reg, bool word, int value)
    {
        if (word) _r[reg] = (ushort)value;
        else if (reg < 4) _r[reg] = (ushort)((_r[reg] & 0xFF00) | (value & 255));
        else _r[reg - 4] = (ushort)((_r[reg - 4] & 255) | (value & 255) << 8);
    }
    private readonly record struct Operand(int Register, ushort Segment, ushort Offset);
    private (int Reg, Operand Rm) Decode()
    {
        byte code = Fetch(); _modRm = code; int mod = code >> 6, rm = code & 7;
        if (mod != 3) _eaClocks += mod == 0 && rm == 6 ? 6 : (rm is 0 or 3 ? 7 : rm is 1 or 2 ? 8 : 5) + (mod == 0 ? 0 : 4);
        if (mod == 3) return ((code >> 3) & 7, new Operand(rm, 0, 0));
        bool bp = rm is 2 or 3 || rm == 6 && mod != 0;
        int address = rm switch { 0 => BX + SI, 1 => BX + DI, 2 => BP + SI, 3 => BP + DI,
            4 => SI, 5 => DI, 6 => mod == 0 ? FetchWord() : BP, _ => BX };
        if (mod == 1) address += (sbyte)Fetch();
        else if (mod == 2) address += FetchWord();
        return ((code >> 3) & 7, new Operand(-1, _s[_segmentOverride >= 0 ? _segmentOverride : bp ? 2 : 3], (ushort)address));
    }
    private int Read(Operand o, bool word) => o.Register >= 0 ? GetReg(o.Register, word) : word ? ReadWord(o.Segment, o.Offset) : ReadByte(o.Segment, o.Offset);
    private void Write(Operand o, bool word, int value)
    {
        if (o.Register >= 0) SetReg(o.Register, word, value);
        else if (word) WriteWord(o.Segment, o.Offset, (ushort)value);
        else WriteByte(o.Segment, o.Offset, (byte)value);
    }
    private void Szp(int value, bool word)
    {
        value &= word ? 65535 : 255;
        SetFlag(Sign, (value & (word ? 0x8000 : 0x80)) != 0);
        SetFlag(Zero, value == 0);
        SetFlag(Parity, (System.Numerics.BitOperations.PopCount((uint)(value & 255)) & 1) == 0);
    }
    private void DecimalAdjust(bool subtract, bool unpacked)
    {
        int original = AX & 255, value = original, high = AX >> 8;
        bool auxiliary = Flag(Auxiliary);
        bool lowAdjust = (original & 15) > 9 || auxiliary, carry = Flag(Carry);
        if (lowAdjust) value += subtract ? -6 : 6;
        SetFlag(Auxiliary, lowAdjust);
        if (unpacked)
        {
            // The 8088 adjusts AL and AH separately; do not use the later AX +/- 0106h.
            if (lowAdjust) high += subtract ? -1 : 1;
            AX = (ushort)(((high & 255) << 8) | (value & 15));
            SetFlag(Carry, lowAdjust);
        }
        else
        {
            // The original NMOS part compares against 9F when the incoming AF is set.
            // Hardware-generated tests cover inputs outside the valid packed-BCD domain.
            bool highAdjust = original > (auxiliary ? 0x9F : 0x99) || carry;
            if (highAdjust) value += subtract ? -0x60 : 0x60;
            SetReg(0, false, value);
            SetFlag(Carry, highAdjust);
            Szp(value, false);
        }
        // Undefined arithmetic flags retain their previous value, not a silicon claim.
    }

    private void AsciiProductAdjust(byte op, byte repeat)
    {
        int radix = Fetch();
        int low = AX & 255;
        if (op == 0xD4 && radix == 0) { Szp(0, false); EnterInterrupt(0); return; }
        AX = op == 0xD4 ? (ushort)(((low / radix) << 8) | low % radix)
            : (ushort)((low + (AX >> 8) * radix) & 255);
        Szp(AX, false);
    }

    private int Alu(int operation, int a, int b, bool word)
    {
        int mask = word ? 65535 : 255, sign = word ? 32768 : 128, result;
        if (operation is 1 or 4 or 6)
        {
            result = operation == 1 ? a | b : operation == 4 ? a & b : a ^ b;
            SetFlag(Carry, false); SetFlag(Overflow, false); SetFlag(Auxiliary, false); // AF undefined on silicon
        }
        else
        {
            int carry = (operation is 2 or 3) && Flag(Carry) ? 1 : 0;
            bool subtract = operation is 3 or 5 or 7;
            result = subtract ? a - b - carry : a + b + carry;
            SetFlag(Carry, subtract ? a < b + carry : result > mask);
            SetFlag(Auxiliary, ((a ^ b ^ result) & 16) != 0);
            SetFlag(Overflow, ((subtract ? (a ^ b) : ~(a ^ b)) & (a ^ result) & sign) != 0);
        }
        Szp(result, word); return result & mask;
    }
    private int IncDec(int value, bool word, bool decrement)
    {
        bool carry = Flag(Carry); int result = Alu(decrement ? 5 : 0, value, 1, word); SetFlag(Carry, carry); return result;
    }
    private int Shift(int operation, int value, int count, bool word)
    {
        if (count == 0) return value;
        int mask = word ? 65535 : 255, sign = word ? 32768 : 128, original = value;
        bool carry = Flag(Carry);
        // The 8088 uses all eight count bits, unlike the 286's five-bit count mask.
        for (int n = 0; n < count; n++)
        {
            bool outgoing = (value & ((operation & 1) == 0 ? sign : 1)) != 0;
            value = operation switch
            {
                0 => value << 1 | (outgoing ? 1 : 0),
                1 => value >> 1 | (outgoing ? sign : 0),
                2 => value << 1 | (carry ? 1 : 0),
                3 => value >> 1 | (carry ? sign : 0),
                4 => value << 1,
                5 => value >> 1,
                _ => value >> 1 | (value & sign), // SAR preserves the sign bit
            };
            value &= mask;
            carry = outgoing;
        }
        SetFlag(Carry, carry);
        if (operation >= 4)
        {
            Szp(value, word);
            SetFlag(Auxiliary, false); // undefined on silicon; normalize as in logical ALU
        }
        if (count == 1)
            SetFlag(Overflow, operation switch
            {
                0 or 2 or 4 => ((value & sign) != 0) != carry,
                1 or 3 => ((value & sign) != 0) != ((value & (sign >> 1)) != 0),
                5 => (original & sign) != 0,
                _ => false,
            });
        // OF for multi-bit operations is undefined; keep its previous value.
        return value;
    }
    private void MultiplyDivide(int operation, int value, bool word, bool negateQuotient = false)
    {
        bool signed = (operation & 1) != 0;
        if (operation < 6)
        {
            long left = signed ? word ? (short)AX : (sbyte)AX : word ? AX : AX & 255;
            long right = signed ? word ? (short)value : (sbyte)value : value;
            long product = left * right;
            AX = (ushort)product;
            if (word) DX = (ushort)(product >> 16);
            bool overflow = signed ? product < (word ? -32768 : -128) || product > (word ? 32767 : 127)
                : product > (word ? 65535 : 255);
            SetFlag(Carry, overflow); SetFlag(Overflow, overflow);
            // Other arithmetic flags are undefined; retain them deterministically.
            return;
        }
        long dividend = word ? signed ? (int)(((uint)DX << 16) | AX) : ((uint)DX << 16) | AX
            : signed ? (short)AX : AX;
        long divisor = signed ? word ? (short)value : (sbyte)value : value;
        if (divisor == 0) { EnterInterrupt(0); return; }
        long quotient = dividend / divisor;
        if (negateQuotient) quotient = -quotient;
        long maximum = signed ? word ? 32767 : 127 : word ? 65535 : 255;
        // Unlike later x86 CPUs, 8088 IDIV also faults on -128 and -32768.
        if (quotient < (signed ? -maximum : 0) || quotient > maximum) { EnterInterrupt(0); return; }
        long remainder = dividend % divisor;
        if (word) { AX = (ushort)quotient; DX = (ushort)remainder; }
        else AX = (ushort)(((remainder & 255) << 8) | (quotient & 255));
        // DIV/IDIV flags are undefined; errors retain AX/DX before entering type 0.
        // EnterInterrupt saves the already consumed IP: 8088 returns to the next instruction.
    }
    private bool Condition(int condition) => condition switch
    {
        0 => Flag(Overflow), 1 => !Flag(Overflow), 2 => Flag(Carry), 3 => !Flag(Carry),
        4 => Flag(Zero), 5 => !Flag(Zero), 6 => Flag(Carry) || Flag(Zero), 7 => !Flag(Carry) && !Flag(Zero),
        8 => Flag(Sign), 9 => !Flag(Sign), 10 => Flag(Parity), 11 => !Flag(Parity),
        12 => Flag(Sign) != Flag(Overflow), 13 => Flag(Sign) == Flag(Overflow),
        14 => Flag(Zero) || Flag(Sign) != Flag(Overflow), _ => !Flag(Zero) && Flag(Sign) == Flag(Overflow)
    };
    private void EnterInterrupt(byte vector, bool hardware = false)
    {
        _repeatCode.Clear();
        if (hardware) { DataBusCycle(); DataBusCycle(); }
        Push(Flags); Push(CS); Push(IP);
        SetFlag(InterruptEnable, false); SetFlag(Trap, false); Halted = _waitingForTest = false;
        IP = ReadWord(0, (ushort)(vector * 4)); CS = ReadWord(0, (ushort)(vector * 4 + 2));
        FlushQueue(); IdleClocks(hardware ? 13 : 31);
    }
    private NotSupportedException Unsupported(byte opcode, string? detail = null) =>
        new($"8088: instrucción {opcode:X2} en {_startCs:X4}:{_startIp:X4} todavía no implementada{(detail == null ? "." : " (" + detail + ").")}");

    /// <summary>One instruction, one REP iteration, or interrupt acceptance. False while halted with no accepted interrupt.</summary>
    public bool Step()
    {
        bool inhibit = _interruptShadow, inhibitNmi = _segmentShadow;
        _interruptShadow = _segmentShadow = false;
        if (_nmi && !inhibitNmi) { _nmi = false; EnterInterrupt(2, true); return true; }
        if (_irq is { } vector && Flag(InterruptEnable) && !inhibit)
        { _irq = null; EnterInterrupt(vector, true); return true; }
        if (Flag(InterruptEnable) && !inhibit && interrupts?.InterruptRequested == true)
        { EnterInterrupt(interrupts.Acknowledge(), true); return true; }
        // HLT finishes only after an enabled external interrupt; its trap waits until then.
        if (Halted) { if (TimingEnabled) { bus.SetBusGranted(bus.HoldRequested); Clock(); } return false; }
        // The original 8088 gives external interrupts priority over the pending trap.
        // Keep it pending across their entry, so it nests before the first ISR instruction.
        if (_trapPending && !inhibitNmi) { _trapPending = false; EnterInterrupt(1, true); return true; }
        bool trap = Flag(Trap);
        ushort timingFlags = Flags, timingCx = CX; StartTimingStep();
        _startCs = CS; _startIp = IP; _segmentOverride = -1;
        byte opcode, repeat = 0; bool locked = false;
        _waitingForTest = false; int prefixes = 0;
        while (true)
        {
            opcode = Fetch();
            if (opcode is 0x26 or 0x2E or 0x36 or 0x3E) _segmentOverride = (opcode >> 3) & 3;
            else if (opcode is 0xF2 or 0xF3) repeat = opcode;
            else if (opcode is 0xF0 or 0xF1) locked = true;
            else break;
            if (++prefixes == 65536) throw Unsupported(opcode, "segmento lleno de prefijos");
        }
        try
        {
            if (locked) LockBus();
            Execute(opcode, repeat);
        }
        finally { if (_busLocked) { _busLocked = false; bus.SetLock(false); } }
        EndTimingStep(opcode, repeat, prefixes, timingFlags, timingCx);
        Instructions++;
        // POPF/IRET changes apply to the next instruction; clearing TF still traps this one.
        _trapPending = trap && !_segmentShadow && !_waitingForTest;
        return true;
    }

    private void LockBus() { if (!_busLocked) { _busLocked = true; bus.SetLock(true); } }

    private void Execute(byte op, byte repeat)
    {
        bool word = (op & 1) != 0;
        if (op <= 0x3D && (op & 7) <= 5)
        {
            int operation = op >> 3;
            if ((op & 7) < 4)
            {
                var (reg, rm) = Decode(); bool toReg = (op & 2) != 0;
                int result = Alu(operation, toReg ? GetReg(reg, word) : Read(rm, word), toReg ? Read(rm, word) : GetReg(reg, word), word);
                if (operation != 7) { if (toReg) SetReg(reg, word, result); else Write(rm, word, result); }
            }
            else
            {
                int result = Alu(operation, GetReg(0, word), word ? FetchWord() : Fetch(), word);
                if (operation != 7) SetReg(0, word, result);
            }
            return;
        }
        if (op is >= 0x40 and <= 0x4F) { int r = op & 7; _r[r] = (ushort)IncDec(_r[r], true, op >= 0x48); return; }
        if (op is >= 0x50 and <= 0x57) { Push(op == 0x54 ? (ushort)(SP - 2) : _r[op & 7]); return; } // original 8088 PUSH SP
        if (op is >= 0x58 and <= 0x5F) { _r[op & 7] = Pop(); return; }
        if (op is >= 0x70 and <= 0x7F) { sbyte displacement = (sbyte)Fetch(); if (Condition(op & 15)) IP = (ushort)(IP + displacement); return; }
        if (op is >= 0xB0 and <= 0xBF) { bool wide = op >= 0xB8; SetReg(op & 7, wide, wide ? FetchWord() : Fetch()); return; }
        if (op is >= 0x91 and <= 0x97) { int r = op & 7; (AX, _r[r]) = (_r[r], AX); return; }
        switch (op)
        {
            case 0x06: case 0x0E: case 0x16: case 0x1E: Push(_s[(op >> 3) & 3]); break;
            case 0x07: case 0x17: case 0x1F:
                _s[(op >> 3) & 3] = Pop(); _interruptShadow = _segmentShadow = true; break;
            case 0x27: case 0x2F: case 0x37: case 0x3F:
                DecimalAdjust((op & 8) != 0, (op & 0x10) != 0); break;
            case 0x80: case 0x81: case 0x82: case 0x83:
            {
                var (operation, rm) = Decode(); word = (op & 1) != 0;
                int immediate = op == 0x81 ? FetchWord() : op == 0x83 ? (ushort)(short)(sbyte)Fetch() : Fetch();
                int result = Alu(operation, Read(rm, word), immediate, word);
                if (operation != 7) Write(rm, word, result); break;
            }
            case 0x84: case 0x85: { var (reg, rm) = Decode(); Alu(4, Read(rm, word), GetReg(reg, word), word); break; }
            case 0x86: case 0x87:
            {
                var (reg, rm) = Decode();
                if (rm.Register < 0) LockBus();
                int old = Read(rm, word); Write(rm, word, GetReg(reg, word)); SetReg(reg, word, old); break;
            }
            case 0x88: case 0x89: case 0x8A: case 0x8B:
            {
                var (reg, rm) = Decode(); if ((op & 2) != 0) SetReg(reg, word, Read(rm, word)); else Write(rm, word, GetReg(reg, word)); break;
            }
            case 0x8C: case 0x8E:
            {
                var (reg, rm) = Decode(); reg &= 3;
                if (op == 0x8E && reg == 1) throw Unsupported(op, "MOV CS no documentado");
                if (op == 0x8C) Write(rm, true, _s[reg]);
                else { _s[reg] = (ushort)Read(rm, true); _interruptShadow = _segmentShadow = true; }
                break;
            }
            case 0x8D: { var (reg, rm) = Decode(); if (rm.Register >= 0) throw Unsupported(op, "LEA requiere memoria"); _r[reg] = rm.Offset; break; }
            case 0x8F: { var (reg, rm) = Decode(); if (reg != 0) throw Unsupported(op); Write(rm, true, Pop()); break; }
            case 0x90: break;
            case 0x98: AX = (ushort)(short)(sbyte)AX; break;
            case 0x99: DX = (AX & 0x8000) != 0 ? (ushort)0xFFFF : (ushort)0; break;
            case 0x9A: { ushort target = FetchWord(), segment = FetchWord(); Push(CS); Push(IP); CS = segment; IP = target; break; }
            case 0x9B:
                _waitingForTest = bus.TestSignal;
                if (_waitingForTest) { IP = _startIp; _repeatAgain = true; }
                break;
            case 0x9C: Push(Flags); break;
            case 0x9D: Flags = Pop(); break;
            case 0x9E: Flags = (ushort)((Flags & 0xFF00) | (AX >> 8)); break;
            case 0x9F: AX = (ushort)((AX & 255) | (Flags & 255) << 8); break;
            case 0xA0: case 0xA1: case 0xA2: case 0xA3:
            {
                var memory = new Operand(-1, _s[_segmentOverride < 0 ? 3 : _segmentOverride], FetchWord());
                if (op < 0xA2) SetReg(0, word, Read(memory, word)); else Write(memory, word, GetReg(0, word)); break;
            }
            case 0xA8: case 0xA9: Alu(4, GetReg(0, word), word ? FetchWord() : Fetch(), word); break;
            case 0xA4: case 0xA5: case 0xA6: case 0xA7: case 0xAA: case 0xAB: case 0xAC: case 0xAD: case 0xAE: case 0xAF:
                String(op, repeat); break;
            case 0xC2: { ushort adjustment = FetchWord(); IP = Pop(); SP += adjustment; break; }
            case 0xC3: IP = Pop(); break;
            case 0xC4: case 0xC5:
            {
                var (reg, rm) = Decode(); if (rm.Register >= 0) throw Unsupported(op, "puntero lejano requiere memoria");
                ushort offset = ReadWord(rm.Segment, rm.Offset), segment = ReadWord(rm.Segment, (ushort)(rm.Offset + 2));
                _r[reg] = offset; _s[op == 0xC4 ? 0 : 3] = segment; break;
            }
            case 0xC6: case 0xC7: { var (reg, rm) = Decode(); if (reg != 0) throw Unsupported(op); Write(rm, word, word ? FetchWord() : Fetch()); break; }
            case 0xCA: { ushort adjustment = FetchWord(); IP = Pop(); CS = Pop(); SP += adjustment; break; }
            case 0xCB: IP = Pop(); CS = Pop(); break;
            case 0xCC: EnterInterrupt(3); break;
            case 0xCD: EnterInterrupt(Fetch()); break;
            case 0xCE: if (Flag(Overflow)) EnterInterrupt(4); break;
            case 0xCF: IP = Pop(); CS = Pop(); Flags = Pop(); break;
            case 0xD0: case 0xD1: case 0xD2: case 0xD3:
            {
                var (operation, rm) = Decode();
                if (operation == 6) throw Unsupported(op, "grupo reservado /6");
                int count = (op & 2) == 0 ? 1 : CX & 255;
                int value = Shift(operation, Read(rm, word), count, word);
                if (count != 0) Write(rm, word, value);
                break;
            }
            case 0xD4: case 0xD5: AsciiProductAdjust(op, repeat); break;
            case >= 0xD8 and <= 0xDF:
            {
                var (_, operand) = Decode();
                // Without an 8087, the CPU decodes the EA and reads a word; no floating-point result is produced.
                if (operand.Register < 0) _ = ReadWord(operand.Segment, operand.Offset);
                break;
            }
            case 0xD7: SetReg(0, false, ReadByte(_s[_segmentOverride < 0 ? 3 : _segmentOverride], (ushort)(BX + GetReg(0, false)))); break;
            case 0xE0: case 0xE1: case 0xE2: case 0xE3:
            {
                sbyte displacement = (sbyte)Fetch(); bool take = op == 0xE3 ? CX == 0 : --CX != 0 && (op == 0xE2 || Flag(Zero) == (op == 0xE1));
                if (take) IP = (ushort)(IP + displacement); break;
            }
            case 0xE4: case 0xE5: case 0xE6: case 0xE7: Io(Fetch(), word, (op & 2) != 0); break;
            case 0xE8: { short displacement = (short)FetchWord(); Push(IP); IP = (ushort)(IP + displacement); break; }
            case 0xE9: { short displacement = (short)FetchWord(); IP = (ushort)(IP + displacement); break; }
            case 0xEA: { ushort target = FetchWord(); CS = FetchWord(); IP = target; break; }
            case 0xEB: { sbyte displacement = (sbyte)Fetch(); IP = (ushort)(IP + displacement); break; }
            case 0xEC: case 0xED: case 0xEE: case 0xEF: Io(DX, word, (op & 2) != 0); break;
            case 0xF4: Halted = true; break;
            case 0xF5: SetFlag(Carry, !Flag(Carry)); break;
            case 0xF6: case 0xF7:
            {
                var (operation, rm) = Decode(); int value = Read(rm, word);
                if (operation == 0) Alu(4, value, word ? FetchWord() : Fetch(), word);
                else if (operation == 2) Write(rm, word, ~value);
                else if (operation == 3) Write(rm, word, Alu(5, 0, value, word));
                else if (operation >= 4)
                {
                    if (repeat != 0 && operation != 7) throw Unsupported(op, "REP en multiplicación/división no contrastado");
                    MultiplyDivide(operation, value, word, repeat != 0);
                }
                else throw Unsupported(op, $"grupo /{operation}"); break;
            }
            case 0xF8: SetFlag(Carry, false); break;
            case 0xF9: SetFlag(Carry, true); break;
            case 0xFA: SetFlag(InterruptEnable, false); break;
            case 0xFB: SetFlag(InterruptEnable, true); _interruptShadow = true; break;
            case 0xFC: SetFlag(Direction, false); break;
            case 0xFD: SetFlag(Direction, true); break;
            case 0xFE: case 0xFF:
            {
                var (operation, rm) = Decode(); word = op == 0xFF;
                if (operation <= 1) Write(rm, word, IncDec(Read(rm, word), word, operation == 1));
                else if (!word || operation == 7) throw Unsupported(op, $"grupo /{operation}");
                else if (operation == 6) Push(rm.Register == 4 ? (ushort)(SP - 2) : (ushort)Read(rm, true));
                else
                {
                    ushort target = (ushort)Read(rm, true), segment = CS;
                    if (operation is 3 or 5)
                    {
                        if (rm.Register >= 0) throw Unsupported(op, "puntero lejano requiere memoria");
                        segment = ReadWord(rm.Segment, (ushort)(rm.Offset + 2));
                    }
                    if (operation is 2 or 3) { if (operation == 3) Push(CS); Push(IP); }
                    CS = segment; IP = target;
                }
                break;
            }
            default: throw Unsupported(op);
        }
    }

    private void String(byte opcode, byte repeat)
    {
        if (repeat != 0 && CX == 0) return;
        bool word = (opcode & 1) != 0; int kind = opcode & 0xFE;
        ushort source = _s[_segmentOverride < 0 ? 3 : _segmentOverride];
        var from = new Operand(-1, source, SI); var to = new Operand(-1, ES, DI);
        if (kind == 0xA4) Write(to, word, Read(from, word));
        else if (kind == 0xA6) Alu(7, Read(from, word), Read(to, word), word);
        else if (kind == 0xAA) Write(to, word, GetReg(0, word));
        else if (kind == 0xAC) SetReg(0, word, Read(from, word));
        else Alu(7, GetReg(0, word), Read(to, word), word);
        int delta = (word ? 2 : 1) * (Flag(Direction) ? -1 : 1);
        if (kind is 0xA4 or 0xA6 or 0xAC) SI = (ushort)(SI + delta);
        if (kind is 0xA4 or 0xA6 or 0xAA or 0xAE) DI = (ushort)(DI + delta);
        if (repeat != 0 && --CX != 0 && (kind is not (0xA6 or 0xAE) || Flag(Zero) == (repeat == 0xF3)))
        { IP = _startIp; _repeatAgain = true; }
    }

    private void PortOut(ushort port, byte value) { DataBusCycle(); bus.Out(port, value); }
    private byte PortIn(ushort port) { DataBusCycle(); return bus.In(port); }
    private void Io(ushort port, bool word, bool output)
    {
        if (output) { PortOut(port, (byte)AX); if (word) PortOut((ushort)(port + 1), (byte)(AX >> 8)); }
        else { int low = PortIn(port); SetReg(0, word, word ? low | PortIn((ushort)(port + 1)) << 8 : low); }
    }
}
