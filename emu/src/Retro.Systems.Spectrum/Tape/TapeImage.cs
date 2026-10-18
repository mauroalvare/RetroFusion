namespace Retro.Systems.Spectrum.Tape;

/// <summary>
/// A tape as the signal the computer's EAR input sees: a sequence of segments, each a
/// level held for a number of T-states. TAP and TZX files are converted into this,
/// so the Spectrum ROM (or a game's own loader) reads the tape exactly as from a
/// cassette.
/// </summary>
public sealed class TapeImage
{
    public readonly List<int> Durations = new();
    public readonly List<bool> Levels = new();
    /// <summary>Segment indices where playback must stop (TZX "pause 0" / stop blocks).</summary>
    public readonly HashSet<int> StopAfter = new();
    public readonly List<TapeBlockInfo> Blocks = new();

    public string Name { get; init; } = "";
    public long TotalTStates { get; private set; }

    private bool _level;

    public void BeginBlock(string description, int dataLength)
    {
        Blocks.Add(new TapeBlockInfo(Blocks.Count, Durations.Count, description, dataLength));
    }

    /// <summary>One pulse: the level flips, then holds for <paramref name="tstates"/>.</summary>
    public void Pulse(int tstates)
    {
        _level = !_level;
        Add(tstates, _level);
    }

    public void PulseRepeat(int tstates, int count)
    {
        for (int i = 0; i < count; i++)
            Pulse(tstates);
    }

    /// <summary>Data bits, MSB first: two pulses per bit.</summary>
    public void Data(ReadOnlySpan<byte> data, int zero, int one, int usedBitsLastByte = 8)
    {
        for (int i = 0; i < data.Length; i++)
        {
            int bits = i == data.Length - 1 ? usedBitsLastByte : 8;
            byte b = data[i];
            for (int bit = 0; bit < bits; bit++)
            {
                int len = (b & (0x80 >> bit)) != 0 ? one : zero;
                Pulse(len);
                Pulse(len);
            }
        }
    }

    /// <summary>Silence: the signal goes low and stays low.</summary>
    public void Pause(int milliseconds)
    {
        if (milliseconds <= 0)
            return;
        // A last edge always closes the final pulse (as the TZX spec requires): turbo loaders
        // wait for it after the last bit. If that leaves the signal high, it drops after 1 ms.
        int total = milliseconds * 3500;
        _level = !_level;
        if (_level)
        {
            int first = Math.Min(total, 3500);
            Add(first, true);
            _level = false;
            if (total > first)
                Add(total - first, false);
        }
        else
        {
            Add(total, false);
        }
    }

    public void Stop() => StopAfter.Add(Durations.Count - 1);

    /// <summary>Direct recording: each sample is the level for <paramref name="tstatesPerSample"/>.</summary>
    public void Direct(ReadOnlySpan<byte> data, int tstatesPerSample, int usedBitsLastByte)
    {
        for (int i = 0; i < data.Length; i++)
        {
            int bits = i == data.Length - 1 ? usedBitsLastByte : 8;
            for (int bit = 0; bit < bits; bit++)
            {
                bool level = (data[i] & (0x80 >> bit)) != 0;
                if (Levels.Count > 0 && Levels[^1] == level && Durations[^1] < int.MaxValue - tstatesPerSample)
                {
                    Durations[^1] += tstatesPerSample;
                    TotalTStates += tstatesPerSample;
                }
                else
                {
                    Add(tstatesPerSample, level);
                }
                _level = level;
            }
        }
    }

    private void Add(int tstates, bool level)
    {
        if (tstates <= 0)
            return;
        Durations.Add(tstates);
        Levels.Add(level);
        TotalTStates += tstates;
    }
}

public sealed record TapeBlockInfo(int Index, int FirstSegment, string Description, int DataLength);

/// <summary>Standard Spectrum ROM loader timings (T-states).</summary>
public static class RomTiming
{
    public const int Pilot = 2168;
    public const int PilotHeader = 8063;
    public const int PilotData = 3223;
    public const int Sync1 = 667;
    public const int Sync2 = 735;
    public const int Zero = 855;
    public const int One = 1710;

    public static void StandardBlock(TapeImage tape, ReadOnlySpan<byte> block, int pauseMs)
    {
        tape.PulseRepeat(Pilot, block.Length > 0 && block[0] < 0x80 ? PilotHeader : PilotData);
        tape.Pulse(Sync1);
        tape.Pulse(Sync2);
        tape.Data(block, Zero, One);
        tape.Pause(pauseMs);
    }

    /// <summary>Description of a standard block (header name or data length).</summary>
    public static string Describe(ReadOnlySpan<byte> block)
    {
        if (block.Length == 19 && block[0] == 0x00)
        {
            string type = block[1] switch { 0 => "Program", 1 => "Number array", 2 => "Character array", 3 => "Bytes", _ => "?" };
            var name = new char[10];
            for (int i = 0; i < 10; i++)
                name[i] = block[2 + i] is >= 32 and < 127 ? (char)block[2 + i] : '?';
            int length = block[12] | (block[13] << 8);
            int p1 = block[14] | (block[15] << 8);
            return type == "Bytes"
                ? $"{type}: \"{new string(name).TrimEnd()}\" {length} bytes en {p1}"
                : $"{type}: \"{new string(name).TrimEnd()}\" ({length} bytes)";
        }
        return block.Length > 0 && block[0] == 0xFF ? $"Datos ({block.Length - 2} bytes)" : $"Bloque ({block.Length} bytes)";
    }
}
