using System.Numerics;
using ImGuiNET;
using Retro.Ai;
using Retro.Core;
using Retro.Fusion;

namespace Retro.App;

public sealed partial class CodeWindow
{
    private enum ToolPage { Changes, Analysis, Search, Code }
    private ToolPage _activePage = ToolPage.Changes;
    private ToolPage? _requestedPage;
    private float? _modificationPanelLeft;
    private long _mediaRevision = -1, _explainRevision, _modificationRevision;
    private IMachine? _modificationMachine, _explainMachine;
    private GameModifications? _modifications;
    private ModificationObservation? _observation;
    private GameAnalysisSnapshot? _analysisSnapshot;
    private ModificationAdvice? _advice;
    private Task<GameAnalysisResult>? _modificationTask;
    private CancellationTokenSource? _modificationCts;
    private readonly AiBuildWindow _modificationActivity = new("modificaciones", "Actividad de la IA", "Ver actividad");
    private readonly AiBuildWindow _routineActivity = new("rutinas", "Actividad de la IA", "Ver actividad");
    private string _modificationWish = "", _modificationPasted = "", _lastAnalysis = "";
    private bool _manualModification, _manualExplanation;
    private int _searchCondition;
    private readonly Dictionary<string, string> _variableInputs = new(), _discoveredLabels = new();
    private readonly Action? _openStudio;

    public void OpenModifications() { _host.RefreshLoadedProfile(); OpenPage(ToolPage.Changes); }
    public void OpenFinder() => OpenPage(ToolPage.Search);
    private void OpenPage(ToolPage page)
    {
        _open = true; _requestedPage = page;
        if (_host.Machine is { } machine) machine.ExecutedMap ??= new bool[65536];
    }
    public void Close() { _open = false; _modificationPanelLeft = null; StopObservation(); }
    public float RightReserve(float width) => _open && _modificationPanelLeft is float left && left >= 200 ? width - left : 0;

    private string VariableLabel(string name) => _discoveredLabels.GetValueOrDefault(name, name switch
    {
        "lives" => "Vidas", "air" => "Aire", "score" => "Puntaje", "energy" => "Energía",
        "timer" or "time" => "Tiempo", "x" => "Posición horizontal", "y" or "willyY" => "Altura",
        "airborne" => "Salto / caída", "cavern" or "level" => "Nivel", _ => name
    });

    private void StopObservation() { _observation?.Dispose(); _observation = null; }

    private GameProfile SessionProfile() => _profile ??= new GameProfile
    {
        Id = _gameId, Name = _gameName, Machine = _machine!.Id,
        Description = "Datos de esta sesión. Las hipótesis de IA no son variables verificadas.",
        Boot = DefaultBoot(_machine), Match = new ProfileMatch { Sha256 = _host.LoadedMediaHashes.ToList() }
    };

    private bool OriginalAvailable()
    {
        if (_host.Fusion != null)
        {
            ImGui.TextWrapped("Esta partida es una fusión. Para cambiar sus reglas, abrila en el estudio.");
            if (_openStudio != null && ImGui.Button("Editar la fusión")) { Close(); _openStudio(); }
            AiPicker.Hint("Código muestra el original enfocado para una revisión técnica.");
            return false;
        }
        if (_host.LoadedMedia != null) return true;
        ImGui.TextWrapped("Abrí un juego de la biblioteca para modificar su partida.");
        return false;
    }

    private void DrawModifications(IMachine machine)
    {
        if (!OriginalAvailable()) return;
        AiPicker.Hint("Enter aplica un valor. Congelar lo mantiene fijo antes de cada cuadro.");
        ImGui.BeginDisabled(_modifications?.CanUndo != true);
        if (ImGui.Button("Deshacer último cambio")) RestoreModifications(undo: true);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Restaura la partida antes del último cambio, incluido el progreso desde entonces. Conserva hasta cinco cambios.");
        ImGui.SameLine();
        ImGui.BeginDisabled(_modifications?.CanRestore != true);
        if (ImGui.Button("Restaurar partida")) RestoreModifications();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Vuelve al momento anterior al primer cambio de esta sesión.");
        if (_modifications?.Frozen.Count is > 0 && ImGui.SmallButton("Liberar valores congelados"))
        { _modifications.ReleaseAll(); _status = "Los valores vuelven a cambiar con el juego."; }
        ImGui.Separator();
        if (_profile?.Variables.Count is > 0) DrawVariableValues(machine, true);
        else ImGui.TextWrapped("Todavía no hay variables. Analizá la partida con IA o usá la búsqueda manual.");
        if (_discoveredLabels.Count > 0) AiPicker.Hint("Las variables marcadas como hipótesis requieren comprobar su efecto en la partida.");
        if (ImGui.Button("Analizar con IA")) _requestedPage = ToolPage.Analysis;
        if (_advice == null || _advice.Ideas.Count == 0) return;
        ImGui.Spacing(); ImGui.Separator();
        ImGui.TextColored(LabelColor, "Propuestas para probar");
        int id = 0;
        foreach (var idea in _advice.Ideas)
        {
            ImGui.PushID(id++);
            ImGui.TextColored(LabelColor, idea.Title);
            ImGui.TextWrapped(idea.Effect);
            foreach (var change in idea.Changes)
                ImGui.TextWrapped($"{VariableLabel(change.Variable)}: {change.Value}{(change.Freeze ? " (congelado)" : "")}");
            if (ImGui.Button("Aplicar propuesta")) ApplyModifications(idea.Changes);
            ImGui.Spacing(); ImGui.PopID();
        }
    }

