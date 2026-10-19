using System.Globalization;
using System.Text;

namespace Retro.Asm;

/// <summary>A mistake in the source, with the line where it is.</summary>
public sealed record AsmError(int Line, string Message)
{
    public override string ToString() => $"línea {Line}: {Message}";
}

/// <summary>What assembling gives: the bytes from <see cref="Origin"/>, the symbols, the errors and a listing.</summary>
public sealed class AsmResult
{
    public ushort Origin { get; set; }
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public Dictionary<string, int> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AsmError> Errors { get; } = new();
    public List<string> Listing { get; } = new();
    public bool Ok => Errors.Count == 0;
}

/// <summary>The instructions of one CPU; the rest (labels, directives, expressions, passes) is shared.</summary>
internal interface IInstructionSet
{
    bool Knows(string mnemonic);
    void Assemble(AsmContext c, string mnemonic, string operand);
}

/// <summary>A symbol that is not defined (yet): pass 1 goes on, pass 2 reports it.</summary>
internal sealed class Unresolved : Exception
{
    public Unresolved(string name) : base(name) { }
}

/// <summary>
/// The state of one pass: the address, the bytes, the symbols. Pass 1 learns symbols and
/// the size of every instruction; pass 2 knows all symbols and keeps those sizes, so labels
/// never move between passes.
/// </summary>
internal sealed class AsmContext
{
    public AsmContext(ushort origin)
    {
        Pc = origin;
        Start = origin;
    }

    public int Pc;
    public ushort Start;
    private bool _started;
    public readonly List<byte> Output = new();
    public readonly Dictionary<string, int> Symbols = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int>? Known;
    public readonly Dictionary<int, int> Sizes = new();
    public Dictionary<int, int>? FixedSizes;
    public readonly List<AsmError> Errors = new();
    public readonly List<string> Listing = new();
    private string _scope = "";
    public int Line { get; private set; }
    public bool Final { get; private set; }

    public void Run(string[] lines, IInstructionSet set, bool final)
    {
        Final = final;
        for (int i = 0; i < lines.Length; i++)
        {
            Line = i + 1;
            try
            {
                DoLine(lines[i], set);
            }
            catch (Unresolved u) when (final)
            {
                Errors.Add(new AsmError(Line, $"símbolo sin definir: {u.Message}"));
            }
            catch (FormatException e)
            {
                Errors.Add(new AsmError(Line, e.Message));
            }
        }
    }

    public void Emit(byte b)
    {
        if (!_started)
        {
            _started = true;
            Start = (ushort)Pc;
        }
        Output.Add(b);
        Pc++;
    }

    /// <summary>The size pass 1 gave this line, when there is one.</summary>
    public int? SizeFromPass1 => FixedSizes != null && FixedSizes.TryGetValue(Line, out int s) ? s : null;

    public void RecordSize(int size)
    {
        if (!Final)
            Sizes[Line] = size;
    }

    private string Qualify(string name) => name.StartsWith('@') ? _scope + name : name;

    private void Define(string name, int value, bool label)
    {
        string full = Qualify(name);
        if (label && !name.StartsWith('@'))
            _scope = name;
        if (!Final && Symbols.ContainsKey(full))
            throw new FormatException($"\"{full}\" ya está definido");
        Symbols[full] = value;
    }

    /// <summary>Where the line being assembled starts: what $ and * mean.</summary>
    private int _lineStart;

