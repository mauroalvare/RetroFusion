using Raylib_cs;
using rlImGui_cs;
using Retro.Update;

namespace Retro.App;

internal static class AppStartup
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Run(string[] args, string? ready, int readyIndex)
    {
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
            throw new InvalidOperationException(StartupReport.NoGraphics);
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
        int startupIndex = appArgs.IndexOf("--startup-ready");
        string? startupReady = startupIndex >= 0 && startupIndex + 1 < appArgs.Count ? appArgs[startupIndex + 1] : null;
        if (startupIndex >= 0 && startupIndex + 1 < appArgs.Count) appArgs.RemoveRange(startupIndex, 2);
        appArgs.Remove("--startup-child");
        if (ready != null) { int index = appArgs.IndexOf("--update-ready"); appArgs.RemoveRange(index, 2); }
        int testIndex = appArgs.IndexOf("--autotest");
        if (testIndex >= 0 && testIndex + 2 < appArgs.Count)
        {
            // Package tests must not discover a player's library in an ancestor folder or Documents.
            string testData = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(appArgs[testIndex + 2]))!, "autotest-data");
            Retro.Studio.StudioLibrary.UserFolder = Path.Combine(testData, "fusions");
            Retro.Ai.CreatedGames.UserFolder = Path.Combine(testData, "created");
            Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(testData, "profiles"));
        }
        var app = new RetroApp(appArgs.ToArray());
        if (ready == null && !args.Contains("--autotest")) app.OfferInterfaceOnStartup(UpdateRunner.AppVersion.ToString(3));
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
            if (startupReady != null)
            {
                using (File.Open(startupReady, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
                startupReady = null;
            }
            if (!cleanupStarted) { UpdateCleanup.Start(AppContext.BaseDirectory); cleanupStarted = true; }
        }
        app.Shutdown();
        rlImGui.Shutdown();
        Raylib.CloseAudioDevice();
        Raylib.CloseWindow();
    }
}
