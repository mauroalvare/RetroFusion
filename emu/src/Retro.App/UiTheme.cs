using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Retro.App;

/// <summary>Shared palette and legible system fonts. No font files are distributed.</summary>
internal static class UiTheme
{
    public const float BaseFontSize = 20;
    public static Vector4 Muted => HighContrast ? new(0.86f, 0.89f, 0.93f, 1) : new(0.67f, 0.69f, 0.63f, 1);
    public static Vector4 Accent => HighContrast ? new(0.55f, 0.86f, 1, 1) : new(0.88f, 0.67f, 0.36f, 1);
    public static Vector4 Panel => HighContrast ? new(0,0,0,1) : new(0.09f,0.115f,0.10f,1);
    public static Vector4 Warning => HighContrast ? new(1, 0.82f, 0.36f, 1) : new(0.96f, 0.75f, 0.42f, 1);
    /// <summary>Current instruction, PC and SP in the debugger.</summary>
    public static Vector4 Highlight => HighContrast ? new(1, 1, 0.2f, 1) : new(1, 1, 0.3f, 1);
    public static Vector4 Error => HighContrast ? new(1, 0.6f, 0.55f, 1) : new(1, 0.5f, 0.4f, 1);
    public static Vector4 Success => HighContrast ? new(0.6f, 1, 0.6f, 1) : new(0.55f, 0.85f, 0.55f, 1);
    /// <summary>Text and spacing factor chosen in the preferences (1 = 20 px body text).</summary>
    public static float Scale { get; private set; } = 1;
    public static bool HighContrast { get; private set; }
    /// <summary>Height of the status bar at the bottom of the window.</summary>
    public static float StatusBarHeight => MathF.Round((BaseFontSize + 8) * Scale);
    private static nint _fontContext;
    private static float _fontSize;
    private static ImFontPtr _codeFont;
    private static ImFontPtr _titleFont;
    private static ImFontPtr _labelFont;
    private static ImGuiStyle? _baseStyle;
    // Atlas configurations retain this pointer for future rebuilds. One range table per process.
    private static readonly GCHandle Ranges = GCHandle.Alloc(new ushort[] { 0x20, 0xFF, 0x2000, 0x206F, 0 }, GCHandleType.Pinned);

    public static float Px(float value) => value * Scale;
    public static Vector2 Px(float x, float y) => new(x * Scale, y * Scale);

