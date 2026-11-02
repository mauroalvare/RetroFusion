using System.Net;
using System.Text.Json;
using Retro.App;
using Retro.Core;
using Retro.Machines;

namespace App.Tests;

public sealed class GameIdentificationTests
{
    private string _folder = "";
    [SetUp] public void SetUp() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-identification-" + Guid.NewGuid().ToString("N"))).FullName;
    [TearDown] public void TearDown() => Directory.Delete(_folder, true);
    private static string Key(string name = "fixture") => LibraryKey.Named("original", name);

    [TestCase("Faro_Propio (1986)(Autor)(es)", "Faro Propio", 1986)]
    [TestCase("Faro (USA) [!]", "Faro", null)]
    [TestCase("Faro (La aventura)", "Faro (La aventura)", null)]
    [TestCase("Faro 2 (Europe) (Rev 1)", "Faro 2", null)]
    [TestCase("Faro, The (1992)", "The Faro", 1992)]
    [TestCase("Faro (1984) (1985)", "Faro", null)]
    public void FilenameHintsPreserveTitlesAndDoNotInventGenres(string name, string title, int? year)
    {
        var detected = GameIdentification.FromFilename(name);
        Assert.That(detected.Title, Is.EqualTo(title)); Assert.That(detected.ReleaseYear, Is.EqualTo(year));
        Assert.That(detected.Genres, Is.Empty); Assert.That(detected.Method, Does.Contain("sin verificar"));
    }

    [Test]
    public void PersonalCorrectionsWinWithoutConvertingDetectedDataIntoPersonalData()
    {
        var date = DateTimeOffset.UtcNow;
        var detected = new IdentifiedGame("Faro", ["Plataformas"], 1986, "Autor", "catálogo", "nombre");
        var personal = new LibraryGameInfo(["Puzles"], 1991, date);
        var effective = GameIdentification.Merge(personal, detected);
        Assert.That(effective.Genres, Is.EqualTo(new[] { "Puzles" })); Assert.That(effective.ReleaseYear, Is.EqualTo(1991)); Assert.That(effective.AddedAt, Is.EqualTo(date));
        Assert.That(GameIdentification.Merge(new([], null, date), detected).ReleaseYear, Is.EqualTo(1986));
        Assert.That(personal.ReleaseYear, Is.EqualTo(1991)); Assert.That(detected.ReleaseYear, Is.EqualTo(1986));
    }

    [Test]
    public void CatalogGenreLabelsUseTheSameFilterCategoriesWithoutGuessingFromTheGameTitle()
    {
        Assert.That(GameIdentification.NormalizeGenres(["videojuego de plataformas", "Platform game", "Puzzle-platformer", "Racing game"]),
            Is.EqualTo(new[] { "Plataformas", "Puzles", "Carreras" }));
        Assert.That(GameIdentification.FromFilename("Racing in space").Genres, Is.Empty);
    }

    [Test]
    public void AWorkYearIsAvailableForFilteringButIsAlwaysLabeledAndPersonalDatesOverrideIt()
    {
        var detected = new IdentifiedGame("Faro", ["Plataformas"], null, null, "catalog", "name and platform", 1986);
        var effective = GameIdentification.Merge(new([], null, null), detected);
        Assert.That(effective.ReleaseYear, Is.EqualTo(1986)); Assert.That(effective.WorkYear, Is.True); Assert.That(effective.YearLabel, Is.EqualTo("1986 (obra)"));
        var corrected = GameIdentification.Merge(new([], 1991, null), detected);
        Assert.That(corrected.WorkYear, Is.False); Assert.That(corrected.YearLabel, Is.EqualTo("1991"));
    }

