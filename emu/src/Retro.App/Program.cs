using Raylib_cs;
using rlImGui_cs;
using Retro.App;

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

var app = new RetroApp(args);
while (!Raylib.WindowShouldClose() && !app.QuitRequested)
{
    app.Frame(Raylib.GetFrameTime());
}
app.Shutdown();
rlImGui.Shutdown();
Raylib.CloseAudioDevice();
Raylib.CloseWindow();
