namespace Retro.App;

public enum InterfaceStyle { Retro, Terminal, Hardcore }

public sealed record InterfaceDescription(InterfaceStyle Style, string Id, string Name, string Description);

public static class InterfaceStyles
{
    public static readonly IReadOnlyList<InterfaceDescription> All = Array.AsReadOnly<InterfaceDescription>([
        new(InterfaceStyle.Retro, "retro", "Retro · taller de máquinas", "El diseño principal: instrumentos, tonos cálidos y navegación con menús, botones, teclado o mando."),
        new(InterfaceStyle.Terminal, "terminal", "Terminal verde · interfaz gráfica", "Negro, fósforo verde y letras de ancho fijo. Conserva todos los menús y botones; no necesitás escribir comandos."),
        new(InterfaceStyle.Hardcore, "hardcore", "Terminal hardcore · por comandos", "Una consola dentro de RetroEmu. Buscá juegos, jugá y ajustá el equipo escribiendo órdenes. Incluye ayuda, historial y autocompletado. Volvés con interfaz retro.")
    ]);

    public static bool TryParse(string? id, out InterfaceStyle style)
    {
        var match = All.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        style = match?.Style ?? InterfaceStyle.Retro;
        return match != null;
    }
    public static InterfaceDescription Describe(InterfaceStyle style) => All.First(item => item.Style == style);
}
