using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Retro.App;

/// <summary>Shared palette and legible system fonts. No font files are distributed.</summary>
internal static class UiTheme
{
    public const float BaseFontSize = 16;
    public static Vector4 Muted => HighContrast ? new(0.86f, 0.89f, 0.93f, 1) : new(0.64f, 0.69f, 0.76f, 1);
    public static Vector4 Accent => HighContrast ? new(0.55f, 0.86f, 1, 1) : new(0.48f, 0.78f, 1, 1);
    public static Vector4 Warning => HighContrast ? new(1, 0.82f, 0.36f, 1) : new(0.96f, 0.75f, 0.42f, 1);
    /// <summary>Current instruction, PC and SP in the debugger.</summary>
    public static Vector4 Highlight => HighContrast ? new(1, 1, 0.2f, 1) : new(1, 1, 0.3f, 1);
    public static Vector4 Error => HighContrast ? new(1, 0.6f, 0.55f, 1) : new(1, 0.5f, 0.4f, 1);
    public static Vector4 Success => HighContrast ? new(0.6f, 1, 0.6f, 1) : new(0.55f, 0.85f, 0.55f, 1);
    /// <summary>Text and spacing factor chosen in the preferences (1 = 16 px text).</summary>
    public static float Scale { get; private set; } = 1;
    public static bool HighContrast { get; private set; }
    /// <summary>Height of the status bar at the bottom of the window.</summary>
    public static float StatusBarHeight => MathF.Round(24 * Scale);
    private static nint _fontContext;
    private static float _fontSize;
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
        style.WindowRounding = 7; style.ChildRounding = 5; style.FrameRounding = 4;
        style.PopupRounding = 6; style.GrabRounding = 4; style.TabRounding = 4;
        style.WindowPadding = new(14, 14); style.FramePadding = new(9, 6);
        style.ItemSpacing = new(10, 8); style.CellPadding = new(9, 8);
        style.WindowBorderSize = 1; style.FrameBorderSize = highContrast ? 1 : 0; style.ScrollbarSize = 14;
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
            colors[(int)ImGuiCol.Text] = new(0.91f, 0.93f, 0.96f, 1);
            colors[(int)ImGuiCol.WindowBg] = new(0.055f, 0.070f, 0.090f, 1);
            colors[(int)ImGuiCol.PopupBg] = new(0.075f, 0.095f, 0.125f, 1);
            colors[(int)ImGuiCol.Border] = new(0.22f, 0.27f, 0.34f, 0.75f);
            colors[(int)ImGuiCol.Separator] = new(0.22f, 0.27f, 0.34f, 0.75f);
            colors[(int)ImGuiCol.FrameBg] = new(0.105f, 0.14f, 0.19f, 1);
            colors[(int)ImGuiCol.FrameBgHovered] = new(0.16f, 0.23f, 0.31f, 1);
            colors[(int)ImGuiCol.FrameBgActive] = new(0.19f, 0.29f, 0.40f, 1);
            colors[(int)ImGuiCol.Button] = new(0.17f, 0.29f, 0.41f, 1);
            colors[(int)ImGuiCol.ButtonHovered] = new(0.23f, 0.41f, 0.57f, 1);
            colors[(int)ImGuiCol.ButtonActive] = new(0.27f, 0.48f, 0.65f, 1);
            colors[(int)ImGuiCol.Header] = new(0.15f, 0.28f, 0.40f, 1);
            colors[(int)ImGuiCol.HeaderHovered] = new(0.20f, 0.35f, 0.48f, 1);
            colors[(int)ImGuiCol.HeaderActive] = new(0.23f, 0.40f, 0.55f, 1);
            colors[(int)ImGuiCol.TitleBg] = new(0.08f, 0.11f, 0.15f, 1);
            colors[(int)ImGuiCol.TitleBgActive] = new(0.12f, 0.21f, 0.30f, 1);
            colors[(int)ImGuiCol.MenuBarBg] = new(0.075f, 0.095f, 0.125f, 1);
            colors[(int)ImGuiCol.CheckMark] = Accent;
            colors[(int)ImGuiCol.NavCursor] = Accent;
        }
        colors[(int)ImGuiCol.TextDisabled] = Muted;
        colors[(int)ImGuiCol.ChildBg] = new(0, 0, 0, 0);
        ImGui.GetIO().FontGlobalScale = 1;
        float size = MathF.Round(BaseFontSize * Scale);
        if (!Raylib.IsWindowReady() || (_fontContext == ImGui.GetCurrentContext() && _fontSize == size)) return;
        bool replace = _fontContext == ImGui.GetCurrentContext();
        _fontContext = ImGui.GetCurrentContext(); _fontSize = size;
        string? font = FontCandidates().FirstOrDefault(File.Exists);

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
            rlImGui.ReloadFonts();
        }
        finally { ImGuiNative.ImFontConfig_destroy(config.NativePtr); }
    }

    private static IEnumerable<string> FontCandidates()
    {
        if (OperatingSystem.IsWindows()) yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf");
        if (OperatingSystem.IsMacOS()) yield return "/System/Library/Fonts/Menlo.ttc";
        yield return "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf";
        yield return "/usr/share/fonts/truetype/liberation2/LiberationMono-Regular.ttf";
    }

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
