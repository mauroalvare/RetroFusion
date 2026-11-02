using System.Globalization;
using System.Numerics;

namespace Retro.App;

public sealed record TerminalColors(string Background, string Text, string Accent, string Muted, string Selection,
    string Success, string Warning, string Error)
{
    internal IReadOnlyList<string> Values => [Background, Text, Accent, Muted, Selection, Success, Warning, Error];
}

public sealed record InterfacePalette(string Palette, TerminalColors Colors);

internal sealed record TerminalPalettePreset(string Id, string Label, TerminalColors Colors)
{
    public string Name => UiText.Get(Label);
}

/// <summary>RGB colors only. Color data never becomes a path, escape sequence or operating-system command.</summary>
internal static class TerminalPalettes
{
    public static readonly TerminalColors Green = new("#020502", "#91F09C", "#73FF87", "#6EAE75", "#1F662B", "#8CD98C", "#F5BF6B", "#FF8066");
    public static readonly TerminalColors Amber = new("#060402", "#FFC966", "#FFBA33", "#C79957", "#57390F", "#ACD980", "#FFE08A", "#FF8D70");
    public static readonly TerminalColors HighContrast = new("#000000", "#FFFFFF", "#FFFF33", "#DDE3ED", "#004C99", "#99FF99", "#FFD15C", "#FF998C");
    public static readonly TerminalColors Retro = new("#111412", "#E8E3D1", "#E0AB5C", "#ABB0A1", "#4D4029", "#8CD98C", "#F5BF6B", "#FF8066");
    public static readonly IReadOnlyList<TerminalPalettePreset> All = Array.AsReadOnly<TerminalPalettePreset>([
        new("auto", "Seguir el estilo de interfaz", Green), new("green", "Fósforo verde", Green), new("amber", "Fósforo ámbar", Amber),
        new("ice", "Hielo", new("#021019", "#A8E2FF", "#55C7FF", "#6EA3BE", "#103D5C", "#9CE8B3", "#FFD080", "#FF9292")),
        new("paper", "Papel", new("#F3F0E6", "#20262C", "#784B14", "#596169", "#D5CCAF", "#226A36", "#815000", "#A52121")),
        new("custom", "Personalizada", Green)
    ]);
    public static readonly IReadOnlyList<(string Id, string Label)> Fields = Array.AsReadOnly<(string, string)>([
        ("background", "Fondo"), ("text", "Texto"), ("accent", "Acento y cursor"), ("muted", "Texto secundario"),
        ("selection", "Selección"), ("success", "Resultados correctos"), ("warning", "Avisos"), ("error", "Errores")
    ]);

    public static bool Known(string id) => All.Any(preset => preset.Id == id);
    public static bool Valid(TerminalColors? colors) => colors != null && colors.Values.All(value => value is { Length: 7 } && value[0] == '#' && value[1..].All(char.IsAsciiHexDigit));
    public static TerminalColors DefaultFor(InterfaceStyle style) => style == InterfaceStyle.Retro ? Retro : style == InterfaceStyle.Amber ? Amber : Green;
    public static TerminalColors Resolve(string id, TerminalColors? custom, InterfaceStyle style) => id == "custom" && Valid(custom) ? custom!
        : id is "auto" or "custom" || !Known(id) ? DefaultFor(style) : All.First(preset => preset.Id == id).Colors;
    public static string CanonicalField(string id) => CommandLine.Fold(id) switch
    {
        "fondo" => "background", "texto" => "text", "acento" or "cursor" => "accent", "secundario" => "muted",
        "seleccion" => "selection", "correcto" => "success", "aviso" => "warning", "errores" => "error", _ => id.ToLowerInvariant()
    };

    public static string Normalize(string value)
    {
        string trimmed = value.Trim();
        string normalized = "#" + (trimmed.StartsWith('#') ? trimmed[1..] : trimmed).ToUpperInvariant();
        if (normalized.Length != 7 || !normalized[1..].All(char.IsAsciiHexDigit)) throw new ArgumentException(UiText.Get("Usá un color RGB de seis cifras hexadecimales, por ejemplo #FFC966."));
        return normalized;
    }
    public static TerminalColors FromValues(IReadOnlyList<string> values)
    {
        if (values.Count != 8) throw new ArgumentException("Eight terminal colors are required.");
        var normalized = values.Select(Normalize).ToArray();
        return new(normalized[0], normalized[1], normalized[2], normalized[3], normalized[4], normalized[5], normalized[6], normalized[7]);
    }
    public static TerminalColors Change(TerminalColors colors, string field, string value)
    {
        int index = Fields.Select(item => item.Id).ToList().IndexOf(CanonicalField(field));
        if (index < 0) throw new ArgumentException(UiText.Get("No existe ese color de la terminal."));
        var changed = colors.Values.ToArray(); changed[index] = Normalize(value); return FromValues(changed);
    }
    public static Vector4 Color(string hex) => new(byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber) / 255f,
        byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber) / 255f, byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber) / 255f, 1);
    public static string Hex(Vector3 rgb) => "#" + string.Concat(new[] { rgb.X, rgb.Y, rgb.Z }.Select(value => ((byte)MathF.Round(Math.Clamp(value, 0, 1) * 255)).ToString("X2", CultureInfo.InvariantCulture)));
}
