using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Retro.Core;

/// <summary>Read-only CC0 metadata. Sends a cleaned title, never media, hashes or local paths.</summary>
public sealed partial class WikidataGameLookup(HttpClient client)
{
    private const string Endpoint = "https://www.wikidata.org/w/api.php?format=json&formatversion=2&";
    [GeneratedRegex("^Q[1-9][0-9]{0,11}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityId();
    private static readonly IReadOnlyDictionary<string, string[]> Platforms = new Dictionary<string, string[]>
    {
        ["zx48"] = ["Q23882"], ["c64"] = ["Q99775"], ["nes"] = ["Q172742"],
        ["pcxt"] = ["Q47604", "Q170434", "Q10683", "Q533571"],
        ["invaders"] = ["Q192851"], ["pacman"] = ["Q192851"], ["galaxian"] = ["Q192851"],
        ["cpc"] = ["Q478829"], ["msx"] = ["Q853547"]
    };

    public static bool Supports(string machine) => Platforms.ContainsKey(machine);

    public async Task<IdentifiedGame?> FindAsync(string name, string machine, CancellationToken cancellation = default)
    {
        if (!Platforms.TryGetValue(machine, out var platforms)) return null;
        string title = GameIdentification.TitleFromName(name);
        if (title.Length < 2) return null;
        using var search = await Fetch("action=wbsearchentities&language=en&uselang=es&type=item&limit=8&search=" + Uri.EscapeDataString(title), cancellation);
        if (!search.RootElement.TryGetProperty("search", out var hits) || hits.ValueKind != JsonValueKind.Array) return null;
        string[] ids = hits.EnumerateArray().Select(hit => hit.TryGetProperty("id", out var id) ? id.GetString() : null)
            .OfType<string>().Where(id => EntityId().IsMatch(id)).Distinct().Take(8).ToArray();
        if (ids.Length == 0) return null;
        using var details = await Entities(ids, "labels|aliases|claims", cancellation);
        var matches = Items(details).Where(item => Names(item).Any(label => GameIdentification.MatchTitle(label) == GameIdentification.MatchTitle(title)) &&
            Values(item, "P400").Any(value => platforms.Contains(ItemId(value)))).ToArray();
        // Two editions sharing a title are an ambiguity, not a reason to pick the first result.
        if (matches.Length != 1) return null;
        var game = matches[0];
        var genres = Values(game, "P136").Select(ItemId).OfType<string>().Where(id => EntityId().IsMatch(id)).Distinct().Take(8).ToArray();
        var publishers = Values(game, "P123").Select(ItemId).OfType<string>().Where(id => EntityId().IsMatch(id)).Distinct().Take(3).ToArray();
        string[] related = genres.Concat(publishers).Distinct().ToArray();
        var labels = new Dictionary<string, string>();
        if (related.Length > 0)
        {
            using var descriptions = await Entities(related, "labels", cancellation);
            foreach (var item in Items(descriptions))
                if (item.TryGetProperty("id", out var id) && Label(item) is { } text) labels[id.GetString()!] = text;
        }
        var claims = Claims(game, "P577").ToArray();
        int? first = claims.Select(Year).OfType<int>().Cast<int?>().Min();
        var platformDates = claims.Where(claim => claim.TryGetProperty("qualifiers", out var qualifiers) && qualifiers.TryGetProperty("P400", out var qualifier) &&
            qualifier.EnumerateArray().Any(snak => SnakValue(snak) is { } value && platforms.Contains(ItemId(value)))).ToArray();
        int? release = platformDates.Length > 0 ? platformDates.Select(Year).OfType<int>().Cast<int?>().Min()
            : Values(game, "P400").Select(ItemId).Distinct().Count() == 1 ? first : null;
        string idText = game.GetProperty("id").GetString()!;
        if (!EntityId().IsMatch(idText)) return null;
        return new(Label(game) ?? title, GameIdentification.NormalizeGenres(genres.Select(id => labels.GetValueOrDefault(id)).OfType<string>()),
            release, publishers.Select(id => labels.GetValueOrDefault(id)).OfType<string>().FirstOrDefault(),
            "https://www.wikidata.org/wiki/" + idText, "Catálogo: título exacto o alias + plataforma; copia sin verificar", first);
    }

    private Task<JsonDocument> Entities(string[] ids, string properties, CancellationToken cancellation) =>
        Fetch("action=wbgetentities&ids=" + string.Join('|', ids) + "&props=" + properties + "&languages=es|en", cancellation);

    private async Task<JsonDocument> Fetch(string query, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + query);
        request.Headers.UserAgent.ParseAdd("RetroEmu/0.3 (game-metadata; https://github.com/mauroalvare/RetroFusion)");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("Respuesta de catálogo demasiado grande.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            if (memory.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("Respuesta de catálogo demasiado grande.");
            memory.Write(buffer, 0, count);
        }
        return JsonDocument.Parse(memory.ToArray());
    }

    private static IEnumerable<JsonElement> Items(JsonDocument document) => document.RootElement.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Object
        ? entities.EnumerateObject().Select(property => property.Value) : [];
    private static string? Label(JsonElement item)
    {
        if (!item.TryGetProperty("labels", out var labels)) return null;
        foreach (string language in new[] { "es", "en" })
            if (labels.TryGetProperty(language, out var value) && value.TryGetProperty("value", out var text) && text.ValueKind == JsonValueKind.String) return text.GetString();
        return null;
    }
    private static IEnumerable<string> Names(JsonElement item)
    {
        if (item.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Object)
            foreach (var label in labels.EnumerateObject())
                if (label.Value.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String) yield return value.GetString()!;
        if (item.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Object)
            foreach (var language in aliases.EnumerateObject())
                if (language.Value.ValueKind == JsonValueKind.Array)
                    foreach (var alias in language.Value.EnumerateArray())
                        if (alias.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String) yield return value.GetString()!;
    }
    private static IEnumerable<JsonElement> Claims(JsonElement item, string property) => item.TryGetProperty("claims", out var claims) && claims.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
        ? values.EnumerateArray().Where(value => !value.TryGetProperty("rank", out var rank) || rank.GetString() != "deprecated") : [];
    private static IEnumerable<JsonElement> Values(JsonElement item, string property) => Claims(item, property).Select(claim => claim.TryGetProperty("mainsnak", out var snak) ? SnakValue(snak) : null).OfType<JsonElement>();
    private static JsonElement? SnakValue(JsonElement snak) => snak.TryGetProperty("datavalue", out var data) && data.TryGetProperty("value", out var value) ? value : null;
    private static string? ItemId(JsonElement value) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
    private static int? Year(JsonElement claim)
    {
        if (!claim.TryGetProperty("mainsnak", out var snak) || SnakValue(snak) is not { } value || value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("time", out var time) || !value.TryGetProperty("precision", out var precision) || !precision.TryGetInt32(out int p) || p < 9) return null;
        string? text = time.GetString();
        return text is { Length: >= 5 } && text[0] == '+' && int.TryParse(text.AsSpan(1, 4), out int year) && year is >= 1900 and <= 2100 ? year : null;
    }
}
