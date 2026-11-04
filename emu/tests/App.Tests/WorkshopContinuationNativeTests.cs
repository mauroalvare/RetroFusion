using System.Text.RegularExpressions;
using Retro.App;
using Retro.Core;
using Retro.Studio;

namespace App.Tests;

/// <summary>A saved research that stopped for a missing check, listed and resumed from the real terminal.</summary>
[NonParallelizable]
public sealed class WorkshopContinuationNativeTests
{
    private const string Reason = "Requiere gráficos nativos: ventana oculta, audio silenciado y datos propios.";
    [TearDown] public void SpanishAgain() => UiText.Language = "es";

    private static string Flat(ProgramTerminal console) => Regex.Replace(console.Output.AllText(), @"\s+", " ");

    [Explicit(Reason)]
    [TestCase("es"), TestCase("en")]
    public void ABlockedResearchIsListedAsBlockedAndResumeWorkOpensIt(string language)
    {
        using var ui = new NativeHarness(1280, 840, 100, "real", 6, "--language", language);
        var games = Directory.GetFiles(ui.Games, "*.nes").Order().Take(2).ToArray();
        var work = new FusionResearchWork
        {
            Status = "blocked", Summary = "Falta comprobar el salto del segundo original.",
            Concept = new GameConcept("Idea propia frenada", "Dos originales propios", "", "") { MissingData = "Comprobar el salto" },
            Originals = games.Select((path, i) => new ResearchOriginal(i == 0 ? "a" : "b", path, new string('A', 64), "propio-" + i, "")).ToList(),
            Slots = [new Retro.Fusion.RecipeGame { Slot = "a", Profile = "propio-0" }, new Retro.Fusion.RecipeGame { Slot = "b", Profile = "propio-1" }]
        };
        var studio = (StudioWindow)ui.Get("_studio");
        var store = studio.ResearchStore;
        store.Save(work);
        Assert.That(store.Folder, Does.StartWith(ui.Games), "The research lives where the workshop keeps it, inside the harness folders.");
        var console = (ProgramTerminal)ui.Property("Terminal");
        ui.Enter(language == "es" ? "proyectos" : "projects"); ui.Frames(2);
        Assert.That(Flat(console), Does.Contain("Idea propia frenada").And.Contain(language == "es" ? "bloqueada: falta una comprobación" : "blocked: a check is missing"));
        ui.Enter((language == "es" ? "retomar-trabajo" : "resume-work") + " Idea propia frenada"); ui.Frames(4);
        Assert.That(Flat(console), Does.Contain(language == "es" ? "Taller abierto" : "Workshop open"));
        Assert.That(studio.IsOpen, Is.True);
        var opened = (FusionResearchWork?)typeof(StudioWindow).GetField("_researchWork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(studio);
        Assert.That(opened?.Id, Is.EqualTo(work.Id), "The workshop opens that research, even when it was saved after the workshop read its list.");
        Assert.That(typeof(StudioWindow).GetField("_problem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(studio), Is.Null);
        // The verification plan follows the interface language (its texts are in the catalog).
        var checks = ResearchGuidance.For(work.Concept);
        Assert.That(checks, Is.Not.Empty);
        if (language == "en") Assert.That(UiText.Get(checks[0].Title), Is.Not.EqualTo(checks[0].Title));
        ui.Bounds.Clear(); ui.Frames(2);
        ui.Capture("taller-investigacion-bloqueada-" + language);
        Assert.That(Flat(console), Does.Not.Contain("ya está terminado").And.Not.Contain("is already finished"));
    }
}
