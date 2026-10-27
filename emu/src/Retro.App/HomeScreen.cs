using System.Numerics;
using ImGuiNET;
using Retro.Ai;
using Retro.Core;
using Retro.Fusion;
using Retro.Machines;
using Retro.Studio;

namespace Retro.App;

/// <summary>The main browser replaces the play area; it is never a floating window.</summary>
public sealed class HomeScreen(GameLibrary library, Action<string> playOriginal, Action<string> playFusion,
    Action browse, Action<bool> visibility, Func<bool> canReturn, LibraryActivity? activity = null) : IDisposable
{
    private readonly LibraryActivity _activity = activity ?? new();
    private readonly ThumbnailCache _thumbnails = new();
    private int _thumbnailRevision = -1;
    private readonly Dictionary<string, GameProfile> _profiles = new(StringComparer.Ordinal);
    private HashSet<string> _availableProfiles = new(StringComparer.Ordinal);
    private bool _open, _compactRows, _refreshed;
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

    public void ShowOriginals() { Select("originals"); Open(); }
    public void ShowFusions() { Select("fusions"); Open(); }
    public void ShowFavorites() { Select("favorites"); Open(); }
    public void ShowRecents() { Select("recent"); Open(); }

    private void Select(string section, string machine = "")
    {
        _section = section;
        _machine = machine;
        _search = "";
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
            if (File.Exists(game.ProgramPath))
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
        _refreshed = true;
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
            _problem = "No pude actualizar la biblioteca: " + e.Message;
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
        { _problem = "No se pudieron actualizar los datos de los juegos: " + error.Message; }
    }

    public void Draw()
    {
        _thumbnails.Poll();
        if (!_open) return;
        if (_thumbnailRevision != library.Revision) { _thumbnailRevision = library.Revision; _thumbnails.Clear(); RefreshProfiles(); }
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, Math.Max(1, viewport.WorkSize.Y - UiTheme.StatusBarHeight)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, UiTheme.Px(18, 18));
        ImGui.Begin("##inicio", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
        bool compact = viewport.WorkSize.X < UiTheme.Px(800);
        if (compact) DrawCompactNavigation();
        else
        {
            ImGui.BeginChild("navegacion", new Vector2(Math.Clamp(viewport.WorkSize.X * 0.22f, UiTheme.Px(156), UiTheme.Px(210)), 0));
            ImGui.SetWindowFontScale(1.4f);
            ImGui.TextUnformatted("RetroEmu");
            ImGui.SetWindowFontScale(1);
            ImGui.Spacing(); ImGui.Spacing();
            Nav("Inicio", "home");
            Nav("Todos los juegos", "all");
            Nav("Favoritos", "favorites");
            Nav("Recientes", "recent");
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextDisabled("Máquinas");
            foreach (var machine in Machines) Nav(Name(machine), "machine", machine.Id);
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            Nav("Fusionados", "fusions");
            Nav("Creados", "created");
            ImGui.Spacing(); ImGui.Spacing();
            if (canReturn() && ImGui.Button("Volver al juego", new Vector2(-1, 0))) Close();
            ImGui.EndChild();
            ImGui.SameLine();
        }
        ImGui.BeginChild("contenido", new Vector2(0, 0));
        string title = SectionTitle();
        if (!compact)
        {
            ImGui.SetWindowFontScale(1.4f); ImGui.TextUnformatted(title); ImGui.SetWindowFontScale(1);
            ImGui.Spacing();
        }
        if (ImGui.Button("Abrir archivo...")) browse();
        ImGui.SameLine();
        ImGui.BeginDisabled(library.Scanning);
        if (ImGui.Button(library.Scanning ? "Buscando..." : "Volver a buscar"))
        {
            library.Rescan(library.Folder);
            Refresh();
        }
        ImGui.EndDisabled();
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        if (_activity.Problem is { } activityProblem) ImGui.TextWrapped(activityProblem);
        if (_problem.Length > 0) ImGui.TextWrapped(_problem);
        if (library.OrganizationError != null) ImGui.TextWrapped(library.OrganizationError);
        else if (library.OrganizedCount > 0 || library.UnclassifiedCount > 0)
        {
            ImGui.TextDisabled($"Ordenados por sistema: {library.OrganizedCount}" +
                (library.UnclassifiedCount > 0 ? $" · Sin clasificar: {library.UnclassifiedCount}" : ""));
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Los archivos cuyo sistema no se puede identificar quedan en ROMs.");
        }
        if (_section == "home") DrawMachines();
        else
        {
            ImGui.SetNextItemWidth(Math.Max(UiTheme.Px(80), ImGui.GetContentRegionAvail().X - UiTheme.Px(92)));
            ImGui.InputTextWithHint("##buscar", "Buscar por nombre", ref _search, 150);
            ImGui.SameLine(); ImGui.BeginDisabled(_search.Length == 0);
            if (ImGui.Button("Limpiar", new(UiTheme.Px(82), 0))) _search = "";
            ImGui.EndDisabled();
            ImGui.Spacing();
            if (_section is "favorites" or "recent") DrawPersonal();
            else if (_section == "fusions") DrawFusions();
            else if (_section == "created") DrawCreated();
            else DrawOriginals();
        }
        ImGui.EndChild();
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private string SectionTitle() => _section switch
    {
        "machine" => Name(Machines.Single(m => m.Id == _machine)), "all" => "Todos los juegos",
        "favorites" => "Favoritos", "recent" => "Recientes", "fusions" => "Fusionados",
        "created" => "Creados", "originals" => "Juegos originales", _ => "Elegí una máquina"
    };

    private void DrawCompactNavigation()
    {
        ImGui.SetNextItemWidth(canReturn() ? Math.Max(UiTheme.Px(150), ImGui.GetContentRegionAvail().X - UiTheme.Px(168)) : -1);
        if (ImGui.BeginCombo("##seccion-biblioteca", _section == "home" ? "Inicio" : SectionTitle()))
        {
            Choice("Inicio", "home"); Choice("Todos los juegos", "all");
            Choice("Favoritos", "favorites"); Choice("Recientes", "recent");
            ImGui.Separator();
            foreach (var machine in Machines) Choice(Name(machine), "machine", machine.Id);
            ImGui.Separator(); Choice("Fusionados", "fusions"); Choice("Creados", "created");
            ImGui.EndCombo();
        }
        if (canReturn()) { ImGui.SameLine(); if (ImGui.Button("Volver al juego", new(UiTheme.Px(158), 0))) Close(); }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        void Choice(string label, string section, string machine = "")
        {
            if (ImGui.Selectable(label, _section == section && _machine == machine)) Select(section, machine);
        }
    }

    private void Nav(string label, string section, string machine = "")
    {
        if (ImGui.Selectable(label, _section == section && _machine == machine, ImGuiSelectableFlags.None, new Vector2(0, UiTheme.Px(28))))
            Select(section, machine);
    }

    private static string Name(MachineInfo machine) => machine.Id == "invaders" ? "Space Invaders" : machine.Name;

    private void DrawMachines()
    {
        int columns = ImGui.GetContentRegionAvail().X >= UiTheme.Px(700) ? 2 : 1;
        if (ImGui.BeginTable("maquinas", columns, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var machine in Machines)
            {
                ImGui.TableNextColumn();
                var size = new Vector2(ImGui.GetContentRegionAvail().X, UiTheme.Px(116));
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
                    "pcxt" => ImGui.ColorConvertFloat4ToU32(new Vector4(0.88f, 0.75f, 0.48f, 1)),
                    _ => ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.85f, 0.55f, 1)),
                };
                draw.AddRectFilled(min, max, ImGui.GetColorU32(hovered ? ImGuiCol.HeaderHovered : ImGuiCol.FrameBg), 5);
                draw.AddRectFilled(min, new Vector2(min.X + UiTheme.Px(4), max.Y), accent, 3);
                HardwareArt.Draw(draw, machine.Id, min + UiTheme.Px(12, 7), UiTheme.Px(100, 76));
                float width = size.X - UiTheme.Px(136);
                draw.AddText(min + UiTheme.Px(124, 18), ImGui.GetColorU32(ImGuiCol.Text), UiTheme.Fit(Name(machine), width));
                draw.AddText(min + UiTheme.Px(124, 42), ImGui.GetColorU32(ImGuiCol.TextDisabled), UiTheme.Fit(machine.Cpu, width));
                var originals = library.Originals(machineId: machine.Id).ToList();
                int count = originals.Count + _created.Count(g => g.Machine == machine.Id);
                int ready = originals.Count(g => g.NotYet == null && g.Confidence >= 50) + _created.Count(g => g.Machine == machine.Id && File.Exists(g.ProgramPath));
                string summary = library.Scanning ? "Buscando juegos..." : count == 0 ? "Sin juegos agregados"
                    : ready == count ? $"{count} juego{(count == 1 ? "" : "s")}" : $"{count} archivos / {ready} admitidos";
                draw.AddText(min + UiTheme.Px(124, 66), ImGui.GetColorU32(ImGuiCol.TextDisabled), UiTheme.Fit(summary, width));
                draw.AddText(min + UiTheme.Px(18, 88), accent, "Ver juegos >");
                if (hovered) UiTheme.Tooltip(Name(machine) + "\n" + summary);
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
        var fusions = _section == "all" ? _fusions.Concat(_inspired).Where(g => Matches(g.Title)).ToList() : new List<StudioGame>();
        if (_section == "machine")
        {
            ImGui.TextWrapped(_machine switch
            {
                "pcxt" => "FreeDOS y BIOS libres incluidos. Abrí un EXE, COM o ZIP para PC/XT, o un disquete IMG/IMA de 160 a 720 KB. Gráficos CGA.",
                "zx48" => "Cintas TAP/TZX e instantáneas Z80/SNA.",
                "c64" => "Programas PRG y cintas T64. Los discos D64 y cartuchos CRT siguen pendientes.",
                "nes" => "Cartuchos NES. La compatibilidad depende de la placa del cartucho.",
                _ => "ROMs de la placa arcade, en un archivo ZIP."
            });
            ImGui.Spacing();
        }
        ImGui.TextDisabled($"{games.Count + created.Count + fusions.Count} resultados");
        if (games.Count + created.Count + fusions.Count == 0)
        {
            ImGui.TextWrapped(library.Scanning ? "Buscando juegos..." : _search.Length > 0
                ? "No hay resultados para esta búsqueda." : "No hay juegos agregados. Usá Abrir archivo o arrastrá uno a la ventana.");
            return;
        }
        if (BeginList())
        {
            foreach (var game in games)
                Row(OriginalKey(game), game.Name, Name(game.Machine), game.NotYet,
                    () => playOriginal(game.Path), game.NotYet == null && game.Confidence >= 50, game.Machine.Id, () => ThumbnailFiles.Beside(game.Path));
            foreach (var game in created)
                Row(CreatedKey(game), game.Title, game.MachineName, game.Pitch,
                    () => playOriginal(game.ProgramPath), File.Exists(game.ProgramPath), game.Machine, () => game.PicturePath);
            foreach (var game in fusions) FusionRow(game);
            ImGui.EndTable();
        }

    }

    private void DrawFusions()
    {
        AiPicker.Hint("Las fusiones nuevas se preparan desde Crear > Fusionar juegos.");
        ImGui.Spacing();
        var games = _fusions.Where(g => Matches(g.Title)).ToList();
        if (games.Count == 0) { ImGui.TextWrapped("No hay fusiones para mostrar."); return; }
        if (BeginList())
        {
            foreach (var game in games) FusionRow(game);
            ImGui.EndTable();
        }

    }

    private void FusionRow(StudioGame game)
    {
        var have = _availableProfiles;
        string missing = string.Join(", ", game.Games.Where(g => !have.Contains(g.Profile))
            .Select(g => _profiles.GetValueOrDefault(g.Profile)?.Name ?? g.Profile));
        Row(FusionKey(game), game.Title, $"{(game.IsInspired ? "Con originales" : "Fusión")} · v{game.Version}", missing.Length > 0 ? "Falta: " + missing : game.Pitch,
            () => playFusion(game.Id), missing.Length == 0,
            _profiles.GetValueOrDefault(game.Games.FirstOrDefault(g => g.Slot == game.BaseSlot)?.Profile ?? game.Games.FirstOrDefault()?.Profile ?? "")?.Machine ?? "fusion",
            () => StudioLibrary.PicturePath(game));
    }

    private void DrawCreated()
    {
        AiPicker.Hint("Para un proyecto nuevo, usá el menú Crear.");
        ImGui.Spacing();
        var games = _created.Where(g => Matches(g.Title)).ToList();
        var inspired = _inspired.Where(g => Matches(g.Title)).ToList();
        if (games.Count + inspired.Count == 0) { ImGui.TextWrapped("No hay juegos creados para mostrar."); return; }
        if (BeginList())
        {
            foreach (var game in games)
                Row(CreatedKey(game), game.Title, game.MachineName, game.Pitch,
                    () => playOriginal(game.ProgramPath), File.Exists(game.ProgramPath), game.Machine, () => game.PicturePath);
            foreach (var game in inspired) FusionRow(game);
            ImGui.EndTable();
        }

    }

    private static string OriginalKey(LibraryEntry game) => game.Identity.Length > 0 ? game.Identity
        : LibraryKey.Named("original", game.Machine.Id + "\0" + game.Path);
    private static string FusionKey(StudioGame game) => LibraryKey.Named("fusion", game.Id);
    private static string CreatedKey(CreatedGame game) => LibraryKey.Named("created", game.Machine + "\0" +
        (game.WorkId ?? game.Created.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public void RecordOriginal(string path, string machine, IReadOnlyList<MediaFile> files)
    {
        // Creator launches can precede the next library refresh.
        CreatedGame? created = null;
        try { created = CreatedGames.All().FirstOrDefault(g => Path.GetFullPath(g.ProgramPath).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        _activity.Played(created == null ? LibraryKey.Original(machine, files) : CreatedKey(created));
    }

    public void RecordFusion(StudioGame game) => _activity.Played(FusionKey(game));

    private sealed record PersonalRow(string Key, string Title, Action Draw);
    private void DrawPersonal()
    {
        if (_section == "recent")
        {
            ImGui.BeginDisabled(_activity.Recents.Count == 0);
            if (ImGui.Button("Vaciar recientes")) _activity.ClearRecents();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Quita la lista; conserva tus juegos y favoritos.");
            ImGui.Spacing();
        }
        var rows = library.Originals().Select(g => new PersonalRow(OriginalKey(g), g.Name,
                () => Row(OriginalKey(g), g.Name, Name(g.Machine), g.NotYet, () => playOriginal(g.Path),
                    g.NotYet == null && g.Confidence >= 50, g.Machine.Id, () => ThumbnailFiles.Beside(g.Path))))
            .Concat(_created.Select(g => new PersonalRow(CreatedKey(g), g.Title,
                () => Row(CreatedKey(g), g.Title, g.MachineName, g.Pitch, () => playOriginal(g.ProgramPath),
                    File.Exists(g.ProgramPath), g.Machine, () => g.PicturePath))))
            .Concat(_fusions.Concat(_inspired).Select(g => new PersonalRow(FusionKey(g), g.Title, () => FusionRow(g))))
            .Where(r => Matches(r.Title) && (_section == "favorites" ? _activity.IsFavorite(r.Key) : _activity.LastPlayed(r.Key) != null))
            .DistinctBy(r => r.Key);
        rows = _section == "recent" ? rows.OrderByDescending(r => _activity.LastPlayed(r.Key)).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            : rows.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase);
        var shown = rows.ToList();
        if (shown.Count == 0)
        {
            ImGui.TextWrapped(library.Scanning ? "Buscando juegos..." : _search.Length > 0
                ? "No hay resultados para esta búsqueda." : _section == "favorites"
                ? "Marcá la estrella junto a un juego para guardarlo acá." : "Acá aparecen los juegos que abriste.");
            return;
        }
        if (BeginList())
        {
            foreach (var row in shown) row.Draw();
            ImGui.EndTable();
        }

    }

    private bool Matches(string title) => title.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool BeginList()
    {
        _compactRows = ImGui.GetContentRegionAvail().X < UiTheme.Px(640);
        if (!ImGui.BeginTable("juegos", _compactRows ? 3 : 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
            new Vector2(0, Math.Max(UiTheme.Px(50), ImGui.GetContentRegionAvail().Y - UiTheme.Px(30))))) return false;
        ImGui.TableSetupColumn("Juego", ImGuiTableColumnFlags.WidthStretch);
        if (!_compactRows) ImGui.TableSetupColumn("Sistema", ImGuiTableColumnFlags.WidthFixed, UiTheme.Px(156));
        ImGui.TableSetupColumn("Fav.", ImGuiTableColumnFlags.WidthFixed, UiTheme.Px(32));
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, UiTheme.Px(75));
        ImGui.TableHeadersRow();
        return true;
    }

    private void Row(string id, string title, string detail, string? hint, Action play, bool playable, string machine, Func<string?> picture)
    {
        ImGui.TableNextRow();
        ImGui.PushID(ImGui.TableGetRowIndex());
        ImGui.PushID(id);
        ImGui.TableNextColumn();
        var position = ImGui.GetCursorScreenPos();
        var size = UiTheme.Px(58, 42);
        ImGui.Dummy(size);
        if (ImGui.IsItemVisible())
        {
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(position, position + size, ImGui.GetColorU32(ImGuiCol.FrameBg), 3);
            if (_thumbnails.TryGet(id, picture, out var texture))
            {
                float scale = Math.Min(size.X / texture.Width, size.Y / texture.Height);
                var fitted = new Vector2(texture.Width, texture.Height) * scale;
                var at = position + (size - fitted) / 2;
                draw.AddImage((nint)texture.Id, at, at + fitted);
            }
            else HardwareArt.Draw(draw, machine, position + UiTheme.Px(2, 1), size - UiTheme.Px(4, 2));
        }
        ImGui.SameLine(); ImGui.BeginGroup();
        float textWidth = Math.Max(20, ImGui.GetContentRegionAvail().X);
        ImGui.TextWrapped(UiTheme.Fit(title, textWidth * 2));
        if (ImGui.IsItemHovered()) UiTheme.Tooltip(title + (string.IsNullOrWhiteSpace(hint) ? "" : "\n" + hint));
        if (_compactRows) ImGui.TextDisabled(UiTheme.Fit(detail, textWidth));
        if (!playable)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Warning);
            ImGui.TextWrapped(UiTheme.Fit(hint ?? "Formato sin confirmar", textWidth * 2));
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) UiTheme.Tooltip(hint ?? "Todavía no se puede abrir este formato.");
        }
        ImGui.EndGroup();
        if (!_compactRows)
        {
            ImGui.TableNextColumn(); ImGui.TextUnformatted(UiTheme.Fit(detail, ImGui.GetContentRegionAvail().X));
            if (ImGui.IsItemHovered()) UiTheme.Tooltip(detail);
        }
        ImGui.TableNextColumn();
        bool favorite = _activity.IsFavorite(id);
        if (ImGui.Button("##favorito", UiTheme.Px(28, 24))) _activity.SetFavorite(id, !favorite);
        var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        Span<Vector2> star = stackalloc Vector2[10];
        for (int i = 0; i < star.Length; i++)
        {
            float angle = -MathF.PI / 2 + i * MathF.PI / 5, radius = UiTheme.Px((i & 1) == 0 ? 9 : 4);
            star[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        var starDraw = ImGui.GetWindowDrawList();
        uint color = favorite ? ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0.78f, 0.25f, 1)) : ImGui.GetColorU32(ImGuiCol.TextDisabled);
        for (int i = 0; i < star.Length; i++) starDraw.AddLine(star[i], star[(i + 1) % star.Length], color, favorite ? 2 : 1);
        if (ImGui.IsItemHovered()) UiTheme.Tooltip(favorite ? "Quitar de favoritos" : "Agregar a favoritos");
        ImGui.TableNextColumn();
        ImGui.BeginDisabled(!playable || library.Scanning);
        if (ImGui.Button("Jugar", new Vector2(-1, 0))) { Close(); play(); }
        ImGui.EndDisabled();
        if (!playable && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiTheme.Tooltip(hint ?? "Este formato todavía no se puede abrir.");
        ImGui.PopID(); ImGui.PopID();
    }

    public void Dispose() => _thumbnails.Dispose();
}