    private void DoLine(string raw, IInstructionSet set)
    {
        string text = AsmText.StripComment(raw).Trim();
        int before = Pc, outBefore = Output.Count;
        _lineStart = Pc;
        while (true)
        {
            int colon = text.IndexOf(':');
            if (colon <= 0 || !AsmText.IsName(text[..colon]))
                break;
            Define(text[..colon], Pc, label: true);
            text = text[(colon + 1)..].Trim();
        }
        if (text.Length == 0)
            return;
        int eq = text.IndexOf('=');
        if (eq > 0 && AsmText.IsName(text[..eq].Trim()) && !text.StartsWith('.'))
        {
            Define(text[..eq].Trim(), EvalOrZero(text[(eq + 1)..]), label: false);
            return;
        }
        // name EQU expr (Z80 style)
        var parts = text.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[1].Equals("EQU", StringComparison.OrdinalIgnoreCase) && AsmText.IsName(parts[0]))
        {
            Define(parts[0], EvalOrZero(parts[2]), label: false);
            return;
        }
        int sp = text.IndexOfAny(new[] { ' ', '\t' });
        string word = sp < 0 ? text : text[..sp];
        string rest = sp < 0 ? "" : text[(sp + 1)..].Trim();
        if (word.StartsWith('.') || Directives.Contains(word))
            Directive(word.TrimStart('.').ToLowerInvariant(), rest);
        else if (set.Knows(word))
            set.Assemble(this, word.ToUpperInvariant(), rest);
        else
            throw new FormatException($"instrucción desconocida: {word.ToUpperInvariant()}");
        if (Final && Output.Count > outBefore)
            Listing.Add($"{before:X4}  {string.Join(" ", Output.Skip(outBefore).Take(8).Select(b => b.ToString("X2"))),-24}{raw.TrimEnd()}");
    }

    /// <summary>Directives also written without the dot (Z80 assemblers: ORG, DB, DEFB, DW, DEFS...).</summary>
    private static readonly HashSet<string> Directives = new(StringComparer.OrdinalIgnoreCase)
        { "ORG", "DB", "DEFB", "DEFM", "DW", "DEFW", "DS", "DEFS" };

    private void Directive(string dir, string args)
    {
        switch (dir)
        {
            case "org":
            {
                int target = Eval(args);
                _lineStart = target;
                if (_started && target < Pc)
                    throw new FormatException($".org ${target:X4} va para atrás (ya se llegó a ${Pc:X4})");
                if (_started)
                    while (Pc < target) Emit(0);
                else
                    Pc = target;
                break;
            }
            case "byte":
            case "db":
            case "defb":
            case "defm":
            case "text":
                foreach (var a in AsmText.SplitArgs(args))
                {
                    if (a.Length >= 2 && a[0] == '"' && a[^1] == '"')
                        foreach (char ch in AsmText.Unescape(a[1..^1])) Emit((byte)ch);
                    else
                        Emit((byte)(EvalOrZero(a) & 0xFF));
                }
                break;
            case "word":
            case "dw":
            case "defw":
                foreach (var a in AsmText.SplitArgs(args))
                {
                    int v = EvalOrZero(a);
                    Emit((byte)v);
                    Emit((byte)(v >> 8));
                }
                break;
            case "fill":
            case "res":
            case "ds":
            case "defs":
            {
                var p = AsmText.SplitArgs(args);
                int n = Eval(p[0]);
                byte v = p.Count > 1 ? (byte)EvalOrZero(p[1]) : (byte)0;
                for (int i = 0; i < n; i++) Emit(v);
                break;
            }
            default:
                throw new FormatException($"directiva desconocida: .{dir}");
        }
    }

    /// <summary>The value, 0 when pass 1 does not know it yet (pass 2 reports the missing symbol).</summary>
    public int EvalOrZero(string expr, int whenUnknown = 0)
    {
        if (TryEval(expr, out int v))
            return v;
        if (Final)
            throw new Unresolved(expr.Trim());
        return whenUnknown;
    }

    public int Eval(string expr) => TryEval(expr, out int v) ? v : throw new FormatException($"no se puede calcular \"{expr.Trim()}\" en este lugar");

    public bool TryEval(string expr, out int value)
    {
        try
        {
            value = new ExprParser(expr, this).Parse();
            return true;
        }
        catch (Unresolved)
        {
            value = 0;
            return false;
        }
    }

    public int Lookup(string name)
    {
        if (name == "*" || name == "$")
            return _lineStart;
        string full = Qualify(name);
        if (Symbols.TryGetValue(full, out int v))
            return v;
        if (Known != null && Known.TryGetValue(full, out v))
            return v;
        throw new Unresolved(full);
    }
}

internal static class AsmCore
{
    public static AsmResult Assemble(string source, ushort origin, IInstructionSet set)
    {
        var lines = source.Replace("\r", "").Split('\n');
        var c = new AsmContext(origin);
        c.Run(lines, set, final: false);
        var result = new AsmResult();
        if (c.Errors.Count > 0)
        {
            result.Errors.AddRange(c.Errors);
            return result;
        }
        var known = new Dictionary<string, int>(c.Symbols, StringComparer.OrdinalIgnoreCase);
        c = new AsmContext(origin) { Known = known, FixedSizes = c.Sizes };
        c.Run(lines, set, final: true);
        result.Errors.AddRange(c.Errors);
        result.Origin = c.Start;
        result.Bytes = c.Output.ToArray();
        foreach (var (k, v) in c.Symbols) result.Symbols[k] = v;
        result.Listing.AddRange(c.Listing);
        return result;
    }
}

/// <summary>Recursive descent over the expression grammar, with the usual precedence.</summary>
internal sealed class ExprParser
{
    private readonly string _s;
    private readonly AsmContext _c;
    private int _i;

    public ExprParser(string s, AsmContext c)
    {
        _s = s.Trim();
        _c = c;
    }

    public int Parse()
    {
        int v = Or();
        Skip();
        if (_i < _s.Length)
            throw new FormatException($"sobra \"{_s[_i..]}\" en la expresión \"{_s}\"");
        return v;
    }

    private void Skip()
    {
        while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
    }

    private bool Eat(string op)
    {
        Skip();
        if (string.CompareOrdinal(_s, _i, op, 0, op.Length) != 0)
            return false;
        _i += op.Length;
        return true;
    }

