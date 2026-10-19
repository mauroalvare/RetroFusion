using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Retro.Ai;

namespace Retro.App;

/// <summary>Shows observed activity; elapsed time is not an estimate of remaining work.</summary>
internal sealed class AiBuildWindow(string id)
{
    private readonly Stopwatch _elapsed = new();
    private CliAgentProvider? _cli;
    private bool _started, _open;

    public void Start(IAiProvider provider)
    {
        _cli = provider as CliAgentProvider;
        _elapsed.Restart();
        _started = true;
    }

    public void Stop() => _elapsed.Stop();

    public void Draw(bool busy, string stage, CancellationTokenSource? cancellation)
    {
        if (!_started) return;
        if (!busy) _elapsed.Stop();
        ImGui.TextDisabled($"Transcurrido: {Duration(_elapsed.Elapsed)}");
        if (ImGui.SmallButton($"Abrir construcción##{id}")) _open = true;
        if (!_open) return;
        ImGui.SetNextWindowSize(new Vector2(640, 420), ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"Construcción del juego##{id}", ref _open))
        {
            ImGui.TextWrapped(stage);
            ImGui.TextUnformatted($"Tiempo transcurrido: {Duration(_elapsed.Elapsed)}");
            if (_cli?.Activity is { } activity)
            {
                ImGui.TextUnformatted(activity.Status);
                ImGui.TextDisabled(activity.OutputCharacters == 0
                    ? "Esta llamada todavía no emitió texto."
                    : $"Última salida hace {Duration(activity.QuietFor)} · {activity.OutputCharacters:N0} caracteres en esta llamada.");
                ImGui.BeginChild("salida", new Vector2(0, -70), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar);
                bool atBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 2;
                ImGui.TextUnformatted(activity.Output.Length > 0 ? activity.Output : "Esperando salida del CLI...");
                if (atBottom) ImGui.SetScrollHereY(1);
                ImGui.EndChild();
            }
            else
                ImGui.TextWrapped(_cli != null ? "Preparando la llamada al CLI..." : "Esperando la respuesta de la IA...");
            ImGui.TextWrapped("Se muestra lo que la herramienta envía; algunas entregan todo el texto recién al terminar.");
            if (busy && cancellation != null && ImGui.Button("Cancelar##construccion")) cancellation.Cancel();
        }
        ImGui.End();
    }

    private static string Duration(TimeSpan elapsed) => $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
}
