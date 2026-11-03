using System.Numerics;
using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private bool _batteryWindow, _batteryReplace, _quitBatteryPending;
    private string? _lastBatteryProblem;
    private string _batteryCopy = "", _batteryUiKey = "", _batteryMessage = "";

    private void OpenBatteryWindow()
    {
        CloseEditors(); _home.Close(); _host.Paused = true; _batteryWindow = true; _batteryReplace = false;
        ReleaseConsoleKeyboard();
    }

    private void DrawBatteryWindow()
    {
        if (!_batteryWindow) return;
        var size = WorkspaceLayout.Available();
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(UiTheme.Px(420, 250), size), size);
        ImGui.SetNextWindowSize(Vector2.Min(UiTheme.Px(670, 480), size), ImGuiCond.FirstUseEver);
        if (ImGui.Begin(UiText.Label("Batería del cartucho"), ref _batteryWindow, ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse))
        {
            WorkspaceLayout.KeepOnScreen();
            ImGui.BeginChild("battery-body", new(0, -(ImGui.GetFrameHeightWithSpacing() + UiTheme.Px(8))));
            InterfaceControlBounds?.Invoke("Contenido batería", ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
            if (_host.Battery is { } battery)
            {
                if (_batteryUiKey != battery.Path)
                {
                    _batteryUiKey = battery.Path; _batteryReplace = false; _batteryMessage = "";
                    _batteryCopy = Path.Combine(Path.GetDirectoryName(battery.Path)!, "recovery", Guid.NewGuid().ToString("N") + ".rfbattery");
                }
                ImGui.TextWrapped(_host.LoadedMedia ?? "NES");
                ImGui.TextWrapped(UiText.Get("La batería guarda lo que el juego escribe en el cartucho; no reemplaza un estado completo. Se conserva al reiniciar y se guarda cada dos segundos, al cambiar de juego y al salir."));
                ImGui.TextWrapped(UiText.Get("Archivo: ") + battery.Path);
                ImGui.TextWrapped(UiText.Get(battery.Pending ? "Hay cambios de batería en memoria." : "La batería no tiene cambios pendientes."));
                if (battery.Problem != null) ImGui.TextWrapped(UiText.Message(battery.Problem));
                if (BatteryButton("Guardar batería ahora")) BatteryAction(() =>
                {
                    if (!battery.Flush()) throw new IOException(battery.Problem);
                    return UiText.Get("Batería guardada.");
                });
                ImGui.Separator();
                ImGui.TextWrapped(UiText.Get("Ruta de la copia (.rfbattery)"));
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText("##battery-copy", ref _batteryCopy, 2048)) _batteryReplace = false;
                InterfaceControlBounds?.Invoke("Ruta batería", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                ImGui.TextWrapped(UiText.Get("Exportar no sobrescribe archivos."));
                if (BatteryButton("Exportar copia", "Exportar batería sin sobrescribir")) BatteryAction(() => UiText.Get("Copia de batería guardada: ") + battery.Export(_batteryCopy));
                ImGui.TextWrapped(UiText.Get("Recuperar conserva respaldos de la memoria y del archivo anteriores."));
                ImGui.Checkbox(UiText.Label("Confirmar recuperación"), ref _batteryReplace);
                InterfaceControlBounds?.Invoke("Confirmar recuperación de batería", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                ImGui.BeginDisabled(!_batteryReplace);
                if (BatteryButton("Recuperar copia", "Recuperar batería de la copia")) BatteryAction(() =>
                {
                    _host.RestoreBattery(_batteryCopy); _batteryReplace = false;
                    return UiText.Get("Batería recuperada. Los datos anteriores se conservaron en respaldos.");
                });
                ImGui.EndDisabled();
                if (battery.ExportedPath != null) ImGui.TextWrapped(UiText.Get("Copia de la memoria anterior: ") + battery.ExportedPath);
                if (battery.PreservedPath != null) ImGui.TextWrapped(UiText.Get("Archivo anterior conservado: ") + battery.PreservedPath);
                if (_batteryMessage.Length > 0) ImGui.TextWrapped(_batteryMessage);
            }
            else ImGui.TextWrapped(UiText.Get("Abrí un cartucho NES con batería de 8 KB para usar este panel. Las fusiones y los ensayos no comparten la batería de la partida."));
            ImGui.EndChild();
            if (BatteryButton("Cerrar batería")) _batteryWindow = false;
        }
        ImGui.End();
    }

    private bool BatteryButton(string title, string? control = null)
    {
        bool clicked = ImGui.Button(UiText.Label(title), new(-1, 0));
        InterfaceControlBounds?.Invoke(control ?? title, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        return clicked;
    }

    private void BatteryAction(Func<string> action)
    {
        try { _batteryMessage = action(); }
        catch (Exception error) when (NesBatteryStore.IsStorageError(error) || error is ArgumentException or NotSupportedException)
        { _batteryMessage = UiText.Message(error.Message); }
    }

    private CommandFailure? NeedBattery() => _host.Battery != null ? null : new(CommandProblem.WrongContext,
        "Abrí un cartucho NES con batería de 8 KB para usar este panel. Las fusiones y los ensayos no comparten la batería de la partida.");

    private IEnumerable<CommandSpec> BatteryOrders() =>
    [
        new()
        {
            Id = "play.battery", Name = "bateria", Aliases = ["battery"], Group = "Partida", Unavailable = NeedBattery,
            Summary = "Consulta, guarda, exporta o recupera la batería del cartucho NES actual.",
            Keywords = "progreso guardar ram sram cartucho respaldo recuperar",
            Arguments =
            [
                new("accion", ArgumentKind.Choice, "estado, abrir, guardar, exportar o recuperar.") { Optional = true, Choices = ["estado", "abrir", "guardar", "exportar", "recuperar"] },
                new("ruta", ArgumentKind.Path, "Archivo .rfbattery; entre comillas si tiene espacios.") { Optional = true },
                new("confirmar", ArgumentKind.Choice, "Recuperar requiere reemplazar; se preservan los datos anteriores.") { Optional = true, Choices = ["reemplazar"] }
            ],
            Examples = ["bateria", "bateria guardar", "bateria exportar \"C:\\Copias\\Faro.rfbattery\"", "bateria recuperar \"C:\\Copias\\Faro.rfbattery\" reemplazar"],
            Errors = ["la copia pertenece a otro juego, está dañada, ya existe o no se puede guardar"],
            Result = "La exportación no sobrescribe archivos. Recuperar conserva la memoria y el archivo anteriores; la partida queda en pausa.",
            Run = context =>
            {
                var battery = _host.Battery!;
                string action = CommandLine.Fold(context.Optional("accion") ?? "estado");
                if (action is "exportar" or "recuperar")
                {
                    if (!context.Has("ruta")) throw new CommandFailure(CommandProblem.InvalidArgument, "Indicá la ruta de la copia de batería.");
                    if (action == "exportar")
                    {
                        if (context.Has("confirmar")) throw new CommandFailure(CommandProblem.InvalidArgument, "La exportación de batería nunca sobrescribe archivos.");
                        context.Success(UiText.Get("Copia de batería guardada: ") + battery.Export(context.Text("ruta")));
                    }
                    else
                    {
                        if (!context.Has("confirmar")) throw new CommandFailure(CommandProblem.InvalidArgument, "Recuperar batería requiere reemplazar; se preservan los datos anteriores.");
                        _host.RestoreBattery(context.Text("ruta"));
                        context.Success("Batería recuperada. Los datos anteriores se conservaron en respaldos.");
                    }
                }
                else
                {
                    if (context.Has("ruta") || context.Has("confirmar")) throw new CommandFailure(CommandProblem.InvalidArgument, "La ruta sólo se usa para exportar o recuperar batería.");
                    if (action == "abrir") OpenBatteryWindow();
                    else if (action == "guardar")
                    {
                        if (!battery.Flush()) throw new IOException(battery.Problem);
                        context.Success("Batería guardada.");
                    }
                    else
                    {
                        context.Detail(battery.Path);
                        context.Detail(battery.Pending ? "Hay cambios de batería en memoria." : "La batería no tiene cambios pendientes.");
                        if (battery.Problem != null) context.Detail(UiText.Message(battery.Problem));
                    }
                }
            }
        }
    ];
}
