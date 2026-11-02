using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private bool _newsOpen;
    private void OpenNews() => _newsOpen = true;

    private void DrawNews()
    {
        if (!_newsOpen || _interfacePicker.IsOpen) return;
        var room = WorkspaceLayout.Available();
        ImGui.SetNextWindowSize(System.Numerics.Vector2.Min(UiTheme.Px(760, 600), room), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(System.Numerics.Vector2.Min(UiTheme.Px(360, 260), room), room);
        if (ImGui.Begin(UiText.Label("Novedades###release-notes"), ref _newsOpen, ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse))
        {
            WorkspaceLayout.KeepOnScreen();
            UiTheme.Heading("Novedades de RetroEmu", "Las funciones nuevas y los cambios de las últimas versiones.");
            float footer = ImGui.GetFrameHeightWithSpacing() + UiTheme.Px(8);
            ImGui.BeginChild("release-notes-body", new(0, -footer));
            ImGui.PushTextWrapPos(0);
            foreach (var release in ReleaseNotes.All)
            {
                UiTheme.Section(release.Version == "current" ? UiText.Get("Esta versión · ") + typeof(ReleaseNotes).Assembly.GetName().Version?.ToString(3) : release.Version);
                foreach (string item in release.Items)
                {
                    ImGui.Bullet(); ImGui.TextWrapped(UiText.Get(item));
                }
                ImGui.Spacing();
            }
            ImGui.PopTextWrapPos(); ImGui.EndChild(); ImGui.Separator();
            if (ImGui.Button(UiText.Label("Cerrar novedades"))) _newsOpen = false;
            InterfaceControlBounds?.Invoke("Cerrar novedades", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }
        ImGui.End();
    }
}

internal sealed record ReleaseNote(string Version, IReadOnlyList<string> Items);
internal static class ReleaseNotes
{
    public static readonly IReadOnlyList<ReleaseNote> All = [
        new("current", [
            "El arranque de emergencia permite buscar e instalar una actualización aunque no abra la interfaz principal.",
            "El actualizador reemplaza los archivos de forma atómica para poder recuperarse después de un corte.",
            "La elección de interfaz se vuelve a ofrecer después de cada actualización, también si se hizo manualmente.",
            "Terminal real es el nuevo nombre de la interfaz por comandos.",
            "El idioma se elige en Preferencias; la terminal ofrece las órdenes correspondientes al idioma elegido.",
            "Los favoritos ausentes siguen visibles y recuperan su marca cuando vuelve el mismo contenido.",
            "El buscador incluye órdenes con parámetros y ejemplos; elegir una prepara la línea sin ejecutarla.",
            "Retro, Explorador, Salón y Terminal real tienen navegación y distribución propias; los colores se eligen aparte.",
            "Cada interfaz conserva su propia paleta y ocho colores RGB libres, con vista previa, Aplicar y Cancelar.",
            "Requisitos muestra qué falta para arrancar; --no-audio permite abrir sin inicializar sonido.",
            "La reparación de Visual C++ sólo se ofrece si falta una dependencia; pide permiso para descargar y para ejecutar el instalador firmado por Microsoft.",
            "El paquete Linux incluye una comprobación previa al runtime, con requisitos de glibc y bibliotecas del sistema.",
            "Los talleres distinguen errores, cancelaciones y resultados parciales; un fallo al guardar anotaciones conserva la respuesta en la sesión."
        ]),
        new("0.3.7", [
            "Consola interna con ayuda, historial, autocompletado y tareas cancelables en segundo plano.",
            "Biblioteca renovada con ficha de detalle, filtros y listas que se mantienen ágiles.",
            "Preferencias por grupos y selector de estilos con vistas previas ampliables.",
            "Sin dispositivo de audio, las partidas siguen a su velocidad normal y se puede reintentar la conexión."
        ]),
        new("0.3.6", [
            "Manuales incluidos en el paquete y ayuda para jugar, crear, investigar y modificar.",
            "Investigación y creación con recuperación de trabajos y datos comprobados por edición."
        ])
    ];
}
