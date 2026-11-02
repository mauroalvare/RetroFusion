using Raylib_cs;
using rlImGui_cs;
using Retro.App;
using Retro.Update;

if (args.Length == 2 && args[0] == "--apply-update")
{
    Environment.ExitCode = await UpdateRunner.RunHelperAsync(args[1]);
    return;
}
if (args.Length == 4 && args[0] == "--recover-update" &&
    int.TryParse(args[2], out int recoveryParent) && long.TryParse(args[3], out long recoveryStarted))
{
    Environment.ExitCode = await UpdateRunner.RecoverAsync(args[1], recoveryParent, recoveryStarted);
    return;
}
string? ready = null;
int readyIndex = Array.IndexOf(args, "--update-ready");
if (readyIndex >= 0 && readyIndex + 1 < args.Length) ready = args[readyIndex + 1];
if (ready == null && UpdateRunner.RecoverBeforeStart()) return;

// RetroEmu: multi-system emulator with a CPU debugger.
// Raylib provides the window, audio and input on Windows, Linux and macOS;
// Dear ImGui draws the menus and the debugger panels.

if (!args.Contains("--autotest"))
    AppDomain.CurrentDomain.UnhandledException += (_, failure) =>
        StartupReport.Fatal("RetroEmu se cerró por un error inesperado. Tus juegos y partidas guardadas no se modificaron.", failure.ExceptionObject as Exception);

Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
var flags = ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint | ConfigFlags.HighDpiWindow;
if (args.Contains("--hidden"))
    flags |= ConfigFlags.HiddenWindow;   // automatic tests: no window pops up on the user's screen
Raylib.SetConfigFlags(flags);
Raylib.InitWindow(1280, 840, "RetroEmu");
if (!Raylib.IsWindowReady())
{
    // Every later call needs the graphics context: without one the program would just vanish.
    StartupReport.Fatal(StartupReport.NoGraphics);
    Environment.ExitCode = 2;
    return;
}
Raylib.SetWindowMinSize(640, 480);
StartupReport.FitWindowToMonitor();
// Deterministic layout checks use an isolated hidden instance, never the user's desktop size.
int sizeIndex = Array.IndexOf(args, "--ui-size");
if (args.Contains("--autotest") && args.Contains("--hidden") && sizeIndex >= 0 && sizeIndex + 1 < args.Length)
{
    string[] dimensions = args[sizeIndex + 1].Split('x');
    if (dimensions.Length == 2 && int.TryParse(dimensions[0], out int width) && int.TryParse(dimensions[1], out int height))
        Raylib.SetWindowSize(Math.Clamp(width, 640, 3840), Math.Clamp(height, 480, 2160));
}
Raylib.SetExitKey(KeyboardKey.Null);
// A computer without sound output carries on: the app detects it and runs in silence.
Raylib.InitAudioDevice();
rlImGui.Setup(true, true);
if (args.Contains("--autotest"))
    unsafe { ImGuiNET.ImGui.GetIO().NativePtr->IniFilename = null; }

var appArgs = args.ToList();
if (readyIndex >= 0 && readyIndex + 1 < args.Length) appArgs.RemoveRange(readyIndex, 2);
var app = new RetroApp(appArgs.ToArray());
bool cleanupStarted = false;
bool closing = false;
while (!app.QuitRequested)
{
    // A close request lets the app ask first when work is in progress. Acting only when it
    // starts keeps the dialog from reappearing if the request were still set next frame.
    bool close = Raylib.WindowShouldClose();
    if (close && !closing) app.RequestQuit();
    closing = close;
    if (app.QuitRequested) break;
    app.Frame(Raylib.GetFrameTime());
    if (ready != null)
    {
        string installedVersion = UpdateRunner.ConfirmStartup(ready);
        app.OfferInterfaceAfterUpdate(installedVersion);
        ready = null;
    }
    if (!cleanupStarted) { UpdateCleanup.Start(AppContext.BaseDirectory); cleanupStarted = true; }
}
app.Shutdown();
rlImGui.Shutdown();
Raylib.CloseAudioDevice();
Raylib.CloseWindow();