    private void DrawModificationAnalysis(IMachine machine)
    {
        if (!OriginalAvailable()) return;
        AiPicker.Hint("Jugá unos segundos para reunir datos. El análisis propone cambios; aplicarlos queda a tu elección.");
        bool busy = _modificationTask != null;
        ImGui.BeginDisabled(busy);
        AiPicker.Choose(_ai, "analisis", "El análisis usa un pedido a la IA elegida.");
        ImGui.TextUnformatted("Qué querés investigar o cambiar:");
        ImGui.InputTextMultiline("##investigar", ref _modificationWish, 2000, new Vector2(-1, 64));
        bool callable = AiPicker.IsApi(_ai.Provider);
        if (ImGui.Button(callable ? (_lastAnalysis.Length > 0 ? "Investigar este pedido" : "Analizar la partida") : _ai.Provider == "agent" ? "Copiar pedido para mi agente" : "Copiar pedido para mi IA"))
        {
            try
            {
                if (callable) BeginModificationAnalysis(_ai.CreateProvider() ?? throw new AiException("Elegí una IA disponible."));
                else
                {
                    Update(); _analysisSnapshot = _observation!.Capture(_gameName, SessionProfile());
                    ImGui.SetClipboardText(GameModificationAnalyzer.Guide + "\n\n" +
                        _analysisSnapshot.Prompt(_modificationWish, _lastAnalysis) +
                        "\nRespondé solo JSON con este esquema:\n" + GameModificationAnalyzer.Schema().GetRawText());
                    _manualModification = true;
                    _status = "Pedido copiado. Pegá la respuesta para revisar los hallazgos.";
                }
            }
            catch (Exception error) when (error is AiException or InvalidOperationException or IOException or FormatException or OverflowException or ArgumentException)
            { _status = error.Message; }
        }
        ImGui.EndDisabled();
        if (busy) { ImGui.SameLine(); if (ImGui.Button("Cancelar análisis")) _modificationCts?.Cancel(); }
        AiPicker.Hint($"Observación local: {_observation?.WrittenAddresses ?? 0} direcciones escritas. No se modifica la partida.");
        _modificationActivity.Draw(busy, _status, _modificationCts);
        if (_manualModification && _analysisSnapshot != null)
        {
            ImGui.InputTextMultiline("##analisis-recibido", ref _modificationPasted, 32000, new Vector2(-1, 90));
            if (ImGui.Button("Revisar respuesta"))
                try { AcceptAnalysis(GameModificationAnalyzer.Parse(_modificationPasted, _analysisSnapshot)); }
                catch (AiException error) { _status = error.Message; }
        }
        if (_lastAnalysis.Length > 0)
        {
            ImGui.Separator(); ImGui.TextColored(LabelColor, _analysisSnapshot == null ? "Informe anterior" : "Informe");
            ImGui.TextWrapped(_lastAnalysis);
            if (ImGui.Button("Ver variables y propuestas")) _requestedPage = ToolPage.Changes;
        }
        if (_analysisSnapshot != null && ImGui.CollapsingHeader("Evidencia enviada"))
        {
            ImGui.TextDisabled($"Cuadro {_analysisSnapshot.Frame} · {_analysisSnapshot.Candidates.Count} bytes de RAM · recorte de código");
            ImGui.BeginChild("evidencia", new Vector2(0, 180), ImGuiChildFlags.Borders);
            foreach (var candidate in _analysisSnapshot.Candidates)
                ImGui.TextUnformatted($"0x{candidate.Address:X4} = {candidate.Value} · {candidate.Writes} escrituras" +
                    (candidate.Writer is uint pc ? $" · instrucción 0x{pc:X4}" : ""));
            ImGui.EndChild();
        }
    }

