using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ImGuiNET;
using Retro.Ai;
using Retro.Core;
using Retro.Fusion;
using Retro.Systems.Spectrum;

namespace Retro.App;

/// <summary>
/// "Código del juego": the running program split into routines with names, comments and
/// pseudocode (yours or written by an AI), code changes, the game's variables (edit or
/// freeze them) and a variable finder that creates the profile a game needs for fusions.
/// </summary>
public sealed partial class CodeWindow : IDisposable
{
    public void Reopen() { _host.RefreshLoadedProfile(); OpenPage(_activePage); }
    // From the theme, so the listing stays legible in high contrast.
    private static Vector4 LabelColor => UiTheme.Accent;
    private static Vector4 CommentColor => UiTheme.Success;
    private static Vector4 DimColor => UiTheme.Muted;
    private static Vector4 ErrorColor => UiTheme.Error;
    private static readonly Regex AddressToken = new(@"\$([0-9A-F]{4,5})(?![0-9A-F])", RegexOptions.Compiled);

    private readonly EmulatorHost _host;
    private readonly AiSettings _ai;
    private readonly Action<uint> _showInDebugger;
    private bool _open;
    public bool IsOpen => _open;

    // game the analysis belongs to
    private IMachine? _machine;
    private string? _media;
    private string _gameId = "";
    private string _gameName = "";
    private GameProfile? _profile;
    private CodeMap? _map;
    private CodeAnnotations _notes = new();
    private string _status = "", _codeSummary = "";

    // routine browser
    private uint? _selected;
    private uint? _line;
    private string _filter = "";
    private bool _onlyExecuted;
    private bool _hideSystemRom = true;
    private string _rename = "";
    private string _commentText = "";
    private string _patchHex = "";
    private readonly Stack<uint> _back = new();

    // AI
    private Task<RoutineExplanation>? _explainTask;
    private uint _explainFor;
    private string _explainStatus = "";
    private CancellationTokenSource? _cts;
    private string _pasted = "";

    // variables and finder
    private MemorySearch? _search;
    private int _searchValue;
    private string _newVarName = "";
    private string _newVarDescription = "";
    private string _bootText = "";

    public CodeWindow(EmulatorHost host, AiSettings ai, Action<uint> showInDebugger, Action? openStudio = null, ModificationHistory? history = null, string? firmwareDirectory = null)
    {
        _host = host;
        _ai = ai;
        _showInDebugger = showInDebugger;
        _openStudio = openStudio;
        _history = history ?? new ModificationHistory();
        _researchFirmwareDirectory = firmwareDirectory ?? "";
        _host.BeforeFrame += ApplyFrozen;
        _host.SessionChanging += LeaveModificationSession;
        _host.SessionChanging += StopAutonomousSession;
    }

    public bool IsWorking => _explainTask != null || _modificationTask != null || _autonomousTask != null || _manualModification || _manualExplanation;
    /// <summary>The job in progress, for the shared task list.</summary>
    public WorkReport? Work
    {
        get
        {
            void Cancel() => _ = CancelWork();
            string target = Path.GetFileName(_host.LoadedMedia ?? "") is { Length: > 0 } name ? name : "juego actual";
            if (_autonomousTask != null && _autonomousProgress is { } progress)
                return new("investigación automática", target, progress.Stage + (progress.Note.Length > 0 ? " · " + progress.Note : ""), AppTaskState.Working, progress.Pass, _autonomousPasses, "rondas", Cancel);
            if (_autonomousTask != null) return new("investigación automática", target, "Arrancando el laboratorio", Cancel: Cancel);
            if (_explainTask != null) return new("explicación", target, "Esperando la respuesta del proveedor", AppTaskState.WaitingProvider, Cancel: Cancel);
            if (_modificationTask != null) return new("análisis", target, "Esperando la respuesta del proveedor", AppTaskState.WaitingProvider, Cancel: Cancel);
            if (_manualModification || _manualExplanation) return new("análisis", target, "Esperando la respuesta que pegues en el panel", AppTaskState.WaitingUser);
            return null;
        }
    }

    public void ShowWork() => OpenPage(_explainTask != null || _manualExplanation ? ToolPage.Code : ToolPage.Analysis);

    public void Open() => OpenPage(ToolPage.Code);

    public void Update()
    {
        _retiredWork.RemoveAll(task => task.IsCompleted);
        var m = _host.Machine;
        if (m == null) { StopObservation(); return; }
        if (!ReferenceEquals(m, _machine) || _media != _host.LoadedMedia || _mediaRevision != _host.MediaRevision)
            SwitchGame(m);
        PollExplain(m);
        PollModificationAdvice();
        PollAutonomousAnalysis();
        if (_open && _host.Fusion == null && m.Memory.AddressBits <= 16) _observation ??= new ModificationObservation(m);
        else StopObservation();
    }

