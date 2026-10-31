using Retro.App;
using Retro.Fusion;

namespace App.Tests;

[TestFixture]
public sealed class FusionPresentationTests
{
    [Test]
    public void LiveHudAndRecentMessagesReachThePlayerWithoutStaleOrFutureEvents()
    {
        var events = new List<FusionEvent> { new(10, "Impacto: -5s"), new(500, "Futuro") };
        Assert.That(FusionPresentation.Status("Cupo 4/10s", events, 15), Is.EqualTo("Impacto: -5s · Cupo 4/10s"));
        Assert.That(FusionPresentation.Status("Cupo 4/10s", events, 191), Is.EqualTo("Cupo 4/10s"));
        Assert.That(FusionPresentation.Status("Cupo 0/10s", events, 0), Is.EqualTo("Cupo 0/10s"));
        Assert.That(FusionPresentation.Status("", events, 15), Is.EqualTo("Impacto: -5s"));
    }
}
