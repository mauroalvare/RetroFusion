using System.Numerics;
using ImGuiNET;
using Retro.Ai;
using Retro.Core;
using Retro.Fusion;
using Retro.Machines;
using Retro.Studio;

namespace Retro.App;

public sealed record HomeActions(Action Create, Action Fuse, Action FindAction, Action? MetadataSettings = null, Func<bool>? OnlineLookups = null, Action? ChooseFolder = null);

/// <summary>The main browser replaces the play area; it is never a floating window.</summary>
public sealed partial class HomeScreen(GameLibrary library, Action<string> playOriginal, Action<string> playFusion,
    Action browse, Action<bool> visibility, Func<bool> canReturn, LibraryActivity? activity = null, HomeActions? actions = null,
    LibraryMetadata? metadata = null, TimeProvider? clock = null, GameMetadataService? identification = null) : IDisposable
{
    private readonly LibraryActivity _activity = activity ?? new();
    private readonly LibraryMetadata _metadata = metadata ?? new();
    private readonly GameMetadataService _identification = identification ?? new(new GameIdentificationCache(persist: false));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ThumbnailCache _thumbnails = new();
    private int _thumbnailRevision = -1;
    private readonly Dictionary<string, GameProfile> _profiles = new(StringComparer.Ordinal);
    private HashSet<string> _availableProfiles = new(StringComparer.Ordinal);
    private bool _open, _compactRows, _refreshed, _focusSearch;
    private int _contentRevision;
    // Drawn every frame: summaries, the filtered list and created-program checks wait for a change.
    private readonly FrameCache<(int, int, bool), Dictionary<string, string>> _summaries = new();
    private readonly FrameCache<int, HashSet<string>> _readyCreated = new();
    private string _section = "home", _machine = "", _search = "", _problem = "";
    private List<StudioGame> _fusions = new();
    private List<StudioGame> _inspired = new();
    private IReadOnlyList<CreatedGame> _created = Array.Empty<CreatedGame>();
    private static readonly MachineInfo[] Machines = MachineCatalog.All.Where(m => m.Status == MachineStatus.Available)
        .OrderBy(m => m.Id switch { "zx48" => 0, "c64" => 1, "nes" => 2, _ => 3 }).ToArray();
    public bool IsOpen => _open;
    public int GameCount => library.Entries.Count(entry => entry.Machine.Status == MachineStatus.Available) + _created.Count + _fusions.Count + _inspired.Count;

    public void Open()
    {
        if (!_open) { _open = true; visibility(true); RevealSelection(); }
        Refresh();
    }

    public void Close()
    {
        _editing = null;
        if (_open) { _open = false; visibility(false); }
    }

    public void ShowMachine(string id)
    {
        if (!Machines.Any(m => m.Id == id)) throw new ArgumentException("Máquina no disponible.", nameof(id));
        Select("machine", id);
        Open();
    }

    /// <summary>Back from a game the list shows the game that was being played.</summary>
    public void RevealSelection() => _scrollToSelection = _selectedKey != null;

    public void ShowOriginals() { Select("originals"); Open(); }
    public void ShowFusions() { Select("fusions"); Open(); }
    public void ShowFavorites() { Select("favorites"); Open(); }
    public void ShowRecents() { Select("recent"); Open(); }

    private void Select(string section, string machine = "")
    {
        _section = section;
        _machine = machine;
        _search = "";
        _filters = new() { Order = section == "recent" ? LibraryOrder.LastPlayed : LibraryOrder.Name };
        _filtersExpanded = false;
    }

    /// <summary>The most recently played games that can be started now, newest first (Juego > Recientes).</summary>
    public IReadOnlyList<(string Title, string Detail, Action Play)> Recents(int count)
    {
        if (!_refreshed) Refresh();
        var items = new List<(string Key, string Title, string Detail, Action Play)>();
        foreach (var game in library.Originals())
            if (game.NotYet == null && game.Confidence >= 50)
                items.Add((OriginalKey(game), game.Name, Name(game.Machine), () => playOriginal(game.Path)));
        foreach (var game in _created)
            if (Ready(game))
                items.Add((CreatedKey(game), game.Title, game.MachineName, () => playOriginal(game.ProgramPath)));
        foreach (var game in _fusions.Concat(_inspired))
            if (game.Games.All(g => _availableProfiles.Contains(g.Profile)))
                items.Add((FusionKey(game), game.Title, game.IsInspired ? "Con originales" : "Fusión", () => playFusion(game.Id)));
        return items.Where(item => _activity.LastPlayed(item.Key) != null).DistinctBy(item => item.Key)
            .OrderByDescending(item => _activity.LastPlayed(item.Key)).ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(count).Select(item => (item.Title, item.Detail, item.Play)).ToList();
    }

    private void Refresh()
    {
        _refreshed = true; _contentRevision++;
        _thumbnails.Clear();
        try
        {
            var games = StudioLibrary.AllVersions().GroupBy(g => g.Id)
                .Select(g => g.OrderByDescending(v => v.Version).First()).OrderBy(g => g.Title).ToList();
            _fusions = games.Where(g => !g.IsInspired).ToList();
            _inspired = games.Where(g => g.IsInspired).ToList();
            _created = CreatedGames.All();
            RefreshProfiles();
            _problem = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _problem = UiText.Get("No pude actualizar la biblioteca: ") + e.Message;
        }
    }

    private void RefreshProfiles()
    {
        _availableProfiles = library.Entries.Where(entry => entry.ProfileId != null).Select(entry => entry.ProfileId!).ToHashSet();
        try
        {
            var profiles = ProfileLibrary.All().GroupBy(profile => profile.Id).ToDictionary(group => group.Key, group => group.Last());
            foreach (string id in _fusions.Concat(_inspired).SelectMany(game => game.ProfileIds).Distinct())
                if (!profiles.ContainsKey(id) && ProfileLibrary.Find(id) is { } profile) profiles[id] = profile;
            _profiles.Clear();
            foreach (var pair in profiles) _profiles.Add(pair.Key, pair.Value);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _problem = UiText.Get("No se pudieron actualizar los datos de los juegos: ") + error.Message; }
    }

    public void Draw()
    {
        _thumbnails.Poll();
        if (!_open) return;
        if (_thumbnailRevision != library.Revision) { _thumbnailRevision = library.Revision; _thumbnails.Clear(); RefreshProfiles(); }
        // Ctrl+F goes to the search box; from the machine overview it searches all games.
        var io = ImGui.GetIO();
        if (io.KeyCtrl && !io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F, false))
        {
            if (_section == "home") Select("all");
            _focusSearch = true;
        }
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(WorkspaceLayout.Origin);
        ImGui.SetNextWindowSize(WorkspaceLayout.Size);
        bool shortList = UiTheme.CompactHeight && _section != "home";
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, shortList ? UiTheme.Px(10, 8) : UiTheme.Px(18, 18));
        if (shortList)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, Math.Min(ImGui.GetStyle().FramePadding.Y, UiTheme.Px(4))));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, Math.Min(ImGui.GetStyle().ItemSpacing.Y, UiTheme.Px(5))));
        }
        ImGui.Begin("##inicio", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
        bool compact = viewport.WorkSize.X < UiTheme.Px(800);
        // With a list on screen the section shares its row with the search box, inside the content area.
        bool sharedRow = compact && shortList;
        if (compact && !sharedRow) DrawCompactNavigation();
        else if (!compact)
        {
            ImGui.BeginChild("navegacion", new Vector2(Math.Clamp(viewport.WorkSize.X * 0.22f, UiTheme.Px(156), UiTheme.Px(210)), 0));
            UiTheme.Eyebrow("CATÁLOGO");
            UiTheme.Heading("Mis máquinas", "Originales y creaciones");
            ImGui.Spacing(); ImGui.Spacing();
            Nav("Inicio", "home");
            Nav("Todos los juegos", "all");
            Nav("Favoritos", "favorites");
            Nav("Recientes", "recent");
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextDisabled(UiText.Get("Máquinas"));
            foreach (var machine in Machines) Nav(Name(machine), "machine", machine.Id);
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            Nav("Fusionados", "fusions");
            Nav("Creados", "created");
            if (actions != null)
            {
                ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                if (ImGui.Selectable(UiText.Label("Buscar acción…"), false, ImGuiSelectableFlags.None, new Vector2(0, UiTheme.Px(28)))) actions.FindAction();
                if (ImGui.IsItemHovered()) UiTheme.Tooltip("Encontrá herramientas, controles y opciones por nombre.");
            }
            if (canReturn()) { ImGui.Spacing(); if (ImGui.Button(UiText.Label("Volver al juego"), new Vector2(-1, 0))) Close(); }
            ImGui.EndChild();
            ImGui.SameLine();
        }
        ImGui.BeginChild("contenido", new Vector2(0, 0));
        if (_section == "home")
        {
            RetroArt.LibraryMasthead(Machines.Count(), library.Entries.Count);
            DrawFileActions(primary: true);
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            DrawLibraryNotices();
            if (library.Entries.Count == 0 && !library.Scanning)
                UiTheme.EmptyState("Todavía no hay juegos en esta carpeta", "Abrí un archivo, arrastralo a la ventana o elegí la carpeta donde guardás tus juegos.");
            DrawMachines(); DrawCreativeActions();
        }
        else
        {
            DrawListHeader(compact);
            DrawLibraryNotices();
            if (sharedRow) DrawCompactNavigation();
            DrawLibraryFilters();
            DrawFilteredGames();
        }
        ImGui.EndChild();
        ImGui.End();
        ImGui.PopStyleVar(shortList ? 3 : 1);
        DrawMetadataEditor();
    }

    private void DrawFileActions(bool primary)
    {
        if (primary ? UiTheme.PrimaryButton("Abrir archivo…") : ImGui.SmallButton(UiText.Label("Abrir archivo…"))) browse();
        LibraryControlBounds?.Invoke("Abrir archivo", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        UiTheme.SameLineIfFits("Actualizar biblioteca");
        ImGui.BeginDisabled(library.Scanning);
        string label = library.Scanning ? "Buscando…" : "Actualizar biblioteca";
        if (primary ? ImGui.Button(UiText.Label(label)) : ImGui.SmallButton(UiText.Label(label))) { library.Rescan(library.Folder); Refresh(); }
        ImGui.EndDisabled();
        if (actions?.ChooseFolder is { } choose && primary)
        {
            UiTheme.SameLineIfFits("Elegir carpeta…");
            if (ImGui.Button(UiText.Label("Elegir carpeta…"))) choose();
            if (ImGui.IsItemHovered()) UiTheme.Tooltip(library.Folder);
        }
    }

    /// <summary>One row for the section's name and its counts: the list starts as high as possible.</summary>
    private void DrawListHeader(bool compact)
    {
        var cards = FilteredCards();
        var section = Catalog.Where(InSection);
        if (_section is "favorites" or "recent") section = section.DistinctBy(card => card.Key);
        int total = section.Count();
        string count = cards.Count == total ? UiText.Format($"{total} {(total == 1 ? UiText.Get("juego") : UiText.Get("juegos"))}") : UiText.Format($"{cards.Count} de {total} juegos");
        if (library.Scanning) count += " · actualizando";
        if (!compact)
        {
            using (UiTheme.Display()) ImGui.TextUnformatted(UiText.Get(SectionTitle()));
            ImGui.SameLine(); ImGui.SetCursorPosY(ImGui.GetCursorPosY() + UiTheme.Px(8)); ImGui.TextDisabled(UiText.Get(count));
            float actions = ImGui.CalcTextSize(UiText.Get("Abrir archivo…Actualizar biblioteca")).X + ImGui.GetStyle().FramePadding.X * 4 + ImGui.GetStyle().ItemSpacing.X * 2;
            if (ImGui.GetContentRegionAvail().X > actions + UiTheme.Px(40))
            {
                ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - actions);
                DrawFileActions(primary: false);
            }
        }
    }

    private void DrawLibraryNotices()
    {
        if (_activity.Problem is { } activityProblem) ImGui.TextWrapped(UiText.Get(activityProblem));
        if (_problem.Length > 0) ImGui.TextWrapped(UiText.Get(_problem));
        if (library.OrganizationError != null) ImGui.TextWrapped(UiText.Get(library.OrganizationError));
        else if (library.OrganizedCount > 0 || library.UnclassifiedCount > 0)
        {
            ImGui.TextDisabled(UiText.Get(UiText.Format($"Ordenados por sistema: {library.OrganizedCount}") +
                (library.UnclassifiedCount > 0 ? UiText.Format($" · Sin clasificar: {library.UnclassifiedCount}") : "")));
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Los archivos cuyo sistema no se puede identificar quedan en ROMs.");
        }
    }

    private string SectionTitle() => _section switch
    {
        "machine" => Name(Machines.Single(m => m.Id == _machine)), "all" => "Todos los juegos",
        "favorites" => "Favoritos", "recent" => "Recientes", "fusions" => "Fusionados",
        "created" => "Creados", "originals" => "Juegos originales", _ => "Tu biblioteca"
    };

    private void DrawCompactNavigation()
    {
        bool shared = _section != "home" && UiTheme.CompactHeight;
        ImGui.SetNextItemWidth(shared ? ImGui.GetContentRegionAvail().X * 0.34f : canReturn() ? Math.Max(UiTheme.Px(150), ImGui.GetContentRegionAvail().X - UiTheme.Px(168)) : -1);
        if (ImGui.BeginCombo("##seccion-biblioteca", UiText.Get(_section == "home" ? "Inicio" : SectionTitle())))
        {
            Choice("Inicio", "home"); Choice("Todos los juegos", "all");
            Choice("Favoritos", "favorites"); Choice("Recientes", "recent");
            ImGui.Separator();
            foreach (var machine in Machines) Choice(Name(machine), "machine", machine.Id);
            ImGui.Separator(); Choice("Fusionados", "fusions"); Choice("Creados", "created");
            ImGui.EndCombo();
        }
        if (shared) ImGui.SameLine();
        else if (canReturn()) { ImGui.SameLine(); if (ImGui.Button(UiText.Label("Volver al juego"), new(UiTheme.Px(158), 0))) Close(); }
        void Choice(string label, string section, string machine = "")
        {
            if (ImGui.Selectable(UiText.Label(label), _section == section && _machine == machine)) Select(section, machine);
        }
    }

    private void Nav(string label, string section, string machine = "")
    {
        if (ImGui.Selectable(UiText.Label(label), _section == section && _machine == machine, ImGuiSelectableFlags.None, new Vector2(0, UiTheme.Px(28))))
            Select(section, machine);
    }

    private static string Name(MachineInfo machine) => machine.Id == "invaders" ? "Space Invaders" : machine.Name;

    private void DrawMachines()
    {
        UiTheme.Section("Máquinas", "El hardware de cada época. Elegí un sistema para ver sus archivos.");
        int columns = ImGui.GetContentRegionAvail().X >= UiTheme.Px(700) ? 2 : 1;
        if (ImGui.BeginTable("maquinas", columns, ImGuiTableFlags.SizingStretchSame))
        {
            int index=0;
            foreach (var machine in Machines)
            {
                index++; ImGui.TableNextColumn();
                var size = new Vector2(ImGui.GetContentRegionAvail().X, UiTheme.Px(122));
                bool selected = ImGui.Button(UiText.Label(UiText.Get("##maquina-") + machine.Id), size);
                bool hovered = ImGui.IsItemHovered() || ImGui.IsItemFocused();
                var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax(); var draw = ImGui.GetWindowDrawList();
                uint accent=ImGui.ColorConvertFloat4ToU32(UiTheme.Accent);
                draw.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.ChildBg)==0?ImGui.ColorConvertFloat4ToU32(UiTheme.Panel):ImGui.GetColorU32(ImGuiCol.ChildBg),UiTheme.Px(2));
                draw.AddRect(min,max,hovered?accent:ImGui.GetColorU32(ImGuiCol.Border),UiTheme.Px(2),ImDrawFlags.None,hovered?UiTheme.Px(2):1);
                draw.AddRectFilled(min+UiTheme.Px(1,1),min+new Vector2(size.X-UiTheme.Px(1),UiTheme.Px(4)),accent);
                float artWidth=Math.Min(UiTheme.Px(144),size.X*0.29f);
                HardwareArt.Draw(draw,machine.Id,min+UiTheme.Px(8,12),new(artWidth,UiTheme.Px(80)));
                float textX=artWidth+UiTheme.Px(22),width=size.X-textX-UiTheme.Px(12);
                draw.AddText(min+new Vector2(textX,UiTheme.Px(18)),ImGui.GetColorU32(ImGuiCol.Text),UiText.Get(UiTheme.Fit(Name(machine),width)));
                string formats=machine.Id switch {"zx48"=>"TAP / TZX / Z80 / SNA","c64"=>"PRG / T64","nes"=>"CARTUCHOS NES","pcxt"=>"EXE / COM / ZIP / IMG",_=>"ROM DE PLACA ARCADE"};
                using(UiTheme.Label()) draw.AddText(min+new Vector2(textX,UiTheme.Px(46)),ImGui.GetColorU32(ImGuiCol.TextDisabled),UiTheme.Fit(UiText.Get(formats),width));
                draw.AddText(min+new Vector2(textX,UiTheme.Px(72)),ImGui.GetColorU32(ImGuiCol.TextDisabled),UiTheme.Fit(UiText.Get(Summary(machine.Id)),width));
                draw.AddLine(min+UiTheme.Px(14,96),new(max.X-UiTheme.Px(14),min.Y+UiTheme.Px(96)),ImGui.GetColorU32(ImGuiCol.Border));
                using(UiTheme.Label())
                {
                    draw.AddText(min+UiTheme.Px(14,102),accent,UiText.Get(UiTheme.Fit(UiText.Format($"{index:00} / {machine.Cpu}"),size.X-UiTheme.Px(108))));
                    draw.AddText(new(max.X-UiTheme.Px(78),min.Y+UiTheme.Px(102)),ImGui.GetColorU32(ImGuiCol.Text),UiText.Get("ABRIR >"));
                }
                if(!UiTheme.HighContrast) RetroArt.Screw(draw,new(max.X-UiTheme.Px(8),min.Y+UiTheme.Px(12)));
                if (hovered) UiTheme.Tooltip(Name(machine)+" · "+machine.Cpu+"\n"+Summary(machine.Id));
                if (selected) Select("machine",machine.Id);
                ImGui.Spacing();
            }
            ImGui.EndTable();
        }
        if (ImGui.Button(UiText.Label("Ver todos los juegos"))) Select("all");
    }

    private void DrawCreativeActions()
    {
        if (actions == null) return;
        UiTheme.Section("Dale forma a una idea", "Creá algo propio o combiná mecánicas de los originales que tenés.");
        if (ImGui.Button(UiText.Label("Crear desde cero…"))) actions.Create();
        UiTheme.SameLineIfFits("Fusionar juegos…");
        if (ImGui.Button(UiText.Label("Fusionar juegos…"))) actions.Fuse();
    }

    private bool Ready(CreatedGame game) => _readyCreated.Get(_contentRevision,
        () => _created.Select(g => g.ProgramPath).Where(File.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase)).Contains(game.ProgramPath);

    private string Summary(string machineId) => _summaries.Get((library.Revision, _contentRevision, library.Scanning),
        () => Machines.ToDictionary(machine => machine.Id, machine => SummaryOf(machine.Id)))[machineId];

    private string SummaryOf(string machineId)
    {
        if (library.Scanning) return "Buscando juegos...";
        var originals = library.Originals(machineId: machineId).ToList();
        var created = _created.Where(g => g.Machine == machineId).ToList();
        int count = originals.Count + created.Count;
        int ready = originals.Count(g => g.NotYet == null && g.Confidence >= 50) + created.Count(Ready);
        return count == 0 ? "Sin juegos agregados"
            : ready == count ? UiText.Format($"{count} archivo{(count == 1 ? UiText.Get("") : UiText.Get("s"))}") : UiText.Format($"{count} archivos / {ready} admitidos");
    }

    private static string OriginalKey(LibraryEntry game) => game.Identity.Length > 0 ? game.Identity
        : LibraryKey.Named("original", game.Machine.Id + "\0" + game.Path);
    private static string FusionKey(StudioGame game) => LibraryKey.Named("fusion", game.Id);
    private static string CreatedKey(CreatedGame game) => LibraryKey.Named("created", game.Machine + "\0" +
        (string.IsNullOrWhiteSpace(game.WorkId) ? game.Created.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : game.WorkId));

    public void RecordOriginal(string path, string machine, IReadOnlyList<MediaFile> files)
    {
        // Creator launches can precede the next library refresh.
        CreatedGame? created = null;
        try { created = CreatedGames.All().FirstOrDefault(g => Path.GetFullPath(g.ProgramPath).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        _activity.Played(created == null ? LibraryKey.Original(machine, files) : CreatedKey(created));
    }

    public void RecordFusion(StudioGame game) => _activity.Played(FusionKey(game));

    public void Dispose() => _thumbnails.Dispose();
}