    public void Draw()
    {
        Update();
        var machine = _host.Machine;
        if (machine == null || !_open) { _modificationPanelLeft = null; return; }
        WorkspaceLayout.NextEditor();
        bool visible = ImGui.Begin(UiText.Label("Modificar y analizar###game-tools"), ref _open, WorkspaceLayout.EditorFlags);
        if (!visible)
        { _modificationPanelLeft = null; ImGui.End(); if (!_open) StopObservation(); return; }
        _modificationPanelLeft = null;
        UiTheme.Eyebrow(UiText.Get("ANALIZAR / ") + machine.DisplayName.ToUpperInvariant());
        UiTheme.Heading(_gameName, "Buscá datos, entendé las rutinas y probá cambios sobre la máquina emulada.", localizeTitle: false);
        if (_status.Length > 0)
        {
            ImGui.TextWrapped(UiText.Get(_status));
            if (ImGui.SmallButton(UiText.Label("Copiar mensaje"))) ImGui.SetClipboardText(_status);
        }
        if (_notes.Problem is { } problem) UiTheme.Hint(problem);
        if (_notes.CanRecover)
        {
            UiTheme.Hint("Para guardar estas anotaciones, conservá primero una copia del archivo anterior.");
            if (ImGui.Button(UiText.Label("Recuperar anotaciones"))) _notes.Recover(_gameId);
        }
        if (_notes.RecoveryPath is { } backup)
        { UiTheme.Hint("Se conservó un respaldo de las anotaciones anteriores."); if (ImGui.IsItemHovered()) UiTheme.Tooltip(backup); }
        if (_requestedPage is { } requested) { _activePage = requested; _requestedPage = null; }
        bool variablesSupported = machine.Memory.AddressBits <= 16;
        float pageWidth = new[] { "Cambios", "Análisis con IA", "Buscar variables", "Código" }
            .Max(label => ImGui.CalcTextSize(UiText.Get(label)).X) + ImGui.GetStyle().CellPadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
        int pageColumns = AnalysisNavigationColumns(ImGui.GetContentRegionAvail().X, pageWidth);
        if (variablesSupported && pageColumns == 1)
        {
            int page = (int)_activePage;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##pagina-herramientas", ref page, new[] { "Cambios", "Análisis con IA", "Buscar variables", "Código" }.Select(UiText.Get).ToArray(), 4)) _activePage = (ToolPage)page;
        }
        else if (variablesSupported && ImGui.BeginTable("navegacion-herramientas", pageColumns, ImGuiTableFlags.SizingStretchSame))
        {
            Page("Cambios", ToolPage.Changes);
            Page("Análisis con IA", ToolPage.Analysis);
            Page("Buscar variables", ToolPage.Search);
            Page("Código", ToolPage.Code);
            ImGui.EndTable();
        }
        if (variablesSupported) DrawHistoryOptions();
        ImGui.Separator();
        if (!variablesSupported)
        {
            AiPicker.Hint("En PC/XT están disponibles el código, los parches y el depurador. El análisis de variables sigue pendiente.");
            DrawCodeTools(machine);
        }
        else switch (_activePage)
        {
            case ToolPage.Changes: DrawModifications(machine); break;
            case ToolPage.Analysis: DrawModificationAnalysis(machine); break;
            case ToolPage.Search: DrawFinder(machine); break;
            case ToolPage.Code: DrawCodeTools(machine); break;
        }
        ImGui.End();
        if (!_open) StopObservation();

        void Page(string label, ToolPage page)
        {
            ImGui.TableNextColumn();
            if (ImGui.Selectable(UiText.Label(label), _activePage == page, ImGuiSelectableFlags.None, new Vector2(0, UiTheme.Px(30)))) _activePage = page;
        }
    }

    private void DrawCodeTools(IMachine machine)
    {
        AiPicker.Hint("Herramientas técnicas: rutinas desensambladas, parches en memoria y perfil del original.");
        if (_map == null && machine.ExecutedMap != null && machine.FrameCount > 30) Analyze(machine);
        if (ImGui.Button(UiText.Label(_map == null ? "Reconstruir rutinas" : "Actualizar rutinas"))) Analyze(machine);
        if (_map != null)
        {
            ImGui.SameLine();
            if (ImGui.Button(UiText.Label("Exportar .asm"))) _status = UiText.Get("Guardado en ") + Export(machine);
        }
        if (_codeSummary.Length > 0) AiPicker.Hint(_codeSummary);
        if (!ImGui.BeginTabBar("herramientas-tecnicas")) return;
        if (ImGui.BeginTabItem(UiText.Label("Rutinas"))) { DrawRoutines(machine); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem(UiText.Label("Parches"))) { DrawPatches(machine); ImGui.EndTabItem(); }
        if (machine.Memory.AddressBits <= 16 && ImGui.BeginTabItem(UiText.Label("Perfil y controles"))) { DrawVariables(machine); ImGui.EndTabItem(); }
        ImGui.EndTabBar();
    }

    // ------------------------------------------------------------------ game identity
    private void SwitchGame(IMachine m)
    {
        StopAutonomousSession();
        RetireWork(ref _explainTask, ref _cts);
        RetireWork(ref _modificationTask, ref _modificationCts);
        StopObservation();
        _modifications?.Dispose();
        _proposalFeedback.Clear();
        _analysisSnapshot = null; _lastAnalysis = ""; _modificationWish = "";
        _analysisProvider = _lastAnalysisProvider = _analysisPrompt = _analysisAnswer = _analysisError = "";
        _analysisReply = null; _analysisAttempt++; _lastAnalysisAttempt = -1;
        _discoveredLabels.Clear(); _manualExplanation = false;
        _modificationActivity.Reset();
        _routineActivity.Reset();
        _mediaRevision = _host.MediaRevision;
        _advice = null;
        _manualModification = false;
        _modificationPasted = "";
        _variableInputs.Clear();
        _explainStatus = "";
        _machine = m;
        _media = _host.LoadedMedia;
        _map = null; _codeSummary = "";
        _selected = null;
        _line = null;
        _back.Clear();
        _search = null;
        (_gameId, _gameName, _profile) = Identify(m);
        if (_profile != null) _profile = GameProfile.Parse(_profile.ToJson());
        _modifications = _profile == null ? null : new GameModifications(m, _profile);
        _notes = CodeAnnotations.Load(_gameId);
        _bootText = string.Join("\n", _profile?.Boot ?? DefaultBoot(m));
        _status = "";
        LoadModificationHistory();
        if (_open)
            m.ExecutedMap ??= new bool[1 << Math.Clamp(m.Memory.AddressBits, 1, 20)];
    }

    private (string id, string name, GameProfile? profile) Identify(IMachine m)
    {
        if (_host.Fusion is { } f && f.Slots.FirstOrDefault(s => ReferenceEquals(s.Machine, m)) is { } slot)
            return (slot.Profile.Id, slot.Profile.Name, slot.Profile);
        if (m.Id == "invaders")
        {
            var p = _host.LoadedProfile ?? ProfileLibrary.Find("invaders");
            return (p?.Id ?? "invaders", p?.Name ?? "Space Invaders", p);
        }
        if (_host.LoadedMedia is { } media)
        {
            var p = _host.LoadedProfile;
            if (p != null)
                return (p.Id, p.Name, p);
            string name = Path.GetFileNameWithoutExtension(media);
            string id = Slug(name);
            return (id, name, null);
        }
        return (m.Id, m.DisplayName + UiText.Get(" (sin juego)"), null);
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        return Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
    }

    private static List<string> DefaultBoot(IMachine m) =>
        m is Spectrum48 ? new() { "load-tape", "wait 200" } : new() { "wait 200" };

    /// <summary>The system ROM (not part of the game) is hidden from the routine list by default.</summary>
    private static uint SystemRomEnd(IMachine m) => m is Spectrum48 ? 0x4000u : 0u;

    internal static (uint start,uint end) SearchRange(string machine)
    {
        var range=Retro.Compat.MemoryLab.RangeFor(machine);
        return ((uint)range.start,(uint)range.end);
    }
    private static (uint start,uint end) RamRange(IMachine m) => SearchRange(m.Id);

