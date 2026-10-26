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

    private sealed record ManualStep(string Title, string Prompt, Action<string> OnAnswer, string? ReferenceFolder = null);

    private readonly EmulatorHost _host;
    private readonly string _romDir;
    private readonly AiSettings _ai;
    private bool _open;
    private bool _libraryChanged;
    private string _folder;
    private readonly List<GameSource> _games = new();
    private IReadOnlyList<StudioGame> _savedGames = [];
    private readonly Dictionary<string, string> _unprepared = new();
    private readonly Dictionary<string, string> _labels = new();
    private Task<Retro.Compat.PrepareResult>? _prepareTask;
    private CancellationTokenSource? _prepareCts;
    private string _preparing = "";
    private volatile string _prepareProgress = "";
    private readonly List<string> _chosen = new();   // order = slots a, b, c
    private string _gameSearch = "";
    private string? _baseProfile;
    private bool _inspiredMode;
    private string _status = "";

    private string _wish = "";
    private IReadOnlyList<StudioWork> _pendingWork = Array.Empty<StudioWork>();
    private string? _activeWork;
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
    private Task<OriginalReferences>? _referenceTask;
    private Action<OriginalReferences>? _afterReferences;
    private Task<(StudioGame game, ValidationReport report)>? _validateTask;
    private Action<StudioGame, ValidationReport>? _afterValidate;
    private Task<StudioSession>? _startTask;
    private StudioGame? _starting;
    private ManualStep? _manual;
    private bool _scrollToManual;
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

    public void Open(bool inspired = false)
    {
        _open = true;
        if (inspired != _inspiredMode)
        {
            if (Busy || _manual != null || _agentDir != null)
            { Problem("Hay un trabajo en curso. Terminá o cancelalo para cambiar de modalidad."); return; }
            _inspiredMode = inspired;
            _concepts = null; _gameSearch = "";
        }
        RefreshWork();
        RefreshSavedGames();
        if (_games.Count == 0 || _libraryChanged)
            Scan();
    }

    public void NotifyLibraryChanged(string folder)
    {
        if (Path.GetFullPath(folder).Equals(Path.GetFullPath(_folder), StringComparison.OrdinalIgnoreCase)) _libraryChanged = true;
    }

    /// <summary>Raised on the UI thread after a stored game starts successfully.</summary>
    public event Action<StudioGame>? GameStarted;

    /// <summary>Command line --studio: plays a stored fusion game with the games in the folder.</summary>
    public void PlayById(string id)
    {
        if (Busy || _manual != null || _agentDir != null) { _open = true; Problem("Terminá o cancelá el trabajo en curso antes de abrir otro juego."); return; }
        var game = StudioLibrary.AllVersions().Where(g => g.Id == id).OrderByDescending(g => g.Version).FirstOrDefault();
        Open(game?.IsInspired ?? false);
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
        _libraryChanged = false;
        _games.Clear();
        _unprepared.Clear();
        _labels.Clear();
        if (!Directory.Exists(_folder))
        {
            _status = "La carpeta no existe.";
            return;
        }
        var catalog = Retro.Compat.GameCatalog.Scan(_folder);
        var twins = catalog.GroupBy(g => g.Profile.Name).ToDictionary(group => group.Key, group => group.Count());
        foreach (var g in catalog)
        {
            _games.Add(g.Source);
            if (!g.Prepared)
                _unprepared[g.Profile.Id] = g.Path;
            bool twin = twins[g.Profile.Name] > 1;
            _labels[g.Profile.Id] = twin ? $"{g.Profile.Name} · {Path.GetFileName(g.Path)}" : g.Profile.Name;
        }
        _status = $"{_games.Count} juegos encontrados.";
    }

    private void RefreshSavedGames()
    {
        try { _savedGames = StudioLibrary.AllVersions(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _status = "No se pudieron actualizar los proyectos: " + error.Message; }
    }

    private IReadOnlyDictionary<string, GameSource> Sources() => _games.ToDictionary(g => g.Profile.Id);

    private List<RecipeGame> Slots() =>
        _chosen.Select((id, i) => new RecipeGame { Slot = SlotNames[i], Profile = id }).ToList();

    public bool IsWorking => Busy || _manual != null || _agentDir != null;
    private bool _focusWork;
    public void ShowWork() { _open = true; _focusWork = true; }

    private bool Busy => _prepareTask != null || _conceptTask != null || _designTask != null || _referenceTask != null || _validateTask != null || _startTask != null;
    public Task CancelWork()
    {
        _cts?.Cancel(); _prepareCts?.Cancel();
        return Task.WhenAll(new Task?[] { _prepareTask, _conceptTask, _designTask, _referenceTask, _validateTask, _startTask }.OfType<Task>());
    }

    // ------------------------------------------------------------------ drawing
    private float? _panelLeft;

    /// <summary>Screen width to leave free on the right for this panel (0 when closed, collapsed or not on the right half).</summary>
    public float RightReserve(float screenWidth) =>
        _open && _panelLeft is float left && left >= 200 ? screenWidth - left : 0;

    public void Close() { _open = false; _panelLeft = null; }
    public void Update() { if (!_open) Poll(); }

    public void Draw()
    {
        Poll();
        DrawToasts();
        if (!_open)
        {
            _panelLeft = null;
            return;
        }
        if (_libraryChanged && !Busy) Scan();
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + vp.Size.X - 540, vp.Pos.Y + 30), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(530, Math.Min(760, vp.Size.Y - 70)), ImGuiCond.FirstUseEver);
        bool visible = ImGui.Begin(_inspiredMode ? "Crear con originales" : "Fusionar juegos", ref _open);
        WorkspaceLayout.KeepOnScreen();
        if (!visible)
        {
            _panelLeft = null;
            ImGui.End();
            return;
        }
        _panelLeft = ImGui.GetWindowPos().X - vp.Pos.X;
        if (_focusWork) { ImGui.SetScrollY(0); _focusWork = false; }
        DrawPendingWork();
        DrawProgress();
        if (_prepareTask != null) DrawPreparation();
        if (_host.Fusion is StudioSession session && session.Game.IsInspired == _inspiredMode)
            DrawPlaying(session);
        else
            DrawSetup();
        ImGui.End();
    }

    /// <summary>Progress of "Preparar" (the buttons are next to the games without verified variables).</summary>
    private void DrawPreparation()
    {
        if (_unprepared.Count > 0 && _prepareTask == null)
            AiPicker.Hint("Preparar identifica datos del original para usarlos en el juego.");
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
        _prepareProgress = advisor != null ? $"Buscando el arranque con ayuda de {advisor.Name}."
            : "Buscando el arranque sin IA. Copiar y pegar no admite esta tarea.";
        _prepareCts = new CancellationTokenSource();
        var ct = _prepareCts.Token;
        _prepareTask = Task.Run(() => Retro.Compat.GamePreparer.PrepareAsync(path, options, line => _prepareProgress = line, ct));
    }

    private void DrawSetup()
    {
        ImGui.TextWrapped(_inspiredMode ? "Creá un juego nuevo a partir de personajes, recursos o reglas de tus ROMs."
            : "Elegí un juego base e incorporá personajes o mecánicas de otros originales.");
        if (ImGui.TreeNode("Carpetas de juegos"))
        {
            ImGui.SetNextItemWidth(-90);
            ImGui.InputText("##carpeta", ref _folder, 1024);
            ImGui.SameLine();
            if (ImGui.Button("Buscar")) Scan();
            string savedFolder = _inspiredMode ? CreatedGames.UserFolder : StudioLibrary.UserFolder;
            if (ImGui.SmallButton(_inspiredMode ? "Abrir mis juegos creados" : "Abrir mis juegos fusionados"))
            {
                Directory.CreateDirectory(savedFolder);
                Process.Start(new ProcessStartInfo { FileName = savedFolder, UseShellExecute = true });
            }
            ImGui.TextWrapped(savedFolder);
            ImGui.TreePop();
        }
        AiPicker.Hint(_status);
        DrawAiChooser("La preparación y creación pueden usar varios pedidos a la IA elegida.");
        ImGui.Separator();
        ImGui.TextColored(Gold, $"1. Elegí los juegos ({_chosen.Count}/3)");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##buscar-juego", "Buscar por nombre o máquina", ref _gameSearch, 150);
        var matches = _games.Where(g => _labels.GetValueOrDefault(g.Profile.Id, g.Profile.Name).Contains(_gameSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        ImGui.BeginChild("elegir-juegos", new Vector2(0, Math.Min(Math.Clamp(ImGui.GetWindowHeight() * 0.28f, 110, 200), Math.Max(74, matches.Count * 30 + 12))), ImGuiChildFlags.Borders);
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
                _manual = null;
                _agentDir = null;
                _agentTemplate = null;
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
                    ImGui.SetTooltip("Analiza el arranque y los datos del juego. Puede usar la IA elegida y tardar varios minutos.");
            }
        }
        ImGui.EndChild();
        if (_chosen.Count > 0)
        {
            ImGui.TextWrapped("Elegidos: " + string.Join(" + ", _chosen.Select(id => _games.FirstOrDefault(g => g.Profile.Id == id)?.Profile.Name ?? id)));
            ImGui.BeginDisabled(Busy);
            if (ImGui.SmallButton("Limpiar selección")) { _chosen.Clear(); _concepts = null; }
            ImGui.EndDisabled();
        }
        if (_prepareTask == null) DrawPreparation();
        if (_chosen.Count < 2)
        {
            ImGui.Spacing();
            return;
        }

        var wanted = _chosen.OrderBy(id => id).ToArray();
        var existing = _savedGames.Where(g => g.IsInspired == _inspiredMode && g.ProfileIds.OrderBy(id => id).SequenceEqual(wanted))
            .GroupBy(g => g.Id).Select(group => group.OrderByDescending(g => g.Version).First()).ToList();
        if (existing.Count > 0 && ImGui.TreeNode(_inspiredMode ? "Creaciones anteriores con estos originales" : "Fusiones anteriores de esta combinación"))
        {
            foreach (var game in existing)
                DrawGameCard(game);
            ImGui.TreePop();
        }

        ImGui.Separator();
        ImGui.TextColored(Gold, _inspiredMode ? "2. Definí el juego nuevo" : "2. Elegí el juego base");
        DrawDirection();
        ImGui.Spacing();
        if (!_inspiredMode) ImGui.TextColored(Gold, "3. Definí los aportes");
        ImGui.TextUnformatted(_inspiredMode ? "Tu propuesta para el juego (opcional):" : "Qué querés incorporar a la base (opcional):");
        ImGui.InputTextMultiline("##deseo", ref _wish, 2000, new Vector2(-1, 64));
        ImGui.BeginDisabled(Busy);
        if (ImGui.Button("Proponer 3 ideas")) AskConcepts();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(Busy || _wish.Trim().Length < 10);
        if (ImGui.Button(_inspiredMode ? "Crear con mi propuesta" : "Fusionar con mi propuesta"))
            Write(new GameConcept("", "", _wish.Trim(), "", PlayerRequest: _wish.Trim()));
        ImGui.EndDisabled();
        if (_concepts != null)
        {
            ImGui.Separator();
            ImGui.TextColored(Gold, "Elegí una propuesta");
            foreach (var concept in _concepts)
            {
                ImGui.PushID(concept.Title);
                ImGui.TextColored(Gold, concept.Title);
                ImGui.TextWrapped(concept.Pitch);
                AiPicker.Hint(concept.HowToPlay);
                if (concept.Design.Length > 0 && ImGui.TreeNode("Detalles del diseño"))
                { ImGui.TextWrapped(concept.Design); ImGui.TreePop(); }
                if (!string.IsNullOrWhiteSpace(concept.MissingData))
                    ImGui.TextWrapped(StudioDesigner.MissingDataPrefix + concept.MissingData);
                ImGui.BeginDisabled(Busy || !string.IsNullOrWhiteSpace(concept.MissingData));
                if (ImGui.Button(_inspiredMode ? "Crear este juego" : "Crear esta fusión")) Write(concept);
                ImGui.EndDisabled();
                ImGui.Spacing(); ImGui.PopID();
            }
        }
    }

    private FusionDirection SelectedDirection()
    {
        int selected = Math.Max(0, _chosen.IndexOf(_baseProfile ?? ""));
        return new FusionDirection(_chosen.Count > 0 ? SlotNames[selected] : null, _inspiredMode);
    }

    private void DrawDirection()
    {
        ImGui.BeginDisabled(Busy);
        if (!_inspiredMode)
        {
            int selected = Math.Max(0, _chosen.IndexOf(_baseProfile ?? ""));
            var labels = _chosen.Select(id => _labels.GetValueOrDefault(id, id)).ToArray();
            ImGui.SetNextItemWidth(-100);
            if (ImGui.Combo("Juego base", ref selected, labels, labels.Length))
            {
                _baseProfile = _chosen[selected];
                Change();
            }
            AiPicker.Hint("Conserva sus mapas, aspecto y reglas. Los demás aportan personajes o mecánicas dentro de ese mundo.");
        }
        else AiPicker.Hint("Tiene escena, presentación y progresión propias. No necesita conservar un juego base.");
        ImGui.EndDisabled();

        void Change()
        {
            _concepts = null;
            _manual = null;
            _agentDir = null;
            _agentTemplate = null;
        }
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
        bool canPreview = _chosen.Count >= 2 || _host.Fusion is StudioSession active && active.Game.IsInspired == _inspiredMode;
        AiPicker.Choose(_ai, "estudio", costNote, canPreview ? () => _host.Fusion is StudioSession current && current.Game.IsInspired == _inspiredMode
            ? new(StudioDesigner.Guide, [new(true, StudioDesigner.RevisePrompt(current.Game, current.Game.Games, ProfileLibrary.Find, _request))], StudioDesigner.GameSchema())
            : new(StudioDesigner.Guide, [new(true, StudioDesigner.WritePrompt(Slots(), ProfileLibrary.Find,
                new("", "", _wish, "", Direction: SelectedDirection())))], StudioDesigner.GameSchema()) : null);
        if (_ai.Provider == "manual") AiPicker.Hint("Copiá el pedido a tu chat y pegá la respuesta.");
        else if (_ai.Provider == "agent") AiPicker.Hint("Prepará la carpeta y pasale la tarea a tu agente.");
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
        if (g.Usage is { } usage && ImGui.TreeNode("Uso de IA de esta versión"))
        { AiBuildWindow.UsageDetails(usage); ImGui.TreePop(); }
        if (g.Design.Length > 0 && ImGui.TreeNode(g.IsInspired ? "Diseño del juego" : "Diseño de la fusión"))
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
        var versions = _savedGames.Where(game => game.Id == g.Id).OrderBy(game => game.Version).ToList();
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
        if (ImGui.SmallButton("Actualizar versiones")) RefreshSavedGames();
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
        AiPicker.Hint("Describí el cambio. Se guardará como una versión nueva.");
        ImGui.InputTextMultiline("##pedido", ref _request, 2000, new Vector2(-1, 56));
        DrawAiChooser("Cada versión puede necesitar varios pedidos a la IA.");
        ImGui.BeginDisabled(Busy || _request.Trim().Length < 3);
        if (ImGui.Button("Hacer versión nueva"))
            Revise(g, _request.Trim());
        ImGui.EndDisabled();

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
                    if (report.Ok) SaveAndStart(game, picture: report.Picture);
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
        if (_cts != null && (_conceptTask != null || _designTask != null || _referenceTask != null))
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
            if (_scrollToManual) { ImGui.SetScrollHereY(0.15f); _scrollToManual = false; }
            if (m.ReferenceFolder != null)
            {
                if (ImGui.SmallButton("Ver capturas originales"))
                    Process.Start(new ProcessStartInfo { FileName = m.ReferenceFolder, UseShellExecute = true });
                AiPicker.Hint("Adjuntalas a tu chat si admite imágenes.");
            }
            if (ImGui.Button("1. Copiar la consigna"))
            {
                ImGui.SetClipboardText(m.Prompt);
                _progress = "Pedido copiado. Pegalo en tu chat y traé la respuesta completa.";
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
            if (ImGui.SmallButton("Copiar instrucción para el agente"))
                ImGui.SetClipboardText($"Trabajá en la carpeta {_agentDir}: leé TAREA.md y seguí sus instrucciones.");
            AiPicker.Hint("El juego se prueba cuando el agente guarda sus cambios.");
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
        var direction = SelectedDirection();
        if (_ai.Provider == "manual" || _ai.Provider == "agent")
        {
            // agents work on whole games; ideas go through copy and paste
            _manual = new ManualStep("Ideas: pegá la consigna en tu chat de IA",
                StudioDesigner.ManualConceptPrompt(slots, ProfileLibrary.Find, wish, direction),
                answer =>
                {
                    _concepts = StudioDesigner.ParseConcepts(answer, direction, wish);
                    _manual = null;
                    _progress = "";
                });
            return;
        }
        var ai = _ai.Operation(_ai.CreateProvider()!, 1);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _progress = $"{ai.Name} está pensando ideas...";
        _build.Start(ai, "ideas", $"{_ai.CliEfforts.GetValueOrDefault(_ai.Provider)}/{_ai.DesignAttempts}");
        _conceptTask = Task.Run(() => new StudioDesigner().ConceptsAsync(ai, slots, ProfileLibrary.Find, wish, token, direction));
    }

    private void Write(GameConcept concept)
    {
        ClearProblem();
        _activeWork = null;
        if (!string.IsNullOrWhiteSpace(concept.MissingData))
        {
            Problem(StudioDesigner.MissingDataPrefix + concept.MissingData);
            return;
        }
        _manual = null;
        var slots = Slots();
        concept = concept with { Direction = SelectedDirection() };
        var template = new StudioGame
        {
            Kind = _inspiredMode ? "inspired" : "fusion",
            Id = string.IsNullOrWhiteSpace(concept.Title) ? "" : StudioLibrary.Slug(concept.Title),
            Title = concept.Title,
            Approach = concept.Approach,
            Pitch = concept.Pitch,
            HowToPlay = concept.HowToPlay,
            Design = concept.Design,
            PlayerRequest = concept.PlayerRequest,
            Games = slots,
            BaseSlot = concept.Direction.Resolve(slots),
            Version = 1,
            Author = AiPicker.ShortName(_ai.Provider),
        };
        string provider = _ai.Provider;
        var ai = provider is "manual" or "agent" ? null : _ai.Operation(_ai.CreateProvider()!);
        PrepareReferences(template, references => WriteReady(concept, template, provider, ai, references));
    }

    private void WriteReady(GameConcept concept, StudioGame template, string provider, IAiProvider? ai, OriginalReferences references)
    {
        var slots = template.Games;
        switch (provider)
        {
            case "manual":
                StartManual("Escribir el juego: pegá la consigna en tu chat de IA",
                    StudioDesigner.ManualWritePrompt(slots, ProfileLibrary.Find, concept), template, first: true, references);
                break;
            case "agent":
                StartAgent(template, $"Implementá esta unión respetando la dirección elegida.\n\nIdea: {concept.Title} ({concept.Approach})\n{concept.Pitch}\nCómo se juega: {concept.HowToPlay}\nDiseño: {concept.Design}", first: true, references);
                break;
            default:
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                var sources = Sources();
                _progress = $"{ai!.Name} está escribiendo el juego...";
                _build.Start(ai, "diseño", $"{_ai.CliEfforts.GetValueOrDefault(_ai.Provider)}/{_ai.DesignAttempts}");
                var designer = RecoverableDesigner(references);
                _designTask = Task.Run(() => designer.WriteAsync(ai, slots, ProfileLibrary.Find, concept,
                    game => StudioValidator.Run(game, sources, _romDir), token, p => _progress = p, references));
                break;
            }
        }
    }

    private void Revise(StudioGame current, string request)
    {
        ClearProblem();
        _activeWork = null;
        var template = current.Copy();
        template.Version = current.Version + 1;
        template.Request = request;
        template.Author = AiPicker.ShortName(_ai.Provider);
        template.Created = DateTime.Now;
        template.Usage = null;
        string provider = _ai.Provider;
        var ai = provider is "manual" or "agent" ? null : _ai.Operation(_ai.CreateProvider()!);
        PrepareReferences(template, references => ReviseReady(current, request, template, provider, ai, references));
    }

    private void ReviseReady(StudioGame current, string request, StudioGame template, string provider, IAiProvider? ai, OriginalReferences references)
    {
        switch (provider)
        {
            case "manual":
                StartManual("Versión nueva: pegá la consigna en tu chat de IA",
                    StudioDesigner.ManualRevisePrompt(current, ProfileLibrary.Find, request), template, first: false, references);
                break;
            case "agent":
                StartAgent(template, $"El jugador probó la versión {current.Version} (está en juego.js) y pide:\n\n\"{request}\"\n\nHacé la versión siguiente.", first: false, references);
                break;
            default:
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                var sources = Sources();
                _progress = $"{ai!.Name} está preparando la corrección...";
                _build.Start(ai, "cambios", $"{_ai.CliEfforts.GetValueOrDefault(_ai.Provider)}/{_ai.DesignAttempts}");
                var designer = RecoverableDesigner(references);
                _designTask = Task.Run(() => designer.ReviseAsync(ai, current, ProfileLibrary.Find, request,
                    game => StudioValidator.Run(game, sources, _romDir), token, p => _progress = p, references));
                break;
            }
        }
        _request = "";
    }

    private StudioDesigner RecoverableDesigner(OriginalReferences references, StudioWork? work = null)
    {
        work ??= new StudioWork { Originals = references.Items.ToDictionary(r => r.Slot, r => r.Fingerprint) };
        _activeWork = work.Id;
        var saved = work;
        return new StudioDesigner { AttemptLimit = _ai.DesignAttempts, Checkpoint = state =>
        {
            saved.State = state;
            saved.Engine = typeof(StudioSession).Module.ModuleVersionId.ToString();
            StudioWorkStore.Save(saved);
        } };
    }

    private void RefreshWork()
    {
        try { _pendingWork = StudioWorkStore.Pending(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { _pendingWork = Array.Empty<StudioWork>(); }
    }

    private void DrawPendingWork()
    {
        var pending = _pendingWork.Where(w => w.State.Template.IsInspired == _inspiredMode).ToList();
        if (Busy || pending.Count == 0) return;
        if (!ImGui.CollapsingHeader($"Trabajos pendientes ({pending.Count})", pending.Count == 1 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None)) return;
        foreach (var work in pending.Take(3))
        {
            var game = work.State.Draft ?? work.State.Template;
            ImGui.PushID(work.Id);
            ImGui.TextColored(Gold, string.IsNullOrWhiteSpace(game.Title) ? "Juego en preparación" : game.Title);
            string wish = work.State.Template.PlayerRequest.Length > 0 ? work.State.Template.PlayerRequest : work.State.Template.Pitch;
            if (wish.Length > 0) AiPicker.Hint(wish.Length > 160 ? wish[..160] + "..." : wish);
            AiPicker.Hint(work.State.Draft != null ? "Se conservó el borrador. Se probará antes de pedir otra corrección." : "Se conservó el pedido. Todavía no hubo un guion nuevo.");
            bool available = work.State.Template.Games.All(g => _games.Any(s => s.Profile.Id == g.Profile));
            ImGui.BeginDisabled(!available || !AiPicker.IsApi(_ai.Provider));
            if (ImGui.Button("Continuar")) ResumeWork(work);
            ImGui.EndDisabled();
            if (work.State.Draft != null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Copiar borrador")) ImGui.SetClipboardText(work.State.Draft.Script);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Descartar pendiente"))
            {
                try { StudioWorkStore.Remove(work.Id); RefreshWork(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { Problem("No se pudo descartar el pendiente: " + e.Message); }
            }
            if (!available) AiPicker.Hint("Faltan originales de este trabajo en la carpeta seleccionada.");
            else if (!AiPicker.IsApi(_ai.Provider)) AiPicker.Hint("Elegí una herramienta conectada para continuar.");
            ImGui.PopID();
        }
        ImGui.Separator();
    }

    private void ResumeWork(StudioWork work)
    {
        ClearProblem();
        var provider = _ai.CreateProvider();
        if (provider == null) return;
        var ai = _ai.Operation(provider, previous: work.State.Usage);
        var template = work.State.Template;
        _inspiredMode = template.IsInspired;
        _chosen.Clear(); _chosen.AddRange(template.Games.Select(g => g.Profile));
        _baseProfile = template.Games.FirstOrDefault(g => g.Slot == template.BaseSlot)?.Profile;
        PrepareReferences(template, references =>
        {
            if (!work.Matches(references))
            {
                Problem("Cambiaron los originales, sus perfiles o el firmware. El borrador sigue guardado; prepará un trabajo nuevo con esos datos actualizados.");
                return;
            }
            var designer = RecoverableDesigner(references, work);
            _cts = new CancellationTokenSource(); var token = _cts.Token;
            var sources = Sources(); _build.Start(ai, "continuar", $"{_ai.CliEfforts.GetValueOrDefault(_ai.Provider)}/{_ai.DesignAttempts}");
            _designTask = Task.Run(() => designer.ResumeAsync(ai, work.State,
                game => StudioValidator.Run(game, sources, _romDir), token, p => _progress = p, references));
        });
    }

    private void PrepareReferences(StudioGame game, Action<OriginalReferences> ready)
    {
        var sources = Sources();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _progress = "Preparando referencias de los originales...";
        _afterReferences = ready;
        _referenceTask = Task.Run(() => OriginalReferences.Build(game, sources, _romDir, token), token);
    }

    private void StartManual(string title, string prompt, StudioGame template, bool first, OriginalReferences? references = null)
    {
        string? referenceFolder = null;
        if (references != null)
        {
            referenceFolder = Path.Combine(StudioWorkspace.Root, "manual", Guid.NewGuid().ToString("N"));
            references.WriteTo(referenceFolder);
            prompt += "\n\n" + references.Describe(template.BaseSlot, true) +
                "\nLas capturas deben adjuntarse al chat; que figure su nombre no significa que se hayan adjuntado. Si faltan, no afirmes haberlas visto.";
        }
        var current = template;
        void OnAnswer(string answer)
        {
            var game = StudioDesigner.ParseGame(answer, current, out var error)
                       ?? throw new AiException(error ?? "no se pudo leer");
            current = game;
            _manual = null;
            ValidateThen(game, (g, report) =>
            {
                if (report.Ok)
                    SaveAndStart(g, first, report.Picture);
                else
                {
                    Problem("El juego todavía falla: copiá la consigna de corrección y pegásela a tu IA.", report);
                    _manual = new ManualStep("Corregir: pegale esto a tu IA",
                        StudioDesigner.ManualFixPrompt(report, g) + (references != null ? "\n\n" + references.Describe(g.BaseSlot, true) +
                            "\nAdjuntá las capturas de originales al chat; no supongas que ya las recibió." : ""), OnAnswer, referenceFolder);
                    _scrollToManual = true;
                }
            });
        }
        _manual = new ManualStep(title, prompt, OnAnswer, referenceFolder);
        _scrollToManual = true;
        _progress = "";
    }

    private void StartAgent(StudioGame template, string task, bool first, OriginalReferences? references = null)
    {
        if (first)
            template.Id = StudioLibrary.UniqueId(string.IsNullOrWhiteSpace(template.Id) ? "juego-nuevo" : template.Id);
        string? cli = FindCli();
        string? hint = cli != null ? $"\"{cli}\" studio juego.js --games \"{_folder}\" --shots prueba" : null;
        _agentDir = StudioWorkspace.Create(template, task, ProfileLibrary.Find, hint, references: references);
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

    private void SaveAndStart(StudioGame game, bool first = false, VideoFrame? picture = null)
    {
        if (first || game.Version <= 1)
            game.Id = StudioLibrary.UniqueId(string.IsNullOrWhiteSpace(game.Id) ? StudioLibrary.Slug(game.Title) : game.Id);
        game.WorkId = _activeWork;
        game.Usage = _activeWork != null ? _build.Usage : null;
        try { StudioLibrary.Save(game, picture); RefreshSavedGames(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            RefreshWork();
            Problem("No se pudo guardar el juego: " + e.Message + ". El trabajo pendiente sigue disponible.");
            return;
        }
        if (_activeWork != null)
        {
            try { StudioWorkStore.Remove(_activeWork); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* The saved game remains playable if cleanup is blocked. */ }
            _activeWork = null;
            RefreshWork();
        }
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
        if (_referenceTask is { IsCompleted: true } rt)
        {
            _referenceTask = null;
            var ready = _afterReferences; _afterReferences = null;
            if (rt.IsCanceled || _cts?.IsCancellationRequested == true) _progress = "Preparación cancelada.";
            else if (rt.IsFaulted) Problem(Failure(rt.Exception));
            else ready?.Invoke(rt.Result);
        }
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
            _build.Stop(_cts?.IsCancellationRequested != true && ct.IsCompletedSuccessfully);
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
            _build.Stop(_cts?.IsCancellationRequested != true && dt.IsCompletedSuccessfully && dt.Result.Game != null);
            _designTask = null;
            RefreshWork();
            if (!dt.IsCompletedSuccessfully)
            {
                _activeWork = null;
                Problem(Failure(dt.Exception));
            }
            else if (dt.Result.Game is { } game)
            {
                _progress = dt.Result.Attempts == 0 ? "Borrador recuperado y probado." : $"Listo en {dt.Result.Attempts} intento{(dt.Result.Attempts > 1 ? "s" : "")}.";
                _lastTest = dt.Result.Report?.Summary;
                SaveAndStart(game, first: game.Version <= 1, picture: dt.Result.Report?.Picture);
            }
            else
            {
                _activeWork = null;
                Problem(dt.Result.Error ?? "El juego necesita otra corrección. El trabajo sigue guardado.", dt.Result.Report);
            }
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
                if (st.Result.Error == null) GameStarted?.Invoke(st.Result.Game);
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
                SaveAndStart(game, first: game.Version <= 1, picture: report.Picture);
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
