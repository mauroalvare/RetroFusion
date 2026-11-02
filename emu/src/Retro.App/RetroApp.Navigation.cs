using System.Numerics;
using ImGuiNET;

namespace Retro.App;

public sealed partial class RetroApp
{
    private enum HubPage { None, Create, Tools, Help }
    private HubPage _hub;
    private bool InterfaceWorkspace => _terminalOpen || _home.IsOpen || _hub!=HubPage.None || _creator.IsOpen || _studio.IsOpen || _code.IsOpen || _stateWindowOpen;
    private bool HasGame => _host.LoadedMedia!=null || _host.Fusion!=null;

    private void OpenHub(HubPage page)
    { CloseEditors(); _home.Close(); _debugMode=false; _hub=page; }
    private void OpenPlay()
    { CloseEditors(); _home.Close(); _debugMode=false; }
    private void OpenAnalyzer()
    { CloseEditors(); _home.Close(); _debugMode=false; _code.Reopen(); }
    private void OpenDebugger()
    { CloseEditors(); _home.Close(); _debugMode=true; }

    private void DrawNavigation()
    {
        WorkspaceLayout.MenuHeight=_menuHeight;
        if (UiTheme.CompactHeight) { WorkspaceLayout.NavigationHeight = 0; return; }
        float height;
        using(UiTheme.Label()) height=ImGui.GetFrameHeight()+UiTheme.Px(20);
        WorkspaceLayout.NavigationHeight=height;
        var viewport=ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos+new Vector2(0,_menuHeight));
        ImGui.SetNextWindowSize(new(viewport.Size.X,height));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,UiTheme.Px(18,10));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding,0);
        ImGui.Begin("##app-navigation",ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoDocking|ImGuiWindowFlags.NoSavedSettings);
        using (UiTheme.Label())
        {
            string[] labels=["Biblioteca","Jugar","Crear","Analizar","Herramientas","Ayuda"];
            int selected=_home.IsOpen?0:_hub==HubPage.Create || _creator.IsOpen || _studio.IsOpen?2:_code.IsOpen || _debugMode?3:_hub==HubPage.Tools || _stateWindowOpen?4:_hub==HubPage.Help?5:1;
            bool resume=InterfaceWorkspace&&HasGame;
            const string resumeLabel="VOLVER A LA PARTIDA";
            float resumeWidth=resume?ImGui.CalcTextSize(resumeLabel).X+ImGui.GetStyle().FramePadding.X*2+ImGui.GetStyle().ItemSpacing.X:0;
            float required=labels.Select((label,i)=>$"{i+1:00} {label.ToUpperInvariant()}").Sum(label=>ImGui.CalcTextSize(label).X+ImGui.GetStyle().FramePadding.X*2+ImGui.GetStyle().ItemSpacing.X)+UiTheme.Px(285)+resumeWidth;
            if (ImGui.GetContentRegionAvail().X<required)
            {
                ImGui.SetNextItemWidth(Math.Max(UiTheme.Px(100),ImGui.GetContentRegionAvail().X-UiTheme.Px(106)-resumeWidth));
                if (ImGui.Combo("##workspace",ref selected,labels,labels.Length)) Navigate(selected);
                ImGui.SameLine();
                if (resume) { if (UiTheme.PrimaryButton(resumeLabel)) OpenPlay(); ImGui.SameLine(); }
            }
            else
            {
                RetroArt.Brand(); ImGui.SameLine(0,UiTheme.Px(20));
                for (int i=0;i<labels.Length;i++)
                {
                    if (i>0) ImGui.SameLine();
                    ImGui.PushID(i);
                    if (RetroArt.ModeButton($"{i+1:00} {labels[i].ToUpperInvariant()}",selected==i)) Navigate(i);
                    ImGui.PopID();
                }
                ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(),ImGui.GetWindowWidth()-UiTheme.Px(106)-resumeWidth));
                if (resume)
                {
                    if (UiTheme.PrimaryButton(resumeLabel)) OpenPlay();
                    InterfaceControlBounds?.Invoke("Volver a la partida",ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
                    if (ImGui.IsItemHovered()) UiTheme.Tooltip((_host.Fusion?.Title??Path.GetFileNameWithoutExtension(_host.LoadedMedia)??"Partida")+(_host.Paused?" · en pausa":""));
                    ImGui.SameLine();
                }
            }
            if (ImGui.Button("Buscar…",new(UiTheme.Px(88),0))) _commands.Open();
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Todas las acciones y sus atajos · "+ToolShortcut("P"));
        }
        ImGui.End(); ImGui.PopStyleVar(2);

        void Navigate(int page)
        {
            switch(page)
            {
                case 0: CloseEditors(); _home.Open(); break;
                case 1: OpenPlay(); break;
                case 2: OpenHub(HubPage.Create); break;
                case 3: OpenAnalyzer(); break;
                case 4: OpenHub(HubPage.Tools); break;
                case 5: OpenHub(HubPage.Help); break;
            }
        }
    }

    private void DrawHub()
    {
        if (_hub==HubPage.None) return;
        WorkspaceLayout.NextEditor();
        ImGui.Begin("##workspace-hub",WorkspaceLayout.EditorFlags);
        switch(_hub)
        {
            case HubPage.Create: DrawCreateHub(); break;
            case HubPage.Tools: DrawToolsHub(); break;
            case HubPage.Help: DrawHelpHub(); break;
        }
        ImGui.End();
    }

    private void DrawCreateHub()
    {
        if (!UiTheme.CompactHeight) UiTheme.Eyebrow("TU TALLER");
        UiTheme.Heading("Banco de creación","Plantillas, originales y proyectos en curso.");
        ToolRows("create-paths",[
            ("Desde cero","Un juego original para una máquina de 8 bits. Empezá con una idea o una plantilla editable, también sin IA.","new"),
            ("Fusionar originales","Elegí un mundo base e incorporá reglas de otros juegos. La investigación comprueba los datos antes de construir.","fusion"),
            ("Crear con originales","Diseñá un juego nuevo a partir de las mecánicas y recursos disponibles en tus originales.","inspired")]);
        UiTheme.Section("Tus proyectos","Las creaciones terminadas están en Biblioteca. Los borradores y las investigaciones se retoman dentro de cada taller.");
        if (ImGui.Button("Retomar una fusión")) { OpenFusionStudio(); _studio.ShowSavedWork(); }
        UiTheme.SameLineIfFits("Mis proyectos desde cero");
        if (ImGui.Button("Mis proyectos desde cero")) { OpenCreator(); _creator.ShowSavedProjects(); }
        UiTheme.Section("Prepará tu método de trabajo");
        ToolRows("creation-method",[("IA y modelos","Conectá una CLI, elegí el modelo y revisá el método de trabajo. También hay copiar y pegar y carpeta de agente.","ai")]);
    }

    private void DrawToolsHub()
    {
        if (!UiTheme.CompactHeight) UiTheme.Eyebrow("MESA DE HERRAMIENTAS");
        UiTheme.Heading("Instrumental","Partidas, memoria, código y ajustes del equipo.");
        UiTheme.BeginCard("current-context");
        ImGui.TextWrapped(HasGame?"En uso: "+(_host.LoadedMedia!=null?Path.GetFileNameWithoutExtension(_host.LoadedMedia):"Creación en ejecución"):"Abrí un original para trabajar sobre una partida. La configuración está disponible siempre.");
        if (ImGui.Button(HasGame?"Volver a jugar":"Abrir un juego…")) { if (HasGame) OpenPlay(); else _browser.Open(); }
        UiTheme.EndCard();
        UiTheme.Section("Investigar y modificar");
        ToolRows("analysis-tools",[("Investigación automática","El laboratorio juega secuencias y contrasta variables en una máquina privada; elegí el objetivo y el presupuesto.","auto-analysis"),
            ("Cambios y análisis","Buscá variables, probá propuestas de la IA y revisá el historial de cambios.","modify"),
            ("Código y rutinas","Recorré el desensamblado, exportá código, anotá funciones y probá parches.","code"),
            ("Depurador","Seguí registros, memoria y ejecución. Incluye breakpoints y avance por instrucción o cuadro.","debugger")]);
        UiTheme.Section("Partida y capturas");
        ToolRows("play-tools",[("Partidas guardadas","Ranuras separadas por juego y edición, con guardado y recuperación entre sesiones.","slots"),
            ("Rebobinado y entradas","Volvé a un momento anterior o grabá y reproducí las entradas de esta sesión.","replay-tools"),
            ("Capturar pantalla","Guardá la imagen del juego a su resolución original.","capture")]);
        if (ImGui.CollapsingHeader("Rebobinado y grabación de entradas",_showReplayTools?ImGuiTreeNodeFlags.DefaultOpen:ImGuiTreeNodeFlags.None)) DrawReplayTools();
        UiTheme.Section("Configuración");
        ToolRows("settings-tools",[("Pantalla, controles y accesibilidad","Tamaño del texto, alto contraste, joystick, rendimiento y pantalla completa.","preferences"),
            ("Sonido","Volumen, silencio y ajustes de salida de audio.","sound"),
            ("Conexión de IA","Proveedor, modelo, sesión y opciones de trabajo.","ai"),
            ("Actualizaciones","Consultá la versión instalada y las entregas disponibles.","updates")]);
    }

    private bool _showReplayTools, _diagnosticCopied;
    private void DrawReplayTools()
    {
        ImGui.BeginDisabled(!_host.CanUseStates);
        bool rewind=_host.RewindEnabled;
        if (ImGui.Checkbox("Conservar historial (hasta 64 MB)",ref rewind)) _host.SetRewindEnabled(rewind);
        ImGui.TextWrapped($"Historial: {_host.RewindPoints} puntos · {_host.RewindBytes/(1024*1024.0):F1} MB");
        ImGui.BeginDisabled(_host.RewindPoints==0 || _host.IsRecordingInputs || _host.IsReplayingInputs);
        if (ImGui.Button("Retroceder cinco segundos")) TryStateAction(()=>_host.Rewind());
        ImGui.EndDisabled(); UiTheme.SameLineIfFits("Vaciar historial");
        if (ImGui.Button("Vaciar historial")) _host.ClearRewind();
        ImGui.BeginDisabled(!_host.CanRecordInputs && !_host.IsRecordingInputs);
        if (ImGui.Button(_host.IsRecordingInputs?"Terminar grabación":"Grabar entradas desde aquí"))
        { if (_host.IsRecordingInputs) _host.StopInputRecording(); else TryStateAction(()=>_host.StartInputRecording()); }
        ImGui.EndDisabled(); UiTheme.SameLineIfFits("Reproducir última grabación");
        ImGui.BeginDisabled(!_host.CanReplayInputs && !_host.IsReplayingInputs);
        if (ImGui.Button(_host.IsReplayingInputs?"Detener reproducción":"Reproducir última grabación"))
        { if (_host.IsReplayingInputs) _host.StopInputReplay(); else TryStateAction(()=>_host.StartInputReplay()); }
        ImGui.EndDisabled(); ImGui.TextWrapped($"Grabación: {_host.RecordedFrames} cuadros · sólo esta sesión.");
        ImGui.EndDisabled();
        if (!_host.CanUseStates) UiTheme.Hint("Disponible al abrir un juego original.");
    }

    private void DrawHelpHub()
    {
        if (!UiTheme.CompactHeight) UiTheme.Eyebrow("AYUDA Y CONTROLES");
        UiTheme.Heading("Manual de operación","Controles, atajos y ayuda para los trabajos del taller.");
        UiTheme.BeginCard("console-help");
        UiTheme.Section("Tres formas de manejarlo");
        foreach (var style in InterfaceStyles.All) ImGui.BulletText(style.ShortName+": "+style.Handling.ToLowerInvariant()+".");
        if (ImGui.Button("Ver estilos y vistas previas…")) OpenInterfacePicker();
        UiTheme.SameLineIfFits("Copiar diagnóstico");
        if (ImGui.Button("Copiar diagnóstico")) { ImGui.SetClipboardText(DiagnosticReport()); _diagnosticCopied=true; }
        if (ImGui.IsItemHovered()) UiTheme.Tooltip("Lleva al portapapeles un resumen técnico sin títulos, rutas personales ni credenciales.");
        if (_diagnosticCopied) UiTheme.Hint("Diagnóstico copiado. Revisalo antes de compartirlo.");
        UiTheme.EndCard();
        UiTheme.BeginCard("play-help");
        UiTheme.Section("Abrir y jugar");
        ImGui.TextWrapped("En Biblioteca elegí un sistema, buscá el archivo y pulsá Jugar. También podés abrir un archivo o arrastrarlo a la ventana. Jugar vuelve a la máquina actual.");
        UiTheme.Section("Controles de la máquina actual");
        ImGui.PushTextWrapPos(0); DrawControlsHelp(); ImGui.PopTextWrapPos();
        UiTheme.EndCard();
        UiTheme.BeginCard("research-help");
        UiTheme.Section("Si una creación necesita más datos");
        ImGui.TextWrapped("El taller guarda la idea y cada original investigado. En Trabajos guardados podés revisar los avances, jugar los originales y abrir el analizador. Las guías describen las pruebas pendientes; continuar vuelve a usar el proveedor que elijas.");
        UiTheme.Hint("Identificar un archivo, arrancarlo, comprobar sus controles y tener datos listos para fusionar son pasos distintos.");
        UiTheme.EndCard();
        DrawInputCheck();
        if (ImGui.CollapsingHeader("Atajos y navegación",ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Tab y flechas recorren la interfaz; Enter o Espacio activan controles y Esc vuelve. Select + Start abre la biblioteca desde un mando. En Jugar, el teclado y el mando controlan la máquina.");
            DrawShortcutsHelp();
        }
        if (ImGui.CollapsingHeader("Máquinas y formatos"))
            foreach (var machine in Retro.Machines.MachineCatalog.All)
            { ImGui.TextWrapped(machine.Name+" · "+machine.Cpu+(machine.Status==Retro.Machines.MachineStatus.Available?" · disponible":" · en desarrollo")); UiTheme.Hint(machine.Needs); }
    }

    private void ToolRows(string id,(string Title,string Detail,string Command)[] items)
    {
        bool compact=ImGui.GetContentRegionAvail().X<UiTheme.Px(560);
        if (!compact && !ImGui.BeginTable(id,3,ImGuiTableFlags.SizingStretchProp)) return;
        if (!compact)
        {
            ImGui.TableSetupColumn("##instrument",ImGuiTableColumnFlags.WidthFixed,UiTheme.Px(58));
            ImGui.TableSetupColumn("##description",ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##action",ImGuiTableColumnFlags.WidthFixed,UiTheme.Px(98));
        }
        foreach(var item in items)
        {
            ImGui.PushID(item.Command);
            if (!compact)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                RetroArt.Tool(ImGui.GetWindowDrawList(),item.Command,ImGui.GetCursorScreenPos(),UiTheme.Px(44));
                ImGui.Dummy(UiTheme.Px(44,48)); ImGui.TableNextColumn();
            }
            var command=Commands.Single(c=>c.Id==item.Command);
            if (compact && UiTheme.CompactHeight)
            {
                ImGui.BeginDisabled(!command.Enabled);
                if (UiTheme.PrimaryButton(item.Title,new(-1,0))) command.Execute();
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiTheme.Tooltip(command.Enabled?item.Detail:command.Unavailable);
                InterfaceControlBounds?.Invoke("Herramienta:"+item.Command,ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
                ImGui.PopID();
                continue;
            }
            using(UiTheme.Display()) { ImGui.PushTextWrapPos(0); ImGui.TextUnformatted(item.Title); ImGui.PopTextWrapPos(); }
            if(command.Shortcut.Length>0)
            {
                using(UiTheme.FixedWidth()) { UiTheme.SameLineIfFits(command.Shortcut); ImGui.TextDisabled(command.Shortcut); }
            }
            UiTheme.Hint(item.Detail);
            if (!command.Enabled) UiTheme.Hint(command.Unavailable);
            if (!compact) ImGui.TableNextColumn();
            ImGui.BeginDisabled(!command.Enabled);
            if (UiTheme.PrimaryButton("Abrir >")) command.Execute();
            ImGui.EndDisabled();
            if(compact) { ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing(); }
            ImGui.PopID();
        }
        if(!compact) ImGui.EndTable();
    }
}