    [Test]
    public void ExistingCollectionListSuppliesFactsButCannotReferOutsideTheLibrary()
    {
        string path = Path.Combine(_folder, "gamelist.xml");
        File.WriteAllText(path, "<gameList><game><path>./demo.nes</path><name>Faro</name><genre>Aventura / Plataformas</genre><releasedate>19860310T000000</releasedate><publisher>Taller</publisher></game><game><path>../escape.nes</path><name>Escape</name></game></gameList>");
        var entries = GameIdentification.ReadGameList(path, _folder);
        Assert.That(entries, Has.Count.EqualTo(1)); var game = entries[Path.Combine(_folder, "demo.nes")];
        Assert.That(game.ReleaseYear, Is.EqualTo(1986)); Assert.That(game.Genres, Is.EqualTo(new[] { "Aventura", "Plataformas" })); Assert.That(game.Publisher, Is.EqualTo("Taller"));
        Assert.That(game.Source, Is.EqualTo("gamelist.xml local"));
        File.WriteAllText(path, "<!DOCTYPE gameList [<!ENTITY secret SYSTEM 'file:///c:/secret'>]><gameList><game><name>&secret;</name></game></gameList>");
        Assert.That(GameIdentification.ReadGameList(path, _folder), Is.Empty);
    }

    [Test]
    public void CacheKeepsContentIdentityAcrossRenamesProtectsSnapshotsAndPreservesCorruption()
    {
        string path = Path.Combine(_folder, "identificaciones.json"), key = Key(); var now = DateTimeOffset.UtcNow;
        var cache = new GameIdentificationCache(path); var facts = new IdentifiedGame("Faro", ["Puzles"], 1986, null, "https://www.wikidata.org/wiki/Q1", "Nombre y plataforma");
        cache.Put(key, facts, "nes:Faro", now); facts.Genres[0] = "changed";
        var loaded = new GameIdentificationCache(path); Assert.That(loaded.Detected(key)!.Genres, Is.EqualTo(new[] { "Puzles" }));
        var snapshot = loaded.Detected(key)!; snapshot.Genres[0] = "changed again";
        Assert.That(loaded.Detected(key)!.Genres[0], Is.EqualTo("Puzles"));
        loaded.Ignore(key, true); Assert.That(new GameIdentificationCache(path).Detected(key), Is.Null);
        loaded.Ignore(key, false); Assert.That(loaded.Detected(key), Is.Not.Null);
        File.WriteAllText(path, "keep broken file"); var broken = new GameIdentificationCache(path);
        broken.Put(key, facts, "query", now); Assert.That(broken.Problem, Is.Not.Null); Assert.That(File.ReadAllText(path), Is.EqualTo("keep broken file"));
    }

    [Test]
    public void AFailedMatchExpiresAndRenamingAnUnknownGameAllowsANewLookup()
    {
        var cache = new GameIdentificationCache(persist: false); var now = DateTimeOffset.UtcNow;
        cache.Put(Key(), null, "nes:Unknown", now);
        Assert.That(cache.IsFresh(Key(), "nes:Unknown", now.AddDays(6)), Is.True);
        Assert.That(cache.IsFresh(Key(), "nes:Unknown", now.AddDays(8)), Is.False);
        Assert.That(cache.IsFresh(Key(), "nes:Faro", now), Is.False);
    }

    [Test]
    public async Task WikidataRequiresAnExactNameAndPlatformAndKeepsAnEditionYearSeparateFromTheWork()
    {
        using var handler = new Handler(Search(), Details([Entity("Q1", ["Q172742", "Q99775"])]), Related());
        using var http = new HttpClient(handler);
        var result = await new WikidataGameLookup(http).FindAsync("Faro (USA) [!]", "nes");
        Assert.That(result, Is.Not.Null); Assert.That(result!.Genres, Is.EqualTo(new[] { "Plataformas" })); Assert.That(result.Publisher, Is.EqualTo("Taller"));
        Assert.That(result.ReleaseYear, Is.Null, "The first release of a multi-platform work is not necessarily the NES edition.");
        Assert.That(result.FirstReleaseYear, Is.EqualTo(1986)); Assert.That(result.Source, Is.EqualTo("https://www.wikidata.org/wiki/Q1"));
        Assert.That(handler.Requests[0], Does.Contain("search=Faro").And.Not.Contain("USA"));
        Assert.That(handler.Requests, Has.Count.EqualTo(3)); Assert.That(handler.Methods, Is.All.EqualTo(HttpMethod.Get));
    }

