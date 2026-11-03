using Retro.Machines;

namespace Retro.App;

/// <summary>The order catalog. Each group lives in its own file; everything acts on the same services as the graphical interface.</summary>
public sealed partial class RetroApp
{
    private IReadOnlyList<CommandSpec>? _terminalCatalog;
    private IReadOnlyList<CommandSpec> TerminalCatalog
    {
        get => _terminalCatalog ??= [.. LibraryOrders(), .. PlayOrders(), .. BatteryOrders(), .. AnalysisOrders(), .. ProjectOrders(), .. TaskOrders(), .. SystemOrders(), .. PaletteOrders()];
        set => _terminalCatalog = value;
    }

    private IEnumerable<CompletionItem> OptionCompletions() => Options.Select(option => new CompletionItem(TerminalLanguage.Option(option.Id), option.Title));
    private IEnumerable<CompletionItem> ActionCompletions() => Commands.Select(command => new CompletionItem(command.Id, command.Title));

    private IEnumerable<CommandSpec> SystemOrders() =>
    [
        new()
        {
            Id = "system.requirements", Name = "requisitos", Group = "Sistema", Summary = "Muestra los requisitos y qué se comprobó en este arranque.",
            Aliases = ["requirements"], Keywords = "arranque dependencias minimo audio opengl runtime sdk",
            Examples = ["requisitos"], Result = "Distingue archivos presentes, ventana inicializada y componentes opcionales.",
            Run = context =>
            {
                foreach (var check in StartupRequirements.Inspect(AppContext.BaseDirectory, Raylib_cs.Raylib.IsWindowReady(), _audioDisabled))
                    context.Write(StartupRequirements.StatusName(check.Status) + " · " + check.Detail);
            }
        },
        new()
        {
            Id = "system.news", Name = "novedades", Group = "Sistema", Summary = "Muestra las funciones nuevas y los cambios de las últimas versiones.",
            Aliases = ["news"], Examples = ["novedades"], Run = _ => { OpenNews(); ReleaseConsoleKeyboard(); }
        },
        new()
        {
            Id = "system.status", Name = "estado", Group = "Sistema", Summary = "Muestra máquina, partida, biblioteca, interfaz y tareas.",
            Aliases = ["status"], Keywords = "resumen situacion ahora",
            Run = context =>
            {
                context.Write(UiText.Format($"Máquina: {_host.Machine?.DisplayName ?? UiText.Get("apagada")} · {(_host.Paused ? UiText.Get("en pausa") : UiText.Get("en ejecución"))}"));
                context.Write(UiText.Get("Juego: ") + (_host.Fusion?.Title ?? (_host.LoadedMedia != null ? Path.GetFileName(_host.LoadedMedia) : "ninguno")));
                context.Write(UiText.Get("Biblioteca: ") + _home.GameCount + UiText.Get(" juegos · ") + (_library.Scanning ? "actualizando" : "al día"));
                context.Write(UiText.Get("Identificación: ") + _gameMetadata.Status);
                context.Write(UiText.Get("Interfaz: ") + InterfaceStyles.Describe(_prefs.Interface).Name + UiText.Format($" · texto {_prefs.TextPercent} %"));
                if (_host.SoundDeviceMissing) context.Warning("no hay salida de audio: los juegos corren sin sonido, a su velocidad. hacer audio-retry la vuelve a buscar.");
                var active = _tasks.Active;
                context.Write(active.Count == 0 ? "Tareas: ninguna en curso" : UiText.Format($"Tareas: {active.Count} en curso · usá tareas"));
                if (_host.Status.Length > 0) context.Detail(_host.Status);
            }
        },
        new()
        {
            Id = "system.options", Name = "opciones", Group = "Sistema", Summary = "Lista los ajustes del programa con su valor actual.",
            Aliases = ["ajustes", "preferencias"], Keywords = "configuracion settings",
            Arguments = [new("texto", ArgumentKind.Text, "Filtra por nombre o grupo.") { Optional = true, Rest = true }],
            Examples = ["opciones", "opciones sonido"], Result = "Son los mismos ajustes del menú de preferencias.",
            Run = context => ListTerminalOptions(context, context.Optional("texto") ?? "")
        },
        new()
        {
            Id = "system.option", Name = "opcion", Group = "Sistema", Summary = "Lee, cambia o restablece un ajuste e indica su alcance.",
            Aliases = ["ajuste"], Keywords = "configurar valor volumen texto contraste",
            Arguments =
            [
                new("id", ArgumentKind.Word, "Identificador de opciones.") { Complete = _ => OptionCompletions() },
                new("valor", ArgumentKind.Text, "Nuevo valor, o restablecer para volver al inicial.") { Optional = true, Rest = true, Complete = earlier => Options.FirstOrDefault(option => option.Id == earlier.FirstOrDefault())?.Completions() ?? [] }
            ],
            Examples = ["opcion volumen", "opcion volumen 60", "opcion contraste on", "opcion volumen restablecer"],
            Errors = ["el valor no está entre los admitidos"], Result = "El cambio se aplica al instante; la ayuda de cada opción indica su alcance.",
            Run = context =>
            {
                string id = TerminalLanguage.CanonicalOption(context.Text("id"));
                var option = Options.FirstOrDefault(option => CommandLine.Fold(option.Id) == CommandLine.Fold(id))
                    ?? throw new CommandFailure(CommandProblem.InvalidArgument, UiText.Get("No existe el ajuste ") + id + ".", "opciones lista los disponibles.");
                if (context.Optional("valor") is { } value)
                {
                    if (CommandLine.Fold(value) is "restablecer" or "reset")
                    {
                        if (option.Default == null) throw new CommandFailure(CommandProblem.WrongContext, "Este ajuste no tiene un valor inicial fijo.");
                        value = option.Default;
                    }
                    if (option.Choices != null) value = TerminalLanguage.CanonicalChoice(value, option.Choices.Select(choice => choice.Value).ToArray());
                    if (option.Write(value) is { } problem) throw new CommandFailure(CommandProblem.InvalidArgument, problem, UiText.Get("Valores: ") + option.Values);
                }
                context.Write(TerminalLanguage.Option(option.Id) + " = " + option.Read() + " · " + option.Title, TerminalKind.Success);
                context.Detail(option.Description);
                context.Detail(UiText.Get("Valores: ") + option.Values + (option.Default != null ? UiText.Get(" · inicial: ") + option.Default : "") + UiText.Get(" · alcance: ") + option.ScopeName);
                string saving = _prefs.Problem ?? _sound.Problem ?? "";
                if (saving.Length > 0) context.Warning(saving);
            }
        },
        new()
        {
            Id = "system.interface", Name = "interfaz", Group = "Sistema", Summary = "Cambia el estilo o abre la elección con vistas previas.",
            Aliases = ["estilo", "tema"], Keywords = "apariencia retro terminal real cambiar grafica",
            Arguments = [new("estilo", ArgumentKind.Choice, "El estilo que querés usar.") { Choices = ["retro", "explorer", "real", "lounge", "preview"] }],
            Examples = ["interfaz retro", "interfaz preview"], Result = "Los datos, ajustes y la partida se conservan.",
            Run = context =>
            {
                string id = context.Text("estilo");
                if (id == "preview") { OpenInterfacePicker(); return; }
                InterfaceStyles.TryParse(id, out var style); SetInterface(style);
                context.Success(UiText.Get("Interfaz: ") + InterfaceStyles.Describe(style).Name);
            }
        },
        new()
        {
            Id = "system.actions", Name = "acciones", Group = "Sistema", Summary = "Lista las funciones del programa con su disponibilidad actual.",
            Keywords = "herramientas funciones catalogo buscar instrumentos",
            Arguments = [new("texto", ArgumentKind.Text, "Filtra por tema.") { Optional = true, Rest = true }],
            Examples = ["acciones", "acciones partida"], Result = "Es el mismo catálogo del buscador gráfico.",
            Run = context => ListTerminalActions(context, context.Optional("texto") ?? "")
        },
        new()
        {
            Id = "system.do", Name = "hacer", Group = "Sistema", Summary = "Ejecuta una acción del catálogo; los instrumentos visuales abren su panel.",
            Keywords = "ejecutar abrir herramienta taller",
            Arguments = [new("id", ArgumentKind.Word, "Identificador de acciones.") { Complete = _ => ActionCompletions() }],
            Examples = ["hacer capture", "hacer fusion", "hacer code"], Result = "Con un panel abierto, Ctrl+Shift+T devuelve la consola completa.",
            Errors = ["la acción necesita una partida o una máquina encendida"],
            Run = context =>
            {
                string id = context.Text("id");
                var command = Commands.FirstOrDefault(command => CommandLine.Fold(command.Id) == CommandLine.Fold(id))
                    ?? throw new CommandFailure(CommandProblem.InvalidArgument, UiText.Get("No existe la acción ") + id + ".", "acciones lista las disponibles.");
                if (!command.Enabled) throw new CommandFailure(CommandProblem.WrongContext, command.Unavailable, "juegos lista tu biblioteca.");
                command.Execute();
                if (_home.IsOpen || _hub != HubPage.None || _creator.IsOpen || _studio.IsOpen || _code.IsOpen || _stateWindowOpen || _debugMode || ConfigurationOpen || _browser.IsOpen)
                    ReleaseConsoleKeyboard();
                if (command.Id == "play") LeaveTerminal();
                context.Success(command.Title);
                context.Detail("Ctrl+Shift+T devuelve la consola completa.");
            }
        },
        new()
        {
            Id = "system.update", Name = "actualizar", Group = "Sistema", Summary = "Consulta la versión o abre el actualizador del programa.",
            Aliases = ["version", "actualizaciones"], Keywords = "nueva entrega instalar",
            Arguments = [new("accion", ArgumentKind.Choice, "abrir busca e instala con confirmación.") { Optional = true, Choices = ["estado", "abrir"] }],
            Examples = ["actualizar", "actualizar abrir"], Result = "Nunca instala sin que lo confirmes en el actualizador.",
            Run = context =>
            {
                if (context.Optional("accion") == "abrir") { _updates.Open(); return; }
                context.Write(UiText.Get("Versión: RetroEmu ") + Retro.Update.UpdateRunner.AppVersion.ToString(3));
                context.Detail(_updates.Summary);
            }
        },
        new()
        {
            Id = "system.diagnostics", Name = "diagnostico", Group = "Sistema", Summary = "Muestra un resumen técnico sin datos personales; copiarlo es una acción explícita.",
            Keywords = "error informe soporte problema reporte registro",
            Arguments = [new("accion", ArgumentKind.Choice, "copiar lo lleva al portapapeles.") { Optional = true, Choices = ["ver", "copiar"] }],
            Examples = ["diagnostico", "diagnostico copiar"], Result = "No incluye rutas personales, títulos, credenciales ni tu historial de órdenes.",
            Run = context =>
            {
                string report = DiagnosticReport();
                if (context.Optional("accion") == "copiar") { ImGuiNET.ImGui.SetClipboardText(report); context.Success("Diagnóstico copiado al portapapeles."); return; }
                context.Write(report);
                context.Detail("diagnostico copiar lo lleva al portapapeles.");
            }
        },
        new()
        {
            Id = "system.copy", Name = "copiar", Group = "Consola", Summary = "Copia el resultado de la última orden o toda la salida, sin decoraciones.",
            Aliases = ["copy"], Keywords = "portapapeles salida seleccion",
            Arguments = [new("que", ArgumentKind.Choice, "Sin indicar, copia el último resultado.") { Optional = true, Choices = ["ultimo", "todo"] }],
            Examples = ["copiar", "copiar todo"], Result = "También podés seleccionar con el mouse y usar Ctrl+C o clic derecho.",
            Run = context =>
            {
                bool all = context.Optional("que") == "todo";
                ImGuiNET.ImGui.SetClipboardText(all ? Terminal.Output.AllText() : Terminal.Output.LastOutput(previous: true));
                context.Success(all ? "Salida completa copiada." : "Último resultado copiado.");
            }
        },
        new()
        {
            Id = "system.shortcuts", Name = "atajos", Group = "Consola", Summary = "Lista las teclas de la consola y de la partida.",
            Aliases = ["teclas"], Keywords = "ctrl atajo teclado controles",
            Run = context =>
            {
                context.Table([new("Teclas"), new("Función", Flexible: true)], TerminalShortcuts.Select(item => new[] { item.Keys, item.Action }));
                context.Detail("Dentro del juego el teclado pertenece a la máquina emulada: solo Ctrl+Shift+T vuelve a la consola.");
            }
        },
        new()
        {
            Id = "system.quit", Name = "salir", Group = "Sistema", Summary = "Cierra el programa, con la misma protección de trabajos pendientes del menú.",
            Aliases = ["exit", "quit"], Keywords = "cerrar terminar",
            Run = _ => RequestQuit()
        }
    ];