    /// <summary>Style, palette and font. Call between frames: a new text size rebuilds the font atlas.</summary>
    public static unsafe void Apply(float scale = 1, bool highContrast = false)
    {
        Scale = Math.Clamp(scale, 1, 2); HighContrast = highContrast;
        var style = ImGui.GetStyle();
        // ScaleAllSizes multiplies the current sizes, so every change starts again from the unscaled style.
        if (_baseStyle is { } unscaled) *style.NativePtr = unscaled; else _baseStyle = *style.NativePtr;
        style.WindowRounding = 3; style.ChildRounding = 2; style.FrameRounding = 2;
        style.PopupRounding = 3; style.GrabRounding = 1; style.TabRounding = 1;
        style.WindowPadding = new(14, 14); style.FramePadding = new(9, 6);
        style.ItemSpacing = new(10, 9); style.CellPadding = new(10, 10);
        style.WindowBorderSize = 1; style.FrameBorderSize = 1; style.ScrollbarSize = 14;
        // Tall entries (library sections, creator and code tabs) center their text vertically.
        style.SelectableTextAlign = new(0, 0.5f);
        if (Scale != 1) style.ScaleAllSizes(Scale);
        var colors = style.Colors;
        if (highContrast)
        {
            // White on black, bright borders and a yellow marker for the keyboard focus.
            colors[(int)ImGuiCol.Text] = new(1, 1, 1, 1);
            colors[(int)ImGuiCol.WindowBg] = new(0, 0, 0, 1);
            colors[(int)ImGuiCol.PopupBg] = new(0.03f, 0.03f, 0.04f, 1);
            colors[(int)ImGuiCol.Border] = new(0.82f, 0.86f, 0.92f, 1);
            colors[(int)ImGuiCol.Separator] = new(0.82f, 0.86f, 0.92f, 1);
            colors[(int)ImGuiCol.FrameBg] = new(0.10f, 0.10f, 0.12f, 1);
            colors[(int)ImGuiCol.FrameBgHovered] = new(0.20f, 0.24f, 0.30f, 1);
            colors[(int)ImGuiCol.FrameBgActive] = new(0.26f, 0.32f, 0.40f, 1);
            colors[(int)ImGuiCol.Button] = new(0, 0.27f, 0.55f, 1);
            colors[(int)ImGuiCol.ButtonHovered] = new(0, 0.36f, 0.72f, 1);
            colors[(int)ImGuiCol.ButtonActive] = new(0, 0.44f, 0.84f, 1);
            colors[(int)ImGuiCol.Header] = new(0, 0.27f, 0.55f, 1);
            colors[(int)ImGuiCol.HeaderHovered] = new(0, 0.36f, 0.72f, 1);
            colors[(int)ImGuiCol.HeaderActive] = new(0, 0.44f, 0.84f, 1);
            colors[(int)ImGuiCol.TitleBg] = new(0.05f, 0.05f, 0.06f, 1);
            colors[(int)ImGuiCol.TitleBgActive] = new(0, 0.27f, 0.55f, 1);
            colors[(int)ImGuiCol.MenuBarBg] = new(0.03f, 0.03f, 0.04f, 1);
            colors[(int)ImGuiCol.CheckMark] = new(1, 0.86f, 0.2f, 1);
            colors[(int)ImGuiCol.NavCursor] = new(1, 0.86f, 0.2f, 1);
        }
        else
        {
            colors[(int)ImGuiCol.Text] = new(0.91f, 0.89f, 0.82f, 1);
            colors[(int)ImGuiCol.WindowBg] = new(0.065f, 0.080f, 0.070f, 1);
            colors[(int)ImGuiCol.PopupBg] = new(0.09f, 0.115f, 0.10f, 1);
            colors[(int)ImGuiCol.Border] = new(0.29f, 0.32f, 0.27f, 1);
            colors[(int)ImGuiCol.Separator] = new(0.27f, 0.30f, 0.25f, 1);
            colors[(int)ImGuiCol.FrameBg] = new(0.115f, 0.145f, 0.12f, 1);
            colors[(int)ImGuiCol.FrameBgHovered] = new(0.18f, 0.21f, 0.17f, 1);
            colors[(int)ImGuiCol.FrameBgActive] = new(0.22f, 0.25f, 0.19f, 1);
            colors[(int)ImGuiCol.Button] = new(0.17f, 0.20f, 0.16f, 1);
            colors[(int)ImGuiCol.ButtonHovered] = new(0.26f, 0.29f, 0.22f, 1);
            colors[(int)ImGuiCol.ButtonActive] = new(0.34f, 0.31f, 0.22f, 1);
            colors[(int)ImGuiCol.Header] = new(0.30f, 0.25f, 0.16f, 1);
            colors[(int)ImGuiCol.HeaderHovered] = new(0.37f, 0.31f, 0.20f, 1);
            colors[(int)ImGuiCol.HeaderActive] = new(0.43f, 0.35f, 0.22f, 1);
            colors[(int)ImGuiCol.TitleBg] = new(0.10f, 0.12f, 0.10f, 1);
            colors[(int)ImGuiCol.TitleBgActive] = new(0.22f, 0.23f, 0.18f, 1);
            colors[(int)ImGuiCol.MenuBarBg] = new(0.085f, 0.10f, 0.085f, 1);
            colors[(int)ImGuiCol.CheckMark] = new(0.63f,0.80f,0.52f,1);
            colors[(int)ImGuiCol.NavCursor] = Accent;
            colors[(int)ImGuiCol.ScrollbarBg]=new(0.06f,0.075f,0.065f,1);
            colors[(int)ImGuiCol.ScrollbarGrab]=new(0.34f,0.38f,0.31f,1);
        }
        foreach(string name in new[]{"Tab","TabDimmed","TabUnfocused"})
            if (Enum.TryParse<ImGuiCol>(name,out var key)) colors[(int)key]=highContrast?new(0.03f,0.03f,0.04f,1):new(0.13f,0.16f,0.13f,1);
        foreach(string name in new[]{"TabSelected","TabActive","TabHovered","TabDimmedSelected","TabUnfocusedActive"})
            if (Enum.TryParse<ImGuiCol>(name,out var key)) colors[(int)key]=highContrast?new(0,0.27f,0.55f,1):new(0.33f,0.28f,0.18f,1);
        colors[(int)ImGuiCol.ResizeGrip]=new(Accent.X,Accent.Y,Accent.Z,0.25f);
        colors[(int)ImGuiCol.ResizeGripHovered]=new(Accent.X,Accent.Y,Accent.Z,0.7f);
        colors[(int)ImGuiCol.ResizeGripActive]=Accent;
        colors[(int)ImGuiCol.SliderGrab]=Accent;
        colors[(int)ImGuiCol.SliderGrabActive]=Accent;

        colors[(int)ImGuiCol.TextDisabled] = Muted;
        colors[(int)ImGuiCol.ModalWindowDimBg] = new(0, 0, 0, highContrast ? 0.8f : 0.65f);
        colors[(int)ImGuiCol.ChildBg] = new(0, 0, 0, 0);
        ImGui.GetIO().FontGlobalScale = 1;
        float size = MathF.Round(BaseFontSize * Scale);
        if (!Raylib.IsWindowReady() || (_fontContext == ImGui.GetCurrentContext() && _fontSize == size)) return;
        bool replace = _fontContext == ImGui.GetCurrentContext();
        _fontContext = ImGui.GetCurrentContext(); _fontSize = size;
        string? font = FontCandidates(false).FirstOrDefault(File.Exists);
        string? codeFont = FontCandidates(true).FirstOrDefault(File.Exists);

        var config = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
        try
        {
            config.OversampleH = 2; config.OversampleV = 2;
            config.SizePixels = size;
            // Another text size replaces the atlas instead of adding one more copy of the font.
            if (replace) ImGui.GetIO().Fonts.Clear();
            var fontPtr = font == null ? ImGui.GetIO().Fonts.AddFontDefault(config)
                : ImGui.GetIO().Fonts.AddFontFromFileTTF(font, size, config, Ranges.AddrOfPinnedObject());
            ImGui.GetIO().NativePtr->FontDefault = fontPtr.NativePtr;
            _codeFont = codeFont == null ? fontPtr
                : ImGui.GetIO().Fonts.AddFontFromFileTTF(codeFont, MathF.Round(16 * Scale), config, Ranges.AddrOfPinnedObject());
            string? titleFont = DisplayCandidates().FirstOrDefault(File.Exists);
            _titleFont = titleFont!=null ? ImGui.GetIO().Fonts.AddFontFromFileTTF(titleFont, MathF.Round(32 * Scale), config, Ranges.AddrOfPinnedObject()) : fontPtr;
            string? labelFont=LabelCandidates().FirstOrDefault(File.Exists);
            _labelFont=labelFont!=null?ImGui.GetIO().Fonts.AddFontFromFileTTF(labelFont,MathF.Round(16*Scale),config,Ranges.AddrOfPinnedObject()):fontPtr;
            rlImGui.ReloadFonts();
        }
        finally { ImGuiNative.ImFontConfig_destroy(config.NativePtr); }
    }

