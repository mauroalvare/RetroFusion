using Retro.Cpu.Z80;

namespace Retro.Asm;

/// <summary>
/// Two-pass Z80 assembler in Zilog syntax (LD A,(IX+5), JR NZ,loop, EX AF,AF'...), with the
/// undocumented instructions (IXH, SLL...) and the same labels, constants, directives and
/// expressions as the 6502 one, plus the usual Z80 spellings: ORG, DB/DEFB/DEFM, DW/DEFW,
/// DS/DEFS, name EQU expr, $ for "here" and 0FFh hex.
/// <para>
/// The instruction table is not typed by hand: every opcode (plain, CB, ED, DD/FD and
/// DDCB/FDCB) is disassembled once with marker bytes, and the text becomes a template
/// ("LD (IX+$5A),$3C" → "LD (IX+d),n"). So the assembler knows exactly what the
/// disassembler shows, and the round trip holds by construction.
/// </para>
/// </summary>
public static class AsmZ80
{
    public static AsmResult Assemble(string source, ushort origin = 0x8000) => AsmCore.Assemble(source, origin, Set.Instance);

    private enum Slot { Fixed, N, NnLo, NnHi, D, E }

    private sealed record Part(Slot Kind, byte Value = 0);

    private sealed record Template(string Mnemonic, string[] Operands, Part[] Parts)
    {
        public int Length => Parts.Length;
    }

