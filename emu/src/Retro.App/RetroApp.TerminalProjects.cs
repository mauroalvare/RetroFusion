using Retro.Ai;
using Retro.Compat;
using Retro.Fusion;
using Retro.Studio;

namespace Retro.App;

public sealed partial class RetroApp
{
    private readonly record struct ProjectRow(string Key, string Type, string Title, string System, string State);
    private string[] _terminalProjects = [];
    private AppTask? _followed;
    private int _followedRevision;

    private List<ProjectRow> ProjectRows()
    {
        var rows = new List<ProjectRow>();
        var available = _library.Entries.Where(entry => entry.ProfileId != null).Select(entry => entry.ProfileId!).ToHashSet(StringComparer.Ordinal);
        foreach (var game in StudioLibrary.AllVersions().GroupBy(game => game.Id).Select(group => group.OrderByDescending(version => version.Version).First()).OrderBy(game => game.Title))
        {
            var missing = game.Games.Where(slot => !available.Contains(slot.Profile)).Select(slot => slot.Profile).ToArray();
            rows.Add(new("fusion:" + game.Id, game.IsInspired ? "con originales" : "fusión", game.Title, "v" + game.Version, missing.Length == 0 ? "lista para abrir" : "faltan originales: " + missing.Length));
        }
        foreach (var game in CreatedGames.All())
            rows.Add(new("creado:" + game.Folder, "desde cero", game.Title, game.Machine, game.Passed ? "prueba aprobada" : "prueba pendiente o fallida"));
        foreach (var work in StudioWorkStore.Pending())
            rows.Add(new("trabajo-fusion:" + work.Id, "trabajo guardado", work.State.Draft?.Title ?? work.State.Template.Title, "", "esperando continuar"));
        foreach (var work in GameWorkStore.Pending())
            rows.Add(new("trabajo-creado:" + work.Id, "trabajo guardado", work.State.Draft?.Title ?? work.State.Wish, work.State.Machine, "esperando continuar"));
        foreach (var work in new FusionResearchStore().Pending())
            rows.Add(new("investigacion:" + work.Id, "investigación", work.Concept.Title, "", work.Summary.Length > 0 ? work.Summary : "en pausa"));
        return rows;
    }

    private ProjectRow FindProject(string value)
    {
        var rows = ProjectRows();
        if (int.TryParse(value, out int number))
        {
            if (_terminalProjects.Length == 0) throw new CommandFailure(CommandProblem.WrongContext, "Todavía no hay una lista de proyectos.", "proyectos lista tus trabajos; después usá el número.");
            if (number < 1 || number > _terminalProjects.Length) throw new CommandFailure(CommandProblem.InvalidArgument, $"La última lista tiene {_terminalProjects.Length} proyectos.");
            string key = _terminalProjects[number - 1];
            return rows.FirstOrDefault(row => row.Key == key) is { Key: not null } found ? found
                : throw new CommandFailure(CommandProblem.MissingResource, "Ese proyecto ya no existe.", "proyectos vuelve a listar.");
        }
        string folded = CommandLine.Fold(value);
        var matches = rows.Where(row => CommandLine.Fold(row.Title).Contains(folded)).ToArray();
        if (matches.Length == 1) return matches[0];
        if (matches.Length == 0) throw new CommandFailure(CommandProblem.MissingResource, "No hay ningún proyecto llamado " + value + ".", "proyectos lista tus trabajos.");
        _terminalProjects = matches.Select(row => row.Key).ToArray();
        throw new CommandFailure(CommandProblem.Ambiguous, $"{matches.Length} proyectos coinciden. Elegí uno por número:", matches.Take(8).Select((row, index) => $"{index + 1}. {row.Title} · {row.Type}").ToArray());
    }

    private static string KeyValue(ProjectRow row) => row.Key[(row.Key.IndexOf(':') + 1)..];

