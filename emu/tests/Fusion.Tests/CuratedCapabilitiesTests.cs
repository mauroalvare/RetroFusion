using Retro.Core;
using Retro.Fusion;

namespace Fusion.Tests;

public sealed class CuratedCapabilitiesTests
{
    private static GameSource Original()
    {
        var found = TestGames.Find() ?? throw new IgnoreException("Falta la edición local para comprobar funciones curadas.");
        return found.sources["invaders"];
    }

    private static GameProfile Alias() => new() { Id = "automatic-alias", Name = "Alias propio", Machine = "invaders",
        Boot = ["wait 37"], Variables = new() { ["x"] = new() { Address = "201B", ReadOnly = true } } };

    [Test]
    public void AnAutomaticAliasRetainsItsIdentityAndRecoversKnownCapabilities()
    {
        var source = Original(); var alias = Alias();
        var result = CuratedCapabilities.Resolve(alias, source.Files);
        Assert.That(result.Id, Is.EqualTo(alias.Id)); Assert.That(result.Boot, Is.EqualTo(alias.Boot));
        Assert.That(result.Variables["x"].ReadOnly, Is.True);
        Assert.That(result.HelpersDoc.Any(line => line.Contains("killAlien")), Is.True);
        Assert.That(result.Sprites, Is.Not.Empty); Assert.That(alias.Helpers, Is.Empty);
        result.Variables["ships"].Description = "Cambio local";
        Assert.That(ProfileLibrary.BuiltIn.Single(p => p.Id == "invaders").Variables["ships"].Description, Is.Not.EqualTo("Cambio local"));
    }

    [Test]
    public void AChangedChipCannotInheritKnownAddressesEvenWhenTheFirstChipStillMatches()
    {
        var source = Original(); var files = source.Files.Select(file => file with { Data = file.Data.ToArray() }).ToArray();
        files.Single(file => file.Name == "invaders.g").Data[16] ^= 1;
        Assert.That(files.Single(file=>file.Name=="invaders.h").Data,Is.EqualTo(source.Files.Single(file=>file.Name=="invaders.h").Data));
        var alias = Alias(); Assert.That(CuratedCapabilities.Resolve(alias, files), Is.SameAs(alias));
        Assert.That(alias.Helpers, Is.Empty);
    }

    [Test]
    public void UnknownOrCustomCapabilitiesAreNeverReplaced()
    {
        var alias = Alias();
        Assert.That(CuratedCapabilities.Resolve(alias, [new("invaders.bin", new byte[8192], "own")]), Is.SameAs(alias));
        alias.Helpers.Add("g.own = function() { return 1; };");
        Assert.That(CuratedCapabilities.Resolve(alias, []), Is.SameAs(alias));
        Assert.That(alias.Helpers, Has.Count.EqualTo(1));
    }
}
