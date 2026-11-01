using Retro.Core;

namespace Retro.App;

internal enum LibraryGameKind { Original, Created, Fusion, Inspired }
internal enum LibraryGenreFilter { Any, Missing, Selected }
internal enum LibraryYearFilter { Any, Missing, Interval }
internal enum LibraryAvailability { Any, Openable, Unavailable }
internal enum LibraryActivityFilter { Any, NeverPlayed, Played7, Played30, Played90, Added7, Added30 }
internal enum LibraryOrder { Name, NameDescending, YearNewest, YearOldest, LastPlayed, AddedNewest }

internal sealed record LibraryFilterRequest
{
    public string Search { get; init; } = "";
    public string Machine { get; init; } = "";
    public LibraryGameKind? Kind { get; init; }
    public LibraryGenreFilter GenreMode { get; init; }
    public string Genre { get; init; } = "";
    public LibraryYearFilter YearMode { get; init; }
    public int YearFrom { get; init; } = 1900;
    public int YearTo { get; init; } = 2100;
    public LibraryAvailability Availability { get; init; }
    public LibraryActivityFilter Activity { get; init; }
    public LibraryOrder Order { get; init; }
    public int ActiveCount => (Search.Trim().Length > 0 ? 1 : 0) + (Machine.Length > 0 ? 1 : 0) + (Kind != null ? 1 : 0) +
        (GenreMode != LibraryGenreFilter.Any ? 1 : 0) + (YearMode != LibraryYearFilter.Any ? 1 : 0) +
        (Availability != LibraryAvailability.Any ? 1 : 0) + (Activity != LibraryActivityFilter.Any ? 1 : 0);
    public string? Problem => YearMode == LibraryYearFilter.Interval && (YearFrom < 1900 || YearTo > 2100 || YearFrom > YearTo)
        ? "El intervalo de años debe ir de menor a mayor, entre 1900 y 2100." : null;
}

internal sealed record LibraryFilterEntry(string Key, string Title, IReadOnlyList<string> Machines, LibraryGameKind Kind,
    bool Openable, LibraryGameInfo Info, bool Favorite = false, DateTimeOffset? LastPlayed = null, int RowIndex = 0);

/// <summary>All library sections share the same predicates and deterministic order; unknown dates stay unknown.</summary>
internal static class LibraryFilters
{
    public static IReadOnlyList<LibraryFilterEntry> Apply(IEnumerable<LibraryFilterEntry> source, LibraryFilterRequest request, DateTimeOffset now)
    {
        if (request.Problem != null) return [];
        string[] words = LibraryMetadata.Normalize(request.Search).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string genre = LibraryMetadata.Normalize(request.Genre);
        var entries = source.Where(entry =>
            (request.Machine.Length == 0 || entry.Machines.Contains(request.Machine, StringComparer.OrdinalIgnoreCase)) &&
            (request.Kind == null || entry.Kind == request.Kind) &&
            (request.GenreMode == LibraryGenreFilter.Any || request.GenreMode == LibraryGenreFilter.Missing && entry.Info.Genres.Count == 0 ||
                request.GenreMode == LibraryGenreFilter.Selected && entry.Info.Genres.Any(value => LibraryMetadata.Normalize(value) == genre)) &&
            (request.YearMode == LibraryYearFilter.Any || request.YearMode == LibraryYearFilter.Missing && entry.Info.ReleaseYear == null ||
                request.YearMode == LibraryYearFilter.Interval && entry.Info.ReleaseYear >= request.YearFrom && entry.Info.ReleaseYear <= request.YearTo) &&
            (request.Availability == LibraryAvailability.Any || entry.Openable == (request.Availability == LibraryAvailability.Openable)) &&
            MatchesActivity(entry, request.Activity, now) &&
            words.All(word => LibraryMetadata.Normalize(entry.Title + " " + string.Join(' ', entry.Info.Genres)).Contains(word)));
        var ordered = request.Order switch
        {
            LibraryOrder.NameDescending => entries.OrderByDescending(entry => entry.Title, StringComparer.OrdinalIgnoreCase),
            LibraryOrder.YearNewest => entries.OrderBy(entry => entry.Info.ReleaseYear == null).ThenByDescending(entry => entry.Info.ReleaseYear),
            LibraryOrder.YearOldest => entries.OrderBy(entry => entry.Info.ReleaseYear == null).ThenBy(entry => entry.Info.ReleaseYear),
            LibraryOrder.LastPlayed => entries.OrderBy(entry => entry.LastPlayed == null).ThenByDescending(entry => entry.LastPlayed),
            LibraryOrder.AddedNewest => entries.OrderBy(entry => entry.Info.AddedAt == null).ThenByDescending(entry => entry.Info.AddedAt),
            _ => entries.OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
        };
        return ordered.ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Key, StringComparer.Ordinal).ToList();
    }

    private static bool MatchesActivity(LibraryFilterEntry entry, LibraryActivityFilter filter, DateTimeOffset now)
    {
        bool Since(DateTimeOffset? date, int days) => date is { } value && value >= now.AddDays(-days) && value <= now;
        return filter switch
        {
            LibraryActivityFilter.NeverPlayed => entry.LastPlayed == null,
            LibraryActivityFilter.Played7 => Since(entry.LastPlayed, 7),
            LibraryActivityFilter.Played30 => Since(entry.LastPlayed, 30),
            LibraryActivityFilter.Played90 => Since(entry.LastPlayed, 90),
            LibraryActivityFilter.Added7 => Since(entry.Info.AddedAt, 7),
            LibraryActivityFilter.Added30 => Since(entry.Info.AddedAt, 30),
            _ => true
        };
    }
}