    private IEnumerable<CommandSpec> ProjectOrders() =>
    [
        new()
        {
            Id = "projects.list", Name = "proyectos", Group = "Proyectos", Summary = "Lista fusiones, creaciones, trabajos guardados e investigaciones.",
            Aliases = ["trabajos"], Keywords = "creaciones fusiones borradores pendientes retomar",
            Examples = ["proyectos"], Result = "Los números valen para proyecto, validar y retomar-trabajo.",
            Run = context =>
            {
                var rows = ProjectRows();
                _terminalProjects = rows.Select(row => row.Key).ToArray();
                if (rows.Count == 0) { context.Detail("Todavía no hay proyectos. crear y fusionar abren los talleres."); return; }
                context.Table([new("#", Right: true), new("Título", Flexible: true), new("Tipo"), new("Sistema", Optional: true), new("Estado", Flexible: true)],
                    rows.Select((row, index) => new[] { (index + 1).ToString(), row.Title, row.Type, row.System, row.State }));
                context.Detail("proyecto <n> · validar <n> · retomar-trabajo <n>");
            }
        },
        new()
        {
            Id = "projects.show", Name = "proyecto", Group = "Proyectos", Summary = "Muestra una creación: idea, originales, versiones y qué falta comprobar.",
            Keywords = "detalle ficha informacion", Arguments = [new("proyecto", ArgumentKind.Text, "Número de proyectos o título.") { Rest = true }], Examples = ["proyecto 1"],
            Run = context =>
            {
                var row = FindProject(context.Text("proyecto"));
                context.Heading(row.Title + " · " + row.Type);
                if (row.Key.StartsWith("fusion:", StringComparison.Ordinal))
                {
                    var versions = StudioLibrary.AllVersions().Where(game => game.Id == KeyValue(row)).OrderByDescending(game => game.Version).ToArray();
                    var game = versions[0];
                    context.Write(game.Pitch);
                    context.Write("Cómo se juega: " + (game.HowToPlay.Length > 0 ? game.HowToPlay : "sin instrucciones"));
                    context.Detail($"Versiones: {versions.Length} · originales: {string.Join(", ", game.Games.Select(slot => slot.Profile))}");
                    context.Detail("Estado: " + row.State + ". Una validación comprueba robots y escenas; no certifica diversión ni un final.");
                }
                else if (row.Key.StartsWith("creado:", StringComparison.Ordinal))
                {
                    var game = CreatedGames.All().First(item => item.Folder == KeyValue(row));
                    context.Write(game.Pitch);
                    if (game.HowToPlay.Length > 0) context.Write("Cómo se juega: " + game.HowToPlay);
                    context.Detail("Carpeta: " + game.Folder);
                    if (game.Trial.Length > 0) context.Write(game.Trial);
                }
                else
                {
                    context.Write("Estado: " + row.State);
                    context.Detail("retomar-trabajo " + context.Text("proyecto") + " abre el taller donde se continúa.");
                }
            }
        },
        new()
        {
            Id = "projects.validate", Name = "validar", Group = "Proyectos", Summary = "Prueba una creación con jugadores automáticos, en segundo plano.",
            Aliases = ["probar"], Keywords = "comprobar test robot verificar", Arguments = [new("proyecto", ArgumentKind.Text, "Número de proyectos o título.") { Rest = true }],
            Examples = ["validar 1", "tareas"], Errors = ["faltan los originales de la fusión"], Result = "El informe queda en tarea <id>; el prompt vuelve enseguida.",
            Cancellation = "cancelar <id> detiene la prueba; lo ya guardado se conserva.",
            Run = context =>
            {
                var row = FindProject(context.Text("proyecto"));
                var task = row.Key.StartsWith("fusion:", StringComparison.Ordinal) ? ValidateFusion(KeyValue(row), row.Title)
                    : row.Key.StartsWith("creado:", StringComparison.Ordinal) ? ValidateCreated(KeyValue(row), row.Title)
                    : throw new CommandFailure(CommandProblem.WrongContext, "Solo se valida una creación guardada.", "retomar-trabajo continúa un trabajo pendiente.");
                context.Success($"Tarea {task.Id}: {task.Kind} de {task.Target}. seguir {task.Id} muestra el avance.");
            }
        },
        new()
        {
            Id = "projects.resume", Name = "retomar-trabajo", Group = "Proyectos", Summary = "Abre el taller visual en un trabajo o una investigación guardados.",
            Keywords = "continuar pendiente borrador reanudar", Arguments = [new("proyecto", ArgumentKind.Text, "Número de proyectos o título.") { Rest = true }], Examples = ["retomar-trabajo 2"],
            Result = "Continuar una construcción usa el proveedor elegido en el taller; esta orden solo lo abre.",
            Run = context =>
            {
                var row = FindProject(context.Text("proyecto"));
                if (row.Key.StartsWith("trabajo-creado:", StringComparison.Ordinal)) { OpenCreator(); _creator.ShowSavedProjects(); }
                else if (row.Key.StartsWith("trabajo-fusion:", StringComparison.Ordinal)) { OpenFusionStudio(); _studio.ShowSavedWork(); }
                else if (row.Key.StartsWith("investigacion:", StringComparison.Ordinal)) { OpenFusionStudio(); _studio.ShowResearch(KeyValue(row)); }
                else throw new CommandFailure(CommandProblem.WrongContext, row.Title + " ya está terminado.", "jugar " + row.Title);
                ReleaseConsoleKeyboard(); context.Success("Taller abierto · Ctrl+Shift+T vuelve a la consola.");
            }
        },
        new()
        {
            Id = "projects.prepare", Name = "preparar", Group = "Proyectos", Summary = "Investiga un original sin IA para usarlo en fusiones, en segundo plano.",
            Aliases = ["investigar"], Keywords = "arranque controles variables perfil laboratorio",
            Arguments = [GameArgument()], Examples = ["preparar 1"], Requires = "un original abierto desde la biblioteca",
            Errors = ["el arranque o los controles no se pudieron comprobar"], Result = "Guarda un perfil para esa edición. Lo no comprobado queda indicado en el informe; el archivo original no cambia.",
            Cancellation = "cancelar <id> detiene la búsqueda.",
            Run = context =>
            {
                var game = FindGame(context.Text("juego"));
                if (game.Path == null || game.Kind != nameof(LibraryGameKind.Original)) throw new CommandFailure(CommandProblem.WrongContext, "Solo se preparan originales.", "ficha muestra el tipo del juego.");
                var task = PrepareOriginal(game);
                context.Success($"Tarea {task.Id}: preparación de {game.Title}. seguir {task.Id} muestra el avance.");
            }
        }
    ];