    [TestCase(false, "Faro 2", "Q172742"), TestCase(false, "Faro", "Q99775"), TestCase(true, "Faro", "Q172742")]
    public async Task SimilarTitlesWrongPlatformsAndAmbiguousEditionsRemainUnknown(bool duplicate, string title, string platform)
    {
        var entities = new List<object> { Entity("Q1", [platform], title) }; if (duplicate) entities.Add(Entity("Q2", [platform]));
        using var handler = new Handler(Search(), Details(entities)); using var http = new HttpClient(handler);
        Assert.That(await new WikidataGameLookup(http).FindAsync("Faro", "nes"), Is.Null); Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CandidatesExplainThemselvesAndAmbiguityIsOfferedForReviewInsteadOfGuessed()
    {
        var entities = new List<object> { Entity("Q1", ["Q172742"]), Entity("Q2", ["Q172742"]), Entity("Q3", ["Q99775"]), Entity("Q4", ["Q172742"], "Faro 2") };
        using var handler = new Handler(Search(), Details(entities), Related(), Related(), Related(), Related());
        using var http = new HttpClient(handler);
        var found = await new WikidataGameLookup(http).CandidatesAsync("Faro", "nes");
        Assert.That(found.Select(candidate => candidate.Game.Source.Split('/').Last()), Is.EqualTo(new[] { "Q1", "Q2", "Q3", "Q4" }), "The most plausible come first.");
        Assert.That(found[0].Reason, Does.Contain("Título exacto y misma plataforma"));
        Assert.That(found[2].Reason, Does.Contain("otra plataforma"));
        Assert.That(found[3].Reason, Does.Contain("parecido"));
        Assert.That(found[0].Game.Method, Does.Contain("Elegido por vos"));
    }

    [Test]
    public void PersonalDecisionsAboutAMatchSurviveTheCatalog()
    {
        string path = Path.Combine(_folder, "decisiones.json"), key = LibraryKey.Original("nes", [new MediaFile("a.nes", [1], "a.nes")]);
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        IdentifiedGame Game(string source) => new("Faro", [], 1986, null, source, "m");
        var cache = new GameIdentificationCache(path);
        cache.Put(key, Game("https://x/Q1"), "nes:Faro", now);
        cache.Reject(key, "https://x/Q1", now);
        Assert.That(cache.Detected(key), Is.Null, "Rejecting the current match removes it.");
        cache.Put(key, Game("https://x/Q1"), "nes:Faro", now.AddDays(100));
        Assert.That(cache.Detected(key), Is.Null, "The same answer later does not come back.");
        cache.Confirm(key, Game("https://x/Q2"), now);
        Assert.That(cache.IsFresh(key, "nes:otra", now.AddYears(5)), Is.True, "A confirmed match never needs another lookup.");
        cache.Put(key, Game("https://x/Q9"), "nes:Faro", now.AddDays(200));
        var reopened = new GameIdentificationCache(path);
        Assert.That(reopened.Detected(key)!.Source, Is.EqualTo("https://x/Q2"), "A later catalog answer does not undo the decision, also after restarting.");
    }

    [Test]
    public void OlderCacheFilesStayReadable()
    {
        string path = Path.Combine(_folder, "anterior.json"), key = LibraryKey.Original("nes", [new MediaFile("b.nes", [2], "b.nes")]);
        File.WriteAllText(path, "{\"version\":1,\"games\":{\"" + key + "\":{\"game\":null,\"query\":\"nes:Faro\",\"checkedAt\":\"2026-10-01T00:00:00+00:00\",\"ignored\":true}}}");
        var cache = new GameIdentificationCache(path);
        Assert.That(cache.Problem, Is.Null); Assert.That(cache.Get(key)!.Ignored, Is.True); Assert.That(cache.Get(key)!.Confirmed, Is.False);
    }

    [TestCase(false), TestCase(true)]
    public async Task SinglePlatformOrQualifiedReleaseDatesCanSupplyThePlatformYear(bool qualifier)
    {
        using var handler = new Handler(Search(), Details([Entity("Q1", qualifier ? ["Q172742", "Q99775"] : ["Q172742"], qualified: qualifier)]), Related()); using var http = new HttpClient(handler);
        Assert.That((await new WikidataGameLookup(http).FindAsync("Faro", "nes"))!.ReleaseYear, Is.EqualTo(1986));
    }

    [Test]
    public async Task UnsupportedMachinesDoNotIssueAnyRequest()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        Assert.That(await new WikidataGameLookup(http).FindAsync("Faro", "unknown"), Is.Null); Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public void LocalMetadataIsAvailableImmediatelyAndOptingOutDoesNotStartTheNetwork()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        using var service = new GameMetadataService(new(persist: false), http);
        var entry = new LibraryEntry(Path.Combine(_folder, "Faro.nes"), "Faro (1986)", MachineCatalog.Get("nes"), 100, "fixture", Identity: Key());
        service.Poll([entry], _folder, 1, online: false);
        Assert.That(service.Get(Key())!.ReleaseYear, Is.EqualTo(1986)); Assert.That(handler.Requests, Is.Empty);
        service.Ignore(Key(), true); Assert.That(service.Get(Key()), Is.Null); service.Ignore(Key(), false); Assert.That(service.Get(Key()), Is.Not.Null);
    }

