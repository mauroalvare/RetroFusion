using System.Diagnostics;
using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private bool _requirementsOpen;
    private Task<bool>? _componentRepair;
    private CancellationTokenSource? _componentCancellation;
    private bool ComponentRepairActive => _componentRepair is { IsCompleted: false };
    private string? _requirementsProblem;

    private Task CancelComponentRepair()
    {
        _componentCancellation?.Cancel();
        return _componentRepair ?? Task.CompletedTask;
    }

    private void DrawRequirements()
    {
        if (_componentRepair?.IsCompleted == true)
        {
            if (_componentRepair.IsFaulted) _requirementsProblem = _componentRepair.Exception!.GetBaseException().Message;
            _componentRepair = null;
            _componentCancellation?.Dispose(); _componentCancellation = null;
        }
        if (!_requirementsOpen) return;
        var room = WorkspaceLayout.Available();
        ImGui.SetNextWindowSize(System.Numerics.Vector2.Min(UiTheme.Px(740, 580), room), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(System.Numerics.Vector2.Min(UiTheme.Px(360, 260), room), room);
        if (ImGui.Begin(UiText.Label("Requisitos###requirements"), ref _requirementsOpen, ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse))
        {
            WorkspaceLayout.KeepOnScreen();
            float footer = ImGui.GetFrameHeightWithSpacing() + UiTheme.Px(8);
            ImGui.BeginChild("requirements-body", new(0, -footer));
            foreach (var check in StartupRequirements.Inspect(AppContext.BaseDirectory, Raylib_cs.Raylib.IsWindowReady(), _audioDisabled))
            { ImGui.Bullet(); ImGui.TextWrapped(StartupRequirements.StatusName(check.Status) + " · " + check.Detail); }
            ImGui.Separator();
            UiTheme.Hint("No se instala nada automáticamente. Los archivos faltantes del paquete se reparan con una copia completa o una actualización.");
            if (OperatingSystem.IsWindows() && OfficialComponent.MissingVisualCpp(AppContext.BaseDirectory).Count > 0)
            {
                ImGui.BeginDisabled(_componentRepair != null);
                if (ImGui.Button(UiText.Label("Reparar componente oficial…")))
                {
                    _componentCancellation = new CancellationTokenSource();
                    var token = _componentCancellation.Token;
                    using var process = Process.GetCurrentProcess();
                    nint owner = process.MainWindowHandle;
                    _componentRepair = Task.Run(() => StartupRecovery.RepairRequirementsAsync(AppContext.BaseDirectory, owner, token));
                }
                InterfaceControlBounds?.Invoke("Reparar componente oficial", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                ImGui.EndDisabled();
            }
            if (_componentRepair != null) UiTheme.Hint("Esperando la decisión o el instalador oficial. Revisá los diálogos de Windows.");
            if (_requirementsProblem != null) ImGui.TextWrapped(_requirementsProblem);
            UiTheme.Hint("Los controladores de video y audio se obtienen desde el sistema operativo o el fabricante del equipo. Un aviso de audio no demuestra que falte un instalador.");
            if (ImGui.Button(UiText.Label("Abrir guía oficial del sistema…")))
            {
                try { Process.Start(new ProcessStartInfo((OperatingSystem.IsWindows() ? OfficialComponent.WindowsGuide : OfficialComponent.LinuxGuide).AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception) { _requirementsProblem = error.Message; }
            }
            InterfaceControlBounds?.Invoke("Guía oficial del sistema", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.EndChild(); ImGui.Separator();
            if (ImGui.Button(UiText.Label("Cerrar requisitos"))) _requirementsOpen = false;
            InterfaceControlBounds?.Invoke("Cerrar requisitos", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }
        ImGui.End();
    }
}
