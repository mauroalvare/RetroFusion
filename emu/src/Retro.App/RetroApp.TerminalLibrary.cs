using Retro.Core;

namespace Retro.App;

public sealed partial class RetroApp
{
    private string[] _terminalListedKeys = [];
    private static readonly string[] Sections = ["biblioteca", "originales", "creados", "fusiones", "favoritos", "recientes", "herramientas", "opciones", "ayuda"];

    private CommandArgument GameArgument(bool rest = true) => new("juego", ArgumentKind.Game, "Número de la última lista, título o clave.") { Rest = rest, Complete = _ => GameCompletions() };

    private IEnumerable<CompletionItem> GameCompletions()
    {
        var games = _home.TerminalGames();
        for (int i = 0; i < _terminalListedKeys.Length; i++)
            if (games.FirstOrDefault(game => game.Key == _terminalListedKeys[i]) is { } listed) yield return new((i + 1).ToString(), listed.Title);
        foreach (var game in games) yield return new(game.Title, game.Systems);
    }

    private IEnumerable<CommandSpec> LibraryOrders() =>
    [
        new()
        {
            Id = "library.section", Name = "cd", Group = "Biblioteca", Summary = "Cambia de sección virtual; no recorre carpetas del sistema.",
            Aliases = ["seccion", "ir"], Keywords = "navegar carpeta ubicacion favoritos recientes originales",
            Arguments = [new("seccion", ArgumentKind.Word, "Sección o sistema; .. vuelve a biblioteca.") { Complete = _ => Sections.Select(section => new CompletionItem(section)).Concat(_home.TerminalMachines().Select(machine => new CompletionItem(machine.Id, machine.Name))) }],
            Examples = ["cd favoritos", "cd nes", "cd .."], Result = "El prompt muestra la sección y ls la lista.",
            Run = context =>
            {
                string location = TerminalLanguage.CanonicalChoice(context.Text("seccion").Trim('/').ToLowerInvariant(), Sections);
                if (location is ".." or "" or "inicio") location = "biblioteca";
                if (_home.TerminalMachines().Any(machine => machine.Id == location)) _home.TerminalShowMachine(location);
                else if (!Sections.Contains(location)) throw new CommandFailure(CommandProblem.InvalidArgument, UiText.Get("No existe la sección ") + location + ".", UiText.Get("Secciones: ") + string.Join(", ", Sections) + ".");
                else if (location is not ("herramientas" or "opciones" or "ayuda")) _home.TerminalSection(location);
                Terminal.Location = location; _terminalListedKeys = [];
                ListLocation(context);
            }
        },
        new() { Id = "library.where", Name = "pwd", Group = "Biblioteca", Summary = "Muestra la sección actual y los filtros activos.", Run = context => { context.Write(Terminal.Prompt); foreach (string line in _home.TerminalFilterSummary()) context.Detail(line); } },
        new() { Id = "library.list-here", Name = "ls", Group = "Biblioteca", Summary = "Lista la sección actual.", Aliases = ["dir"], Keywords = "listar ver contenido", Run = ListLocation },
        new()
        {
            Id = "library.sections", Name = "secciones", Group = "Biblioteca", Summary = "Muestra cada sección de la biblioteca con su cantidad.", Keywords = "categorias sistemas maquinas resumen",
            Run = context => context.Table([new("Sección"), new("Juegos", Right: true)], _home.TerminalSections().Select(item => new[] { item.Id, item.Count.ToString() }))
        },
        new()
        {
            Id = "library.games", Name = "juegos", Group = "Biblioteca", Summary = "Lista y numera los juegos de la sección con los filtros activos.",
            Aliases = ["lista"], Keywords = "listar biblioteca titulos roms",
            Arguments = [new("texto", ArgumentKind.Text, "Búsqueda por título o género.") { Optional = true, Rest = true }],
            Examples = ["juegos", "juegos faro"], Result = "Los números valen para jugar, ficha y favorito hasta la próxima lista.",
            Run = context => { if (context.Optional("texto") is { } text) _home.TerminalFilter("buscar", text); ListGames(context); }
        },
        new()
        {
            Id = "library.search", Name = "buscar", Group = "Biblioteca", Summary = "Busca títulos y géneros sin perder los demás filtros.",
            Aliases = ["find"], Keywords = "encontrar titulo",
            Arguments = [new("texto", ArgumentKind.Text, "Sin acentos también encuentra.") { Rest = true }],
            Examples = ["buscar aventura"], Run = context => { _home.TerminalFilter("buscar", context.Text("texto")); ListGames(context); }
        },
        new()
        {
            Id = "library.filter", Name = "filtrar", Group = "Biblioteca", Summary = "Combina filtros compartidos con la biblioteca gráfica.",
            Aliases = ["filtro"], Keywords = "genero año sistema tipo disponibilidad actividad limpiar quitar",
            Arguments =
            [
                new("campo", ArgumentKind.Choice, "Qué filtrar; quitar los borra.") { Choices = HomeScreen.TerminalFilterFields },
                new("valor", ArgumentKind.Text, "Año admite 1980:1989; sin selecciona desconocidos.") { Optional = true, Rest = true, Complete = earlier => _home.TerminalFilterValues(earlier.FirstOrDefault() ?? "").Select(value => new CompletionItem(value)) }
            ],
            Examples = ["filtrar genero Plataformas", "filtrar año 1980:1989", "filtrar quitar"],
            Run = context =>
            {
                string field = CommandLine.Fold(context.Text("campo"));
                if (field != "quitar" && !context.Has("valor")) throw new CommandFailure(CommandProblem.InvalidArgument, UiText.Get("Falta el valor de ") + field + ".", UiText.Get("Valores: ") + string.Join(", ", _home.TerminalFilterValues(field).Take(12)));
                string value = TerminalLanguage.CanonicalChoice(context.Optional("valor") ?? "", _home.TerminalFilterValues(field).ToArray());
                context.Success(_home.TerminalFilter(field, value));
                _terminalListedKeys = [];
            }
        },
        new()
        {
            Id = "library.filters", Name = "filtros", Group = "Biblioteca", Summary = "Muestra los filtros activos y el orden.", Keywords = "activos ver",
            Run = context => { var lines = _home.TerminalFilterSummary(); if (lines.Count == 0) context.Detail("Sin filtros activos."); foreach (string line in lines) context.Write(line); }
        },
        new()
        {
            Id = "library.sort", Name = "ordenar", Group = "Biblioteca", Summary = "Cambia el orden de la lista.", Keywords = "orden alfabetico fecha",
            Arguments = [new("orden", ArgumentKind.Choice, "Criterio.") { Choices = HomeScreen.TerminalOrders }],
            Examples = ["ordenar año", "ordenar recientes"], Run = context => { _home.TerminalFilter("orden", context.Text("orden")); ListGames(context); }
        },
        new()
        {
            Id = "library.info", Name = "ficha", Group = "Biblioteca", Summary = "Muestra sistema, datos, procedencia, disponibilidad y preparación.",
            Aliases = ["info"], Keywords = "datos detalle año genero editora procedencia",
            Arguments = [GameArgument()], Examples = ["ficha 1", "ficha Faro del taller"], Run = context => ShowCard(context, FindGame(context.Text("juego")))
        },
        new()
        {
            Id = "library.correct", Name = "corregir", Group = "Biblioteca", Summary = "Guarda una corrección personal, que tiene prioridad sobre lo detectado.",
            Aliases = ["editar-ficha"], Keywords = "editar ficha genero año cambiar dato",
            Arguments =
            [
                GameArgument(rest: false),
                new("campo", ArgumentKind.Choice, "Qué corregir.") { Choices = ["genero", "año"] },
                new("valor", ArgumentKind.Text, "Géneros separados por coma, un año, o borrar para volver a lo detectado.") { Rest = true }
            ],
            Examples = ["corregir 1 genero Plataformas, Acción", "corregir 1 año 1986", "corregir 1 año borrar"],
            Errors = ["el año está fuera de rango o las fichas no se pudieron guardar"], Result = "La corrección se guarda por identidad del juego; una actualización del catálogo no la deshace.",
            Run = context =>
            {
                var game = FindGame(context.Text("juego"));
                context.Success(_home.TerminalCorrect(game.Key, CommandLine.Fold(context.Text("campo")).StartsWith('a') ? "año" : "genero", context.Text("valor")) + " · " + game.Title);
            }
        },
        new()
        {
            Id = "library.favorite", Name = "favorito", Group = "Biblioteca", Summary = "Marca o quita el favorito que se ve en la biblioteca.",
            Aliases = ["fav"], Keywords = "estrella marcar",
            Arguments = [GameArgument(rest: false), new("marca", ArgumentKind.Toggle, "Sin indicar, invierte la marca.") { Optional = true }],
            Examples = ["favorito 1", "favorito 1 off"],
            Run = context =>
            {
                var game = FindGame(context.Text("juego"));
                bool on = context.Has("marca") ? context.Toggle("marca") : !game.Favorite;
                _home.TerminalFavorite(game.Key, on);
                context.Success((on ? "Favorito: " : "Ya no es favorito: ") + game.Title);
            }
        },
        new() { Id = "library.favorites", Name = "favoritos", Group = "Biblioteca", Summary = "Lista tus favoritos.", Keywords = "estrella marcados", Run = context => GoTo(context, "favoritos") },
        new() { Id = "library.recent", Name = "recientes", Group = "Biblioteca", Summary = "Lista los juegos abiertos últimamente.", Keywords = "historial ultimos continuar", Run = context => GoTo(context, "recientes") },
        new()
        {
            Id = "library.identify", Name = "identificar", Group = "Biblioteca", Summary = "Consulta o controla la identificación de juegos.",
            Aliases = ["catalogo"], Keywords = "wikidata metadatos datos en linea fichas reintentar",
            Arguments = [new("accion", ArgumentKind.Choice, "Sin indicar, muestra el estado.") { Optional = true, Choices = ["estado", "on", "off", "reintentar"] }],
            Examples = ["identificar", "identificar on", "identificar reintentar"], Result = "La consulta en línea es opcional y corre en segundo plano; la biblioteca sigue disponible.",
            Cancellation = "identificar off detiene la consulta pendiente.",
            Run = context =>
            {
                switch (context.Optional("accion"))
                {
                    case "on": _prefs.OnlineGameInfo = true; _prefs.Save(); break;
                    case "off": _prefs.OnlineGameInfo = false; _prefs.Save(); break;
                    case "reintentar": _gameMetadata.Retry(); break;
                }
                context.Write(_gameMetadata.Status);
                context.Detail(UiText.Get("Consulta en línea: ") + (_prefs.OnlineGameInfo ? "activada" : "desactivada") + UiText.Get(". Envía el título limpio; nunca archivos ni rutas."));
                if (_gameMetadata.Problem is { } problem) context.Warning(problem);
            }
        },
        new()
        {
            Id = "library.reject", Name = "omitir", Group = "Biblioteca", Summary = "Rechaza o recupera la identificación automática de un juego.",
            Aliases = ["rechazar"], Keywords = "coincidencia incorrecta quitar dato",
            Arguments = [GameArgument(rest: false), new("marca", ArgumentKind.Toggle, "on omite; off vuelve a aceptarla.") { Optional = true }],
            Examples = ["omitir 1", "omitir 1 off"], Result = "No borra tu ficha personal.",
            Run = context =>
            {
                var game = FindGame(context.Text("juego"));
                bool on = !context.Has("marca") || context.Toggle("marca");
                _gameMetadata.Ignore(game.Key, on);
                context.Success((on ? "Identificación automática omitida: " : "Identificación automática aceptada: ") + game.Title);
            }
        },
        new()
        {
            Id = "library.candidates", Name = "candidatos", Group = "Biblioteca", Summary = "Pide al catálogo posibles identidades de un juego, para revisarlas.",
            Keywords = "coincidencia dudosa revisar identificar wikidata", Arguments = [GameArgument()], Examples = ["candidatos 1", "candidato 1 aceptar"],
            Requires = "la consulta en línea activada", Cancellation = "Termina sola en doce segundos.", Result = "Muestra por qué se propone cada una. Ninguna se aplica sola.",
            Unavailable = () => _prefs.OnlineGameInfo ? null : new(CommandProblem.WrongContext, "La consulta en línea está desactivada.", "identificar on la activa; solo envía el título limpio."),
            Run = context =>
            {
                var game = FindGame(context.Text("juego"));
                _gameMetadata.SearchCandidates(game.Key, game.Title, game.Systems.Split(',')[0]);
                _candidatesFor = game.Title; context.Success("Consultando el catálogo en segundo plano.");
            }
        },
        new()
        {
            Id = "library.candidate", Name = "candidato", Group = "Biblioteca", Summary = "Acepta o rechaza un candidato de la última búsqueda.",
            Keywords = "elegir coincidencia aceptar rechazar",
            Arguments = [new("numero", ArgumentKind.Number, "Número de candidatos.") { Min = 1, Max = 6 }, new("accion", ArgumentKind.Choice, "Qué hacer.") { Choices = ["aceptar", "rechazar"] }],
            Examples = ["candidato 1 aceptar", "candidato 2 rechazar"], Result = "La decisión se guarda por identidad del juego y una actualización del catálogo no la deshace.",
            Run = context =>
            {
                int number = context.Number("numero");
                var found = _gameMetadata.Candidates;
                if (_gameMetadata.CandidateKey is not { } key || number > found.Count) throw new CommandFailure(CommandProblem.WrongContext, "No hay un candidato con ese número.", "candidatos <juego> vuelve a buscar.");
                var candidate = found[number - 1];
                if (context.Text("accion") == "aceptar") _gameMetadata.Accept(key, candidate); else _gameMetadata.Reject(key, candidate);
                context.Success((context.Text("accion") == "aceptar" ? "Aceptado: " : "Rechazado: ") + candidate.Game.Title);
            }
        },
        new()
        {
            Id = "library.folder", Name = "carpeta", Group = "Biblioteca", Summary = "Muestra o cambia la carpeta de juegos, y la recuerda.",
            Aliases = ["roms"], Keywords = "importar agregar carpeta directorio ubicacion",
            Arguments = [new("ruta", ArgumentKind.Path, "Carpeta con tus juegos; entre comillas si tiene espacios.") { Optional = true, Rest = true }],
            Examples = ["carpeta", "carpeta \"C:\\Mis juegos\""], Errors = ["la carpeta no existe"], Result = "Busca en segundo plano; tus archivos no se mueven al cambiar de carpeta.",
            Run = context =>
            {
                if (context.Optional("ruta") is { } path)
                {
                    if (!Directory.Exists(path)) throw new CommandFailure(CommandProblem.MissingResource, UiText.Get("No existe la carpeta ") + path + ".");
                    UseLibraryFolder(path);
                }
                context.Write(UiText.Get("Carpeta: ") + _library.Folder);
                context.Detail(_library.Scanning ? "Buscando juegos en segundo plano." : _library.Entries.Count + UiText.Get(" archivos reconocidos."));
            }
        },
        new()
        {
            Id = "library.rescan", Name = "escanear", Group = "Biblioteca", Summary = "Vuelve a revisar la carpeta de juegos en segundo plano.",
            Aliases = ["actualizar-biblioteca", "refrescar"], Keywords = "buscar nuevos archivos detectar",
            Run = context => { _library.Rescan(_library.Folder); _home.TerminalRefresh(); context.Success("Revisando la carpeta en segundo plano; tareas muestra el avance."); }
        }
    ];

