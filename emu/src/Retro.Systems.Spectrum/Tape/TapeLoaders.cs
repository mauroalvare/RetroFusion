namespace Retro.Systems.Spectrum.Tape;

public sealed class TapeFormatException : Exception
{
    public TapeFormatException(string message) : base(message) { }
}

/// <summary>
/// .TAP: the raw blocks saved by the ROM (flag byte, data, checksum), each preceded by
/// a 16-bit length. Every block is played with the standard ROM timings.
/// </summary>
public static class TapLoader
{
    public static bool Probe(ReadOnlySpan<byte> data)
    {
        // Walk the block chain; it must end exactly at the end of the file.
        int pos = 0, blocks = 0;
        while (pos + 2 <= data.Length)
        {
            int len = data[pos] | (data[pos + 1] << 8);
            if (len == 0 || pos + 2 + len > data.Length)
                return false;
            pos += 2 + len;
            blocks++;
        }
        return blocks > 0 && pos == data.Length;
    }

    public static TapeImage Load(ReadOnlySpan<byte> data, string name)
    {
        if (!Probe(data))
            throw new TapeFormatException("El archivo no es una cinta TAP válida (la cadena de bloques no cierra).");
        var tape = new TapeImage { Name = name };
        int pos = 0;
        while (pos + 2 <= data.Length)
        {
            int len = data[pos] | (data[pos + 1] << 8);
            var block = data.Slice(pos + 2, len);
            tape.BeginBlock(RomTiming.Describe(block), len, romLoadable: true);
            RomTiming.StandardBlock(tape, block, 1000);
            pos += 2 + len;
        }
        return tape;
    }
}

/// <summary>
/// .TZX: a description of the tape signal block by block (standard, turbo, pure tone,
/// pulse sequences, direct recordings...). Needed for games with custom loaders.
/// </summary>
public static class TzxLoader
{
    private static readonly byte[] Signature = "ZXTape!\x1A"u8.ToArray();

    public static bool Probe(ReadOnlySpan<byte> data) =>
        data.Length >= 10 && data[..8].SequenceEqual(Signature);

