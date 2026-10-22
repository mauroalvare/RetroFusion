using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Retro.Ai;
using Retro.Asm;
using Retro.Compat;

namespace Retro.App;

/// <summary>
/// "Crear un juego de 8 bits": the AI writes an original game in assembly for a real (emulated)
/// C64 or Spectrum, the emulator tries it with a robot player and the AI fixes it until it
/// works; or the player starts from the machine's template and edits it by hand. Every game
/// is a folder in "Juegos creados" and plays like any other game.
/// </summary>
public sealed class CreatorWindow
{
    private static readonly Vector4 Gold = new(1, 0.85f, 0.3f, 1);
    private static readonly Vector4 Blue = new(0.6f, 0.8f, 1, 1);
    private static readonly Vector4 Red = new(1, 0.5f, 0.4f, 1);
    private static readonly Vector4 Green = new(0.5f, 0.9f, 0.5f, 1);

    private readonly string _romDir;
    private readonly AiSettings _ai;
    private readonly Action<string> _play;
    private readonly Action? _fromOriginals;
    private bool _open;
    private int _machine;               // index in Templates.All
    private string _wish = "";
    private volatile string _progress = "";
    private readonly AiBuildWindow _build = new("creador");
    private string? _problem;
    private string? _problemDetail;
    private CancellationTokenSource? _cts;
    private Task<WriterOutcome>? _writeTask;
    private string _writeMachine = "", _writeWish = "", _writeAuthor = "";
    private Task<CreatedGame>? _templateTask;
    private Task<(CreatedGame game, TrialReport report)>? _retryTask;
    private IReadOnlyList<CreatedGame>? _games;
    private Task<int>? _exportTask;
    private bool _exported;
    private GameWork? _activeWork;
    private IReadOnlyList<GameWork> _pendingWork = [];

    // copy and paste: the prompt the player takes to a chat, and the answer they bring back
    private string? _manualPrompt;
    private string _pasted = "";
    private GameDraft? _manualDraft;
    private Task<(GameDraft draft, TrialReport report)>? _manualTask;

    // agent folder: the emulator tries juego.s every time the agent saves it
    private string? _agentDir;
    private DateTime _agentSeen;
    private double _agentPoll;
    private Task<(GameDraft draft, TrialReport report)>? _agentTask;
    private string? _agentReport;

    public CreatorWindow(string romDir, AiSettings ai, Action<string> play, Action? fromOriginals = null)
    {
        _romDir = romDir;
        _ai = ai;
        _play = play;
        _fromOriginals = fromOriginals;
    }

    public void Open()
    {
        _open = true;
        RefreshWork();
        _games = null;
        if (!_exported)
        {
            // the example games, tried once in the emulator, so there is something to play right away
            _exported = true;
            _exportTask = Task.Run(() => CreatedGames.ExportExamples(_romDir));
        }
    }

    private bool Busy => _writeTask != null || _templateTask != null || _retryTask != null || _manualTask != null || _agentTask != null;
    public Task CancelWork()
    {
        _cts?.Cancel();
        return Task.WhenAll(new Task?[] { _writeTask, _templateTask, _retryTask, _manualTask, _agentTask, _exportTask }.OfType<Task>());
    }

    private float? _panelLeft;

    /// <summary>Screen width to leave free on the right for this panel (0 when closed or not on the right half).</summary>
    public float RightReserve(float screenWidth) =>
        _open && _panelLeft is float left && left > screenWidth / 2 ? screenWidth - left : 0;

