using System.Numerics;
using ImGuiNET;

namespace Retro.App;

/// <summary>
/// One comparable scene for every style, drawn with the palette, fonts, hardware art and text-table layout of the real
/// interface. Nothing here reads or changes the running program, so looking at a preview has no side effects.
/// </summary>
internal static class InterfaceScene
{
    private const float Width = 640, Height = 400;
    private static readonly (string Title, string System, string Machine, string Year, string Genre)[] Demo =
    [
        ("Faro del taller", "Nintendo NES", "nes", "1986", "Plataformas"),
        ("Ruedas de cobre", "Commodore 64", "c64", "1991", "Carreras"),
        ("Cuadrícula nocturna", "ZX Spectrum 48K", "zx48", "1984", "Puzles"),
        ("La mina de Ohm", "Commodore 64", "c64", "1988", "Aventura"),
    ];

    public static void Draw(ImDrawListPtr draw, InterfaceStyle style, Vector2 at, Vector2 size)
    {
        var palette = UiTheme.PaletteOf(style);
        float unit = Math.Min(size.X / Width, size.Y / Height);
        var scene = new Vector2(Width, Height) * unit;
        at += (size - scene) / 2;
        draw.PushClipRect(at, at + scene, true);
        draw.AddRectFilled(at, at + scene, Color(palette.Window));
        if (style == InterfaceStyle.Hardcore) Console(draw, palette, at, unit);
        else Library(draw, palette, at, unit, style);
        draw.PopClipRect();
        draw.AddRect(at, at + scene, Color(palette.Border));
    }

