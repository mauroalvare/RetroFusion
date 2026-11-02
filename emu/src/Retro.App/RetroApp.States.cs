using System.Numerics;
using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private readonly StateSlotStore _stateSlots;
    private bool _stateWindowOpen;
    private (int Slot, string Key, long Revision)? _pendingStateSave;
    private long _stateListingRevision = -1;
    private string _stateKey = "", _stateMessage = "";
    private IReadOnlyList<StateSlotInfo> _savedSlots = [], _legacySlots = [];

    private void OpenStateWindow()
    {
        CloseEditors(); _home.Close(); _stateWindowOpen = true;
        _stateListingRevision = -1; _stateMessage = ""; _pendingStateSave = null;
    }

    private void RefreshStateSlots()
    {
        if (!_host.CanUseStates) return;
        string machine = _host.Machine!.Id;
        _stateKey = StateSlotStore.GameKey(machine, _host.LoadedMediaHashes, _host.FirmwareIdentity);
        _savedSlots = _stateSlots.List(_stateKey);
        _legacySlots = _stateSlots.ListLegacy(machine);
        _stateListingRevision = _host.MediaRevision;
    }

    private void DrawStateWindow()
    {
        if (!_stateWindowOpen) return;
        var size = WorkspaceLayout.Available();
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(UiTheme.Px(540, 300), size), size);
        ImGui.SetNextWindowSize(Vector2.Min(UiTheme.Px(650, 420), size), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin(UiText.Label("Partidas guardadas"), ref _stateWindowOpen)) { ImGui.End(); return; }
        WorkspaceLayout.KeepOnScreen();
        try
        {
            if (!_host.CanUseStates)
            { ImGui.TextWrapped(UiText.Get("Abrí un juego original para guardar o recuperar una partida.")); return; }
            if (_stateListingRevision != _host.MediaRevision) { RefreshStateSlots(); _stateMessage = ""; }
            bool compact = ImGui.GetContentRegionAvail().X < UiTheme.Px(520);
            ImGui.TextWrapped(UiText.Get(_host.LoadedMedia ?? _host.Info?.Name ?? _host.Machine!.DisplayName));
            if (!compact) UiTheme.Hint("Cada juego y edición tiene sus propias ranuras.");
            else if (ImGui.IsItemHovered()) UiTheme.Tooltip("Cada juego y edición tiene sus propias ranuras.");
            ImGui.Separator();
            if (compact)
            {
                foreach (var slot in _savedSlots)
                {
                    ImGui.PushID(slot.Number);
                    ImGui.TextUnformatted(UiText.Get(UiText.Format($"Ranura {slot.Number}")));
                    string detail = slot.Problem ?? slot.SavedAt?.ToString("dd/MM/yyyy HH:mm") ?? "Vacía";
                    UiTheme.SameLineIfFits(detail); UiTheme.Hint(detail);
                    DrawStateSlotButtons(slot);
                    ImGui.Separator(); ImGui.PopID();
                }
            }
            else if (ImGui.BeginTable("slots", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            {
                float actionsWidth = ImGui.CalcTextSize(UiText.Get("Guardar") + UiText.Get("Cargar")).X + ImGui.GetStyle().FramePadding.X * 4 + ImGui.GetStyle().ItemSpacing.X;
                ImGui.TableSetupColumn(UiText.Get("Ranura"), ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(UiText.Get("Ranura")).X);
                ImGui.TableSetupColumn(UiText.Get("Guardada"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn(UiText.Get("Acciones"), ImGuiTableColumnFlags.WidthFixed, actionsWidth);
                ImGui.TableHeadersRow();
                foreach (var slot in _savedSlots)
                {
                    ImGui.PushID(slot.Number); ImGui.TableNextRow(); ImGui.TableNextColumn();
                    ImGui.TextUnformatted(UiText.Get(slot.Number.ToString())); ImGui.TableNextColumn();
                    ImGui.TextWrapped(UiText.Get(slot.Problem ?? slot.SavedAt?.ToString("dd/MM/yyyy HH:mm") ?? "Vacía"));
                    ImGui.TableNextColumn();
                    DrawStateSlotButtons(slot); ImGui.PopID();
                }
                ImGui.EndTable();
            }
            DrawStateSaveConfirmation();
            if (_stateMessage.Length > 0) ImGui.TextWrapped(UiText.Get(_stateMessage));
            ImGui.Spacing();
            if (_legacySlots.Any(s => s.SavedAt != null) && ImGui.CollapsingHeader(UiText.Label("Guardados de versiones anteriores")))
            {
                ImGui.TextWrapped(UiText.Get("Estos archivos se compartían entre juegos. Cargalos solo si corresponden a esta partida."));
                foreach (var slot in _legacySlots.Where(s => s.SavedAt != null))
                {
                    if (ImGui.SmallButton(UiText.Label(UiText.Format($"Cargar anterior {slot.Number}"))))
                    {
                        TryStateAction(() =>
                        {
                            _host.LoadStateFromFile(_stateSlots.LegacyPath(_host.Machine!.Id, slot.Number));
                            _stateMessage = "Guardado anterior cargado. Podés conservarlo en una ranura de este juego.";
                        });
                        RefreshStateSlots();
                    }
                    ImGui.SameLine(); ImGui.TextDisabled(UiText.Get(slot.SavedAt!.Value.ToString("dd/MM/yyyy HH:mm")));
                }
            }
            ImGui.Separator();
            ImGui.TextWrapped(UiText.Get("Las ranuras se conservan entre sesiones. El guardado rápido es temporal."));
            if (ImGui.SmallButton(UiText.Label("Actualizar lista"))) RefreshStateSlots();
        }
        finally { ImGui.End(); }
    }

    private void DrawStateSlotButtons(StateSlotInfo slot)
    {
        if (ImGui.SmallButton(UiText.Label("Guardar"))) RequestStateSlotSave(slot.Number);
        UiTheme.SameLineIfFits("Cargar"); ImGui.BeginDisabled(slot.SavedAt == null);
        if (ImGui.SmallButton(UiText.Label("Cargar"))) RunStateSlot(slot.Number, save: false);
        ImGui.EndDisabled();
    }

    private void RequestStateSlotSave(int slot)
    {
        if (!_host.CanUseStates) return;
        RefreshStateSlots();
        if (File.Exists(_stateSlots.PathFor(_stateKey, slot)))
            _pendingStateSave = (slot, _stateKey, _host.MediaRevision);
        else RunStateSlot(slot, save: true);
    }

    private void ConfirmStateSlotSave(bool replace)
    {
        var pending = _pendingStateSave; _pendingStateSave = null;
        if (!replace || pending == null || !_host.CanUseStates) return;
        RefreshStateSlots();
        if (pending.Value.Key != _stateKey || pending.Value.Revision != _host.MediaRevision)
        { _stateMessage = "La partida cambió. Elegí de nuevo dónde guardarla."; return; }
        RunStateSlot(pending.Value.Slot, save: true);
    }

    private void DrawStateSaveConfirmation()
    {
        if (_pendingStateSave is not { } pending) return;
        ImGui.OpenPopup(UiText.Label("Reemplazar partida guardada"));
        float width = Math.Min(UiTheme.Px(440), ImGui.GetMainViewport().WorkSize.X - 24);
        ImGui.SetNextWindowSizeConstraints(new(width, 0), new(width, ImGui.GetMainViewport().WorkSize.Y - 24));
        ImGui.SetNextWindowSize(new(width, 0), ImGuiCond.Appearing);
        bool open = true;
        if (ImGui.BeginPopupModal(UiText.Label("Reemplazar partida guardada"), ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped(UiText.Get(UiText.Format($"La ranura {pending.Slot} ya tiene una partida. ¿Querés reemplazarla por la actual?")));
            if (ImGui.Button(UiText.Label("Reemplazar"))) { ConfirmStateSlotSave(true); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button(UiText.Label("Cancelar"))) { ConfirmStateSlotSave(false); ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        if (!open) ConfirmStateSlotSave(false);
    }

    private void RunStateSlot(int slot, bool save)
    {
        // Recheck the current identity if another control changed the machine this frame.
        if (!_host.CanUseStates) return;
        RefreshStateSlots();
        TryStateAction(() =>
        {
            string path = _stateSlots.PathFor(_stateKey, slot);
            if (save) _host.SaveStateToFile(path); else _host.LoadStateFromFile(path);
            _stateMessage = UiText.Format($"Ranura {slot} {(save ? UiText.Get("guardada") : UiText.Get("cargada"))}.");
        });
        RefreshStateSlots();
    }
}
