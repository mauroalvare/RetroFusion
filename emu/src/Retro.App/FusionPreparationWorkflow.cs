using System.Security.Cryptography;
using System.Text.Json;
using Retro.Ai;
using Retro.Compat;
using Retro.Core;
using Retro.Fusion;
using Retro.Studio;

namespace Retro.App;

public sealed record FusionPreparationTarget(string Path, GameSource Source, bool NeedsPreparation = true);
public sealed record FusionPreparationResult(GameConcept Concept, IReadOnlyDictionary<string, GameSource> Sources,
    IReadOnlyList<PrepareResult> Preparations, string Summary, IReadOnlyList<RecipeGame> Slots, IReadOnlyDictionary<string, string> Fingerprints,
    int ResearchPasses = 1, string StopReason = "")
{
    public void EnsureOriginalsUnchanged() => FusionPreparationWorkflow.EnsureUnchanged(Fingerprints);
}

/// <summary>Research, review the remaining requirements, and continue from observed play while time remains.</summary>
public sealed class FusionPreparationWorkflow
{
    public Func<string, PrepareOptions, Action<string>?, CancellationToken, Task<PrepareResult>> Prepare { get; init; }
        = GamePreparer.PrepareAsync;
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    public TimeSpan ResearchTime { get; init; } = FusionResearchPlan.ResearchTime;