    private AppTask ValidateFusion(string id, string title)
    {
        var game = StudioLibrary.AllVersions().Where(game => game.Id == id).OrderByDescending(game => game.Version).First();
        string folder = _library.Folder, firmware = _romDir;
        return _tasks.Run("validación", title, (task, token) => Task.Run(() =>
        {
            task.Report("Buscando los originales de esta fusión");
            var catalog = GameCatalog.Scan(folder);
            var sources = new Dictionary<string, GameSource>(StringComparer.Ordinal);
            foreach (var slot in game.Games)
            {
                var source = catalog.FirstOrDefault(item => item.Profile.Id == slot.Profile);
                if (source == null) throw new InvalidOperationException("Falta el original " + slot.Profile + ". Agregalo a la biblioteca y volvé a validar.");
                sources[slot.Profile] = source.Source;
            }
            token.ThrowIfCancellationRequested();
            task.Report("Jugadores automáticos en una máquina privada");
            var report = StudioValidator.Run(game, sources, firmware, ct: token);
            task.Result(report.Summary);
            foreach (string problem in report.Problems) task.Error(problem);
            if (report.Error != null) task.Error(report.Error);
            task.Partial = !report.Ok;
        }, token));
    }

    private AppTask ValidateCreated(string folder, string title)
    {
        var game = CreatedGames.All().First(item => item.Folder == folder);
        string firmware = _romDir;
        return _tasks.Run("prueba", title, (task, token) => Task.Run(() =>
        {
            var report = CreatedGames.TryAgain(game, firmware, token, progress => task.Report(progress.Stage, AppTaskState.Working, progress.Frames, progress.TotalFrames, "cuadros"));
            task.Result(report.Summary);
            task.Artifact(game.ProgramPath);
            foreach (var check in report.Checks.Where(check => !check.Ok && check.Required)) task.Error(check.Name + ": " + check.Detail);
            task.Partial = !report.Ok;
        }, token));
    }

