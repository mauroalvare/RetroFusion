namespace Retro.App;

internal sealed record RewindPoint(long Frame, byte[] State, int[] Pixels)
{
    public long Bytes => State.LongLength + 4 * Pixels.LongLength;
}

internal sealed class RewindBuffer(long capacity = 64 * 1024 * 1024, int maxPoints = 600)
{
    private readonly LinkedList<RewindPoint> _points = new();
    public long Bytes { get; private set; }
    public int Count => _points.Count;
    public long LastFrame => _points.Last?.Value.Frame ?? -1;

    // Takes ownership of freshly captured arrays. No compression or disk writes on the emulation thread.
    public bool Add(RewindPoint point)
    {
        if (point.Bytes > capacity || maxPoints <= 0 || point.Frame <= LastFrame) return false;
        _points.AddLast(point); Bytes += point.Bytes;
        while (Bytes > capacity || _points.Count > maxPoints) RemoveFirst();
        return true;
    }
    private void RemoveFirst()
    {
        Bytes -= _points.First!.Value.Bytes; _points.RemoveFirst();
    }
    public RewindPoint? Find(long target, long current)
    {
        RewindPoint? result = null;
        foreach (var point in _points)
        {
            if (point.Frame >= current) break;
            if (result != null && point.Frame > target) break;
            result = point;
        }
        return result;
    }
    public void TrimAfter(long frame)
    {
        while (_points.Last is { } last && last.Value.Frame > frame)
        { Bytes -= last.Value.Bytes; _points.RemoveLast(); }
    }
    public void Clear() { _points.Clear(); Bytes = 0; }
}

internal readonly record struct InputChange(string Key, bool Pressed);
internal sealed record InputFrame(InputChange[] Changes, byte[] ScreenHash);
internal sealed class InputMovie(string identity, byte[] initialState, int[] initialPixels, int maxFrames)
{
    public string Identity { get; } = identity;
    public byte[] InitialState { get; } = initialState;
    public int[] InitialPixels { get; } = initialPixels;
    public int MaxFrames { get; } = maxFrames;
    public List<InputFrame> Frames { get; } = [];
    public int Changes { get; set; }
}
