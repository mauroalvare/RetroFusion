namespace Retro.App;

/// <summary>
/// A value computed for a key and kept until the key changes: views drawn every frame recompute
/// their lists only when the library, the search or the section they depend on changes.
/// </summary>
public sealed class FrameCache<TKey, TValue>
{
    private TKey? _key;
    private TValue? _value;
    private bool _valid;
    public int Computations { get; private set; }

    public TValue Get(TKey key, Func<TValue> compute)
    {
        if (!_valid || !EqualityComparer<TKey>.Default.Equals(_key, key))
        {
            _value = compute(); _key = key; _valid = true; Computations++;
        }
        return _value!;
    }

    public void Invalidate() => _valid = false;
}
