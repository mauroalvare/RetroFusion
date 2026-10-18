namespace Retro.Core;

public enum SearchCondition
{
    Changed,
    Unchanged,
    Increased,
    Decreased,
    EqualTo,
    IncreasedBy,
    DecreasedBy,
}

/// <summary>
/// Finds where a game keeps a variable (lives, score, position, timer), like the
/// classic cheat finders: take a snapshot, do something in the game, keep only the
/// addresses whose value changed the way the variable should have changed.
/// </summary>
public sealed class MemorySearch
{
    private readonly IMemoryView _memory;
    private readonly List<uint> _candidates = new();
    private readonly Dictionary<uint, byte> _previous = new();

    public MemorySearch(IMemoryView memory) => _memory = memory;

    public int Count => _candidates.Count;

    /// <summary>Starts a new search over <paramref name="start"/>..<paramref name="end"/> (inclusive).</summary>
    public void Start(uint start, uint end)
    {
        _candidates.Clear();
        _previous.Clear();
        for (uint a = start; a <= end; a++)
        {
            _candidates.Add(a);
            _previous[a] = _memory.Peek(a);
        }
    }

    /// <summary>Keeps the addresses that satisfy the condition since the last step.</summary>
    public int Filter(SearchCondition condition, int value = 0)
    {
        _candidates.RemoveAll(a =>
        {
            byte before = _previous[a], now = _memory.Peek(a);
            return !(condition switch
            {
                SearchCondition.Changed => now != before,
                SearchCondition.Unchanged => now == before,
                SearchCondition.Increased => now > before,
                SearchCondition.Decreased => now < before,
                SearchCondition.EqualTo => now == value,
                SearchCondition.IncreasedBy => now == (byte)(before + value),
                SearchCondition.DecreasedBy => now == (byte)(before - value),
                _ => false,
            });
        });
        foreach (var a in _candidates)
            _previous[a] = _memory.Peek(a);
        return _candidates.Count;
    }

    public IReadOnlyList<(uint Address, byte Value)> Results(int max = 64) =>
        _candidates.Take(max).Select(a => (a, _memory.Peek(a))).ToList();
}