    private const byte M1 = 0x5A, M2 = 0x3C;
    private const ushort At = 0x8000;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "A", "B", "C", "D", "E", "H", "L", "I", "R", "F", "AF", "BC", "DE", "HL", "SP", "IX", "IY", "IXH", "IXL", "IYH", "IYL", "AF'",
        "NZ", "Z", "NC", "PO", "PE", "P", "M",
    };

    private sealed class Set : IInstructionSet
    {
        public static readonly Set Instance = new();
        private readonly Dictionary<string, List<Template>> _byMnemonic = new(StringComparer.OrdinalIgnoreCase);

        private Set()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Try(byte[] fixedBytes, int dispAt = -1)
            {
                var t = Build(fixedBytes, dispAt);
                if (t == null)
                    return;
                string key = t.Mnemonic + " " + string.Join(",", t.Operands);
                if (!seen.Add(key))
                    return;
                if (!_byMnemonic.TryGetValue(t.Mnemonic, out var list))
                    _byMnemonic[t.Mnemonic] = list = new List<Template>();
                list.Add(t);
            }
            var prefixes = new HashSet<int> { 0xCB, 0xDD, 0xED, 0xFD };
            for (int op = 0; op < 256; op++)
                if (!prefixes.Contains(op)) Try(new[] { (byte)op });
            for (int op = 0; op < 256; op++) Try(new byte[] { 0xCB, (byte)op });
            for (int op = 0; op < 256; op++) Try(new byte[] { 0xED, (byte)op });
            foreach (byte ix in new byte[] { 0xDD, 0xFD })
            {
                for (int op = 0; op < 256; op++)
                    if (!prefixes.Contains(op)) Try(new[] { ix, (byte)op });
                for (int op = 0; op < 256; op++)
                    Try(new[] { ix, (byte)0xCB, (byte)op }, dispAt: 2);
            }
        }

        /// <summary>
        /// The template of one encoding: disassemble it with M1 and M2 in the operand bytes
        /// and turn those values back into placeholders. <paramref name="dispAt"/> is where the
        /// displacement goes when it comes before the last opcode byte (DDCB d op).
        /// </summary>
        private static Template? Build(byte[] fixedBytes, int dispAt)
        {
            var buf = new byte[8];
            int nFixed = fixedBytes.Length;
            if (dispAt >= 0)
            {
                // DD CB d op: the displacement sits between the prefixes and the opcode
                buf[0] = fixedBytes[0];
                buf[1] = fixedBytes[1];
                buf[2] = M1;
                buf[3] = fixedBytes[2];
            }
            else
            {
                fixedBytes.CopyTo(buf, 0);
                buf[nFixed] = M1;
                buf[nFixed + 1] = M2;
            }
            var d = new Z80Disassembler().Disassemble(a => a - At < buf.Length ? buf[a - At] : (byte)0, At);
            string text = d.Text;
            if (text.Contains('*') || text.StartsWith("DB") || text.Contains('?'))
                return null;
            int len = d.Length;
            var parts = new Part[len];
            if (dispAt >= 0)
            {
                if (len != 4 || !text.Contains($"+${M1:X2})"))
                    return null;
                parts[0] = new Part(Slot.Fixed, buf[0]);
                parts[1] = new Part(Slot.Fixed, buf[1]);
                parts[2] = new Part(Slot.D);
                parts[3] = new Part(Slot.Fixed, buf[3]);
                text = text.Replace($"+${M1:X2})", "+d)");
            }
            else
            {
                for (int i = 0; i < nFixed; i++)
                    parts[i] = new Part(Slot.Fixed, buf[i]);
                int operands = len - nFixed;
                string nn = $"${M2:X2}{M1:X2}", rel = $"${At + nFixed + 1 + (sbyte)M1:X4}";
                if (operands == 2 && text.Contains(nn))
                {
                    text = text.Replace(nn, "nn");
                    parts[nFixed] = new Part(Slot.NnLo);
                    parts[nFixed + 1] = new Part(Slot.NnHi);
                }
                else if (operands == 2 && text.Contains($"+${M1:X2})") && text.Contains($"${M2:X2}"))
                {
                    // LD (IX+d),n
                    text = text.Replace($"+${M1:X2})", "+d)").Replace($"${M2:X2}", "n");
                    parts[nFixed] = new Part(Slot.D);
                    parts[nFixed + 1] = new Part(Slot.N);
                }
                else if (operands == 1 && text.Contains($"+${M1:X2})"))
                {
                    text = text.Replace($"+${M1:X2})", "+d)");
                    parts[nFixed] = new Part(Slot.D);
                }
                else if (operands == 1 && (text.StartsWith("JR") || text.StartsWith("DJNZ")) && text.Contains(rel))
                {
                    text = text.Replace(rel, "e");
                    parts[nFixed] = new Part(Slot.E);
                }
                else if (operands == 1 && text.Contains($"${M1:X2}"))
                {
                    text = text.Replace($"${M1:X2}", "n");
                    parts[nFixed] = new Part(Slot.N);
                }
                else if (operands != 0)
                    return null;
            }
            int sp = text.IndexOf(' ');
            string mnemonic = sp < 0 ? text : text[..sp];
            string[] ops = sp < 0 ? Array.Empty<string>() : AsmText.SplitArgs(text[(sp + 1)..]).ToArray();
            return new Template(mnemonic, ops, parts);
        }

        public bool Knows(string mnemonic) => _byMnemonic.ContainsKey(mnemonic);

        public void Assemble(AsmContext c, string mnem, string operand)
        {
            var given = AsmText.SplitArgs(operand).Select(o => o.Trim()).ToList();
            foreach (var t in _byMnemonic[mnem])
            {
                if (t.Operands.Length != given.Count)
                    continue;
                var values = new Dictionary<Slot, string>();
                bool ok = true;
                for (int i = 0; i < given.Count && ok; i++)
                    ok = Match(c, t.Operands[i], given[i], values);
                if (!ok)
                    continue;
                c.RecordSize(t.Length);
                Emit(c, t, values);
                return;
            }
            throw new FormatException($"{mnem} {operand} no es una forma válida del Z80");
        }

        private static bool Wrapped(string s)
        {
            if (s.Length < 2 || s[0] != '(' || s[^1] != ')')
                return false;
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')') depth--;
                if (depth == 0 && i < s.Length - 1)
                    return false;
            }
            return true;
        }

        /// <summary>A number, label or calculation: not a register, not in parentheses, and naming no register (IX+5 is not one).</summary>
        private static bool IsExpression(string s) =>
            s.Length > 0 && !Reserved.Contains(s) && !Wrapped(s) &&
            !System.Text.RegularExpressions.Regex.Matches(s, @"[A-Za-z_][A-Za-z0-9_']*").Any(m => Reserved.Contains(m.Value) && !IsHexLiteral(s, m.Index));

        /// <summary>The letters of $FE or 0FFh are digits, not the registers F or E.</summary>
        private static bool IsHexLiteral(string s, int at)
        {
            int i = at;
            while (i > 0 && Uri.IsHexDigit(s[i - 1])) i--;
            return (i > 0 && s[i - 1] == '$') || (i < s.Length && char.IsDigit(s[i])) || (i > 1 && s[i - 1] == 'x' && s[i - 2] == '0');
        }

        /// <summary>Does the written operand fit the template's operand? Placeholders keep the expression.</summary>
        private static bool Match(AsmContext c, string t, string s, Dictionary<Slot, string> values)
        {
            string compact = s.Replace(" ", "").Replace("\t", "");
            switch (t)
            {
                case "n" when IsExpression(s):
                    values[Slot.N] = s;
                    return true;
                case "nn" when IsExpression(s):
                    values[Slot.NnLo] = s;
                    return true;
                case "e" when IsExpression(s):
                    values[Slot.E] = s;
                    return true;
                case "(n)" when Wrapped(compact) && IsExpression(compact[1..^1]):
                    values[Slot.N] = compact[1..^1];
                    return true;
                case "(nn)" when Wrapped(compact) && IsExpression(compact[1..^1]):
                    values[Slot.NnLo] = compact[1..^1];
                    return true;
            }
            if (t is "(IX+d)" or "(IY+d)")
            {
                if (!Wrapped(compact))
                    return false;
                string inner = compact[1..^1];
                string reg = t[1..3];
                if (!inner.StartsWith(reg, StringComparison.OrdinalIgnoreCase))
                    return false;
                string rest = inner[2..];
                if (rest.Length > 0 && rest[0] != '+' && rest[0] != '-')
                    return false;
                values[Slot.D] = rest.Length == 0 ? "0" : rest[0] == '+' ? rest[1..] : rest;
                return true;
            }
            if (t is "n" or "nn" or "e" or "(n)" or "(nn)")
                return false;
            // fixed numbers in the template: RST $38, IM 1, BIT 3,A
            if (t.Length > 0 && (t[0] == '$' || char.IsDigit(t[0])) && !t.Contains('/'))
                return !Reserved.Contains(s) && c.TryEval(s, out int v) && c.TryEval(t, out int want) && v == want;
            return string.Equals(compact, t, StringComparison.OrdinalIgnoreCase);
        }

        private static void Emit(AsmContext c, Template t, Dictionary<Slot, string> values)
        {
            int start = c.Pc;
            foreach (var p in t.Parts)
            {
                switch (p.Kind)
                {
                    case Slot.Fixed:
                        c.Emit(p.Value);
                        break;
                    case Slot.N:
                    {
                        int v = c.EvalOrZero(values[Slot.N]);
                        if (c.Final && (v < -128 || v > 255))
                            throw new FormatException($"{v} no entra en un byte");
                        c.Emit((byte)v);
                        break;
                    }
                    case Slot.NnLo:
                        c.Emit((byte)c.EvalOrZero(values[Slot.NnLo]));
                        break;
                    case Slot.NnHi:
                        c.Emit((byte)(c.EvalOrZero(values[Slot.NnLo]) >> 8));
                        break;
                    case Slot.D:
                    {
                        int v = c.EvalOrZero(values[Slot.D]);
                        if (c.Final && (v < -128 || v > 127))
                            throw new FormatException($"el desplazamiento {v} está fuera de -128..127");
                        c.Emit((byte)v);
                        break;
                    }
                    case Slot.E:
                    {
                        int target = c.EvalOrZero(values[Slot.E], start + t.Length);
                        int offset = target - (start + t.Length);
                        if (c.Final && (offset < -128 || offset > 127))
                            throw new FormatException($"el salto relativo a ${target:X4} está fuera de alcance ({offset})");
                        c.Emit((byte)offset);
                        break;
                    }
                }
            }
        }
    }

}
