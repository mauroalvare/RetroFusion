using ImGuiNET;
using Raylib_cs;

namespace Retro.App;

public sealed partial class RetroApp
{
    private IReadOnlyList<AppOption>? _optionCatalog;
    private IReadOnlyList<AppOption> Options => _optionCatalog ??= BuildOptions();
    private IReadOnlyList<AppOption> BuildOptions() => [
        AppOption.Choice("idioma", "Idioma / Language", "Interfaz", "Idioma de todos los menús, mensajes y órdenes de la terminal.",
            () => _prefs.Language, SetLanguage, new("es", "Español"), new("en", "English")) with { Default = "es" },
        AppOption.Choice("interfaz", "Estilo de interfaz", "Interfaz", "Retro es el diseño principal. Terminal verde conserva los botones; Terminal real se maneja con comandos.",
            () => InterfaceStyles.Describe(_prefs.Interface).Id, id => { InterfaceStyles.TryParse(id, out var style); SetInterface(style); },
            InterfaceStyles.All.Select(item => new OptionChoice(item.Id, item.Name)).ToArray()) with { Default = "retro" },
        AppOption.Choice("texto", "Tamaño del texto", "Lectura", "El tamaño se aplica a todos los estilos.",
            () => _prefs.TextPercent.ToString(), value => SetTextSize(int.Parse(value)), AppPreferences.TextSizes.Select(size => new OptionChoice(size.ToString(), size + " %")).ToArray()) with { Default = "100" },
        AppOption.Toggle("contraste", "Alto contraste", "Lectura", "Texto blanco sobre negro, bordes visibles y foco del teclado en amarillo.", () => _prefs.HighContrast, SetHighContrast) with { Default = "off" },
        AppOption.Toggle("consulta-fichas", "Completar fichas en línea", "Identificación de juegos", "Consulta Wikidata (datos CC0) en segundo plano por título y plataforma. Envía el nombre limpio; nunca el archivo ni su ruta. Tus correcciones tienen prioridad.",
            () => _prefs.OnlineGameInfo, value => { _prefs.OnlineGameInfo = value; _prefs.Save(); }) with { Default = "off" },
        AppOption.Toggle("pantalla-completa", "Pantalla completa (F11)", "Pantalla", "Alterna entre ventana y pantalla completa.", () => Raylib.IsWindowFullscreen(), value => { if (value != Raylib.IsWindowFullscreen()) ToggleFullscreen(); }) with { Default = "off" },
        AppOption.Toggle("escala-entera", "Escala entera (píxeles nítidos)", "Pantalla", "Evita estirar píxeles de la pantalla emulada.", () => _prefs.IntegerScale, value => { _prefs.IntegerScale = value; _prefs.Save(); }) with { Default = "on" },
        AppOption.Toggle("fps", "Mostrar cuadros por segundo", "Pantalla", "Muestra el ritmo de la ventana.", () => _prefs.ShowFps, value => { _prefs.ShowFps = value; _prefs.Save(); }) with { Default = "off" },
        AppOption.Choice("joystick-zx", "Joystick del Spectrum", "Controles", "Las flechas y el mando manejan el joystick elegido.", () => _prefs.SpectrumJoystick.ToString(), value => SetSpectrumJoystick(Enum.Parse<JoystickMode>(value)),
            Enum.GetValues<JoystickMode>().Select((mode, i) => new OptionChoice(mode.ToString(), JoystickNames[i])).ToArray()) with { Default = nameof(JoystickMode.Kempston) },
        AppOption.Choice("puerto-c64", "Joystick del C64", "Controles", "Puerto 2 en la mayoría de los juegos.", () => _prefs.C64Port.ToString(), value => SetC64Port(int.Parse(value)), new("1", C64Ports[0]), new("2", C64Ports[1])) with { Default = "2" },
        AppOption.Toggle("load-cintas", "Escribir LOAD \"\" al abrir cintas", "Cintas del Spectrum", "Carga automática de cintas compatibles.", () => _prefs.TypeLoad, value => { _prefs.TypeLoad = value; _prefs.Save(); }) with { Default = "on" },
        AppOption.Toggle("turbo-carga", "Acelerar durante la carga", "Cintas del Spectrum", "Vuelve a la velocidad normal al terminar la carga.", () => _prefs.TurboWhileLoading, SetTurboWhileLoading) with { Default = "on" },
        AppOption.Toggle("ahorro", "Ahorrar energía sin partida", "Rendimiento", UiText.Format($"La interfaz usa hasta {FramePacing.Interface} cuadros por segundo; sin foco, {FramePacing.Background}. La partida conserva su frecuencia."), () => _prefs.PowerSaving, value => { _prefs.PowerSaving = value; _prefs.Save(); }) with { Default = "on" },
        AppOption.Toggle("pausa-fondo", "Pausar al cambiar de ventana", "Rendimiento", "Al volver, continúa solo si la pausa fue automática.", () => _prefs.PauseInBackground, value => { _prefs.PauseInBackground = value; _prefs.Save(); }) with { Default = "off" },
        AppOption.Choice("consola-cursor", "Cursor de la consola", "Consola", "Forma del cursor en Terminal real.",
            () => AppPreferences.CursorNames.First(pair => pair.Value == _prefs.ConsoleCursor).Key, value => { _prefs.ConsoleCursor = AppPreferences.CursorNames[value]; _prefs.Save(); },
            new("block", "Bloque"), new("barra", "Barra"), new("subrayado", "Subrayado")) with { Default = "block" },
        AppOption.Toggle("consola-parpadeo", "Parpadeo del cursor", "Consola", "Desactivalo si el movimiento te molesta; el cursor queda fijo.", () => _prefs.ConsoleBlink, value => { _prefs.ConsoleBlink = value; _prefs.Save(); }) with { Default = "on" },
        AppOption.Toggle("consola-crt", "Líneas de monitor CRT", "Consola", "Dibuja líneas tenues sobre la consola. Reduce un poco la lectura.", () => _prefs.ConsoleScanlines, value => { _prefs.ConsoleScanlines = value; _prefs.Save(); }) with { Default = "off" },
        AppOption.Toggle("consola-historial", "Recordar las órdenes", "Consola", "Guarda las órdenes en este equipo para la próxima sesión. El diagnóstico nunca las incluye.",
            () => _prefs.ConsoleHistory, value => { _prefs.ConsoleHistory = value; _prefs.Save(); if (_terminal != null) { _terminal.History.Persist = value; _terminal.History.Save(); } }) with { Default = "on" },
        AppOption.Number("volumen", "Volumen", "Sonido", "Porcentaje de salida, de 0 a 100.", () => _sound.VolumePercent, value => { _sound.VolumePercent = value; _sound.Save(); }, 0, 100) with { Default = "100" },
        AppOption.Toggle("silencio", "Silenciar", "Sonido", "Conserva el nivel de volumen elegido.", () => _sound.Muted, value => { _sound.Muted = value; _sound.Save(); }) with { Default = "off" },
        AppOption.Toggle("silencio-fondo", "Silenciar en segundo plano", "Sonido", "Silencia cuando otra ventana tiene el foco.", () => _sound.MuteInBackground, value => { _sound.MuteInBackground = value; _sound.Save(); }) with { Default = "off" },
        AppOption.Choice("buffer-audio", "Margen de audio", "Sonido", "Más cuadros toleran mejor las variaciones de tiempo, con mayor demora.", () => _sound.BufferFrames.ToString(), value => { _sound.BufferFrames = int.Parse(value); _sound.Save(); },
            new("1", "Corto · 1 cuadro"), new("2", "Equilibrado · 2 cuadros"), new("4", "Amplio · 4 cuadros")) with { Default = "2" }
    ];