    internal static readonly (string Keys, string Action)[] TerminalShortcuts =
    [
        ("Enter", "Ejecuta la orden"), ("Tab / Shift+Tab", "Completa y recorre alternativas"), ("Esc", "Cancela una sugerencia, una selección o la línea"),
        ("↑ / ↓", "Órdenes anteriores; el borrador se conserva"), ("Ctrl+R", "Busca en las órdenes anteriores"),
        ("← → · Ctrl+← →", "Mueve por carácter o por palabra"), ("Inicio / Fin · Ctrl+A / Ctrl+E", "Principio y final de la línea"),
        ("Shift + movimiento · Ctrl+Shift+A", "Selecciona texto de la línea"), ("Retroceso / Supr · Ctrl+ esas teclas", "Borra un carácter o una palabra"),
        ("Ctrl+W · Ctrl+U · Ctrl+K", "Borra palabra, hasta el inicio, hasta el final"), ("Ctrl+Z / Ctrl+Y", "Deshace y rehace la edición"),
        ("Ctrl+C", "Copia la selección; sin selección abandona la línea. Nunca cancela tareas"), ("Ctrl+X / Ctrl+V", "Corta y pega"),
        ("Clic derecho", "Copia la selección; sin selección pega"), ("Ctrl+L", "Despeja la vista y conserva la salida anterior"),
        ("RePág / AvPág · Ctrl+↑ ↓ · Ctrl+Inicio / Fin", "Desplaza la salida"), ("Ctrl+Shift+T", "Alterna entre la partida y la consola"),
        ("F2 · F3 · F5 · F8 · F9", "Durante la partida: estado rápido, reinicio, turbo y pausa, según la máquina")
    ];

