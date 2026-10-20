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
    private static readonly Vector4 LabelColor = new(0.5f, 0.8f, 1f, 1);
    private static readonly Vector4 CommentColor = new(0.55f, 0.85f, 0.55f, 1);
    private static readonly Vector4 DimColor = new(0.55f, 0.55f, 0.6f, 1);
    private static readonly Vector4 ErrorColor = new(1, 0.5f, 0.4f, 1);
    private static readonly Regex AddressToken = new(@"\$([0-9A-F]{4})", RegexOptions.Compiled);

    private readonly EmulatorHost _host;
    private readonly AiSettings _ai;
    private readonly Action<uint> _showInDebugger;
    private bool _open;

    // game the analysis belongs to
    private IMachine? _machine;
    private string? _media;
    private string _gameId = "";
    private string _gameName = "";
    private GameProfile? _profile;
    private CodeMap? _map;
    private CodeAnnotations _notes = new();
    private string _status = "";

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

    public CodeWindow(EmulatorHost host, AiSettings ai, Action<uint> showInDebugger, Action? openStudio = null)
    {
        _host = host;
        _ai = ai;
        _showInDebugger = showInDebugger;
        _openStudio = openStudio;
        _host.BeforeFrame += ApplyFrozen;
    }

    public void Open()
    {
        _open = true;
        _modificationsMode = false;
        _finderOnOpen = _variablesOnOpen = false;
        if (_host.Machine is { } m)
            m.ExecutedMap ??= new bool[0x10000];
    }

    public void Update()
    {
        var m = _host.Machine;
        if (m == null) return;
        if (!ReferenceEquals(m, _machine) || _media != _host.LoadedMedia || _mediaRevision != _host.MediaRevision)
            SwitchGame(m);
        PollExplain(m);
        PollModificationAdvice();
    }

    public void Draw()
    {
        Update();
        var m = _host.Machine;
        if (m == null || !_open)
        {
            _modificationPanelLeft = null;
            return;
        }
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + (_modificationsMode ? Math.Max(10, vp.Size.X - 550) : 10), vp.Pos.Y + 30), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(Math.Min(_modificationsMode ? 540 : 940, vp.Size.X - 20), Math.Min(760, vp.Size.Y - 70)), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin(_modificationsMode ? "Modificar el juego" : "Código del juego", ref _open))
        {
            _modificationPanelLeft = null;
            ImGui.End();
            return;
        }
        _modificationPanelLeft = _modificationsMode ? ImGui.GetWindowPos().X - vp.Pos.X : null;
        if (_modificationsMode)
        {
            DrawModifications(m);
            ImGui.End();
            return;
        }
        if (_finderOnOpen || _variablesOnOpen)
        {
            bool finder = _finderOnOpen;
            ImGui.TextUnformatted(_gameName);
            if (ImGui.Button("Volver a modificaciones")) OpenModifications();
            ImGui.SameLine();
            if (ImGui.Button("Ver todas las herramientas de código")) Open();
            if (finder) DrawFinder(m); else DrawVariables(m);
            if (_status.Length > 0) ImGui.TextWrapped(_status);
            ImGui.End();
            return;
        }
        if (_map == null && m.ExecutedMap != null && m.FrameCount > 30)
            Analyze(m);
        ImGui.TextUnformatted($"{_gameName}  -  {m.DisplayName} ({m.Cpu.CpuName})");
        ImGui.SameLine();
        if (ImGui.Button(_map == null ? "Analizar el código" : "Actualizar análisis"))
            Analyze(m);
        if (_map != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Exportar listado .asm"))
                _status = "Guardado en " + Export(m);
        }
        if (_status.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, DimColor);
            ImGui.TextWrapped(_status);
            ImGui.PopStyleColor();
        }

        if (ImGui.BeginTabBar("codetabs"))
        {
            if (ImGui.BeginTabItem("Rutinas"))
            {
                DrawRoutines(m);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Variables y controles"))
            {
                DrawVariables(m);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Buscar variables"))
            {
                DrawFinder(m);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem($"Cambios al código ({_notes.Patches.Count})"))
            {
                DrawPatches(m);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    // ------------------------------------------------------------------ game identity
    private void SwitchGame(IMachine m)
    {
        _cts?.Cancel();
        _modificationCts?.Cancel();
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
        _map = null;
        _selected = null;
        _line = null;
        _back.Clear();
        _search = null;
        (_gameId, _gameName, _profile) = Identify(m);
        _modifications = _profile == null ? null : new GameModifications(m, _profile);
        _notes = CodeAnnotations.Load(_gameId);
        _bootText = string.Join("\n", _profile?.Boot ?? DefaultBoot(m));
        _status = "";
        if (_open)
            m.ExecutedMap ??= new bool[0x10000];
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
        return (m.Id, m.DisplayName + " (sin juego)", null);
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

    private static (uint start, uint end) RamRange(IMachine m) => m switch
    {
        Spectrum48 => (0x5B00, 0xFFFF),          // after the screen and the system variables
        { Id: "invaders" } => (0x2000, 0x23FF),  // work RAM (the rest is video)
        _ => (0, 0xFFFF),
    };

    // ------------------------------------------------------------------ analysis
    private void Analyze(IMachine m)
    {
        m.ExecutedMap ??= new bool[0x10000];
        _map = CodeMap.Build(a => m.Memory.Peek(a), m.Flow, m.CodeEntryPoints, m.ExecutedMap);
        int executed = m.ExecutedMap.Count(x => x);
        int game = _map.Routines.Keys.Count(a => a >= SystemRomEnd(m));
        _status = $"{_map.Routines.Count} rutinas ({game} del juego), {_map.Code.Count} instrucciones; " +
                  $"{executed} se ejecutaron desde que abriste esta ventana. Jugá un rato y actualizá para encontrar más.";
        if (_selected is uint s && !_map.Routines.ContainsKey(s))
            _selected = null;
        // start on the game's busiest routine
        _selected ??= _map.Routines.Values
            .Where(r => r.Executed && r.Start >= SystemRomEnd(m))
            .OrderByDescending(r => r.Callers.Count).ThenBy(r => r.Start)
            .FirstOrDefault()?.Start;
    }

    private string Name(IMachine m, uint a) =>
        _notes.Label(a) ?? m.SymbolFor(a) ?? (_map?.Routines.ContainsKey(a) == true ? $"sub_{a:X4}" : $"${a:X4}");

    private string? VariableAt(uint a)
    {
        if (_profile == null)
            return null;
        foreach (var (name, v) in _profile.Variables)
        {
            uint va = v.AddressValue;
            int len = v.Encoding is "bcd" or "ascii" ? v.Bytes : v.Encoding == "uint16" ? 2 : 1;
            if (a >= va && a < va + len)
                return a == va ? name : $"{name}+{a - va}";
        }
        return null;
    }

    /// <summary>Instruction text with addresses replaced by routine and variable names.</summary>
    private string Pretty(IMachine m, string text) =>
        AddressToken.Replace(text, match =>
        {
            uint a = uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber);
            return _notes.Label(a) ?? VariableAt(a) ?? (_map?.Routines.ContainsKey(a) == true ? $"sub_{a:X4}" : match.Value);
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
            ImGui.TextWrapped("El emulador anota cada instrucción que la CPU ejecuta. Jugá un rato (así pasa por más partes del juego) y tocá \"Analizar el código\": " +
                              "el programa se divide en rutinas, que podés renombrar, comentar, hacer explicar por una IA (con pseudocódigo) y modificar.");
            return;
        }
        float listWidth = 300;
        ImGui.BeginChild("routinelist", new Vector2(listWidth, 0), ImGuiChildFlags.Borders);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filtro", "buscar por nombre o dirección", ref _filter, 64);
        ImGui.Checkbox("Solo las que se ejecutaron", ref _onlyExecuted);
        if (SystemRomEnd(m) > 0)
            ImGui.Checkbox("Ocultar la ROM del sistema", ref _hideSystemRom);
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
            if (ImGui.Selectable($"{r.Start:X4}  {name}##{r.Start}", _selected == r.Start))
                Select(r.Start);
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered() && _notes.Summaries.TryGetValue(CodeAnnotations.Key(r.Start), out var sum))
                ImGui.SetTooltip(sum);
        }
        ImGui.EndChild();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("routinedetail", Vector2.Zero, ImGuiChildFlags.Borders);
        if (_selected is uint sel && _map.Routines.TryGetValue(sel, out var routine))
            DrawRoutine(m, routine);
        else
            ImGui.TextWrapped("Elegí una rutina de la lista. En blanco las que se ejecutaron, en gris las que todavía no, en verde las que ya tienen explicación.");
        ImGui.EndChild();
    }

    private void DrawRoutine(IMachine m, Routine r)
    {
        string key = CodeAnnotations.Key(r.Start);
        if (_back.Count > 0)
        {
            if (ImGui.Button("< Volver"))
                Select(_back.Pop(), remember: false);
            ImGui.SameLine();
        }
        ImGui.TextColored(LabelColor, $"{Name(m, r.Start)}  (${r.Start:X4}-${r.End:X4}, {r.Instructions.Count} instrucciones)");
        if (_rename.Length == 0)
            _rename = _notes.Label(r.Start) ?? "";
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputTextWithHint("##nombre", "nombre nuevo", ref _rename, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            SetLabel(r.Start, _rename);
        ImGui.SameLine();
        if (ImGui.Button("Renombrar"))
            SetLabel(r.Start, _rename);
        ImGui.SameLine();
        if (ImGui.Button("Ver en el depurador"))
            _showInDebugger(r.Start);
        ImGui.SameLine();
        bool bp = _host.Debug.IsBreakpoint(r.Start);
        ImGui.BeginDisabled(_host.Fusion != null);
        if (ImGui.Button(bp ? "Quitar breakpoint" : "Breakpoint al entrar"))
            _host.Debug.Toggle(r.Start);
        ImGui.EndDisabled();
        if (_host.Fusion != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Durante una fusión los juegos no se detienen en breakpoints.");

        if (_notes.Summaries.TryGetValue(key, out var summary))
            ImGui.TextWrapped(summary);
        DrawRoutineLinks(m, "Llama a:", r.Calls);
        DrawRoutineLinks(m, "La llaman:", r.Callers);
        DrawExplain(m, r);
        ImGui.Separator();

        if (ImGui.BeginTabBar("routinetabs"))
        {
            if (ImGui.BeginTabItem("Código"))
            {
                DrawListing(m, r);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Pseudocódigo"))
            {
                string code = _notes.Pseudocode.TryGetValue(key, out var p) ? p : "";
                ImGui.TextDisabled(code.Length == 0 ? "Todavía no hay pseudocódigo: pedíselo a la IA o escribilo vos." : "Podés corregirlo: se guarda solo.");
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
        ImGui.TextDisabled(title);
        foreach (var t in targets.OrderBy(x => x).Take(24))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"{Name(m, t)}##{title}{t}"))
                Select(t);
        }
        if (targets.Count > 24)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"y {targets.Count - 24} más");
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
        float editHeight = _line != null ? 92 : 0;
        ImGui.BeginChild("listing", new Vector2(0, -editHeight));
        uint? expected = null;
        uint pc = m.Cpu.ProgramCounter;
        foreach (var a in r.Instructions.OrderBy(x => x))
        {
            if (expected is uint e && e != a)
                ImGui.TextDisabled("        ...");
            var ins = m.Disassembler.Disassemble(x => m.Memory.Peek(x), a);
            expected = (a + (uint)ins.Length) & 0xFFFF;
            string? label = _notes.Label(a) ?? m.SymbolFor(a) ?? (a == r.Start ? $"sub_{a:X4}" : null);
            if (a == r.Start || label != null)
                ImGui.TextColored(LabelColor, (a == r.Start ? Name(m, a) : label) + ":");
            bool executed = m.ExecutedMap?[a] == true;
            string bytes = string.Join(" ", ins.Bytes.Select(b => b.ToString("X2")));
            string line = $"{(a == pc ? ">" : " ")} {a:X4}  {bytes,-12} {Pretty(m, ins.Text)}";
            ImGui.PushStyleColor(ImGuiCol.Text, executed ? new Vector4(1, 1, 1, 1) : DimColor);
            if (ImGui.Selectable(line + "##" + a, _line == a, ImGuiSelectableFlags.AllowDoubleClick))
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
                ImGui.TextColored(CommentColor, "; " + c);
            }
        }
        ImGui.EndChild();
        if (_line is uint sel)
            DrawLineEditor(m, r, sel);
    }

    private void DrawLineEditor(IMachine m, Routine r, uint a)
    {
        ImGui.Separator();
        ImGui.TextUnformatted($"Línea ${a:X4}  (doble clic en un CALL o salto para ir a esa rutina)");
        ImGui.SetNextItemWidth(-130);
        if (ImGui.InputTextWithHint("##comentario", "comentario", ref _commentText, 200, ImGuiInputTextFlags.EnterReturnsTrue))
            SetComment(a, _commentText);
        ImGui.SameLine();
        if (ImGui.Button("Guardar comentario"))
            SetComment(a, _commentText);
        ImGui.SetNextItemWidth(200);
        ImGui.InputText("bytes nuevos (hex)", ref _patchHex, 64);
        ImGui.SameLine();
        if (ImGui.Button("Cambiar"))
            Patch(m, a, _patchHex);
        ImGui.SameLine();
        if (ImGui.Button("Anular (NOP)"))
        {
            var ins = m.Disassembler.Disassemble(x => m.Memory.Peek(x), a);
            Patch(m, a, string.Join(" ", Enumerable.Repeat("00", ins.Length)));
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Reemplaza la instrucción por NOP (no hacer nada). Ej.: anular la resta de vidas = vidas infinitas.");
    }

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
        catch (FormatException)
        {
            _status = "Bytes inválidos: usá pares hexadecimales separados por espacios (ej. 3E 05).";
            return;
        }
        if (bytes.Length == 0)
            return;
        string key = CodeAnnotations.Key(a);
        string original = _notes.Patches.TryGetValue(key, out var old) ? old.Split('|')[0]
            : string.Join(" ", Enumerable.Range(0, bytes.Length).Select(i => m.Memory.Peek((uint)(a + i)).ToString("X2")));
        if (!Write(m, a, bytes))
        {
            _status = $"${a:X4} es ROM del sistema: no se puede cambiar.";
            return;
        }
        _notes.Patches[key] = original + "|" + string.Join(" ", bytes.Select(b => b.ToString("X2")));
        _notes.Save(_gameId);
        _status = $"Cambiado ${a:X4}. El juego ya corre con el cambio (el archivo original no se toca).";
        Analyze(m);
    }

    private static bool Write(IMachine m, uint a, byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            uint ad = (uint)((a + i) & 0xFFFF);
            if (m is ICodePatch p)
                p.PatchCode(ad, bytes[i]);
            else
                m.Memory.Poke(ad, bytes[i]);
            if (m.Memory.Peek(ad) != bytes[i])
                return false;
        }
        return true;
    }

    private static byte[] Hex(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => byte.Parse(h, NumberStyles.HexNumber)).ToArray();

    private void DrawPatches(IMachine m)
    {
        ImGui.TextWrapped("Los cambios se aplican a la copia del juego en memoria. Se guardan con el juego para volver a aplicarlos (por ejemplo después de reiniciar o volver a cargar la cinta).");
        if (_notes.Patches.Count == 0)
        {
            ImGui.TextDisabled("Todavía no hiciste cambios. En la pestaña Rutinas elegí una línea y cambiá sus bytes o anulala.");
            return;
        }
        if (ImGui.Button("Aplicar todos de nuevo"))
        {
            foreach (var (key, value) in _notes.Patches)
                Write(m, uint.Parse(key, NumberStyles.HexNumber), Hex(value.Split('|')[1]));
            _status = "Cambios aplicados.";
        }
        ImGui.Separator();
        foreach (var (key, value) in _notes.Patches.OrderBy(p => p.Key).ToList())
        {
            uint a = uint.Parse(key, NumberStyles.HexNumber);
            var parts = value.Split('|');
            bool applied = Hex(parts[1]).Select((b, i) => m.Memory.Peek((uint)(a + i)) == b).All(x => x);
            ImGui.TextUnformatted($"${key}  {parts[0]}  ->  {parts[1]}   {(applied ? "(aplicado)" : "(no aplicado)")}");
            if (_map?.RoutineAt(a) is { } r)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"{Name(m, r.Start)}##p{key}"))
                    Select(r.Start);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Deshacer##" + key))
            {
                Write(m, a, Hex(parts[0]));
                _notes.Patches.Remove(key);
                _notes.Save(_gameId);
            }
        }
    }

    // ------------------------------------------------------------------ AI
    private void DrawExplain(IMachine m, Routine r)
    {
        var provider = AiPicker.IsApi(_ai.Provider) ? _ai.Provider : null;
        bool busy = _explainTask != null;
        if (provider != null)
        {
            string who = AiPicker.ShortName(provider);
            if (ImGui.Button(busy ? "La IA está leyendo..." : $"Explicar con IA ({who})") && !busy)
            {
                var ai = _ai.CreateProvider()!;
                string prompt = CodeExplainer.Prompt(m, r, _notes, _profile, _map!);
                _explainFor = r.Start;
                _explainRevision = _mediaRevision;
                _explainMachine = m;
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _explainStatus = "Pidiendo la explicación...";
                _routineActivity.Start(ai);
                _explainTask = Task.Run(() => CodeExplainer.ExplainAsync(ai, prompt, token));
            }
            if (busy)
            {
                ImGui.SameLine();
                if (ImGui.Button("Cancelar"))
                    _cts?.Cancel();
            }
            ImGui.SameLine();
        }
        if (ImGui.Button("Copiar consigna para otra IA"))
        {
            ImGui.SetClipboardText(CodeExplainer.ManualPrompt(CodeExplainer.Prompt(m, r, _notes, _profile, _map!)));
            _explainStatus = "Consigna copiada: pegala en ChatGPT, Gemini, Claude.ai o tu agente y pegá abajo su respuesta.";
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Elegí la IA y su conexión en Modificar > Variables y cambios divertidos.");
        if (_explainStatus.Length > 0)
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1, 1), _explainStatus);
        _routineActivity.Draw(_explainTask != null, _explainStatus, _cts);
        if (_explainStatus.StartsWith("Consigna copiada"))
        {
            ImGui.InputTextMultiline("##respuestaia", ref _pasted, 20000, new Vector2(-1, 60));
            if (ImGui.Button("Usar la respuesta"))
            {
                try
                {
                    CodeExplainer.Apply(CodeExplainer.Parse(_pasted), r, _notes);
                    _notes.Save(_gameId);
                    _rename = "";
                    _pasted = "";
                    _explainStatus = "Listo: nombre, explicación, pseudocódigo y comentarios guardados.";
                }
                catch (AiException e)
                {
                    _explainStatus = "Consigna copiada, pero la respuesta no sirve: " + e.Message;
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
            _explainStatus = "Error: " + t.Exception!.GetBaseException().Message;
            return;
        }
        if (_map != null && _map.Routines.TryGetValue(_explainFor, out var r))
        {
            CodeExplainer.Apply(t.Result, r, _notes);
            _notes.Save(_gameId);
            _rename = "";
            _explainStatus = $"Listo: {t.Result.Name}. Nombre, explicación, pseudocódigo y comentarios guardados.";
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
            ImGui.TextWrapped("Este juego todavía no tiene perfil. En \"Buscar variables\" encontrá dónde guarda las vidas, el puntaje o la posición: " +
                              "con eso se crea el perfil y el juego ya se puede fusionar con otros.");
            return;
        }
        ImGui.TextWrapped(_profile.Description);
        ImGui.TextDisabled("Cambiá un valor y apretá Enter. \"Congelar\" lo mantiene fijo (vidas infinitas, aire infinito...).");
        DrawVariableValues(m, true);

        ImGui.Separator();
        ImGui.TextUnformatted("Controles para las fusiones (qué tecla del juego es cada acción):");
        var keys = m.Keys.Select(k => k.Name).ToArray();
        var options = new[] { "(ninguna)" }.Concat(keys).ToArray();
        bool changed = false;
        foreach (var input in Retro.Studio.StudioSession.InputNames)
        {
            int index = _profile.Controls.TryGetValue(input, out var k) ? Array.IndexOf(keys, k) + 1 : 0;
            ImGui.SetNextItemWidth(160);
            if (ImGui.Combo(input, ref index, options, options.Length))
            {
                if (index == 0) _profile.Controls.Remove(input);
                else _profile.Controls[input] = keys[index - 1];
                changed = true;
            }
        }
        ImGui.TextUnformatted("Arranque (una orden por línea: load-tape, wait N, press TECLA N):");
        if (ImGui.InputTextMultiline("##boot", ref _bootText, 2000, new Vector2(-1, 80)))
        {
            _profile.Boot = _bootText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            changed = true;
        }
        if (changed) ProfileLibrary.Save(_profile);
        ImGui.TextDisabled("Se guarda solo en tus perfiles. \"press ENTER 40\" = mantener ENTER 40 cuadros; \"wait 100\" = esperar 100 cuadros.");
    }

    private void DrawVariableValues(IMachine m, bool addresses)
    {
        if (_profile == null) return;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
        if (!addresses) flags |= ImGuiTableFlags.ScrollY;
        var size = addresses ? Vector2.Zero : new Vector2(0, Math.Clamp(ImGui.GetWindowHeight() * 0.3f, 120, 235));
        if (ImGui.BeginTable("vars", addresses ? 5 : 4, flags, size))
        {
            if (!addresses) ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Variable");
            if (addresses) ImGui.TableSetupColumn("Dirección");
            ImGui.TableSetupColumn("Valor");
            ImGui.TableSetupColumn("Congelar");
            ImGui.TableSetupColumn("Qué es", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            foreach (var (name, v) in _profile.Variables)
            {
                int value = 0; bool readable = true;
                try { value = v.Read(m.Memory); }
                catch (Exception error) when (error is FormatException or OverflowException or IndexOutOfRangeException or ArgumentException)
                { readable = false; }
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(addresses ? name : VariableLabel(name));
                if (addresses) { ImGui.TableNextColumn(); ImGui.TextUnformatted("$" + v.Address); }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(90);
                ImGui.BeginDisabled(v.ReadOnly || !readable);
                string input = _variableInputs.GetValueOrDefault(name, value.ToString(CultureInfo.InvariantCulture));
                // InputScalar/InputInt rejects EnterReturnsTrue in this ImGui version.
                // Keep an explicit text draft so a partial number never writes RAM.
                bool enter = ImGui.InputText("##v" + name, ref input, 16, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.EnterReturnsTrue);
                if (ImGui.IsItemActive()) _variableInputs[name] = input;
                else _variableInputs.Remove(name);
                if (enter)
                {
                    if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int requested))
                        ApplyModifications([new(name, requested, _modifications?.Frozen.ContainsKey(name) == true)]);
                    else _status = "Escribí un número entero válido.";
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip($"Rango: {v.Min} a {v.Max}");
                ImGui.EndDisabled();
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(v.ReadOnly || !readable);
                bool frozen = _modifications?.Frozen.ContainsKey(name) == true;
                if (ImGui.Checkbox("##f" + name, ref frozen))
                {
                    ApplyModifications([new(name, v.Read(m.Memory), frozen)]);
                }
                ImGui.EndDisabled();
                ImGui.TableNextColumn(); ImGui.TextWrapped(readable ? v.Description + (v.ReadOnly ? " (solo lectura)" : "") : "Este dato tiene un formato o dirección inválidos en el perfil.");
            }
            ImGui.EndTable();
        }

    }

    private void DrawFinder(IMachine m)
    {
        ImGui.TextWrapped("Como los buscadores de trucos clásicos: tocá Empezar, hacé algo en el juego (perdé una vida, juntá puntos, movete) " +
                          "y decí cómo cambió el valor. Repetí hasta que queden pocas direcciones.");
        var (start, end) = RamRange(m);
        if (ImGui.Button(_search == null ? "Empezar búsqueda" : "Empezar de nuevo"))
        {
            _search = new MemorySearch(m.Memory);
            _search.Start(start, end);
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"RAM ${start:X4}-${end:X4}");
        if (_search == null)
            return;
        if (ImGui.Button("Aumentó")) _search.Filter(SearchCondition.Increased);
        ImGui.SameLine();
        if (ImGui.Button("Disminuyó")) _search.Filter(SearchCondition.Decreased);
        ImGui.SameLine();
        if (ImGui.Button("Cambió")) _search.Filter(SearchCondition.Changed);
        ImGui.SameLine();
        if (ImGui.Button("No cambió")) _search.Filter(SearchCondition.Unchanged);
        ImGui.SameLine();
        if (ImGui.Button("Es igual a")) _search.Filter(SearchCondition.EqualTo, _searchValue);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("##valor", ref _searchValue);
        ImGui.TextUnformatted($"Quedan {_search.Count} direcciones.");
        if (_search.Count > 200)
        {
            ImGui.TextDisabled("Todavía son muchas: seguí filtrando.");
            return;
        }
        ImGui.SetNextItemWidth(160);
        ImGui.InputTextWithHint("##nuevavar", "nombre (ej. lives)", ref _newVarName, 32);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##desc", "qué es (ej. vidas de reserva, baja al morir)", ref _newVarDescription, 200);
        foreach (var (address, value) in _search.Results(40))
        {
            ImGui.TextUnformatted($"${address:X4} = {value,3}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Agregar al perfil##" + address))
                AddVariable(m, address, value);
        }
    }

    private void AddVariable(IMachine m, uint address, byte value)
    {
        string name = Regex.Replace(_newVarName.Trim().ToLowerInvariant(), @"[^a-z0-9_]", "_");
        if (name.Length == 0)
        {
            _status = "Escribí un nombre para la variable (en inglés y sin espacios, ej. lives, score, energy).";
            return;
        }
        if (_profile == null)
        {
            _profile = new GameProfile
            {
                Id = _gameId,
                Name = _gameName,
                Machine = m.Id,
                Match = new ProfileMatch { NamePattern = Regex.Escape(Path.GetFileNameWithoutExtension(_host.LoadedMedia ?? _gameName)) },
                Description = "Perfil creado con el buscador de variables.",
                Boot = DefaultBoot(m),
                Verified = "Creado con el buscador de variables del emulador.",
            };
            _bootText = string.Join("\n", _profile.Boot);
        }
        _profile.Variables[name] = new GameVariable
        {
            Address = address.ToString("X4"),
            Encoding = "uint8",
            Min = 0,
            Max = 255,
            Description = _newVarDescription.Trim().Length > 0 ? _newVarDescription.Trim() : $"Encontrada con el buscador (valía {value}).",
        };
        ProfileLibrary.Save(_profile);
        _modifications ??= new GameModifications(m, _profile);
        _status = $"\"{name}\" (${address:X4}) agregada al perfil de {_profile.Name}. Configurá sus controles en \"Variables y controles\" y ya se puede fusionar.";
        _newVarName = "";
        _newVarDescription = "";
    }

    // ------------------------------------------------------------------ export
    private string Export(IMachine m)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"; {_gameName} - {m.DisplayName} ({m.Cpu.CpuName})");
        sb.AppendLine($"; Desensamblado por RetroEmu con los nombres y comentarios guardados. {m.MemoryMapDescription}");
        if (_profile != null)
            foreach (var (name, v) in _profile.Variables)
                sb.AppendLine($"{name,-16} EQU ${v.Address}   ; {v.Description}");
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
                expected = (a + (uint)ins.Length) & 0xFFFF;
                if (a == r.Start || _notes.Label(a) != null)
                    sb.AppendLine(Name(m, a) + ":");
                string comment = _notes.Comment(a) is { } c ? "  ; " + c : "";
                sb.AppendLine($"        {Pretty(m, ins.Text),-28}; {a:X4}  {string.Join(" ", ins.Bytes.Select(b => b.ToString("X2")))}{comment}");
            }
        }
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RetroEmu", "codigo");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, _gameId + ".asm");
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
