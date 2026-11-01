using System.Numerics;
using ImGuiNET;
using Retro.Core;
using Retro.Studio;

namespace Retro.App;

public sealed partial class HomeScreen
{
    private sealed record LibraryCard(string Key, string Title, string Detail, string? Hint, string Machine,
        IReadOnlyList<string> Systems, LibraryGameKind Kind, bool Openable, Action Play, Func<string?> Picture);
    private readonly FrameCache<(int, int, int), List<LibraryCard>> _catalog = new();
    private readonly FrameCache<(int, int, int, int, int, string, string, LibraryFilterRequest, long), List<LibraryCard>> _filtered = new();
    private readonly FrameCache<(int, int, int, int, string), string[]> _genres = new();
    private LibraryFilterRequest _filters = new();
    private bool _filtersExpanded;
    private LibraryCard? _editing;
    private string _editGenres = "", _editProblem = "";
    private int _editYear;
    private bool _editHasYear, _focusEditor;
    internal Action<string, Vector2, Vector2>? LibraryControlBounds { get; set; }
    private static readonly string[] SuggestedGenres = ["Acción", "Aventura", "Plataformas", "Disparos", "Puzles", "Rol", "Estrategia", "Deportes", "Carreras", "Lucha", "Simulación", "Educativo", "Otros"];
    private static readonly string[] Orders = ["Nombre: A–Z", "Nombre: Z–A", "Año: recientes primero", "Año: antiguos primero", "Última partida", "Agregados recientemente"];

    private LibraryGameInfo EffectiveInfo(string key) => GameIdentification.Merge(_metadata.Get(key), _identification.Get(key));

    private List<LibraryCard> Catalog
    {
        get
        {
            _identification.UpdateLocal(library.Entries, library.Folder, library.Revision);
            return _catalog.Get((library.Revision, _contentRevision, _identification.Revision), BuildCatalog);
        }
    }

    private List<LibraryCard> BuildCatalog()
    {
        var cards = new List<LibraryCard>();
        foreach (var game in library.Originals())
            cards.Add(new(OriginalKey(game), _identification.Get(game.Identity)?.Title ?? game.Name, Name(game.Machine),
                string.Join(" · ", new[] { game.Name, game.NotYet }.OfType<string>()), game.Machine.Id, [game.Machine.Id],
                LibraryGameKind.Original, game.NotYet == null && game.Confidence >= 50, () => playOriginal(game.Path), () => ThumbnailFiles.Beside(game.Path)));
        foreach (var game in _created)
            cards.Add(new(CreatedKey(game), game.Title, game.MachineName, game.Pitch, game.Machine, [game.Machine],
                LibraryGameKind.Created, Ready(game), () => playOriginal(game.ProgramPath), () => game.PicturePath));
        foreach (var game in _fusions.Concat(_inspired))
        {
            string[] systems = game.Games.Select(slot => _profiles.GetValueOrDefault(slot.Profile)?.Machine).OfType<string>().Distinct().ToArray();
            string missing = string.Join(", ", game.Games.Where(slot => !_availableProfiles.Contains(slot.Profile))
                .Select(slot => _profiles.GetValueOrDefault(slot.Profile)?.Name ?? slot.Profile));
            string machine = _profiles.GetValueOrDefault(game.Games.FirstOrDefault(slot => slot.Slot == game.BaseSlot)?.Profile ?? "")?.Machine ?? systems.FirstOrDefault() ?? "fusion";
            cards.Add(new(FusionKey(game), game.Title, $"{(game.IsInspired ? "Con originales" : "Fusión")} · v{game.Version}",
                missing.Length > 0 ? "Falta: " + missing : game.Pitch, machine, systems,
                game.IsInspired ? LibraryGameKind.Inspired : LibraryGameKind.Fusion, missing.Length == 0,
                () => playFusion(game.Id), () => StudioLibrary.PicturePath(game)));
        }
        _metadata.Observe(cards.Select(card => card.Key));
        return cards;
    }