    private AppTask PrepareOriginal(TerminalGame game)
    {
        string path = game.Path!, firmware = _romDir;
        return _tasks.Run("preparación", game.Title, async (task, token) =>
        {
            var options = new PrepareOptions { FirmwareDirectory = firmware };
            var result = await GamePreparer.PrepareAsync(path, options, line => task.Report(line), token).ConfigureAwait(false);
            foreach (string line in result.CheckedData) task.Result("comprobado: " + line);
            foreach (string line in result.UnresolvedData) task.Result("sin resolver: " + line);
            if (result.Problem != null) task.Error(result.Problem);
            task.Partial = !result.Ok;
        });
    }

    private AppTask? TaskArgument(CommandContext context)
    {
        var task = _tasks.Find(context.Number("tarea")) ?? throw new CommandFailure(CommandProblem.MissingResource, "No existe la tarea " + context.Text("tarea") + ".", "tareas lista las de esta sesión.");
        return task;
    }

    private IEnumerable<CommandSpec> TaskOrders()
    {
        var argument = new CommandArgument("tarea", ArgumentKind.Number, "Número de tareas.") { Min = 1, Complete = _ => _tasks.All.Select(task => new CompletionItem(task.Id.ToString(), task.Kind + " · " + task.Target)) };
        return
        [
            new()
            {
                Id = "tasks.list", Name = "tareas", Group = "Tareas", Summary = "Lista los procesos largos de esta sesión y qué está haciendo cada uno.",
                Aliases = ["jobs", "ps"], Keywords = "procesos segundo plano progreso en curso",
                Arguments = [new("accion", ArgumentKind.Choice, "limpiar quita las terminadas.") { Optional = true, Choices = ["limpiar"] }],
                Run = context =>
                {
                    if (context.Has("accion")) { _tasks.ClearFinished(); context.Success("Tareas terminadas retiradas de la lista."); return; }
                    var all = _tasks.All;
                    if (all.Count == 0) { context.Detail("No hay tareas en esta sesión."); return; }
                    context.Table([new("#", Right: true), new("Tipo"), new("Objetivo", Flexible: true), new("Estado", Flexible: true), new("Avance", Optional: true), new("Tiempo", Optional: true)],
                        all.Select(task => new[] { task.Id.ToString(), task.Kind, task.Target, _tasks.Describe(task), AppTaskCenter.Progress(task), AppTaskCenter.Duration(task.Elapsed) }));
                    context.Detail("tarea <n> · seguir <n> · cancelar <n>");
                }
            },
            new()
            {
                Id = "tasks.show", Name = "tarea", Group = "Tareas", Summary = "Muestra el detalle de una tarea: etapa, resultados, errores y archivos.",
                Keywords = "informe resultado detalle", Arguments = [argument], Examples = ["tarea 1"],
                Run = context =>
                {
                    var task = TaskArgument(context)!;
                    context.Heading($"Tarea {task.Id}: {task.Kind} · {task.Target}");
                    context.Write("Estado: " + _tasks.Describe(task) + " · " + AppTaskCenter.Duration(task.Elapsed));
                    if (task.Stage.Length > 0) context.Write("Etapa: " + task.Stage);
                    if (AppTaskCenter.Progress(task) is { Length: > 0 } progress) context.Write("Avance: " + progress);
                    if (_tasks.Advice(task) is { Length: > 0 } advice) context.Warning(advice);
                    foreach (string line in task.Results) context.Write(line);
                    foreach (string line in task.Errors) context.Write("error: " + line, TerminalKind.Error);
                    foreach (string line in task.Artifacts) context.Detail("Archivo: " + line);
                    if (task.CanResume) context.Detail($"retomar {task.Id} la vuelve a intentar.");
                }
            },
            new()
            {
                Id = "tasks.follow", Name = "seguir", Group = "Tareas", Summary = "Muestra en la consola cada avance de una tarea hasta que termine.",
                Aliases = ["follow", "tail"], Keywords = "ver avance progreso", Arguments = [argument], Examples = ["seguir 1"],
                Cancellation = "Ctrl+C deja de seguirla; la tarea continúa.",
                Run = context =>
                {
                    var task = TaskArgument(context)!;
                    if (!task.Active) { context.Detail("Esa tarea ya terminó: " + _tasks.Describe(task) + "."); return; }
                    _followed = task; _followedRevision = -1; context.Detail("Siguiendo la tarea · Ctrl+C deja de seguirla.");
                }
            },
            new()
            {
                Id = "tasks.cancel", Name = "cancelar", Group = "Tareas", Summary = "Pide detener una tarea. Lo que ya produjo se conserva.",
                Aliases = ["kill", "detener-tarea"], Keywords = "parar abortar frenar", Arguments = [argument], Examples = ["cancelar 1"], Errors = ["la tarea ya terminó o no admite cancelación"],
                Run = context =>
                {
                    var task = TaskArgument(context)!;
                    if (!task.Cancel()) throw new CommandFailure(CommandProblem.WrongContext, task.Active ? "Esta tarea no se puede cancelar desde acá." : "Esa tarea ya terminó.", "tareas muestra su estado.");
                    context.Success($"Cancelación pedida para la tarea {task.Id}. Se detiene en el próximo punto seguro.");
                }
            },
            new()
            {
                Id = "tasks.resume", Name = "retomar", Group = "Tareas", Summary = "Vuelve a intentar una tarea cancelada o fallida que lo admita.",
                Keywords = "reintentar continuar", Arguments = [argument], Examples = ["retomar 1"],
                Run = context =>
                {
                    var task = TaskArgument(context)!;
                    if (!task.Resume()) throw new CommandFailure(CommandProblem.WrongContext, "Esta tarea no se puede retomar.", "Volvé a iniciarla con su orden.");
                    context.Success("Tarea retomada.");
                }
            }
        ];
    }

    /// <summary>Writes the progress of the followed task and the endings of all tasks. Runs on the interface thread.</summary>
    private void ReportTasksToConsole()
    {
        if (_terminal == null) return;
        if (_followed is { } task && task.Revision != _followedRevision)
        {
            _followedRevision = task.Revision;
            string progress = AppTaskCenter.Progress(task);
            if (task.Active && task.Stage.Length > 0) _terminal.Write($"[{task.Id}] {task.Stage}" + (progress.Length > 0 ? " · " + progress : ""), TerminalKind.Detail);
        }
    }

    private void AnnounceTask(AppTask task)
    {
        if (_followed == task) _followed = null;
        if (_terminal == null || !Hardcore) return;
        var kind = task.State switch { AppTaskState.Completed => TerminalKind.Success, AppTaskState.Failed => TerminalKind.Error, _ => TerminalKind.Warning };
        _terminal.Write($"[{task.Id}] {task.Kind} de {task.Target}: {AppTaskCenter.StateName(task.State)} · {AppTaskCenter.Duration(task.Elapsed)}. tarea {task.Id} muestra el detalle.", kind);
    }
}