    private static IEnumerable<string> FontCandidates(bool code)
    {
        if (OperatingSystem.IsWindows())
        {
            string folder=Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if(!code) { yield return Path.Combine(folder,"GIL_____.TTF"); yield return Path.Combine(folder,"trebuc.ttf"); }
            yield return Path.Combine(folder,code?"consola.ttf":"segoeui.ttf");
        }
        if (OperatingSystem.IsMacOS())
        { if(!code) yield return "/System/Library/Fonts/Supplemental/GillSans.ttc"; yield return code ? "/System/Library/Fonts/Menlo.ttc" : "/System/Library/Fonts/Supplemental/Trebuchet MS.ttf"; }
        yield return "/usr/share/fonts/truetype/dejavu/" + (code ? "DejaVuSansMono.ttf" : "DejaVuSans.ttf");
        yield return "/usr/share/fonts/truetype/liberation2/" + (code ? "LiberationMono-Regular.ttf" : "LiberationSans-Regular.ttf");
    }

    private static IEnumerable<string> DisplayCandidates()
    {
        if(OperatingSystem.IsWindows())
        {
            string folder=Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            yield return Path.Combine(folder,"AGENCYB.TTF"); yield return Path.Combine(folder,"bahnschrift.ttf");
        }
        yield return "/System/Library/Fonts/Supplemental/DIN Condensed Bold.ttf";
        yield return "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf";
    }

