using System.Globalization;
using Retro.Core;
using Retro.Fusion;
using Retro.Machines;

namespace Retro.App;

public sealed partial class RetroApp
{
    private MemorySearch? _terminalSearch;
    private bool _awaitingStop;

    private CommandFailure? NeedMachine() => _host.Machine != null ? null
        : new(CommandProblem.WrongContext, "No hay una máquina encendida.", "juegos lista tu biblioteca; jugar <número> abre uno.", "maquinas muestra los sistemas; maquina <id> enciende uno.");
    private CommandFailure? NeedStates() => _host.CanUseStates && _host.LoadedMedia != null ? null
        : new(CommandProblem.WrongContext, "Las ranuras pertenecen a un original cargado: cada juego y edición tiene las suyas.", "juegos lista tu biblioteca; jugar <número> abre uno.");
    private CommandFailure? NeedTape() => _host.Zx != null ? null : new(CommandProblem.WrongContext, "La cinta es del ZX Spectrum.", "cd zx48 lista los juegos de esa máquina.");
    private CommandFailure? NeedFusion() => _host.Fusion != null ? null : new(CommandProblem.WrongContext, "No hay una fusión o creación en curso.", "proyectos lista las tuyas.");

    private uint TerminalAddress(uint address)
    {
        int bits = _host.Machine!.Memory.AddressBits;
        if (bits < 32 && address >= 1u << bits) throw new CommandFailure(CommandProblem.InvalidArgument, $"La dirección está fuera de la memoria de esta máquina ({bits} bits).");
        return address;
    }

    /// <summary>Addresses only mean something for one edition, so every inspection says which one it read.</summary>
    private string InspectionContext()
    {
        string media = _host.LoadedMedia != null ? Path.GetFileName(_host.LoadedMedia) : "sin juego";
        string edition = _host.LoadedMediaHashes.FirstOrDefault() is { Length: >= 8 } hash ? " · edición " + hash[..8].ToLowerInvariant() : "";
        return $"{_host.Machine!.DisplayName} · {media}{edition}";
    }