    // ------------------------------------------------------------------ analysis
    private void Analyze(IMachine m)
    {
        m.ExecutedMap ??= new bool[1 << Math.Clamp(m.Memory.AddressBits, 1, 20)];
        var entries = m.CodeEntryPoints.Append(m.Cpu.ProgramCounter);
        int firstGame = -1; // Add an observed game entry separately from ROM fall-through.
        for (int address = (int)SystemRomEnd(m); address < m.ExecutedMap.Length; address++)
            if (m.ExecutedMap[address]) { firstGame = address; break; }
        if (firstGame >= 0) entries = entries.Append((uint)firstGame);
        _map = CodeMap.Build(a => m.Memory.Peek(a), m.Flow, entries, m.ExecutedMap);
        int executed = m.ExecutedMap.Count(x => x);
        int game = _map.Routines.Keys.Count(a => a >= SystemRomEnd(m));
        _codeSummary = UiText.Format($"{_map.Routines.Count} rutinas ({game} del juego), {_map.Code.Count} instrucciones; ") +
                  UiText.Format($"{executed} se ejecutaron desde que abriste esta ventana. Jugá un rato y actualizá para encontrar más.");
        if (_selected is uint s && !_map.Routines.ContainsKey(s))
            _selected = null;
        // start on the game's busiest routine
        _selected ??= _map.Routines.Values
            .Where(r => r.Executed && r.Start >= SystemRomEnd(m))
            .OrderByDescending(r => r.Callers.Count).ThenBy(r => r.Start)
            .FirstOrDefault()?.Start;
    }

    private string Name(IMachine m, uint a) =>
        _notes.Label(a) ?? m.SymbolFor(a) ?? (_map?.Routines.ContainsKey(a) == true ? UiText.Format($"sub_{a:X4}") : UiText.Format($"${a:X4}"));

    private string? VariableAt(uint a)
    {
        if (_profile == null)
            return null;
        foreach (var (name, v) in _profile.Variables)
        {
            uint va = v.AddressValue;
            int len = v.Encoding is "bcd" or "ascii" ? v.Bytes : v.Encoding == "uint16" ? 2 : 1;
            if (a >= va && a < va + len)
                return a == va ? name : UiText.Format($"{name}+{a - va}");
        }
        return null;
    }

