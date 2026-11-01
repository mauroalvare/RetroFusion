using Retro.App;
using Retro.Core;

namespace App.Tests;

[TestFixture]
internal sealed class LibraryFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
    private static LibraryFilterEntry Game(string key, string title, int? year = null, string[]? genres = null,
        string[]? systems = null, LibraryGameKind kind = LibraryGameKind.Original, bool openable = true,
        DateTimeOffset? played = null, DateTimeOffset? added = null) =>
        new(key, title, systems ?? ["zx48"], kind, openable, new(genres ?? [], year, added), LastPlayed: played);
    private static readonly LibraryFilterEntry[] Games =
    [
        Game("a", "Aventura del faro", 1984, ["Aventura", "Plataformas"], played: Now.AddDays(-2), added: Now.AddDays(-60)),
        Game("b", "Ruedas", 1990, ["Carreras"], ["nes"], played: Now.AddDays(-40), added: Now.AddDays(-2)),
        Game("c", "Cuadrícula", 1988, ["Puzles"], ["c64"], openable: false),
        Game("d", "Nueva escena", 2026, ["Acción"], ["c64", "nes"], LibraryGameKind.Fusion, played: Now.AddDays(-1), added: Now.AddDays(-3)),
        Game("e", "Sin ficha", added: Now.AddDays(-5)),
        Game("f", "Proyecto propio", 2025, ["Aventura"], ["c64"], LibraryGameKind.Created, added: Now.AddDays(-30)),
        Game("g", "Inspirado", systems: ["zx48", "nes"], kind: LibraryGameKind.Inspired)
    ];

    private static string[] Apply(LibraryFilterRequest request, IEnumerable<LibraryFilterEntry>? games = null) =>
        LibraryFilters.Apply(games ?? Games, request, Now).Select(game => game.Key).ToArray();

    [Test]
    public void SeveralFiltersAreCombinedInsteadOfReplacingEachOther()
    {
        var request = new LibraryFilterRequest { GenreMode = LibraryGenreFilter.Selected, Genre = "aventura", Machine = "zx48",
            YearMode = LibraryYearFilter.Interval, YearFrom = 1980, YearTo = 1989, Availability = LibraryAvailability.Openable,
            Activity = LibraryActivityFilter.Played7, Kind = LibraryGameKind.Original, Search = "faro" };
        Assert.That(Apply(request), Is.EqualTo(new[] { "a" }));
        Assert.That(Apply(request with { Machine = "nes" }), Is.Empty);
    }

    [Test]
    public void UnknownGenreAndYearRemainDistinctFromARealYearOrClassification()
    {
        Assert.That(Apply(new() { GenreMode = LibraryGenreFilter.Missing }), Is.EquivalentTo(new[] { "e", "g" }));
        Assert.That(Apply(new() { YearMode = LibraryYearFilter.Missing }), Is.EquivalentTo(new[] { "e", "g" }));
        Assert.That(Apply(new() { YearMode = LibraryYearFilter.Interval, YearFrom = 1900, YearTo = 2100 }), Does.Not.Contain("e").And.Not.Contain("g"));
        Assert.That(Apply(new()), Has.Length.EqualTo(Games.Length));
    }

    [TestCase(LibraryOrder.YearNewest, "d", "a")]
    [TestCase(LibraryOrder.YearOldest, "a", "d")]
    public void ReleaseYearOrderingKeepsUnknownDatesAtTheEnd(LibraryOrder order, string first, string lastKnown)
    {
        string[] keys = Apply(new() { Order = order });
        Assert.That(keys[0], Is.EqualTo(first)); Assert.That(keys[^3], Is.EqualTo(lastKnown));
        Assert.That(keys[^2..], Is.EquivalentTo(new[] { "e", "g" }));
    }

    [TestCase(1899, 1990), TestCase(1990, 2101), TestCase(1990, 1980), TestCase(int.MinValue, int.MaxValue)]
    public void InvalidYearIntervalsAreExplainedWithoutChangingStoredData(int from, int to)
    {
        var request = new LibraryFilterRequest { YearMode = LibraryYearFilter.Interval, YearFrom = from, YearTo = to };
        Assert.That(request.Problem, Is.Not.Null); Assert.That(Apply(request), Is.Empty);
        Assert.That(Games[0].Info.ReleaseYear, Is.EqualTo(1984));
    }

    [Test]
    public void AYearIntervalIncludesBothEnds()
    {
        Assert.That(Apply(new() { YearMode = LibraryYearFilter.Interval, YearFrom = 1984, YearTo = 1988 }), Is.EquivalentTo(new[] { "a", "c" }));
        Assert.That(Apply(new() { YearMode = LibraryYearFilter.Interval, YearFrom = 1984, YearTo = 1984 }), Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void SearchAndGenreAcceptAccentsCaseAndAllRequestedWords()
    {
        Assert.That(Apply(new() { Search = "  aventura FARO " }), Is.EqualTo(new[] { "a" }));
        Assert.That(Apply(new() { Search = "CUADRICULA" }), Is.EqualTo(new[] { "c" }));
        Assert.That(Apply(new() { GenreMode = LibraryGenreFilter.Selected, Genre = "ACCION" }), Is.EqualTo(new[] { "d" }));
        Assert.That(Apply(new() { Search = "aventura plataformas" }), Is.EqualTo(new[] { "a" }));
        Assert.That(Apply(new() { Search = "aventura carreras" }), Is.Empty);
    }

    [Test]
    public void ASecondaryGenreAndEitherOriginalSystemOfAFusionCanMatch()
    {
        Assert.That(Apply(new() { GenreMode = LibraryGenreFilter.Selected, Genre = "plataformas" }), Is.EqualTo(new[] { "a" }));
        Assert.That(Apply(new() { Kind = LibraryGameKind.Fusion, Machine = "nes" }), Is.EqualTo(new[] { "d" }));
        Assert.That(Apply(new() { Kind = LibraryGameKind.Fusion, Machine = "c64" }), Is.EqualTo(new[] { "d" }));
        Assert.That(Apply(new() { Kind = LibraryGameKind.Fusion, Machine = "zx48" }), Is.Empty);
    }

    [TestCase(LibraryGameKind.Original, 4), TestCase(LibraryGameKind.Created, 1), TestCase(LibraryGameKind.Fusion, 1), TestCase(LibraryGameKind.Inspired, 1)]
    public void EachCreationKindRemainsSeparate(LibraryGameKind kind, int count) => Assert.That(Apply(new() { Kind = kind }), Has.Length.EqualTo(count));

    [Test]
    public void AvailabilityDoesNotPretendToBeAFullGameplayCertification()
    {
        Assert.That(Apply(new() { Availability = LibraryAvailability.Unavailable }), Is.EqualTo(new[] { "c" }));
        Assert.That(Apply(new() { Availability = LibraryAvailability.Openable }), Has.Length.EqualTo(Games.Length - 1));
    }

    [TestCase(LibraryActivityFilter.Played7, new[] { "a", "d" })]
    [TestCase(LibraryActivityFilter.Played30, new[] { "a", "d" })]
    [TestCase(LibraryActivityFilter.Played90, new[] { "a", "b", "d" })]
    [TestCase(LibraryActivityFilter.Added7, new[] { "b", "d", "e" })]
    [TestCase(LibraryActivityFilter.Added30, new[] { "b", "d", "e", "f" })]
    public void ActivityUsesRecordedEventsRatherThanReleaseYears(LibraryActivityFilter activity, string[] expected) =>
        Assert.That(Apply(new() { Activity = activity }), Is.EquivalentTo(expected));

    [Test]
    public void AnUnplayedGameIsNotTheSameAsAnOldPlayOrAFutureTimestamp()
    {
        Assert.That(Apply(new() { Activity = LibraryActivityFilter.NeverPlayed }), Is.EquivalentTo(new[] { "c", "e", "f", "g" }));
        var boundary = new[] { Game("start", "First", played: Now.AddDays(-7)), Game("now", "Second", played: Now),
            Game("old", "Third", played: Now.AddDays(-7).AddTicks(-1)), Game("future", "Fourth", played: Now.AddTicks(1)) };
        Assert.That(Apply(new() { Activity = LibraryActivityFilter.Played7 }, boundary), Is.EquivalentTo(new[] { "start", "now" }));
    }

    [Test]
    public void OrderingByActivityIsDeterministicAndNeverPlacesUnknownValuesFirst()
    {
        Assert.That(Apply(new() { Order = LibraryOrder.LastPlayed })[..3], Is.EqualTo(new[] { "d", "a", "b" }));
        Assert.That(Apply(new() { Order = LibraryOrder.AddedNewest })[..4], Is.EqualTo(new[] { "b", "d", "e", "f" }));
        var ties = new[] { Game("b", "Same", 1980), Game("a", "Same", 1980) };
        Assert.That(Apply(new() { Order = LibraryOrder.YearOldest }, ties), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void ClearingARequestRestoresAllGamesAndActiveCountExcludesOrder()
    {
        var request = new LibraryFilterRequest { Search = "x", Machine = "nes", Kind = LibraryGameKind.Original,
            GenreMode = LibraryGenreFilter.Missing, YearMode = LibraryYearFilter.Missing,
            Availability = LibraryAvailability.Openable, Activity = LibraryActivityFilter.Played7, Order = LibraryOrder.YearNewest };
        Assert.That(request.ActiveCount, Is.EqualTo(7));
        Assert.That(new LibraryFilterRequest { Order = LibraryOrder.LastPlayed }.ActiveCount, Is.Zero);
        Assert.That(Apply(new()), Has.Length.EqualTo(Games.Length));
    }
}
