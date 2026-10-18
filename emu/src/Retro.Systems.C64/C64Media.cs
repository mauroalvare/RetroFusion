using System.Text;
using Retro.Core;

namespace Retro.Systems.C64;

/// <summary>A program file: where it loads and its bytes.</summary>
public sealed record C64Program(string Name, ushort Start, byte[] Data);

public sealed record C64LoadResult(string Description, IReadOnlyList<string> Warnings);

/// <summary>
/// C64 programs: .T64 tape archives and .PRG files. The machine boots to the BASIC prompt,
/// the program is placed in memory where it belongs, and it starts the way the user would
/// have started it: through the SYS address of its BASIC line, or by typing RUN.
/// </summary>
public static class C64Media
{
    public static int Probe(MediaFile f)
    {
        var d = f.Data;
        if (StartsWith(d, "C64 tape image file") || StartsWith(d, "C64S tape"))
            return 95;
        if (f.Extension == ".prg" && d.Length > 2)
            return (d[0] | (d[1] << 8)) == 0x0801 ? 85 : 60;
        return 0;
    }

    private static bool StartsWith(byte[] d, string sig) =>
        d.Length >= sig.Length && Encoding.ASCII.GetString(d, 0, sig.Length).Equals(sig, StringComparison.OrdinalIgnoreCase);

    /// <summary>The files of a .T64. Many archives have a wrong end address: the size then comes from the file length.</summary>
    public static List<C64Program> ReadT64(byte[] d)
    {
        var list = new List<C64Program>();
        if (d.Length < 64)
            return list;
        int max = Math.Max(1, d[34] | (d[35] << 8));
        var entries = new List<(string name, ushort start, ushort end, int offset)>();
        for (int i = 0; i < max && 64 + i * 32 + 32 <= d.Length; i++)
        {
            int e = 64 + i * 32;
            if (d[e] == 0)
                continue;
            ushort start = (ushort)(d[e + 2] | (d[e + 3] << 8));
            ushort end = (ushort)(d[e + 4] | (d[e + 5] << 8));
            int offset = d[e + 8] | (d[e + 9] << 8) | (d[e + 10] << 16) | (d[e + 11] << 24);
            string name = Encoding.ASCII.GetString(d, e + 16, 16).TrimEnd(' ', '\0', (char)0xA0);
            entries.Add((name, start, end, offset));
        }
        var offsets = entries.Select(x => x.offset).OrderBy(x => x).ToList();
        foreach (var (name, start, end, offset) in entries)
        {
            if (offset <= 0 || offset >= d.Length)
                continue;
            int next = offsets.FirstOrDefault(o => o > offset, d.Length);
            int size = end > start ? end - start : next - offset;
            if (offset + size > next)
                size = next - offset;
            if (size <= 0)
                continue;
            list.Add(new C64Program(name.Length > 0 ? name : "PROGRAMA", start, d[offset..(offset + size)]));
        }
        return list;
    }

    public static C64Program ReadPrg(MediaFile f) =>
        new(Path.GetFileNameWithoutExtension(f.Name).ToUpperInvariant(), (ushort)(f.Data[0] | (f.Data[1] << 8)), f.Data[2..]);

    public static C64LoadResult Load(C64 m, MediaFile f)
    {
        var warnings = new List<string>();
        C64Program program;
        string kind;
        if (StartsWith(f.Data, "C64 tape image file") || StartsWith(f.Data, "C64S tape"))
        {
            var files = ReadT64(f.Data);
            if (files.Count == 0)
                throw new InvalidDataException("La cinta .T64 no tiene ningún programa.");
            if (files.Count > 1)
                warnings.Add($"la cinta tiene {files.Count} programas; se cargó el primero");
            program = files[0];
            kind = "cinta T64";
        }
        else if (f.Extension == ".prg")
        {
            program = ReadPrg(f);
            kind = "programa PRG";
        }
        else
            throw new NotSupportedException($"Por ahora el C64 carga .t64 y .prg; los discos .d64, los cartuchos .crt y las cintas .tap llegan en la próxima etapa.");

        m.Reset(true);
        BootToReady(m);
        string how = Start(m, program);
        return new C64LoadResult($"{kind} \"{program.Name}\": {program.Data.Length} bytes en ${program.Start:X4}, {how} ({m.FirmwareDescription})", warnings);
    }

