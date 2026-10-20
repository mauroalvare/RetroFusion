using System.Numerics;
using ImGuiNET;
using Retro.Ai;
using Retro.Fusion;
using Retro.Studio;

namespace Retro.App;

/// <summary>A single entry point for playing; originals never depend on a stored fusion.</summary>
public sealed class GamesWindow(GameLibrary library, Action<string> playOriginal, Action<string> playFusion,
    Action browse, Action fuse, Action create, Action<bool> visibility)
{
    private bool _open;
    private string _search = "", _problem = "";
    private List<StudioGame> _fusions = new();
    private IReadOnlyList<CreatedGame> _created = Array.Empty<CreatedGame>();
    public bool IsOpen => _open;

    public void Open()
    {
        if (!_open) { _open = true; visibility(true); }
        Refresh();
    }

    public void Close()
    {
        if (_open) { _open = false; visibility(false); }
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
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size / 2, ImGuiCond.FirstUseEver, new Vector2(0.5f));
        ImGui.SetNextWindowSize(new Vector2(Math.Min(900, viewport.Size.X - 30), Math.Min(620, viewport.Size.Y - 70)), ImGuiCond.FirstUseEver);
        bool open = true;
        bool shown = ImGui.Begin("Tus juegos", ref open, ImGuiWindowFlags.NoDocking);
        if (!open) Close();
        if (shown)
        {
            ImGui.TextColored(new Vector4(1, 0.85f, 0.3f, 1), "Elegí un juego y empezá a jugar");
            ImGui.TextWrapped("Tus originales, las fusiones y los juegos nuevos tienen su propio lugar.");
            if (ImGui.Button("Abrir archivo...")) { Close(); browse(); }
            ImGui.SameLine();
            if (ImGui.Button("Fusionar juegos")) { Close(); fuse(); }
            ImGui.SameLine();
            if (ImGui.Button("Crear un juego")) { Close(); create(); }
            ImGui.Separator();
            ImGui.SetNextItemWidth(-110);
            ImGui.InputTextWithHint("##buscar", "Buscar un juego por nombre", ref _search, 150);
            ImGui.SameLine();
            if (ImGui.Button(library.Scanning ? "Buscando..." : "Actualizar") && !library.Scanning)
            {
                library.Rescan(library.Folder);
                Refresh();
            }
            if (_problem.Length > 0) ImGui.TextWrapped(_problem);
            if (ImGui.BeginTabBar("biblioteca"))
            {
                if (ImGui.BeginTabItem("Originales"))
                {
                    ImGui.TextDisabled("Siempre disponibles, aunque también tengan una fusión.");
                    var games = library.Originals(_search).ToList();
                    if (BeginList())
                    {
                        foreach (var game in games)
                            Row(game.Path, game.Name, game.Machine.Name, game.NotYet,
                                () => playOriginal(game.Path), game.NotYet == null && game.Confidence >= 50);
                        ImGui.EndTable();
                    }
                    if (games.Count == 0) ImGui.TextWrapped(library.Scanning ? "Buscando tus juegos..." : "No hay originales con ese nombre. Podés abrir un archivo con el botón de arriba.");
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Fusionados"))
                {
                    ImGui.TextDisabled("Juegos que combinan tus originales. Cada entrada abre su última versión.");
                    var have = library.Entries.Where(e => e.ProfileId != null).Select(e => e.ProfileId!).ToHashSet();
                    var games = _fusions.Where(g => Matches(g.Title)).ToList();
                    if (BeginList())
                    {
                        foreach (var game in games)
                        {
                            string missing = string.Join(", ", game.Games.Where(g => !have.Contains(g.Profile))
                                .Select(g => ProfileLibrary.Find(g.Profile)?.Name ?? g.Profile));
                            Row(game.Id, game.Title, $"Versión {game.Version}", missing.Length > 0 ? "Falta: " + missing : game.Pitch,
                                () => playFusion(game.Id), missing.Length == 0);
                        }
                        ImGui.EndTable();
                    }
                    if (games.Count == 0) ImGui.TextWrapped("No hay fusiones con ese nombre. Usá Fusionar juegos para inventar una.");
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Creados"))
                {
                    ImGui.TextDisabled("Juegos nuevos hechos desde cero.");
                    var games = _created.Where(g => Matches(g.Title)).ToList();
                    if (BeginList())
                    {
                        foreach (var game in games)
                            Row(game.Folder, game.Title, game.MachineName, game.Pitch,
                                () => playOriginal(game.ProgramPath), File.Exists(game.ProgramPath));
                        ImGui.EndTable();
                    }
                    if (games.Count == 0) ImGui.TextWrapped("No hay juegos creados con ese nombre. Usá Crear un juego para empezar uno.");
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }

    private bool Matches(string title) => title.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool BeginList()
    {
        if (!ImGui.BeginTable("juegos", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
            new Vector2(0, Math.Max(130, ImGui.GetContentRegionAvail().Y - 30)))) return false;
        ImGui.TableSetupColumn("Juego", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Sistema / versión", ImGuiTableColumnFlags.WidthFixed, 180);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableHeadersRow();
        return true;
    }

    private void Row(string id, string title, string detail, string? hint, Action play, bool playable)
    {
        ImGui.PushID(id);
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(title);
        if (!string.IsNullOrWhiteSpace(hint) && ImGui.IsItemHovered()) ImGui.SetTooltip(hint);
        ImGui.TableNextColumn(); ImGui.TextUnformatted(detail);
        ImGui.TableNextColumn();
        ImGui.BeginDisabled(!playable);
        if (ImGui.Button("Jugar", new Vector2(-1, 0))) { Close(); play(); }
        ImGui.EndDisabled();
        if (!playable && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(hint ?? "Este formato todavía no se puede abrir.");
        ImGui.PopID();
    }
}
