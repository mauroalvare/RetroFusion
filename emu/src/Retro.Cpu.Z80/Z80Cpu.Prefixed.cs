namespace Retro.Cpu.Z80;

public sealed partial class Z80Cpu
{
    // ---------------------------------------------------------------- CB prefix
    private void ExecuteCB(byte op)
    {
        int x = op >> 6, y = (op >> 3) & 7, z = op & 7;
        if (z == 6)
        {
            byte v = Read(HL);
            Contend(HL, 1);
            switch (x)
            {
                case 0: Write(HL, Rotate(y, v)); break;
                case 1: Bit(y, v, WZ >> 8); break;
                case 2: Write(HL, (byte)(v & ~(1 << y))); break;
                default: Write(HL, (byte)(v | (1 << y))); break;
            }
            return;
        }
        byte r = GetReg(z);
        switch (x)
        {
            case 0: SetReg(z, Rotate(y, r)); break;
            case 1: Bit(y, r, r); break;
            case 2: SetReg(z, (byte)(r & ~(1 << y))); break;
            default: SetReg(z, (byte)(r | (1 << y))); break;
        }
    }

    // ---------------------------------------------------------------- ED prefix
    private void ExecuteED(byte op)
    {
        if (op >= 0x40 && op < 0x80)
        {
            ExecuteEDMain(op);
            return;
        }
        switch (op)
        {
            case 0xA0: Ldi(1, false); break;
            case 0xA1: Cpi(1, false); break;
            case 0xA2: Ini(1, false); break;
            case 0xA3: Outi(1, false); break;
            case 0xA8: Ldi(-1, false); break;
            case 0xA9: Cpi(-1, false); break;
            case 0xAA: Ini(-1, false); break;
            case 0xAB: Outi(-1, false); break;
            case 0xB0: Ldi(1, true); break;
            case 0xB1: Cpi(1, true); break;
            case 0xB2: Ini(1, true); break;
            case 0xB3: Outi(1, true); break;
            case 0xB8: Ldi(-1, true); break;
            case 0xB9: Cpi(-1, true); break;
            case 0xBA: Ini(-1, true); break;
            case 0xBB: Outi(-1, true); break;
            default: break; // undefined ED opcodes behave as an 8 T-state NOP
        }
    }

    private void ExecuteEDMain(byte op)
    {
        int y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
        switch (z)
        {
            case 0:                                                              // IN r,(C) / IN (C)
            {
                ushort port = BC;
                WZ = (ushort)(port + 1);
                byte v = In(port);
                F = (byte)((F & FC) | SZ53P[v]);
                Q = F;
                if (y != 6)
                    SetReg(y, v);
                break;
            }
            case 1:                                                              // OUT (C),r / OUT (C),0
                Out(BC, y == 6 ? (byte)0 : GetReg(y));
                WZ = (ushort)(BC + 1);
                break;
            case 2:                                                              // SBC/ADC HL,rp
                Contend(IR, 7);
                if (q == 0) Sbc16(GetRp(p));
                else Adc16(GetRp(p));
                break;
            case 3:                                                              // LD (nn),rp / LD rp,(nn)
            {
                ushort a = ReadWordAtPc();
                if (q == 0)
                {
                    ushort v = GetRp(p);
                    Write(a++, (byte)v);
                    WZ = a;
                    Write(a, (byte)(v >> 8));
                }
                else
                {
                    byte lo = Read(a++);
                    WZ = a;
                    byte hi = Read(a);
                    SetRp(p, (ushort)(lo | (hi << 8)));
                }
                break;
            }
            case 4:                                                              // NEG
            {
                byte v = A;
                A = 0;
                Sub8(v);
                break;
            }
            case 5:                                                              // RETN / RETI
                IFF1 = IFF2;
                Ret();
                break;
            case 6:                                                              // IM 0/1/2
                IM = (y & 3) switch { 0 => 0, 1 => 0, 2 => 1, _ => 2 };
                break;
            default:
                switch (y)
                {
                    case 0: Contend(IR, 1); I = A; break;                        // LD I,A
                    case 1: Contend(IR, 1); R = A; break;                        // LD R,A
                    case 2:                                                      // LD A,I
                        Contend(IR, 1);
                        A = I;
                        F = (byte)((F & FC) | SZ53[A] | (IFF2 ? FV : 0));
                        Q = F;
                        break;
                    case 3:                                                      // LD A,R
                        Contend(IR, 1);
                        A = R;
                        F = (byte)((F & FC) | SZ53[A] | (IFF2 ? FV : 0));
                        Q = F;
                        break;
                    case 4:                                                      // RRD
                    {
                        byte v = Read(HL);
                        Contend(HL, 4);
                        Write(HL, (byte)((A << 4) | (v >> 4)));
                        A = (byte)((A & 0xF0) | (v & 0x0F));
                        F = (byte)((F & FC) | SZ53P[A]);
                        Q = F;
                        WZ = (ushort)(HL + 1);
                        break;
                    }
                    case 5:                                                      // RLD
                    {
                        byte v = Read(HL);
                        Contend(HL, 4);
                        Write(HL, (byte)((v << 4) | (A & 0x0F)));
                        A = (byte)((A & 0xF0) | (v >> 4));
                        F = (byte)((F & FC) | SZ53P[A]);
                        Q = F;
                        WZ = (ushort)(HL + 1);
                        break;
                    }
                    default: break;                                              // ED 77 / ED 7F: NOP
                }
                break;
        }
    }

