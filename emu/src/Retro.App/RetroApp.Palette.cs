using System.Numerics;
using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private bool _paletteEditorOpen, _paletteEditorFocus, _paletteReturnToConsole;
    private Vector3[] _paletteDraft = [];
    private int _paletteField;
    private TerminalColors EffectiveConsoleColors => TerminalPalettes.Resolve(_prefs.ConsolePalette, _prefs.ConsoleColors, _prefs.Interface);

    private void SetConsolePalette(string id)
    {
        id = CommandLine.Fold(id) switch { "verde" => "green", "ambar" => "amber", "hielo" => "ice", "papel" => "paper", "personalizada" => "custom", _ => CommandLine.Fold(id) };
        if (!TerminalPalettes.Known(id)) throw new ArgumentException(UiText.Get("No existe esa paleta. Usá paleta lista para ver las disponibles."));
        _prefs.ConsolePalette = id; _prefs.Save(); _themeChanged = true;
    }

    private void SetConsoleColors(TerminalColors colors)
    {
        if (!TerminalPalettes.Valid(colors)) throw new ArgumentException(UiText.Get("La paleta contiene un color inválido."));
        _prefs.ConsoleColors = TerminalPalettes.FromValues(colors.Values); SetConsolePalette("custom");
    }

    private void SetPaletteDraft(TerminalColors colors) => _paletteDraft = colors.Values.Select(value =>
    { var color = TerminalPalettes.Color(value); return new Vector3(color.X, color.Y, color.Z); }).ToArray();

    private void OpenPaletteEditor()
    {
        SetPaletteDraft(EffectiveConsoleColors); _paletteEditorOpen = _paletteEditorFocus = true;
        _paletteField = 0;
        _paletteReturnToConsole = _terminalOpen; ReleaseConsoleKeyboard();
    }

    private void ClosePaletteEditor()
    {
        _paletteEditorOpen = false;
        if (_paletteReturnToConsole) { OpenTerminal(); _consoleWaitForEnterRelease = true; }
        _paletteReturnToConsole = false;
    }

    private void DrawPaletteEditor()
    {
        if (!_paletteEditorOpen) return;
        var room = WorkspaceLayout.Available();
        ImGui.SetNextWindowSize(Vector2.Min(UiTheme.Px(700, 680), room), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(UiTheme.Px(360, 280), room), room);
        if (_paletteEditorFocus) { ImGui.SetNextWindowFocus(); _paletteEditorFocus = false; }
        bool compact = UiTheme.CompactHeight;
        if (compact) ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ImGui.GetStyle().WindowPadding.X, UiTheme.Px(8)));
        bool open = true;
        if (ImGui.Begin(UiText.Label("Colores de la terminal###terminal-palette"), ref open, ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            WorkspaceLayout.KeepOnScreen();
            string applyLabel = compact ? "OK" : "Aplicar colores", resetLabel = compact ? "Base" : "Restaurar colores";
            string[] actions = [applyLabel, "Cancelar", resetLabel];
            float width = ImGui.GetContentRegionAvail().X, used = 0; int rows = 1;
            foreach (string label in actions)
            {
                float needed = ImGui.CalcTextSize(UiText.Get(label)).X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
                if (used > 0 && used + needed > width) { rows++; used = 0; }
                used += needed;
            }
            ImGui.BeginChild("palette-fields", new(0, -(ImGui.GetFrameHeightWithSpacing() * rows + ImGui.GetStyle().ItemSpacing.Y + 1)));
            if (compact)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, UiTheme.Px(3)));
                ImGui.SetNextItemWidth(-1);
                bool choose = ImGui.BeginCombo("##palette-field", UiText.Get(TerminalPalettes.Fields[_paletteField].Label));
                InterfaceControlBounds?.Invoke("Elegir color", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                if (choose)
                {
                    for (int i = 0; i < TerminalPalettes.Fields.Count; i++)
                    {
                        if (ImGui.Selectable(UiText.Label(TerminalPalettes.Fields[i].Label), _paletteField == i)) _paletteField = i;
                        InterfaceControlBounds?.Invoke("Elegir color:" + TerminalPalettes.Fields[i].Id, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                    }
                    ImGui.EndCombo();
                }
            }
            else
            {
                UiTheme.Hint("Elegí cualquier color RGB. La vista previa cambia primero; Aplicar guarda la paleta personalizada.");
                var colors = TerminalPalettes.FromValues(_paletteDraft.Select(TerminalPalettes.Hex).ToArray());
                var start = ImGui.GetCursorScreenPos(); var preview = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeightWithSpacing() * 3 + UiTheme.Px(16));
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(start, start + preview, ImGui.ColorConvertFloat4ToU32(TerminalPalettes.Color(colors.Background)));
                draw.AddText(start + UiTheme.Px(8, 6), ImGui.ColorConvertFloat4ToU32(TerminalPalettes.Color(colors.Accent)), "retro:/> " + TerminalLanguage.Name("juegos"));
                draw.AddText(start + new Vector2(UiTheme.Px(8), UiTheme.Px(6) + ImGui.GetTextLineHeightWithSpacing()), ImGui.ColorConvertFloat4ToU32(TerminalPalettes.Color(colors.Text)), UiText.Get("Tu terminal, tus colores."));
                draw.AddText(start + new Vector2(UiTheme.Px(8), UiTheme.Px(6) + ImGui.GetTextLineHeightWithSpacing() * 2), ImGui.ColorConvertFloat4ToU32(TerminalPalettes.Color(colors.Muted)), UiText.Get("La partida se conserva."));
                ImGui.Dummy(preview);
            }
            foreach (int i in compact ? new[] { _paletteField } : Enumerable.Range(0, TerminalPalettes.Fields.Count))
            {
                if (!compact) ImGui.TextUnformatted(UiText.Get(TerminalPalettes.Fields[i].Label));
                ImGui.SetNextItemWidth(-1);
                ImGui.ColorEdit3("##palette-" + TerminalPalettes.Fields[i].Id, ref _paletteDraft[i], ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.NoLabel);
                InterfaceControlBounds?.Invoke("Color:" + TerminalPalettes.Fields[i].Id, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            }
            if (compact) ImGui.PopStyleVar();
            ImGui.EndChild(); ImGui.Separator();
            if (UiTheme.PrimaryButton(applyLabel)) { SetConsoleColors(TerminalPalettes.FromValues(_paletteDraft.Select(TerminalPalettes.Hex).ToArray())); ClosePaletteEditor(); }
            InterfaceControlBounds?.Invoke("Aplicar paleta", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (compact && ImGui.IsItemHovered()) UiTheme.Tooltip("Aplicar colores");
            UiTheme.SameLineIfFits("Cancelar"); if (ImGui.Button(UiText.Label("Cancelar"))) ClosePaletteEditor();
            InterfaceControlBounds?.Invoke("Cancelar paleta", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            UiTheme.SameLineIfFits(resetLabel);
            if (ImGui.Button(UiText.Label(resetLabel))) SetPaletteDraft(TerminalPalettes.Resolve("auto", null, _prefs.Interface));
            InterfaceControlBounds?.Invoke("Restaurar paleta", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (compact && ImGui.IsItemHovered()) UiTheme.Tooltip("Restaurar colores");
        }
        ImGui.End();
        if (compact) ImGui.PopStyleVar();
        if (!open) ClosePaletteEditor();
    }

    private IEnumerable<CommandSpec> PaletteOrders() =>
    [
        new()
        {
            Id = "system.palette", Name = "paleta", Group = "Consola", Summary = "Elige una paleta o personaliza cualquier color RGB de la terminal.",
            Aliases = ["palette"], Keywords = "colores fondo texto acento cursor tema personalizar",
            Arguments = [
                new("accion", ArgumentKind.Choice, "Sin indicar, muestra la paleta actual.") { Optional = true, Choices = ["ver", "lista", "usar", "color", "editar", "restablecer"] },
                new("valor", ArgumentKind.Word, "Nombre de paleta o de color.") { Optional = true, Complete = words => words.FirstOrDefault() is "color" ? TerminalPalettes.Fields.Select(field => new CompletionItem(field.Id, UiText.Get(field.Label))) : TerminalPalettes.All.Select(palette => new CompletionItem(palette.Id, palette.Name)) },
                new("rgb", ArgumentKind.Word, "Seis cifras hexadecimales, con # opcional.") { Optional = true }
            ],
            Examples = ["paleta lista", "paleta usar amber", "paleta color text #FFC966", "paleta editar", "paleta restablecer"],
            Result = "Los colores se guardan sin cambiar la partida ni el modo de interfaz. Alto contraste conserva la paleta elegida.",
            Run = context =>
            {
                string action = context.Optional("accion") ?? "ver";
                if (action is not ("usar" or "color") && (context.Has("valor") || context.Has("rgb")) || action == "usar" && context.Has("rgb"))
                    throw new CommandFailure(CommandProblem.InvalidArgument, "Sobran argumentos para esa acción de paleta.", "ayuda paleta");
                try
                {
                    switch (action)
                    {
                        case "lista": context.Table([new("Paleta"), new("Nombre")], TerminalPalettes.All.Select(palette => new[] { palette.Id, palette.Name })); return;
                        case "usar":
                            if (!context.Has("valor")) throw new ArgumentException(UiText.Get("Falta la paleta. Ejemplo: paleta usar amber."));
                            SetConsolePalette(context.Text("valor")); break;
                        case "color":
                            if (!context.Has("valor") || !context.Has("rgb")) throw new ArgumentException(UiText.Get("Faltan el color y su valor. Ejemplo: paleta color text #FFC966."));
                            SetConsoleColors(TerminalPalettes.Change(EffectiveConsoleColors, context.Text("valor"), context.Text("rgb"))); break;
                        case "editar": OpenPaletteEditor(); return;
                        case "restablecer": SetConsolePalette("auto"); break;
                    }
                }
                catch (ArgumentException error) { throw new CommandFailure(CommandProblem.InvalidArgument, error.Message, "paleta lista", "paleta color text #FFC966", "paleta restablecer"); }
                context.Heading(UiText.Get("Paleta: ") + TerminalPalettes.All.First(palette => palette.Id == _prefs.ConsolePalette).Name);
                var colors = EffectiveConsoleColors;
                context.Table([new("Color"), new("RGB")], TerminalPalettes.Fields.Select((field, index) => new[] { field.Id, colors.Values[index] }));
            }
        }
    ];
}