    private string? _candidatesFor;

    /// <summary>The console's answer when a candidate search ends.</summary>
    private void ReportCandidates()
    {
        if (_terminal == null || _candidatesFor == null) return;
        var buffer = _terminal; string title = _candidatesFor; _candidatesFor = null;
        buffer.Write(UiText.Get("Candidatos para ") + title + ": " + _gameMetadata.CandidateStatus, TerminalKind.Heading);
        var found = _gameMetadata.Candidates;
        foreach (var (text, kind) in TextTable.Layout([new("#", Right: true), new("Título", Flexible: true), new("Año"), new("Por qué", Flexible: true, Optional: true)],
            found.Select((candidate, index) => (IReadOnlyList<string>)[(index + 1).ToString(), candidate.Game.Title, (candidate.Game.ReleaseYear ?? candidate.Game.FirstReleaseYear)?.ToString() ?? "?", candidate.Reason]).ToList(), buffer.Output.Columns))
            if (found.Count > 0) buffer.Write(text, kind);
        if (found.Count > 0) buffer.Write("candidato <n> aceptar · candidato <n> rechazar", TerminalKind.Detail);
    }

    private void GoTo(CommandContext context, string section)
    {
        _home.TerminalSection(section); Terminal.Location = section; _terminalListedKeys = [];
        ListGames(context);
    }

