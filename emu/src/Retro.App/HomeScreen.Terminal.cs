using Retro.Core;

namespace Retro.App;

internal sealed record TerminalGame(string Key, string Title, string Systems, string Kind, bool Openable,
    LibraryGameInfo Info, IdentifiedGame? Detected, bool Favorite, Action Play);

public sealed partial class HomeScreen
{
    internal IReadOnlyList<TerminalGame> TerminalGames(bool filtered = true)
    {
        if (!_refreshed) Refresh();
        return (filtered ? FilteredCards() : Catalog).DistinctBy(card => card.Key).Select(card => new TerminalGame(card.Key, card.Title,
            string.Join(",", card.Systems), card.Kind.ToString(), card.Openable, EffectiveInfo(card.Key), _identification.Get(card.Key), _activity.IsFavorite(card.Key), card.Play)).ToArray();
    }

    internal void TerminalSection(string section)
    {
        if (section is not ("biblioteca" or "originales" or "creados" or "fusiones" or "favoritos" or "recientes")) throw new ArgumentException("Carpetas: biblioteca, originales, creados, fusiones, favoritos, recientes, herramientas, opciones, ayuda.");
        Select(section switch { "originales" => "originals", "creados" => "created", "fusiones" => "fusions", "favoritos" => "favorites", "recientes" => "recent", _ => "home" });
    }

    internal string TerminalFilter(string field, string value)
    {
        switch (field)
        {
            case "quitar": _filters = new(); _search = ""; break;
            case "buscar": _search = value; break;
            case "sistema":
                if (value != "todos" && !Machines.Any(machine => machine.Id == value)) throw new ArgumentException("Sistema: " + string.Join(", ", Machines.Select(machine => machine.Id)) + " o todos.");
                _filters = _filters with { Machine = value == "todos" ? "" : value }; break;
            case "tipo":
                _filters = _filters with { Kind = value switch { "todos" => null, "original" => LibraryGameKind.Original, "creado" => LibraryGameKind.Created, "fusion" => LibraryGameKind.Fusion, "inspirado" => LibraryGameKind.Inspired,
                    _ => throw new ArgumentException("Tipo: todos, original, creado, fusion o inspirado.") } }; break;
            case "actividad":
                _filters = _filters with { Activity = value switch { "todos" => LibraryActivityFilter.Any, "nunca" => LibraryActivityFilter.NeverPlayed,
                    "7" => LibraryActivityFilter.Played7, "30" => LibraryActivityFilter.Played30, "90" => LibraryActivityFilter.Played90,
                    "agregados7" => LibraryActivityFilter.Added7, "agregados30" => LibraryActivityFilter.Added30,
                    _ => throw new ArgumentException("Actividad: todos, nunca, 7, 30, 90, agregados7 o agregados30.") } }; break;
            case "genero": case "género": _filters = _filters with { GenreMode = value == "todos" ? LibraryGenreFilter.Any : value == "sin" ? LibraryGenreFilter.Missing : LibraryGenreFilter.Selected, Genre = value }; break;
            case "año": case "anio":
                if (value is "todos" or "sin") _filters = _filters with { YearMode = value == "todos" ? LibraryYearFilter.Any : LibraryYearFilter.Missing };
                else
                {
                    var years = value.Split(':');
                    if (years.Length is < 1 or > 2 || !int.TryParse(years[0], out int first) || !int.TryParse(years[^1], out int last) || first < 1900 || last > 2100 || first > last) throw new ArgumentException("Año: 1985, 1980:1989, todos o sin.");
                    _filters = _filters with { YearMode = LibraryYearFilter.Interval, YearFrom = first, YearTo = last };
                }
                break;
            case "orden":
                string[] orders = ["nombre", "nombre-desc", "año-desc", "año", "recientes", "agregados"];
                int index = Array.IndexOf(orders, value);
                if (index < 0) throw new ArgumentException("Orden: " + string.Join(", ", orders));
                _filters = _filters with { Order = (LibraryOrder)index }; break;
            case "disponibles":
                _filters = _filters with { Availability = value switch { "todos" => LibraryAvailability.Any, "si" or "sí" => LibraryAvailability.Openable, "no" => LibraryAvailability.Unavailable, _ => throw new ArgumentException("Usá todos, si o no.") } }; break;
            default: throw new ArgumentException("Filtros: buscar, sistema, tipo, genero, año, actividad, orden, disponibles, quitar.");
        }
        return $"Filtro aplicado · {TerminalGames().Count} juegos. Usá juegos para actualizar la lista.";
    }

    internal void TerminalFavorite(string key, bool on) => _activity.SetFavorite(key, on);
}