    private ushort GetRp(int p) => p switch { 0 => BC, 1 => DE, 2 => HL, _ => SP };

    private void SetRp(int p, ushort v)
    {
        switch (p)
        {
            case 0: BC = v; break;
            case 1: DE = v; break;
            case 2: HL = v; break;
            default: SP = v; break;
        }
    }

    // LDI / LDD / LDIR / LDDR
    private void Ldi(int dir, bool repeat)
    {
        byte v = Read(HL);
        BC--;
        Write(DE, v);
        Contend(DE, 2);
        int n = v + A;
        F = (byte)((F & (FC | FZ | FS)) | (BC != 0 ? FV : 0) | (n & F3) | ((n & 0x02) != 0 ? F5 : 0));
        Q = F;
        if (repeat && BC != 0)
        {
            Contend(DE, 5);
            PC -= 2;
            WZ = (ushort)(PC + 1);
        }
        HL = (ushort)(HL + dir);
        DE = (ushort)(DE + dir);
    }

    // CPI / CPD / CPIR / CPDR
    private void Cpi(int dir, bool repeat)
    {
        byte v = Read(HL);
        int t = (A - v) & 0xFF;
        int lookup = ((A & 0x08) >> 3) | ((v & 0x08) >> 2) | ((t & 0x08) >> 1);
        Contend(HL, 5);
        BC--;
        int f = (F & FC) | (BC != 0 ? (FV | FN) : FN) | HalfcarrySub[lookup] | (t != 0 ? 0 : FZ) | (t & FS);
        if ((f & FH) != 0)
            t--;
        f |= (t & F3) | ((t & 0x02) != 0 ? F5 : 0);
        F = (byte)f;
        Q = F;
        if (repeat && (F & (FV | FZ)) == FV)
        {
            Contend(HL, 5);
            PC -= 2;
            WZ = (ushort)(PC + 1);
        }
        else
        {
            WZ = (ushort)(WZ + dir);
        }
        HL = (ushort)(HL + dir);
    }

    // INI / IND / INIR / INDR
    private void Ini(int dir, bool repeat)
    {
        Contend(IR, 1);
        byte v = In(BC);
        Write(HL, v);
        WZ = (ushort)(BC + dir);
        B--;
        int t = v + ((C + dir) & 0xFF);
        F = (byte)(((v & 0x80) != 0 ? FN : 0) | (t > 0xFF ? (FH | FC) : 0)
                   | (Parity[(t & 0x07) ^ B] != 0 ? FP : 0) | SZ53[B]);
        Q = F;
        if (repeat && B != 0)
        {
            Contend(HL, 5);
            PC -= 2;
        }
        HL = (ushort)(HL + dir);
    }

    // OUTI / OUTD / OTIR / OTDR
    private void Outi(int dir, bool repeat)
    {
        Contend(IR, 1);
        byte v = Read(HL);
        B--;
        WZ = (ushort)(BC + dir);
        Out(BC, v);
        HL = (ushort)(HL + dir);
        int t = v + L;
        F = (byte)(((v & 0x80) != 0 ? FN : 0) | (t > 0xFF ? (FH | FC) : 0)
                   | (Parity[(t & 0x07) ^ B] != 0 ? FP : 0) | SZ53[B]);
        Q = F;
        if (repeat && B != 0)
        {
            Contend(BC, 5);
            PC -= 2;
        }
    }