    public async Task<FusionPreparationResult> RunAsync(GameConcept concept, IReadOnlyList<RecipeGame> slots,
        IReadOnlyList<FusionPreparationTarget> targets, string firmware, IPlayAdvisor? advisor,
        Func<GameConcept, IReadOnlyList<RecipeGame>, Func<string, GameProfile?>, CancellationToken, Task<GameConcept>>? review,
        bool economic, Action<string>? progress, CancellationToken ct, TimeSpan? availableTime = null,
        IReadOnlyList<PrepareResult>? priorResearch = null)
    {
        ct.ThrowIfCancellationRequested();
        if (targets.Count is < 2 or > 3 || targets.Select(t => t.Source.Profile.Id).Distinct().Count() != targets.Count)
            throw new InvalidDataException("Elegí dos o tres originales distintos para preparar la idea.");
        if (slots.Count != targets.Count || slots.Any(s => !targets.Any(t => t.Source.Profile.Id == s.Profile)))
            throw new InvalidDataException("Cambió la selección de originales. Volvé a elegir la idea.");
        TimeSpan budget = availableTime ?? ResearchTime;
        if (budget <= TimeSpan.Zero || budget > FusionResearchPlan.TotalTime)
            throw new ArgumentOutOfRangeException(nameof(ResearchTime));
        var fingerprints = targets.ToDictionary(t => t.Path, t => Fingerprint(t.Path));
        var sources = targets.ToDictionary(t => t.Source.Profile.Id, t => t.Source);
        var latest = new Dictionary<string, PrepareResult>();
        var mappedIds = targets.ToDictionary(t => t.Source.Profile.Id, t => t.Source.Profile.Id);
        var history = targets.ToDictionary(t => t.Source.Profile.Id, _ => new List<PreparationExperimentResult>());
        foreach (var target in targets)
        {
            string id = target.Source.Profile.Id;
            var saved = priorResearch?.FirstOrDefault(r => r.Ok && r.Profile?.Id == id &&
                target.Source.Files.Select(GamePreparer.Sha256Of).Contains(r.Sha256, StringComparer.OrdinalIgnoreCase));
            if (saved == null) continue;
            latest[id] = saved;
            history[id].AddRange(saved.Experiments.TakeLast(144));
            if (saved.ProfilePath is { } path) fingerprints[path] = Fingerprint(path);
        }
        GameConcept current = concept;
        int passes = 0, stalled = 0;
        string stop = "";
        bool automatic = review != null && advisor is IPreparationExperimentAdvisor;
        using var timeout = new CancellationTokenSource(budget, Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var token = linked.Token;
        long started = Clock.GetTimestamp();
        List<RecipeGame> CurrentSlots() => slots.Select(s => new RecipeGame { Slot = s.Slot, Profile = mappedIds[s.Profile] }).ToList();
        FusionPreparationResult Finish()
        {
            ct.ThrowIfCancellationRequested();
            EnsureUnchanged(fingerprints);
            foreach (var pair in latest) pair.Value.Experiments = history[pair.Key].TakeLast(144).ToList();
            var results = latest.Values.ToList();
            string summary = string.Join("\n", results.Select(r =>
                $"{r.Game}: {r.Profile?.Variables.Count(v => !v.Value.ReadOnly) ?? 0} variables con pruebas de escritura. " +
                string.Join(" ", r.CheckedData.TakeLast(4))));
            if (stop.Length > 0) summary += "\n" + stop;
            return new(current, sources.Values.ToDictionary(s => s.Profile.Id), results, summary, CurrentSlots(), fingerprints, passes, stop);
        }
        try
        {
            for (int pass = 1; pass <= (automatic ? FusionResearchPlan.MaxPasses : 1); pass++)
            {
                token.ThrowIfCancellationRequested();
                if (Clock.GetElapsedTime(started) >= budget) { stop = "Se alcanzó el tiempo de investigación. Se conservaron los datos comprobados; faltan requisitos."; break; }
                passes = pass;
                string before = EvidenceStamp(sources.Values);
                bool newExperiment = false;
                foreach (var target in targets.Where(t => pass > 1 || t.NeedsPreparation))
                {
                    token.ThrowIfCancellationRequested();
                    EnsureUnchanged(fingerprints);
                    string id = target.Source.Profile.Id;
                    var source = sources[id];
                    latest.TryGetValue(id, out var previous);
                    progress?.Invoke($"Investigación {pass}: {source.Profile.Name}. Faltantes: {current.MissingData}");
                    var result = await Prepare(target.Path, new PrepareOptions
                    {
                        FirmwareDirectory = firmware, Advisor = advisor,
                        Goal = Goal(current), PreviousProfile = source.Profile,
                        Resume = previous?.Continuation, PreviousExperiments = history[id].ToArray(),
                        ContinueResearch = automatic, InvestigationPass = pass, TrialRounds = FusionResearchPlan.TrialRounds,
                        MaxRounds = FusionResearchPlan.NavigationRounds,
                    }, line => progress?.Invoke($"{source.Profile.Name}: {line}"), token);
                    // Only this original's own profile may have been written by Prepare.
                    EnsureUnchanged(fingerprints.Where(p => p.Key != previous?.ProfilePath).ToDictionary());
                    latest[id] = result;
                    history[id].AddRange(result.Experiments);
                    newExperiment |= result.Experiments.Any(e => !e.Skipped);
                    if (!result.Ok || result.Profile == null)
                    {
                        stop = $"No se pudo completar la preparación de {source.Profile.Name}: {result.Problem ?? "no se verificó un arranque reproducible"}";
                        return Finish();
                    }
                    var profile = result.Profile;
                    if (profile.Machine != target.Source.Profile.Machine ||
                        !source.Files.Select(GamePreparer.Sha256Of).Contains(result.Sha256, StringComparer.OrdinalIgnoreCase) ||
                        !profile.Match.Sha256.Contains(result.Sha256, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException("Los datos preparados corresponden a otra edición. La idea anterior se conserva.");
                    mappedIds[id] = profile.Id;
                    sources[id] = source with { Profile = profile };
                    if (result.ProfilePath is { } profilePath) fingerprints[profilePath] = Fingerprint(profilePath);
                    token.ThrowIfCancellationRequested();
                    if (Clock.GetElapsedTime(started) >= budget)
                    {
                        stop = "Se alcanzó el tiempo de investigación. Se conservaron los datos comprobados y los requisitos pendientes.";
                        return Finish();
                    }
                }
                token.ThrowIfCancellationRequested();
                EnsureUnchanged(fingerprints);
                var updatedSlots = CurrentSlots();
                if (updatedSlots.Select(s => s.Profile).Distinct().Count() != updatedSlots.Count)
                    throw new InvalidDataException("Los originales preparados coinciden en la misma edición. Revisá la selección.");
                if (review == null) break;
                progress?.Invoke($"Revisando los datos de la misma idea después de la investigación {pass}...");
                var reviewed = await review(current, updatedSlots, id => sources.Values.FirstOrDefault(s => s.Profile.Id == id)?.Profile, token);
                token.ThrowIfCancellationRequested();
                EnsureUnchanged(fingerprints);
                if (Clock.GetElapsedTime(started) >= budget)
                {
                    stop = "Se alcanzó el tiempo de investigación. Se conservaron los datos comprobados y los requisitos pendientes.";
                    return Finish();
                }
                current = reviewed;
                if (string.IsNullOrWhiteSpace(current.MissingData)) break;
                if (!automatic) break;
                bool changed = before != EvidenceStamp(sources.Values);
                stalled = changed || newExperiment ? 0 : stalled + 1;
                if (stalled >= 3)
                {
                    stop = "La IA no encontró una prueba nueva ni datos nuevos en tres rondas. Se conservaron los avances y los requisitos pendientes.";
                    break;
                }
                progress?.Invoke($"Todavía hay datos por comprobar. Continúo la investigación desde la partida observada: {current.MissingData}");
                if (pass == FusionResearchPlan.MaxPasses)
                    stop = "Se completaron las rondas de investigación. Se conservaron los avances; la idea sigue requiriendo los datos indicados.";
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            stop = "Se alcanzó el tiempo de investigación. Se conservaron los datos de las rondas completadas y los requisitos pendientes.";
        }
        ct.ThrowIfCancellationRequested();
        EnsureUnchanged(fingerprints);
        return Finish();
    }

    private static string Goal(GameConcept concept)
    {
        static string Limit(string text, int length) => text.Length <= length ? text : text[..length];
        // Missing requirements precede long prose so the preparer's context limit cannot discard them.
        return $"Datos que faltan: {Limit(concept.MissingData, 1400)}\nIdea: {Limit(concept.Title, 160)}\n" +
            $"Pedido del jugador: {Limit(concept.PlayerRequest, 700)}\nAportes: {Limit(concept.Pitch, 600)}\nDiseño: {Limit(concept.Design, 800)}";
    }

    internal static string EvidenceStamp(IEnumerable<GameSource> sources) => JsonSerializer.Serialize(sources.Select(s => new
    {
        s.Profile.Id, s.Profile.Machine,
        Variables = s.Profile.Variables.OrderBy(v => v.Key).Select(v => new { v.Key, v.Value.Address, v.Value.Encoding, v.Value.Bytes,
            v.Value.Mask, v.Value.HighBitAddress, v.Value.HighBitMask, v.Value.ReadOnly, v.Value.Min, v.Value.Max }),
        Controls = s.Profile.Controls.OrderBy(c => c.Key), s.Profile.Helpers, s.Profile.HelpersDoc, s.Profile.Sprites,
    }));

    private static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static void EnsureUnchanged(IReadOnlyDictionary<string, string> fingerprints)
    {
        foreach (var pair in fingerprints)
            if (Fingerprint(pair.Key) != pair.Value)
                throw new InvalidDataException("Un original o perfil cambió durante la preparación. No se usaron datos de otra edición.");
    }
}
