using Retro.Fusion;

namespace Retro.Studio;

/// <summary>Inputs and observations only. No test code, writes, raw addresses or saved states.</summary>
public sealed class StudioPlaytest
{
    public string Name { get; set; } = "";
    public List<PlaytestStep> Steps { get; set; } = [];
    public List<PlaytestProbe> Checks { get; set; } = [];
}

public sealed class PlaytestStep
{
    public int Ticks { get; set; }
    public List<string> Inputs { get; set; } = [];
}

/// <summary>The host measures a delta between two ticks, including tick zero after init.</summary>
public sealed class PlaytestProbe
{
    public string Slot { get; set; } = "";
    public string Variable { get; set; } = "";
    public int FromTick { get; set; }
    public int AtTick { get; set; }
    public long Min { get; set; }
    public long Max { get; set; }
}

public sealed record PlaytestObservation(string Slot, string Variable, int FromTick, int AtTick,
    int Before, int After, long Delta, long Min, long Max, bool Ok, string Purpose);
public sealed record PlaytestResult(string Name, bool Ok, int Ticks, string? Error,
    IReadOnlyList<PlaytestObservation> Observations);

/// <summary>Repeatable checks on real original memory, independent of game.score/log claims.</summary>
public static class StudioPlaytests
{
    public const int MaxPlans = 3;
    public const int MaxTicks = 5400;

    public static string? CheckShape(IReadOnlyList<StudioPlaytest>? plans)
    {
        if (plans == null || plans.Count > MaxPlans) return "Se admiten hasta tres recorridos dirigidos.";
        foreach (var plan in plans)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.Name) || plan.Name.Length > 120 ||
                plan.Steps == null || plan.Steps.Count is < 1 or > 64 || plan.Checks == null || plan.Checks.Count is < 1 or > 12)
                return "Cada recorrido necesita nombre, 1–64 pasos y 1–12 observaciones de memoria.";
            int total = 0;
            foreach (var step in plan.Steps)
            {
                if (step == null || step.Ticks is < 1 or > 600 || step.Inputs == null || step.Inputs.Count > StudioSession.InputNames.Length ||
                    step.Inputs.Any(i => !StudioSession.InputNames.Contains(i, StringComparer.Ordinal)) || step.Inputs.Distinct().Count() != step.Inputs.Count)
                    return "El recorrido usa una entrada desconocida o un paso fuera de 1–600 ticks.";
                total += step.Ticks;
            }
            if (total > MaxTicks) return "Cada recorrido puede durar hasta 5400 ticks (90 segundos).";
            foreach (var check in plan.Checks)
                if (check == null || string.IsNullOrWhiteSpace(check.Slot) || check.Slot.Length > 32 ||
                    string.IsNullOrWhiteSpace(check.Variable) || check.Variable.Length > 120 ||
                    check.FromTick < 0 || check.AtTick <= check.FromTick || check.AtTick > total || check.Min > check.Max ||
                    check.Min < -4294967295L || check.Max > 4294967295L)
                    return "La observación necesita slot/variable, dos ticks ordenados dentro del recorrido y un intervalo válido de cambio.";
        }
        return null;
    }

    public static IReadOnlyList<PlaytestResult> Run(StudioSession session, CancellationToken ct = default)
    {
        var plans = session.Game.Playtests;
        var results = new List<PlaytestResult>();
        try
        {
            if (CheckShape(plans) is { } invalid) return [new("Plan de comprobación", false, 0, invalid, [])];
            foreach (var plan in plans)
            {
                ct.ThrowIfCancellationRequested();
                // Resolve every observation before executing inputs. Never accept addresses or guesses as variables.
                var probes = new List<(FusionSlot Slot, GameVariable Variable)>();
                string? error = null;
                foreach (var check in plan.Checks)
                {
                    var slot = session.Slots.FirstOrDefault(s => s.Id == check.Slot);
                    if (slot == null || !slot.Profile.Variables.TryGetValue(check.Variable, out var variable))
                    { error = $"No existe games.{check.Slot}.{check.Variable} en los perfiles actuales."; break; }
                    probes.Add((slot, variable));
                }
                if (error != null) { results.Add(new(plan.Name, false, 0, error, [])); continue; }
                session.Restart();
                int tick = 0;
                var before = new int[probes.Count];
                var observations = new List<PlaytestObservation>();
                void Observe()
                {
                    for (int i = 0; i < probes.Count; i++)
                    {
                        var check = plan.Checks[i]; var probe = probes[i];
                        if (check.FromTick == tick) before[i] = probe.Variable.Read(probe.Slot.Machine.Memory);
                        if (check.AtTick != tick) continue;
                        int after = probe.Variable.Read(probe.Slot.Machine.Memory);
                        long delta = (long)after - before[i];
                        observations.Add(new(check.Slot, check.Variable, check.FromTick, tick, before[i], after, delta,
                            check.Min, check.Max, delta >= check.Min && delta <= check.Max, probe.Variable.PurposeSummary));
                    }
                }
                Observe();
                foreach (var step in plan.Steps)
                {
                    foreach (string input in StudioSession.InputNames) session.SetInput(input, step.Inputs.Contains(input));
                    for (int i = 0; i < step.Ticks; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (session.Error != null) { error = session.Error; break; }
                        session.Update(1.0 / StudioSession.TicksPerSecond); tick++;
                        if (session.Error != null || session.SceneViolation)
                        { error = session.Error ?? session.SceneProblem ?? StudioValidator.OwnSceneMessage; break; }
                        Observe();
                    }
                    if (error != null) break;
                }
                results.Add(new(plan.Name, error == null && observations.Count == probes.Count && observations.All(o => o.Ok),
                    tick, error, observations));
            }
        }
        finally
        {
            foreach (string input in StudioSession.InputNames) session.SetInput(input, false);
            foreach (var slot in session.Slots) slot.Machine.ReleaseAllKeys();
        }
        return results;
    }
}