    private IEnumerable<CommandSpec> PlayOrders() =>
    [
        new()
        {
            Id = "play.open-game", Name = "jugar", Group = "Partida", Summary = "Abre un original, creación o fusión y le entrega el teclado.",
            Aliases = ["play", "correr-juego"], Keywords = "iniciar abrir juego empezar",
            Arguments = [GameArgument()], Examples = ["jugar 1", "jugar Faro del taller"], Requires = "un juego de la biblioteca",
            Errors = ["el archivo ya no está o su formato no se puede abrir"], Result = "La partida toma la pantalla y el teclado. Ctrl+Shift+T vuelve a la consola.",
            Run = context =>
            {
                var game = FindGame(context.Text("juego"));
                if (!game.Openable) throw new CommandFailure(CommandProblem.MissingResource, "No se puede abrir " + game.Title + ".", game.Hint is { Length: > 0 } hint ? hint : "ficha muestra qué falta.");
                game.Play();
                if (_errorPopup == null && _pendingOpen == null) { LeaveTerminal(); context.Success("Abriendo: " + game.Title); }
                else ReportOpenProblem(context);
            }
        },
        new()
        {
            Id = "play.open-file", Name = "abrir", Group = "Partida", Summary = "Abre un archivo del equipo con el emulador.",
            Aliases = ["open"], Keywords = "archivo rom cinta disco ruta",
            Arguments = [new("ruta", ArgumentKind.Path, "Entre comillas si tiene espacios.") { Rest = true }],
            Examples = ["abrir \"C:\\Mis juegos\\Faro.nes\""], Errors = ["el archivo no existe o su formato no se reconoce"],
            Run = context =>
            {
                string path = context.Text("ruta");
                if (!File.Exists(path)) throw new CommandFailure(CommandProblem.MissingResource, "No existe ese archivo: " + path);
                OpenFile(path);
                if (_errorPopup == null && _pendingOpen == null) { LeaveTerminal(); context.Success("Abriendo medio: " + Path.GetFileName(path)); }
                else ReportOpenProblem(context);
            }
        },
        new()
        {
            Id = "play.resume", Name = "continuar", Group = "Partida", Summary = "Vuelve a la partida y le entrega el teclado.",
            Aliases = ["seguir-jugando", "volver", "resume"], Keywords = "reanudar despausar regresar juego",
            Unavailable = NeedMachine, Result = "Ctrl+Shift+T vuelve a la consola.",
            Run = context => { OpenPlay(); LeaveTerminal(); context.Success("Partida en curso · Ctrl+Shift+T para volver."); }
        },
        new()
        {
            Id = "play.pause", Name = "pausa", Group = "Partida", Summary = "Pausa la máquina sin salir de la consola.",
            Aliases = ["pausar"], Keywords = "detener congelar", Unavailable = NeedMachine,
            Run = context => { _host.Pause("Pausa desde la consola."); context.Success("Partida en pausa."); }
        },
        new()
        {
            Id = "play.reset", Name = "reiniciar", Group = "Partida", Summary = "Reinicia la partida o fusión; duro también limpia la memoria del original.",
            Aliases = ["reset"], Keywords = "empezar de nuevo", Unavailable = NeedMachine,
            Arguments = [new("modo", ArgumentKind.Choice, "duro borra la RAM.") { Optional = true, Choices = ["duro"] }],
            Examples = ["reiniciar", "reiniciar duro"],
            Run = context => { _host.Reset(context.Has("modo")); _host.Paused = true; context.Success("Partida reiniciada; usá continuar para jugar."); }
        },
        new()
        {
            Id = "play.capture", Name = "captura", Group = "Partida", Summary = "Guarda la imagen emulada a su resolución.",
            Aliases = ["foto", "screenshot"], Keywords = "imagen png pantalla", Unavailable = NeedMachine,
            Run = context => { SaveCapture(); context.Success(_host.Status); }
        },
        new()
        {
            Id = "play.slots", Name = "ranuras", Group = "Partida", Summary = "Lista las partidas guardadas de este juego y edición.",
            Keywords = "guardados estados partidas", Unavailable = NeedStates,
            Run = context =>
            {
                RefreshStateSlots();
                context.Table([new("Ranura", Right: true), new("Guardada", Flexible: true)], _savedSlots.Select(slot => new[] { slot.Number.ToString(), slot.Problem ?? slot.SavedAt?.ToString("dd/MM/yyyy HH:mm") ?? "vacía" }));
            }
        },
        new()
        {
            Id = "play.save", Name = "guardar", Group = "Partida", Summary = "Guarda la partida en una ranura. Si está ocupada exige escribir reemplazar.",
            Aliases = ["save"], Keywords = "estado progreso ranura", Unavailable = NeedStates,
            Arguments = [new("ranura", ArgumentKind.Number, "Ranura de este juego.") { Min = 1, Max = 5 }, new("confirmar", ArgumentKind.Choice, "Permite sobrescribir.") { Optional = true, Choices = ["reemplazar"] }],
            Examples = ["guardar 1", "guardar 1 reemplazar"], Errors = ["la ranura está ocupada y falta reemplazar"],
            Run = context =>
            {
                int slot = context.Number("ranura");
                RefreshStateSlots(); string path = _stateSlots.PathFor(_stateKey, slot);
                if (File.Exists(path) && !context.Has("confirmar"))
                    throw new CommandFailure(CommandProblem.InvalidArgument, "La ranura " + slot + " ya tiene una partida.", "Para reemplazarla: guardar " + slot + " reemplazar");
                _host.SaveStateToFile(path); context.Success($"Ranura {slot} guardada.");
            }
        },
        new()
        {
            Id = "play.load", Name = "cargar", Group = "Partida", Summary = "Recupera una ranura de este juego y edición.",
            Aliases = ["load"], Keywords = "estado progreso recuperar", Unavailable = NeedStates,
            Arguments = [new("ranura", ArgumentKind.Number, "Ranura de este juego.") { Min = 1, Max = 5 }],
            Examples = ["cargar 1"], Errors = ["la ranura está vacía o dañada"],
            Run = context =>
            {
                int slot = context.Number("ranura");
                RefreshStateSlots(); string path = _stateSlots.PathFor(_stateKey, slot);
                if (!File.Exists(path)) throw new CommandFailure(CommandProblem.MissingResource, $"La ranura {slot} está vacía.", "ranuras muestra las ocupadas.");
                _host.LoadStateFromFile(path); _host.Paused = true; context.Success($"Ranura {slot} cargada; continuar vuelve a jugar.");
            }
        },
        new()
        {
            Id = "play.rewind", Name = "rebobinar", Group = "Partida", Summary = "Activa el historial o retrocede en la partida.",
            Aliases = ["rewind"], Keywords = "volver atras historial retroceder", Unavailable = NeedStates,
            Arguments = [new("accion", ArgumentKind.Word, "on, off, vaciar, estado o los segundos a retroceder.") { Optional = true, Complete = _ => [new("estado"), new("on"), new("off"), new("vaciar"), new("5", "segundos")] }],
            Examples = ["rebobinar on", "rebobinar 5", "rebobinar estado"],
            Run = context =>
            {
                string action = context.Optional("accion") ?? "estado";
                if (action == "on") _host.SetRewindEnabled(true);
                else if (action == "off") _host.SetRewindEnabled(false);
                else if (action == "vaciar") _host.ClearRewind();
                else if (double.TryParse(action, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                {
                    if (_host.RewindPoints == 0) throw new CommandFailure(CommandProblem.WrongContext, "No hay historial todavía.", "rebobinar on lo activa; se acumula mientras la partida corre.");
                    _host.Rewind(seconds); _host.Paused = true;
                }
                else if (action != "estado") throw new CommandFailure(CommandProblem.InvalidArgument, "Usá on, off, vaciar, estado o una cantidad de segundos.");
                context.Write($"Historial: {(_host.RewindEnabled ? "activado" : "desactivado")} · {_host.RewindPoints} puntos · {_host.RewindBytes / (1024 * 1024.0):F1} MB");
            }
        },
        new()
        {
            Id = "play.inputs", Name = "entradas", Group = "Partida", Summary = "Graba y reproduce las entradas de esta sesión.",
            Aliases = ["grabar"], Keywords = "replay reproduccion grabacion", Unavailable = NeedStates,
            Arguments = [new("accion", ArgumentKind.Choice, "Qué hacer.") { Optional = true, Choices = ["estado", "grabar", "terminar", "reproducir", "detener"] }],
            Examples = ["entradas grabar", "entradas reproducir"],
            Run = context =>
            {
                switch (context.Optional("accion"))
                {
                    case "grabar": if (!_host.CanRecordInputs) throw new CommandFailure(CommandProblem.WrongContext, "Ahora no se puede grabar: quitá los breakpoints o terminá la reproducción."); _host.StartInputRecording(); break;
                    case "terminar": _host.StopInputRecording(); break;
                    case "reproducir": if (!_host.CanReplayInputs) throw new CommandFailure(CommandProblem.WrongContext, "No hay una grabación para reproducir.", "entradas grabar empieza una."); _host.StartInputReplay(); break;
                    case "detener": _host.StopInputReplay(); break;
                }
                context.Write($"Grabación: {_host.RecordedFrames} cuadros · {(_host.IsRecordingInputs ? "grabando" : _host.IsReplayingInputs ? "reproduciendo" : "detenida")}. Solo dura esta sesión.");
            }
        },
        new()
        {
            Id = "play.turbo", Name = "turbo", Group = "Partida", Summary = "Acelera la emulación todo lo posible.", Keywords = "rapido velocidad", Unavailable = NeedMachine,
            Arguments = [new("marca", ArgumentKind.Toggle, "Sin indicar, invierte el estado.") { Optional = true }], Examples = ["turbo on"],
            Run = context => { _host.Turbo = context.Has("marca") ? context.Toggle("marca") : !_host.Turbo; context.Success("Turbo: " + (_host.Turbo ? "activado" : "desactivado")); }
        },
        new()
        {
            Id = "play.tape", Name = "cinta", Group = "Partida", Summary = "Controla la cinta del ZX Spectrum.", Keywords = "tap tzx load reproducir rebobinar expulsar", Unavailable = NeedTape,
            Arguments = [new("accion", ArgumentKind.Choice, "Qué hacer.") { Optional = true, Choices = ["estado", "reproducir", "detener", "rebobinar", "expulsar", "load"] }],
            Examples = ["cinta reproducir", "cinta load"],
            Run = context =>
            {
                var zx = _host.Zx!; var tape = zx.Tape;
                switch (context.Optional("accion"))
                {
                    case "reproducir": if (!tape.IsLoaded) throw new CommandFailure(CommandProblem.MissingResource, "No hay una cinta cargada."); tape.Play(zx.Z80.T); break;
                    case "detener": tape.Stop(zx.Z80.T); break;
                    case "rebobinar": tape.Rewind(); break;
                    case "expulsar": tape.Eject(); break;
                    case "load": zx.TypeLoadCommand(); break;
                }
                context.Write(!tape.IsLoaded ? "Sin cinta." : tape.IsPlaying ? $"Reproduciendo · {tape.Progress * 100:F0} %" : tape.AtEnd ? "Cinta al final." : "Cinta detenida.");
            }
        },
        new()
        {
            Id = "play.stop-fusion", Name = "detener", Group = "Partida", Summary = "Termina la fusión o creación en curso.", Keywords = "cerrar salir", Unavailable = NeedFusion,
            Run = context => { _host.StopFusion(); context.Success("Fusión detenida."); }
        },
        new()
        {
            Id = "play.controls", Name = "controles", Group = "Partida", Summary = "Muestra cómo se juega en la máquina actual.", Keywords = "teclas mando joystick botones", Unavailable = NeedMachine,
            Run = context => { foreach (string line in ControlsHelp()) context.Write(line); }
        },
        new()
        {
            Id = "play.machines", Name = "maquinas", Group = "Partida", Summary = "Lista los sistemas y su estado real de implementación.", Aliases = ["máquinas", "sistemas"], Keywords = "hardware compatibilidad",
            Run = context => context.Table([new("Id"), new("Sistema", Flexible: true), new("Estado")], MachineCatalog.All.Select(machine => new[] { machine.Id, machine.Name, machine.Status == MachineStatus.Available ? "disponible" : "en desarrollo" }))
        },
        new()
        {
            Id = "play.machine", Name = "maquina", Group = "Partida", Summary = "Enciende una máquina implementada con su firmware local; no descarga nada.",
            Aliases = ["máquina"], Keywords = "encender sin juego basic",
            Arguments = [new("id", ArgumentKind.Word, "Identificador de maquinas.") { Complete = _ => MachineCatalog.All.Where(machine => machine.Status == MachineStatus.Available).Select(machine => new CompletionItem(machine.Id, machine.Name)) }],
            Examples = ["maquina zx48"], Errors = ["falta su firmware"],
            Run = context =>
            {
                string id = context.Text("id");
                var machine = MachineCatalog.All.FirstOrDefault(machine => machine.Id == id && machine.Status == MachineStatus.Available)
                    ?? throw new CommandFailure(CommandProblem.InvalidArgument, "Esa máquina no está disponible.", "maquinas lista las disponibles.");
                if (_host.Machine != null) _input.ReleaseAll(_host.Machine);
                _host.Start(machine, _romDir); _host.Paused = true; context.Success(machine.Name + " encendida · continuar le da el teclado.");
            }
        },
        new()
        {
            Id = "play.create", Name = "crear", Group = "Proyectos", Summary = "Abre el taller visual de creación.", Keywords = "juego nuevo desde cero",
            Run = context => { OpenCreator(); ReleaseConsoleKeyboard(); context.Success("Taller de creación abierto · Ctrl+Shift+T vuelve a la consola."); }
        },
        new()
        {
            Id = "play.fuse", Name = "fusionar", Group = "Proyectos", Summary = "Abre el taller visual de fusión.", Keywords = "mezclar combinar",
            Run = context => { OpenFusionStudio(); ReleaseConsoleKeyboard(); context.Success("Taller de fusión abierto · Ctrl+Shift+T vuelve a la consola."); }
        }
    ];

    private string ReadVariable(GameVariable variable)
    {
        try { return variable.Read(_host.Machine!.Memory).ToString(CultureInfo.InvariantCulture); }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException or OverflowException) { return "?"; }
    }

    private void ReportOpenProblem(CommandContext context)
    {
        if (_pendingOpen != null) { context.Warning("el archivo contiene varios programas: elegí uno en el panel."); return; }
        string problem = _errorPopup ?? ""; _errorPopup = null;
        throw new CommandFailure(CommandProblem.MissingResource, problem.ReplaceLineEndings(" "), "El juego anterior y tus archivos no se modificaron.");
    }

    private IEnumerable<CommandSpec> AnalysisOrders() =>
    [
        new()
        {
            Id = "analysis.registers", Name = "registros", Group = "Análisis", Summary = "Lee los registros de la CPU.", Aliases = ["regs", "r"], Keywords = "cpu pc depurar", Unavailable = NeedMachine,
            Run = context =>
            {
                context.Detail(InspectionContext());
                context.Table([new("Registro"), new("Valor", Right: true)], _host.Machine!.Cpu.GetRegisters().Select(register => new[] { register.Name, register.Value.ToString(register.Bits <= 8 ? "X2" : "X4") }));
            }
        },
        new()
        {
            Id = "analysis.memory", Name = "memoria", Group = "Análisis", Summary = "Muestra memoria sin efectos de lectura del hardware.", Aliases = ["mem", "m"], Keywords = "volcado hexadecimal bytes ver", Unavailable = NeedMachine,
            Arguments = [new("direccion", ArgumentKind.Hex, "En hexadecimal."), new("cantidad", ArgumentKind.Number, "Bytes a mostrar.") { Optional = true, Min = 1, Max = 4096 }],
            Examples = ["memoria 8000", "memoria 8000 256"],
            Run = context =>
            {
                var machine = _host.Machine!; uint address = TerminalAddress(context.Hex("direccion")); int length = context.Number("cantidad", 64);
                ulong end = Math.Min((ulong)address + (uint)length, 1UL << machine.Memory.AddressBits);
                context.Detail(InspectionContext());
                for (ulong row = address; row < end; row += 16)
                {
                    var bytes = Enumerable.Range(0, (int)Math.Min(16UL, end - row)).Select(offset => machine.Memory.Peek((uint)(row + (uint)offset))).ToArray();
                    string text = new(bytes.Select(value => value is >= 32 and < 127 ? (char)value : '.').ToArray());
                    context.Write($"{row:X4}  {string.Join(' ', bytes.Select(value => value.ToString("X2"))),-47}  {text}");
                }
            }
        },
        new()
        {
            Id = "analysis.poke", Name = "escribir", Group = "Análisis", Summary = "Escribe un byte en memoria para probar una hipótesis.", Aliases = ["poke"], Keywords = "modificar cambiar valor parche vidas", Unavailable = NeedMachine,
            Arguments = [new("direccion", ArgumentKind.Hex, "En hexadecimal."), new("valor", ArgumentKind.Hex, "Byte en hexadecimal.")],
            Examples = ["escribir 0040 09"], Result = "Solo cambia la memoria de esta sesión; el archivo del juego no se toca. reiniciar o cargar deshacen el cambio.",
            Run = context =>
            {
                uint address = TerminalAddress(context.Hex("direccion")), value = context.Hex("valor");
                if (value > 255) throw new CommandFailure(CommandProblem.InvalidArgument, "El valor debe ser un byte, de 00 a FF.");
                byte before = _host.Machine!.Memory.Peek(address);
                _host.Machine.Memory.Poke(address, (byte)value);
                context.Success($"{address:X4}: {before:X2} -> {_host.Machine.Memory.Peek(address):X2}");
                context.Detail("Válido para esta edición: " + InspectionContext());
            }
        },
        new()
        {
            Id = "analysis.code", Name = "codigo", Group = "Análisis", Summary = "Desensambla instrucciones sin modificar la máquina.", Aliases = ["código", "dis", "u"], Keywords = "ensamblador rutina instrucciones", Unavailable = NeedMachine,
            Arguments = [new("direccion", ArgumentKind.Hex, "Por defecto, el PC actual.") { Optional = true }, new("cantidad", ArgumentKind.Number, "Instrucciones.") { Optional = true, Min = 1, Max = 256 }],
            Examples = ["codigo", "codigo 8000 32"],
            Run = context =>
            {
                var machine = _host.Machine!; uint pc = machine.Cpu.ProgramCounter;
                uint address = context.Has("direccion") ? TerminalAddress(context.Hex("direccion")) : pc;
                context.Detail(InspectionContext());
                for (int i = 0, count = context.Number("cantidad", 16); i < count; i++)
                {
                    var instruction = machine.Disassembler.Disassemble(machine.Memory.Peek, address);
                    string mark = _host.Debug.Breakpoints.Contains(address) ? "*" : address == pc ? ">" : " ";
                    context.Write($"{mark} {address:X4}  {instruction.Text}", address == pc ? TerminalKind.Success : TerminalKind.Output);
                    address += (uint)Math.Max(1, instruction.Length);
                    if (machine.Memory.AddressBits < 32) address &= (1u << machine.Memory.AddressBits) - 1;
                }
            }
        },
        new()
        {
            Id = "analysis.step", Name = "paso", Group = "Análisis", Summary = "Avanza una instrucción, un cuadro o pasa por encima de una rutina.", Aliases = ["step", "s"], Keywords = "instruccion cuadro avanzar depurar", Unavailable = NeedMachine,
            Arguments = [new("modo", ArgumentKind.Choice, "Sin indicar, una instrucción.") { Optional = true, Choices = ["cuadro", "saltar"] }],
            Examples = ["paso", "paso cuadro", "paso saltar"],
            Run = context =>
            {
                _host.Paused = true;
                switch (context.Optional("modo"))
                {
                    case "cuadro": _host.StepFrame(); break;
                    case "saltar": _host.StepOver(); _awaitingStop = true; break;
                    default: _host.StepInto(); break;
                }
                context.Write($"PC = {_host.Machine!.Cpu.ProgramCounter:X4} · cuadro {_host.Machine.FrameCount}");
            }
        },
        new()
        {
            Id = "analysis.run", Name = "correr", Group = "Análisis", Summary = "Ejecuta la máquina con la consola abierta, hasta un breakpoint o una pausa.", Aliases = ["run", "g"], Keywords = "ejecutar continuar depurar", Unavailable = NeedMachine,
            Arguments = [new("hasta", ArgumentKind.Hex, "Se detiene al llegar a esta dirección.") { Optional = true }],
            Examples = ["correr", "correr 8000"], Cancellation = "pausa la detiene.", Result = "El teclado sigue en la consola; un mensaje avisa dónde se detuvo.",
            Run = context =>
            {
                if (context.Has("hasta")) _host.RunTo(TerminalAddress(context.Hex("hasta"))); else _host.Continue();
                _awaitingStop = true; context.Success("Máquina en ejecución · pausa la detiene.");
            }
        },
        new()
        {
            Id = "analysis.breakpoint", Name = "bp", Group = "Análisis", Summary = "Administra los breakpoints del depurador.", Aliases = ["breakpoint"], Keywords = "detener punto de parada", Unavailable = NeedMachine,
            Arguments = [new("accion", ArgumentKind.Choice, "Qué hacer.") { Optional = true, Choices = ["lista", "agregar", "quitar", "limpiar"] }, new("direccion", ArgumentKind.Hex, "En hexadecimal.") { Optional = true }],
            Examples = ["bp agregar 8000", "bp quitar 8000", "bp limpiar"], Result = "Se comparten con el panel del depurador.",
            Run = context =>
            {
                string action = context.Optional("accion") ?? "lista";
                if (action is "agregar" or "quitar")
                {
                    if (!context.Has("direccion")) throw new CommandFailure(CommandProblem.InvalidArgument, "Falta la dirección.", "Ejemplo: bp " + action + " 8000");
                    uint address = TerminalAddress(context.Hex("direccion"));
                    if (action == "agregar") _host.Debug.Add(address); else _host.Debug.Remove(address);
                }
                else if (action == "limpiar") _host.Debug.Clear();
                var points = _host.Debug.Breakpoints.Order().ToArray();
                context.Write(points.Length == 0 ? "Sin breakpoints." : "Breakpoints: " + string.Join(", ", points.Select(point => point.ToString("X4"))));
            }
        },
        new()
        {
            Id = "analysis.search", Name = "rastrear", Group = "Análisis", Summary = "Busca una variable comparando la memoria entre momentos de la partida.", Aliases = ["buscar-memoria"], Keywords = "variable vidas puntos encontrar dato candidatos", Unavailable = NeedMachine,
            Arguments = [new("accion", ArgumentKind.Choice, "iniciar toma la primera foto.") { Choices = ["iniciar", "igual", "cambio", "igual-que-antes", "subio", "bajo", "lista"] }, new("valor", ArgumentKind.Number, "Para igual: el valor buscado.") { Optional = true, Min = 0, Max = 255 }],
            Examples = ["rastrear iniciar", "rastrear igual 3", "rastrear bajo", "rastrear lista"], Requires = "repetir el ciclo: jugar un poco, filtrar y volver a filtrar",
            Result = "Una coincidencia vale para esta edición; comprobá el efecto antes de llamarla variable.",
            Run = context =>
            {
                string action = context.Text("accion");
                if (action == "iniciar")
                {
                    var (start, end) = CodeWindow.SearchRange(_host.Machine!.Id);
                    _terminalSearch = new(_host.Machine.Memory); _terminalSearch.Start(start, end);
                    context.Success($"Búsqueda iniciada: {_terminalSearch.Count} direcciones. Jugá un poco y filtrá."); return;
                }
                var search = _terminalSearch ?? throw new CommandFailure(CommandProblem.WrongContext, "Todavía no empezaste una búsqueda.", "rastrear iniciar toma la primera foto de la memoria.");
                if (action != "lista")
                {
                    if (action == "igual" && !context.Has("valor")) throw new CommandFailure(CommandProblem.InvalidArgument, "Falta el valor.", "Ejemplo: rastrear igual 3");
                    search.Filter(action switch { "igual" => SearchCondition.EqualTo, "cambio" => SearchCondition.Changed, "subio" => SearchCondition.Increased, "bajo" => SearchCondition.Decreased, _ => SearchCondition.Unchanged }, context.Number("valor", 0));
                }
                context.Write(search.Count + " direcciones candidatas.");
                foreach (var (address, value) in search.Results(16)) context.Write($"{address:X4} = {value:X2} ({value})");
                if (search.Count > 16) context.Detail("Filtrá más para reducir la lista.");
            }
        },
        new()
        {
            Id = "analysis.variables", Name = "variables", Group = "Análisis", Summary = "Lista las variables conocidas de este juego, con su valor y su evidencia.", Keywords = "perfil datos vidas puntos propósito", Unavailable = NeedMachine,
            Run = context =>
            {
                var profile = _host.LoadedProfile ?? throw new CommandFailure(CommandProblem.MissingResource, "Este juego no tiene un perfil con variables.", "preparar <juego> intenta crearlo sin IA.", "rastrear busca una dirección a mano.");
                context.Detail(InspectionContext());
                context.Table([new("Variable"), new("Dirección"), new("Valor", Right: true), new("Propósito", Flexible: true, Optional: true)],
                    profile.Variables.Select(item => new[] { item.Key, item.Value.Address, ReadVariable(item.Value), item.Value.PurposeSummary }));
            }
        }
    ];
}
