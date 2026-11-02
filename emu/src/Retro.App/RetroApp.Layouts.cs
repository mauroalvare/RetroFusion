using System.Numerics;
using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private static readonly string[] WorkspaceNames = ["Biblioteca", "Jugar", "Crear", "Analizar", "Herramientas", "Ayuda"];
    private static readonly string[] WorkspaceIds = ["library", "play", "create", "analysis", "tools", "help"];
    private int CurrentWorkspace => _home.IsOpen ? 0 : _hub == HubPage.Create || _creator.IsOpen || _studio.IsOpen ? 2
        : _code.IsOpen || _debugMode ? 3 : _hub == HubPage.Tools || _stateWindowOpen ? 4 : _hub == HubPage.Help ? 5 : 1;
    private const ImGuiWindowFlags ShellFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollWithMouse;

    private void PrepareGraphicalLayout()
    {
        if (_prefs.Interface == InterfaceStyle.Terminal)
        {
            bool rail = !UiTheme.CompactHeight && ImGui.GetMainViewport().Size.X >= UiTheme.Px(900);
            WorkspaceLayout.SidebarWidth = rail ? UiTheme.Px(180) : 0;
            WorkspaceLayout.NavigationHeight = rail ? 0 : ImGui.GetFrameHeight() + UiTheme.Px(UiTheme.CompactHeight ? 6 : 16);
        }
        else if (_prefs.Interface == InterfaceStyle.Amber)
        {
            _menuHeight = WorkspaceLayout.MenuHeight = 0;
            WorkspaceLayout.NavigationHeight = WorkspaceLayout.DockHeight = ImGui.GetFrameHeight() + UiTheme.Px(UiTheme.CompactHeight ? 6 : 16);
        }
    }

    private void NavigateWorkspace(int page)
    {
        switch (page)
        {
            case 0: CloseEditors(); _home.Open(); break;
            case 1: OpenPlay(); break;
            case 2: OpenHub(HubPage.Create); break;
            case 3: OpenAnalyzer(); break;
            case 4: OpenHub(HubPage.Tools); break;
            case 5: OpenHub(HubPage.Help); break;
        }
    }

    private void TrackWorkspace(int page)
    {
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        // Selectables extend into horizontal padding. Report their clickable part in the clip area.
        var draw = ImGui.GetWindowDrawList(); min = Vector2.Max(min, draw.GetClipRectMin()); max = Vector2.Min(max, draw.GetClipRectMax());
        InterfaceControlBounds?.Invoke("Workspace:" + WorkspaceIds[page], min, max);
    }
    private void WorkspaceChoice()
    {
        bool open = ImGui.BeginCombo("##workspace-choice", UiText.Get(WorkspaceNames[CurrentWorkspace]));
        InterfaceControlBounds?.Invoke("Cambiar sección", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (open)
        {
            for (int i = 0; i < WorkspaceNames.Length; i++)
            {
                if (ImGui.Selectable(UiText.Label(WorkspaceNames[i]), CurrentWorkspace == i)) NavigateWorkspace(i);
                TrackWorkspace(i);
            }
            ImGui.EndCombo();
        }
    }

    private void DrawExplorerNavigation()
    {
        WorkspaceLayout.MenuHeight = _menuHeight;
        var viewport = ImGui.GetMainViewport();
        bool rail = WorkspaceLayout.SidebarWidth > 0;
        ImGui.SetNextWindowPos(viewport.Pos + new Vector2(0, _menuHeight));
        ImGui.SetNextWindowSize(rail ? new(WorkspaceLayout.SidebarWidth, viewport.Size.Y - _menuHeight - UiTheme.StatusBarHeight)
            : new(viewport.Size.X, WorkspaceLayout.NavigationHeight));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, UiTheme.Px(8, UiTheme.CompactHeight ? 3 : 8));
        ImGui.Begin("##explorer-navigation", ShellFlags);
        if (rail)
        {
            ImGui.TextUnformatted(UiText.Get("EXPLORADOR")); ImGui.Separator();
            for (int i = 0; i < WorkspaceNames.Length; i++)
            {
                if (ImGui.Selectable(UiText.Label(WorkspaceNames[i]), CurrentWorkspace == i, ImGuiSelectableFlags.None, new(0, ImGui.GetFrameHeight()))) NavigateWorkspace(i);
                TrackWorkspace(i);
            }
            ImGui.Separator();
            if (ImGui.Button(UiText.Label("Buscar…"), new(-1, 0))) _commands.Open();
            InterfaceControlBounds?.Invoke("Buscar acciones", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (ImGui.Button(UiText.Label("Interfaz…"), new(-1, 0))) OpenInterfacePicker();
            if (ImGui.Button(UiText.Label("Colores…"), new(-1, 0))) OpenPaletteEditor();
            InterfaceControlBounds?.Invoke("Personalizar paleta", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }
        else
        {
            float search = ImGui.CalcTextSize(UiText.Get("Buscar…")).X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SetNextItemWidth(Math.Max(100, ImGui.GetContentRegionAvail().X - search)); WorkspaceChoice(); ImGui.SameLine();
            if (ImGui.Button(UiText.Label("Buscar…"))) _commands.Open();
            InterfaceControlBounds?.Invoke("Buscar acciones", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }
        ImGui.End(); ImGui.PopStyleVar();
    }

    private void DrawLoungeNavigation()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos); ImGui.SetNextWindowSize(new(viewport.Size.X, WorkspaceLayout.NavigationHeight));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, UiTheme.Px(10, UiTheme.CompactHeight ? 3 : 8));
        ImGui.Begin("##lounge-header", ShellFlags);
        ImGui.TextUnformatted(UiText.Get("SALÓN"));
        float actions = ImGui.CalcTextSize(UiText.Get("Buscar…")).X + ImGui.CalcTextSize(UiText.Get("Opciones")).X
            + ImGui.GetStyle().FramePadding.X * 4 + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - actions - ImGui.GetStyle().WindowPadding.X));
        if (ImGui.Button(UiText.Label("Buscar…"))) _commands.Open();
        InterfaceControlBounds?.Invoke("Buscar acciones", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(); if (ImGui.Button(UiText.Label("Opciones"))) ImGui.OpenPopup("lounge-options");
        InterfaceControlBounds?.Invoke("Menú de salón", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (ImGui.BeginPopup("lounge-options"))
        {
            if (ImGui.MenuItem(UiText.Label("Estilos de interfaz"))) OpenInterfacePicker();
            if (ImGui.MenuItem(UiText.Label("Colores…"))) OpenPaletteEditor();
            InterfaceControlBounds?.Invoke("Personalizar paleta", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (ImGui.MenuItem(UiText.Label("Preferencias"))) _preferencesOpen = true;
            if (ImGui.MenuItem(UiText.Label("Sonido"))) _soundWindowOpen = true;
            if (ImGui.MenuItem(UiText.Label("Actualizaciones"))) _updates.Open();
            ImGui.EndPopup();
        }
        ImGui.End();
        ImGui.SetNextWindowPos(viewport.Pos + new Vector2(0, viewport.Size.Y - UiTheme.StatusBarHeight - WorkspaceLayout.DockHeight));
        ImGui.SetNextWindowSize(new(viewport.Size.X, WorkspaceLayout.DockHeight));
        ImGui.Begin("##lounge-dock", ShellFlags);
        float required = WorkspaceNames.Sum(name => ImGui.CalcTextSize(UiText.Get(name)).X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X);
        if (required > ImGui.GetContentRegionAvail().X)
        {
            float play = HasGame ? ImGui.CalcTextSize(UiText.Get("Jugar")).X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X : 0;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - play); WorkspaceChoice();
            if (HasGame) { ImGui.SameLine(); if (UiTheme.PrimaryButton("Jugar")) OpenPlay(); TrackWorkspace(1); }
        }
        else
        {
            float width = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 5) / 6;
            for (int i = 0; i < WorkspaceNames.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                bool active = CurrentWorkspace == i;
                if (active)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.Accent);
                    ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.HighContrast ? new Vector4(0, 0, 0, 1) : UiTheme.PaletteOf(_prefs.Interface).OnAccent);
                }
                bool chosen = ImGui.Button(UiText.Label(WorkspaceNames[i]), new(width, 0)); TrackWorkspace(i);
                if (active) ImGui.PopStyleColor(2);
                if (chosen) NavigateWorkspace(i);
            }
        }
        ImGui.End(); ImGui.PopStyleVar();
    }

    private void DrawLayoutTools(string id, (string Title, string Detail, string Command)[] items)
    {
        bool tiles = _prefs.Interface == InterfaceStyle.Amber;
        int columns = ImGui.GetContentRegionAvail().X >= UiTheme.Px(650) ? 2 : 1;
        if (tiles && !ImGui.BeginTable(id, columns, ImGuiTableFlags.SizingStretchSame)) return;
        foreach (var item in items)
        {
            var command = Commands.Single(entry => entry.Id == item.Command);
            ImGui.PushID(item.Command);
            if (tiles)
            {
                ImGui.TableNextColumn(); UiTheme.BeginCard("tool-tile");
                if (!UiTheme.CompactHeight) { ImGui.TextWrapped(UiText.Get(item.Title)); UiTheme.Hint(item.Detail); }
                ImGui.BeginDisabled(!command.Enabled);
                if (UiTheme.PrimaryButton(item.Title, new(-1, 0))) command.Execute();
            }
            else
            {
                ImGui.BeginDisabled(!command.Enabled);
                if (ImGui.Selectable(UiText.Label(item.Title), false, ImGuiSelectableFlags.None, new(0, ImGui.GetFrameHeight()))) command.Execute();
            }
            InterfaceControlBounds?.Invoke("Herramienta:" + item.Command, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiTheme.Tooltip(command.Enabled ? UiText.Get(item.Detail) : command.Unavailable);
            if (tiles) UiTheme.EndCard();
            ImGui.PopID();
        }
        if (tiles) ImGui.EndTable();
    }
}