    public static TapeImage Load(ReadOnlySpan<byte> data, string name, List<string>? warnings = null)
    {
        if (!Probe(data))
            throw new TapeFormatException("Falta la firma 'ZXTape!' de un archivo TZX.");
        var tape = new TapeImage { Name = name };
        int pos = 10;
        var loopStack = new Stack<(int pos, int count)>();
        int guard = 0;
        while (pos < data.Length)
        {
            if (++guard > 1_000_000)
                throw new TapeFormatException("El TZX tiene demasiados bloques (¿bucle infinito?).");
            byte id = data[pos++];
            switch (id)
            {
                case 0x10: // standard speed data
                {
                    int pause = U16(data, pos), len = U16(data, pos + 2);
                    var block = Slice(data, pos + 4, len);
                    tape.BeginBlock(RomTiming.Describe(block), len, romLoadable: true);
                    RomTiming.StandardBlock(tape, block, pause);
                    pos += 4 + len;
                    break;
                }
                case 0x11: // turbo speed data
                {
                    int pilot = U16(data, pos), sync1 = U16(data, pos + 2), sync2 = U16(data, pos + 4);
                    int zero = U16(data, pos + 6), one = U16(data, pos + 8), pilotCount = U16(data, pos + 10);
                    int used = data[pos + 12], pause = U16(data, pos + 13), len = U24(data, pos + 15);
                    var block = Slice(data, pos + 18, len);
                    tape.BeginBlock($"Turbo ({len} bytes)", len);
                    tape.PulseRepeat(pilot, pilotCount);
                    tape.Pulse(sync1);
                    tape.Pulse(sync2);
                    tape.Data(block, zero, one, used == 0 ? 8 : used);
                    tape.Pause(pause);
                    pos += 18 + len;
                    break;
                }
                case 0x12: // pure tone
                    tape.BeginBlock("Tono", 0);
                    tape.PulseRepeat(U16(data, pos), U16(data, pos + 2));
                    pos += 4;
                    break;
                case 0x13: // pulse sequence
                {
                    int n = data[pos];
                    tape.BeginBlock($"Secuencia de {n} pulsos", 0);
                    for (int i = 0; i < n; i++)
                        tape.Pulse(U16(data, pos + 1 + 2 * i));
                    pos += 1 + 2 * n;
                    break;
                }
                case 0x14: // pure data
                {
                    int zero = U16(data, pos), one = U16(data, pos + 2), used = data[pos + 4];
                    int pause = U16(data, pos + 5), len = U24(data, pos + 7);
                    tape.BeginBlock($"Datos puros ({len} bytes)", len);
                    tape.Data(Slice(data, pos + 10, len), zero, one, used == 0 ? 8 : used);
                    tape.Pause(pause);
                    pos += 10 + len;
                    break;
                }
                case 0x15: // direct recording
                {
                    int tps = U16(data, pos), pause = U16(data, pos + 2), used = data[pos + 4];
                    int len = U24(data, pos + 5);
                    tape.BeginBlock($"Grabación directa ({len} bytes)", len);
                    tape.Direct(Slice(data, pos + 8, len), tps, used == 0 ? 8 : used);
                    tape.Pause(pause);
                    pos += 8 + len;
                    break;
                }
                case 0x18: // CSW recording
                case 0x19: // generalized data
                {
                    int len = (int)U32(data, pos);
                    warnings?.Add($"Bloque TZX 0x{id:X2} todavía no soportado: se saltea.");
                    tape.BeginBlock($"Bloque 0x{id:X2} no soportado", len);
                    pos += 4 + len;
                    break;
                }
                case 0x20: // pause or "stop the tape"
                {
                    int ms = U16(data, pos);
                    tape.BeginBlock(ms == 0 ? "Detener cinta" : $"Pausa {ms} ms", 0);
                    if (ms == 0)
                        tape.Stop();
                    else
                        tape.Pause(ms);
                    pos += 2;
                    break;
                }
                case 0x21: pos += 1 + data[pos]; break;             // group start
                case 0x22: break;                                    // group end
                case 0x23: pos += 2; break;                          // jump (not followed)
                case 0x24:                                           // loop start
                    loopStack.Push((pos + 2, U16(data, pos)));
                    pos += 2;
                    break;
                case 0x25:                                           // loop end
                    if (loopStack.Count > 0)
                    {
                        var (start, count) = loopStack.Pop();
                        if (count > 1)
                        {
                            loopStack.Push((start, count - 1));
                            pos = start;
                        }
                    }
                    break;
                case 0x26: pos += 2 + 2 * U16(data, pos); break;     // call sequence
                case 0x27: break;                                    // return
                case 0x28: pos += 2 + U16(data, pos); break;         // select block
                case 0x2A: pos += 4; tape.Stop(); break;             // stop if 48K
                case 0x2B: pos += 5; break;                          // set signal level
                case 0x30: pos += 1 + data[pos]; break;              // text description
                case 0x31: pos += 2 + data[pos + 1]; break;          // message
                case 0x32: pos += 2 + U16(data, pos); break;         // archive info
                case 0x33: pos += 1 + 3 * data[pos]; break;          // hardware type
                case 0x35: pos += 20 + (int)U32(data, pos + 16); break; // custom info
                case 0x5A: pos += 9; break;                          // glue block
                default:
                    throw new TapeFormatException($"Bloque TZX desconocido 0x{id:X2} en la posición {pos - 1}.");
            }
        }
        tape.Finish();
        return tape;
    }

    private static int U16(ReadOnlySpan<byte> d, int p) => d[p] | (d[p + 1] << 8);
    private static int U24(ReadOnlySpan<byte> d, int p) => d[p] | (d[p + 1] << 8) | (d[p + 2] << 16);
    private static uint U32(ReadOnlySpan<byte> d, int p) => (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24));

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> d, int p, int len)
    {
        if (p + len > d.Length)
            throw new TapeFormatException("TZX truncado.");
        return d.Slice(p, len);
    }
}