    /// <summary>Instruction text with addresses replaced by routine and variable names.</summary>
    private string Pretty(IMachine m, string text) =>
        AddressToken.Replace(text, match =>
        {
            uint a = uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber);
            return _notes.Label(a) ?? VariableAt(a) ?? (_map?.Routines.ContainsKey(a) == true ? UiText.Format($"sub_{a:X4}") : match.Value);
        });

    private void Select(uint start, bool remember = true)
    {
        if (remember && _selected is uint prev && prev != start)
            _back.Push(prev);
        _selected = start;
        _line = null;
        _rename = "";
        _pasted = "";
    }

    // ------------------------------------------------------------------ routines tab
    private void DrawRoutines(IMachine m)
    {
        if (_map == null)
        {
            ImGui.TextWrapped(UiText.Get("Jugá un momento y pulsá Reconstruir rutinas. Para investigar cambios con IA usá la pestaña Analizar con IA."));
            return;
        }
        bool wide=ImGui.GetContentRegionAvail().X>=UiTheme.Px(700);
        float listWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * 0.26f, UiTheme.Px(160), UiTheme.Px(280));
        if (!wide)
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##compact-routines",UiText.Get(_selected is uint selected?Name(m,selected):"Elegí una rutina")))
            {
                foreach(var entry in _map.Routines.Values.OrderBy(r=>r.Start))
                    if (ImGui.Selectable(UiText.Label(UiText.Format($"{entry.Start:X4} {Name(m,entry.Start)}")),_selected==entry.Start)) Select(entry.Start);
                ImGui.EndCombo();
            }
        }
        if (wide)
        {
        ImGui.BeginChild("routinelist", new Vector2(listWidth, Math.Max(UiTheme.Px(240),ImGui.GetContentRegionAvail().Y)), ImGuiChildFlags.Borders);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filtro", UiText.Get("Buscar rutina"), ref _filter, 64);
        ImGui.Checkbox(UiText.Label("Solo ejecutadas"), ref _onlyExecuted);
        if (SystemRomEnd(m) > 0)
            ImGui.Checkbox(UiText.Label("Ocultar ROM del sistema"), ref _hideSystemRom);
        ImGui.Separator();
        ImGui.BeginChild("routinescroll");
        foreach (var r in _map.Routines.Values.OrderBy(r => r.Start))
        {
            if (_onlyExecuted && !r.Executed)
                continue;
            if (_hideSystemRom && r.Start < SystemRomEnd(m))
                continue;
            string name = Name(m, r.Start);
            if (_filter.Length > 0 && !name.Contains(_filter, StringComparison.OrdinalIgnoreCase) &&
                !r.Start.ToString("X4").Contains(_filter, StringComparison.OrdinalIgnoreCase))
                continue;
            bool explained = _notes.Summaries.ContainsKey(CodeAnnotations.Key(r.Start));
            ImGui.PushStyleColor(ImGuiCol.Text, r.Executed ? (explained ? CommentColor : new Vector4(1, 1, 1, 1)) : DimColor);
            if (ImGui.Selectable(UiText.Label(UiText.Format($"{r.Start:X4}  {name}##{r.Start}")), _selected == r.Start))
                Select(r.Start);
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered() && _notes.Summaries.TryGetValue(CodeAnnotations.Key(r.Start), out var sum))
                UiTheme.Tooltip(sum);
        }
        ImGui.EndChild();
        ImGui.EndChild();
        ImGui.SameLine();
        }
        ImGui.BeginChild("routinedetail", new(0,Math.Max(UiTheme.Px(240),ImGui.GetContentRegionAvail().Y)), ImGuiChildFlags.Borders);
        if (_selected is uint sel && _map.Routines.TryGetValue(sel, out var routine))
            DrawRoutine(m, routine);
        else
            ImGui.TextWrapped(UiText.Get("Elegí una rutina de la lista. En blanco las que se ejecutaron, en gris las que todavía no, en verde las que ya tienen explicación."));
        ImGui.EndChild();
    }

    private void DrawRoutine(IMachine m, Routine r)
    {
        string key = CodeAnnotations.Key(r.Start);
        if (_back.Count > 0)
        {
            if (ImGui.Button(UiText.Label("< Volver")))
                Select(_back.Pop(), remember: false);
            ImGui.SameLine();
        }
        ImGui.TextColored(LabelColor, UiText.Get(UiText.Format($"{Name(m, r.Start)}  (${r.Start:X4}-${r.End:X4}, {r.Instructions.Count} instrucciones)")));
        if (_rename.Length == 0)
            _rename = _notes.Label(r.Start) ?? "";
        ImGui.SetNextItemWidth(Math.Max(UiTheme.Px(60), ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(UiText.Get("Renombrar")).X - ImGui.GetStyle().FramePadding.X*2 - ImGui.GetStyle().ItemSpacing.X));
        if (ImGui.InputTextWithHint("##nombre", UiText.Get("nombre nuevo"), ref _rename, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            SetLabel(r.Start, _rename);
        ImGui.SameLine();
        if (ImGui.Button(UiText.Label("Renombrar")))
            SetLabel(r.Start, _rename);
        if (ImGui.Button(UiText.Label("Ver en el depurador")))
            _showInDebugger(r.Start);
        bool bp = _host.Debug.IsBreakpoint(r.Start);
        UiTheme.SameLineIfFits(bp ? "Quitar breakpoint" : "Breakpoint al entrar");
        ImGui.BeginDisabled(_host.Fusion != null);
        if (ImGui.Button(UiText.Label(bp ? "Quitar breakpoint" : "Breakpoint al entrar")))
            _host.Debug.Toggle(r.Start);
        ImGui.EndDisabled();
        if (_host.Fusion != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiTheme.Tooltip("Durante una fusión los juegos no se detienen en breakpoints.");
        UiTheme.SameLineIfFits("Copiar referencia");
        if (ImGui.Button(UiText.Label("Copiar referencia")))
        {
            ImGui.SetClipboardText(RoutineReference(m, r));
            _status = "Referencia copiada con direcciones y huellas; sin bytes del original.";
        }

        if (_notes.Summaries.TryGetValue(key, out var summary))
            ImGui.TextWrapped(UiText.Get(summary));
        DrawRoutineLinks(m, "Llama a:", r.Calls);
        DrawRoutineLinks(m, "La llaman:", r.Callers);
        if (ImGui.CollapsingHeader(UiText.Label("Explicación con IA"),IsWorking?ImGuiTreeNodeFlags.DefaultOpen:ImGuiTreeNodeFlags.None)) DrawExplain(m, r);
        ImGui.Separator();

        if (ImGui.BeginTabBar("routinetabs"))
        {
            if (ImGui.BeginTabItem(UiText.Label("Código")))
            {
                DrawListing(m, r);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(UiText.Label("Pseudocódigo")))
            {
                string code = _notes.Pseudocode.TryGetValue(key, out var p) ? p : "";
                ImGui.TextDisabled(UiText.Get(code.Length == 0 ? "Todavía no hay pseudocódigo: pedíselo a la IA o escribilo vos." : "Podés corregirlo: se guarda solo."));
                if (ImGui.InputTextMultiline("##pseudo", ref code, 20000, new Vector2(-1, -1)))
                {
                    _notes.Pseudocode[key] = code;
                    _notes.Save(_gameId);
                }
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawRoutineLinks(IMachine m, string title, HashSet<uint> targets)
    {
        if (targets.Count == 0)
            return;
        ImGui.TextDisabled(UiText.Get(title));
        foreach (var t in targets.OrderBy(x => x).Take(24))
        {
            UiTheme.SameLineIfFits(Name(m,t));
            if (ImGui.SmallButton(UiText.Label(UiText.Format($"{Name(m, t)}##{title}{t}"))))
                Select(t);
        }
        if (targets.Count > 24)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(UiText.Get(UiText.Format($"y {targets.Count - 24} más")));
        }
    }

    private void SetLabel(uint a, string name)
    {
        name = Regex.Replace(name.Trim(), @"\s+", "_");
        if (name.Length == 0)
            _notes.Labels.Remove(CodeAnnotations.Key(a));
        else
            _notes.Labels[CodeAnnotations.Key(a)] = name;
        _notes.Save(_gameId);
    }

    private void DrawListing(IMachine m, Routine r)
    {
        using var font = UiTheme.FixedWidth();
        float editHeight = _line != null ? UiTheme.Px(130) : 0;
        ImGui.BeginChild("listing", new Vector2(0, Math.Max(UiTheme.Px(140),ImGui.GetContentRegionAvail().Y-editHeight)), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        uint? expected = null;
        uint pc = m.Cpu.ProgramCounter;
        foreach (var a in r.Instructions.OrderBy(x => x))
        {
            if (expected is uint e && e != a)
                ImGui.TextDisabled(UiText.Get("        ..."));
            var ins = m.Disassembler.Disassemble(x => m.Memory.Peek(x), a);
            expected = (a + (uint)ins.Length) & ((1u << m.Memory.AddressBits) - 1);
            string? label = _notes.Label(a) ?? m.SymbolFor(a) ?? (a == r.Start ? UiText.Format($"sub_{a:X4}") : null);
            if (a == r.Start || label != null)
                ImGui.TextColored(LabelColor, UiText.Get((a == r.Start ? Name(m, a) : label) + ":"));
            bool executed = m.ExecutedMap?[a] == true;
            string bytes = string.Join(" ", ins.Bytes.Select(b => b.ToString("X2")));
            string line = UiText.Format($"{(a == pc ? UiText.Get(">") : UiText.Get(" "))} {a:X4}  {bytes,-12} {Pretty(m, ins.Text)}");
            ImGui.PushStyleColor(ImGuiCol.Text, executed ? new Vector4(1, 1, 1, 1) : DimColor);
            if (ImGui.Selectable(UiText.Label(line + "##" + a), _line == a, ImGuiSelectableFlags.AllowDoubleClick))
            {
                _line = a;
                _commentText = _notes.Comment(a) ?? "";
                _patchHex = bytes;
                var f = m.Flow.Analyze(x => m.Memory.Peek(x), a);
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && f.Target is uint t && _map!.Routines.ContainsKey(t))
                    Select(t);
            }
            ImGui.PopStyleColor();
            if (_notes.Comment(a) is { } c)
            {
                ImGui.SameLine();
                ImGui.TextColored(CommentColor, UiText.Get("; " + c));
            }
        }
        ImGui.EndChild();
        if (_line is uint sel)
            DrawLineEditor(m, r, sel);
    }

    private string RoutineReference(IMachine machine, Routine routine) => CodeExplainer.Reference(machine, routine,
        _host.Fusion == null ? _host.LoadedMediaHashes : [],
        _host.Fusion == null ? _host.FirmwareIdentity : "No asociada a esta ranura desde el visor; consultá las referencias del proyecto.", _gameName);

    private void DrawLineEditor(IMachine m, Routine r, uint a)
    {
        ImGui.Separator();
        ImGui.TextUnformatted(UiText.Get(UiText.Format($"Línea ${a:X4}  (doble clic en un CALL o salto para ir a esa rutina)")));
        ImGui.SetNextItemWidth(-130);
        if (ImGui.InputTextWithHint("##comentario", UiText.Get("comentario"), ref _commentText, 200, ImGuiInputTextFlags.EnterReturnsTrue))
            SetComment(a, _commentText);
        ImGui.SameLine();
        if (ImGui.Button(UiText.Label("Guardar comentario")))
            SetComment(a, _commentText);
        ImGui.SetNextItemWidth(UiTheme.Px(200));
        ImGui.InputText(UiText.Label("bytes nuevos (hex)"), ref _patchHex, 64);
        ImGui.SameLine();
        if (ImGui.Button(UiText.Label("Cambiar")))
            Patch(m, a, _patchHex);
        ImGui.SameLine();
        if (ImGui.Button(UiText.Label("Anular (NOP)")))
        {
            var ins = m.Disassembler.Disassemble(x => m.Memory.Peek(x), a);
            Patch(m, a, string.Join(" ", Enumerable.Repeat(NopOpcode(m).ToString("X2"), ins.Length)));
        }
        if (ImGui.IsItemHovered())
            UiTheme.Tooltip("Reemplaza la instrucción por NOP: la CPU deja de ejecutar esa operación.");
    }

    private static byte NopOpcode(IMachine m) => m.Cpu is Retro.Cpu.M6502.M6502 ? (byte)0xEA : m.Cpu is Retro.Cpu.I8088.I8088Cpu ? (byte)0x90 : (byte)0x00;

    private void SetComment(uint a, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            _notes.Comments.Remove(CodeAnnotations.Key(a));
        else
            _notes.Comments[CodeAnnotations.Key(a)] = text.Trim();
        _notes.Save(_gameId);
    }

    // ------------------------------------------------------------------ code changes
    private void Patch(IMachine m, uint a, string hex)
    {
        byte[] bytes;
        try
        {
            bytes = hex.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(h => byte.Parse(h, NumberStyles.HexNumber)).ToArray();
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            _status = "Bytes inválidos: usá pares hexadecimales separados por espacios (ej. 3E 05).";
            return;
        }
        if (bytes.Length == 0)
            return;
        string key = CodeAnnotations.Key(a);
        byte[]? previous = null;
        int length = bytes.Length;
        try
        {
            if (_notes.Patches.TryGetValue(key, out var old))
            {
                var parts = old.Split('|');
                if (parts.Length != 2 || (previous = Hex(parts[0])).Length != Hex(parts[1]).Length)
                { _status = "El respaldo de este parche está incompleto. Conservá las anotaciones y volvé a cargar el original antes de crear otro parche."; return; }
                length = Math.Max(length, previous.Length);
            }
            if ((ulong)a + (uint)length > 1UL << m.Memory.AddressBits)
            { _status = "El parche sale del espacio de direcciones. No se cambió la partida."; return; }
            foreach (var other in _notes.Patches.Where(other => other.Key != key))
            {
                uint start = uint.Parse(other.Key, NumberStyles.HexNumber);
                var parts = other.Value.Split('|');
                if (parts.Length != 2) throw new FormatException();
                int count = Math.Max(Hex(parts[0]).Length, Hex(parts[1]).Length);
                if ((ulong)a < (ulong)start + (uint)count && (ulong)start < (ulong)a + (uint)length)
                { _status = "El parche se superpone con otro guardado. Deshacé el anterior antes de modificar ese rango."; return; }
            }
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        { _status = "Hay un parche guardado inválido. Conservá las anotaciones y revisá sus bytes antes de continuar."; return; }
        byte[] original = Enumerable.Range(0, length).Select(i => m.Memory.Peek(a + (uint)i)).ToArray();
        previous?.CopyTo(original, 0);
        // A shorter edit restores the old tail; an extension preserves every newly covered original byte.
        byte[] replacement = (byte[])original.Clone(); bytes.CopyTo(replacement, 0);
        if (!Write(m, a, replacement))
        {
            _status = "No se aplicó el parche: una dirección no admite escritura. Se restauró la partida anterior; revisá el rango.";
            return;
        }
        _notes.Patches[key] = string.Join(" ", original.Select(b => b.ToString("X2"))) + "|" + string.Join(" ", replacement.Select(b => b.ToString("X2")));
        _notes.Save(_gameId);
        _status = UiText.Format($"Cambiado ${a:X4}. El juego ya corre con el cambio (el archivo original no se toca).");
        Analyze(m);
    }

    private static bool Write(IMachine m, uint a, byte[] bytes)
    {
        if (bytes.Length == 0 || m.Memory.AddressBits is < 1 or > 32 || (ulong)a + (uint)bytes.Length > 1UL << m.Memory.AddressBits) return false;
        byte[] original = Enumerable.Range(0, bytes.Length).Select(i => m.Memory.Peek(a + (uint)i)).ToArray();
        byte[] state = m.SaveState();
        int[] picture = (int[])m.Video.Pixels.Clone();
        bool committed = false;
        try
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                uint ad = a + (uint)i;
                if (m is ICodePatch p) p.PatchCode(ad, bytes[i]);
                else m.Memory.Poke(ad, bytes[i]);
                if (m.Memory.Peek(ad) != bytes[i]) return false;
            }
            committed = true;
            return true;
        }
        finally
        {
            if (!committed)
            {
                // Save states need not include editable program ROM; restore that copy explicitly as well.
                if (m is ICodePatch patch)
                    for (int i = 0; i < original.Length; i++) patch.PatchCode(a + (uint)i, original[i]);
                m.LoadState(state);
                picture.CopyTo(m.Video.Pixels, 0);
            }
        }
    }

    private static byte[] Hex(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => byte.Parse(h, NumberStyles.HexNumber)).ToArray();

    private void DrawPatches(IMachine m)
    {
        ImGui.TextWrapped(UiText.Get("Los parches modifican la partida en memoria. Podés guardarlos y volver a aplicarlos al cargar el juego."));
        if (_notes.Patches.Count == 0)
        {
            ImGui.TextDisabled(UiText.Get("Todavía no hiciste cambios. En la pestaña Rutinas elegí una línea y cambiá sus bytes o anulala."));
            return;
        }
        if (ImGui.Button(UiText.Label("Aplicar todos de nuevo")))
        {
            int applied = 0;
            foreach (var (key, value) in _notes.Patches)
                if (Write(m, uint.Parse(key, NumberStyles.HexNumber), Hex(value.Split('|')[1]))) applied++;
            _status = UiText.Format($"Parches aplicados: {applied} de {_notes.Patches.Count}. Revisá los rangos que no admiten escritura.");
        }
        ImGui.Separator();
        foreach (var (key, value) in _notes.Patches.OrderBy(p => p.Key).ToList())
        {
            uint a = uint.Parse(key, NumberStyles.HexNumber);
            var parts = value.Split('|');
            bool applied = Hex(parts[1]).Select((b, i) => m.Memory.Peek((uint)(a + i)) == b).All(x => x);
            ImGui.TextUnformatted(UiText.Get(UiText.Format($"${key}  {parts[0]}  ->  {parts[1]}   {(applied ? UiText.Get("(aplicado)") : UiText.Get("(no aplicado)"))}")));
            if (_map?.RoutineAt(a) is { } r)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(UiText.Label(UiText.Format($"{Name(m, r.Start)}##p{key}"))))
                    Select(r.Start);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton(UiText.Label(UiText.Get("Deshacer##") + key)))
            {
                if (Write(m, a, Hex(parts[0])))
                { _notes.Patches.Remove(key); _notes.Save(_gameId); _status = "Parche deshecho; se restauraron los bytes originales."; Analyze(m); }
                else _status = "No se pudo deshacer el parche. La partida y el respaldo se conservaron; volvé a cargar el original.";
            }
        }
    }

    // ------------------------------------------------------------------ AI
    private void DrawExplain(IMachine m, Routine r)
    {
        AiPicker.Choose(_ai, "rutina", "Explicar la rutina usa un pedido a la IA elegida.");
        var provider = AiPicker.IsApi(_ai.Provider) ? _ai.Provider : null;
        bool busy = _explainTask != null;
        if (provider != null)
        {
            if (ImGui.Button(UiText.Label(busy ? "La IA está leyendo..." : "Explicar esta rutina")) && !busy)
            {
                try
                {
                    var ai = _ai.CreateProvider()!;
                    string prompt = CodeExplainer.Prompt(m, r, _notes, _profile, _map!) + "\n\n" + RoutineReference(m, r);
                    _explainFor = r.Start;
                    _explainRevision = _mediaRevision;
                    _explainMachine = m;
                    _cts = new CancellationTokenSource();
                    var token = _cts.Token;
                    _explainStatus = "Pidiendo la explicación...";
                    _routineActivity.Start(ai);
                    _explainTask = Task.Run(() => CodeExplainer.ExplainAsync(ai, prompt, token));
                }
                catch (Exception error) when (error is AiException or InvalidOperationException or IOException)
                { _explainStatus = error.Message; }
            }
            if (busy)
            {
                ImGui.SameLine();
                if (ImGui.Button(UiText.Label("Cancelar")))
                    _cts?.Cancel();
            }
            ImGui.SameLine();
        }
        if (ImGui.Button(UiText.Label("Copiar pedido")))
        {
            ImGui.SetClipboardText(CodeExplainer.ManualPrompt(CodeExplainer.Prompt(m, r, _notes, _profile, _map!) + "\n\n" + RoutineReference(m, r)));
            _manualExplanation = true;
            _explainStatus = "Pedido copiado. Pegalo en tu IA y traé la respuesta completa.";
        }
        if (ImGui.IsItemHovered())
            UiTheme.Tooltip("El selector de IA de esta sección también se usa para explicar rutinas.");
        if (_explainStatus.Length > 0)
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1, 1), UiText.Get(_explainStatus));
        _routineActivity.Draw(_explainTask != null, _explainStatus, _cts);
        if (_manualExplanation)
        {
            ImGui.InputTextMultiline("##respuestaia", ref _pasted, 20000, new Vector2(-1, UiTheme.Px(60)));
            if (ImGui.Button(UiText.Label("Usar la respuesta")))
            {
                try
                {
                    CodeExplainer.Apply(CodeExplainer.Parse(_pasted), r, _notes);
                    _notes.Save(_gameId);
                    _rename = "";
                    _pasted = ""; _manualExplanation = false;
                    _explainStatus = "Listo: nombre, explicación, pseudocódigo y comentarios guardados.";
                }
                catch (AiException e)
                {
                    _explainStatus = UiText.Get("Consigna copiada, pero la respuesta no sirve: ") + e.Message;
                }
            }
        }
    }

    private void PollExplain(IMachine m)
    {
        if (_explainTask is not { IsCompleted: true } t)
            return;
        _explainTask = null;
        _routineActivity.Stop();
        _cts?.Dispose(); _cts = null;
        if (_explainRevision != _mediaRevision || !ReferenceEquals(m, _explainMachine))
        {
            _ = t.Exception;
            return;
        }
        if (t.IsCanceled || t.Exception?.GetBaseException() is OperationCanceledException)
        {
            _explainStatus = "Cancelado.";
            return;
        }
        if (t.IsFaulted)
        {
            _explainStatus = UiText.Get("Error: ") + t.Exception!.GetBaseException().Message;
            return;
        }
        if (_map != null && _map.Routines.TryGetValue(_explainFor, out var r))
        {
            CodeExplainer.Apply(t.Result, r, _notes);
            _notes.Save(_gameId);
            _rename = "";
            _explainStatus = UiText.Format($"Listo: {UiText.Raw(t.Result.Name)}. Nombre, explicación, pseudocódigo y comentarios guardados.");
        }
    }

    // ------------------------------------------------------------------ variables
    private void ApplyFrozen(IMachine m)
    {
        if (ReferenceEquals(m, _machine) && _mediaRevision == _host.MediaRevision)
            _modifications?.Tick();
    }

    private void DrawVariables(IMachine m)
    {
        if (_profile == null)
        {
            ImGui.TextWrapped(UiText.Get("Este juego no tiene perfil. Usá Buscar variables para identificar sus datos."));
            return;
        }
        ImGui.TextWrapped(UiText.Get(_profile.Description));
        ImGui.Separator();
        ImGui.TextUnformatted(UiText.Get("Controles para las fusiones (qué tecla del juego es cada acción):"));
        var keys = m.Keys.Select(k => k.Name).ToArray();
        var options = new[] { "(ninguna)" }.Concat(keys).ToArray();
        bool changed = false;
        foreach (var input in Retro.Studio.StudioSession.InputNames)
        {
            int index = _profile.Controls.TryGetValue(input, out var k) ? Array.IndexOf(keys, k) + 1 : 0;
            ImGui.SetNextItemWidth(UiTheme.Px(160));
            if (ImGui.Combo(input, ref index, options, options.Length))
            {
                if (index == 0) _profile.Controls.Remove(input);
                else _profile.Controls[input] = keys[index - 1];
                changed = true;
            }
        }
        ImGui.TextUnformatted(UiText.Get("Arranque (una orden por línea: load-tape, wait N, press TECLA N):"));
        if (ImGui.InputTextMultiline("##boot", ref _bootText, 2000, new Vector2(-1, UiTheme.Px(80))))
        {
            _profile.Boot = _bootText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            changed = true;
        }
        if (changed) SaveProfile();
        AiPicker.Hint("N indica la duración en cuadros. Los cambios se guardan automáticamente en el perfil.");
    }

    private void DrawVariableValues(IMachine m, bool addresses)
    {
        if (_profile == null) return;
        bool compact = ImGui.GetContentRegionAvail().X < UiTheme.Px(860);
        if (!compact) ScrollAnalysisAutotest("values");
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
        var size = new Vector2(0, Math.Clamp(ImGui.GetWindowHeight() * 0.32f, UiTheme.Px(130), UiTheme.Px(245)));
        if (compact)
        {
            foreach (var (name, variable) in _profile.Variables) DrawValue(name, variable);
        }
        else if (ImGui.BeginTable("vars", addresses ? 6 : 5, flags, size))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn(UiText.Get("Variable"), ImGuiTableColumnFlags.WidthStretch, 2);
            if (addresses) ImGui.TableSetupColumn(UiText.Get("Dirección"), ImGuiTableColumnFlags.WidthFixed, UiTheme.Px(110));
            ImGui.TableSetupColumn(UiText.Get("Valor"), ImGuiTableColumnFlags.WidthFixed, UiTheme.Px(90));
            ImGui.TableSetupColumn(UiText.Get("RAM actual → propuesta"), ImGuiTableColumnFlags.WidthStretch, 3);
            ImGui.TableSetupColumn(UiText.Get("Congelar"), ImGuiTableColumnFlags.WidthFixed, UiTheme.Px(90));
            ImGui.TableSetupColumn(UiText.Get("Qué es"), ImGuiTableColumnFlags.WidthStretch, 3);
            ImGui.TableHeadersRow();
            foreach (var (name, v) in _profile.Variables)
            {
                ImGui.TableNextRow();
                DrawValue(name, v);
            }
            ImGui.EndTable();
        }
        AiPicker.Hint("Revisá los bytes antes de Enter o Aplicar. La prueba modifica RAM temporalmente; Deshacer y Restaurar partida recuperan el estado anterior.");

        void Field(string label)
        {
            if (compact) ImGui.TextWrapped(UiText.Get(label));
            else ImGui.TableNextColumn();
        }

        void DrawValue(string name, GameVariable v)
        {
                int value = 0; bool readable = true;
                try { value = v.Read(m.Memory); }
                catch (Exception error) when (error is FormatException or OverflowException or IndexOutOfRangeException or ArgumentException or InvalidDataException)
                { readable = false; }
                string title = VariableLabel(name) + (v.PurposeIsHypothesis ? " · hipótesis" : "");
                if (compact) UiTheme.Section(title);
                else { ImGui.TableNextColumn(); ImGui.TextWrapped(UiText.Get(title)); }
                if (ImGui.IsItemHovered()) UiTheme.Tooltip(name + "\n" + v.PurposeSummary + "\n" + v.Description);
                if (addresses)
                {
                    Field("Dirección / formato");
                    using (UiTheme.FixedWidth()) ImGui.TextUnformatted(UiText.Get("$" + v.Address));
                    ImGui.TextWrapped(UiText.Get(v.Encoding));
                }
                Field("Valor propuesto");
                ImGui.SetNextItemWidth(compact ? Math.Min(UiTheme.Px(160), ImGui.GetContentRegionAvail().X) : -1);
                ImGui.BeginDisabled(v.ReadOnly || !readable);
                string input = _variableInputs.GetValueOrDefault(name, value.ToString(CultureInfo.InvariantCulture));
                // InputScalar/InputInt rejects EnterReturnsTrue in this ImGui version.
                // Keep an explicit text draft so a partial number never writes RAM.
                bool enter = ImGui.InputText(UiText.Label(UiText.Get("##v") + name), ref input, 16, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.EnterReturnsTrue);
                if (ImGui.IsItemActive()) _variableInputs[name] = input;
                else _variableInputs.Remove(name);
                IReadOnlyList<VariableBytePreview>? preview = null;
                string previewProblem = "";
                bool parsed = int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int requested);
                if (parsed && readable && !v.ReadOnly)
                {
                    try { preview = PreviewVariable(_profile, name, requested, m.Memory); }
                    catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException or FormatException or OverflowException or IndexOutOfRangeException)
                    { previewProblem = error.Message; }
                }
                else if (!parsed) previewProblem = "Escribí un número entero válido.";
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiTheme.Tooltip(UiText.Format($"Rango: {v.Min} a {v.Max}"));
                ImGui.BeginDisabled(preview == null);
                bool apply = ImGui.SmallButton(UiText.Label(UiText.Get("Aplicar##") + name));
                ImGui.EndDisabled();
                ImGui.EndDisabled();
                if (enter || apply)
                {
                    if (preview != null) ApplyModifications([new(name, requested, _modifications?.Frozen.ContainsKey(name) == true)]);
                    else _status = previewProblem.Length > 0 ? previewProblem : "No se pudo previsualizar esta codificación. No se aplicó el cambio.";
                }
                if (compact) ScrollAnalysisAutotest("values");
                Field("RAM actual → propuesta");
                if (preview != null)
                {
                    using (UiTheme.FixedWidth())
                        foreach (var cell in preview)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, cell.Before == cell.After ? UiTheme.Muted : UiTheme.Accent);
                            ImGui.TextWrapped(UiText.Get(UiText.Format($"${cell.Address:X4}: {cell.Before:X2} → {cell.After:X2}")));
                            ImGui.PopStyleColor();
                        }
                }
                else
                {
                    try
                    {
                        using (UiTheme.FixedWidth())
                            foreach (var cell in VariableMemory.Cells(v, m.Memory.AddressBits))
                                ImGui.TextUnformatted(UiText.Get(UiText.Format($"${cell.Address:X4}: {m.Memory.Peek(cell.Address):X2}")));
                    }
                    catch (Exception error) when (error is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
                    { previewProblem = error.Message; }
                    if (previewProblem.Length > 0) ImGui.TextWrapped(UiText.Get(previewProblem));
                }
                Field("Congelar");
                ImGui.BeginDisabled(v.ReadOnly || !readable);
                bool frozen = _modifications?.Frozen.ContainsKey(name) == true;
                if (ImGui.Checkbox(UiText.Label(UiText.Get("##f") + name), ref frozen))
                {
                    ApplyModifications([new(name, v.Read(m.Memory), frozen)]);
                }
                ImGui.EndDisabled();
                Field("Interpretación");
                string description = readable ? v.Description + (v.ReadOnly ? " (solo lectura)" : "") : "Este dato tiene un formato o dirección inválidos en el perfil.";
                ImGui.TextWrapped(UiText.Get(!compact && description.Length > 140 ? description[..137] + "..." : description));
                if (!compact && description.Length > 140 && ImGui.IsItemHovered()) UiTheme.Tooltip(description);
        }
    }

    private void DrawFinder(IMachine m)
    {
        ImGui.TextWrapped(UiText.Get("Empezá la búsqueda, jugá un momento y filtrá según cómo cambió el valor. Repetí para reducir los resultados."));
        var (start, end) = RamRange(m);
        if (ImGui.Button(UiText.Label(_search == null ? "Empezar búsqueda" : "Empezar de nuevo")))
        {
            _search = new MemorySearch(m.Memory);
            _search.Start(start, end);
        }
        ImGui.SameLine();
        ImGui.TextDisabled(UiText.Get(UiText.Format($"RAM ${start:X4}-${end:X4}")));
        if (_search == null)
            return;
        string[] conditions = ["Cambió", "No cambió", "Aumentó", "Disminuyó", "Es igual a"];
        SearchCondition[] filters = [SearchCondition.Changed, SearchCondition.Unchanged, SearchCondition.Increased, SearchCondition.Decreased, SearchCondition.EqualTo];
        ImGui.SetNextItemWidth(UiTheme.Px(150));
        ImGui.Combo(UiText.Label("Condición"), ref _searchCondition, conditions.Select(UiText.Get).ToArray(), conditions.Length);
        if (_searchCondition == 4) { ImGui.SetNextItemWidth(UiTheme.Px(90)); ImGui.InputInt(UiText.Label("Valor"), ref _searchValue); }
        if (ImGui.Button(UiText.Label("Filtrar resultados"))) _search.Filter(filters[_searchCondition], _searchValue);
        ImGui.TextUnformatted(UiText.Get(UiText.Format($"Quedan {_search.Count} direcciones.")));
        if (_search.Count > 200)
        {
            ImGui.TextDisabled(UiText.Get("Todavía son muchas: seguí filtrando."));
            return;
        }
        ImGui.SetNextItemWidth(UiTheme.Px(160));
        ImGui.InputTextWithHint("##nuevavar", UiText.Get("Nombre de la variable"), ref _newVarName, 32);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##desc", UiText.Get("Qué representa"), ref _newVarDescription, 200);
        foreach (var (address, value) in _search.Results(40))
        {
            ImGui.TextUnformatted(UiText.Get(UiText.Format($"${address:X4} = {value,3}")));
            ImGui.SameLine();
            if (ImGui.SmallButton(UiText.Label(UiText.Get("Agregar al perfil##") + address)))
                AddVariable(m, address, value);
        }
    }

    private void AddVariable(IMachine m, uint address, byte value)
    {
        string name = Regex.Replace(_newVarName.Trim().ToLowerInvariant(), @"[^a-z0-9_]", "_");
        if (name.Length == 0)
        {
            _status = "Escribí un nombre para identificar la variable.";
            return;
        }
        if (_profile?.Variables.ContainsKey(name) == true)
        { _status = "Ese nombre ya existe. Elegí otro para agregar una variable."; return; }
        if (_profile == null)
        {
            _profile = new GameProfile
            {
                Id = _gameId,
                Name = _gameName,
                Machine = m.Id,
                Match = new ProfileMatch { Sha256 = _host.LoadedMediaHashes.ToList() },
                Description = "Perfil creado con el buscador de variables.",
                Boot = DefaultBoot(m),
                Verified = "Creado con el buscador de variables del emulador.",
            };
            _bootText = string.Join("\n", _profile.Boot);
        }
        _discoveredLabels.Remove(name);
        _profile.Variables[name] = new GameVariable
        {
            Address = address.ToString("X4"),
            Encoding = "uint8",
            Min = 0,
            Max = 255,
            Description = _newVarDescription.Trim().Length > 0 ? _newVarDescription.Trim() : UiText.Format($"Encontrada con el buscador (valía {value})."),
        };
        bool saved = SaveProfile();
        _modifications ??= new GameModifications(m, _profile);
        PersistModifications();
        if (saved) _status = UiText.Format($"\"{name}\" (${address:X4}) agregada al perfil de {UiText.Raw(_profile.Name)}. Sus controles se configuran en Código > Perfil y controles.");
        _newVarName = "";
        _newVarDescription = "";
    }

    // ------------------------------------------------------------------ export
    private string Export(IMachine m)
    {
        var sb = new StringBuilder();
        sb.AppendLine(UiText.Format($"; {_gameName} - {m.DisplayName} ({m.Cpu.CpuName})"));
        sb.AppendLine(UiText.Format($"; Desensamblado por RetroEmu con los nombres y comentarios guardados. {m.MemoryMapDescription}"));
        if (_profile != null)
            foreach (var (name, v) in _profile.Variables)
                sb.AppendLine(UiText.Format($"{name,-16} EQU ${v.Address}   ; {v.Description}"));
        foreach (var r in _map!.Routines.Values.OrderBy(r => r.Start))
        {
            if (_hideSystemRom && r.Start < SystemRomEnd(m))
                continue;
            string key = CodeAnnotations.Key(r.Start);
            sb.AppendLine();
            if (_notes.Summaries.TryGetValue(key, out var summary))
                sb.AppendLine("; " + summary);
            if (_notes.Pseudocode.TryGetValue(key, out var pseudo))
                foreach (var l in pseudo.Split('\n'))
                    sb.AppendLine(";   " + l.TrimEnd());
            uint? expected = null;
            foreach (var a in r.Instructions.OrderBy(x => x))
            {
                var ins = m.Disassembler.Disassemble(x => m.Memory.Peek(x), a);
                if (expected is uint e && e != a)
                    sb.AppendLine("        ; ...");
                expected = (a + (uint)ins.Length) & ((1u << m.Memory.AddressBits) - 1);
                if (a == r.Start || _notes.Label(a) != null)
                    sb.AppendLine(Name(m, a) + ":");
                string comment = _notes.Comment(a) is { } c ? "  ; " + c : "";
                sb.AppendLine(UiText.Format($"        {Pretty(m, ins.Text),-28}; {a:X4}  {string.Join(" ", ins.Bytes.Select(b => b.ToString("X2")))}{comment}"));
            }
        }
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RetroEmu", "codigo");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, _gameId + UiText.Get(".asm"));
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
