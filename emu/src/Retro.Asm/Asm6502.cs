using System.Text.RegularExpressions;
using Retro.Cpu.M6502;

namespace Retro.Asm;

/// <summary>
/// Two-pass 6502 assembler (a small, ca65-like dialect, the same as the Python kit):
/// <code>
///   label:  @local:  name = expr      labels (@ is local to the last label) and constants
///   LDA #$10   LDA a:$0010            immediate; a: (or four hex digits) forces absolute
///   .org $C000  .byte 1,"text",&lt;x  .word x  .text "abc"  .fill n[,v]
/// </code>
/// Expressions: $hex %bin decimal 'c' names * (here) + - * / &amp; | ^ &lt;&lt; &gt;&gt; ~, unary
/// &lt; (low byte) and &gt; (high byte), parentheses. The undocumented opcodes (LAX, SAX, DCP...)
/// are accepted with the names the disassembler gives them.
/// </summary>
public static class Asm6502
{
    public static AsmResult Assemble(string source, ushort origin = 0x0801) => AsmCore.Assemble(source, origin, Set.Instance);

    private sealed class Set : IInstructionSet
    {
        public static readonly Set Instance = new();
        private static readonly Dictionary<(string, M6502Mode), byte> Encode = BuildEncode();
        private static readonly HashSet<string> Branches = new(StringComparer.OrdinalIgnoreCase) { "BPL", "BMI", "BVC", "BVS", "BCC", "BCS", "BNE", "BEQ" };
        private static readonly HashSet<string> Mnemonics = new(Encode.Keys.Select(k => k.Item1), StringComparer.OrdinalIgnoreCase);

        /// <summary>Official opcodes win over the undocumented ones that do the same (SBC #$EB, the many NOPs).</summary>
        private static Dictionary<(string, M6502Mode), byte> BuildEncode()
        {
            var map = new Dictionary<(string, M6502Mode), byte>();
            var unofficial = new HashSet<int> { 0xEB, 0x80, 0x82, 0x89, 0xC2, 0xE2, 0x1A, 0x3A, 0x5A, 0x7A, 0xDA, 0xFA };
            for (int pass = 0; pass < 2; pass++)
                for (int op = 0; op < 256; op++)
                {
                    var (name, mode) = M6502Disassembler.Table[op];
                    bool official = (op & 3) != 3 && name != "JAM" && !unofficial.Contains(op) && !(name == "NOP" && op != 0xEA);
                    if ((pass == 0) != official || name == "JAM")
                        continue;
                    map.TryAdd((name, mode), (byte)op);
                }
            return map;
        }

        public bool Knows(string mnemonic) => Mnemonics.Contains(mnemonic);

        public void Assemble(AsmContext c, string mnem, string operand)
        {
            if (Branches.Contains(mnem))
            {
                int target = c.EvalOrZero(operand, c.Pc);
                int offset = target - (c.Pc + 2);
                if (c.Final && (offset < -128 || offset > 127))
                    throw new FormatException($"el salto de {mnem} a ${target:X4} está fuera de alcance ({offset})");
                c.Emit(Encode[(mnem, M6502Mode.Rel)]);
                c.Emit((byte)offset);
                return;
            }
            var (mode, expr) = Parse(mnem, operand);
            if (expr == null)
            {
                c.Emit(Op(mnem, mode));
                return;
            }
            bool forceAbs = expr.StartsWith("a:", StringComparison.OrdinalIgnoreCase);
            if (forceAbs)
                expr = expr[2..];
            // a literal written with four hex digits ($0034) means absolute, as the disassembler writes it
            if (Regex.IsMatch(expr.Trim(), @"^\$[0-9A-Fa-f]{4}$"))
                forceAbs = true;
            bool known = c.TryEval(expr, out int value);
            // zero page when the value fits and the instruction has that mode; sizes from pass 1 are kept
            M6502Mode? zp = mode switch { M6502Mode.Abs => M6502Mode.Zp, M6502Mode.AbsX => M6502Mode.ZpX, M6502Mode.AbsY => M6502Mode.ZpY, _ => null };
            if (zp is { } z && !forceAbs && Encode.ContainsKey((mnem, z)))
            {
                bool small = known && value is >= 0 and < 256;
                if (c.SizeFromPass1 is int size)
                    small = size == 2;
                if (small || !Encode.ContainsKey((mnem, mode)))
                    mode = z;
            }
            c.RecordSize(M6502Disassembler.Length(mode));
            if (c.Final && !known)
                throw new Unresolved(expr);
            c.Emit(Op(mnem, mode));
            c.Emit((byte)value);
            if (M6502Disassembler.Length(mode) == 3)
                c.Emit((byte)(value >> 8));
        }

        private static byte Op(string mnem, M6502Mode mode) =>
            Encode.TryGetValue((mnem, mode), out byte op) ? op : throw new FormatException($"{mnem} no tiene el modo {mode}");

        private static (M6502Mode mode, string? expr) Parse(string mnem, string operand)
        {
            if (operand.Length == 0)
                return (Encode.ContainsKey((mnem, M6502Mode.Imp)) ? M6502Mode.Imp : M6502Mode.Acc, null);
            if (operand.Equals("A", StringComparison.OrdinalIgnoreCase) && Encode.ContainsKey((mnem, M6502Mode.Acc)))
                return (M6502Mode.Acc, null);
            if (operand.StartsWith('#'))
                return (M6502Mode.Imm, operand[1..].Trim());
            // operands have no strings, so spaces can go: "( $10 , X )" is "($10,X)"
            string o = operand.Replace(" ", "").Replace("\t", "");
            string up = o.ToUpperInvariant();
            if (up.StartsWith('(') && up.EndsWith(",X)"))
                return (M6502Mode.IndX, o[1..^3]);
            if (up.StartsWith('(') && up.EndsWith("),Y"))
                return (M6502Mode.IndY, o[1..^3]);
            if (up.StartsWith('(') && up.EndsWith(')') && Encode.ContainsKey((mnem, M6502Mode.Ind)))
                return (M6502Mode.Ind, o[1..^1]);
            if (up.EndsWith(",X"))
                return (M6502Mode.AbsX, o[..^2]);
            if (up.EndsWith(",Y"))
                return (M6502Mode.AbsY, o[..^2]);
            return (M6502Mode.Abs, operand);
        }
    }
}
