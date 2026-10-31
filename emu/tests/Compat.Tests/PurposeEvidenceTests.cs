using Retro.Compat;
using Retro.Fusion;

namespace Compatibility.Tests;

[TestFixture]
public sealed class PurposeEvidenceTests
{
    [Test]
    public void AWriteEffectAndAnAdvisorsNameDoNotProvePurposeOrConstrainItsRange()
    {
        var lab = new LabReport();
        lab.Findings.Add(new() { Kind = "lives", Address = "6000", Status = "verified", Confidence = .9 });
        var prepared = new PrepareResult { Game = "Programa propio", Sha256 = new string('a', 64), Machine = "zx48" };
        var controls = PlayDetector.SetsFor("zx48")[0];
        var automatic = GamePreparer.BuildProfile(prepared, "Spectrum", lab, controls);
        Assert.That(automatic.Variables["lives"].PurposeIsHypothesis, Is.True);
        Assert.That(automatic.Variables["lives"].Max, Is.EqualTo(255), "A guessed name cannot establish a nine-life limit");
        var named = GamePreparer.BuildProfile(prepared, "Spectrum", lab, controls,
            [new VariableName("6000", "lives", "Intentos según la IA", true)]);
        var roundtrip = GameProfile.Parse(named.ToJson()).Variables["lives"];
        Assert.That(roundtrip.PurposeIsHypothesis, Is.True);
        Assert.That(roundtrip.PurposeSummary, Does.Contain("falta comprobar"));
        Assert.That(roundtrip.ReadOnly, Is.False, "The measured write effect remains separate from meaning");
        Assert.That(roundtrip.Max, Is.EqualTo(255));
        Assert.That(new GameVariable { Description = "Nombre propuesto por la IA: energía" }.PurposeIsHypothesis, Is.True,
            "Older automatically named profiles must not silently become certified");
    }
}
