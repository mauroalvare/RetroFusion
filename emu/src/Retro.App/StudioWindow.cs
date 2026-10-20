using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Retro.Ai;
using Retro.Core;
using Retro.Fusion;
using Retro.Studio;

namespace Retro.App;

/// <summary>
/// "Crear juego fusionado con IA": pick 2 or 3 games, the AI proposes ideas that mix them
/// into ONE new game, writes it as a script over the real emulated games, tries it and
/// fixes it; then the player asks for changes and every change is a new version.
/// </summary>
public sealed class StudioWindow
{
    private static readonly Vector4 Gold = new(1, 0.85f, 0.3f, 1);
    private static readonly Vector4 Blue = new(0.6f, 0.8f, 1, 1);
    private static readonly Vector4 Red = new(1, 0.5f, 0.4f, 1);
    private static readonly string[] SlotNames = { "a", "b", "c" };

    private sealed record ManualStep(string Title, string Prompt, Action<string> OnAnswer);

    private readonly EmulatorHost _host;
    private readonly string _romDir;
    private readonly AiSettings _ai;
    private bool _open;
    private string _folder;
    private readonly List<GameSource> _games = new();
    private readonly Dictionary<string, string> _unprepared = new();
    private readonly Dictionary<string, string> _labels = new();
    private Task<Retro.Compat.PrepareResult>? _prepareTask;
    private CancellationTokenSource? _prepareCts;
    private string _preparing = "";
    private volatile string _prepareProgress = "";
    private readonly List<string> _chosen = new();   // order = slots a, b, c
    private string _gameSearch = "";
    private string _status = "";

    private string _wish = "";
    private string _ownIdea = "";
    private string _request = "";
    private List<GameConcept>? _concepts;
    private volatile string _progress = "";
    private readonly AiBuildWindow _build = new("estudio");
    private string? _problem;          // last failure, shown in red
    private string? _problemDetail;    // validation report behind it
    private string? _lastTest;         // summary of the playtest of the game being played
    private CancellationTokenSource? _cts;
    private Task<List<GameConcept>>? _conceptTask;
    private Task<DesignOutcome>? _designTask;
    private Task<(StudioGame game, ValidationReport report)>? _validateTask;
    private Action<StudioGame, ValidationReport>? _afterValidate;
    private Task<StudioSession>? _startTask;
    private StudioGame? _starting;
    private ManualStep? _manual;
    private string _pasted = "";
    private string? _agentDir;
    private StudioGame? _agentTemplate;
    private DateTime _agentSeen;
    private double _agentPoll;
    private string _code = "";
    private string _codeFor = "";
    private int _seenEvents;
    private readonly List<FusionEvent> _toasts = new();

    public StudioWindow(EmulatorHost host, string romDir, string gamesFolder, AiSettings ai)
    {
        _host = host;
        _romDir = romDir;
        _folder = gamesFolder;
        _ai = ai;
    }

    public void Open()
    {
        _open = true;
        if (_games.Count == 0)
            Scan();
    }

    /// <summary>Command line --studio: plays a stored fusion game with the games in the folder.</summary>
    public void PlayById(string id)
    {
        Open();
        var game = StudioLibrary.AllVersions().Where(g => g.Id == id).OrderByDescending(g => g.Version).FirstOrDefault();
        if (game == null)
        {
            _status = $"No existe el juego \"{id}\".";
            return;
        }
        _chosen.Clear();
        _chosen.AddRange(game.Games.Select(g => g.Profile));
        StartGame(game);
    }

    private void Scan()
    {
        _games.Clear();
        _unprepared.Clear();
        _labels.Clear();
        if (!Directory.Exists(_folder))
        {
            _status = "La carpeta no existe.";
            return;
        }
        var catalog = Retro.Compat.GameCatalog.Scan(_folder);
        foreach (var g in catalog)
        {
            _games.Add(g.Source);
            if (!g.Prepared)
                _unprepared[g.Profile.Id] = g.Path;
            bool twin = catalog.Count(o => o.Profile.Name == g.Profile.Name) > 1;
            _labels[g.Profile.Id] = twin ? $"{g.Profile.Name} · {Path.GetFileName(g.Path)}" : g.Profile.Name;
        }
        _status = $"{_games.Count} juegos encontrados.";
    }

