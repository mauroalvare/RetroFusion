namespace Retro.Systems.Spectrum.Tape;

/// <summary>Plays a <see cref="TapeImage"/> against the CPU clock, like a cassette deck.</summary>
public sealed class TapePlayer
{
    private TapeImage? _tape;
    private int _segment;
    private long _segmentEnd;     // absolute T-state where the current segment ends
    private long _remaining;      // remaining T-states of the segment while stopped
    private bool _level;

    private HashSet<int> _romBlockStarts = new();

    public TapeImage? Tape => _tape;
    public bool IsPlaying { get; private set; }

    /// <summary>
    /// Stop when the next block the ROM can load begins, as a user stops the deck while the
    /// game runs. The machine asks for it when nobody is reading the tape; LD-BYTES plays it again.
    /// </summary>
    public bool StopAtNextRomBlock { get; set; }

    /// <summary>Times the tape stopped by itself before a block (for reports and tests).</summary>
    public int AutoStops { get; private set; }

    /// <summary>
    /// Level changes (absolute T-state, new level) since the consumer last cleared the
    /// list. The machine uses them to mix the tape signal into the sound exactly.
    /// </summary>
    public List<(long T, bool Level)> Edges { get; } = new();
    public bool IsLoaded => _tape != null;
    public bool AtEnd => _tape != null && _segment >= _tape.Durations.Count;

    public void Insert(TapeImage tape)
    {
        _tape = tape;
        _romBlockStarts = tape.Blocks.Where(b => b.RomLoadable && b.Index > 0).Select(b => b.FirstSegment).ToHashSet();
        Rewind();
    }

    public void Eject()
    {
        _tape = null;
        IsPlaying = false;
        _level = false;
    }

    public void Rewind()
    {
        IsPlaying = false;
        _segment = 0;
        _level = false;
        _remaining = _tape != null && _tape.Durations.Count > 0 ? _tape.Durations[0] : 0;
        if (_tape != null && _tape.Levels.Count > 0)
            _level = _tape.Levels[0];
    }

    public void Play(long now)
    {
        if (_tape == null || AtEnd || IsPlaying)
            return;
        _segmentEnd = now + _remaining;
        IsPlaying = true;
        StopAtNextRomBlock = false;
    }

    public void Stop(long now)
    {
        if (!IsPlaying)
            return;
        LevelAt(now);
        _remaining = Math.Max(0, _segmentEnd - now);
        IsPlaying = false;
    }

    /// <summary>Signal level at the EAR input at T-state <paramref name="now"/>.</summary>
    public bool LevelAt(long now)
    {
        if (!IsPlaying || _tape == null)
            return _level;
        while (now >= _segmentEnd)
        {
            if (_tape.StopAfter.Contains(_segment))
            {
                _segment++;
                IsPlaying = false;
                _remaining = _segment < _tape.Durations.Count ? _tape.Durations[_segment] : 0;
                if (_segment < _tape.Levels.Count)
                    _level = _tape.Levels[_segment];
                return _level;
            }
            _segment++;
            if (StopAtNextRomBlock && _romBlockStarts.Contains(_segment))
            {
                IsPlaying = false;
                StopAtNextRomBlock = false;
                AutoStops++;
                _remaining = _tape.Durations[_segment];
                _level = _tape.Levels[_segment];
                return _level;
            }
            if (_segment >= _tape.Durations.Count)
            {
                IsPlaying = false;
                RecordEdge(_segmentEnd, false);
                _level = false;
                return _level;
            }
            bool next = _tape.Levels[_segment];
            if (next != _level)
                RecordEdge(_segmentEnd, next);
            _level = next;
            _segmentEnd += _tape.Durations[_segment];
        }
        return _level;
    }

    /// <summary>Pilot pulses of the shortest ROM block (data: 3223); fewer played means still in the pilot.</summary>
    private const int ShortestRomPilot = 3000;

    /// <summary>
    /// Stops now if the tape is still in the pilot tone of a block the ROM can load, and
    /// goes back to the start of that block: the loader that asks for it later finds it
    /// whole. Returns false (and keeps playing) anywhere else.
    /// </summary>
    public bool StopInRomPilot(long now)
    {
        if (_tape == null || !IsPlaying)
            return false;
        LevelAt(now);
        var block = _tape.Blocks.LastOrDefault(b => b.FirstSegment <= _segment);
        if (block == null || !block.RomLoadable || block.Index == 0 || _segment - block.FirstSegment >= ShortestRomPilot)
            return false;
        IsPlaying = false;
        StopAtNextRomBlock = false;
        AutoStops++;
        _segment = block.FirstSegment;
        _remaining = _tape.Durations[_segment];
        _level = _tape.Levels[_segment];
        return true;
    }

    private void RecordEdge(long t, bool level)
    {
        if (Edges.Count > 200_000)
            Edges.Clear();   // nobody is consuming them
        Edges.Add((t, level));
    }

    /// <summary>Index of the block being played (for the UI).</summary>
    public int CurrentBlock
    {
        get
        {
            if (_tape == null)
                return -1;
            int current = 0;
            foreach (var b in _tape.Blocks)
                if (b.FirstSegment <= _segment)
                    current = b.Index;
            return current;
        }
    }

    public double Progress => _tape == null || _tape.Durations.Count == 0 ? 0 : (double)_segment / _tape.Durations.Count;

    // -- save states ------------------------------------------------------------
    public (int segment, long remaining, bool playing, bool level) Capture(long now)
    {
        long remaining = IsPlaying ? Math.Max(0, _segmentEnd - now) : _remaining;
        return (_segment, remaining, IsPlaying, _level);
    }

    public void Restore(int segment, long remaining, bool playing, bool level, long now)
    {
        _segment = segment;
        _remaining = remaining;
        _level = level;
        IsPlaying = false;
        if (playing)
            Play(now);
    }
}