    public void Draw()
    {
        Poll();
        if (!_open)
        {
            _panelLeft = null;
            return;
        }
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + vp.Size.X - 540, vp.Pos.Y + 30), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(530, Math.Min(760, vp.Size.Y - 70)), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Crear un juego de 8 bits", ref _open))
        {
            _panelLeft = null;
            ImGui.End();
            return;
        }
        _panelLeft = ImGui.GetWindowPos().X - vp.Pos.X;
        DrawPendingWork();
        DrawNew();
        ImGui.Separator();
        DrawGames();
        ImGui.End();
    }

    private void DrawNew()
    {
        if (_fromOriginals != null)
        {
            ImGui.BeginDisabled(Busy);
            if (ImGui.Button("Crear a partir de tus ROMs")) { _open = false; _fromOriginals(); }
            ImGui.EndDisabled();
            ImGui.Separator();
            ImGui.TextColored(Gold, "Crear desde cero");
        }
        ImGui.TextWrapped("Elegí una máquina y describí el juego que querés crear.");
        ImGui.TextUnformatted("Máquina:");
        for (int i = 0; i < Templates.All.Count; i++)
        {
            ImGui.SameLine();
            ImGui.RadioButton(Templates.All[i].Name, ref _machine, i);
        }
        AiPicker.Combo(_ai, "##ia-crear");
        bool callable = AiPicker.IsApi(_ai.Provider);
        if (callable)
        {
            AiPicker.Settings(_ai, "La creación puede necesitar varios pedidos a la IA.");
            AiPicker.Generation(_ai, () => new(GameWriter.Guide(Templates.All[_machine]),
                [new(true, GameWriter.FirstPrompt(Templates.All[_machine], _wish, new Random(0)))], null));
        }
        else if (_ai.Provider == "manual")
            AiPicker.Hint("Copiá el pedido a tu chat y pegá acá la respuesta.");
        else
            AiPicker.Hint("Prepará la carpeta y pasale la tarea a tu agente.");
        ImGui.TextUnformatted("Qué juego querés (opcional):");
        ImGui.InputTextMultiline("##pedido-crear", ref _wish, 1000, new Vector2(-1, 44));
        AiPicker.Hint("Dejalo vacío si preferís que la IA elija la idea.");
        ImGui.BeginDisabled(Busy || _agentDir != null);
        if (ImGui.Button(callable ? "Escribir el juego con IA" : _ai.Provider == "manual" ? "Preparar la consigna" : "Preparar la carpeta del agente"))
        {
            if (callable)
                Write();
            else if (_ai.Provider == "manual")
                StartManual();
            else
                StartAgent();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(Busy);
        if (ImGui.Button("Empezar desde la plantilla"))
            FromTemplate();
        ImGui.EndDisabled();
        if (_progress.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Blue);
            ImGui.TextWrapped(_progress);
            ImGui.PopStyleColor();
        }
        if (_cts != null && _writeTask != null)
        {
            if (ImGui.SmallButton("Cancelar"))
                _cts.Cancel();
        }
        _build.Draw(_writeTask != null, _progress, _cts);
        if (_problem != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Red);
            ImGui.TextWrapped(_problem);
            ImGui.PopStyleColor();
            if (_problemDetail != null && ImGui.TreeNode("Informe de la prueba##problema"))
            {
                ImGui.TextWrapped(_problemDetail);
                ImGui.TreePop();
            }
        }
        if (_manualPrompt != null)
            DrawManual();
        if (_agentDir != null)
            DrawAgent();
    }

    private void DrawManual()
    {
        ImGui.Separator();
        ImGui.TextColored(Gold, _manualDraft == null ? "Copiar y pegar" : $"Corregir \"{_manualDraft.Title}\"");
        if (ImGui.Button(_manualDraft == null ? "1. Copiar la consigna" : "1. Copiar el pedido de corrección"))
        {
            ImGui.SetClipboardText(_manualPrompt);
            _progress = "Pedido copiado. Pegalo en tu chat y traé la respuesta completa.";
        }
        ImGui.TextUnformatted("2. Pegá acá la respuesta completa:");
        ImGui.InputTextMultiline("##respuesta-crear", ref _pasted, 200_000, new Vector2(-1, 90));
        ImGui.BeginDisabled(Busy || _pasted.Trim().Length == 0);
        if (ImGui.Button("3. Probar el juego"))
            TryPasted();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.SmallButton("Cancelar##manual"))
        {
            _manualPrompt = null;
            _manualDraft = null;
            _pasted = "";
        }
    }

    private void DrawAgent()
    {
        ImGui.Separator();
        ImGui.TextColored(Gold, "Carpeta del agente");
        ImGui.TextWrapped(_agentDir);
        if (ImGui.Button("Abrir la carpeta##agente"))
            Process.Start(new ProcessStartInfo { FileName = _agentDir, UseShellExecute = true });
        ImGui.SameLine();
        if (ImGui.Button("Copiar la instrucción para el agente"))
        {
            ImGui.SetClipboardText($"Trabajá en la carpeta {_agentDir}: leé TAREA.md y seguí sus instrucciones.");
            _progress = "Instrucción copiada. Pegala en tu agente.";
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Dejar de esperar"))
        {
            _agentDir = null;
            _agentReport = null;
            return;
        }
        AiPicker.Hint(_agentTask != null ? "Probando lo que guardó el agente..." : "Esperando que el agente guarde juego.s...");
        if (_agentReport != null && ImGui.TreeNode("Último informe##agente"))
        {
            ImGui.TextWrapped(_agentReport);
            ImGui.TreePop();
        }
    }

    private void StartManual()
    {
        _problem = _problemDetail = null;
        _agentDir = null;
        _manualDraft = null;
        _pasted = "";
        _manualPrompt = GameWriter.ManualPrompt(Templates.All[_machine], _wish, Random.Shared);
        _writeMachine = Templates.All[_machine].Machine;
        _writeWish = _wish;
        _progress = "Consigna lista: copiala con el botón 1.";
    }

    private void TryPasted()
    {
        var draft = GameWriter.ParseDraft(_pasted, out var error);
        if (draft == null)
        {
            Problem("No pude leer la respuesta: " + error + ". Copiá toda la respuesta de la IA, con el bloque de código.");
            return;
        }
        _problem = _problemDetail = null;
        _progress = $"Probando \"{draft.Title}\" en el emulador...";
        string machine = _writeMachine;
        _manualTask = Task.Run(() => (draft, HomebrewTrial.Run(draft.Source, machine, _romDir)));
    }

    private void StartAgent()
    {
        _problem = _problemDetail = null;
        _manualPrompt = null;
        _writeMachine = Templates.All[_machine].Machine;
        _writeWish = _wish;
        string cli = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "retro-cli.exe" : "retro-cli");
        _agentDir = GameWorkspace.Create(_writeMachine, _wish, Random.Shared, File.Exists(cli) ? $"\"{cli}\" probar {GameWorkspace.SourceFile}" : null);
        _agentSeen = GameWorkspace.Read(_agentDir)?.written ?? DateTime.MinValue;
        _agentReport = null;
        _progress = "Carpeta lista: pasale la instrucción a tu agente.";
    }

    /// <summary>Once a second: if the agent saved juego.s again, try it.</summary>
    private void PollAgent()
    {
        if (_agentDir == null || _agentTask != null)
            return;
        _agentPoll += ImGui.GetIO().DeltaTime;
        if (_agentPoll < 1)
            return;
        _agentPoll = 0;
        if (GameWorkspace.Read(_agentDir) is not { } read || read.written <= _agentSeen)
            return;
        _agentSeen = read.written;
        string dir = _agentDir, machine = _writeMachine;
        _agentTask = Task.Run(() =>
        {
            var report = HomebrewTrial.Run(read.draft.Source, machine, _romDir);
            GameWorkspace.WriteReport(dir, report);
            return (read.draft, report);
        });
    }

    private void DrawGames()
    {
        _games ??= CreatedGames.All();
        ImGui.TextColored(Gold, "Juegos creados");
        if (ImGui.SmallButton("Abrir la carpeta"))
        {
            Directory.CreateDirectory(CreatedGames.UserFolder);
            Process.Start(new ProcessStartInfo { FileName = CreatedGames.UserFolder, UseShellExecute = true });
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(CreatedGames.UserFolder);
        if (_games.Count == 0)
        {
            AiPicker.Hint("Todavía no hay ninguno.");
            return;
        }
        if (ImGui.TreeNode("Editar juegos a mano"))
        {
            AiPicker.Hint("Abrí la carpeta del juego y modificá juego.s. Después usá Probar de nuevo.");
            ImGui.TreePop();
        }
        foreach (var g in _games)
        {
            ImGui.PushID(g.Folder);
            ImGui.Spacing();
            ImGui.TextColored(Gold, g.Title);
            ImGui.SameLine();
            if (g.Passed)
                ImGui.TextColored(Green, "pasó la prueba");
            else
                ImGui.TextColored(Red, "no pasó la prueba");
            ImGui.TextDisabled($"{g.MachineName} - {g.Author} - {g.Created:dd/MM HH:mm}");
            if (g.Pitch.Length > 0)
                ImGui.TextWrapped(g.Pitch);
            if (g.HowToPlay.Length > 0)
                AiPicker.Hint("Cómo se juega: " + g.HowToPlay);
            if (g.Usage is { } usage && ImGui.TreeNode("Uso de IA"))
            { AiBuildWindow.UsageDetails(usage); ImGui.TreePop(); }
            ImGui.BeginDisabled(Busy || !File.Exists(g.ProgramPath));
            if (ImGui.Button("Jugar"))
                _play(g.ProgramPath);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(Busy);
            if (ImGui.Button("Probar de nuevo"))
                TryAgain(g);
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Abrir su carpeta"))
                Process.Start(new ProcessStartInfo { FileName = g.Folder, UseShellExecute = true });
            if (g.Trial.Length > 0 && ImGui.TreeNode("Prueba automática"))
            {
                ImGui.TextWrapped(g.Trial);
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
    }

    private void Write() => StartWriting(new GameWork(), resume: false);

    private void RefreshWork()
    {
        try { _pendingWork = GameWorkStore.Pending(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _pendingWork = []; }
    }

    private void DrawPendingWork()
    {
        if (Busy || _pendingWork.Count == 0) return;
        if (!ImGui.CollapsingHeader($"Trabajos pendientes ({_pendingWork.Count})", _pendingWork.Count == 1 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None)) return;
        foreach (var work in _pendingWork.Take(3))
        {
            ImGui.PushID(work.Id);
            ImGui.TextColored(Gold, work.State.Draft?.Title ?? "Juego en preparación");
            if (work.State.Wish.Length > 0) AiPicker.Hint(work.State.Wish.Length > 160 ? work.State.Wish[..160] + "..." : work.State.Wish);
            AiPicker.Hint(work.State.Draft != null ? "Se probará el código guardado antes de pedir otra respuesta." : "Se conservó la idea elegida. Todavía no hubo código nuevo.");
            ImGui.BeginDisabled(!AiPicker.IsApi(_ai.Provider) || _manualPrompt != null || _agentDir != null);
            if (ImGui.Button("Continuar")) StartWriting(work, resume: true);
            ImGui.EndDisabled();
            if (work.State.Draft is { } draft)
            { ImGui.SameLine(); if (ImGui.SmallButton("Copiar código")) ImGui.SetClipboardText(draft.Source); }
            ImGui.SameLine();
            if (ImGui.SmallButton("Descartar pendiente"))
            {
                try { GameWorkStore.Remove(work.Id); RefreshWork(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Problem("No se pudo descartar el pendiente: " + e.Message); }
            }
            if (!AiPicker.IsApi(_ai.Provider)) AiPicker.Hint("Elegí una herramienta conectada para continuar.");
            ImGui.PopID();
        }
        ImGui.Separator();
    }

    private void StartWriting(GameWork work, bool resume)
    {
        var provider = _ai.CreateProvider();
        if (provider == null)
        {
            Problem("Esa IA no se puede llamar desde acá.");
            return;
        }
        if (resume && work.State.Draft is { } draft && CreatedGames.All().FirstOrDefault(g=>g.WorkId==work.Id&&g.Machine==work.State.Machine) is { } saved && File.Exists(saved.SourcePath))
        {
            try
            {
                string source = File.ReadAllText(saved.SourcePath);
                if (string.IsNullOrWhiteSpace(source)) { Problem("El archivo de código está vacío. El borrador pendiente se conserva; terminá de editarlo antes de continuar."); return; }
                if (source != draft.Source) { work.State.Draft = draft with { Source = source }; work.State.Author = "edición manual"; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Problem("No se pudo leer el borrador editado: " + e.Message); return; }
        }
        provider = _ai.Operation(provider, previous: resume ? work.State.Usage : null);
        _problem = _problemDetail = null;
        _manualPrompt = null;
        _agentDir = null;
        _cts = new CancellationTokenSource();
        _writeMachine = resume ? work.State.Machine : Templates.All[_machine].Machine;
        _writeWish = resume ? work.State.Wish : _wish;
        if (resume) { _wish = _writeWish; _machine = Enumerable.Range(0, Templates.All.Count).First(i=>Templates.All[i].Machine==_writeMachine); }
        _writeAuthor = provider.Name;
        _activeWork = work;
        _progress = $"{provider.Name} está escribiendo el juego...";
        _build.Start(provider, resume ? "continuar" : "diseño", $"{_ai.CliEfforts.GetValueOrDefault(_ai.Provider)}/{_ai.DesignAttempts}");
        var token = _cts.Token;
        string machine = _writeMachine, wish = _writeWish;
        var writer = new GameWriter { MaxAttempts = _ai.DesignAttempts, Checkpoint = state => { work.State = state; GameWorkStore.Save(work); } };
        _writeTask = Task.Run(() => resume ? writer.ResumeAsync(provider, work.State, _romDir, token, p=>_progress=p)
            : writer.WriteAsync(provider, machine, wish, _romDir, token, p=>_progress=p), token);
    }

    private void FromTemplate()
    {
        _problem = _problemDetail = null;
        string machine = Templates.All[_machine].Machine;
        _progress = "Copiando la plantilla y probándola...";
        _templateTask = Task.Run(() => CreatedGames.FromTemplate(machine, _romDir));
    }

    private void TryAgain(CreatedGame g)
    {
        _problem = _problemDetail = null;
        _progress = $"Probando \"{g.Title}\" de nuevo...";
        _retryTask = Task.Run(() => (g, CreatedGames.TryAgain(g, _romDir)));
    }

    private void Poll()
    {
        PollAgent();
        if (_manualTask is { IsCompleted: true } mt)
        {
            _manualTask = null;
            if (!mt.IsCompletedSuccessfully)
                Problem(Failure(mt.Exception));
            else if (mt.Result.report.Ok)
            {
                var g = CreatedGames.Save(mt.Result.draft, mt.Result.report, _writeMachine, _writeWish, AiPicker.ShortName("manual"));
                _games = null;
                _manualPrompt = null;
                _manualDraft = null;
                _pasted = "";
                _progress = $"\"{g.Title}\" pasó la prueba.";
                _play(g.ProgramPath);
            }
            else
            {
                _manualDraft = mt.Result.draft;
                _manualPrompt = GameWriter.ManualFixPrompt(mt.Result.draft, mt.Result.report, _writeWish);
                _pasted = "";
                Problem($"\"{mt.Result.draft.Title}\" no pasó la prueba: copiá el pedido de corrección y pegale la respuesta nueva.", mt.Result.report.Summary);
            }
        }
        if (_agentTask is { IsCompleted: true } at)
        {
            _agentTask = null;
            if (!at.IsCompletedSuccessfully)
                Problem(Failure(at.Exception));
            else
            {
                _agentReport = at.Result.report.Summary;
                if (at.Result.report.Ok)
                {
                    var g = CreatedGames.Save(at.Result.draft, at.Result.report, _writeMachine, _writeWish, "agente");
                    _games = null;
                    _agentDir = null;
                    _agentReport = null;
                    _progress = $"\"{g.Title}\" pasó la prueba.";
                    _play(g.ProgramPath);
                }
                else
                    _progress = "El juego del agente todavía no pasa la prueba (el agente tiene el informe en su carpeta).";
            }
        }
        if (_exportTask is { IsCompleted: true } et)
        {
            _exportTask = null;
            _games = null;
            if (!et.IsCompletedSuccessfully)
                Problem("No pude copiar los juegos de ejemplo: " + Failure(et.Exception));
        }
        if (_writeTask is { IsCompleted: true } wt)
        {
            _build.Stop(_cts?.IsCancellationRequested != true && wt.IsCompletedSuccessfully && wt.Result.Ok);
            _writeTask = null;
            _cts = null;
            var work = _activeWork; _activeWork = null;
            RefreshWork();
            if (!wt.IsCompletedSuccessfully)
                Problem(Failure(wt.Exception));
            else if (wt.Result.Game is { } draft)
            {
                CreatedGame g;
                try { g = CreatedGames.Save(draft, wt.Result.Report, _writeMachine, _writeWish,
                    string.IsNullOrEmpty(work?.State.Author) ? _writeAuthor : work.State.Author, _build.Usage, work?.Id); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { Problem("No se pudo guardar el juego: " + e.Message + ". Podés continuar desde el trabajo pendiente."); return; }
                _games = null;
                if (wt.Result.Ok)
                {
                    if (work != null)
                        try { GameWorkStore.Remove(work.Id); RefreshWork(); }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    _progress = wt.Result.Attempts == 0 ? "Borrador recuperado y probado." : $"\"{g.Title}\" listo en {wt.Result.Attempts} intento{(wt.Result.Attempts > 1 ? "s" : "")}.";
                    _play(g.ProgramPath);
                }
                else
                    Problem($"La IA no logró que \"{g.Title}\" pase la prueba en {wt.Result.Attempts} intentos ({wt.Result.Problem}). " +
                            "Quedó guardado: podés editarlo y probarlo de nuevo.", wt.Result.Report?.Summary);
            }
            else
                Problem("El trabajo quedó pendiente: " + wt.Result.Problem);
        }
        if (_templateTask is { IsCompleted: true } tt)
        {
            _templateTask = null;
            _games = null;
            if (!tt.IsCompletedSuccessfully)
                Problem(Failure(tt.Exception));
            else
            {
                _progress = $"Listo: {tt.Result.SourcePath}";
                _play(tt.Result.ProgramPath);
            }
        }
        if (_retryTask is { IsCompleted: true } rt)
        {
            _retryTask = null;
            _games = null;
            if (!rt.IsCompletedSuccessfully)
                Problem(Failure(rt.Exception));
            else if (rt.Result.report.Ok)
            {
                _progress = $"\"{rt.Result.game.Title}\" pasó la prueba.";
                _play(rt.Result.game.ProgramPath);
            }
            else
                Problem($"\"{rt.Result.game.Title}\" no pasó la prueba.", rt.Result.report.Summary);
        }
    }

    private void Problem(string text, string? detail = null)
    {
        _progress = "";
        _problem = text;
        _problemDetail = detail;
    }

    private static string Failure(AggregateException? e)
    {
        var inner = e?.GetBaseException();
        return inner is OperationCanceledException ? "Cancelado." : inner?.Message ?? "Error desconocido.";
    }
}