    private bool InSection(LibraryCard card) => _section switch
    {
        "originals" => card.Kind == LibraryGameKind.Original,
        "created" => card.Kind is LibraryGameKind.Created or LibraryGameKind.Inspired,
        "fusions" => card.Kind == LibraryGameKind.Fusion,
        "machine" => card.Kind is LibraryGameKind.Original or LibraryGameKind.Created && card.Systems.Contains(_machine),
        "favorites" => _activity.IsFavorite(card.Key),
        "recent" => _activity.LastPlayed(card.Key) != null,
        _ => true
    };

    private List<LibraryCard> FilteredCards()
    {
        var catalog = Catalog;
        var request = _filters with { Search = _search };
        var now = _clock.GetUtcNow();
        return _filtered.Get((library.Revision, _contentRevision, _metadata.Revision, _identification.Revision, _activity.Revision, _section, _machine, request,
            now.UtcTicks / TimeSpan.TicksPerMinute), () =>
        {
            var selected = catalog.Select((card, index) => (card, index)).Where(item => InSection(item.card));
            if (_section is "favorites" or "recent") selected = selected.DistinctBy(item => item.card.Key);
            var candidates = selected.Select(item => new LibraryFilterEntry(item.card.Key, item.card.Title, item.card.Systems, item.card.Kind, item.card.Openable,
                    EffectiveInfo(item.card.Key), _activity.IsFavorite(item.card.Key), _activity.LastPlayed(item.card.Key), item.index));
            return LibraryFilters.Apply(candidates, request, now).Select(entry => catalog[entry.RowIndex]).ToList();
        });
    }

    private void DrawFilteredGames()
    {
        if (_section == "machine" && ImGui.CollapsingHeader("Formatos y compatibilidad")) ImGui.TextWrapped(_machine switch
        {
            "pcxt" => "FreeDOS y BIOS libres incluidos. Abrí un EXE, COM o ZIP para PC/XT, o un disquete IMG/IMA de 160 a 720 KB. Gráficos CGA.",
            "zx48" => "Cintas TAP/TZX e instantáneas Z80/SNA.",
            "c64" => "Programas PRG y cintas T64. Los discos D64 y cartuchos CRT siguen pendientes.",
            "nes" => "Cartuchos NES. La compatibilidad depende de la placa del cartucho.",
            _ => "ROMs de la placa arcade, en un archivo ZIP."
        });
        if (_section == "recent")
        {
            ImGui.BeginDisabled(_activity.Recents.Count == 0);
            if (ImGui.SmallButton("Vaciar recientes")) _activity.ClearRecents();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Quita la lista; conserva tus juegos, favoritos y fichas.");
        }
        var cards = FilteredCards();
        var section = Catalog.Where(InSection);
        if (_section is "favorites" or "recent") section = section.DistinctBy(card => card.Key);
        int total = section.Count();
        UiTheme.Hint($"{cards.Count} de {total} juegos" + (library.Scanning ? " · Actualizando…" : ""));
        if (ImGui.SmallButton("Identificación de juegos…")) actions?.MetadataSettings?.Invoke();
        if (ImGui.IsItemHovered()) UiTheme.Tooltip(_identification.Status + ". Leé el origen de los datos en Ficha.");
        if (cards.Count == 0)
        {
            UiTheme.EmptyState("No hay juegos para mostrar", _filters.ActiveCount > 0 || _search.Trim().Length > 0
                ? "Probá quitar algún filtro. Los juegos sin ficha están en Sin clasificar y Sin año."
                : _section == "favorites" ? "Marcá la estrella junto a un juego para guardarlo acá."
                : _section == "recent" ? "Acá aparecen los juegos que abriste." : "Abrí un archivo o agregalo a la biblioteca.");
            return;
        }
        if (!BeginList()) return;
        foreach (var card in cards)
            Row(card.Key, card.Title, card.Detail, card.Hint, card.Play, card.Openable, card.Machine, card.Picture);
        ImGui.EndTable();
    }

