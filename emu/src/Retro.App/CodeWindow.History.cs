using ImGuiNET;
using Retro.Ai;
using Retro.Fusion;

namespace Retro.App;

public sealed partial class CodeWindow
{
    private readonly ModificationHistory _history;
    private string? _historyKey;
    private bool _rememberModifications, _reapplyModifications;
    private string _historyProblem = "";
    private readonly List<SavedModificationReport> _reports = new();
    private IReadOnlyList<VariableEdit> _rememberedChanges = [];
    private bool _savedReport;

    private void DrawHistoryOptions()
    {
        if (_host.Fusion != null || _historyKey == null) return;
        bool remember = _rememberModifications;
        if (ImGui.Checkbox("Conservar análisis y cambios de este juego", ref remember)) SetRememberModifications(remember);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Guarda los hallazgos y hasta doce informes para esta edición del juego. Desmarcar borra su historial guardado.");
        if (_rememberModifications)
        {
            bool reapply = _reapplyModifications;
            if (ImGui.Checkbox("Reaplicar cambios al reiniciar y volver a jugar", ref reapply)) SetReapplyModifications(reapply);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Vuelve a escribir los valores elegidos y mantiene los congelados. Puede afectar el arranque. Desmarcar libera los congelados actuales.");
        }
        if (_historyProblem.Length > 0)
        {
            ImGui.TextWrapped(_historyProblem);
            if (ImGui.SmallButton("Borrar historial guardado")) SetRememberModifications(false);
        }
    }

    private void SetRememberModifications(bool remember)
    {
        if (_historyKey == null) return;
        if (!remember)
        {
            if (!_history.Delete(_historyKey)) { _historyProblem = _history.Problem ?? "No se pudo borrar el historial."; return; }
            _rememberModifications = _reapplyModifications = false;
            _historyProblem = "";
            _status = "Historial guardado borrado. Los hallazgos y cambios actuales quedan solo en esta sesión.";
        }
        else
        {
            _rememberModifications = true;
            _rememberedChanges = _modifications?.Selection.ToArray() ?? [];
            PersistModifications();
            if (_historyProblem.Length == 0) _status = "Se conservarán los análisis, las variables y los cambios de este juego.";
        }
    }

    private void SetReapplyModifications(bool reapply)
    {
        _reapplyModifications = reapply;
        if (!reapply) _modifications?.ReleaseAll();
        PersistModifications();
        _status = reapply ? "Los cambios elegidos se reaplicarán al reiniciar o abrir esta edición."
            : "Reaplicación desactivada y congelados liberados. Se conservan los hallazgos y propuestas.";
    }

    private void RememberSelection()
    {
        _rememberedChanges = _modifications?.Selection.ToArray() ?? [];
        PersistModifications();
    }

    private void PersistModifications()
    {
        if (!_rememberModifications || _historyKey == null || _machine == null || _host.Fusion != null || _mediaRevision != _host.MediaRevision) return;
        var saved = new SavedModifications(1, _historyKey, _machine.Id,
            _profile?.Variables ?? new(), new(_discoveredLabels), _reports.ToArray(), _rememberedChanges, _reapplyModifications);
        _historyProblem = _history.Write(saved, _machine.Memory.AddressBits) ? "" : _history.Problem ?? "No se pudo guardar el historial.";
    }

    private void LeaveModificationSession()
    {
        PersistModifications();
        // Detach before reset/bootstrap; the new session can then restore its own selection.
        _modifications?.Dispose();
        _proposalFeedback.Clear();
    }

