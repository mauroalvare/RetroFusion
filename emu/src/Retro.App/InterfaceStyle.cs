namespace Retro.App;

public enum InterfaceStyle { Retro, Terminal, Hardcore }

public sealed record InterfaceDescription(InterfaceStyle Style, string Id, string Name, string Description)
{
    public string ShortName => Name.Split('·')[0].Trim();
    /// <summary>How it is operated, in a few words.</summary>
    public string Handling { get; init; } = "";
    /// <summary>What sets it apart in practice.</summary>
    public IReadOnlyList<string> Features { get; init; } = [];
}

public static class InterfaceStyles
{
    public static readonly IReadOnlyList<InterfaceDescription> All = Array.AsReadOnly<InterfaceDescription>([
        new(InterfaceStyle.Retro, "retro", "Retro · taller de máquinas", "El diseño principal: instrumentos, tonos cálidos y navegación con menús, botones, teclado o mando.")
        {
            Handling = "Menús, botones y mando",
            Features = ["Biblioteca con ilustraciones de cada máquina y panel de detalle.", "Tipografías de instrumental técnico."]
        },
        new(InterfaceStyle.Terminal, "terminal", "Terminal verde · interfaz gráfica", "Negro, fósforo verde y letras de ancho fijo. Conserva todos los menús y botones; no necesitás escribir comandos.")
        {
            Handling = "Menús y botones, sin comandos",
            Features = ["Las mismas pantallas y acciones que Retro.", "Texto de ancho fijo y contraste fuerte entre verde y negro."]
        },
        new(InterfaceStyle.Hardcore, "hardcore", "Terminal hardcore · por comandos", "Una consola dentro de RetroEmu. Buscá juegos, jugá y ajustá el equipo escribiendo órdenes. Incluye ayuda, historial y autocompletado. Volvés con interfaz retro.")
        {
            Handling = "Órdenes escritas",
            Features = ["Autocompletado, historial y búsqueda de órdenes anteriores.", "Tareas largas en segundo plano, sin perder el prompt.", "Los editores visuales se siguen abriendo con hacer."]
        }
    ]);

    public static bool TryParse(string? id, out InterfaceStyle style)
    {
        var match = All.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        style = match?.Style ?? InterfaceStyle.Retro;
        return match != null;
    }
    public static InterfaceDescription Describe(InterfaceStyle style) => All.First(item => item.Style == style);
}
