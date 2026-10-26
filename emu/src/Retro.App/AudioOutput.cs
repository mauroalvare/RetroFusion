namespace Retro.App;

/// <summary>Callback-owned gain ramp; no allocations or locks in sample processing.</summary>
public sealed class AudioOutput
{
    private float _gain;
    public void Reset() => _gain = 0;
    public void Process(Span<short> samples, float volume, bool muted)
    {
        float target = muted || !float.IsFinite(volume) ? 0 : Math.Clamp(volume, 0, 1);
        int i = 0;
        while (i < samples.Length && _gain != target)
        {
            _gain = _gain < target ? Math.Min(target, _gain + 1f / 256) : Math.Max(target, _gain - 1f / 256);
            samples[i] = (short)(samples[i] * _gain); i++;
        }
        if (_gain == 0) samples[i..].Clear();
        else if (_gain != 1)
            for (; i < samples.Length; i++) samples[i] = (short)(samples[i] * _gain);
    }
}