    private void ListLocation(CommandContext context)
    {
        switch (Terminal.Location)
        {
            case "opciones": ListTerminalOptions(context, ""); break;
            case "herramientas": ListTerminalActions(context, ""); break;
            case "ayuda": context.Detail("ayuda lista las órdenes; como <tema> busca por lo que querés hacer."); break;
            default: ListGames(context); break;
        }
    }

    private void ListGames(CommandContext context)
    {
        var games = _home.TerminalGames();
        _terminalListedKeys = games.Select(game => game.Key).ToArray();
        if (games.Count == 0)
        {
            context.Detail(_home.TerminalFilterSummary().Count > 0 ? "Ningún juego coincide. filtrar quitar borra los filtros." : "No hay juegos en esta sección. carpeta muestra dónde se buscan.");
            return;
        }
        context.Table([new("#", Right: true), new("*"), new("Título", Flexible: true), new("Sistema", Flexible: true), new("Año", Optional: true), new("Género", Flexible: true, Optional: true)],
            games.Select((game, index) => new[]
            {
                (index + 1).ToString(), game.Favorite ? "*" : "", game.Title + (game.Openable ? "" : UiText.Get(" [no se puede abrir]")), game.Systems, game.Info.YearLabel, string.Join(", ", game.Info.Genres.Select(UiText.Get))
            }));
        context.Detail(UiText.Format($"{games.Count} juegos · jugar <n> · ficha <n> · favorito <n> · filtrar quitar"));
    }

