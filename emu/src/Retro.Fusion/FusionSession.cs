using Retro.Core;

namespace Retro.Fusion;

/// <summary>One game inside a fusion.</summary>
public sealed class FusionSlot
{
    public required string Id { get; init; }
    public required GameProfile Profile { get; init; }
    public required IMachine Machine { get; init; }
    internal double Accumulator;
    internal int PausedTicks;
    internal readonly Queue<short> Audio = new();
    internal readonly Dictionary<string, int> LastValues = new();
}

public sealed record FusionEvent(long Tick, string Text);

/// <summary>Something that runs several games as one: a rule fusion or a scripted fusion game.</summary>
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

/// <summary>
/// Runs two or three emulated machines at the same time. Every tick (60 per second)
/// each machine advances by real time at its own frame rate, the player's inputs are
/// routed to the games the recipe says, the rules compare each RAM variable with its
/// value on the previous tick and write into the other games, and the pictures and
/// sounds are combined into one.
/// </summary>
public sealed class FusionSession : IFusionRun
{
    public const int TicksPerSecond = 60;

    private readonly List<CompiledRule> _rules = new();
    private readonly short[] _drain = new short[8192];
    private readonly Dictionary<string, bool> _inputs = new();
    private double _audioCarry;
    private readonly List<short> _mixed = new();
    private int _warmup = 30;

    public FusionRecipe Recipe { get; }
    public string Title => Recipe.Name;
    public string Description => Recipe.Description;
    public IReadOnlyList<FusionSlot> Slots { get; }
    public VideoFrame Video { get; }
    public List<FusionEvent> Events { get; } = new();
    public long Tick { get; private set; }
    public string BaseSlot { get; private set; }
    public double OverlayOpacity { get; set; }

    private sealed record CompiledRule(RecipeRule Source, RuleCondition? Condition)
    {
        public long LastFired = long.MinValue / 2;
    }

    public FusionSession(FusionRecipe recipe, IReadOnlyList<FusionSlot> slots)
    {
        Recipe = recipe;
        Slots = slots;
        BaseSlot = recipe.Layout.Base ?? slots[0].Id;
        OverlayOpacity = recipe.Layout.Opacity;
        foreach (var r in recipe.Rules)
            _rules.Add(new CompiledRule(r, r.When != null ? RuleCondition.Parse(r.When) : null));
        var size = Compositor.OutputSize(recipe.Layout.Mode, slots, BaseSlot);
        Video = new VideoFrame(size.width, size.height);
        foreach (var s in slots)
            foreach (var (name, v) in s.Profile.Variables)
                s.LastValues[name] = v.Read(s.Machine.Memory);
    }

    /// <summary>Builds the machines for a recipe and boots each game to playing.</summary>
    public static FusionSession Start(FusionRecipe recipe, IReadOnlyDictionary<string, GameSource> sources, string romDirectory, Action<string>? log = null)
    {
        var slots = new List<FusionSlot>();
        foreach (var g in recipe.Games)
        {
            var src = sources[g.Profile];
            var m = MachineFactory.Create(src, romDirectory);
            MachineFactory.Boot(m, src.Profile, log);
            slots.Add(new FusionSlot { Id = g.Slot, Profile = src.Profile, Machine = m });
        }
        return new FusionSession(recipe, slots);
    }

    public FusionSlot Slot(string id) => Slots.First(s => s.Id == id);

    /// <summary>Player input (left, right, fire, jump, start, coin...).</summary>
    public void SetInput(string input, bool pressed)
    {
        _inputs.TryGetValue(input, out bool was);
        if (was == pressed)
            return;
        _inputs[input] = pressed;
        if (!Recipe.Controls.TryGetValue(input, out var targets))
            return;
        foreach (var t in targets)
        {
            var parts = t.Split('.');
            var slot = Slots.FirstOrDefault(s => s.Id == parts[0]);
            if (slot != null && slot.Profile.Controls.TryGetValue(parts[1], out var key))
                slot.Machine.SetKey(key, pressed);
        }
    }

