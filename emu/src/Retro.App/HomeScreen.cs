using System.Numerics;
using ImGuiNET;
using Retro.Ai;
using Retro.Fusion;
using Retro.Machines;
using Retro.Studio;

namespace Retro.App;

/// <summary>The main browser replaces the play area; it is never a floating window.</summary>
public sealed class HomeScreen(GameLibrary library, Action<string> playOriginal, Action<string> playFusion,
    Action browse, Action fuse, Action create, Action<bool> visibility, Func<bool> canReturn)
{
    private bool _open;
    private string _section = "home", _machine = "", _search = "", _problem = "";
    private List<StudioGame> _fusions = new();
    private IReadOnlyList<CreatedGame> _created = Array.Empty<CreatedGame>();
    private static readonly MachineInfo[] Machines = MachineCatalog.All.Where(m => m.Status == MachineStatus.Available)
        .OrderBy(m => m.Id switch { "zx48" => 0, "c64" => 1, "nes" => 2, _ => 3 }).ToArray();
    public bool IsOpen => _open;
    public int GameCount => library.Originals().Count() + _created.Count + _fusions.Count;

    public void Open()
    {
        if (!_open) { _open = true; visibility(true); }
        Refresh();
    }

    public void Close()
    {
        if (_open) { _open = false; visibility(false); }
    }

    public void ShowMachine(string id)
    {
        if (!Machines.Any(m => m.Id == id)) throw new ArgumentException("Máquina no disponible.", nameof(id));
        Select("machine", id);
        Open();
    }

    private void Select(string section, string machine = "")
    {
        _section = section;
        _machine = machine;
        _search = "";
    }

    private void Refresh()
    {
        try
        {
            _fusions = StudioLibrary.AllVersions().GroupBy(g => g.Id)
                .Select(g => g.OrderByDescending(v => v.Version).First()).OrderBy(g => g.Title).ToList();
            _created = CreatedGames.All();
            _problem = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _problem = "No pude actualizar la biblioteca: " + e.Message;
        }
    }

    public void Draw()
    {
        if (!_open) return;
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, Math.Max(1, viewport.WorkSize.Y - 24)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18, 18));
        ImGui.Begin("##inicio", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.BeginChild("navegacion", new Vector2(Math.Min(210, viewport.WorkSize.X * 0.29f), 0));
        ImGui.SetWindowFontScale(1.4f);
        ImGui.TextUnformatted("RetroEmu");
        ImGui.SetWindowFontScale(1);
        ImGui.Spacing(); ImGui.Spacing();
        Nav("Inicio", "home");
        Nav("Todos los juegos", "all");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextDisabled("Máquinas");
        foreach (var machine in Machines) Nav(Name(machine), "machine", machine.Id);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        Nav("Fusionados", "fusions");
        Nav("Creados", "created");
        ImGui.Spacing(); ImGui.Spacing();
        if (canReturn() && ImGui.Button("Volver a la partida", new Vector2(-1, 0))) Close();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("contenido", new Vector2(0, 0));
        string title = _section switch
        {
            "machine" => Name(Machines.Single(m => m.Id == _machine)), "all" => "Todos los juegos",
            "fusions" => "Fusionados", "created" => "Creados", _ => "Elegí una máquina",
        };
        ImGui.SetWindowFontScale(1.4f);
        ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1);
        ImGui.Spacing();
        if (ImGui.Button("Abrir archivo...")) browse();
        ImGui.SameLine();
        ImGui.BeginDisabled(library.Scanning);
        if (ImGui.Button(library.Scanning ? "Buscando..." : "Actualizar"))
        {
            library.Rescan(library.Folder);
            Refresh();
        }
        ImGui.EndDisabled();
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        if (_problem.Length > 0) ImGui.TextWrapped(_problem);
        if (library.OrganizationError != null) ImGui.TextWrapped(library.OrganizationError);
        else if (library.OrganizedCount > 0 || library.UnclassifiedCount > 0)
        {
            ImGui.TextDisabled($"Ordenados por sistema: {library.OrganizedCount}" +
                (library.UnclassifiedCount > 0 ? $" · Sin clasificar: {library.UnclassifiedCount}" : ""));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Los archivos cuyo sistema no se puede identificar quedan en ROMs.");
        }
        if (_section == "home") DrawMachines();
        else
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##buscar", "Buscar por nombre", ref _search, 150);
            ImGui.Spacing();
            if (_section == "fusions") DrawFusions();
            else if (_section == "created") DrawCreated();
            else DrawOriginals();
        }
        ImGui.EndChild();
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void Nav(string label, string section, string machine = "")
    {
        if (ImGui.Selectable(label, _section == section && _machine == machine, ImGuiSelectableFlags.None, new Vector2(0, 28)))
            Select(section, machine);
    }

    private static string Name(MachineInfo machine) => machine.Id == "invaders" ? "Space Invaders" : machine.Name;

    private void DrawMachines()
    {
        int columns = ImGui.GetContentRegionAvail().X >= 540 ? 2 : 1;
        if (ImGui.BeginTable("maquinas", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var machine in Machines)
            {
                ImGui.TableNextColumn();
                var size = new Vector2(ImGui.GetContentRegionAvail().X, 116);
                bool selected = ImGui.InvisibleButton("maquina-" + machine.Id, size);
                bool hovered = ImGui.IsItemHovered();
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var draw = ImGui.GetWindowDrawList();
                uint accent = machine.Id switch
                {
                    "zx48" => ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.75f, 1, 1)),
                    "c64" => ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.65f, 1, 1)),
                    "nes" => ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0.5f, 0.45f, 1)),
                    _ => ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.85f, 0.55f, 1)),
                };
                draw.AddRectFilled(min, max, ImGui.GetColorU32(hovered ? ImGuiCol.HeaderHovered : ImGuiCol.FrameBg), 5);
                draw.AddRectFilled(min, new Vector2(min.X + 4, max.Y), accent, 3);
                draw.AddText(min + new Vector2(18, 18), ImGui.GetColorU32(ImGuiCol.Text), Name(machine));
                int count = library.Originals(machineId: machine.Id).Count() + _created.Count(g => g.Machine == machine.Id);
                draw.AddText(min + new Vector2(18, 44), ImGui.GetColorU32(ImGuiCol.TextDisabled),
                    library.Scanning ? "Buscando juegos..." : count == 0 ? "Sin juegos agregados" : $"{count} juego{(count == 1 ? "" : "s")}");
                draw.AddText(min + new Vector2(18, 82), accent, "Ver juegos >");
                if (selected) Select("machine", machine.Id);
                ImGui.Spacing();
            }
            ImGui.EndTable();
        }
        ImGui.Spacing();
        if (ImGui.Button("Ver todos los juegos")) Select("all");
    }

    private void DrawOriginals()
    {
        var games = library.Originals(_search, _section == "machine" ? _machine : null).ToList();
        var created = _created.Where(g => Matches(g.Title) && (_section == "all" || g.Machine == _machine)).ToList();
        var fusions = _section == "all" ? _fusions.Where(g => Matches(g.Title)).ToList() : new List<StudioGame>();
        if (BeginList())
        {
            foreach (var game in games)
                Row(game.Path, game.Name, Name(game.Machine), game.NotYet,
                    () => playOriginal(game.Path), game.NotYet == null && game.Confidence >= 50);
            foreach (var game in created)
                Row(game.Folder, game.Title, game.MachineName, game.Pitch,
                    () => playOriginal(game.ProgramPath), File.Exists(game.ProgramPath));
            foreach (var game in fusions) FusionRow(game);
            ImGui.EndTable();
        }
        if (games.Count + created.Count + fusions.Count == 0) ImGui.TextWrapped(library.Scanning ? "Buscando juegos..."
            : _search.Length > 0 ? "No hay resultados para esta búsqueda." : "No hay juegos agregados. Usá Abrir archivo o arrastrá uno a la ventana.");
    }

    private void DrawFusions()
    {
        if (ImGui.Button("Fusionar juegos")) { Close(); fuse(); }
        ImGui.Spacing();
        var games = _fusions.Where(g => Matches(g.Title)).ToList();
        if (BeginList())
        {
            foreach (var game in games) FusionRow(game);
            ImGui.EndTable();
        }
        if (games.Count == 0) ImGui.TextWrapped("No hay fusiones para mostrar.");
    }

    private void FusionRow(StudioGame game)
    {
        var have = library.Entries.Where(e => e.ProfileId != null).Select(e => e.ProfileId!).ToHashSet();
        string missing = string.Join(", ", game.Games.Where(g => !have.Contains(g.Profile))
            .Select(g => ProfileLibrary.Find(g.Profile)?.Name ?? g.Profile));
        Row(game.Id, game.Title, $"Fusión · v{game.Version}", missing.Length > 0 ? "Falta: " + missing : game.Pitch,
            () => playFusion(game.Id), missing.Length == 0);
    }

    private void DrawCreated()
    {
        if (ImGui.Button("Crear un juego")) { Close(); create(); }
        ImGui.Spacing();
        var games = _created.Where(g => Matches(g.Title)).ToList();
        if (BeginList())
        {
            foreach (var game in games)
                Row(game.Folder, game.Title, game.MachineName, game.Pitch,
                    () => playOriginal(game.ProgramPath), File.Exists(game.ProgramPath));
            ImGui.EndTable();
        }
        if (games.Count == 0) ImGui.TextWrapped("No hay juegos creados para mostrar.");
    }

    private bool Matches(string title) => title.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool BeginList()
    {
        if (!ImGui.BeginTable("juegos", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
            new Vector2(0, Math.Max(50, ImGui.GetContentRegionAvail().Y - 30)))) return false;
        ImGui.TableSetupColumn("Juego", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Sistema / versión", ImGuiTableColumnFlags.WidthFixed, Math.Clamp(ImGui.GetContentRegionAvail().X * 0.27f, 120, 180));
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 75);
        ImGui.TableHeadersRow();
        return true;
    }

    private void Row(string id, string title, string detail, string? hint, Action play, bool playable)
    {
        ImGui.PushID(id);
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(title);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(string.IsNullOrWhiteSpace(hint) ? title : hint);
        ImGui.TableNextColumn(); ImGui.TextUnformatted(detail);
        ImGui.TableNextColumn();
        ImGui.BeginDisabled(!playable || library.Scanning);
        if (ImGui.Button("Jugar", new Vector2(-1, 0))) { Close(); play(); }
        ImGui.EndDisabled();
        if (!playable && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(hint ?? "Este formato todavía no se puede abrir.");
        ImGui.PopID();
    }
}
