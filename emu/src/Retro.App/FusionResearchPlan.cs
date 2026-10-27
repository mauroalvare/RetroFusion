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

    public static string EstimateText(int originals, bool needsResearch)
    {
        var range = Estimate(originals, needsResearch);
        return $"Tiempo orientativo: {range.Low.TotalMinutes:0}–{range.High.TotalMinutes:0} minutos para preparar, crear y comprobar. " +
            "Puede terminar antes; depende de los datos y de la IA.";
    }
}