    /// <summary>Runs the machine until BASIC shows READY. (or 5 seconds).</summary>
    public static void BootToReady(C64 m)
    {
        var sink = new short[8192];
        for (int frame = 0; frame < 250; frame++)
        {
            m.RunFrame(null);
            m.DrainAudio(sink);
            if (frame > 20 && ScreenContains(m, "READY."))
            {
                m.DefaultIrqHandler = (ushort)(m.Ram[0x0314] | (m.Ram[0x0315] << 8));
                // a few more frames so the cursor loop is running
                for (int i = 0; i < 5; i++) { m.RunFrame(null); m.DrainAudio(sink); }
                return;
            }
        }
    }

    /// <summary>True when the text is on the default screen ($0400), in screen codes.</summary>
    public static bool ScreenContains(C64 m, string text)
    {
        var codes = text.ToUpperInvariant().Select(c => (byte)(c >= 'A' && c <= 'Z' ? c - 'A' + 1 : c)).ToArray();
        var ram = m.Ram;
        for (int i = 0x0400; i < 0x0400 + 1000 - codes.Length; i++)
        {
            int k = 0;
            while (k < codes.Length && (ram[i + k] & 0x7F) == codes[k]) k++;
            if (k == codes.Length)
                return true;
        }
        return false;
    }

    /// <summary>Puts the program in RAM and starts it. Returns how it was started.</summary>
    public static string Start(C64 m, C64Program p)
    {
        var ram = m.Ram;
        int end = Math.Min(0x10000, p.Start + p.Data.Length);
        Array.Copy(p.Data, 0, ram, p.Start, end - p.Start);
        if (p.Start == 0x0801)
        {
            // BASIC pointers: start and end of the program and its variables
            void Word(int at, int value) { ram[at] = (byte)value; ram[at + 1] = (byte)(value >> 8); }
            Word(0x2B, 0x0801);
            Word(0x2D, end);
            Word(0x2F, end);
            Word(0x31, end);
            Word(0xAE, end);
            if (SysAddress(ram) is int sys)
            {
                JumpTo(m, (ushort)sys);
                return $"arranca con SYS {sys}";
            }
            TypeRun(m);
            return "arranca con RUN";
        }
        JumpTo(m, p.Start);
        return $"arranca en ${p.Start:X4}";
    }

    /// <summary>The address of a "SYS nnnn" in the first BASIC line, if there is one.</summary>
    public static int? SysAddress(byte[] ram)
    {
        int i = 0x0805;   // after the link and the line number
        for (int guard = 0; guard < 40 && ram[i] != 0; guard++, i++)
        {
            if (ram[i] != 0x9E)   // SYS token
                continue;
            i++;
            while (ram[i] == ' ' || ram[i] == '(') i++;
            int value = 0, digits = 0;
            while (ram[i] >= '0' && ram[i] <= '9' && digits < 6)
            {
                value = value * 10 + (ram[i] - '0');
                i++;
                digits++;
            }
            return digits > 0 && value < 0x10000 ? value : null;
        }
        return null;
    }

    private static void TypeRun(C64 m)
    {
        var ram = m.Ram;
        byte[] keys = { (byte)'R', (byte)'U', (byte)'N', 0x0D };
        keys.CopyTo(ram, 0x0277);
        ram[0xC6] = (byte)keys.Length;
    }

    /// <summary>
    /// Jumps to the program exactly as BASIC's SYS does: A, X, Y and the flags come from
    /// $030C-$030F and the call is a JSR, so the stack pointer is $F6 with a return address
    /// on it. Some programs depend on it (a SYS into the line number used as a BNE, crunchers
    /// that count with TSX). A program that returns ends in a harmless idle loop.
    /// </summary>
    private static void JumpTo(C64 m, ushort address)
    {
        var cpu = m.Cpu6510;
        for (int guard = 0; guard < 200_000 && (cpu.P & 0x04) != 0; guard++)
            m.StepInstruction();
        var ram = m.Ram;
        // idle loop at $02A7 (unused by the KERNAL): JMP $02A7
        ram[0x02A7] = 0x4C; ram[0x02A8] = 0xA7; ram[0x02A9] = 0x02;
        ushort ret = 0x02A7 - 1;   // RTS adds one
        ram[0x01F8] = (byte)(ret >> 8);
        ram[0x01F7] = (byte)ret;
        cpu.S = 0xF6;
        cpu.A = ram[0x030C];
        cpu.X = ram[0x030D];
        cpu.Y = ram[0x030E];
        cpu.P = (byte)((ram[0x030F] & 0xCF) | 0x20);
        cpu.PC = address;
    }
}
