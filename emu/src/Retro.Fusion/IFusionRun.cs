using Retro.Core;

namespace Retro.Fusion;

/// <summary>One game inside a fusion.</summary>
public sealed class FusionSlot
{
    public required string Id { get; init; }
    public required GameProfile Profile { get; init; }
    public required IMachine Machine { get; init; }
}

public sealed record FusionEvent(long Tick, string Text);

/// <summary>Something that runs several games as one: a scripted fusion game with its own scene.</summary>
public interface IFusionRun
{
    string Title { get; }
    string Description { get; }
    IReadOnlyList<FusionSlot> Slots { get; }
    VideoFrame Video { get; }
    List<FusionEvent> Events { get; }
    long Tick { get; }
    /// <summary>Player input (left, right, up, down, fire, jump, start, coin).</summary>
    void SetInput(string input, bool pressed);
    /// <summary>Advances everything by one tick of <paramref name="seconds"/> (1/60 s).</summary>
    void Update(double seconds);
    int DrainAudio(short[] destination);
}

