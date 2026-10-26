using System.Numerics;
using ImGuiNET;
using Retro.Machines;

namespace Retro.App;

/// <summary>A cached file picker: selecting a file and opening it are separate actions.</summary>
public sealed class FileBrowser
{
    private static readonly HashSet<string> Known = new(MachineCatalog.All.SelectMany(m => m.Extensions)
        .Concat(new[] { ".zip", ".rfstate", ".rom", ".bin" }), StringComparer.OrdinalIgnoreCase);
    private sealed record Item(string Path, string Name, bool Directory, long Size, bool Hidden);
    private string _dir, _pathInput, _search = "", _problem = "";
    private string? _selected;
    private bool _open, _showAll;
    private IReadOnlyList<Item> _items = [];
    private IReadOnlyList<string> _drives = [];
    private DateTime _refreshAt;

    public FileBrowser(string startDir) { _dir = startDir; _pathInput = startDir; }
    public void Open()
    {
        _open = true; _pathInput = _dir; _selected = null; _search = "";
        Refresh();
        var drives = new List<string>();
        if (OperatingSystem.IsWindows())
            foreach (var drive in DriveInfo.GetDrives())
                try { if (drive.IsReady) drives.Add(drive.RootDirectory.FullName); }
                catch (IOException) { }
        _drives = drives;
    }

    private bool Navigate(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) { _problem = "No se encontró esa carpeta."; return false; }
            _dir = _pathInput = full; _search = ""; _selected = null; Refresh(); return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { _problem = "No se puede abrir esa carpeta: " + e.Message; return false; }
    }
    private void Refresh()
    {
        _refreshAt = DateTime.UtcNow.AddSeconds(2);
        try
        {
            _items = new DirectoryInfo(_dir).EnumerateFileSystemInfos().Select(info =>
                new Item(info.FullName, info.Name, info is DirectoryInfo, info is FileInfo file ? file.Length : 0,
                    info.Attributes.HasFlag(FileAttributes.Hidden)))
                .OrderByDescending(item => item.Directory).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (_selected != null && !_items.Any(item => item.Path == _selected)) _selected = null;
            _problem = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { _items = []; _selected = null; _problem = "No se puede leer la carpeta: " + e.Message; }
    }
    private IEnumerable<Item> VisibleItems() => _items.Where(item =>
        (_showAll || !item.Hidden && (item.Directory || Known.Contains(Path.GetExtension(item.Path)))) &&
        item.Name.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase));

    private void OpenSelected(Action<string> onPick)
    {
        if (_selected == null) return;
        if (Directory.Exists(_selected)) { Navigate(_selected); return; }
        if (!File.Exists(_selected)) { Refresh(); _problem = "Ese archivo ya no está disponible. Elegí otro."; return; }
        string path = _selected; _open = false; onPick(path);
    }

    public void Draw(Action<string> onPick)
    {
        if (!_open) return;
        if (DateTime.UtcNow >= _refreshAt) Refresh();
        ImGui.SetNextWindowSize(new(760, 540), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Abrir archivo", ref _open, ImGuiWindowFlags.NoDocking)) { ImGui.End(); return; }
        WorkspaceLayout.KeepOnScreen();
        ImGui.SetNextItemWidth(-90);
        if (ImGui.InputTextWithHint("##path", "Carpeta o archivo", ref _pathInput, 2048, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (File.Exists(_pathInput)) { _selected = Path.GetFullPath(_pathInput); OpenSelected(onPick); }
            else Navigate(_pathInput);
        }
        if (ImGui.IsItemHovered()) UiTheme.Tooltip("Podés pegar una ruta y pulsar Enter.");
        ImGui.SameLine();
        var parent = Directory.GetParent(_dir);
        ImGui.BeginDisabled(parent == null);
        if (ImGui.Button("Subir", new(80, 0))) Navigate(parent!.FullName);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered()) UiTheme.Tooltip("Ir a la carpeta superior.");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##filtro", "Buscar en esta carpeta", ref _search, 150)) _selected = null;
        if (ImGui.Checkbox("Todos los archivos", ref _showAll)) _selected = null;
        foreach (string drive in _drives)
        {
            if (ImGui.GetContentRegionAvail().X > 80) ImGui.SameLine();
            if (ImGui.SmallButton(drive)) Navigate(drive);
        }
        if (_problem.Length > 0) ImGui.TextWrapped(_problem);
        var shown = VisibleItems().ToArray();
        if (ImGui.BeginTable("archivos", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
            new(0, Math.Max(60, ImGui.GetContentRegionAvail().Y - 100))))
        {
            ImGui.TableSetupColumn("Nombre", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Tamaño", ImGuiTableColumnFlags.WidthFixed, 85); ImGui.TableHeadersRow();
            foreach (var item in shown)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.PushID(item.Path);
                string label = (item.Directory ? "> " : "") + item.Name;
                if (ImGui.Selectable(UiTheme.Fit(label, ImGui.GetContentRegionAvail().X) + "##archivo", _selected == item.Path,
                    ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.SpanAllColumns))
                {
                    _selected = item.Path;
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { OpenSelected(onPick); ImGui.PopID(); break; }
                }
                if (ImGui.IsItemHovered()) UiTheme.Tooltip(item.Path);
                ImGui.TableNextColumn(); ImGui.TextDisabled(item.Directory ? "Carpeta" : item.Size >= 1024 * 1024
                    ? $"{item.Size / (1024.0 * 1024):F1} MB" : $"{item.Size / 1024.0:F1} KB");
                ImGui.PopID();
            }
            if (shown.Length == 0) { ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextDisabled("No hay archivos para mostrar."); }
            ImGui.EndTable();
        }
        ImGui.TextUnformatted(UiTheme.Fit(_selected == null ? "Seleccioná un archivo para abrirlo." : Path.GetFileName(_selected), ImGui.GetContentRegionAvail().X));
        ImGui.BeginDisabled(_selected == null);
        if (ImGui.Button("Abrir", new(110, 0))) OpenSelected(onPick);
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.Button("Cancelar", new(110, 0))) _open = false;
        ImGui.SameLine(); ImGui.TextDisabled($"{shown.Length} elementos");
        ImGui.End();
    }
}