    [Test]
    public void BackgroundLookupPublishesOnceAndTheCacheAvoidsRepeatedRequests()
    {
        using var handler = new Handler(Search(), Details([Entity("Q1", ["Q172742"])]), Related()); using var http = new HttpClient(handler);
        using var service = new GameMetadataService(new(persist: false), http);
        var entry = new LibraryEntry(Path.Combine(_folder, "private-path.nes"), "Faro (USA)", MachineCatalog.Get("nes"), 100, "fixture", Identity: Key());
        service.Poll([entry], _folder, 1, online: true); service.Poll([entry], _folder, 1, online: true);
        Assert.That(service.Get(Key())!.ReleaseYear, Is.EqualTo(1986)); Assert.That(service.Get(Key())!.Source, Does.StartWith("https://www.wikidata.org/"));
        for (int i = 0; i < 50; i++) service.Poll([entry], _folder, 1, online: true);
        Assert.That(handler.Requests, Has.Count.EqualTo(3)); Assert.That(handler.Requests, Has.None.Contains("private-path").And.None.Contains(_folder));
        service.Poll([entry], _folder, 2, online: true); Assert.That(handler.Requests, Has.Count.EqualTo(3));
    }

    [Test]
    public void ALocalCollectionOverridesPublicCachedFactsAndSurvivesAutomaticOrganization()
    {
        string root = Path.Combine(_folder, "ROMs"); Directory.CreateDirectory(root);
        string gamePath = Path.Combine(root, "Faro.nes"); byte[] rom = new byte[16 + 16384]; "NES\x1A"u8.CopyTo(rom); rom[4] = 1;
        File.WriteAllBytes(gamePath, rom); File.SetLastWriteTimeUtc(gamePath, DateTime.UtcNow.AddMinutes(-10));
        File.WriteAllText(Path.Combine(root, "gamelist.xml"), "<gameList><game><path>./Faro.nes</path><name>Faro de la colección</name><genre>Plataformas</genre><releasedate>19910101</releasedate></game></gameList>");
        using var library = new GameLibrary { AutoOrganize = true }; library.Rescan(root);
        Assert.That(SpinWait.SpinUntil(() => { library.Poll(); return !library.Scanning; }, TimeSpan.FromSeconds(10)), Is.True);
        var game = library.Entries.Single(); Assert.That(game.Path, Is.Not.EqualTo(gamePath)); Assert.That(File.ReadAllBytes(game.Path), Is.EqualTo(rom));
        var cache = new GameIdentificationCache(Path.Combine(_folder, "cache.json"));
        cache.Put(game.Identity, new("Faro público", ["Acción"], 1986, null, "https://www.wikidata.org/wiki/Q1", "catalog"), "nes:Faro", DateTimeOffset.UtcNow);
        using var service = new GameMetadataService(cache); service.Poll(library.Entries, root, library.Revision, online: false);
        Assert.That(service.Get(game.Identity)!.Title, Is.EqualTo("Faro de la colección")); Assert.That(service.Get(game.Identity)!.ReleaseYear, Is.EqualTo(1991));
        using var reopened = new GameMetadataService(new(Path.Combine(_folder, "cache.json")));
        reopened.Poll([game with { CollectionInfo = null }], root, 1, online: false);
        Assert.That(reopened.Get(game.Identity)!.Title, Is.EqualTo("Faro de la colección"));
        Assert.That(GameIdentification.ReadGameList(Path.Combine(root, "gamelist.xml"), root + Path.DirectorySeparatorChar), Has.Count.EqualTo(1));
    }

