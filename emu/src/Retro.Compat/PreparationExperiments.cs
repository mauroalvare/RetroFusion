using System.Text;
using Retro.Core;

namespace Retro.Compat;

/// <summary>Observed context for declarative, bounded input experiments. No host code or addresses.</summary>
public sealed class PreparationExperimentQuestion
{
    public string Goal { get; init; } = "";
    public string Game { get; init; } = "";
    public string Machine { get; init; } = "";
    public string MachineName { get; init; } = "";
    public VideoFrame? Picture { get; init; }
    public ControlSet? Controls { get; init; }
    public IReadOnlyList<string> Keys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<VariableCandidate> Candidates { get; init; } = Array.Empty<VariableCandidate>();

    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine($"Juego: {Game}; máquina: {MachineName}.");
        text.AppendLine("Objetivo de esta preparación: " + Goal);
        text.AppendLine("Teclas disponibles: " + string.Join(", ", Keys));
        if (Controls != null) text.AppendLine($"Controles comprobados: {Controls.Name}; {string.Join(", ", Controls.Keys)}.");
        foreach (var candidate in Candidates)
            text.AppendLine("Dato observado $" + candidate.Address + $": {candidate.Guess}, {candidate.Encoding}, {candidate.Bytes} byte(s). {candidate.Evidence}");
        text.AppendLine("Proponé entradas para provocar los cambios que faltan. El laboratorio buscará candidatos y repetirá cada prueba; una diferencia no demuestra su significado.");
        text.AppendLine("Límites: 3 experimentos; 30–900 cuadros por experimento; 24 pasos; hold 1–180; frames dentro del experimento.");
        text.AppendLine("Kind: score, level, ammo, item o change. No se aceptan direcciones, escrituras, archivos, comandos ni código.");
        return text.ToString();
    }
}

public interface IPreparationExperimentAdvisor
{
    Task<List<LabExperiment>> PlanExperimentsAsync(PreparationExperimentQuestion question, CancellationToken ct);
}

/// <summary>Validation is repeated by the host even if the provider already checked its response.</summary>
public static class PreparationExperiments
{
    public const int MaxExperiments = 3;
    public const int MaxFrames = 900;
    public const int MaxSteps = 24;
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
        { "score", "level", "ammo", "item", "change" };

    public static bool TryValidate(LabExperiment? experiment, IReadOnlyCollection<string> keys, out string problem)
    {
        problem = "";
        if (experiment == null || !Kinds.Contains(experiment.Kind ?? "") ||
            experiment.Frames is < 30 or > MaxFrames || experiment.RepeatEvery < 0 ||
            experiment.RepeatEvery > experiment.Frames || experiment.Steps == null ||
            experiment.Steps.Count is < 1 or > MaxSteps || (experiment.Description?.Length ?? 0) > 600)
        { problem = "El experimento excede los límites de tipo, cuadros o acciones."; return false; }
        foreach (var step in experiment.Steps)
        {
            string[] requested = (step?.Keys ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (step == null || step.Frame < 0 || step.Frame >= experiment.Frames ||
                step.Hold is < 1 or > 180 || step.Hold > experiment.Frames - step.Frame ||
                requested.Length is < 1 or > 8 || requested.Any(key => !keys.Contains(key, StringComparer.OrdinalIgnoreCase)))
            { problem = "El experimento usa una tecla o una duración que no existe en esta máquina."; return false; }
        }
        return true;
    }
}