    private static uint Color(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    private static void Library(ImDrawListPtr draw, UiTheme.StylePalette palette, Vector2 at, float unit, InterfaceStyle style)
    {
        var body = UiTheme.PreviewFont(style); var label = UiTheme.PreviewLabelFont(style); var title = UiTheme.PreviewTitleFont(style);
        Vector2 P(float x, float y) => at + new Vector2(x, y) * unit;
        void Text(ImFontPtr font, float size, float x, float y, Vector4 ink, string value) => draw.AddText(font, size * unit, P(x, y), Color(ink), value);
        void Button(float x, float y, float w, float h, string value, bool primary)
        {
            draw.AddRectFilled(P(x, y), P(x + w, y + h), Color(primary ? palette.Accent : palette.Button), palette.Rounded ? 2 * unit : 0);
            if (!primary) draw.AddRect(P(x, y), P(x + w, y + h), Color(palette.Border), palette.Rounded ? 2 * unit : 0);
            var fit = body.CalcTextSizeA(11 * unit, float.MaxValue, 0, value);
            draw.AddText(body, 11 * unit, P(x, y) + (new Vector2(w, h) * unit - fit) / 2, Color(primary ? palette.OnAccent : palette.Text), value);
        }
        draw.AddRectFilled(P(0, 0), P(Width, 26), Color(palette.Panel));
        Text(title, 15, 10, 5, palette.Text, "RETROEMU");
        string[] modes = ["BIBLIOTECA", "JUGAR", "CREAR", "ANALIZAR", "AYUDA"];
        float tab = 100;
        for (int i = 0; i < modes.Length; i++)
        {
            float w = 8 + modes[i].Length * 5.6f;
            draw.AddRect(P(tab, 5), P(tab + w, 21), Color(palette.Border));
            if (i == 0) draw.AddRectFilled(P(tab + 3, 19), P(tab + w - 3, 20.5f), Color(palette.Accent));
            Text(label, 9, tab + 4, 8, i == 0 ? palette.Text : palette.Muted, modes[i]);
            tab += w + 6;
        }
        draw.AddLine(P(0, 26), P(Width, 26), Color(palette.Border));
        Text(label, 9, 12, 38, palette.Accent, "CATÁLOGO");
        string[] sections = ["Inicio", "Todos los juegos", "Favoritos", "Recientes", "ZX Spectrum 48K", "Commodore 64", "Nintendo NES"];
        for (int i = 0; i < sections.Length; i++)
        {
            float y = 54 + i * 20;
            if (i == 1) draw.AddRectFilled(P(8, y - 2), P(116, y + 16), Color(palette.Header));
            Text(body, 11, 12, y, palette.Text, sections[i]);
        }
        Text(title, 16, 130, 36, palette.Text, "Todos los juegos");
        draw.AddRectFilled(P(130, 60), P(404, 80), Color(palette.Frame));
        draw.AddRect(P(130, 60), P(404, 80), Color(palette.Border));
        Text(body, 10, 136, 64, palette.Muted, "Buscar nombre o género");
        Button(410, 60, 48, 20, "Filtros", false);
        for (int i = 0; i < Demo.Length; i++)
        {
            var game = Demo[i]; float y = 88 + i * 42;
            if (i == 0) draw.AddRectFilled(P(130, y), P(462, y + 38), Color(palette.Header));
            HardwareArt.Draw(draw, game.Machine, P(132, y + 3), new Vector2(46, 32) * unit);
            Text(body, 11, 184, y + 5, palette.Text, game.Title);
            Text(body, 10, 184, y + 20, palette.Muted, game.Genre + " · " + game.Year);
            Button(372, y + 10, 42, 18, "Jugar", true);
            Button(418, y + 10, 40, 18, "Ficha", false);
        }
        draw.AddRectFilled(P(470, 36), P(630, 390), Color(palette.Panel));
        draw.AddRect(P(470, 36), P(630, 390), Color(palette.Border));
        draw.AddRectFilled(P(480, 46), P(620, 130), Color(palette.Frame));
        HardwareArt.Draw(draw, "nes", P(490, 50), new Vector2(120, 76) * unit);
        Text(title, 14, 480, 138, palette.Text, Demo[0].Title);
        Text(body, 10, 480, 160, palette.Muted, Demo[0].System);
        Button(480, 180, 140, 20, "Jugar", true);
        Button(480, 206, 140, 20, "Ficha", false);
        Text(label, 8, 480, 240, palette.Muted, "AÑO");
        Text(body, 11, 480, 252, palette.Text, Demo[0].Year);
        Text(label, 8, 480, 274, palette.Muted, "GÉNERO");
        Text(body, 11, 480, 286, palette.Text, Demo[0].Genre);
    }

    private static void Console(ImDrawListPtr draw, UiTheme.StylePalette palette, Vector2 at, float unit)
    {
        var font = UiTheme.ConsoleFont; float size = 14 * unit, row = MathF.Round(size * 1.2f);
        int columns = (int)((Width * unit - 16 * unit) / Math.Max(1, font.CalcTextSizeA(size, float.MaxValue, 0, "M").X));
        var position = at + new Vector2(8, 6) * unit;
        void Line(string text, Vector4 ink) { draw.AddText(font, size, position, Color(ink), text); position.Y += row; }
        Line("retro:/biblioteca> juegos", palette.Bright);
        // The real table layout: a change to how tables are drawn reaches this preview too.
        var lines = TextTable.Layout([new("#", Right: true), new("Título", Flexible: true), new("Sistema"), new("Año"), new("Género", Optional: true)],
            Demo.Select((game, index) => (IReadOnlyList<string>)[(index + 1).ToString(), game.Title, game.Machine, game.Year, game.Genre]).ToList(), columns);
        foreach (var (text, kind) in lines) Line(text, kind == TerminalKind.Heading ? palette.Accent : kind == TerminalKind.Rule ? palette.Muted : palette.Text);
        Line("4 juegos · jugar <n> · ficha <n> · filtrar quitar", palette.Muted);
        Line("retro:/biblioteca> como guardar", palette.Bright);
        Line("guardar <ranura> [reemplazar]   Guarda la partida", palette.Text);
        Line("retro:/biblioteca> jugar 1", palette.Bright);
        Line("Abriendo: Faro del taller", palette.Accent);
        draw.AddText(font, size, position, Color(palette.Accent), "retro:/biblioteca> ");
        var end = position + new Vector2(font.CalcTextSizeA(size, float.MaxValue, 0, "retro:/biblioteca> ").X, 0);
        draw.AddRectFilled(end, end + new Vector2(size * 0.55f, row), Color(palette.Bright));
    }
}
