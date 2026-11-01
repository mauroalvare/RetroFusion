using System.Globalization;
using Retro.Machines;

namespace Retro.App;

public sealed partial class RetroApp
{
    private string[] _terminalListedKeys = [];
    private IReadOnlyList<TerminalCommand> BuildTerminalCommands() => [
        new("cd", "cd <biblioteca|originales|creados|fusiones|favoritos|recientes|herramientas|opciones|ayuda>", "Cambia de sección virtual; no recorre carpetas del sistema.", args =>
        {
            string location = Required(args, 1)[0].Trim('/').ToLowerInvariant();
            if (location is ".." or "" or "inicio") location = "biblioteca";
            if (location is not ("herramientas" or "opciones" or "ayuda")) _home.TerminalSection(location);
            Terminal.Location = location; _terminalListedKeys = []; return ListTerminalLocation();
        }),
        new("pwd", "pwd", "Muestra la sección actual.", args => [Terminal.Prompt]),
        new("ls", "ls", "Lista la sección actual.", args => ListTerminalLocation()),
        new("juegos", "juegos [texto]", "Lista y numera los resultados de la biblioteca compartida.", args =>
        {
            if (args.Count > 0) _home.TerminalFilter("buscar", string.Join(' ', args));
            return ListTerminalGames();
        }),
        new("buscar", "buscar <texto>", "Busca títulos y géneros sin perder los demás filtros.", args => { _home.TerminalFilter("buscar", string.Join(' ', Required(args, 1, exact: false))); return ListTerminalGames(); }),
        new("filtrar", "filtrar <genero|año|sistema|tipo|actividad|orden|disponibles|quitar> [valor]", "Comparte filtros y orden con la biblioteca gráfica. Año admite 1980:1989; sin selecciona desconocidos.", args =>
        {
            Required(args, 1, exact: false);
            string result = _home.TerminalFilter(args[0].ToLowerInvariant(), string.Join(' ', args.Skip(1)));
            _terminalListedKeys = []; return [result];
        }),
        new("ficha", "ficha <número o clave>", "Muestra sistema, tipo, año, géneros y procedencia de los datos.", args =>
        {
            var game = TerminalGameById(Required(args, 1)[0]);
            var result = new List<string> { game.Title, "Clave: " + game.Key, $"Sistema: {game.Systems} · Tipo: {game.Kind}", "Año: " + game.Info.YearLabel,
                "Género: " + (game.Info.Genres.Count == 0 ? "sin clasificar" : string.Join(", ", game.Info.Genres)), game.Openable ? "Archivo disponible para abrir; la jugabilidad depende de su compatibilidad." : "No disponible para abrir." };
            if (game.Info.WorkYear) result.Add("Año de la obra; el año de esta plataforma no está determinado.");
            if (game.Detected is { } facts) { result.Add(facts.Method); result.Add("Fuente: " + facts.Source); if (facts.Publisher != null) result.Add("Editora: " + facts.Publisher); }
            result.Add("Las correcciones de tu ficha personal tienen prioridad sobre la detección."); return result;
        }),
        new("favorito", "favorito <número o clave> <on|off>", "Modifica el mismo favorito que muestra la estrella de la biblioteca.", args =>
        {
            Required(args, 2); var game = TerminalGameById(args[0]);
            if (args[1] is not ("on" or "off")) throw new ArgumentException("Usá on u off.");
            _home.TerminalFavorite(game.Key, args[1] == "on"); return ["Favorito: " + args[1] + " · " + game.Title];
        }),
        new("jugar", "jugar <número o clave>", "Abre el original, creado o fusión seleccionado. Ctrl+Shift+T vuelve a la consola.", args =>
        {
            var game = TerminalGameById(Required(args, 1)[0]);
            if (!game.Openable) throw new InvalidOperationException("Ese juego todavía no está disponible para abrir.");
            game.Play(); _terminalOpen = false; _host.Continue();
            return ["Abriendo: " + game.Title];
        }),
        new("abrir", "abrir \"ruta del juego\"", "Abre un medio con el emulador. Las rutas con espacios van entre comillas.", args =>
        {
            string path = Required(args, 1)[0]; if (!File.Exists(path)) throw new IOException("No existe ese archivo.");
            OpenFile(path); _terminalOpen = false; _host.Continue(); return ["Abriendo medio: " + Path.GetFileName(path)];
        }),
        new("continuar", "continuar", "Vuelve a la partida y le entrega el teclado.", args =>
        { if (_host.Machine == null) throw new InvalidOperationException("Primero abrí un juego o encendé una máquina."); OpenPlay(); LeaveTerminal(); return ["Partida en curso · Ctrl+Shift+T para volver."]; }),
        new("interfaz", "interfaz <retro|terminal|hardcore|preview>", "Cambia el estilo compartido, o abre la elección con vistas previas.", args =>
        {
            string id = Required(args, 1)[0];
            if (id == "preview") { OpenInterfacePicker(); return ["Vistas previas abiertas."]; }
            if (!InterfaceStyles.TryParse(id, out var style)) throw new ArgumentException("Estilos: retro, terminal, hardcore o preview.");
            SetInterface(style); return ["Interfaz: " + InterfaceStyles.Describe(style).Name];
        }),
        new("opciones", "opciones [texto]", "Lee los mismos ajustes, valores y descripciones que el menú de preferencias.", args => ListTerminalOptions(string.Join(' ', args))),
        new("opcion", "opcion <id> [valor]", "Lee o cambia un ajuste compartido. Booleanos: on/off. Ej.: opcion volumen 60.", args =>
        {
            Required(args, 1, exact: false); if (args.Count > 2) throw new ArgumentException("Usá opcion <id> [valor].");
            var option = Options.FirstOrDefault(option => option.Id.Equals(args[0], StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Ajuste desconocido. Escribí opciones.");
            if (args.Count == 2 && option.Write(args[1]) is { } problem) throw new ArgumentException(problem);
            return [option.Id + " = " + option.Read(), option.Description, OptionValues(option), _prefs.Problem ?? _sound.Problem ?? "Valor compartido con todos los estilos."];
        }),
        new("acciones", "acciones [texto]", "Catálogo vivo de funciones del programa. Incluye las que se agreguen al menú.", args => ListTerminalActions(string.Join(' ', args))),
        new("hacer", "hacer <id de acción>", "Ejecuta una acción del catálogo. Los instrumentos visuales abren su panel dentro del programa; la consola permanece abajo.", args =>
        {
            var command = Commands.FirstOrDefault(command => command.Id == Required(args, 1)[0]) ?? throw new ArgumentException("Acción desconocida. Escribí acciones.");
            if (!command.Enabled) throw new InvalidOperationException(command.Unavailable);
            command.Execute();
            if (_home.IsOpen || _hub != HubPage.None || _creator.IsOpen || _studio.IsOpen || _code.IsOpen || _stateWindowOpen || _debugMode || ConfigurationOpen || _browser.IsOpen)
                _terminalOpen = false;
            if (command.Id == "play") LeaveTerminal();
            return [command.Title, "Ctrl+Shift+T devuelve la consola completa."];
        }),
        new("estado", "estado", "Muestra máquina, partida, pausa y tareas de identificación.", args =>
            [$"Máquina: {_host.Machine?.DisplayName ?? "apagada"} · {(_host.Paused ? "en pausa" : "en ejecución")}", _host.Status ?? "Lista", _gameMetadata.Status]),
        new("pausa", "pausa", "Pausa la partida; escribir en la consola nunca manda teclas al juego.", args => { _host.Pause("Pausa desde la consola."); return ["Partida en pausa."]; }),
        new("reiniciar", "reiniciar [duro]", "Reinicia toda la partida o fusión; duro también limpia la memoria del original.", args =>
        {
            if (args.Count > 1 || args.Count == 1 && args[0] != "duro") throw new ArgumentException("Usá reiniciar o reiniciar duro.");
            _host.Reset(args.Count == 1); _host.Paused = true; return ["Partida reiniciada; usá continuar para jugar."];
        }),
        new("captura", "captura", "Guarda la imagen emulada mediante la misma acción del menú.", args =>
        { var command = Commands.First(command => command.Id == "capture"); if (!command.Enabled) throw new InvalidOperationException(command.Unavailable); command.Execute(); return [_host.Status]; }),
        new("ranuras", "ranuras", "Lista las cinco partidas guardadas de este juego y edición.", args =>
        { RequireTerminalStates(); RefreshStateSlots(); return _savedSlots.Select(slot => $"{slot.Number}: {slot.Problem ?? slot.SavedAt?.ToString("dd/MM/yyyy HH:mm") ?? "vacía"}").ToArray(); }),
        new("guardar", "guardar <1..5> [reemplazar]", "Guarda una ranura. Si está ocupada exige escribir reemplazar.", args => TerminalStateSlot(args, save: true)),
        new("cargar", "cargar <1..5>", "Recupera una ranura del juego y edición actuales, conservando la consola.", args => TerminalStateSlot(args, save: false)),
        new("registros", "registros", "Lee los registros actuales de la CPU.", args => _host.Machine?.Cpu.GetRegisters().Select(register => $"{register.Name,-8} {register.Value.ToString(register.Bits <= 8 ? "X2" : "X4")}").ToArray() ?? ["No hay máquina encendida."]),
        new("memoria", "memoria <dirección hexadecimal> [cantidad 1..256]", "Inspecciona memoria sin efectos de lectura del hardware.", args => TerminalMemory(args)),
        new("codigo", "codigo [dirección hexadecimal]", "Desensambla doce instrucciones sin modificar la máquina.", args => TerminalCode(args)),
        new("paso", "paso [cuadro|saltar]", "Avanza una instrucción, un cuadro o pasa una llamada de rutina.", args =>
        {
            if (_host.Machine == null) throw new InvalidOperationException("Encendé una máquina.");
            if (args.Count == 0) _host.StepInto(); else if (args.Count == 1 && args[0] == "cuadro") _host.StepFrame(); else if (args.Count == 1 && args[0] == "saltar") _host.StepOver(); else throw new ArgumentException("Usá paso, paso cuadro o paso saltar.");
            return ["PC = " + _host.Machine.Cpu.ProgramCounter.ToString("X4")];
        }),
        new("maquinas", "maquinas", "Lista sistemas y su estado real de implementación.", args => MachineCatalog.All.Select(machine => $"{machine.Id,-12} {machine.Name} · {machine.Status}").ToArray()),
        new("maquina", "maquina <id>", "Enciende una máquina implementada con su firmware local; no descarga nada.", args =>
        {
            string id = Required(args, 1)[0]; var machine = MachineCatalog.All.FirstOrDefault(machine => machine.Id == id && machine.Status == MachineStatus.Available) ?? throw new ArgumentException("Máquina no disponible. Escribí maquinas.");
            _host.Start(machine, _romDir); _host.Paused = true; return [machine.Name + " encendida · usá continuar."];
        }),
        new("actualizar", "actualizar", "Abre el actualizador del programa.", args => { _updates.Open(); return ["Actualizador abierto."]; }),
        new("copiar", "copiar", "Copia el registro de la consola al portapapeles.", args => { ImGuiNET.ImGui.SetClipboardText(string.Join(Environment.NewLine, Terminal.Lines)); return ["Registro copiado."]; }),
        new("salir", "salir", "Cierra el programa, con la misma protección de trabajos pendientes del menú.", args => { RequestQuit(); return ["Solicitud de cierre recibida."]; })
    ];

    private static IReadOnlyList<string> Required(IReadOnlyList<string> args, int count, bool exact = true)
    {
        if (args.Count < count || exact && args.Count != count) throw new ArgumentException("Faltan datos o sobran argumentos. Escribí ayuda <orden> para ver la forma de uso.");
        return args;
    }
    private IEnumerable<string> ListTerminalLocation() => Terminal.Location switch
    {
        "opciones" => ListTerminalOptions(""), "herramientas" => ListTerminalActions(""), "ayuda" => ["Escribí ayuda para ver todas las órdenes. acciones lista los instrumentos y opciones los ajustes."], _ => ListTerminalGames()
    };
    private IEnumerable<string> ListTerminalActions(string filter) => CommandPalette.Search(Commands, filter).Select(command =>
        $"{command.Id} · {command.Title}" + (command.Enabled ? "" : " [" + command.Unavailable + "]"));
    private static string OptionValues(AppOption option) => option.Kind switch
    { AppOptionKind.Toggle => "Valores: on, off", AppOptionKind.Choice => "Valores: " + string.Join(", ", option.Choices!.Select(choice => choice.Value)), _ => $"Valores: {option.Min}..{option.Max}" };
    private IEnumerable<string> ListTerminalOptions(string filter) => Options.Where(option => (option.Id + " " + option.Title + " " + option.Group).Contains(filter, StringComparison.OrdinalIgnoreCase))
        .SelectMany(option => new[] { option.Id + " = " + option.Read() + " · " + option.Title, "  " + OptionValues(option) + ". " + option.Description });

    private IEnumerable<string> ListTerminalGames()
    {
        var games = _home.TerminalGames(); _terminalListedKeys = games.Select(game => game.Key).Take(200).ToArray();
        var lines = new List<string> { $"{games.Count} juegos · los filtros y favoritos se comparten con la interfaz gráfica." };
        for (int i = 0; i < _terminalListedKeys.Length; i++)
        {
            var game = games[i];
            lines.Add($"{i + 1:000} {(game.Favorite ? "*" : " ")} {game.Title} | {game.Systems} | {game.Info.YearLabel} | {string.Join(", ", game.Info.Genres)}" + (game.Openable ? "" : " [no disponible]"));
        }
        if (games.Count > 200) lines.Add("Se muestran 200. Usá buscar o filtrar para reducir la lista.");
        lines.Add("jugar <número> · ficha <número> · favorito <número> on · filtrar quitar"); return lines;
    }

    private TerminalGame TerminalGameById(string value)
    {
        var games = _home.TerminalGames(filtered: false);
        if (int.TryParse(value, out int number))
        {
            if (number < 1 || number > _terminalListedKeys.Length) throw new ArgumentException("Escribí juegos antes de elegir un número de la lista.");
            return games.FirstOrDefault(game => game.Key == _terminalListedKeys[number - 1]) ?? throw new InvalidOperationException("La biblioteca cambió. Volvé a listar juegos.");
        }
        var matches = games.Where(game => value.Length >= 8 && game.Key.StartsWith(value, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new ArgumentException("Clave inexistente o ambigua. Usá juegos y elegí un número.");
    }

    private void RequireTerminalStates()
    { if (!_host.CanUseStates) throw new InvalidOperationException("Disponible en un original cargado; cada juego y edición tiene sus ranuras."); }
    private IEnumerable<string> TerminalStateSlot(IReadOnlyList<string> args, bool save)
    {
        RequireTerminalStates(); Required(args, 1, exact: false);
        if (args.Count > (save ? 2 : 1) || !int.TryParse(args[0], out int number) || number is < 1 or > 5 || args.Count == 2 && args[1] != "reemplazar") throw new ArgumentException("Ranura de 1 a 5. Para reemplazar: guardar N reemplazar.");
        RefreshStateSlots(); string path = _stateSlots.PathFor(_stateKey, number);
        if (save && File.Exists(path) && args.Count != 2) return ["Esa ranura tiene una partida. Para reemplazarla escribí: guardar " + number + " reemplazar"];
        if (save) _host.SaveStateToFile(path); else _host.LoadStateFromFile(path);
        _host.Paused = true; return [$"Ranura {number} {(save ? "guardada" : "cargada")}."];
    }
    private uint TerminalAddress(string text)
    {
        int bits = _host.Machine?.Memory.AddressBits ?? throw new InvalidOperationException("No hay máquina encendida.");
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address) || bits < 32 && address >= (1u << bits)) throw new ArgumentException("La dirección hexadecimal está fuera de la memoria de esta máquina.");
        return address;
    }
    private IEnumerable<string> TerminalMemory(IReadOnlyList<string> args)
    {
        Required(args, 1, exact: false); if (args.Count > 2) throw new ArgumentException("Usá memoria <hex> [cantidad].");
        uint address = TerminalAddress(args[0]); int length = 64;
        if (args.Count == 2 && (!int.TryParse(args[1], out length) || length is < 1 or > 256)) throw new ArgumentException("Cantidad de 1 a 256 bytes.");
        var machine = _host.Machine!; ulong end = Math.Min((ulong)address + (uint)length, 1UL << machine.Memory.AddressBits);
        var lines = new List<string>();
        for (ulong row = address; row < end; row += 16)
            lines.Add($"{row:X4}: " + string.Join(' ', Enumerable.Range(0, (int)Math.Min(16UL, end - row)).Select(offset => machine.Memory.Peek((uint)(row + (uint)offset)).ToString("X2"))));
        return lines;
    }
    private IEnumerable<string> TerminalCode(IReadOnlyList<string> args)
    {
        if (args.Count > 1) throw new ArgumentException("Usá codigo [hex].");
        var machine = _host.Machine ?? throw new InvalidOperationException("No hay máquina encendida.");
        uint address = args.Count == 0 ? machine.Cpu.ProgramCounter : TerminalAddress(args[0]); var lines = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            var instruction = machine.Disassembler.Disassemble(machine.Memory.Peek, address);
            lines.Add($"{address:X4}  {instruction.Text}"); address += (uint)Math.Max(1, instruction.Length);
            if (machine.Memory.AddressBits < 32) address &= (1u << machine.Memory.AddressBits) - 1;
        }
        return lines;
    }
}
