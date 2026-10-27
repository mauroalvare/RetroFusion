using Retro.Ai;

namespace Retro.App;

/// <summary>One whole creation, with time reserved for writing after research.</summary>
public static class FusionResearchPlan
{
    public static readonly TimeSpan TotalTime = TimeSpan.FromHours(1);
    public static readonly TimeSpan ResearchTime = TimeSpan.FromMinutes(50);
    public const int MaxPasses = 48;
    public const int NavigationRounds = 24;
    public const int TrialRounds = 3;

    public static int Calls(int originals, int writingAttempts) =>
        MaxPasses * (Math.Clamp(originals, 2, 3) * (NavigationRounds - TrialRounds + 2) + 1)
        + Math.Clamp(writingAttempts, 1, 10);

    public static AiTimeRange Estimate(int originals, bool needsResearch) => needsResearch
        ? new(TimeSpan.FromMinutes(originals >= 3 ? 30 : 20), TotalTime, 0)
        : new(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(20), 0);

    public static string EstimateText(int originals, bool needsResearch, AiTimeRange? observed = null)
    {
        var range = observed ?? Estimate(originals, needsResearch);
        var high = range.High > TotalTime ? TotalTime : range.High;
        var low = range.Low > high ? high : range.Low;
        string duration = high < TimeSpan.FromMinutes(1) ? "menos de un minuto"
            : $"{Math.Max(1, Math.Floor(low.TotalMinutes)):0}–{Math.Max(1, Math.Ceiling(high.TotalMinutes)):0} minutos";
        return $"Tiempo orientativo: {duration} para preparar, crear y comprobar. " +
            (range.Samples >= 3 ? "Basado en trabajos completos anteriores de esta configuración. " : "") +
            "Puede variar; el máximo de este trabajo es una hora.";
    }
}
