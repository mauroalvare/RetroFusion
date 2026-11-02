using Retro.Core;

namespace Retro.App;

internal sealed record TerminalGame(string Key, string Title, string Systems, string Kind, bool Openable,
    LibraryGameInfo Info, IdentifiedGame? Detected, bool Favorite, Action Play, string? Hint, string? Path, string Preparation);

public sealed partial class HomeScreen
{
    internal static readonly string[] TerminalFilterFields = ["buscar", "sistema", "tipo", "genero", "año", "actividad", "orden", "disponibles", "quitar"];
    internal static readonly string[] TerminalOrders = ["nombre", "nombre-desc", "año-desc", "año", "recientes", "agregados"];
    private static readonly string[] Kinds = ["todos", "original", "creado", "fusion", "inspirado"];
    private static readonly string[] Activities = ["todos", "nunca", "7", "30", "90", "agregados7", "agregados30"];

    internal IReadOnlyList<TerminalGame> TerminalGames(bool filtered = true)
    {
        if (!_refreshed) Refresh();
        return (filtered ? FilteredCards() : Catalog).DistinctBy(card => card.Key).Select(card => new TerminalGame(card.Key, card.Title,
            string.Join(",", card.Systems), card.Kind.ToString(), card.Openable, EffectiveInfo(card.Key), _identification.Get(card.Key), _activity.IsFavorite(card.Key), card.Play,
            card.Hint, card.File, card.Preparation ?? "Preparación: no aplica.")).ToArray();
    }

    internal IEnumerable<(string Id, string Name)> TerminalMachines() => Machines.Select(machine => (machine.Id, Name(machine)));
    internal void TerminalShowMachine(string id) => Select("machine", id);
    internal void TerminalRefresh() => Refresh();

    internal IReadOnlyList<(string Id, int Count)> TerminalSections()
    {
        if (!_refreshed) Refresh();
        var catalog = Catalog.DistinctBy(card => card.Key).ToArray();
        var rows = new List<(string, int)>
        {
            ("biblioteca", catalog.Length), ("originales", catalog.Count(card => card.Kind == LibraryGameKind.Original)),
            ("creados", catalog.Count(card => card.Kind is LibraryGameKind.Created or LibraryGameKind.Inspired)), ("fusiones", catalog.Count(card => card.Kind == LibraryGameKind.Fusion)),
            ("favoritos", catalog.Count(card => _activity.IsFavorite(card.Key))), ("recientes", catalog.Count(card => _activity.LastPlayed(card.Key) != null))
        };
        rows.AddRange(Machines.Select(machine => (machine.Id, catalog.Count(card => card.Systems.Contains(machine.Id)))));
        return rows;
    }

    internal void TerminalSection(string section)
    {
        if (section is not ("biblioteca" or "originales" or "creados" or "fusiones" or "favoritos" or "recientes")) throw new ArgumentException("Carpetas: biblioteca, originales, creados, fusiones, favoritos, recientes, herramientas, opciones, ayuda.");
        Select(section switch { "originales" => "originals", "creados" => "created", "fusiones" => "fusions", "favoritos" => "favorites", "recientes" => "recent", _ => "all" });
    }

    internal IEnumerable<string> TerminalFilterValues(string field) => CommandLine.Fold(field) switch
    {
        "sistema" => Machines.Select(machine => machine.Id).Prepend("todos"),
        "tipo" => Kinds,
        "genero" => SuggestedGenres.Concat(Catalog.SelectMany(card => EffectiveInfo(card.Key).Genres)).DistinctBy(LibraryMetadata.Normalize).Prepend("sin").Prepend("todos"),
        "ano" => ["todos", "sin", "1980:1989", "1990:1999"],
        "actividad" => Activities,
        "orden" => TerminalOrders,
        "disponibles" => ["todos", "si", "no"],
        _ => []
    };