    [Test]
    public void AConnectionFailureStopsTheQueueAndDoesNotTurnIntoAnEndlessRetryLoop()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        using var service = new GameMetadataService(new(persist: false), http);
        var entry = new LibraryEntry(Path.Combine(_folder, "Faro.nes"), "Faro", MachineCatalog.Get("nes"), 100, "fixture", Identity: Key());
        service.Poll([entry], _folder, 1, online: true); service.Poll([entry], _folder, 1, online: true);
        for (int i = 0; i < 100; i++) service.Poll([entry], _folder, 1, online: true);
        Assert.That(handler.Requests, Has.Count.EqualTo(1)); Assert.That(service.Status, Does.Contain("Reintentar"));
        Assert.That(service.Get(Key())!.Title, Is.EqualTo("Faro")); Assert.That(service.Cache.Get(Key()), Is.Null, "Network errors are not cached as absent games.");
    }

    [Test]
    public void DisablingOnlineLookupsCancelsAnOutstandingRequestAndKeepsLocalFacts()
    {
        using var handler = new PendingHandler(); using var http = new HttpClient(handler);
        using var service = new GameMetadataService(new(persist: false), http);
        var entry = new LibraryEntry(Path.Combine(_folder, "Faro.nes"), "Faro (1986)", MachineCatalog.Get("nes"), 100, "fixture", Identity: Key());
        service.Poll([entry], _folder, 1, online: true); Assert.That(handler.Token.CanBeCanceled, Is.True);
        service.Poll([entry], _folder, 1, online: false);
        Assert.That(handler.Token.IsCancellationRequested, Is.True); Assert.That(service.Pending, Is.Zero); Assert.That(service.Get(Key())!.ReleaseYear, Is.EqualTo(1986));
    }

    private sealed class PendingHandler : HttpMessageHandler
    {
        public CancellationToken Token { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Token = cancellationToken; await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return new(HttpStatusCode.OK); }
    }

    internal static string Search() => "{\"search\":[{\"id\":\"Q1\"},{\"id\":\"Q2\"}]}";
    internal static object Entity(string id, string[] platforms, string title = "Faro", bool qualified = false) => new
    {
        id, labels = new { en = new { value = title }, es = new { value = title } }, aliases = new { },
        claims = new Dictionary<string, object>
        {
            ["P400"] = platforms.Select(Item).ToArray(), ["P136"] = new[] { Item("Q10") }, ["P123"] = new[] { Item("Q11") },
            ["P577"] = new[] { new { mainsnak = new { datavalue = new { value = new { time = "+1986-01-01T00:00:00Z", precision = 9 } } },
                qualifiers = qualified ? new Dictionary<string, object> { ["P400"] = new[] { new { datavalue = new { value = new { id = "Q172742" } } } } } : new() } }
        }
    };
    private static object Item(string id) => new { mainsnak = new { datavalue = new { value = new { id } } } };
    internal static string Details(IEnumerable<object> entities) => JsonSerializer.Serialize(new { entities = entities.ToDictionary(entity => (string)entity.GetType().GetProperty("id")!.GetValue(entity)!) });
    internal static string Related() => "{\"entities\":{\"Q10\":{\"id\":\"Q10\",\"labels\":{\"es\":{\"value\":\"Plataformas\"}}},\"Q11\":{\"id\":\"Q11\",\"labels\":{\"es\":{\"value\":\"Taller\"}}}}}";
    internal sealed class Handler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public readonly List<string> Requests = [];
        public readonly List<HttpMethod> Methods = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri); Methods.Add(request.Method);
            if (_responses.Count == 0) throw new InvalidOperationException("Unexpected external request in an offline test.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responses.Dequeue()) });
        }
    }
}
