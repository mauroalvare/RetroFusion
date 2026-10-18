namespace Retro.Systems.Spectrum;

public sealed class SnapshotException : Exception
{
    public SnapshotException(string message) : base(message) { }
}

/// <summary>.SNA (48K): registers + 48 KB of RAM; PC is on the stack.</summary>
public static class SnaSnapshot
{
    public const int Size48 = 27 + 49152;

    public static bool Probe(ReadOnlySpan<byte> data) => data.Length == Size48;

    public static void Load(Spectrum48 m, ReadOnlySpan<byte> d)
    {
        if (d.Length != Size48)
            throw new SnapshotException(d.Length == 131103 || d.Length == 147487
                ? "Snapshot SNA de 128K: requiere el Spectrum 128K (próxima etapa)."
                : "Tamaño de SNA inválido.");
        m.Reset(false);
        var c = m.Z80;
        c.I = d[0];
        c.HL_ = W(d, 1); c.DE_ = W(d, 3); c.BC_ = W(d, 5); c.AF_ = W(d, 7);
        c.HL = W(d, 9); c.DE = W(d, 11); c.BC = W(d, 13); c.IY = W(d, 15); c.IX = W(d, 17);
        c.IFF2 = (d[19] & 0x04) != 0;
        c.IFF1 = c.IFF2;
        c.R = d[20];
        c.AF = W(d, 21);
        c.SP = W(d, 23);
        c.IM = d[25] & 3;
        m.LoadMemoryImage(d.Slice(27, 49152), d[26]);
        // RETN: the snapshot stored PC on the stack
        c.PC = (ushort)(m.Peek(c.SP) | (m.Peek((ushort)(c.SP + 1)) << 8));
        c.SP += 2;
    }

    private static ushort W(ReadOnlySpan<byte> d, int p) => (ushort)(d[p] | (d[p + 1] << 8));
}

/// <summary>.Z80 snapshot, versions 1, 2 and 3 (48K machines).</summary>
public static class Z80Snapshot
{
    public static bool Probe(ReadOnlySpan<byte> data) => data.Length > 30;

    public static void Load(Spectrum48 m, ReadOnlySpan<byte> d)
    {
        if (d.Length < 30)
            throw new SnapshotException("Snapshot Z80 demasiado corto.");
        ushort pc = W(d, 6);
        int version = 1;
        int headerLength = 30;
        bool is48 = true;
        if (pc == 0)
        {
            int extra = W(d, 30);
            version = extra == 23 ? 2 : 3;
            headerLength = 32 + extra;
            pc = W(d, 32);
            int hw = d[34];
            is48 = version == 2 ? hw is 0 or 1 : hw is 0 or 1 or 3;
            if (!is48)
                throw new SnapshotException($"Snapshot Z80 para hardware {hw} (128K u otro): requiere una próxima etapa del emulador.");
        }
        byte flags1 = d[12] == 0xFF ? (byte)1 : d[12];
        var ram = new byte[49152];
        if (version == 1)
        {
            bool compressed = (flags1 & 0x20) != 0;
            var body = d[30..];
            if (compressed)
                Decompress(body, ram, true);
            else
                body[..Math.Min(body.Length, 49152)].CopyTo(ram);
        }
        else
        {
            int pos = headerLength;
            while (pos + 3 <= d.Length)
            {
                int len = W(d, pos);
                int page = d[pos + 2];
                pos += 3;
                int offset = page switch { 8 => 0x0000, 4 => 0x4000, 5 => 0x8000, _ => -1 };
                var block = new byte[16384];
                if (len == 0xFFFF)
                {
                    d.Slice(pos, 16384).CopyTo(block);
                    pos += 16384;
                }
                else
                {
                    Decompress(d.Slice(pos, len), block, false);
                    pos += len;
                }
                if (offset >= 0)
                    block.CopyTo(ram, offset);
            }
        }

        m.Reset(false);
        var c = m.Z80;
        c.A = d[0]; c.F = d[1]; c.BC = W(d, 2); c.HL = W(d, 4); c.PC = pc; c.SP = W(d, 8);
        c.I = d[10];
        c.R = (byte)((d[11] & 0x7F) | ((flags1 & 1) << 7));
        c.DE = W(d, 13); c.BC_ = W(d, 15); c.DE_ = W(d, 17); c.HL_ = W(d, 19);
        c.A_ = d[21]; c.F_ = d[22]; c.IY = W(d, 23); c.IX = W(d, 25);
        c.IFF1 = d[27] != 0; c.IFF2 = d[28] != 0;
        c.IM = d[29] & 3;
        m.LoadMemoryImage(ram, (byte)((flags1 >> 1) & 7));
    }

    /// <summary>ED ED nn bb = byte bb repeated nn times.</summary>
    private static void Decompress(ReadOnlySpan<byte> src, byte[] dst, bool v1)
    {
        int s = 0, o = 0;
        while (s < src.Length && o < dst.Length)
        {
            if (v1 && s + 3 < src.Length && src[s] == 0 && src[s + 1] == 0xED && src[s + 2] == 0xED && src[s + 3] == 0)
                break;
            if (s + 3 < src.Length && src[s] == 0xED && src[s + 1] == 0xED)
            {
                int count = src[s + 2];
                byte value = src[s + 3];
                for (int i = 0; i < count && o < dst.Length; i++)
                    dst[o++] = value;
                s += 4;
            }
            else
            {
                dst[o++] = src[s++];
            }
        }
    }

    private static ushort W(ReadOnlySpan<byte> d, int p) => (ushort)(d[p] | (d[p + 1] << 8));
}
