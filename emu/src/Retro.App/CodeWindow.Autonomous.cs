using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using ImGuiNET;
using Retro.Ai;
using Retro.Compat;
using Retro.Core;
using Retro.Fusion;

namespace Retro.App;

public sealed partial class CodeWindow
{
    private readonly string _researchFirmwareDirectory;
    private bool _automaticAnalysis = true;
    private string _autonomousGoal = "", _autonomousMessage = "", _autonomousSource = "", _autonomousStoreProblem = "";
    private int _autonomousTarget = 12, _autonomousMinutes = 5, _autonomousCalls = 12;
    private int _autonomousPasses = 8, _autonomousFrames = 900, _autonomousFinding;
    private long _autonomousGeneration, _autonomousContextRevision = -1, _autonomousStarted, _autonomousStageStarted;
    private double _autonomousPriorSeconds;
    private AutonomousResearchStore? _autonomousStore;
    private AutonomousResearchWork? _autonomousWork;
    private IReadOnlyList<AutonomousResearchWork> _autonomousSaved = [];
    private IReadOnlyList<AutonomousResearchWork> _autonomousFolderSaved = [];
    private string? _autonomousDiscardId;
    private bool _autonomousDiscardOpen;
    private AutonomousResearchProgress? _autonomousProgress;
    private Task<AutonomousResearchWork>? _autonomousTask;
    private CancellationTokenSource? _autonomousCts;
    private AiOperation? _autonomousAi;
    private readonly ConcurrentQueue<(long Generation, string? Json, AutonomousResearchProgress? Progress)> _autonomousUpdates = new();
    private string _analysisAutotestAction = "", _analysisAutotestSection = "";
    private int _analysisAutotestScrollFrames;
    internal Action<string, Vector2, Vector2>? AnalysisControlBounds { get; set; }
    private bool AnalysisButton(string label, bool primary = false)
    {
        bool clicked = primary ? UiTheme.PrimaryButton(label) : ImGui.Button(UiText.Label(label));
        AnalysisControlBounds?.Invoke(label, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        return clicked;
    }

    internal static int AnalysisNavigationColumns(float available, float pageWidth) =>
        available >= pageWidth * 4 ? 4 : available >= pageWidth * 2 ? 2 : 1;

    /// <summary>Only the isolated --autotest --hidden entry point may enable these requests.</summary>
    internal void ConfigureAnalysisAutotest(string action, string section, bool enabled)
    {
        if (!enabled) return;
        if (action is not ("" or "local")) throw new ArgumentException("Acción de prueba desconocida.", nameof(action));
        if (section is not ("" or "budget" or "progress" or "findings" or "verification" or "values")) throw new ArgumentException("Sección de prueba desconocida.", nameof(section));
        OpenAutomaticAnalysis(); Update();
        if (section == "values") OpenModifications();
        _analysisAutotestAction = action; _analysisAutotestSection = section;
        _analysisAutotestScrollFrames = 8;
    }

    private void ApplyAnalysisAutotest()
    {
        if (_analysisAutotestAction != "local" || _autonomousStore == null || _machine == null) return;
        _analysisAutotestAction = "";
        _autonomousCalls = 0; _autonomousPasses = 1; _autonomousFrames = 120; _autonomousMinutes = 1;
        BeginAutonomousAnalysis(false);
    }

    private void ScrollAnalysisAutotest(string section)
    {
        if (_analysisAutotestScrollFrames <= 0 || _analysisAutotestSection != section) return;
        ImGui.SetScrollFromPosY(ImGui.GetCursorScreenPos().Y - ImGui.GetWindowPos().Y, 0);
        _analysisAutotestScrollFrames--;
    }

    public void OpenAutomaticAnalysis()
    {
        _host.RefreshLoadedProfile(); _automaticAnalysis = true;
        OpenPage(ToolPage.Analysis);
    }

    private static AutonomousResearchWork CopyResearch(AutonomousResearchWork work) =>
        JsonSerializer.Deserialize<AutonomousResearchWork>(JsonSerializer.Serialize(work, GameProfile.Json), GameProfile.Json)!;

    private void StopAutonomousSession()
    {
        _autonomousGeneration++;
        RetireWork(ref _autonomousTask, ref _autonomousCts);
        _autonomousWork = null; _autonomousProgress = null; _autonomousAi = null;
        _autonomousStore = null; _autonomousSaved = []; _autonomousFolderSaved = []; _autonomousSource = "";
        _autonomousDiscardId = null; _autonomousDiscardOpen = false;
        _autonomousContextRevision = -1; _autonomousMessage = _autonomousStoreProblem = ""; _autonomousGoal = "";
        _autonomousFinding = 0;
        _analysisAutotestAction = _analysisAutotestSection = ""; _analysisAutotestScrollFrames = 0;
    }

    private void LoadAutonomousContext()
    {
        if (_autonomousContextRevision == _host.MediaRevision) return;
        _autonomousContextRevision = _host.MediaRevision;
        try
        {
            _autonomousSource = AutonomousResearch.ResolveOriginal(_host.LoadedMediaSource ?? _host.LoadedMedia ?? "");
            _autonomousStore = new AutonomousResearchStore(Path.Combine(Path.GetDirectoryName(_autonomousSource)!, ".retroemu", "analisis"));
            RefreshAutonomousHistory();
            if (_autonomousSaved.FirstOrDefault() is { } latest) SelectAutonomousWork(latest);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        { _autonomousMessage = UiText.Get("No se pudo abrir el análisis guardado: ") + error.Message; }
    }

    private void RefreshAutonomousHistory()
    {
        if (_autonomousStore == null || _autonomousSource.Length == 0) return;
        string hash = AutonomousResearch.FileHash(_autonomousSource);
        _autonomousFolderSaved = _autonomousStore.Read();
        _autonomousSaved = _autonomousFolderSaved.Where(w => string.Equals(w.FileSha256, hash, StringComparison.OrdinalIgnoreCase)
            && AutonomousResearch.MatchesEdition(w, _host.LoadedMediaHashes)).ToArray();
        _autonomousStoreProblem = _autonomousStore.Problem;
    }

    private void SelectAutonomousWork(AutonomousResearchWork work)
    {
        _autonomousWork = CopyResearch(work); _autonomousFinding = 0; _autonomousProgress = null; _autonomousAi = null;
        bool changed = AutonomousResearch.InvalidateChangedContext(_autonomousWork, SessionProfile());
        _autonomousGoal = work.Options.Goal; _autonomousTarget = work.Options.TargetVariables;
        _autonomousMinutes = work.Options.Minutes; _autonomousCalls = work.Options.AiCalls;
        _autonomousPasses = work.Options.MaxPasses; _autonomousFrames = work.Options.LabFrames;
        _autonomousMessage = changed ? "Cambió el motor o el perfil. Los resultados anteriores se muestran como evidencia histórica y necesitan nuevo contraste. Continuar es explícito."
            : "Análisis guardado abierto. Continuar es una acción explícita y vuelve a comprobar el original y el firmware.";
    }

    private void BeginAutonomousAnalysis(bool resume)
    {
        if (_autonomousTask != null || _host.Fusion != null || _machine == null || _autonomousStore == null) return;
        try
        {
            bool useAi = _autonomousCalls > 0 && AiPicker.IsApi(_ai.Provider);
            var options = new AutonomousResearchOptions(_autonomousTarget, _autonomousMinutes,
                useAi ? _autonomousCalls : 0, _autonomousPasses, _autonomousFrames, _autonomousGoal.Trim());
            options.Validate();
            var profile = GameProfile.Parse(SessionProfile().ToJson());
            var previous = resume && _autonomousWork != null ? CopyResearch(_autonomousWork) : null;
            _autonomousAi = useAi ? _ai.Operation(_ai.CreateProvider() ?? throw new AiException("Elegí una IA disponible."), calls: options.AiCalls) : null;
            IPlayAdvisor? advisor = _autonomousAi == null ? null : new AiPlayAdvisor(_autonomousAi);
            _autonomousCts?.Dispose(); _autonomousCts = new CancellationTokenSource();
            var token = _autonomousCts.Token;
            long generation = ++_autonomousGeneration;
            var store = _autonomousStore; string source = _autonomousSource, firmware = _researchFirmwareDirectory;
            _autonomousPriorSeconds = previous?.ElapsedSeconds ?? 0;
            _autonomousStarted = _autonomousStageStarted = Stopwatch.GetTimestamp();
            _autonomousProgress = new("Inicio", _autonomousPriorSeconds, previous?.CompletedPasses ?? 0,
                previous?.Candidates ?? 0, previous?.VerifiedWrites ?? 0, previous?.AiCallsUsed ?? 0, "Guardando el objetivo antes de investigar.");
            _autonomousMessage = "El laboratorio arranca el original y ejecuta entradas en una máquina de prueba.";
            if (!resume) { _autonomousWork = null; _autonomousFinding = 0; }
            // Do not pass token to Task.Run: even an immediate pause must reach the first checkpoint.
            _autonomousTask = Task.Run(async () =>
            {
                var result = await new AutonomousResearch().RunAsync(source, profile, firmware, options, advisor, previous,
                    progress: progress => _autonomousUpdates.Enqueue((generation, null, progress)),
                    checkpoint: work =>
                    {
                        store.Save(work);
                        // The UI never reads the worker's mutable finding list.
                        _autonomousUpdates.Enqueue((generation, JsonSerializer.Serialize(work, GameProfile.Json), null));
                    }, ct: token).ConfigureAwait(false);
                return CopyResearch(result);
            });
        }
        catch (Exception error) when (error is AiException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { _autonomousMessage = UiText.Get("No se inició el análisis: ") + error.Message; }
    }

    private void PollAutonomousAnalysis()
    {
        while (_autonomousUpdates.TryDequeue(out var update))
        {
            if (update.Generation != _autonomousGeneration) continue;
            if (update.Json != null)
                _autonomousWork = JsonSerializer.Deserialize<AutonomousResearchWork>(update.Json, GameProfile.Json);
            if (update.Progress is { } progress)
            {
                if (_autonomousProgress?.Stage != progress.Stage) _autonomousStageStarted = Stopwatch.GetTimestamp();
                _autonomousProgress = progress;
            }
        }
        if (_autonomousTask is not { IsCompleted: true } task) return;
        try
        {
            _autonomousWork = task.GetAwaiter().GetResult();
            _autonomousMessage = "";
            RefreshAutonomousHistory();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { _autonomousMessage = UiText.Get("El análisis se detuvo: ") + error.Message + UiText.Get(". Se conserva el último checkpoint que pudo guardarse."); }
        finally { _autonomousTask = null; _autonomousCts?.Dispose(); _autonomousCts = null; }
        if (_analysisAutotestSection.Length > 0) _analysisAutotestScrollFrames = 8;
    }

    private void DrawAutonomousAnalysis(IMachine machine)
    {
        if (!GamePreparer.CanPrepare(machine.Id))
        { AiPicker.Hint("Esta máquina todavía no dispone de laboratorio automático. Podés usar la observación avanzada."); return; }
        LoadAutonomousContext();
        ApplyAnalysisAutotest();
        bool busy = _autonomousTask != null;
        UiTheme.Section("Investigación automática", "El laboratorio arranca el original, juega secuencias y contrasta escrituras sin que tengas que jugar primero.");
        AiPicker.Hint("La cantidad es un objetivo, no una garantía. Un efecto de escritura comprobado no confirma el propósito ni el nombre de una variable.");
        ImGui.BeginDisabled(busy);
        ImGui.TextWrapped(UiText.Get("Objetivo opcional"));
        ImGui.InputTextMultiline("##objetivo-automatico", ref _autonomousGoal, 4000, new Vector2(-1, UiTheme.Px(62)));
        ResearchInt("Cantidad deseada de efectos comprobados", "objetivo-efectos", ref _autonomousTarget, 1, 64);
        ScrollAnalysisAutotest("budget");
        if (_analysisAutotestSection == "budget") ImGui.SetNextItemOpen(true, ImGuiCond.Once);
        if (ImGui.CollapsingHeader(UiText.Label("Presupuesto de IA")))
        {
            ResearchInt("Minutos por ejecución", "minutos-automaticos", ref _autonomousMinutes, 1, 60);
            ResearchInt("Máximo de pedidos de IA por ejecución", "pedidos-automaticos", ref _autonomousCalls, 0, 100);
            AiPicker.Hint("Al continuar, este presupuesto se agrega al trabajo anterior. 0 pedidos ejecuta solo pruebas locales.");
            AiPicker.Choose(_ai, "investigacion-automatica", "Cada consejo de juego, propuesta de experimento o pedido de nombres consume un pedido del presupuesto.");
            int textLimit = _ai.TextLimitThousands;
            if (ResearchInt("Tope de texto por ejecución (miles de caracteres)", "texto-automatico", ref textLimit, 0, 10000, 10))
            { _ai.TextLimitThousands = Math.Clamp(textLimit, 0, 10000); _ai.Save(); }
            AiPicker.Hint("0 deja el texto sin tope. El límite cuenta pedidos completos y no los recorta.");
            ResearchInt("Máximo de rondas por ejecución", "rondas-automaticas", ref _autonomousPasses, 1, 24);
            ResearchInt("Cuadros de observación por ronda", "cuadros-automaticos", ref _autonomousFrames, 60, 9000, 60);
        }
        if (_autonomousCalls > 0 && !AiPicker.IsApi(_ai.Provider))
            AiPicker.Hint("La conexión elegida es manual: este análisis ejecutará pruebas locales sin pedidos de IA. Elegí un CLI o una conexión automática para recibir consejos.");
        else AiPicker.Hint(_autonomousCalls == 0 ? "Pruebas locales sin pedidos de IA." : UiText.Format($"Conexión: {AiPicker.ShortName(_ai.Provider)} · hasta {_autonomousCalls} pedidos en esta ejecución."));
        ImGui.BeginDisabled(_autonomousStore == null);
        if (AnalysisButton(_autonomousWork == null ? "Iniciar investigación" : "Iniciar otro análisis", true)) BeginAutonomousAnalysis(false);
        if (_autonomousWork != null)
        {
            int extraCalls = AiPicker.IsApi(_ai.Provider) ? _autonomousCalls : 0;
            AiPicker.Hint(UiText.Format($"Al continuar se agregan {_autonomousMinutes} minutos y hasta {extraCalls} pedidos de IA."));
            if (AnalysisButton("Continuar análisis")) BeginAutonomousAnalysis(true);
        }
        ImGui.EndDisabled(); ImGui.EndDisabled();
        if (busy)
        {
            bool stopping = _autonomousCts?.IsCancellationRequested == true;
            ImGui.BeginDisabled(stopping);
            if (AnalysisButton("Pausar análisis")) _autonomousCts?.Cancel();
            ImGui.EndDisabled();
            AiPicker.Hint(stopping ? "Deteniendo la prueba en curso y guardando lo completado. Podés continuar después."
                : "Pausar conserva el progreso completado para continuar después.");
        }
        if (_autonomousMessage.Length > 0) ImGui.TextWrapped(UiText.Get(_autonomousMessage));
        if (_autonomousStoreProblem.Length > 0) ImGui.TextWrapped(UiText.Get(_autonomousStoreProblem));
        DrawAutonomousProgress(busy);
        DrawAutonomousHistory(busy);
        DrawAutonomousStorage(busy || _retiredWork.Any(task => !task.IsCompleted));
        DrawAutonomousFindings(machine);
    }

    private static bool ResearchInt(string label, string id, ref int value, int minimum, int maximum, int step = 1)
    {
        ImGui.TextWrapped(UiText.Get(label));
        ImGui.SetNextItemWidth(Math.Min(UiTheme.Px(160), ImGui.GetContentRegionAvail().X));
        bool changed = ImGui.InputInt(UiText.Label("##" + id), ref value, step);
        if (changed) value = Math.Clamp(value, minimum, maximum);
        return changed;
    }

    private static string ResearchDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds >= TimeSpan.MaxValue.TotalSeconds - 1) return "sin tiempo válido";
        var duration = TimeSpan.FromSeconds(seconds);
        return UiText.Format($"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}");
    }

    private void DrawAutonomousProgress(bool busy)
    {
        var progress = _autonomousProgress;
        var work = _autonomousWork;
        double seconds = busy ? _autonomousPriorSeconds + Stopwatch.GetElapsedTime(_autonomousStarted).TotalSeconds : work?.ElapsedSeconds ?? 0;
        ScrollAnalysisAutotest("progress");
        UiTheme.Section("Estado y evidencia");
        if (busy) RetroArt.Activity();
        ImGui.TextWrapped(UiText.Get(busy ? UiText.Format($"Etapa: {progress?.Stage ?? UiText.Get("Inicio")} · en esta etapa {ResearchDuration(Stopwatch.GetElapsedTime(_autonomousStageStarted).TotalSeconds)}")
            : work == null ? "Listo para empezar." : UiText.Get("Estado: ") + ResearchStatus(work.Status)));
        ImGui.TextWrapped(UiText.Get(UiText.Format($"Tiempo acumulado: {ResearchDuration(seconds)} · rondas completadas: {progress?.Pass ?? work?.CompletedPasses ?? 0}")));
        ImGui.TextWrapped(UiText.Get(UiText.Format($"Candidatos: {progress?.Candidates ?? work?.Candidates ?? 0} · efectos de escritura comprobados: {progress?.VerifiedWrites ?? work?.VerifiedWrites ?? 0} · pedidos de IA acumulados: {progress?.AiCalls ?? work?.AiCallsUsed ?? 0}")));
        if (!busy && work?.StopReason is { Length: > 0 } reason) ImGui.TextWrapped(UiText.Get(reason));
        if (busy && progress?.Note is { Length: > 0 } note) ImGui.TextWrapped(UiText.Get(note));
        if (_autonomousAi != null && ImGui.CollapsingHeader(UiText.Label("Uso de IA de esta ejecución")))
            AiBuildWindow.UsageDetails(_autonomousAi.Stats);
        if (work != null && (work.Checked.Count > 0 || work.Evidence.CheckedControls.Count > 0 || work.Evidence.Notes.Count > 0)
            && ImGui.CollapsingHeader(UiText.Label("Pruebas de arranque y entradas")))
        {
            foreach (string item in work.Checked) ImGui.TextWrapped(UiText.Get("• " + item));
            foreach (var control in work.Evidence.CheckedControls) ImGui.TextWrapped(UiText.Get(control.Key + ": " + control.Value));
            foreach (string observation in work.Evidence.Notes) ImGui.TextWrapped(UiText.Get("• " + observation));
        }
        AiPicker.Hint("Las pruebas cambian RAM temporalmente en máquinas de laboratorio. Los archivos del juego no se parchean. Cada ronda completada queda guardada junto al original.");
    }

    private static string ResearchStatus(string status) => status switch
    { "running" => "En curso", "paused" => "En pausa", "limit" => "Presupuesto agotado", "complete" => "Rondas terminadas", "error" => "Requiere revisión", _ => "Preparado" };

    private void DrawAutonomousHistory(bool busy)
    {
        if (_autonomousSaved.Count == 0 || !ImGui.CollapsingHeader(UiText.Label("Análisis guardados"))) return;
        ImGui.BeginDisabled(busy);
        foreach (var work in _autonomousSaved)
        {
            ImGui.PushID(work.Id);
            if (ImGui.Selectable(UiText.Label(ResearchStatus(work.Status) + UiText.Get("##guardado")), _autonomousWork?.Id == work.Id)) SelectAutonomousWork(work);
            ImGui.TextWrapped(UiText.Get(UiText.Format($"{work.Candidates} candidatos · {work.VerifiedWrites} efectos · {ResearchDuration(work.ElapsedSeconds)}")));
            ImGui.PopID();
        }
        ImGui.EndDisabled();
        AiPicker.Hint("Abrir un análisis muestra sus resultados sin ejecutar pruebas ni gastar IA.");
    }

    private bool DiscardAutonomousWork(string id)
    {
        // Retired workers may still be saving their final checkpoint after a game change.
        if (_autonomousTask != null || _retiredWork.Any(task => !task.IsCompleted) || _autonomousStore == null) return false;
        if (!_autonomousFolderSaved.Any(work => work.Id == id))
        { _autonomousMessage = "El análisis elegido ya no está en la lista de guardados. Actualizá la carpeta antes de descartarlo."; return false; }
        try
        {
            _autonomousStore.Remove(id);
            if (_autonomousWork?.Id == id)
            {
                _autonomousWork = null; _autonomousProgress = null; _autonomousAi = null;
                _autonomousFinding = 0; _autonomousGoal = "";
            }
            _autonomousDiscardId = null; _autonomousDiscardOpen = false;
            RefreshAutonomousHistory();
            _autonomousMessage = "Se descartó el progreso guardado del análisis elegido.";
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { _autonomousMessage = UiText.Get("No se pudo descartar el análisis: ") + error.Message; return false; }
    }

    private void DrawAutonomousStorage(bool busy)
    {
        const string title = "Descartar análisis guardado";
        bool requestConfirmation = false;
        if (_autonomousStore != null && ImGui.CollapsingHeader(UiText.Label("Administrar guardados")))
        {
            AiPicker.Hint(UiText.Format($"Hasta 12 análisis por carpeta de originales. Hay {_autonomousFolderSaved.Count} guardados legibles. Descartar requiere elegir un análisis y confirmar."));
            ImGui.BeginDisabled(busy);
            if (ImGui.SmallButton(UiText.Label("Actualizar lista")))
            {
                try { RefreshAutonomousHistory(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                { _autonomousMessage = UiText.Get("No se pudo actualizar la lista: ") + error.Message; }
            }
            foreach (var work in _autonomousFolderSaved)
            {
                ImGui.PushID(work.Id);
                string name = work.Game.Length > 100 ? work.Game[..97] + "..." : work.Game;
                ImGui.TextWrapped(UiText.Get(name.Length == 0 ? "Análisis sin nombre" : name));
                ImGui.TextWrapped(UiText.Get(UiText.Format($"{ResearchStatus(work.Status)} · {work.Candidates} candidatos · {ResearchDuration(work.ElapsedSeconds)}")));
                using (UiTheme.FixedWidth()) ImGui.TextWrapped(UiText.Get(UiText.Get("ID ") + work.Id[..8] + UiText.Get(" · edición ") + work.FileSha256[..12]));
                if (ImGui.SmallButton(UiText.Label("Descartar progreso…")))
                {
                    _autonomousDiscardId = work.Id; _autonomousDiscardOpen = true;
                    requestConfirmation = true;
                }
                ImGui.Spacing(); ImGui.PopID();
            }
            ImGui.EndDisabled();
            if (busy) AiPicker.Hint("La gestión se habilita al terminar de guardar el trabajo en curso.");
        }
        if (requestConfirmation) ImGui.OpenPopup(UiText.Label(title));
        float width = Math.Max(1, Math.Min(UiTheme.Px(450), ImGui.GetIO().DisplaySize.X - UiTheme.Px(32)));
        ImGui.SetNextWindowSize(new Vector2(width, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(UiText.Label(title), ref _autonomousDiscardOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking)) return;
        var selected = _autonomousFolderSaved.FirstOrDefault(work => work.Id == _autonomousDiscardId);
        if (selected == null)
        {
            _autonomousDiscardOpen = false; _autonomousDiscardId = null;
            ImGui.CloseCurrentPopup();
        }
        else
        {
            string name = selected.Game.Length > 160 ? selected.Game[..157] + "..." : selected.Game;
            ImGui.TextWrapped(UiText.Get(UiText.Get("¿Descartar el progreso guardado de «") + name + "»?"));
            ImGui.TextWrapped(UiText.Get(UiText.Format($"Se eliminará este análisis: {selected.CompletedPasses} rondas, {selected.Candidates} candidatos y {selected.VerifiedWrites} efectos comprobados.")));
            if (_autonomousMessage.StartsWith("No se pudo descartar", StringComparison.Ordinal)) ImGui.TextWrapped(UiText.Get(_autonomousMessage));
            ImGui.BeginDisabled(busy);
            if (ImGui.Button(UiText.Label("Descartar análisis")) && DiscardAutonomousWork(selected.Id)) ImGui.CloseCurrentPopup();
            ImGui.EndDisabled();
            UiTheme.SameLineIfFits("Conservar");
            if (ImGui.Button(UiText.Label("Conservar")))
            {
                _autonomousDiscardOpen = false; _autonomousDiscardId = null;
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.EndPopup();
    }

    private void DrawAutonomousFindings(IMachine machine)
    {
        if (_autonomousWork?.Evidence.Findings is not { Count: > 0 } findings) return;
        if (_analysisAutotestSection == "verification")
        { int verified = findings.FindIndex(f => f.Verification != null); if (verified >= 0) _autonomousFinding = verified; }
        ScrollAnalysisAutotest("findings");
        UiTheme.Section("Candidatos encontrados", "El tipo y el propósito propuestos siguen siendo hipótesis, incluso cuando se comprobó un efecto al escribir.");
        string[] headers = ["Dirección", "Tipo propuesto / formato", "Bytes", "Resultado"];
        float tableWidth = headers.Sum(label => ImGui.CalcTextSize(UiText.Get(label)).X + ImGui.GetStyle().CellPadding.X * 2) + UiTheme.Px(100);
        if (ImGui.GetContentRegionAvail().X < tableWidth)
        {
            ImGui.TextWrapped(UiText.Get("Elegí un candidato"));
            ImGui.SetNextItemWidth(-1);
            string[] addresses = findings.Select(finding => "$" + finding.Address).ToArray();
            ImGui.Combo("##candidato-automatico", ref _autonomousFinding, addresses, addresses.Length);
        }
        else if (ImGui.BeginTable("hallazgos-automaticos", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(0, UiTheme.Px(190))))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            float addressWidth = ImGui.CalcTextSize(UiText.Get("Dirección")).X;
            using (UiTheme.FixedWidth()) addressWidth = Math.Max(addressWidth, ImGui.CalcTextSize(UiText.Get("$FFFFF")).X);
            ImGui.TableSetupColumn(UiText.Get("Dirección"), ImGuiTableColumnFlags.WidthFixed, addressWidth);
            ImGui.TableSetupColumn(UiText.Get("Tipo propuesto / formato"), ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableSetupColumn(UiText.Get("Bytes"), ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(UiText.Get("Bytes")).X);
            ImGui.TableSetupColumn(UiText.Get("Resultado"), ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableHeadersRow();
            for (int index = 0; index < findings.Count; index++)
            {
                var finding = findings[index];
                ImGui.PushID(index); ImGui.TableNextRow(); ImGui.TableNextColumn();
                using (UiTheme.FixedWidth())
                    if (ImGui.Selectable(UiText.Label("$" + finding.Address), index == _autonomousFinding, ImGuiSelectableFlags.SpanAllColumns)) _autonomousFinding = index;
                ImGui.TableNextColumn(); ImGui.TextWrapped(UiText.Get(finding.Kind + " / " + finding.Encoding));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(UiText.Get(finding.Bytes.ToString(CultureInfo.InvariantCulture)));
                ImGui.TableNextColumn(); ImGui.TextWrapped(UiText.Get(FindingStatus(finding.Status))); ImGui.PopID();
            }
            ImGui.EndTable();
        }
        var selected = findings[Math.Clamp(_autonomousFinding, 0, findings.Count - 1)];
        using (UiTheme.FixedWidth()) ImGui.TextWrapped(UiText.Get("$" + selected.Address + " · " + selected.Encoding + " · " + selected.Bytes + UiText.Get(" bytes")));
        ImGui.TextWrapped(UiText.Get(UiText.Get("Tipo propuesto: ") + selected.Kind + " · " + FindingStatus(selected.Status)));
        ImGui.TextWrapped(UiText.Get(UiText.Get("Hipótesis de propósito: ") + (!string.IsNullOrWhiteSpace(selected.Hypothesis) ? selected.Hypothesis : "Todavía no se estableció.")));
        foreach (string evidence in (selected.Evidence ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Take(24)) ImGui.TextWrapped(UiText.Get("• " + evidence));
        if (selected.Verification is { } verification)
        {
            ImGui.TextWrapped(UiText.Get(UiText.Get("Experimento: ") + verification.Experiment));
            ScrollAnalysisAutotest("verification");
            ImGui.TextWrapped(UiText.Get(UiText.Format($"Valor inicial: {verification.BeforeValue} · valor probado: {verification.TestValue}")));
            ImGui.TextWrapped(UiText.Get("Bytes por dirección: inicial → probado"));
            var changes = ResearchByteChanges(selected.Address, verification.BeforeHex, verification.TestHex);
            if (changes.Count == 0) AiPicker.Hint("No se guardaron los bytes hexadecimales de esta prueba.");
            using (UiTheme.FixedWidth())
                foreach (string change in changes) ImGui.TextWrapped(UiText.Get(change));
            ImGui.TextWrapped(UiText.Get(UiText.Format($"{verification.Frames} cuadros comparados · hasta {verification.PixelsChanged} píxeles distintos.")));
            ImGui.TextWrapped(UiText.Get(UiText.Format($"RAM distinta frente al control: {verification.ChangedRamBytes} bytes.")));
            AiPicker.Hint("Incluye el dato probado y consecuencias de la escritura. Por sí sola no demuestra el propósito de la variable.");
            using (UiTheme.FixedWidth())
                foreach (string sample in (verification.ChangedRamSample ?? []).Take(8)) ImGui.TextWrapped(UiText.Get(sample));
            foreach (string observation in (verification.Observations ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Take(24)) ImGui.TextWrapped(UiText.Get("• " + observation));
        }
        if (!string.IsNullOrWhiteSpace(selected.HowToVerify)) ImGui.TextWrapped(UiText.Get(UiText.Get("Para contrastar el propósito: ") + selected.HowToVerify));
        if (selected.Writers is { Count: > 0 }) ImGui.TextWrapped(UiText.Get(UiText.Get("Escrituras observadas desde: ") + string.Join(", ", selected.Writers.Where(s => !string.IsNullOrWhiteSpace(s)).Take(16))));
        var (start, end) = RamRange(machine);
        bool addressValid = uint.TryParse(selected.Address, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address) && address >= start && address <= end;
        ImGui.BeginDisabled(!addressValid);
        AiPicker.Hint("Abrí el buscador para contrastar esta hipótesis con la partida actual.");
        if (ImGui.Button(UiText.Label("Abrir buscador")))
        {
            _search = new MemorySearch(machine.Memory); _search.Start(start, end);
            _searchCondition = 4; _searchValue = machine.Memory.Peek(address);
            _newVarName = ""; _newVarDescription = UiText.Get("Hipótesis a contrastar: ") + selected.Hypothesis;
            _requestedPage = ToolPage.Search;
            _status = UiText.Format($"Candidato ${address:X4}. El buscador toma la RAM de esta partida: filtrá por su valor, jugá la acción indicada y compará. El resultado de laboratorio no se aplica automáticamente.");
        }
        ImGui.EndDisabled();
        AiPicker.Hint("Para probar un cambio en tu partida, contrastá la dirección y el formato, agregá la variable desde el buscador y revisá sus bytes en Cambios. Aplicar modifica RAM temporalmente; Deshacer o Restaurar partida recuperan el estado previo.");
    }

    internal static IReadOnlyList<string> ResearchByteChanges(string address, string? before, string? tested)
    {
        string initial = new((before ?? "").Where(c => !char.IsWhiteSpace(c)).Take(128).ToArray());
        string changed = new((tested ?? "").Where(c => !char.IsWhiteSpace(c)).Take(128).ToArray());
        int count = (Math.Max(initial.Length, changed.Length) + 1) / 2;
        bool validAddress = uint.TryParse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint first);
        return Enumerable.Range(0, count).Select(index =>
            (validAddress ? "$" + ((ulong)first + (uint)index).ToString("X4", CultureInfo.InvariantCulture) : "+" + index.ToString("X4", CultureInfo.InvariantCulture))
            + ": " + ByteAt(initial, index) + " → " + ByteAt(changed, index)).ToArray();

        static string ByteAt(string hex, int index)
        {
            int offset = index * 2;
            return offset + 1 < hex.Length && char.IsAsciiHexDigit(hex[offset]) && char.IsAsciiHexDigit(hex[offset + 1])
                ? hex.Substring(offset, 2).ToUpperInvariant() : "??";
        }
    }

    private static string FindingStatus(string status) => status switch
    { "verified" => "Efecto de escritura comprobado", "refuted" => "Refutado por la prueba", "inconclusive" => "Sin concluir", _ => "Hipótesis" };

    internal readonly record struct VariableBytePreview(uint Address, byte Before, byte After);

    private sealed class PreviewMemory(IMemoryView source, IEnumerable<VariableCell> cells) : IMemoryView
    {
        public readonly Dictionary<uint, byte> Bytes = cells.Select(c => c.Address).Distinct().ToDictionary(a => a, source.Peek);
        public int AddressBits => source.AddressBits;
        public byte Peek(uint address) => Bytes.TryGetValue(address, out byte value) ? value : throw new InvalidOperationException("La escritura sale de las celdas de la variable.");
        public void Poke(uint address, byte value)
        {
            if (!Bytes.ContainsKey(address)) throw new InvalidOperationException("La escritura sale de las celdas de la variable.");
            Bytes[address] = value;
        }
    }

    internal static IReadOnlyList<VariableBytePreview> PreviewVariable(GameProfile profile, string name, int requested, IMemoryView memory)
    {
        GameModifications.Validate(profile, [new(name, requested, false)], memory.AddressBits);
        var variable = profile.Variables[name];
        var shadow = new PreviewMemory(memory, VariableMemory.Cells(variable, memory.AddressBits));
        var before = new Dictionary<uint, byte>(shadow.Bytes);
        variable.Write(shadow, requested);
        return before.Select(cell => new VariableBytePreview(cell.Key, cell.Value, shadow.Bytes[cell.Key])).ToArray();
    }
}