    private int Or() { int v = Xor(); while (Eat("|")) v |= Xor(); return v; }
    private int Xor() { int v = And(); while (Eat("^")) v ^= And(); return v; }
    private int And() { int v = Shift(); while (Eat("&")) v &= Shift(); return v; }

    private int Shift()
    {
        int v = Add();
        while (true)
        {
            if (Eat("<<")) v <<= Add();
            else if (Eat(">>")) v >>= Add();
            else return v;
        }
    }

    private int Add()
    {
        int v = Mul();
        while (true)
        {
            if (Eat("+")) v += Mul();
            else if (Eat("-")) v -= Mul();
            else return v;
        }
    }

    private int Mul()
    {
        int v = Unary();
        while (true)
        {
            if (Eat("*")) v *= Unary();
            else if (Eat("/")) { int d = Unary(); v = d == 0 ? throw new FormatException("división por cero") : v / d; }
            else return v;
        }
    }

    private int Unary()
    {
        Skip();
        if (Eat("<")) return Unary() & 0xFF;
        if (Eat(">")) return (Unary() >> 8) & 0xFF;
        if (Eat("-")) return -Unary();
        if (Eat("~")) return ~Unary() & 0xFFFF;
        return Atom();
    }

    private int Atom()
    {
        Skip();
        if (_i >= _s.Length)
            throw new FormatException($"falta un valor en \"{_s}\"");
        char c = _s[_i];
        if (Eat("("))
        {
            int v = Or();
            if (!Eat(")"))
                throw new FormatException($"falta cerrar un paréntesis en \"{_s}\"");
            return v;
        }
        if (c == '$' && (_i + 1 >= _s.Length || !Uri.IsHexDigit(_s[_i + 1])))
        {
            _i++;
            return _c.Lookup("$");
        }
        if (c == '$')
            return Number(16, 1);
        if (c == '%')
            return Number(2, 1);
        if (c == '0' && _i + 1 < _s.Length && (_s[_i + 1] == 'x' || _s[_i + 1] == 'X'))
            return Number(16, 2);
        if (char.IsDigit(c))
        {
            // 0FFh / 1Ah (Z80 style hex) or plain decimal
            int j = _i;
            while (j < _s.Length && Uri.IsHexDigit(_s[j])) j++;
            if (j < _s.Length && (_s[j] == 'h' || _s[j] == 'H') && (j + 1 >= _s.Length || !char.IsLetterOrDigit(_s[j + 1])))
            {
                int v = int.Parse(_s[_i..j], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                _i = j + 1;
                return v;
            }
            return Number(10, 0);
        }
        if (c == '\'' && _i + 2 < _s.Length && _s[_i + 2] == '\'')
        {
            _i += 3;
            return _s[_i - 2];
        }
        if (c == '*')
        {
            _i++;
            return _c.Lookup("*");
        }
        if (char.IsLetter(c) || c == '_' || c == '@')
        {
            int start = _i;
            _i++;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
            return _c.Lookup(_s[start.._i]);
        }
        throw new FormatException($"no entiendo \"{_s[_i..]}\" en la expresión");
    }

    private int Number(int radix, int prefix)
    {
        _i += prefix;
        int start = _i;
        while (_i < _s.Length && Uri.IsHexDigit(_s[_i]) && (radix == 16 || char.IsDigit(_s[_i]))) _i++;
        string digits = _s[start.._i];
        if (digits.Length == 0)
            throw new FormatException($"número mal escrito en \"{_s}\"");
        return radix switch
        {
            16 => int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            2 => Convert.ToInt32(digits, 2),
            _ => int.Parse(digits, CultureInfo.InvariantCulture),
        };
    }
}

internal static class AsmText
{
    public static bool IsName(string s) =>
        s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_' || (s[0] == '@' && s.Length > 1)) && s.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_');

    public static string StripComment(string line)
    {
        var sb = new StringBuilder();
        char quote = '\0';
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quote != '\0')
            {
                sb.Append(ch);
                if (ch == quote) quote = '\0';
            }
            else if (ch == '"' || (ch == '\'' && i + 2 < line.Length && line[i + 2] == '\''))
            {
                // "strings" and 'c' character constants (AF' is not a quote)
                quote = ch;
                sb.Append(ch);
            }
            else if (ch == ';')
                break;
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }

    public static List<string> SplitArgs(string text)
    {
        var args = new List<string>();
        var cur = new StringBuilder();
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quote != '\0')
            {
                cur.Append(ch);
                if (ch == quote) quote = '\0';
                continue;
            }
            if (ch == '"' || (ch == '\'' && i + 2 < text.Length && text[i + 2] == '\'')) quote = ch;
            if (ch == '(') depth++;
            if (ch == ')') depth--;
            if (ch == ',' && depth == 0)
            {
                args.Add(cur.ToString().Trim());
                cur.Clear();
                continue;
            }
            cur.Append(ch);
        }
        if (cur.ToString().Trim().Length > 0)
            args.Add(cur.ToString().Trim());
        return args;
    }

    public static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
}
