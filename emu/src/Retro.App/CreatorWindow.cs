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
    private bool _open;
    private int _machine;               // index in Templates.All
    private string _wish = "";
    private volatile string _progress = "";
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

    public CreatorWindow(string romDir, AiSettings ai, Action<string> play)
    {
        _romDir = romDir;
        _ai = ai;
        _play = play;
    }

    public void Open()
    {
        _open = true;
        _games = null;
        if (!_exported)
        {
            // the example games, tried once in the emulator, so there is something to play right away
            _exported = true;
            _exportTask = Task.Run(() => CreatedGames.ExportExamples(_romDir));
        }
    }

    private bool Busy => _writeTask != null || _templateTask != null || _retryTask != null;

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
        DrawNew();
        ImGui.Separator();
        DrawGames();
        ImGui.End();
    }

    private void DrawNew()
    {
        ImGui.TextWrapped("La IA escribe un juego ORIGINAL en ensamblador para una máquina de verdad (emulada), lo prueba con un " +
                          "jugador robot y lo corrige hasta que anda. También podés empezar uno vos desde la plantilla y editarlo.");
        ImGui.TextUnformatted("Máquina:");
        for (int i = 0; i < Templates.All.Count; i++)
        {
            ImGui.SameLine();
            ImGui.RadioButton(Templates.All[i].Name, ref _machine, i);
        }
        AiPicker.Combo(_ai, "##ia-crear");
        bool callable = AiPicker.IsApi(_ai.Provider);
        if (callable)
            AiPicker.Settings(_ai, "Cada intento es un pedido a la IA; un juego suele llevar de 1 a 5.");
        else
            AiPicker.Hint("Para escribir juegos elegí una IA por suscripción o por API (copiar y pegar y la carpeta de agentes todavía no). " +
                          "Con \"Empezar desde la plantilla\" no hace falta IA.");
        ImGui.TextUnformatted("Qué juego querés (opcional):");
        ImGui.InputTextMultiline("##pedido-crear", ref _wish, 1000, new Vector2(-1, 44));
        AiPicker.Hint("Ej.: \"un faro que guía barcos en una tormenta\", \"un laberinto con fantasmas tímidos\", \"algo para dos jugadores\". " +
                      "Vacío: la IA sortea un género, un mundo, un tono y una regla rara, distintos cada vez.");
        ImGui.BeginDisabled(Busy || !callable);
        if (ImGui.Button("Escribir el juego con IA"))
            Write();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(Busy);
        if (ImGui.Button("Empezar desde la plantilla"))
            FromTemplate();
        ImGui.EndDisabled();
        if (_progress.Length > 0)
            ImGui.TextColored(Blue, _progress);
        if (_cts != null && _writeTask != null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancelar"))
                _cts.Cancel();
        }
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
        ImGui.SameLine();
        ImGui.TextDisabled(CreatedGames.UserFolder);
        if (_games.Count == 0)
        {
            AiPicker.Hint("Todavía no hay ninguno.");
            return;
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
            AiPicker.Hint("Para cambiarlo a mano: editá juego.s en su carpeta y apretá Probar de nuevo.");
            ImGui.PopID();
        }
    }

    private void Write()
    {
        var provider = _ai.CreateProvider();
        if (provider == null)
        {
            Problem("Esa IA no se puede llamar desde acá.");
            return;
        }
        _problem = _problemDetail = null;
        _cts = new CancellationTokenSource();
        _writeMachine = Templates.All[_machine].Machine;
        _writeWish = _wish;
        _writeAuthor = provider.Name;
        _progress = $"{provider.Name} está escribiendo el juego...";
        var token = _cts.Token;
        string machine = _writeMachine, wish = _writeWish;
        _writeTask = Task.Run(() => new GameWriter().WriteAsync(provider, machine, wish, _romDir, token, p => _progress = p), token);
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
        if (_exportTask is { IsCompleted: true } et)
        {
            _exportTask = null;
            _games = null;
            if (!et.IsCompletedSuccessfully)
                Problem("No pude copiar los juegos de ejemplo: " + Failure(et.Exception));
        }
        if (_writeTask is { IsCompleted: true } wt)
        {
            _writeTask = null;
            _cts = null;
            if (!wt.IsCompletedSuccessfully)
                Problem(Failure(wt.Exception));
            else if (wt.Result.Game is { } draft)
            {
                var g = CreatedGames.Save(draft, wt.Result.Report, _writeMachine, _writeWish, _writeAuthor);
                _games = null;
                if (wt.Result.Ok)
                {
                    _progress = $"\"{g.Title}\" listo en {wt.Result.Attempts} intento{(wt.Result.Attempts > 1 ? "s" : "")}.";
                    _play(g.ProgramPath);
                }
                else
                    Problem($"La IA no logró que \"{g.Title}\" pase la prueba en {wt.Result.Attempts} intentos ({wt.Result.Problem}). " +
                            "Quedó guardado: podés editarlo y probarlo de nuevo.", wt.Result.Report?.Summary);
            }
            else
                Problem("La IA no devolvió un juego: " + wt.Result.Problem);
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