    private static IEnumerable<string> LabelCandidates()
    {
        if(OperatingSystem.IsWindows())
        {
            string folder=Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            yield return Path.Combine(folder,"OCRAEXT.TTF"); yield return Path.Combine(folder,"bahnschrift.ttf");
        }
    }

    public readonly struct FontScope : IDisposable
    {
        public void Dispose() => ImGui.PopFont();
    }

    /// <summary>Fixed-width text only where columns and addresses carry meaning.</summary>
    public static FontScope FixedWidth() { ImGui.PushFont(_codeFont); return new(); }
    public static FontScope Display() { ImGui.PushFont(_titleFont); return new(); }
    public static FontScope Label() { ImGui.PushFont(_labelFont); return new(); }

    public static void Heading(string title, string? description = null)
    {
        ImGui.PushFont(_titleFont); ImGui.PushTextWrapPos(0); ImGui.TextUnformatted(title); ImGui.PopTextWrapPos(); ImGui.PopFont();
        if (description != null) Hint(description);
        ImGui.Spacing();
    }

    public static void Hint(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Muted); ImGui.TextWrapped(text); ImGui.PopStyleColor();
    }

    public static void Section(string title, string? description = null)
    {
        ImGui.Spacing();
        using (Label()) { ImGui.PushStyleColor(ImGuiCol.Text,Accent); ImGui.SeparatorText(title); ImGui.PopStyleColor(); }
        if (description != null) Hint(description);
    }

    public static bool PrimaryButton(string label, Vector2 size = default)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, HighContrast ? new Vector4(0, 0.32f, 0.68f, 1) : new Vector4(0.83f, 0.62f, 0.32f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, HighContrast ? new Vector4(0, 0.44f, 0.84f, 1) : new Vector4(0.94f, 0.76f, 0.47f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, HighContrast ? new Vector4(0, 0.44f, 0.84f, 1) : new Vector4(0.72f,0.54f,0.29f,1));
        ImGui.PushStyleColor(ImGuiCol.Text,HighContrast?new Vector4(1,1,1,1):new Vector4(0.065f,0.08f,0.07f,1));
        bool pressed = ImGui.Button(label, size); ImGui.PopStyleColor(4); return pressed;
    }

    public static void EmptyState(string title, string detail)
    {
        ImGui.Spacing(); ImGui.TextUnformatted(title); Hint(detail); ImGui.Spacing();
    }

    public static void Eyebrow(string text) { using(Label()) ImGui.TextColored(Accent, text); ImGui.Spacing(); }

    public static void BeginCard(string id, Vector2 size = default)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Panel);
        ImGui.BeginChild(id, size, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);
        ImGui.PopStyleColor();
        if (!HighContrast) RetroArt.Screw(ImGui.GetWindowDrawList(),ImGui.GetWindowPos()+new Vector2(ImGui.GetWindowWidth()-UiTheme.Px(7),UiTheme.Px(7)));
    }
    public static void EndCard() => ImGui.EndChild();

    /// <summary>Keeps the next button on this row only if it fits, so rows of buttons wrap at large text sizes.</summary>
    public static void SameLineIfFits(string buttonLabel) =>
        SameLineIfFits(ImGui.CalcTextSize(buttonLabel, true).X + ImGui.GetStyle().FramePadding.X * 2);

    public static void SameLineIfFits(float width)
    {
        // After an item the cursor waits at the start of the next row, so this is the row's right edge.
        float right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= right) ImGui.SameLine();
    }

    public static void Tooltip(string text)
    {
        if (!ImGui.BeginTooltip()) return;
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos(); ImGui.EndTooltip();
    }

    public static string Fit(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width) return text;
        const string suffix = "...";
        float remaining = width - ImGui.CalcTextSize(suffix).X;
        if (remaining <= 0) return "";
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int middle = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text[..middle]).X <= remaining) lo = middle; else hi = middle - 1;
        }
        if (lo > 0 && char.IsHighSurrogate(text[lo - 1])) lo--;
        return text[..lo].TrimEnd() + suffix;
    }
}
