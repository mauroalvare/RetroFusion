using System.Numerics;
using ImGuiNET;
using Retro.Ai;

namespace Retro.App;

/// <summary>Shows observed activity; local timing remains separate from observed CLI activity.</summary>
internal sealed class AiBuildWindow(string id, string title = "Construcción del juego", string openLabel = "Abrir construcción", string? timingPath = null, TimeProvider? clock = null)
{
    private AiTiming? _timing, _displayTiming;
    private CliAgentProvider? _cli;
    private AiOperation? _operation;
    private bool _started, _open, _estimate;
    public AiOperationStats? Usage => _operation?.Stats;

    public void Start(IAiProvider provider, string operation = "", string variant = "")
    {
        _operation = provider as AiOperation;
        _cli = (_operation?.Provider ?? provider) as CliAgentProvider;
        _timing = new AiTiming(provider.Name + "\0" + id + "\0" + operation + "\0" + variant, new AiDurationHistory(timingPath), clock);
        _started = true; _estimate = operation.Length > 0;
    }

    public void Stop(bool success = false) => _timing?.Stop(success);
    public void Reset() { _timing = _displayTiming = null; _cli = null; _operation = null; _started = _open = false; }

    public void Draw(bool busy, string stage, CancellationTokenSource? cancellation, AiTiming? wholeWork = null)
    {
        _displayTiming = wholeWork ?? _timing;
        if ((!_started && wholeWork == null) || _displayTiming == null) return;
        if (!busy) _timing?.Stop(false);
        ImGui.TextDisabled($"Transcurrido: {Duration(_displayTiming.Elapsed)}");
        if (busy && (_estimate || wholeWork != null)) ImGui.TextWrapped(_displayTiming.RemainingText);
        if (ImGui.SmallButton($"{openLabel}##{id}")) _open = true;
        if (!_open) return;
        ImGui.SetNextWindowSize(UiTheme.Px(640, 420), ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"{title}##{id}", ref _open))
        {
            ImGui.TextWrapped(stage);
            if (ImGui.SmallButton("Copiar estado")) ImGui.SetClipboardText(stage);
            ImGui.TextUnformatted($"Tiempo transcurrido: {Duration(_displayTiming.Elapsed)}");
            if (busy && (_estimate || wholeWork != null)) ImGui.TextWrapped(_displayTiming.RemainingText);
            if (_operation?.Stats is { } stats)
                UsageDetails(stats);
            if (_cli?.Activity is { } activity)
            {
                ImGui.TextUnformatted(activity.Status);
                ImGui.BeginDisabled(activity.Output.Length == 0);
                if (ImGui.SmallButton("Copiar salida del CLI")) ImGui.SetClipboardText(activity.Output);
                ImGui.EndDisabled();
                ImGui.TextDisabled(activity.OutputCharacters == 0
                    ? "Todavía no hay salida."
                    : $"Sin salida nueva hace {Duration(activity.QuietFor)} · {activity.OutputCharacters:N0} caracteres");
                ImGui.BeginChild("salida", new Vector2(0, -UiTheme.Px(70)), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar);
                bool atBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 2;
                ImGui.TextUnformatted(activity.Output.Length > 0 ? activity.Output : "Esperando salida del CLI...");
                if (atBottom) ImGui.SetScrollHereY(1);
                ImGui.EndChild();
            }
            else
                ImGui.TextWrapped(_cli != null ? "Preparando la llamada al CLI..." : "Esperando la respuesta de la IA...");
            AiPicker.Hint("Algunas herramientas muestran la respuesta recién al terminar.");
            if (busy && cancellation != null && ImGui.Button("Cancelar##construccion")) cancellation.Cancel();
        }
        ImGui.End();
    }

    private static string Duration(TimeSpan elapsed) => $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    public static void UsageDetails(AiOperationStats stats)
    {
        ImGui.TextDisabled($"Pedidos: {stats.Calls} · Texto enviado: {stats.TextCharacters:N0} caracteres");
        if (stats.CallsWithUsage > 0)
            ImGui.TextWrapped($"Tokens informados{(stats.CallsWithUsage < stats.Calls ? " (parcial)" : "")}: {stats.InputTokens:N0} entrada · {stats.OutputTokens:N0} salida" +
                (stats.CachedInputTokens is long cached ? $" · {cached:N0} de entrada en caché" : ""));
        else ImGui.TextDisabled("La herramienta no informó consumo de tokens.");
    }
}