    internal string TerminalFilter(string field, string value)
    {
        switch (CommandLine.Fold(field))
        {
            case "quitar": _filters = new() { Order = _section == "recent" ? LibraryOrder.LastPlayed : LibraryOrder.Name }; _search = ""; break;
            case "buscar": _search = value; break;
            case "sistema":
                if (value != "todos" && !Machines.Any(machine => machine.Id == value)) throw new ArgumentException(UiText.Get("Sistema: ") + string.Join(", ", Machines.Select(machine => machine.Id)) + UiText.Get(" o todos."));
                _filters = _filters with { Machine = value == "todos" ? "" : value }; break;
            case "tipo":
                _filters = _filters with { Kind = value switch { "todos" => null, "original" => LibraryGameKind.Original, "creado" => LibraryGameKind.Created, "fusion" => LibraryGameKind.Fusion, "inspirado" => LibraryGameKind.Inspired,
                    _ => throw new ArgumentException("Tipo: todos, original, creado, fusion o inspirado.") } }; break;
            case "actividad":
                _filters = _filters with { Activity = value switch { "todos" => LibraryActivityFilter.Any, "nunca" => LibraryActivityFilter.NeverPlayed,
                    "7" => LibraryActivityFilter.Played7, "30" => LibraryActivityFilter.Played30, "90" => LibraryActivityFilter.Played90,
                    "agregados7" => LibraryActivityFilter.Added7, "agregados30" => LibraryActivityFilter.Added30,
                    _ => throw new ArgumentException("Actividad: todos, nunca, 7, 30, 90, agregados7 o agregados30.") } }; break;
            case "genero": _filters = _filters with { GenreMode = value == "todos" ? LibraryGenreFilter.Any : value == "sin" ? LibraryGenreFilter.Missing : LibraryGenreFilter.Selected, Genre = value }; break;
            case "ano":
                if (value is "todos" or "sin") _filters = _filters with { YearMode = value == "todos" ? LibraryYearFilter.Any : LibraryYearFilter.Missing };
                else
                {
                    var years = value.Split(':');
                    if (years.Length is < 1 or > 2 || !int.TryParse(years[0], out int first) || !int.TryParse(years[^1], out int last) || first < 1900 || last > 2100 || first > last) throw new ArgumentException("Año: 1985, 1980:1989, todos o sin.");
                    _filters = _filters with { YearMode = LibraryYearFilter.Interval, YearFrom = first, YearTo = last };
                }
                break;
            case "orden":
                int index = Array.IndexOf(TerminalOrders, value);
                if (index < 0) throw new ArgumentException(UiText.Get("Orden: ") + string.Join(", ", TerminalOrders));
                _filters = _filters with { Order = (LibraryOrder)index }; break;
            case "disponibles":
                _filters = _filters with { Availability = value switch { "todos" => LibraryAvailability.Any, "si" or "sí" => LibraryAvailability.Openable, "no" => LibraryAvailability.Unavailable, _ => throw new ArgumentException("Usá todos, si o no.") } }; break;
            default: throw new ArgumentException(UiText.Get("Filtros: ") + string.Join(", ", TerminalFilterFields) + ".");
        }
        return UiText.Format($"Filtro aplicado · {TerminalGames().Count} juegos. juegos muestra la lista.");
    }

    /// <summary>The active filters in words, the same ones the graphical list shows.</summary>
    internal IReadOnlyList<string> TerminalFilterSummary()
    {
        var lines = new List<string>();
        if (_search.Trim().Length > 0) lines.Add(UiText.Get("Búsqueda: ") + _search.Trim());
        if (_filters.Machine.Length > 0) lines.Add(UiText.Get("Sistema: ") + _filters.Machine);
        if (_filters.Kind is { } kind) lines.Add(UiText.Get("Tipo: ") + kind);
        if (_filters.GenreMode == LibraryGenreFilter.Selected) lines.Add(UiText.Get("Género: ") + _filters.Genre);
        else if (_filters.GenreMode == LibraryGenreFilter.Missing) lines.Add("Género: sin clasificar");
        if (_filters.YearMode == LibraryYearFilter.Interval) lines.Add(UiText.Format($"Año: {_filters.YearFrom}–{_filters.YearTo}"));
        else if (_filters.YearMode == LibraryYearFilter.Missing) lines.Add("Año: sin año");
        if (_filters.Activity != LibraryActivityFilter.Any) lines.Add(UiText.Get("Actividad: ") + _filters.Activity);
        if (_filters.Availability != LibraryAvailability.Any) lines.Add(UiText.Get("Disponibilidad: ") + _filters.Availability);
        if (_filters.Order != LibraryOrder.Name) lines.Add(UiText.Get("Orden: ") + TerminalOrders[(int)_filters.Order]);
        return lines;
    }

    internal void TerminalFavorite(string key, bool on) => _activity.SetFavorite(key, on);

    /// <summary>A personal correction. Fields are saved apart from detected data, so a catalog refresh cannot undo them.</summary>
    internal string TerminalCorrect(string key, string field, string value)
    {
        var current = _metadata.Get(key);
        bool year = field == "año", clear = CommandLine.Fold(value) == "borrar";
        int? newYear = current.ReleaseYear;
        IReadOnlyList<string> genres = current.Genres;
        if (year)
        {
            if (clear) newYear = null;
            else if (int.TryParse(value, out int parsed)) newYear = parsed;
            else throw new CommandFailure(CommandProblem.InvalidArgument, "El año debe ser un número.", "Ejemplo: corregir 1 año 1986");
        }
        else genres = clear ? [] : ParseGenres(value);
        if (!_metadata.Update(key, genres.ToArray(), newYear)) throw new CommandFailure(CommandProblem.Failed, _metadata.Problem ?? "No se pudo actualizar la ficha.");
        return clear ? "Corrección quitada" : "Corrección guardada";
    }
}
