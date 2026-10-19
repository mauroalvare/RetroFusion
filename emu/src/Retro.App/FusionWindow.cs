using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Retro.Ai;
using Retro.Core;
using Retro.Fusion;

namespace Retro.App;

/// <summary>
/// "Fusión de juegos": pick 2 or 3 games, play a registered recipe or have an AI design
/// a new one (Claude, ChatGPT, Gemini, Ollama, copy/paste into any chat, or a folder for coding agents).
/// </summary>
public sealed class FusionWindow
{
    private static readonly string[] Layouts = { "side", "overlay", "pip" };
    private static readonly string[] LayoutNames = { "Lado a lado", "Superpuestos", "Uno dentro del otro" };

    private readonly EmulatorHost _host;
    private readonly string _romDir;
    private readonly AiSettings _ai;
    private bool _open;
    private string _folder;
    private readonly List<GameSource> _games = new();
    /// <summary>Profile id -> file of the games with no verified variables yet.</summary>
    private readonly Dictionary<string, string> _unprepared = new();
    /// <summary>Profile id -> label in the list (the file name is added when two games share a name).</summary>
    private readonly Dictionary<string, string> _labels = new();
    private readonly HashSet<string> _chosen = new();
    private string _instructions = "";
    private string _status = "";
    private volatile string _progress = "";
    private Task<DesignResult>? _designTask;
    private Task<FusionSession>? _startTask;
    private CancellationTokenSource? _cts;
    private FusionRecipe? _designed;
    private List<string> _designErrors = new();
    private string _pasted = "";
    private string? _agentDir;
    private double _agentPoll;
    private int _layoutIndex = -1;
    private readonly List<FusionEvent> _toasts = new();
    private int _seenEvents;

    public FusionWindow(EmulatorHost host, string romDir, string gamesFolder, AiSettings ai)
    {
        _host = host;
        _romDir = romDir;
        _folder = gamesFolder;
        _ai = ai;
    }

    public void Open()
    {
        _open = true;
        if (_games.Count == 0)
            Scan();
    }

    private void Scan()
    {
        _games.Clear();
        _unprepared.Clear();
        if (!Directory.Exists(_folder))
        {
            _status = "La carpeta no existe.";
            return;
        }
        _labels.Clear();
        var catalog = Retro.Compat.GameCatalog.Scan(_folder);
        foreach (var g in catalog)
        {
            _games.Add(g.Source);
            if (!g.Prepared)
                _unprepared[g.Profile.Id] = g.Path;
            bool twin = catalog.Count(o => o.Profile.Name == g.Profile.Name) > 1;
            _labels[g.Profile.Id] = twin ? $"{g.Profile.Name} · {Path.GetFileName(g.Path)}" : g.Profile.Name;
        }
        _status = $"{_games.Count} juegos que este emulador puede correr ({_games.Count - _unprepared.Count} preparados con variables verificadas).";
    }

    /// <summary>Command line --fusion: starts a recipe with the games found in the folder.</summary>
    public void StartByRecipeId(string id)
    {
        Open();
        var recipe = RecipeLibrary.All().FirstOrDefault(r => r.Id == id);
        if (recipe == null)
        {
            _status = $"No existe la receta \"{id}\".";
            return;
        }
        var chosen = recipe.Games.Select(g => _games.FirstOrDefault(x => x.Profile.Id == g.Profile)).ToList();
        if (chosen.Any(c => c == null))
        {
            _status = $"Faltan juegos en la carpeta para \"{recipe.Name}\".";
            return;
        }
        foreach (var c in chosen)
            _chosen.Add(c!.Profile.Id);
        Start(recipe, chosen!);
    }

    /// <summary>Grey text that wraps to the window width.</summary>
    private static void Hint(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private List<GameSource> Chosen() => _games.Where(g => _chosen.Contains(g.Profile.Id)).ToList();

    public void Draw()
    {
        PollTasks();
        DrawToasts();
        if (!_open)
            return;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + vp.Size.X - 530, vp.Pos.Y + 30), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(520, Math.Min(720, vp.Size.Y - 70)), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Fusión de juegos", ref _open))
        {
            ImGui.End();
            return;
        }
        if (_host.Fusion is FusionSession running)
            DrawRunning(running);
        else if (_host.Fusion != null)
            Hint("Hay un juego fusionado con IA en marcha: se maneja desde Fusión > Crear juego fusionado con IA.");
        else
            DrawSetup();
        ImGui.End();
    }