    private void ShowCard(CommandContext context, TerminalGame game)
    {
        context.Heading(game.Title);
        context.Write(UiText.Get("Sistema: ") + game.Systems + UiText.Get(" · tipo: ") + game.Kind);
        context.Write(UiText.Get("Año: ") + game.Info.YearLabel + (game.Info.WorkYear ? " · año de la obra; el de esta plataforma no está determinado" : ""));
        context.Write(UiText.Get("Género: ") + (game.Info.Genres.Count == 0 ? "sin clasificar" : string.Join(", ", game.Info.Genres)));
        context.Write(game.Openable ? "Archivo: disponible; la jugabilidad depende de su compatibilidad." : UiText.Get("Archivo: no se puede abrir") + (game.Hint is { Length: > 0 } hint ? " · " + hint : "") + ".");
        context.Write(UiText.Get("Compatibilidad: ") + UiText.Get(game.Compatibility));
        context.Write(UiText.Get("Jugabilidad: ") + UiText.Get(game.Playability));
        if (game.Path != null) context.Detail(UiText.Get("Ubicación: ") + game.Path);
        context.Write(game.Preparation);
        if (game.Detected is { } facts)
        {
            context.Detail(UiText.Get("Identificación: ") + facts.Method);
            context.Detail(UiText.Get("Fuente: ") + facts.Source + (facts.Publisher != null ? UiText.Get(" · editora: ") + facts.Publisher : ""));
        }
        else context.Detail("Identificación: sin coincidencia automática; los datos desconocidos quedan así.");
        context.Detail(UiText.Get("Clave: ") + game.Key);
        context.Detail("corregir <juego> genero|año <valor> guarda una corrección que tiene prioridad. omitir <juego> rechaza la coincidencia.");
    }