    private IReadOnlyDictionary<string, GameSource> Sources() => _games.ToDictionary(g => g.Profile.Id);

    private List<RecipeGame> Slots() =>
        _chosen.Select((id, i) => new RecipeGame { Slot = SlotNames[i], Profile = id }).ToList();

    private bool Busy => _prepareTask != null || _conceptTask != null || _designTask != null || _validateTask != null || _startTask != null;

    // ------------------------------------------------------------------ drawing
    private float? _panelLeft;

    /// <summary>Screen width to leave free on the right for this panel (0 when closed, collapsed or not on the right half).</summary>
    public float RightReserve(float screenWidth) =>
        _open && _panelLeft is float left && left > screenWidth / 2 ? screenWidth - left : 0;

    public void Draw()
    {
        Poll();
        DrawToasts();
        if (!_open)
        {
            _panelLeft = null;
            return;
        }
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + vp.Size.X - 540, vp.Pos.Y + 30), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(530, Math.Min(760, vp.Size.Y - 70)), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Crear juego fusionado con IA", ref _open))
        {
            _panelLeft = null;
            ImGui.End();
            return;
        }
        _panelLeft = ImGui.GetWindowPos().X - vp.Pos.X;
        if (_host.Fusion is StudioSession session)
            DrawPlaying(session);
        else
            DrawSetup();
        ImGui.End();
    }

    /// <summary>Progress of "Preparar" (the buttons are next to the games without verified variables).</summary>
    private void DrawPreparation()
    {
        if (_unprepared.Count > 0 && _prepareTask == null)
            AiPicker.Hint("Prepará estos juegos para que el guion pueda leer sus vidas, puntaje o posición y usar sus piezas en una escena propia.");
        if (_prepareTask == null)
            return;
        ImGui.TextColored(new Vector4(1, 0.85f, 0.3f, 1), $"Preparando {_preparing}...");
        AiPicker.Hint(_prepareProgress);
        if (ImGui.Button("Cancelar la preparación"))
            _prepareCts?.Cancel();
    }

    /// <summary>Command line --preparar: opens the window and prepares that file.</summary>
    public void Prepare(string path)
    {
        Open();
        StartPrepare(Path.GetFileName(path), path);
    }

    private void StartPrepare(string name, string path)
    {
        Retro.Compat.IPlayAdvisor? advisor = _ai.CreateProvider() is { } provider ? new AiPlayAdvisor(provider)
            : _ai.Provider == "agent" ? new FolderPlayAdvisor(Path.Combine(Path.GetTempPath(), "RetroEmu-preparar", Path.GetFileNameWithoutExtension(path)))
            : null;
        var options = new Retro.Compat.PrepareOptions { FirmwareDirectory = _romDir, Advisor = advisor };
        _preparing = name;
        _prepareProgress = advisor != null ? $"Primero se prueban las teclas habituales; después decide {advisor.Name}."
            : "Sin IA (\"copiar y pegar\" no sirve para esto): solo se prueban las teclas habituales.";
        _prepareCts = new CancellationTokenSource();
        var ct = _prepareCts.Token;
        _prepareTask = Task.Run(() => Retro.Compat.GamePreparer.PrepareAsync(path, options, line => _prepareProgress = line, ct));
    }

    private void DrawSetup()
    {
        ImGui.TextWrapped("Combiná las reglas de 2 o 3 juegos en uno nuevo, con una mecánica y una presentación propias. " +
                          "Después de jugarlo podés pedir cambios y conservar sus versiones.");
        if (ImGui.TreeNode("Carpetas de juegos"))
        {
            ImGui.SetNextItemWidth(-90);
            ImGui.InputText("##carpeta", ref _folder, 1024);
            ImGui.SameLine();
            if (ImGui.Button("Buscar")) Scan();
            if (ImGui.SmallButton("Abrir mis juegos fusionados"))
            {
                Directory.CreateDirectory(StudioLibrary.UserFolder);
                Process.Start(new ProcessStartInfo { FileName = StudioLibrary.UserFolder, UseShellExecute = true });
            }
            ImGui.TextWrapped(StudioLibrary.UserFolder);
            ImGui.TreePop();
        }
        AiPicker.Hint(_status);
        ImGui.Separator();
        ImGui.TextColored(Gold, $"1. Elegí los juegos ({_chosen.Count}/3)");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##buscar-juego", "Buscar por nombre o máquina", ref _gameSearch, 150);
        var matches = _games.Where(g => _labels.GetValueOrDefault(g.Profile.Id, g.Profile.Name).Contains(_gameSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        ImGui.BeginChild("elegir-juegos", new Vector2(0, Math.Clamp(ImGui.GetWindowHeight() * 0.28f, 110, 200)), ImGuiChildFlags.Borders);
        if (matches.Count == 0) ImGui.TextWrapped("No encontré juegos con ese nombre. Probá otra búsqueda.");
        foreach (var g in matches)
        {
            int slot = _chosen.IndexOf(g.Profile.Id);
            bool on = slot >= 0;
            ImGui.BeginDisabled(Busy || !on && _chosen.Count >= 3);
            if (ImGui.Checkbox($"{_labels.GetValueOrDefault(g.Profile.Id, g.Profile.Name)}##{g.Profile.Id}", ref on))
            {
                if (on && _chosen.Count < 3) _chosen.Add(g.Profile.Id);
                else _chosen.Remove(g.Profile.Id);
                _concepts = null;
                _problem = null;
            }
            ImGui.EndDisabled();
            if (_unprepared.TryGetValue(g.Profile.Id, out var file))
            {
                ImGui.SameLine();
                ImGui.BeginDisabled(Busy);
                if (ImGui.SmallButton($"Preparar##{g.Profile.Id}"))
                    StartPrepare(g.Profile.Name, file);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Busca el arranque y verifica variables como vidas, puntaje y posición. Puede tardar unos minutos y usar la IA elegida.");
            }
        }
        ImGui.EndChild();
        if (_chosen.Count > 0)
            ImGui.TextWrapped("Elegidos: " + string.Join(" + ", _chosen.Select(id => _games.FirstOrDefault(g => g.Profile.Id == id)?.Profile.Name ?? id)));
        DrawPreparation();
        if (_chosen.Count < 2)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Marcá 2 o 3 juegos.");
            return;
        }

        var existing = StudioLibrary.For(_chosen).ToList();
        if (existing.Count > 0 && ImGui.TreeNode("Fusiones anteriores de esta combinación"))
        {
            foreach (var game in existing)
                DrawGameCard(game);
            ImGui.TreePop();
        }

        ImGui.Separator();
        ImGui.TextColored(Gold, "2. Inventá la fusión");
        DrawAiChooser("Cada idea o versión es un pedido a la IA (unos centavos; escribir un juego usa más que proponer ideas).");
        if (ImGui.BeginTabBar("forma-de-idea"))
        {
            if (ImGui.BeginTabItem("Proponer ideas"))
            {
                ImGui.TextUnformatted("Enfoque o pedido (opcional):");
                ImGui.InputTextMultiline("##deseo", ref _wish, 1000, new Vector2(-1, 44));
                AiPicker.Hint("Ej.: \"una carrera cooperativa\", \"un puzle sin disparos\", \"que las rutas cambien al robar\".");
                ImGui.BeginDisabled(Busy);
                if (ImGui.Button("Proponer 3 ideas")) AskConcepts();
                ImGui.EndDisabled();
                if (_concepts != null)
                {
                    ImGui.Separator();
                    ImGui.TextUnformatted("Ideas de la IA (elegí una):");
                    foreach (var c in _concepts)
                    {
                        ImGui.PushID(c.Title);
                        ImGui.TextColored(Gold, c.Title);
                        ImGui.SameLine();
                        ImGui.TextDisabled(c.Approach);
                        ImGui.TextWrapped(c.Pitch);
                        AiPicker.Hint("Cómo se juega: " + c.HowToPlay);
                        if (c.Design.Length > 0 && ImGui.TreeNode("Diseño##" + c.Title))
                        {
                            ImGui.TextWrapped(c.Design);
                            ImGui.TreePop();
                        }
                        ImGui.BeginDisabled(Busy);
                        if (ImGui.Button("Hacer este juego")) Write(c);
                        ImGui.EndDisabled();
                        ImGui.Spacing();
                        ImGui.PopID();
                    }
                }
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Mi idea"))
            {
                ImGui.TextUnformatted("Contá cómo querés unir los juegos:");
                ImGui.InputTextMultiline("##idea", ref _ownIdea, 2000, new Vector2(-1, 60));
                ImGui.BeginDisabled(Busy || _ownIdea.Trim().Length < 10);
                if (ImGui.Button("Hacer mi idea")) Write(new GameConcept("", "", _ownIdea.Trim(), ""));
                ImGui.EndDisabled();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        DrawProgress();
    }

    private void DrawGameCard(StudioGame game)
    {
        ImGui.PushID(game.Id);
        ImGui.TextColored(Gold, game.Title);
        ImGui.SameLine();
        ImGui.TextDisabled($"v{game.Version} - {game.Author}");
        ImGui.TextWrapped(game.Pitch);
        ImGui.BeginDisabled(Busy);
        if (ImGui.Button("Jugar"))
            StartGame(game);
        ImGui.EndDisabled();
        ImGui.Spacing();
        ImGui.PopID();
    }

    private void DrawAiChooser(string costNote)
    {
        AiPicker.Combo(_ai);
        if (AiPicker.IsApi(_ai.Provider))
            AiPicker.Settings(_ai, costNote);
        else if (_ai.Provider == "manual")
            AiPicker.Hint("Te doy la consigna para pegar en el chat de tu IA y vos pegás acá la respuesta. El emulador prueba el juego y, si falla, te da el texto para devolvérselo.");
        else if (_ai.Provider == "agent")
            AiPicker.Hint("El emulador arma una carpeta con la tarea, la API y los juegos. Tu agente escribe juego.js; el emulador lo prueba solo cada vez que lo guarda y le deja informe.md.");
        else
            AiPicker.Hint("La IA escribe el juego y el emulador lo prueba. Durante la creación podés abrir Construcción para ver su actividad y cancelar.");
    }

    private void DrawPlaying(StudioSession s)
    {
        var g = s.Game;
        ImGui.TextColored(Gold, g.Title);
        ImGui.SameLine();
        ImGui.TextDisabled($"versión {g.Version} - {g.Author}");
        if (g.Approach.Length > 0)
            AiPicker.Hint(g.Approach);
        ImGui.TextWrapped(g.Pitch);
        ImGui.TextColored(Blue, "Cómo se juega:");
        ImGui.SameLine();
        ImGui.TextWrapped(g.HowToPlay);
        if (g.Design.Length > 0 && ImGui.TreeNode("Diseño de la fusión"))
        {
            ImGui.TextWrapped(g.Design);
            ImGui.TreePop();
        }
        if (_lastTest != null && ImGui.TreeNode("Prueba automática de jugabilidad"))
        {
            ImGui.TextWrapped(_lastTest);
            ImGui.TreePop();
        }
        if (g.Changes is { Length: > 0 } changes)
            AiPicker.Hint("Cambios de esta versión: " + changes);
        if (s.Hud.Length > 0)
            ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), s.Hud);
        if (s.Error != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Red);
            ImGui.TextWrapped("El guion falló: " + s.Error);
            ImGui.PopStyleColor();
            ImGui.BeginDisabled(Busy);
            if (ImGui.Button("Pedir a la IA que lo arregle"))
                Revise(s.Game, "Al jugar apareció este error y el juego se detuvo: " + s.Error + ". Arreglalo.");
            ImGui.EndDisabled();
            ImGui.SameLine();
        }
        if (ImGui.Button("Reiniciar"))
            s.Restart();
        ImGui.SameLine();
        if (ImGui.Button("Detener"))
            _host.StopFusion();
        var versions = StudioLibrary.Versions(g.Id);
        if (versions.Count > 1)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            if (ImGui.BeginCombo("##versiones", $"Versión {g.Version} de {versions.Count}"))
            {
                foreach (var v in versions)
                    if (ImGui.Selectable($"v{v.Version}: {(v.Request ?? "original")}##v{v.Version}", v.Version == g.Version) && v.Version != g.Version)
                        StartGame(v);
                ImGui.EndCombo();
            }
        }
        if (s.Slots.Count > 1)
        {
            ImGui.TextDisabled("Depurador y código de:");
            foreach (var slot in s.Slots)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton(slot.Profile.Name.Split(' ')[0] + "##" + slot.Id, _host.FusionFocus == slot.Id))
                    _host.FusionFocus = slot.Id;
            }
        }

        ImGui.Separator();
        ImGui.TextColored(Gold, "Pedile un cambio a la IA");
        AiPicker.Hint("Una corrección (\"los invasores son muy rápidos\", \"que el láser atraviese\") o una visión totalmente distinta (\"que ahora sea cooperativo\", \"que Willy sea el que invade\"). Cada pedido es una versión nueva; las anteriores quedan guardadas.");
        ImGui.InputTextMultiline("##pedido", ref _request, 2000, new Vector2(-1, 56));
        DrawAiChooser("Cada versión es un pedido a la IA (unos centavos).");
        ImGui.BeginDisabled(Busy || _request.Trim().Length < 3);
        if (ImGui.Button("Hacer versión nueva"))
            Revise(g, _request.Trim());
        ImGui.EndDisabled();
        DrawProgress();

        ImGui.Separator();
        if (ImGui.CollapsingHeader("Ver y editar el guion (JavaScript)"))
        {
            string key = g.Id + "#" + g.Version;
            if (_codeFor != key)
            {
                _code = g.Script;
                _codeFor = key;
            }
            ImGui.InputTextMultiline("##guion", ref _code, 200_000, new Vector2(-1, 260), ImGuiInputTextFlags.AllowTabInput);
            ImGui.BeginDisabled(Busy || _code == g.Script);
            if (ImGui.Button("Probar mis cambios"))
            {
                var mine = g.Copy();
                mine.Script = _code;
                mine.Version = g.Version + 1;
                mine.Request = "Cambios a mano en el guion";
                mine.Changes = "Editado a mano.";
                mine.Author = "vos";
                mine.Created = DateTime.Now;
                ValidateThen(mine, (game, report) =>
                {
                    if (report.Ok) SaveAndStart(game);
                    else Problem("Tus cambios no funcionan todavía.", report);
                });
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Copiar guion"))
                ImGui.SetClipboardText(_code);
            if (s.Log.Count > 0)
            {
                ImGui.TextDisabled("log():");
                foreach (var line in s.Log.TakeLast(6))
                    ImGui.TextDisabled("  " + line);
            }
        }
    }

    private void DrawProgress()
    {
        if (_progress.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Blue);
            ImGui.TextWrapped(_progress);
            ImGui.PopStyleColor();
        }
        if (_cts != null && (_conceptTask != null || _designTask != null))
        {
            if (ImGui.SmallButton("Cancelar"))
                _cts.Cancel();
        }
        _build.Draw(_conceptTask != null || _designTask != null, _progress, _cts);
        if (_problem != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Red);
            ImGui.TextWrapped(_problem);
            ImGui.PopStyleColor();
            if (_problemDetail != null && ImGui.TreeNode("Informe de la prueba"))
            {
                ImGui.TextWrapped(_problemDetail);
                ImGui.TreePop();
            }
        }
        if (_manual is { } m)
        {
            ImGui.Separator();
            ImGui.TextColored(Gold, m.Title);
            if (ImGui.Button("1. Copiar la consigna"))
            {
                ImGui.SetClipboardText(m.Prompt);
                _progress = "Consigna copiada: pegala en el chat de tu IA y copiá toda su respuesta.";
            }
            ImGui.TextUnformatted("2. Pegá acá la respuesta:");
            ImGui.InputTextMultiline("##respuesta", ref _pasted, 300_000, new Vector2(-1, 90));
            ImGui.BeginDisabled(Busy || _pasted.Trim().Length == 0);
            if (ImGui.Button("3. Usar la respuesta"))
            {
                string answer = _pasted;
                _pasted = "";
                try { m.OnAnswer(answer); }
                catch (AiException e) { _problem = "La respuesta no sirve: " + e.Message; }
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancelar##manual"))
                _manual = null;
        }
        if (_agentDir != null)
        {
            ImGui.Separator();
            ImGui.TextColored(Gold, "Carpeta para tu agente");
            ImGui.TextWrapped(_agentDir);
            if (ImGui.Button("Abrir carpeta"))
                Process.Start(new ProcessStartInfo { FileName = _agentDir, UseShellExecute = true });
            ImGui.SameLine();
            if (ImGui.Button("Copiar ruta"))
                ImGui.SetClipboardText(_agentDir);
            ImGui.SameLine();
            if (ImGui.Button("Dejar de esperar"))
                _agentDir = null;
            AiPicker.Hint("Pedile a tu agente: \"leé TAREA.md y hacelo\". Cada vez que guarde juego.js lo pruebo solo; cuando funcione arranca acá.");
        }
    }

    private void DrawToasts()
    {
        if (_host.Fusion is not StudioSession s)
        {
            _seenEvents = 0;
            _toasts.Clear();
            return;
        }
        if (_seenEvents > s.Events.Count)
            _seenEvents = 0;
        for (; _seenEvents < s.Events.Count; _seenEvents++)
            _toasts.Add(s.Events[_seenEvents]);
        _toasts.RemoveAll(t => s.Tick - t.Tick > 180 || t.Tick > s.Tick);
        if (_toasts.Count == 0)
            return;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + vp.Size.X / 2, vp.Pos.Y + 40), ImGuiCond.Always, new Vector2(0.5f, 0));
        ImGui.SetNextWindowBgAlpha(0.75f);
        ImGui.Begin("##studiotoasts", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs |
                                      ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoDocking);
        foreach (var t in _toasts.TakeLast(3))
            ImGui.TextColored(new Vector4(1, 0.9f, 0.4f, 1), t.Text);
        ImGui.End();
    }

    // ------------------------------------------------------------------ actions
    private void Problem(string text, ValidationReport? report = null)
    {
        _problem = text;
        _problemDetail = report?.Summary;
        _progress = "";
    }

    private void ClearProblem()
    {
        _problem = null;
        _problemDetail = null;
    }

    private void AskConcepts()
    {
        ClearProblem();
        _concepts = null;
        var slots = Slots();
        string wish = _wish;
        if (_ai.Provider == "manual" || _ai.Provider == "agent")
        {
            // agents work on whole games; ideas go through copy and paste
            _manual = new ManualStep("Ideas: pegá la consigna en tu chat de IA",
                StudioDesigner.ManualConceptPrompt(slots, ProfileLibrary.Find, wish),
                answer =>
                {
                    _concepts = StudioDesigner.ParseConcepts(answer);
                    _manual = null;
                    _progress = "";
                });
            return;
        }
        var ai = _ai.CreateProvider()!;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _progress = $"{ai.Name} está pensando ideas...";
        _build.Start(ai);
        _conceptTask = Task.Run(() => new StudioDesigner().ConceptsAsync(ai, slots, ProfileLibrary.Find, wish, token));
    }

    private void Write(GameConcept concept)
    {
        ClearProblem();
        var slots = Slots();
        var template = new StudioGame
        {
            Id = string.IsNullOrWhiteSpace(concept.Title) ? "" : StudioLibrary.Slug(concept.Title),
            Title = concept.Title,
            Approach = concept.Approach,
            Pitch = concept.Pitch,
            HowToPlay = concept.HowToPlay,
            Games = slots,
            Version = 1,
            Author = AiPicker.ShortName(_ai.Provider),
        };
        switch (_ai.Provider)
        {
            case "manual":
                StartManual("Escribir el juego: pegá la consigna en tu chat de IA",
                    StudioDesigner.ManualWritePrompt(slots, ProfileLibrary.Find, concept), template, first: true);
                break;
            case "agent":
                StartAgent(template, $"Escribí este juego fusionado desde cero.\n\nIdea: {concept.Title} ({concept.Approach})\n{concept.Pitch}\nCómo se juega: {concept.HowToPlay}", first: true);
                break;
            default:
            {
                var ai = _ai.CreateProvider()!;
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                var sources = Sources();
                _progress = $"{ai.Name} está escribiendo el juego...";
                _build.Start(ai);
                _designTask = Task.Run(() => new StudioDesigner().WriteAsync(ai, slots, ProfileLibrary.Find, concept,
                    game => StudioValidator.Run(game, sources, _romDir), token, p => _progress = p));
                break;
            }
        }
    }

    private void Revise(StudioGame current, string request)
    {
        ClearProblem();
        var template = current.Copy();
        template.Version = current.Version + 1;
        template.Request = request;
        template.Author = AiPicker.ShortName(_ai.Provider);
        template.Created = DateTime.Now;
        switch (_ai.Provider)
        {
            case "manual":
                StartManual("Versión nueva: pegá la consigna en tu chat de IA",
                    StudioDesigner.ManualRevisePrompt(current, ProfileLibrary.Find, request), template, first: false);
                break;
            case "agent":
                StartAgent(template, $"El jugador probó la versión {current.Version} (está en juego.js) y pide:\n\n\"{request}\"\n\nHacé la versión siguiente.", first: false);
                break;
            default:
            {
                var ai = _ai.CreateProvider()!;
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                var sources = Sources();
                _progress = $"{ai.Name} está preparando la corrección...";
                _build.Start(ai);
                _designTask = Task.Run(() => new StudioDesigner().ReviseAsync(ai, current, ProfileLibrary.Find, request,
                    game => StudioValidator.Run(game, sources, _romDir), token, p => _progress = p));
                break;
            }
        }
        _request = "";
    }

    private void StartManual(string title, string prompt, StudioGame template, bool first)
    {
        void OnAnswer(string answer)
        {
            var game = StudioDesigner.ParseGame(answer, template, out var error)
                       ?? throw new AiException(error ?? "no se pudo leer");
            _manual = null;
            ValidateThen(game, (g, report) =>
            {
                if (report.Ok)
                    SaveAndStart(g, first);
                else
                {
                    Problem("El juego todavía falla: copiá la consigna de corrección y pegásela a tu IA.", report);
                    _manual = new ManualStep("Corregir: pegale esto a tu IA", StudioDesigner.ManualFixPrompt(report), OnAnswer);
                }
            });
        }
        _manual = new ManualStep(title, prompt, OnAnswer);
        _progress = "";
    }

    private void StartAgent(StudioGame template, string task, bool first)
    {
        if (first)
            template.Id = StudioLibrary.UniqueId(string.IsNullOrWhiteSpace(template.Id) ? "juego-nuevo" : template.Id);
        string? cli = FindCli();
        string? hint = cli != null ? $"\"{cli}\" studio juego.js --games \"{_folder}\" --shots prueba" : null;
        _agentDir = StudioWorkspace.Create(template, task, ProfileLibrary.Find, hint);
        _agentTemplate = template;
        _agentSeen = File.GetLastWriteTimeUtc(Path.Combine(_agentDir, StudioWorkspace.ScriptFile));
        _progress = "Esperando que tu agente escriba juego.js...";
    }

    private static string? FindCli()
    {
        foreach (var name in new[] { "retro-cli.exe", "retro-cli" })
        {
            string p = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    private void ValidateThen(StudioGame game, Action<StudioGame, ValidationReport> then)
    {
        var sources = Sources();
        _progress = "Probando el juego con un jugador robot...";
        _afterValidate = then;
        _validateTask = Task.Run(() => (game, StudioValidator.Run(game, sources, _romDir)));
    }

    private void SaveAndStart(StudioGame game, bool first = false)
    {
        if (first || game.Version <= 1)
            game.Id = StudioLibrary.UniqueId(string.IsNullOrWhiteSpace(game.Id) ? StudioLibrary.Slug(game.Title) : game.Id);
        StudioLibrary.Save(game);
        StartGame(game);
    }

    private void StartGame(StudioGame game)
    {
        var sources = Sources();
        var missing = game.Games.Where(g => !sources.ContainsKey(g.Profile)).Select(g => g.Profile).ToList();
        if (missing.Count > 0)
        {
            Problem("Faltan juegos en la carpeta: " + string.Join(", ", missing));
            return;
        }
        _starting = game;
        _progress = $"Arrancando \"{game.Title}\" (la primera vez carga la cinta del Spectrum, unos segundos)...";
        _startTask = Task.Run(() => StudioSession.Start(game, sources, _romDir));
    }

    // ------------------------------------------------------------------ background work
    private void Poll()
    {
        if (_prepareTask is { IsCompleted: true } pt)
        {
            _prepareTask = null;
            if (pt.IsFaulted)
            {
                var e = pt.Exception!.GetBaseException();
                _status = e is OperationCanceledException ? "Preparación cancelada." : $"No se pudo preparar {_preparing}: {e.Message}";
            }
            else if (pt.IsCanceled)
                _status = "Preparación cancelada.";
            else if (pt.Result is { Ok: true } r)
            {
                Scan();
                _status = $"{_preparing} quedó preparado: {r.Profile!.Variables.Count(v => !v.Value.ReadOnly)} variables verificadas " +
                          $"({string.Join(", ", r.Profile.Variables.Keys)}). Ya aparece en la lista.";
            }
            else
                _status = $"No se pudo preparar {_preparing}: {pt.Result.Problem}";
            Console.WriteLine("preparar: " + _status);
        }
        if (_conceptTask is { IsCompleted: true } ct)
        {
            _build.Stop();
            _conceptTask = null;
            if (ct.IsCompletedSuccessfully)
            {
                _concepts = ct.Result;
                _progress = "";
            }
            else
                Problem(Failure(ct.Exception));
        }
        if (_designTask is { IsCompleted: true } dt)
        {
            _build.Stop();
            _designTask = null;
            if (!dt.IsCompletedSuccessfully)
                Problem(Failure(dt.Exception));
            else if (dt.Result.Game is { } game)
            {
                _progress = $"Listo en {dt.Result.Attempts} intento{(dt.Result.Attempts > 1 ? "s" : "")}.";
                _lastTest = dt.Result.Report?.Summary;
                SaveAndStart(game, first: game.Version <= 1);
            }
            else
                Problem($"La IA no logró un juego que funcione en {StudioDesigner.MaxAttempts} intentos: {dt.Result.Error}", dt.Result.Report);
        }
        if (_validateTask is { IsCompleted: true } vt)
        {
            _validateTask = null;
            var then = _afterValidate;
            _afterValidate = null;
            if (vt.IsCompletedSuccessfully)
            {
                _progress = "";
                if (vt.Result.report.Ok)
                    _lastTest = vt.Result.report.Summary;
                then?.Invoke(vt.Result.game, vt.Result.report);
            }
            else
                Problem(Failure(vt.Exception));
        }
        if (_startTask is { IsCompleted: true } st)
        {
            _startTask = null;
            if (st.IsCompletedSuccessfully)
            {
                _host.StartFusion(st.Result);
                _progress = "";
                _status = "";
            }
            else
                Problem("No se pudo arrancar: " + Failure(st.Exception));
        }
        PollAgent();
    }

    private void PollAgent()
    {
        if (_agentDir == null || _agentTemplate == null || Busy)
            return;
        _agentPoll += ImGui.GetIO().DeltaTime;
        if (_agentPoll < 1)
            return;
        _agentPoll = 0;
        if (StudioWorkspace.Read(_agentDir, _agentTemplate) is not { } read || read.written <= _agentSeen || read.game.Script.Trim().Length == 0)
            return;
        _agentSeen = read.written;
        string dir = _agentDir;
        ValidateThen(read.game, (game, report) =>
        {
            StudioWorkspace.WriteReport(dir, report);
            if (report.Ok)
            {
                _agentDir = null;
                SaveAndStart(game, first: game.Version <= 1);
            }
            else
                Problem("El agente tiene que corregir el juego (le dejé informe.md).", report);
        });
    }

    private static string Failure(AggregateException? e)
    {
        var inner = e?.GetBaseException();
        return inner is OperationCanceledException ? "Cancelado." : inner?.Message ?? "Error desconocido.";
    }
}
