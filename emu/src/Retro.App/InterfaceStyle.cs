namespace Retro.App;

public enum InterfaceStyle { Retro, Terminal, Hardcore, Amber }

public sealed record InterfaceDescription(InterfaceStyle Style, string Id, string Name, string Description)
{
    private string _name = Name, _description = Description, _handling = "";
    public string Name { get => UiText.Get(_name); init => _name = value; }
    public string Description { get => UiText.Get(_description); init => _description = value; }
    public string ShortName => Name.Split('·')[0].Trim();
    /// <summary>How it is operated, in a few words.</summary>
    public string Handling { get => UiText.Get(_handling); init => _handling = value; }
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
        new(InterfaceStyle.Terminal, "explorer", "Explorador · árbol y listado", "Navegación lateral, árbol de sistemas y listado de archivos. Elegís una entrada y sus acciones sin escribir comandos.")
        {
            Handling = "Árbol, listas y teclado",
            Features = ["Secciones y herramientas en una barra lateral.", "Biblioteca compacta con árbol de sistemas y acciones sobre la selección.", "Colores propios, independientes de los otros diseños."]
        },
        new(InterfaceStyle.Hardcore, "real", "Terminal real · por comandos", "Una consola dentro de RetroEmu. Buscá juegos, jugá y ajustá el equipo escribiendo órdenes. Incluye ayuda, historial y autocompletado. Volvés con interfaz retro.")
        {
            Handling = "Órdenes escritas",
            Features = ["Autocompletado, historial y búsqueda de órdenes anteriores.", "Tareas largas en segundo plano, sin perder el prompt.", "Los editores visuales se siguen abriendo con hacer."]
        },
        new(InterfaceStyle.Amber, "lounge", "Salón · biblioteca visual", "Tarjetas de juegos con imagen, una ficha de selección y navegación al pie. Un diseño para elegir qué jugar con accesos grandes.")
        {
            Handling = "Tarjetas y navegación al pie",
            Features = ["Biblioteca visual en una grilla de tarjetas.", "Acciones grandes sobre el juego seleccionado.", "Elegí sus colores sin cambiar la organización."]
        }
    ]);

    public static bool TryParse(string? id, out InterfaceStyle style)
    {
        if (string.Equals(id, "hardcore", StringComparison.OrdinalIgnoreCase)) id = "real";
        if (id != null && CommandLine.Fold(id) is "ambar" or "amber" or "salon") id = "lounge";
        if (id != null && CommandLine.Fold(id) is "explorador" or "terminal") id = "explorer";
        var match = All.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        style = match?.Style ?? InterfaceStyle.Retro;
        return match != null;
    }
    public static InterfaceDescription Describe(InterfaceStyle style) => All.First(item => item.Style == style);
}
