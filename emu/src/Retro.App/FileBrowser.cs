using System.Numerics;
using ImGuiNET;

namespace Retro.App;

/// <summary>Simple cross-platform file picker drawn with ImGui.</summary>
public sealed class FileBrowser
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tap", ".tzx", ".z80", ".sna", ".rfstate", ".prg", ".d64", ".t64", ".crt", ".dsk", ".do",
        ".po", ".woz", ".adf", ".st", ".msa", ".exe", ".com", ".rom", ".bin", ".xex", ".atr", ".d88",
    };

    private string _dir;
    private bool _open;
    private bool _showAll;
    private string _pathInput;

    public FileBrowser(string startDir)
    {
        _dir = startDir;
        _pathInput = startDir;
    }

    public void Open()
    {
        _open = true;
        _pathInput = _dir;
    }

    public void Draw(Action<string> onPick)
    {
        if (!_open)
            return;
        ImGui.SetNextWindowSize(new Vector2(700, 520), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Abrir archivo", ref _open, ImGuiWindowFlags.NoDocking))
        {
            ImGui.End();
            return;
        }
        ImGui.SetNextItemWidth(-130);
        if (ImGui.InputText("##path", ref _pathInput, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (Directory.Exists(_pathInput))
                _dir = _pathInput;
            else if (File.Exists(_pathInput))
            {
                onPick(_pathInput);
                _open = false;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Subir"))
        {
            var parent = Directory.GetParent(_dir);
            if (parent != null)
            {
                _dir = parent.FullName;
                _pathInput = _dir;
            }
        }
        ImGui.SameLine();
        ImGui.Checkbox("Todo", ref _showAll);
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                if (ImGui.SmallButton(drive.Name))
                {
                    _dir = drive.RootDirectory.FullName;
                    _pathInput = _dir;
                }
                ImGui.SameLine();
            }
            ImGui.NewLine();
        }
        ImGui.BeginChild("files", new Vector2(0, -30));
        try
        {
            foreach (var d in Directory.GetDirectories(_dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (ImGui.Selectable("[carpeta] " + Path.GetFileName(d), false, ImGuiSelectableFlags.AllowDoubleClick)
                    && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _dir = d;
                    _pathInput = d;
                }
            }
            foreach (var f in Directory.GetFiles(_dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (!_showAll && !Known.Contains(Path.GetExtension(f)))
                    continue;
                long size = new FileInfo(f).Length;
                if (ImGui.Selectable($"{Path.GetFileName(f)}   ({size / 1024.0:F1} KB)", false, ImGuiSelectableFlags.AllowDoubleClick)
                    && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    onPick(f);
                    _open = false;
                }
            }
        }
        catch (Exception e)
        {
            ImGui.TextUnformatted("No se puede leer la carpeta: " + e.Message);
        }
        ImGui.EndChild();
        ImGui.TextDisabled("Doble clic para abrir. También podés arrastrar archivos a la ventana.");
        ImGui.End();
    }
}