    private void BeginModificationAnalysis(IAiProvider provider)
    {
        Update();
        if (_machine == null || _host.Fusion != null || _modificationTask != null) return;
        _observation ??= new ModificationObservation(_machine);
        _analysisSnapshot = _observation.Capture(_gameName, SessionProfile());
        _modificationRevision = _mediaRevision; _modificationMachine = _machine;
        _modificationCts?.Dispose(); _modificationCts = new CancellationTokenSource();
        var token = _modificationCts.Token;
        string wish = _modificationWish, previous = _lastAnalysis;
        var snapshot = _analysisSnapshot;
        _status = $"{provider.Name} está analizando la captura de la partida...";
        _modificationActivity.Start(provider);
        _modificationTask = Task.Run(() => GameModificationAnalyzer.AnalyzeAsync(provider, snapshot, wish, previous, token));
    }

    private void AcceptAnalysis(GameAnalysisResult result)
    {
        var profile = SessionProfile();
        // Revalidate names before merging if the manual finder changed the session while the request ran.
        if (result.Variables.Any(variable => profile.Variables.ContainsKey(variable.Name) ||
            GameModificationAnalyzer.OverlapsKnownMemory(profile, variable.Address, variable.Bytes)))
            throw new AiException("Las variables cambiaron durante el análisis. Pedí una captura nueva.");
        foreach (var variable in result.Variables)
        {
            profile.Variables.Add(variable.Name, GameModificationAnalyzer.Definition(variable));
            _discoveredLabels[variable.Name] = variable.Label;
        }
        _modifications ??= new GameModifications(_machine!, profile);
        _lastAnalysis = result.Analysis; _advice = result.Advice;
        _manualModification = false; _modificationPasted = "";
        int variables = result.Variables.Count, proposals = result.Advice.Ideas.Count;
        _status = $"Análisis listo: {variables} variable{(variables == 1 ? "" : "s")} nueva{(variables == 1 ? "" : "s")} y {proposals} propuesta{(proposals == 1 ? "" : "s")}. Revisá Cambios.";
    }

    private void PollModificationAdvice()
    {
        if (_modificationTask is not { IsCompleted: true } task) return;
        _modificationTask = null; _modificationActivity.Stop();
        _modificationCts?.Dispose(); _modificationCts = null;
        if (_mediaRevision != _modificationRevision || !ReferenceEquals(_machine, _modificationMachine))
        { _ = task.Exception; return; }
        if (task.IsCanceled || task.Exception?.GetBaseException() is OperationCanceledException) _status = "Análisis cancelado.";
        else if (task.IsFaulted) _status = "No se pudo completar el análisis: " + task.Exception!.GetBaseException().Message;
        else
            try { AcceptAnalysis(task.Result); }
            catch (AiException error) { _status = error.Message; }
    }

    private void ApplyModifications(IReadOnlyList<VariableEdit> changes)
    {
        try
        {
            if (_modifications == null) throw new InvalidOperationException("Primero agregá variables para esta partida.");
            _modifications.Apply(changes);
            _status = "Cambios aplicados. Podés deshacer el último o restaurar la partida.";
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or NotSupportedException)
        { _status = "No se aplicó el cambio: " + error.Message; }
    }

    private void RestoreModifications(bool undo = false)
    {
        if (_host.Fusion != null || _modifications == null || !(undo ? _modifications.Undo() : _modifications.Restore())) return;
        _host.StateRestored();
        _mediaRevision = _host.MediaRevision; // Keep this session's definitions and remaining undo history.
        _cts?.Cancel(); _modificationCts?.Cancel();
        _map = null; _codeSummary = ""; _analysisSnapshot = null; _advice = null; _variableInputs.Clear();
        StopObservation(); _modificationActivity.Reset();
        _status = undo ? "Último cambio deshecho; partida y congelados anteriores restaurados."
            : "Partida restaurada al momento anterior al primer cambio.";
    }

    private void SaveProfile()
    {
        if (_profile == null) return;
        var persistent = GameProfile.Parse(_profile.ToJson());
        foreach (var name in _discoveredLabels.Keys) persistent.Variables.Remove(name);
        ProfileLibrary.Save(persistent);
    }

    public void Dispose()
    {
        StopObservation(); _host.BeforeFrame -= ApplyFrozen;
        _cts?.Cancel(); _modificationCts?.Cancel();
        _cts?.Dispose(); _modificationCts?.Dispose();
    }

    public Task CancelWork()
    {
        _cts?.Cancel(); _modificationCts?.Cancel();
        return Task.WhenAll(new Task?[] { _explainTask, _modificationTask }.OfType<Task>());
    }
}
