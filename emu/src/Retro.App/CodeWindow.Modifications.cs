using System.Numerics;
using ImGuiNET;
using Retro.Ai;
using Retro.Core;
using Retro.Fusion;

namespace Retro.App;

public sealed partial class CodeWindow
{
    private bool _modificationsMode, _finderOnOpen, _variablesOnOpen, _manualModification;
    private float? _modificationPanelLeft;
    private long _mediaRevision = -1, _explainRevision, _modificationRevision;
    private IMachine? _modificationMachine;
    private IMachine? _explainMachine;
    private GameModifications? _modifications;
    private ModificationAdvice? _advice;
    private Task<ModificationAdvice>? _modificationTask;
    private CancellationTokenSource? _modificationCts;
    private readonly AiBuildWindow _modificationActivity = new("modificaciones", "Actividad de la IA", "Ver actividad de la IA");
    private readonly AiBuildWindow _routineActivity = new("rutinas", "Actividad de la IA", "Ver actividad de la IA");
    private string _modificationWish = "", _modificationPasted = "";
    private readonly Dictionary<string, string> _variableInputs = new();
    private readonly Action? _openStudio;

    public void OpenModifications() { _host.RefreshLoadedProfile(); Open(); _modificationsMode = true; }
    public void OpenFinder() { Open(); _finderOnOpen = true; _variablesOnOpen = false; }
    public float RightReserve(float width) => _open && _modificationsMode && _modificationPanelLeft is float left && left > width / 2 ? width - left : 0;

    private static string VariableLabel(string name) => name switch
    {
        "lives" => "Vidas", "air" => "Aire", "score" => "Puntaje", "energy" => "Energía",
        "timer" or "time" => "Tiempo", "x" => "Posición horizontal", "y" or "willyY" => "Altura",
        "airborne" => "Salto / caída", "cavern" or "level" => "Nivel", _ => name
    };

