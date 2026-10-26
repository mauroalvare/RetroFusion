namespace Retro.App;

/// <summary>Bounded mono PCM queue shared by the emulator and audio callback.</summary>
public sealed class AudioQueue
{
    private readonly short[] _buf;
    private readonly object _sync = new();
    private int _read, _write, _count;
    private short _last;
    public AudioQueue(int capacity) => _buf = capacity > 0 ? new short[capacity] : throw new ArgumentOutOfRangeException(nameof(capacity));
    public int Count { get { lock (_sync) return _count; } }
    public void Clear() { lock (_sync) { _read = _write = _count = 0; _last = 0; } }
    public void Discard(int keep)
    {
        if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep));
        lock (_sync)
        {
            if (_count <= keep) return;
            _read = (_read + _count - keep) % _buf.Length; _count = keep;
        }
    }
    public void Push(ReadOnlySpan<short> samples)
    {
        lock (_sync)
        {
            if (samples.Length >= _buf.Length)
            {
                samples[^_buf.Length..].CopyTo(_buf);
                _read = _write = 0; _count = _buf.Length; return;
            }
            int drop = Math.Max(0, _count + samples.Length - _buf.Length);
            _read = (_read + drop) % _buf.Length; _count -= drop;
            int first = Math.Min(samples.Length, _buf.Length - _write);
            samples[..first].CopyTo(_buf.AsSpan(_write)); samples[first..].CopyTo(_buf);
            _write = (_write + samples.Length) % _buf.Length; _count += samples.Length;
        }
    }
    public void Pop(Span<short> dest)
    {
        lock (_sync)
        {
            int take = Math.Min(dest.Length, _count), first = Math.Min(take, _buf.Length - _read);
            _buf.AsSpan(_read, first).CopyTo(dest); _buf.AsSpan(0, take - first).CopyTo(dest[first..]);
            _read = (_read + take) % _buf.Length; _count -= take;
            if (take > 0) _last = dest[take - 1];
            for (int i = take; i < dest.Length; i++) { _last = (short)(_last * 0.98); dest[i] = _last; }
        }
    }
}