    /// <summary>Advances every game by <paramref name="seconds"/> of real time (one tick = 1/60 s).</summary>
    public void Update(double seconds)
    {
        Tick++;
        foreach (var s in Slots)
        {
            if (s.PausedTicks > 0)
            {
                s.PausedTicks--;
                continue;
            }
            s.Accumulator += seconds * s.Machine.FramesPerSecond;
            int guard = 0;
            while (s.Accumulator >= 1 && guard++ < 4)
            {
                s.Accumulator -= 1;
                s.Machine.RunFrame(null);
                int n = s.Machine.DrainAudio(_drain);
                for (int i = 0; i < n; i++)
                    s.Audio.Enqueue(_drain[i]);
                while (s.Audio.Count > 8820)
                    s.Audio.Dequeue();
            }
        }
        ApplyRules();
        MixAudio(seconds);
        Compositor.Compose(Recipe.Layout.Mode, Slots, BaseSlot, OverlayOpacity, Video);
    }

    private void ApplyRules()
    {
        var now = new Dictionary<(string slot, string variable), int>();
        foreach (var s in Slots)
            foreach (var (name, v) in s.Profile.Variables)
                now[(s.Id, name)] = v.Read(s.Machine.Memory);

        if (_warmup > 0)
            _warmup--;
        else
        {
            foreach (var rule in _rules)
            {
                bool fire;
                if (rule.Condition is { } c)
                {
                    var slot = Slots.FirstOrDefault(s => s.Id == c.Slot);
                    if (slot == null || !slot.LastValues.TryGetValue(c.Variable, out int before))
                        continue;
                    fire = c.Fires(before, now[(c.Slot, c.Variable)]);
                }
                else
                {
                    fire = rule.Source.Every is int every && Tick % every == 0;
                }
                if (!fire || Tick - rule.LastFired < rule.Source.Cooldown)
                    continue;
                rule.LastFired = Tick;
                foreach (var a in rule.Source.Do)
                    Execute(RuleAction.Parse(a), now);
                if (rule.Source.Message != null)
                    Events.Add(new FusionEvent(Tick, rule.Source.Message));
            }
        }
        // Remember the values after the rules wrote, so a rule's own write does not
        // retrigger another rule on the next tick.
        foreach (var s in Slots)
            foreach (var (name, v) in s.Profile.Variables)
                s.LastValues[name] = v.Read(s.Machine.Memory);
    }

    private void Execute(RuleAction a, Dictionary<(string, string), int> now)
    {
        switch (a.Kind)
        {
            case ActionKind.Set:
            case ActionKind.Add:
            {
                var slot = Slot(a.Slot!);
                var v = slot.Profile.Variables[a.Variable!];
                int current = v.Read(slot.Machine.Memory);
                int value = a.Kind == ActionKind.Add ? current + a.Amount
                    : a.SourceSlot != null ? now[(a.SourceSlot, a.SourceVariable!)] : a.Amount;
                v.Write(slot.Machine.Memory, value);
                break;
            }
            case ActionKind.Press:
            {
                var slot = Slot(a.Slot!);
                slot.Machine.QueueKeys(a.Amount, slot.Profile.Controls[a.Variable!]);
                break;
            }
            case ActionKind.Pause:
                Slot(a.Slot!).PausedTicks = a.Amount;
                break;
            case ActionKind.Swap:
                var others = Slots.Where(s => s.Id != BaseSlot).ToList();
                if (others.Count > 0)
                    BaseSlot = others[0].Id;
                break;
            case ActionKind.Message:
                Events.Add(new FusionEvent(Tick, a.Text ?? ""));
                break;
        }
    }

    private void MixAudio(double seconds)
    {
        _audioCarry += seconds * 44100;
        int n = (int)_audioCarry;
        _audioCarry -= n;
        float gain = 1f / Math.Max(1, Slots.Count) * 1.4f;
        for (int i = 0; i < n; i++)
        {
            int sum = 0;
            foreach (var s in Slots)
                if (s.Audio.Count > 0)
                    sum += s.Audio.Dequeue();
            _mixed.Add((short)Math.Clamp(sum * gain, -32767, 32767));
        }
    }

    public int DrainAudio(short[] destination)
    {
        int n = Math.Min(destination.Length, _mixed.Count);
        _mixed.CopyTo(0, destination, 0, n);
        _mixed.RemoveRange(0, n);
        return n;
    }

    /// <summary>Current values of every variable (for the UI and the AI).</summary>
    public IEnumerable<(string slot, string variable, int value)> Variables() =>
        Slots.SelectMany(s => s.Profile.Variables.Select(v => (s.Id, v.Key, v.Value.Read(s.Machine.Memory))));
}