    private void DrawModifications(IMachine machine)
    {
        ImGui.TextColored(LabelColor, _gameName);
        if (_host.Fusion != null)
        {
            ImGui.TextWrapped("Para cambiar esta fusión, pedí una versión nueva en el estudio. El regreso de partida solo está disponible en originales.");
            if (ImGui.Button("Variables del original enfocado (avanzado)"))
            { Open(); _variablesOnOpen = true; }
            return;
        }
        if (_host.LoadedMedia == null)
        {
            ImGui.TextWrapped("Abrí un juego desde Jugar para experimentar con su partida.");
            return;
        }
        ImGui.TextWrapped("Cambiá un valor y confirmá con Enter. Congelar lo mantiene fijo.");
        if (_status.Length > 0) ImGui.TextWrapped(_status);
        ImGui.BeginDisabled(_modifications?.CanRestore != true);
        if (ImGui.Button("Volver antes de probar")) RestoreModifications();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("El primer cambio guarda la partida. Volver deshace también el progreso jugado desde ese momento.");
        ImGui.SameLine();
        ImGui.BeginDisabled(_modifications?.Frozen.Count is not > 0);
        if (ImGui.Button("Soltar todos los congelados"))
        { _modifications!.ReleaseAll(); _status = "Los valores vuelven a cambiar con el juego. El punto de regreso sigue disponible."; }
        ImGui.EndDisabled();
        ImGui.Separator();
        if (_profile == null || _profile.Variables.Count == 0)
        {
            ImGui.TextWrapped("No hay variables conocidas para esta edición. Buscalas en la partida o prepará el juego en el estudio.");
            if (ImGui.Button("Buscar variables en esta partida")) OpenFinder();
            ImGui.SameLine();
            if (ImGui.Button("Analizar el código")) Open();
            if (_openStudio != null && ImGui.Button("Preparar un juego en el estudio"))
            { _open = false; _openStudio(); }
            AiPicker.Hint("Cuando termine la preparación, volvé con Ctrl+M.");
            return;
        }
        ImGui.TextColored(LabelColor, "Variables conocidas");
        DrawVariableValues(machine, false);
        if (ImGui.SmallButton("Buscar otras variables")) OpenFinder();
        ImGui.SameLine();
        if (ImGui.SmallButton("Ver rutinas y análisis del código")) Open();
        ImGui.Separator();
        ImGui.TextColored(LabelColor, "Pedir ideas a la IA");
        AiPicker.Combo(_ai);
        if (AiPicker.IsApi(_ai.Provider)) AiPicker.Settings(_ai, "Pedir ideas usa una llamada a la IA elegida.");
        ImGui.TextUnformatted("Qué querés probar (opcional):");
        ImGui.InputTextMultiline("##experimento", ref _modificationWish, 1200, new Vector2(-1, 48));
        AiPicker.Hint("Las propuestas se aplican cuando pulsás Probar estos cambios.");
        bool busy = _modificationTask != null;
        bool writable = _profile.Variables.Any(v => !v.Value.ReadOnly);
        if (!writable) AiPicker.Hint("Los datos conocidos son de solo lectura. Buscá otra variable para poder experimentar.");
        ImGui.BeginDisabled(busy || !writable);
        if (AiPicker.IsApi(_ai.Provider))
        {
            if (ImGui.Button(busy ? "La IA está analizando..." : "Pedir ideas para modificar"))
            {
                try { BeginModificationAdvice(_ai.CreateProvider() ?? throw new AiException("Elegí una IA disponible.")); }
                catch (Exception error) when (error is AiException or InvalidOperationException or IOException or FormatException or OverflowException or ArgumentException)
                { _status = error.Message; }
            }
        }
        else if (ImGui.Button("Copiar pedido para mi IA o agente"))
        {
            try
            {
                ImGui.SetClipboardText(GameModificationAdvisor.Guide + "\n\n" + ModificationPrompt(machine) +
                    "\nRespondé solo un objeto JSON que cumpla este esquema:\n" + GameModificationAdvisor.Schema().GetRawText());
                _manualModification = true;
                _status = "Pedido copiado. Pegá acá la respuesta de tu IA para revisar sus propuestas.";
            }
            catch (Exception error) when (error is InvalidOperationException or FormatException or OverflowException or ArgumentException)
            { _status = "No pude armar el pedido: " + error.Message; }
        }
        ImGui.EndDisabled();
        busy = _modificationTask != null;
        if (busy)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancelar análisis")) _modificationCts?.Cancel();
        }
        _modificationActivity.Draw(busy, _status, _modificationCts);
        if (_manualModification)
        {
            ImGui.InputTextMultiline("##ideas-recibidas", ref _modificationPasted, 16000, new Vector2(-1, 80));
            if (ImGui.Button("Revisar las propuestas recibidas"))
                try { _advice = GameModificationAdvisor.Parse(_modificationPasted, _profile); _status = "Propuestas listas para elegir."; }
                catch (AiException error) { _status = error.Message; }
        }
        if (_advice != null)
        {
            ImGui.Separator();
            ImGui.TextWrapped(_advice.Analysis);
            int id = 0;
            foreach (var idea in _advice.Ideas)
            {
                ImGui.PushID(id++);
                ImGui.TextColored(LabelColor, idea.Title);
                ImGui.TextWrapped(idea.Effect);
                foreach (var change in idea.Changes)
                    ImGui.TextWrapped($"{VariableLabel(change.Variable)}: {change.Value}{(change.Freeze ? " (congelado)" : "")}");
                if (ImGui.Button("Probar estos cambios")) ApplyModifications(idea.Changes);
                ImGui.Spacing(); ImGui.PopID();
            }
        }
    }

    private string ModificationPrompt(IMachine machine) => GameModificationAdvisor.Prompt(_profile!, machine.Memory,
        string.IsNullOrWhiteSpace(_modificationWish) ? "Proponé pruebas divertidas y distintas con estas variables." : _modificationWish.Trim());

    private void BeginModificationAdvice(IAiProvider provider)
    {
        Update();
        if (_profile == null || _machine == null || _modificationTask != null) return;
        string prompt = ModificationPrompt(_machine);
        var profile = GameProfile.Parse(_profile.ToJson());
        _modificationRevision = _mediaRevision; _modificationMachine = _machine;
        _modificationCts?.Dispose(); _modificationCts = new CancellationTokenSource();
        var token = _modificationCts.Token;
        _advice = null;
        _status = $"{provider.Name} está leyendo las variables conocidas...";
        _modificationActivity.Start(provider);
        _modificationTask = Task.Run(() => GameModificationAdvisor.ProposeAsync(provider, prompt, profile, token));
    }

    private void PollModificationAdvice()
    {
        if (_modificationTask is not { IsCompleted: true } task) return;
        _modificationTask = null; _modificationActivity.Stop();
        _modificationCts?.Dispose(); _modificationCts = null;
        if (_mediaRevision != _modificationRevision || !ReferenceEquals(_machine, _modificationMachine))
        { _ = task.Exception; return; }
        if (task.IsCanceled || task.Exception?.GetBaseException() is OperationCanceledException)
            _status = "Análisis cancelado.";
        else if (task.IsFaulted) _status = "No se pudieron obtener propuestas: " + task.Exception!.GetBaseException().Message;
        else { _advice = task.Result; _status = "Elegí una propuesta para probarla; las variables siguen como estaban."; }
    }

    private void ApplyModifications(IReadOnlyList<VariableEdit> changes)
    {
        try
        {
            if (_modifications == null) throw new InvalidOperationException("Primero encontrá variables para este juego.");
            _modifications.Apply(changes);
            _status = "Cambios aplicados a esta partida. Podés volver al punto anterior a la primera prueba.";
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or NotSupportedException)
        { _status = "No se aplicó la prueba: " + error.Message; }
    }

    private void RestoreModifications()
    {
        if (_host.Fusion != null || _modifications?.Restore() != true) return;
        _host.StateRestored();
        Update();
        _status = "Partida restaurada al momento anterior a la primera prueba; congelados liberados.";
    }

    public void Dispose()
    {
        _host.BeforeFrame -= ApplyFrozen;
        _cts?.Cancel(); _modificationCts?.Cancel();
        _cts?.Dispose(); _modificationCts?.Dispose();
    }

    public Task CancelWork()
    {
        _cts?.Cancel(); _modificationCts?.Cancel();
        return Task.WhenAll(new Task?[] { _explainTask, _modificationTask }.OfType<Task>());
    }
}