    private void SetLanguage(string language)
    {
        _prefs.Language = language; UiText.Language = language; _prefs.Save();
        _commandCatalog = null; _optionCatalog = null; _terminalCatalog = null;
    }

    private void DrawSharedOptions(string? onlyGroup = null, string search = "")
    {
        string wanted = CommandLine.Fold(search.Trim());
        foreach (var group in Options.Where(option => (onlyGroup == null || option.Group == onlyGroup)
            && (wanted.Length == 0 || CommandLine.Fold(option.Id + " " + option.Title + " " + option.Group + " " + option.Description).Contains(wanted))).GroupBy(option => option.Group))
        {
            UiTheme.Section(group.Key);
            foreach (var option in group)
            {
                ImGui.PushID(option.Id);
                string value = option.Read();
                switch (option.Kind)
                {
                    case AppOptionKind.Toggle:
                        bool enabled = value == "on";
                        if (ImGui.Checkbox(option.Title, ref enabled)) option.Write(enabled ? "on" : "off");
                        break;
                    case AppOptionKind.Choice:
                        ImGui.TextUnformatted(option.Title); ImGui.SetNextItemWidth(-1);
                        if (ImGui.BeginCombo("##value", UiText.Get(option.Choices!.FirstOrDefault(choice => choice.Value == value)?.Label ?? value)))
                        {
                            foreach (var choice in option.Choices!)
                                if (ImGui.Selectable(UiText.Label(choice.Label), choice.Value == value)) option.Write(choice.Value);
                            ImGui.EndCombo();
                        }
                        break;
                    case AppOptionKind.Number:
                        int number = int.Parse(value); ImGui.TextUnformatted(option.Title); ImGui.SetNextItemWidth(-1);
                        if (ImGui.SliderInt("##value", ref number, option.Min, option.Max)) option.Write(number.ToString());
                        break;
                }
                InterfaceControlBounds?.Invoke("Opción:" + option.Id, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                UiTheme.Hint(option.Description);
                if (option.Modified)
                {
                    if (ImGui.SmallButton(UiText.Label("Volver al valor inicial"))) option.Write(option.Default!);
                    if (ImGui.IsItemHovered()) UiTheme.Tooltip(UiText.Get("Valor inicial: ") + (option.Choices?.FirstOrDefault(choice => choice.Value == option.Default)?.Label ?? option.Default));
                }
                if (option.Id == "interfaz" && ImGui.Button(UiText.Label("Ver estilos y vistas previas…"))) OpenInterfacePicker();
                if (option.Id == "consulta-fichas")
                {
                    UiTheme.Hint(_gameMetadata.Status);
                    if (ImGui.SmallButton(UiText.Label("Reintentar catálogo en línea"))) _gameMetadata.Retry();
                    if (_gameMetadata.Problem is { } problem) UiTheme.Hint(problem);
                }
                ImGui.PopID();
            }
        }
    }
}
