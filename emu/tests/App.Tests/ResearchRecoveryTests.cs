using System.Text.Json;
using Retro.App;
using Retro.Compat;
using Retro.Studio;

namespace App.Tests;

public sealed class ResearchRecoveryTests
{
    [Test]
    public void LegacyImportRecoversOnlySharedGoalsWithoutInventingBaseOrExecutable()
    {
        string folder=Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),"retro-legacy-"+Guid.NewGuid())).FullName;
        try
        {
            var report=new LabReport {Notes=["Revisar qué requisitos están comprobados. Datos que faltan: Cadencia del reloj\nIdea: Un vuelo propio\nDiseño: Conservar el recorrido"]};
            report.Save(Path.Combine(folder,"one.json"));
            report.Notes=["Revisar qué requisitos están comprobados. Datos que faltan: Acumulación y préstamo\nIdea: Un vuelo propio\nDiseño: Conservar el recorrido"];
            report.Save(Path.Combine(folder,"two.json"));
            File.WriteAllText(Path.Combine(folder,"broken.json"),"{");
            Assert.That(LegacyResearchImport.Read(["one"],folder),Is.Empty);
            var recovered=LegacyResearchImport.Read(["one","two","broken","../outside"],folder).Single();
            Assert.That(recovered.Title,Is.EqualTo("Un vuelo propio"));
            Assert.That(recovered.Goal,Does.Contain("Cadencia del reloj"));
            Assert.That(recovered.Goal,Does.Contain("Acumulación y préstamo"));
            Assert.That(recovered.Profiles,Is.EquivalentTo(new[]{"one","two"}));
            Assert.That(File.ReadAllText(Path.Combine(folder,"broken.json")),Is.EqualTo("{"));
        }
        finally { Directory.Delete(folder,true); }
    }

    [Test]
    public void GuidanceCoversClockPhysicsActorsAndResetWithStepsAndEvidence()
    {
        var idea=new GameConcept("Prueba","","","",MissingData:"reloj, acarreo, salto, cámara, bombas, formación, muerte, reinicio");
        var guides=ResearchGuidance.For(idea);
        Assert.That(guides,Has.Count.EqualTo(4));
        Assert.That(guides.All(g=>g.Steps.Count>=3 && g.Evidence.Length>40),Is.True);
        string markdown=ResearchGuidance.Markdown(new() {Concept=idea});
        Assert.That(markdown,Does.Contain(idea.MissingData).And.Contain("Partidas guardadas").And.Contain("Buscar variables"));
        Assert.That(ResearchGuidance.For(idea with {MissingData="Campo desconocido sin descripción"}),Has.Count.EqualTo(1));
    }

    [Test]
    public void AnUnknownRequirementStartsWithAnAutomaticExperimentAndOnlyThenOffersAHumanObservation()
    {
        var guide = ResearchGuidance.For(new GameConcept("Propio", "", "", "", MissingData: "Efecto desconocido del escudo")).Single();
        Assert.That(guide.Steps[0], Does.Contain("Investigación automática"));
        Assert.That(string.Join("\n", guide.Steps), Does.Contain("Si").And.Contain("sin esa acción"));
    }

    [Test]
    public void InterfaceOwnsGamepadWhileResearchOrEditorsAreOpen()
    {
        var input=new InputMapper {InterfaceActive=true};
        Assert.That(input.GamepadReady(true,_=>true),Is.False);
        input.InterfaceActive=false;
        Assert.That(input.GamepadReady(true,_=>true),Is.True);
        input.HoldGamepadUntilReleased(); input.InterfaceActive=true;
        Assert.That(input.GamepadReady(true,_=>false),Is.False);
        input.InterfaceActive=false;
        Assert.That(input.GamepadReady(true,_=>true),Is.True,"Releasing a navigation chord while in the interface should permit the next new press.");
    }

    [Test]
    public void NesVariableSearchExcludesIoRomAndMirrorsOfTheSameWorkRam()
    {
        var range=CodeWindow.SearchRange("nes");
        Assert.That(range.start,Is.Zero);
        Assert.That(range.end,Is.EqualTo(0x07ff));
        Assert.That(range.end,Is.LessThan(0x2000),"The PPU/controller register area and cartridge ROM must not become variable candidates.");
    }
}