    private void ListTerminalOptions(CommandContext context, string filter)
    {
        string folded = CommandLine.Fold(filter);
        var found = Options.Where(option => CommandLine.Fold(TerminalLanguage.Option(option.Id) + " " + option.Title + " " + UiText.Get(option.Group)).Contains(folded)).ToArray();
        if (found.Length == 0) throw new CommandFailure(CommandProblem.InvalidArgument, UiText.Get("Ningún ajuste coincide con ") + filter + ".", "opciones lista todos.");
        context.Table([new("Ajuste"), new("Valor"), new("Grupo", Optional: true), new("Descripción", Flexible: true)], found.Select(option => new[] { TerminalLanguage.Option(option.Id), option.Read(), option.Group, option.Title }));
        context.Detail("opcion <ajuste> muestra sus valores; opcion <ajuste> <valor> lo cambia.");
    }

    private void ListTerminalActions(CommandContext context, string filter)
    {
        var found = CommandPalette.Search(Commands, filter);
        if (found.Count == 0) throw new CommandFailure(CommandProblem.InvalidArgument, UiText.Get("Ninguna acción coincide con ") + filter + ".", "acciones lista todas.");
        context.Table([new("Acción"), new("Grupo", Optional: true), new("Función", Flexible: true)], found.Select(command => new[] { command.Id, command.Group, command.Title + (command.Enabled ? "" : " [" + command.Unavailable + "]") }));
        context.Detail("hacer <acción> la ejecuta.");
    }

    private void RegisterConsoleCapabilities(ProgramTerminal terminal)
    {
        terminal.Capabilities = _ => Commands.Select(command => (UiText.Get("hacer ") + command.Id, command.Title + " · " + command.Group + " " + command.Keywords))
            .Concat(Options.Select(option => (UiText.Get("opcion ") + option.Id, option.Title + " · " + option.Group + " " + option.Description)));
    }
}
