using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private bool _newsOpen, _newsAutomatic, _newsHistory, _newsPausing, _pausedBeforeNews;
    private string? _newsFromVersion;
    private string _newsVersion = "";
    private ReleaseNews NewsDelivery => new(_prefs);
    private static string NewsText(string es, string en) => UiText.English ? en : es;

    private void QueueNews(string version, string? previousVersion, bool force)
    {
        if (!NewsDelivery.Queue(version, previousVersion, force)) return;
        _newsVersion = _prefs.PendingNewsVersion!;
        _newsFromVersion = _prefs.PendingNewsFromVersion;
        _newsHistory = false; _newsAutomatic = _newsOpen = true;
    }

    private void OpenNews()
    {
        if (_newsOpen) return;
        _newsVersion = typeof(ReleaseNotes).Assembly.GetName().Version!.ToString(3);
        _newsFromVersion = null; _newsHistory = true; _newsAutomatic = false; _newsOpen = true;
    }

    private void PauseForNews()
    {
        if (!_newsOpen || _interfacePicker.IsOpen) return;
        if (!_newsPausing) { _pausedBeforeNews = _host.Paused; _newsPausing = true; }
        _host.Paused = true;
    }

    private void CloseNews()
    {
        if (_newsAutomatic) NewsDelivery.Acknowledge(_newsVersion);
        _newsOpen = _newsAutomatic = false;
        if (_newsPausing) { _host.Paused = _pausedBeforeNews; _newsPausing = false; }
    }

    private void NewsBounds(string key) => InterfaceControlBounds?.Invoke(key, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

    private void DrawNews()
    {
        if (!_newsOpen || _interfacePicker.IsOpen) return;
        PauseForNews();
        bool fullHeight = UiTheme.CompactHeight;
        var room = fullHeight
            ? System.Numerics.Vector2.Max(new(100, 80), ImGui.GetMainViewport().Size - new System.Numerics.Vector2(16))
            : WorkspaceLayout.Available();
        ImGui.SetNextWindowSize(System.Numerics.Vector2.Min(UiTheme.Px(760, 600), room), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(System.Numerics.Vector2.Min(UiTheme.Px(360, 260), room), room);
        bool open = true;
        if (ImGui.Begin(UiText.Label("Novedades###release-notes"), ref open, ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            WorkspaceLayout.KeepOnScreen(modal: fullHeight, coverStatusBar: fullHeight);
            // Reserve exactly the rows needed by the footer; only the news body scrolls.
            string language = UiText.English ? "Español" : "English";
            string history = NewsText(_newsHistory ? "Lo nuevo" : "Historial", _newsHistory ? "New only" : "History");
            string close = NewsText("Listo", "Done");
            float available = ImGui.GetContentRegionAvail().X, gap = ImGui.GetStyle().ItemSpacing.X;
            float ButtonWidth(string text) => ImGui.CalcTextSize(text).X + ImGui.GetStyle().FramePadding.X * 2;
            string[] controls = _newsAutomatic ? [language, history, close] : [language, close];
            int rows = 1; float used = 0;
            foreach (string control in controls)
            {
                float width = ButtonWidth(control);
                if (used > 0 && used + gap + width > available) { rows++; used = 0; }
                used += (used > 0 ? gap : 0) + width;
            }
            float footer = ImGui.GetFrameHeightWithSpacing() * rows + ImGui.GetStyle().ItemSpacing.Y + 1;
            ImGui.BeginChild("release-notes-body", new(0, -footer));
            InterfaceControlBounds?.Invoke("Cuerpo novedades", ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
            ImGui.PushTextWrapPos(0);
            ImGui.TextWrapped(NewsText("Novedades de RetroEmu", "What's new in RetroEmu"));
            string? from = ReleaseNotes.ValidVersion(_newsFromVersion);
            ImGui.TextWrapped(_newsAutomatic
                ? from == null
                    ? NewsText("¡Bienvenido! Estas son las funciones y mejoras que podés descubrir.", "Welcome! Here are the features and improvements you can explore.")
                    : NewsText($"Te ponemos al día: de la {from} a la {_newsVersion}.", $"Let's catch you up: from {from} to {_newsVersion}.")
                : NewsText("Volvé a descubrir lo que se agregó en cada versión.", "Rediscover what was added in each version."));
            foreach (var release in ReleaseNotes.Since(_newsHistory ? null : _newsFromVersion, _newsVersion))
            {
                ImGui.Spacing(); UiTheme.Section(release.Version);
                foreach (var item in release.Items)
                {
                    ImGui.TextWrapped(item.KindIn(_prefs.Language) + " · " + item.Title.In(_prefs.Language));
                    ImGui.TextWrapped(item.Description.In(_prefs.Language));
                    ImGui.Spacing();
                }
                NewsBounds("Novedades " + release.Version);
            }
            ImGui.PopTextWrapPos(); ImGui.EndChild(); ImGui.Separator();
            used = 0;
            void PlaceButton(string text)
            {
                float width = ButtonWidth(text);
                if (used > 0 && used + gap + width <= available) ImGui.SameLine();
                else used = 0;
                used += (used > 0 ? gap : 0) + width;
            }
            PlaceButton(language);
            if (ImGui.Button(language + "##news-language")) SetLanguage(UiText.English ? "es" : "en");
            NewsBounds("Idioma novedades");
            if (_newsAutomatic)
            {
                PlaceButton(history);
                if (ImGui.Button(history + "##news-history")) _newsHistory = !_newsHistory;
                NewsBounds("Historial novedades");
            }
            PlaceButton(close);
            if (ImGui.Button(close + "##news-close")) open = false;
            NewsBounds("Cerrar novedades");
        }
        ImGui.End();
        if (!open) CloseNews();
    }
}
