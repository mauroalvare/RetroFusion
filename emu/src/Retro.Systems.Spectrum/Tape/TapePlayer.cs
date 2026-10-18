namespace Retro.Systems.Spectrum.Tape;

/// <summary>Plays a <see cref="TapeImage"/> against the CPU clock, like a cassette deck.</summary>
public sealed class TapePlayer
{
    private TapeImage? _tape;
    private int _segment;
    private long _segmentEnd;     // absolute T-state where the current segment ends
    private long _remaining;      // remaining T-states of the segment while stopped
    private bool _level;

    public TapeImage? Tape => _tape;
    public bool IsPlaying { get; private set; }

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