    private void TrackLibraryControl(string name) => LibraryControlBounds?.Invoke(name, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

    private bool LibraryCombo(string id, string control, ref int selected, string[] options)
    {
        int before = selected;
        bool open = ImGui.BeginCombo(id, options[Math.Clamp(selected, 0, options.Length - 1)]);
        TrackLibraryControl(control);
        if (open)
        {
            for (int index = 0; index < options.Length; index++)
            {
                if (ImGui.Selectable(options[index] + "###option-" + index, selected == index)) selected = index;
                TrackLibraryControl("Elegir " + control + ":" + options[index]);
                if (selected == index) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        return before != selected;
    }

    private void DrawLibraryFilters(bool inline)
    {
        _ = Catalog;
        int active = (_filters with { Search = _search }).ActiveCount;
        string label = active > 0 ? $"Filtros ({active})" : "Filtros";
        float width = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2;
        float available = ImGui.GetContentRegionAvail().X;
        if (inline) ImGui.SameLine();
        if (ImGui.Button(label + "###library-filters")) _filtersExpanded = !_filtersExpanded;
        TrackLibraryControl("Filtros");
        if (!inline)
        {
            ImGui.SameLine(); ImGui.SetNextItemWidth(Math.Max(UiTheme.Px(90), available - width - ImGui.GetStyle().ItemSpacing.X));
            int order = (int)_filters.Order;
            if (LibraryCombo("##orden-biblioteca", "Orden", ref order, Orders)) _filters = _filters with { Order = (LibraryOrder)order };
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Ordenar la lista. Los años desconocidos quedan al final.");
        }
        if (active > 0)
        {
            if (ImGui.SmallButton("Quitar filtros"))
            { _search = ""; _filters = new() { Order = _section == "recent" ? LibraryOrder.LastPlayed : LibraryOrder.Name }; }
            TrackLibraryControl("Quitar filtros");
        }
        if (_filtersExpanded)
        {
            ImGui.Spacing();
            bool table = ImGui.GetContentRegionAvail().X >= UiTheme.Px(620) && ImGui.BeginTable("library-filter-fields", 2, ImGuiTableFlags.SizingStretchSame);
            void Field(string title) { if (table) ImGui.TableNextColumn(); ImGui.TextUnformatted(title); ImGui.SetNextItemWidth(-1); }
            if (inline)
            {
                Field("Orden"); int order = (int)_filters.Order;
                if (LibraryCombo("##orden-biblioteca", "Orden", ref order, Orders)) _filters = _filters with { Order = (LibraryOrder)order };
            }
            if (_section != "machine")
            {
                Field("Sistema");
                string[] systems = new[] { "Todos" }.Concat(Machines.Select(Name)).ToArray();
                int selected = Array.FindIndex(Machines, machine => machine.Id == _filters.Machine) + 1;
                if (LibraryCombo("##sistema-filtro", "Sistema", ref selected, systems)) _filters = _filters with { Machine = selected == 0 ? "" : Machines[selected - 1].Id };
            }
            Field("Tipo"); int kind = _filters.Kind == null ? 0 : (int)_filters.Kind + 1;
            if (LibraryCombo("##tipo-filtro", "Tipo", ref kind, ["Todos", "Originales", "Desde cero", "Fusiones", "Con originales"]))
                _filters = _filters with { Kind = kind == 0 ? null : (LibraryGameKind)(kind - 1) };
            Field("Género");
            var choices = _genres.Get((library.Revision, _contentRevision, _metadata.Revision, _identification.Revision, _filters.Genre), () => SuggestedGenres
                .Concat(Catalog.SelectMany(card => EffectiveInfo(card.Key).Genres)).Concat(_filters.GenreMode == LibraryGenreFilter.Selected ? [_filters.Genre] : [])
                .DistinctBy(LibraryMetadata.Normalize)
                .Order(StringComparer.CurrentCultureIgnoreCase).ToArray());
            string[] genres = new[] { "Todos", "Sin clasificar" }.Concat(choices).ToArray();
            int selectedGenre = _filters.GenreMode == LibraryGenreFilter.Any ? 0 : _filters.GenreMode == LibraryGenreFilter.Missing ? 1
                : Array.FindIndex(choices, genre => LibraryMetadata.Normalize(genre) == LibraryMetadata.Normalize(_filters.Genre)) + 2;
            if (LibraryCombo("##genero-filtro", "Género", ref selectedGenre, genres)) _filters = _filters with
            {
                GenreMode = selectedGenre == 0 ? LibraryGenreFilter.Any : selectedGenre == 1 ? LibraryGenreFilter.Missing : LibraryGenreFilter.Selected,
                Genre = selectedGenre >= 2 ? choices[selectedGenre - 2] : ""
            };
            Field("Año de lanzamiento"); int years = (int)_filters.YearMode;
            if (LibraryCombo("##anio-filtro", "Año", ref years, ["Cualquiera", "Sin año", "Intervalo"])) _filters = _filters with { YearMode = (LibraryYearFilter)years };
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Usa el año de tu ficha o del catálogo. La marca (obra) indica primera publicación, con fecha de esta plataforma sin determinar.");
            if (_filters.YearMode == LibraryYearFilter.Interval)
            {
                Field("Desde el año"); int from = _filters.YearFrom;
                if (ImGui.InputInt("##anio-desde", ref from)) _filters = _filters with { YearFrom = from };
                TrackLibraryControl("Desde");
                Field("Hasta el año"); int to = _filters.YearTo;
                if (ImGui.InputInt("##anio-hasta", ref to)) _filters = _filters with { YearTo = to };
                TrackLibraryControl("Hasta");
            }
            Field("Disponibilidad"); int availability = (int)_filters.Availability;
            if (LibraryCombo("##disponibilidad-filtro", "Disponibilidad", ref availability, ["Todos", "Se pueden abrir", "Requieren revisión"]))
                _filters = _filters with { Availability = (LibraryAvailability)availability };
            Field("Actividad"); int activity = (int)_filters.Activity;
            if (LibraryCombo("##actividad-filtro", "Actividad", ref activity, ["Cualquiera", "Sin jugar", "Jugados: últimos 7 días", "Jugados: últimos 30 días", "Jugados: últimos 90 días", "Agregados: últimos 7 días", "Agregados: últimos 30 días"]))
                _filters = _filters with { Activity = (LibraryActivityFilter)activity };
            TrackLibraryControl("Actividad");
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Agregados cuenta desde que este catálogo registró el juego; no usa la fecha del archivo.");
            if (table) ImGui.EndTable();
            if (ImGui.Button("Ver resultados")) _filtersExpanded = false;
            TrackLibraryControl("Ver resultados");
            if (inline)
            {
                if (ImGui.SmallButton("Abrir archivo…")) browse();
                UiTheme.SameLineIfFits("Actualizar biblioteca"); ImGui.BeginDisabled(library.Scanning);
                if (ImGui.SmallButton("Actualizar biblioteca")) { library.Rescan(library.Folder); Refresh(); }
                ImGui.EndDisabled();
            }
            UiTheme.Hint("Completá género y año desde Ficha, junto a Jugar. Los datos faltantes quedan sin clasificar.");
        }
        if (_filters.Problem is { } problem) UiTheme.Hint(problem);
        if (_metadata.Problem is { } storageProblem) UiTheme.Hint(storageProblem);
        if (_metadata.CanRecover)
        {
            if (ImGui.Button("Recuperar fichas")) _metadata.Recover();
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Conserva el archivo anterior en un respaldo y guarda las fichas de esta sesión.");
        }
        if (_metadata.RecoveryPath is { } backup)
        { UiTheme.Hint("Se conservó un respaldo de las fichas anteriores."); if (ImGui.IsItemHovered()) UiTheme.Tooltip(backup); }
        ImGui.Spacing();
    }

    private void OpenMetadata(string key)
    {
        _editing = Catalog.FirstOrDefault(card => card.Key == key);
        if (_editing == null) return;
        var info = _metadata.Get(key); _editGenres = string.Join(", ", info.Genres);
        _editHasYear = info.ReleaseYear != null; _editYear = info.ReleaseYear ?? _clock.GetLocalNow().Year;
        _editProblem = ""; _focusEditor = true;
    }

    private void DrawMetadataEditor()
    {
        if (_editing is not { } card) return;
        Vector2 available = WorkspaceLayout.Available();
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(UiTheme.Px(360, 220), available), available);
        ImGui.SetNextWindowSize(Vector2.Min(UiTheme.Px(620, 520), available), ImGuiCond.Appearing);
        bool open = true;
        if (!ImGui.Begin("Ficha del juego###library-game-info", ref open, ImGuiWindowFlags.NoDocking))
        { ImGui.End(); if (!open) _editing = null; return; }
        WorkspaceLayout.KeepOnScreen();
        UiTheme.Heading(card.Title);
        if (_identification.Get(card.Key) is { } detected)
        {
            ImGui.TextWrapped("Detectado: " + string.Join(" · ", new[] { detected.ReleaseYear?.ToString(), string.Join(", ", detected.Genres), detected.Publisher }.Where(value => !string.IsNullOrWhiteSpace(value))));
            UiTheme.Hint(detected.Method);
            UiTheme.Hint("Fuente: " + detected.Source);
            if (detected.FirstReleaseYear is { } first && detected.ReleaseYear == null)
                UiTheme.Hint($"Primera publicación de la obra: {first}. Año de esta plataforma sin determinar.");
        }
        bool ignored = _identification.Cache.Get(card.Key)?.Ignored ?? false;
        if (ImGui.Checkbox("Omitir identificación automática de este juego", ref ignored)) _identification.Ignore(card.Key, ignored);
        ImGui.SeparatorText("Tu ficha personal");
        ImGui.TextUnformatted("Géneros separados por coma");
        if (_focusEditor) { ImGui.SetKeyboardFocusHere(); _focusEditor = false; }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##generos-ficha", "Ej.: aventura, plataformas", ref _editGenres, 2048);
        TrackLibraryControl("Géneros de la ficha");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##sugerencias-genero", "Elegir géneros…"))
        {
            foreach (string genre in SuggestedGenres)
            {
                var current = ParseGenres(_editGenres).ToList(); bool selected = current.Any(value => LibraryMetadata.Normalize(value) == LibraryMetadata.Normalize(genre));
                ImGui.BeginDisabled(!selected && current.Count >= LibraryMetadata.GenreLimit);
                if (ImGui.Checkbox(genre, ref selected))
                {
                    if (!selected) current.RemoveAll(value => LibraryMetadata.Normalize(value) == LibraryMetadata.Normalize(genre));
                    else current.Add(genre);
                    _editGenres = string.Join(", ", current);
                }
                ImGui.EndDisabled();
            }
            ImGui.EndCombo();
        }
        ImGui.Checkbox("Completar año de lanzamiento", ref _editHasYear);
        TrackLibraryControl("Completar año");
        ImGui.BeginDisabled(!_editHasYear); ImGui.SetNextItemWidth(-1);
        ImGui.InputInt("##anio-ficha", ref _editYear); TrackLibraryControl("Año de la ficha"); ImGui.EndDisabled();
        var info = _metadata.Get(card.Key);
        UiTheme.Hint("Tus campos completos tienen prioridad. Un campo vacío usa el dato detectado, si existe. La ficha no cambia el juego.");
        if (info.AddedAt is { } added) UiTheme.Hint("Registrado en este catálogo: " + added.ToLocalTime().ToString("dd/MM/yyyy"));
        if (_activity.LastPlayed(card.Key) is { } played) UiTheme.Hint("Última partida: " + played.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
        if (_editProblem.Length > 0) UiTheme.Hint(_editProblem);
        if (UiTheme.PrimaryButton("Guardar ficha"))
        {
            if (_metadata.Update(card.Key, ParseGenres(_editGenres), _editHasYear ? _editYear : null)) _editing = null;
            else _editProblem = _metadata.Problem ?? "No se pudo actualizar la ficha.";
        }
        TrackLibraryControl("Guardar ficha"); UiTheme.SameLineIfFits("Cancelar");
        if (ImGui.Button("Cancelar")) _editing = null;
        TrackLibraryControl("Cancelar ficha");
        ImGui.End();
        if (!open) _editing = null;
    }

    private static string[] ParseGenres(string text) => text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
