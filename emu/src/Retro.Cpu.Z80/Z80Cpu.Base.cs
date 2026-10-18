namespace Retro.Cpu.Z80;

public sealed partial class Z80Cpu
{
    private void ExecuteBase(byte op)
    {
        // LD r,r' / LD r,(HL) / LD (HL),r / HALT
        if (op >= 0x40 && op < 0x80)
        {
            if (op == 0x76)
            {
                Halted = true;
                PC--;
                return;
            }
            int dst = (op >> 3) & 7, src = op & 7;
            if (src == 6)
                SetReg(dst, Read(HL));
            else if (dst == 6)
                Write(HL, GetReg(src));
            else
                SetReg(dst, GetReg(src));
            return;
        }
        // ALU A,r / ALU A,(HL)
        if (op >= 0x80 && op < 0xC0)
        {
            int src = op & 7;
            Alu((op >> 3) & 7, src == 6 ? Read(HL) : GetReg(src));
            return;
        }

        switch (op)
        {
            case 0x00: break;                                                    // NOP
            case 0x01: C = Read(PC++); B = Read(PC++); break;                    // LD BC,nn
            case 0x02: Write(BC, A); WZ = (ushort)(((BC + 1) & 0xFF) | (A << 8)); break;
            case 0x03: Contend(IR, 2); BC++; break;
            case 0x04: B = Inc8(B); break;
            case 0x05: B = Dec8(B); break;
            case 0x06: B = Read(PC++); break;
            case 0x07:                                                           // RLCA
                A = (byte)((A << 1) | (A >> 7));
                F = (byte)((F & (FP | FZ | FS)) | (A & (FC | F3 | F5)));
                Q = F;
                break;
            case 0x08: (A, A_) = (A_, A); (F, F_) = (F_, F); break;              // EX AF,AF'
            case 0x09: Contend(IR, 7); HL = Add16(HL, BC); break;
            case 0x0A: WZ = (ushort)(BC + 1); A = Read(BC); break;
            case 0x0B: Contend(IR, 2); BC--; break;
            case 0x0C: C = Inc8(C); break;
            case 0x0D: C = Dec8(C); break;
            case 0x0E: C = Read(PC++); break;
            case 0x0F:                                                           // RRCA
                F = (byte)((F & (FP | FZ | FS)) | (A & FC));
                A = (byte)((A >> 1) | (A << 7));
                F |= (byte)(A & (F3 | F5));
                Q = F;
                break;

            case 0x10:                                                           // DJNZ e
                Contend(IR, 1);
                B--;
                if (B != 0) Jr();
                else { ContendRead(PC, 3); PC++; }
                break;
            case 0x11: E = Read(PC++); D = Read(PC++); break;
            case 0x12: Write(DE, A); WZ = (ushort)(((DE + 1) & 0xFF) | (A << 8)); break;
            case 0x13: Contend(IR, 2); DE++; break;
            case 0x14: D = Inc8(D); break;
            case 0x15: D = Dec8(D); break;
            case 0x16: D = Read(PC++); break;
            case 0x17:                                                           // RLA
            {
                int old = A;
                A = (byte)((A << 1) | (F & FC));
                F = (byte)((F & (FP | FZ | FS)) | (A & (F3 | F5)) | (old >> 7));
                Q = F;
                break;
            }
            case 0x18: Jr(); break;
            case 0x19: Contend(IR, 7); HL = Add16(HL, DE); break;
            case 0x1A: WZ = (ushort)(DE + 1); A = Read(DE); break;
            case 0x1B: Contend(IR, 2); DE--; break;
            case 0x1C: E = Inc8(E); break;
            case 0x1D: E = Dec8(E); break;
            case 0x1E: E = Read(PC++); break;
            case 0x1F:                                                           // RRA
            {
                int old = A;
                A = (byte)((A >> 1) | (F << 7));
                F = (byte)((F & (FP | FZ | FS)) | (A & (F3 | F5)) | (old & FC));
                Q = F;
                break;
            }

            case 0x20: case 0x28: case 0x30: case 0x38:                          // JR cc,e
                if (Condition((op >> 3) & 3)) Jr();
                else { ContendRead(PC, 3); PC++; }
                break;
            case 0x21: L = Read(PC++); H = Read(PC++); break;
            case 0x22:                                                           // LD (nn),HL
            {
                ushort a = ReadWordAtPc();
                Write(a++, L);
                WZ = a;
                Write(a, H);
                break;
            }
            case 0x23: Contend(IR, 2); HL++; break;
            case 0x24: H = Inc8(H); break;
            case 0x25: H = Dec8(H); break;
            case 0x26: H = Read(PC++); break;
            case 0x27: Daa(); break;
            case 0x29: Contend(IR, 7); HL = Add16(HL, HL); break;
            case 0x2A:                                                           // LD HL,(nn)
            {
                ushort a = ReadWordAtPc();
                L = Read(a++);
                WZ = a;
                H = Read(a);
                break;
            }
            case 0x2B: Contend(IR, 2); HL--; break;
            case 0x2C: L = Inc8(L); break;
            case 0x2D: L = Dec8(L); break;
            case 0x2E: L = Read(PC++); break;
            case 0x2F:                                                           // CPL
                A ^= 0xFF;
                F = (byte)((F & (FC | FP | FZ | FS)) | (A & (F3 | F5)) | FN | FH);
                Q = F;
                break;

            case 0x31: SP = ReadWordAtPc(); break;
            case 0x32:                                                           // LD (nn),A
            {
                ushort a = ReadWordAtPc();
                Write(a++, A);
                WZ = (ushort)((a & 0xFF) | (A << 8));
                break;
            }
            case 0x33: Contend(IR, 2); SP++; break;
            case 0x34:                                                           // INC (HL)
            {
                byte v = Read(HL);
                Contend(HL, 1);
                Write(HL, Inc8(v));
                break;
            }
            case 0x35:                                                           // DEC (HL)
            {
                byte v = Read(HL);
                Contend(HL, 1);
                Write(HL, Dec8(v));
                break;
            }
            case 0x36: { byte n = Read(PC++); Write(HL, n); break; }             // LD (HL),n
            case 0x37:                                                           // SCF
                F = (byte)((F & (FP | FZ | FS)) | (((_prevQ ^ F) | A) & (F3 | F5)) | FC);
                Q = F;
                break;
            case 0x39: Contend(IR, 7); HL = Add16(HL, SP); break;
            case 0x3A:                                                           // LD A,(nn)
            {
                ushort a = ReadWordAtPc();
                WZ = (ushort)(a + 1);
                A = Read(a);
                break;
            }
            case 0x3B: Contend(IR, 2); SP--; break;
            case 0x3C: A = Inc8(A); break;
            case 0x3D: A = Dec8(A); break;
            case 0x3E: A = Read(PC++); break;
            case 0x3F:                                                           // CCF
                F = (byte)((F & (FP | FZ | FS)) | ((F & FC) != 0 ? FH : FC) | (((_prevQ ^ F) | A) & (F3 | F5)));
                Q = F;
                break;

            case 0xC0: case 0xC8: case 0xD0: case 0xD8:                          // RET cc
            case 0xE0: case 0xE8: case 0xF0: case 0xF8:
                Contend(IR, 1);
                if (Condition((op >> 3) & 7)) Ret();
                break;
            case 0xC1: BC = Pop(); break;
            case 0xD1: DE = Pop(); break;
            case 0xE1: HL = Pop(); break;
            case 0xF1: { byte f = Read(SP++); A = Read(SP++); F = f; break; }  // POP AF (Q stays 0)

            case 0xC2: case 0xCA: case 0xD2: case 0xDA:                          // JP cc,nn
            case 0xE2: case 0xEA: case 0xF2: case 0xFA:
                if (Condition((op >> 3) & 7)) Jp();
                else
                {
                    // The address is still read (and ends up in MEMPTR).
                    byte lo = Read(PC);
                    byte hi = Read((ushort)(PC + 1));
                    WZ = (ushort)(lo | (hi << 8));
                    PC += 2;
                }
                break;
            case 0xC3: Jp(); break;
            case 0xC4: case 0xCC: case 0xD4: case 0xDC:                          // CALL cc,nn
            case 0xE4: case 0xEC: case 0xF4: case 0xFC:
                if (Condition((op >> 3) & 7)) Call();
                else
                {
                    byte lo = Read(PC);
                    byte hi = Read((ushort)(PC + 1));
                    WZ = (ushort)(lo | (hi << 8));
                    PC += 2;
                }
                break;
            case 0xC5: Contend(IR, 1); Push(BC); break;
            case 0xD5: Contend(IR, 1); Push(DE); break;
            case 0xE5: Contend(IR, 1); Push(HL); break;
            case 0xF5: Contend(IR, 1); Push(AF); break;
            case 0xC6: case 0xCE: case 0xD6: case 0xDE:                          // ALU A,n
            case 0xE6: case 0xEE: case 0xF6: case 0xFE:
                Alu((op >> 3) & 7, Read(PC++));
                break;
            case 0xC7: case 0xCF: case 0xD7: case 0xDF:                          // RST p
            case 0xE7: case 0xEF: case 0xF7: case 0xFF:
                Contend(IR, 1);
                Rst((ushort)(op & 0x38));
                break;
            case 0xC9: Ret(); break;
            case 0xCB: ExecuteCB(FetchOpcode()); break;
            case 0xCD: Call(); break;
            case 0xD3:                                                           // OUT (n),A
            {
                byte n = Read(PC++);
                ushort port = (ushort)(n | (A << 8));
                Out(port, A);
                WZ = (ushort)(((n + 1) & 0xFF) | (A << 8));
                break;
            }
            case 0xD9:                                                           // EXX
                (B, B_) = (B_, B); (C, C_) = (C_, C);
                (D, D_) = (D_, D); (E, E_) = (E_, E);
                (H, H_) = (H_, H); (L, L_) = (L_, L);
                break;
            case 0xDB:                                                           // IN A,(n)
            {
                byte n = Read(PC++);
                ushort port = (ushort)(n | (A << 8));
                A = In(port);
                WZ = (ushort)(port + 1);
                break;
            }
            case 0xDD: ExecuteIndexed(FetchOpcode(), ref IX); break;
            case 0xE3:                                                           // EX (SP),HL
            {
                byte lo = Read(SP);
                byte hi = Read((ushort)(SP + 1));
                Contend((ushort)(SP + 1), 1);
                Write((ushort)(SP + 1), H);
                Write(SP, L);
                Contend(SP, 2);
                L = lo;
                H = hi;
                WZ = HL;
                break;
            }
            case 0xE9: PC = HL; break;                                           // JP (HL)
            case 0xEB: (D, H) = (H, D); (E, L) = (L, E); break;                  // EX DE,HL
            case 0xED: ExecuteED(FetchOpcode()); break;
            case 0xF3: IFF1 = IFF2 = false; break;                               // DI
            case 0xF9: Contend(IR, 2); SP = HL; break;                           // LD SP,HL
            case 0xFB: IFF1 = IFF2 = true; _eiDelay = true; break;               // EI
            case 0xFD: ExecuteIndexed(FetchOpcode(), ref IY); break;
            default: throw new InvalidOperationException($"opcode sin decodificar {op:X2}");
        }
    }
}
