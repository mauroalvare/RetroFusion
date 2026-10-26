using System.Security.Cryptography;
using Retro.Ai;
using Retro.Compat;
using Retro.Core;
using Retro.Fusion;
using Retro.Studio;

namespace Retro.App;

public sealed record FusionPreparationTarget(string Path, GameSource Source, bool NeedsPreparation = true);
public sealed record FusionPreparationResult(GameConcept Concept, IReadOnlyDictionary<string, GameSource> Sources,
    IReadOnlyList<PrepareResult> Preparations, string Summary, IReadOnlyList<RecipeGame> Slots, IReadOnlyDictionary<string, string> Fingerprints)
{
    public void EnsureOriginalsUnchanged() => FusionPreparationWorkflow.EnsureUnchanged(Fingerprints);
}

/// <summary>One bounded pass of play/experiments followed by a fresh review of the same idea.</summary>
public sealed class FusionPreparationWorkflow
{
    public Func<string, PrepareOptions, Action<string>?, CancellationToken, Task<PrepareResult>> Prepare { get; init; }
        = GamePreparer.PrepareAsync;

    public async Task<FusionPreparationResult> RunAsync(GameConcept concept, IReadOnlyList<RecipeGame> slots,
        IReadOnlyList<FusionPreparationTarget> targets, string firmware, IPlayAdvisor? advisor,
        Func<GameConcept, IReadOnlyList<RecipeGame>, Func<string, GameProfile?>, CancellationToken, Task<GameConcept>>? review,
        bool economic, Action<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (targets.Count is < 2 or > 3 || targets.Select(t => t.Source.Profile.Id).Distinct().Count() != targets.Count)
            throw new InvalidDataException("Elegí dos o tres originales distintos para preparar la idea.");
        if (slots.Count != targets.Count || slots.Any(s => !targets.Any(t => t.Source.Profile.Id == s.Profile)))
            throw new InvalidDataException("Cambió la selección de originales. Volvé a elegir la idea.");
        var fingerprints = targets.ToDictionary(t => t.Path, t => Fingerprint(t.Path));
        var sources = targets.ToDictionary(t => t.Source.Profile.Id, t => t.Source);
        var results = new List<PrepareResult>();
        var mappedIds = targets.ToDictionary(t => t.Source.Profile.Id, t => t.Source.Profile.Id);
        string goal = $"Idea: {concept.Title}\nAportes: {concept.Pitch}\nDiseño: {concept.Design}\nDatos que requiere: {concept.MissingData}";
        foreach (var target in targets.Where(target => target.NeedsPreparation))
        {
            ct.ThrowIfCancellationRequested();
            EnsureUnchanged(fingerprints);
            progress?.Invoke($"Preparando {target.Source.Profile.Name}: jugando y comprobando los datos de esta idea...");
            var result = await Prepare(target.Path, new PrepareOptions
            {
                FirmwareDirectory = firmware, Advisor = advisor, Goal = goal,
                TrialRounds = 3, MaxRounds = economic ? 6 : 8,
            }, line => progress?.Invoke($"{target.Source.Profile.Name}: {line}"), ct);
            ct.ThrowIfCancellationRequested();
            EnsureUnchanged(fingerprints);
            results.Add(result);
            if (!result.Ok || result.Profile == null)
                return new(concept, sources, results, $"No se pudo completar la preparación de {target.Source.Profile.Name}: {result.Problem ?? "no se verificó un arranque reproducible"}",
                    slots.Select(slot => new RecipeGame { Slot = slot.Slot, Profile = mappedIds[slot.Profile] }).ToList(), fingerprints);
            var profile = result.Profile;
            var hashes = target.Source.Files.Select(GamePreparer.Sha256Of);
            if (profile.Machine != target.Source.Profile.Machine ||
                !hashes.Contains(result.Sha256, StringComparer.OrdinalIgnoreCase) ||
                !profile.Match.Sha256.Contains(result.Sha256, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Los datos preparados corresponden a otra edición. La idea anterior se conserva.");
            mappedIds[target.Source.Profile.Id] = profile.Id;
            sources[target.Source.Profile.Id] = target.Source with { Profile = profile };
            if (result.ProfilePath is { } profilePath) fingerprints[profilePath] = Fingerprint(profilePath);
        }
        ct.ThrowIfCancellationRequested();
        EnsureUnchanged(fingerprints);
        var updatedSlots = slots.Select(slot => new RecipeGame { Slot = slot.Slot, Profile = mappedIds[slot.Profile] }).ToList();
        if (updatedSlots.Select(slot => slot.Profile).Distinct().Count() != updatedSlots.Count)
            throw new InvalidDataException("Los originales preparados coinciden en la misma edición. Revisá la selección.");
        sources = sources.Values.ToDictionary(source => source.Profile.Id);
        GameConcept reviewed = concept;
        if (review != null)
        {
            progress?.Invoke("Revisando la misma idea con los datos comprobados...");
            reviewed = await review(concept, updatedSlots, id => sources.GetValueOrDefault(id)?.Profile, ct);
            ct.ThrowIfCancellationRequested();
            EnsureUnchanged(fingerprints);
        }
        string summary = string.Join("\n", results.Select(r =>
            $"{r.Game}: {r.Profile?.Variables.Count(v => !v.Value.ReadOnly) ?? 0} variables verificadas." +
            (r.CheckedData.Count > 0 ? " " + string.Join(" ", r.CheckedData) : "")));
        return new(reviewed, sources, results, summary, updatedSlots, fingerprints);
    }

    private static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static void EnsureUnchanged(IReadOnlyDictionary<string, string> fingerprints)
    {
        foreach (var pair in fingerprints)
            if (Fingerprint(pair.Key) != pair.Value)
                throw new InvalidDataException("Un original cambió durante la preparación. No se usaron datos de otra edición.");
    }
}