    private void LoadModificationHistory()
    {
        _historyKey = _host.Fusion == null && _machine!.Memory.AddressBits == 16 ? ModificationHistory.Key(_machine!.Id, _host.LoadedMediaHashes, _host.FirmwareIdentity) : null;
        _rememberModifications = _reapplyModifications = _savedReport = false;
        _historyProblem = ""; _reports.Clear(); _rememberedChanges = [];
        if (_historyKey == null) return;
        var saved = _history.Read(_historyKey, _machine!.Id, _machine.Memory.AddressBits);
        _historyProblem = _history.Problem ?? "";
        if (saved == null) return;
        try
        {
            var profile = _profile == null ? new GameProfile
            {
                Id = _gameId, Name = _gameName, Machine = _machine.Id, Boot = DefaultBoot(_machine),
                Match = new ProfileMatch { Sha256 = _host.LoadedMediaHashes.ToList() },
                Description = "Hallazgos guardados para esta edición. Las hipótesis requieren comprobarse."
            } : GameProfile.Parse(_profile.ToJson());
            var labels = new Dictionary<string, string>();
            foreach (var (name, variable) in saved.Variables)
            {
                if (profile.Variables.TryGetValue(name, out var known))
                {
                    if (known.Encoding != variable.Encoding || known.Bytes != variable.Bytes || known.Min != variable.Min || known.Max != variable.Max ||
                        known.ReadOnly != variable.ReadOnly || !VariableMemory.Cells(known).SequenceEqual(VariableMemory.Cells(variable)))
                        throw new InvalidDataException();
                }
                else
                {
                    var cells = VariableMemory.Cells(variable);
                    if (profile.Variables.Values.SelectMany(v => VariableMemory.Cells(v)).Any(old => cells.Any(cell =>
                        VariableMemory.Canonical(_machine.Id, old.Address) == VariableMemory.Canonical(_machine.Id, cell.Address) && (old.Mask & cell.Mask) != 0)))
                        throw new InvalidDataException();
                    profile.Variables.Add(name, variable);
                    labels[name] = saved.Labels.GetValueOrDefault(name, name);
                }
            }
            _profile = profile;
            foreach (var label in labels) _discoveredLabels[label.Key] = label.Value;
            foreach (var label in saved.Labels) _discoveredLabels[label.Key] = label.Value;
            _modifications?.Dispose(); _modifications = new GameModifications(_machine, profile);
            _rememberModifications = true; _reapplyModifications = saved.Reapply;
            _rememberedChanges = saved.Changes;
            _reports.AddRange(saved.Reports);
            if (_reports.Count > 0) SelectSavedReport(_reports.Count - 1);
            _status = "Historial recuperado para esta edición del juego.";
            if (_reapplyModifications && _rememberedChanges.Count > 0) ApplyRememberedChanges();
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        { _historyProblem = "El historial no coincide con las variables del perfil actual. Se conserva el archivo, sin aplicar cambios."; }
    }

    private void SelectSavedReport(int index)
    {
        var report = _reports[index];
        _lastAnalysis = report.Analysis; _lastAnalysisProvider = report.Provider;
        _lastAnalysisAt = report.At; _lastAnalysisFrame = report.Frame;
        _modificationWish = report.Wish;
        _advice = new ModificationAdvice(report.Analysis, report.Ideas);
        _savedReport = true; _lastAnalysisAttempt = -1; _proposalFeedback.Clear();
    }

    private void RecordAnalysis()
    {
        _savedReport = false;
        _reports.Add(new(_lastAnalysisProvider, _lastAnalysisAt, _lastAnalysisFrame, _modificationWish, _lastAnalysis, _advice!.Ideas));
        if (_reports.Count > ModificationHistory.ReportLimit) _reports.RemoveAt(0);
        PersistModifications();
    }

    private void DrawAnalysisHistory()
    {
        if (_reports.Count == 0 || !ImGui.CollapsingHeader($"Historial de análisis ({_reports.Count})")) return;
        AiPicker.Hint(_rememberModifications ? "Guardado para esta edición. Elegir un informe no aplica sus cambios." : "Solo esta sesión. Activá Conservar análisis y cambios para recuperarlos después.");
        for (int index = _reports.Count - 1; index >= 0; index--)
        {
            var report = _reports[index];
            ImGui.PushID(index);
            if (ImGui.Selectable($"{report.At.ToLocalTime():dd/MM HH:mm} · {report.Provider} · cuadro {report.Frame}")) SelectSavedReport(index);
            ImGui.PopID();
        }
    }

    private void DrawRememberedChanges()
    {
        if (_rememberedChanges.Count == 0 || !ImGui.CollapsingHeader($"Cambios recordados ({_rememberedChanges.Count})")) return;
        foreach (var edit in _rememberedChanges)
            ImGui.TextWrapped($"{VariableLabel(edit.Variable)}: {edit.Value}" + (edit.Freeze ? " (congelado)" : ""));
        if (ImGui.Button("Aplicar cambios recordados")) ApplyRememberedChanges();
    }

    private void ApplyRememberedChanges()
    {
        try
        {
            _modifications!.ApplySelection(_rememberedChanges);
            _proposalFeedback.Clear();
            _status = "Cambios recordados aplicados. Podés deshacerlos como un único cambio.";
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or NotSupportedException)
        { _status = "No se reaplicaron los cambios: " + error.Message; }
    }
}