    private TerminalGame FindGame(string value)
    {
        var games = _home.TerminalGames(filtered: false);
        if (int.TryParse(value, out int number))
        {
            if (_terminalListedKeys.Length == 0) throw new CommandFailure(CommandProblem.WrongContext, "Todavía no hay una lista numerada.", "juegos lista la biblioteca; después usá el número, por ejemplo jugar 1.");
            if (number < 1 || number > _terminalListedKeys.Length) throw new CommandFailure(CommandProblem.InvalidArgument, UiText.Format($"La última lista tiene {_terminalListedKeys.Length} juegos."), "juegos vuelve a listar.");
            return games.FirstOrDefault(game => game.Key == _terminalListedKeys[number - 1])
                ?? throw new CommandFailure(CommandProblem.MissingResource, "Ese juego ya no está en la biblioteca.", "juegos vuelve a listar.");
        }
        var keys = games.Where(game => value.Length >= 8 && game.Key.StartsWith(value, StringComparison.Ordinal)).ToArray();
        if (keys.Length == 1) return keys[0];
        string folded = CommandLine.Fold(value);
        var exact = games.Where(game => CommandLine.Fold(game.Title) == folded).ToArray();
        if (exact.Length == 1) return exact[0];
        var partial = exact.Length > 1 ? exact : games.Where(game => CommandLine.Fold(game.Title).Contains(folded)).ToArray();
        if (partial.Length == 1) return partial[0];
        if (partial.Length == 0) throw new CommandFailure(CommandProblem.MissingResource, UiText.Get("No hay ningún juego llamado ") + value + ".", "juegos lista la biblioteca; buscar <texto> la filtra.");
        // The ambiguous matches become the current list, so the answer is one number away.
        _terminalListedKeys = partial.Select(game => game.Key).ToArray();
        throw new CommandFailure(CommandProblem.Ambiguous, UiText.Format($"{partial.Length} juegos coinciden con {value}. Elegí uno por número:"),
            partial.Take(8).Select((game, index) => UiText.Format($"{index + 1}. {UiText.Raw(game.Title)} · {game.Systems}")).ToArray());
    }
}
