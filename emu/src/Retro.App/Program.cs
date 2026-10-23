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

Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
var flags = ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint | ConfigFlags.HighDpiWindow;
if (args.Contains("--hidden"))
    flags |= ConfigFlags.HiddenWindow;   // automatic tests: no window pops up on the user's screen
Raylib.SetConfigFlags(flags);
Raylib.InitWindow(1280, 840, "RetroEmu");
Raylib.SetWindowMinSize(640, 480);
Raylib.SetExitKey(KeyboardKey.Null);
Raylib.InitAudioDevice();
rlImGui.Setup(true, true);

var appArgs = args.ToList();
if (readyIndex >= 0 && readyIndex + 1 < args.Length) appArgs.RemoveRange(readyIndex, 2);
var app = new RetroApp(appArgs.ToArray());
bool cleanupStarted = false;
while (!Raylib.WindowShouldClose() && !app.QuitRequested)
{
    app.Frame(Raylib.GetFrameTime());
    if (ready != null) { UpdateRunner.Acknowledge(ready); ready = null; }
    if (!cleanupStarted) { UpdateCleanup.Start(AppContext.BaseDirectory); cleanupStarted = true; }
}
app.Shutdown();
rlImGui.Shutdown();
Raylib.CloseAudioDevice();
Raylib.CloseWindow();