    // ---------------------------------------------------------------- DD / FD prefix
    private void ExecuteIndexed(byte op, ref ushort xy)
    {
        switch (op)
        {
            case 0x09: Contend(IR, 7); xy = Add16(xy, BC); return;
            case 0x19: Contend(IR, 7); xy = Add16(xy, DE); return;
            case 0x29: Contend(IR, 7); xy = Add16(xy, xy); return;
            case 0x39: Contend(IR, 7); xy = Add16(xy, SP); return;
            case 0x21: xy = ReadWordAtPc(); return;
            case 0x22:
            {
                ushort a = ReadWordAtPc();
                Write(a++, (byte)xy);
                WZ = a;
                Write(a, (byte)(xy >> 8));
                return;
            }
            case 0x2A:
            {
                ushort a = ReadWordAtPc();
                byte lo = Read(a++);
                WZ = a;
                byte hi = Read(a);
                xy = (ushort)(lo | (hi << 8));
                return;
            }
            case 0x23: Contend(IR, 2); xy++; return;
            case 0x2B: Contend(IR, 2); xy--; return;
            case 0x24: xy = (ushort)((Inc8((byte)(xy >> 8)) << 8) | (xy & 0xFF)); return;
            case 0x25: xy = (ushort)((Dec8((byte)(xy >> 8)) << 8) | (xy & 0xFF)); return;
            case 0x26: xy = (ushort)((Read(PC++) << 8) | (xy & 0xFF)); return;
            case 0x2C: xy = (ushort)((xy & 0xFF00) | Inc8((byte)xy)); return;
            case 0x2D: xy = (ushort)((xy & 0xFF00) | Dec8((byte)xy)); return;
            case 0x2E: xy = (ushort)((xy & 0xFF00) | Read(PC++)); return;
            case 0x34:
            {
                ushort a = IndexAddress(xy, 5);
                byte v = Read(a);
                Contend(a, 1);
                Write(a, Inc8(v));
                return;
            }
            case 0x35:
            {
                ushort a = IndexAddress(xy, 5);
                byte v = Read(a);
                Contend(a, 1);
                Write(a, Dec8(v));
                return;
            }
            case 0x36:
            {
                sbyte d = (sbyte)Read(PC++);
                byte n = Read(PC);
                Contend(PC, 2);
                PC++;
                ushort a = (ushort)(xy + d);
                WZ = a;
                Write(a, n);
                return;
            }
            case 0xCB:
            {
                sbyte d = (sbyte)Read(PC++);
                ushort a = (ushort)(xy + d);
                byte op3 = Read(PC);
                Contend(PC, 2);
                PC++;
                ExecuteIndexedCB(op3, a);
                return;
            }
            case 0xE1: xy = Pop(); return;
            case 0xE3:
            {
                byte lo = Read(SP);
                byte hi = Read((ushort)(SP + 1));
                Contend((ushort)(SP + 1), 1);
                Write((ushort)(SP + 1), (byte)(xy >> 8));
                Write(SP, (byte)xy);
                Contend(SP, 2);
                xy = (ushort)(lo | (hi << 8));
                WZ = xy;
                return;
            }
            case 0xE5: Contend(IR, 1); Push(xy); return;
            case 0xE9: PC = xy; return;
            case 0xF9: Contend(IR, 2); SP = xy; return;
        }

        // LD r,r' group with IXH/IXL or (IX+d)
        if (op >= 0x40 && op < 0x80 && op != 0x76)
        {
            int dst = (op >> 3) & 7, src = op & 7;
            if (src == 6)
            {
                ushort a = IndexAddress(xy, 5);
                SetReg(dst, Read(a));                        // H and L stay H and L here
                return;
            }
            if (dst == 6)
            {
                ushort a = IndexAddress(xy, 5);
                Write(a, GetReg(src));
                return;
            }
            if (dst == 4 || dst == 5 || src == 4 || src == 5)
            {
                byte v = GetIndexedReg(src, xy);
                SetIndexedReg(dst, v, ref xy);
                return;
            }
        }
        // ALU group
        else if (op >= 0x80 && op < 0xC0)
        {
            int src = op & 7;
            if (src == 6)
            {
                ushort a = IndexAddress(xy, 5);
                Alu((op >> 3) & 7, Read(a));
                return;
            }
            if (src == 4 || src == 5)
            {
                Alu((op >> 3) & 7, GetIndexedReg(src, xy));
                return;
            }
        }

        // The prefix does not apply to this opcode: the prefix cost 4 T-states and the
        // already fetched opcode runs as a normal instruction.
        ExecuteBase(op);
    }

    /// <summary>Reads the displacement, spends the internal cycles, sets MEMPTR to IX+d.</summary>
    private ushort IndexAddress(ushort xy, int internalCycles)
    {
        sbyte d = (sbyte)Read(PC);
        Contend(PC, internalCycles);
        PC++;
        ushort a = (ushort)(xy + d);
        WZ = a;
        return a;
    }

    private byte GetIndexedReg(int r, ushort xy) => r switch
    {
        4 => (byte)(xy >> 8),
        5 => (byte)xy,
        _ => GetReg(r),
    };

    private void SetIndexedReg(int r, byte v, ref ushort xy)
    {
        if (r == 4) xy = (ushort)((v << 8) | (xy & 0xFF));
        else if (r == 5) xy = (ushort)((xy & 0xFF00) | v);
        else SetReg(r, v);
    }

    private void ExecuteIndexedCB(byte op, ushort a)
    {
        int x = op >> 6, y = (op >> 3) & 7, z = op & 7;
        WZ = a;
        byte v = Read(a);
        Contend(a, 1);
        if (x == 1)
        {
            Bit(y, v, a >> 8);
            return;
        }
        byte result = x switch
        {
            0 => Rotate(y, v),
            2 => (byte)(v & ~(1 << y)),
            _ => (byte)(v | (1 << y)),
        };
        Write(a, result);
        if (z != 6)
            SetReg(z, result);   // undocumented: the result is also copied to a register
    }
}