    // ------------------------------------------------------------------ setup
    /// <summary>Opens the AI game studio (set by the app).</summary>
    public Action? OpenStudio { get; set; }

    private void DrawSetup()
    {
        ImGui.TextColored(new Vector4(1, 0.85f, 0.3f, 1), "Esto es la fusión clásica: los juegos corren a la vez y se conectan con reglas.");
        if (OpenStudio != null && ImGui.Button("Quiero un juego nuevo que mezcle los juegos de verdad (con IA)"))
            OpenStudio();
        ImGui.Separator();
        ImGui.TextWrapped("Elegí 2 o 3 juegos. Corren al mismo tiempo con emulación real y se conectan con reglas que leen y escriben su memoria.");
        ImGui.SetNextItemWidth(-90);
        ImGui.InputText("##carpeta", ref _folder, 1024);
        ImGui.SameLine();
        if (ImGui.Button("Buscar"))
            Scan();
        Hint(_status);
        ImGui.Separator();
        foreach (var g in _games)
        {
            bool on = _chosen.Contains(g.Profile.Id);
            if (ImGui.Checkbox($"{_labels.GetValueOrDefault(g.Profile.Id, g.Profile.Name)}##{g.Profile.Id}", ref on))
            {
                if (on) _chosen.Add(g.Profile.Id);
                else _chosen.Remove(g.Profile.Id);
                _designed = null;
                _designErrors.Clear();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(g.Profile.Description + "\nVariables: " + (g.Profile.Variables.Count > 0 ? string.Join(", ", g.Profile.Variables.Keys) : "ninguna todavía"));
        }
        var chosen = Chosen();
        if (chosen.Count is < 2 or > 3)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Marcá 2 o 3 juegos para ver sus fusiones.");
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Recetas para esta combinación:");
        var recipes = RecipeLibrary.For(chosen.Select(g => g.Profile.Id)).ToList();
        if (recipes.Count == 0)
            Hint("   Ninguna registrada todavía: creala con la IA abajo.");
        foreach (var r in recipes)
            DrawRecipeCard(r, chosen, saved: true);

        ImGui.Separator();
        DrawDesigner(chosen);
    }

    private void DrawRecipeCard(FusionRecipe r, List<GameSource> chosen, bool saved)
    {
        ImGui.PushID(r.Id + r.Name);
        ImGui.TextColored(new Vector4(1, 0.85f, 0.3f, 1), r.Name);
        ImGui.SameLine();
        Hint($"({r.Author})");
        ImGui.TextWrapped(r.Description);
        foreach (var rule in r.Rules)
            Hint($"   {(rule.When ?? $"cada {rule.Every} ticks")}  ->  {string.Join("; ", rule.Do)}");
        bool busy = _startTask != null;
        if (ImGui.Button(busy ? "Preparando..." : "Jugar esta fusión") && !busy)
            Start(r, chosen);
        if (!saved)
        {
            ImGui.SameLine();
            if (ImGui.Button("Guardar receta"))
                _status = "Guardada en " + RecipeLibrary.Save(r);
        }
        ImGui.Spacing();
        ImGui.PopID();
    }

    private void Start(FusionRecipe recipe, List<GameSource> chosen)
    {
        var sources = chosen.ToDictionary(g => g.Profile.Id);
        _status = "Arrancando los juegos (la cinta del Spectrum carga en unos segundos)...";
        _startTask = Task.Run(() => FusionSession.Start(recipe, sources, _romDir));
    }

    private void DrawDesigner(List<GameSource> chosen)
    {
        ImGui.TextUnformatted("Crear una fusión nueva con IA");
        AiPicker.Combo(_ai);
        ImGui.TextUnformatted("Tu pedido (opcional):");
        ImGui.InputTextMultiline("##pedido", ref _instructions, 2000, new Vector2(-1, 60));
        Hint("Ej.: \"que cada vez que Willy salte el cañón dispare\", \"modo caos\", \"cooperativo para dos personas\".");

        switch (_ai.Provider)
        {
            case "claude" or "openai" or "gemini" or "ollama" or "sub-claude" or "sub-codex" or "sub-google":
                AiPicker.Settings(_ai, "Cada receta es un pedido a la IA (unos centavos).");
                DrawGenerateButton(chosen);
                break;
            case "manual":
            {
                var profiles = chosen.Select(g => g.Profile).ToList();
                if (ImGui.Button("1. Copiar la consigna"))
                {
                    ImGui.SetClipboardText(FusionDesigner.ManualPrompt(profiles, _instructions));
                    _status = "Consigna copiada: pegala en el chat de tu IA y copiá su respuesta.";
                }
                ImGui.TextUnformatted("2. Pegá acá la respuesta de la IA:");
                ImGui.InputTextMultiline("##respuesta", ref _pasted, 20000, new Vector2(-1, 100));
                if (ImGui.Button("3. Validar"))
                {
                    var (recipe, errors) = FusionDesigner.Check(_pasted, profiles, ProfileLibrary.Find);
                    _designed = recipe;
                    _designErrors = errors;
                    if (recipe != null && string.IsNullOrWhiteSpace(recipe.Author))
                        recipe.Author = "IA (copiar y pegar)";
                }
                if (_designErrors.Count > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Copiar errores para la IA"))
                        ImGui.SetClipboardText("La receta tiene estos problemas:\n- " + string.Join("\n- ", _designErrors) + "\nDevolvé la receta completa corregida.");
                }
                break;
            }
            default:
            {
                var profiles = chosen.Select(g => g.Profile).ToList();
                if (ImGui.Button("Crear carpeta para el agente"))
                {
                    _agentDir = AgentWorkspace.Create(profiles, _instructions);
                    _status = "Abrí esa carpeta con tu agente y pedile: \"leé TAREA.md\".";
                }
                if (_agentDir != null)
                {
                    ImGui.TextWrapped("Carpeta: " + _agentDir);
                    if (ImGui.Button("Abrir carpeta"))
                        Process.Start(new ProcessStartInfo { FileName = _agentDir, UseShellExecute = true });
                    ImGui.SameLine();
                    if (ImGui.Button("Copiar ruta"))
                        ImGui.SetClipboardText(_agentDir);
                    Hint("Esperando receta.json... (se revisa sola cada segundo)");
                    _agentPoll += ImGui.GetIO().DeltaTime;
                    if (_agentPoll > 1)
                    {
                        _agentPoll = 0;
                        if (AgentWorkspace.Poll(_agentDir, profiles, ProfileLibrary.Find) is { } r)
                        {
                            _designed = r.recipe;
                            _designErrors = r.errors;
                            if (_designed != null && string.IsNullOrWhiteSpace(_designed.Author))
                                _designed.Author = "Agente";
                        }
                    }
                }
                break;
            }
        }

        if (_progress.Length > 0)
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1, 1), _progress);
        foreach (var e in _designErrors)
            ImGui.TextColored(new Vector4(1, 0.5f, 0.4f, 1), "  - " + e);
        if (_designed != null)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Receta nueva:");
            DrawRecipeCard(_designed, chosen, saved: false);
        }
    }

    private void DrawGenerateButton(List<GameSource> chosen)
    {
        bool busy = _designTask != null;
        if (ImGui.Button(busy ? "Generando..." : "Generar fusión con IA") && !busy)
        {
            var provider = _ai.CreateProvider();
            if (provider == null)
                return;
            _designed = null;
            _designErrors.Clear();
            _cts = new CancellationTokenSource();
            var profiles = chosen.Select(g => g.Profile).ToList();
            string wish = _instructions;
            _designTask = Task.Run(() => new FusionDesigner().DesignAsync(provider, profiles, wish, ProfileLibrary.Find, _cts.Token, p => _progress = p));
        }
        if (busy)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancelar"))
                _cts?.Cancel();
        }
    }

    // ------------------------------------------------------------------ running
    private void DrawRunning(FusionSession s)
    {
        ImGui.TextColored(new Vector4(1, 0.85f, 0.3f, 1), s.Recipe.Name);
        ImGui.TextWrapped(s.Recipe.Description);
        Hint("Flechas = mover, Ctrl o Z = disparar, Espacio o X = saltar, Enter o 1 = empezar, 5 o C = moneda.");
        if (_layoutIndex < 0)
            _layoutIndex = Math.Max(0, Array.IndexOf(Layouts, s.Recipe.Layout.Mode));
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Vista", ref _layoutIndex, LayoutNames, LayoutNames.Length) && Layouts[_layoutIndex] != s.Recipe.Layout.Mode)
        {
            // the layout changes the picture size: restart the composition with the same machines
            s.Recipe.Layout.Mode = Layouts[_layoutIndex];
            _host.StartFusion(new FusionSession(s.Recipe, s.Slots));
        }
        float opacity = (float)s.OverlayOpacity;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderFloat("Transparencia", ref opacity, 0.15f, 1f))
            s.OverlayOpacity = opacity;
        ImGui.TextUnformatted("Juego que muestran el depurador y el visor de código:");
        foreach (var slot in s.Slots)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(slot.Profile.Name.Split(' ')[0] + "##" + slot.Id, _host.FusionFocus == slot.Id))
                _host.FusionFocus = slot.Id;
        }
        ImGui.Separator();
        if (ImGui.BeginTable("vars", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
        {
            foreach (var (slot, variable, value) in s.Variables())
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(slot);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(variable);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(value.ToString());
            }
            ImGui.EndTable();
        }
        ImGui.TextUnformatted("Eventos:");
        foreach (var e in s.Events.TakeLast(8).Reverse())
            Hint($"  {e.Tick / 60.0,6:F1} s  {e.Text}");
        if (ImGui.Button("Guardar receta en mis fusiones"))
            _status = "Guardada en " + RecipeLibrary.Save(s.Recipe);
        ImGui.SameLine();
        if (ImGui.Button("Detener fusión"))
        {
            _host.StopFusion();
            _layoutIndex = -1;
        }
        Hint(_status);
    }

    /// <summary>Rule messages pop up over the game for a few seconds.</summary>
    private void DrawToasts()
    {
        var s = _host.Fusion;
        if (s == null)
        {
            _seenEvents = 0;
            _toasts.Clear();
            return;
        }
        for (; _seenEvents < s.Events.Count; _seenEvents++)
            _toasts.Add(s.Events[_seenEvents]);
        _toasts.RemoveAll(t => s.Tick - t.Tick > 180);
        if (_toasts.Count == 0)
            return;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + vp.Size.X / 2, vp.Pos.Y + 40), ImGuiCond.Always, new Vector2(0.5f, 0));
        ImGui.SetNextWindowBgAlpha(0.7f);
        ImGui.Begin("##toasts", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoDocking);
        foreach (var t in _toasts.TakeLast(3))
            ImGui.TextColored(new Vector4(1, 0.9f, 0.4f, 1), t.Text);
        ImGui.End();
    }

    private void PollTasks()
    {
        if (_designTask is { IsCompleted: true } dt)
        {
            _designTask = null;
            if (dt.IsFaulted)
            {
                var e = dt.Exception!.GetBaseException();
                _progress = e is OperationCanceledException ? "Cancelado." : "Error: " + e.Message;
            }
            else if (dt.IsCanceled)
                _progress = "Cancelado.";
            else
            {
                var r = dt.Result;
                _designed = r.Recipe;
                _designErrors = r.Errors.ToList();
                _progress = r.Recipe != null
                    ? $"Lista en {r.Attempts} intento{(r.Attempts > 1 ? "s" : "")}: probala con \"Jugar esta fusión\"."
                    : "La IA no logró una receta válida en 3 intentos. Errores:";
            }
        }
        if (_startTask is { IsCompleted: true } st)
        {
            _startTask = null;
            if (st.IsFaulted)
                _status = "No se pudo arrancar: " + st.Exception!.GetBaseException().Message;
            else
            {
                _host.StartFusion(st.Result);
                _status = "";
                _layoutIndex = -1;
            }
        }
    }
}
